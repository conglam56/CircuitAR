using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARSubsystems;

/// <summary>Pure camera-coordinate checks; no device/session or scene is started.</summary>
internal static class ARCameraCoordinateRegressionChecks
{
    public static void Run(List<string> results)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));
        Require(CustomARFrameBridge.GetInferenceDimensions(1920, 1080) == new Vector2Int(640, 360) &&
                CustomARFrameBridge.GetInferenceDimensions(480, 640) == new Vector2Int(480, 640) &&
                CustomARFrameBridge.GetInferenceDimensions(320, 240) == new Vector2Int(320, 240) &&
                CustomARFrameBridge.GetInferenceDimensions(0, 480) == Vector2Int.zero,
            "AR inference resizing must bound CPU work, preserve aspect, avoid upscaling and reject invalid dimensions.");
        results.Add("PASS: AR CPU inference size is bounded without cropping, stretching or unnecessary upscaling");
        Matrix4x4 portrait = Matrix4x4.identity;
        portrait.m00 = 0f;
        portrait.m01 = -1f;
        portrait.m10 = -1f;
        portrait.m11 = 0f;
        portrait.m20 = 1f;
        portrait.m21 = 1f;
        Require(CustomARFrameBridge.TryMapImagePointToViewport(new Vector2(0.2f, 0.7f), portrait,
                out Vector2 uncropped) && Near(uncropped, new Vector2(0.3f, 0.8f)),
            "Uncropped portrait must preserve the calibrated (1-imageY, 1-imageX) mapping without a second rotation.");
        CheckRoundTrips(portrait);
        results.Add("PASS: uncropped AR portrait coordinates preserve the original finger direction without double rotation");

        Matrix4x4 croppedPortrait = portrait;
        croppedPortrait.m01 = -0.6f;
        croppedPortrait.m21 = 0.8f;
        Require(CustomARFrameBridge.TryMapImagePointToViewport(new Vector2(0.2f, 0.68f), croppedPortrait,
                out Vector2 cropped) && Near(cropped, new Vector2(0.2f, 0.8f)),
            "Portrait AR crop must be inverted, rather than stretching the whole CPU image across the screen.");
        Require(!Near(cropped, new Vector2(0.32f, 0.8f)),
            "The crop fixture must actually distinguish the new mapping from the old fixed swap/flip.");
        CheckRoundTrips(croppedPortrait);
        Require(CustomARFrameBridge.TryMapImagePointToViewport(new Vector2(0.2f, 0.95f), croppedPortrait,
                out Vector2 croppedOut) && croppedOut.x < 0f,
            "A landmark outside the visible crop must remain outside, not clamp onto a false edge placement point.");
        results.Add("PASS: portrait AR crop and offscreen landmarks use the native camera display transform");

        Matrix4x4 landscape = Matrix4x4.identity;
        landscape.m00 = 0.75f;
        landscape.m11 = -1f;
        landscape.m20 = 0.125f;
        landscape.m21 = 1f;
        Require(CustomARFrameBridge.TryMapImagePointToViewport(new Vector2(0.275f, 0.3f), landscape,
                out Vector2 landscapePoint) && Near(landscapePoint, new Vector2(0.2f, 0.7f)),
            "Landscape must invert its own display matrix instead of inheriting portrait axis flags.");
        CheckRoundTrips(landscape);
        results.Add("PASS: landscape AR mapping respects camera rotation, crop and vertical origin");

        Matrix4x4 singular = Matrix4x4.identity;
        singular.m00 = singular.m01 = singular.m10 = singular.m11 = 1f;
        Matrix4x4 nan = portrait;
        nan.m20 = float.NaN;
        Matrix4x4 infinite = portrait;
        infinite.m11 = float.PositiveInfinity;
        foreach (Matrix4x4 invalid in new[] { Matrix4x4.zero, singular, nan, infinite })
            Require(!CustomARFrameBridge.TryMapImagePointToViewport(new Vector2(0.2f, 0.7f), invalid,
                    out Vector2 fallback) && Near(fallback, new Vector2(0.3f, 0.8f)),
                "An absent, singular or non-finite matrix must return the finite uncropped portrait fallback.");
        Require(!CustomARFrameBridge.TryMapImagePointToViewport(new Vector2(float.NaN, 0.5f), portrait,
                out Vector2 invalidPoint) && Near(invalidPoint, new Vector2(0.5f, 0.5f)),
            "Invalid source landmarks must not propagate NaN into screen rays.");
        results.Add("PASS: missing and invalid display matrices preserve a finite, explicit startup fallback");

        var bridge = new CustomARFrameBridge(null);
        Set(bridge, "_displayMatrix", croppedPortrait);
        Set(bridge, "_hasDisplayMatrix", true);
        Vector2 indexViewport = new Vector2(0.2f, 0.8f);
        Vector2 thumbViewport = new Vector2(0.65f, 0.25f);
        Require(bridge.TryMapImagePointsToViewport(ToImage(indexViewport, croppedPortrait),
                ToImage(thumbViewport, croppedPortrait), out Vector2 mappedIndex, out Vector2 mappedThumb) &&
                Near(mappedIndex, indexViewport) && Near(mappedThumb, thumbViewport),
            "Index and thumb must use the same cached display transform and viewport origin.");
        bridge.Pause();
        Require(!bridge.TryMapImagePointsToViewport(new Vector2(0.2f, 0.7f), new Vector2(0.6f, 0.1f),
                out Vector2 pausedIndex, out Vector2 pausedThumb) &&
                Near(pausedIndex, new Vector2(0.3f, 0.8f)) && Near(pausedThumb, new Vector2(0.9f, 0.4f)),
            "Pausing must discard the previous session's crop matrix for both landmarks.");
        results.Add("PASS: index and thumb share one camera-frame mapping and stale session transforms are cleared");
        CheckDepthProbeHandMask(results, croppedPortrait);
        CheckAsyncCameraConversion(results);
        CheckHandResultDispatch(results);
    }

    private sealed class AsyncCameraApi : XRCpuImage.Api, IDisposable
    {
        public XRCpuImage.AsyncConversionStatus State = XRCpuImage.AsyncConversionStatus.Processing;
        public int DisposedCount;
        private int requests;
        private IntPtr buffer = Marshal.AllocHGlobal(16);
        public AsyncCameraApi() { for (int i = 0; i < 16; i++) Marshal.WriteByte(buffer, i, 42); }
        public override int ConvertAsync(int nativeHandle, XRCpuImage.ConversionParams conversionParams) => ++requests;
        public override XRCpuImage.AsyncConversionStatus GetAsyncRequestStatus(int requestId) => State;
        public override bool TryGetAsyncRequestData(int requestId, out IntPtr pointer, out int length)
        { pointer = buffer; length = 16; return true; }
        public override void DisposeAsyncRequest(int requestId)
        { DisposedCount++; for (int i = 0; i < 16; i++) Marshal.WriteByte(buffer, i, 0); }
        public void Dispose() { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
    }

    private static void CheckAsyncCameraConversion(List<string> results)
    {
        using var api = new AsyncCameraApi();
        var bridge = new CustomARFrameBridge(null);
        var constructor = typeof(XRCpuImage.AsyncConversion).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(XRCpuImage.Api), typeof(int), typeof(XRCpuImage.ConversionParams) }, null);
        void Queue()
        {
            var parameters = new XRCpuImage.ConversionParams { outputDimensions = new Vector2Int(2, 2), outputFormat = TextureFormat.RGBA32 };
            Set(bridge, "_cpuConversion", constructor.Invoke(new object[] { api, 1, parameters }));
            Set(bridge, "_cpuConversionPending", true);
            Set(bridge, "_pendingDimensions", new Vector2Int(2, 2));
            Set(bridge, "_acceptCpuFrames", true);
            Set(bridge, "_cpuConversionStartedAt", System.Diagnostics.Stopwatch.GetTimestamp());
        }
        try
        {
            Queue();
            Require(bridge.FrameVersion == 0 && !bridge.isPrepared && api.DisposedCount == 0,
                "An in-flight CPU conversion must not block or publish an unfinished image.");
            api.State = XRCpuImage.AsyncConversionStatus.Ready;
            Require(bridge.FrameVersion == 1 && bridge.isPrepared && api.DisposedCount == 1 && bridge.LatestFrameReadyTimestamp > 0,
                "Only a complete asynchronous camera image may publish a frame, exactly once.");
            var texture = (Texture2D)bridge.GetCurrentTexture();
            Require(bridge.TryGetLatestCpuFrame(out Texture2D snapshot, out int version, out long readyAt) &&
                    snapshot == texture && version == 1 && readyAt == bridge.LatestFrameReadyTimestamp,
                "Inference must acquire texture, version and capture time from one completed CPU-frame snapshot.");
            Require(texture.GetRawTextureData<byte>()[0] == 42 && texture.GetRawTextureData<byte>()[15] == 42 && bridge.FrameVersion == 1,
                "Published CPU image bytes must survive disposal of the native request and cannot be republished.");
            api.State = XRCpuImage.AsyncConversionStatus.Processing;
            Queue();
            bridge.Pause();
            Require(api.DisposedCount == 2 && bridge.FrameVersion == 1,
                "Pausing must cancel and release native conversion work without publishing a stale image.");
            Require(!bridge.TryGetLatestCpuFrame(out _, out _, out _),
                "Retaining the last texture during pause must not make it eligible for another inference.");
            api.State = XRCpuImage.AsyncConversionStatus.Failed;
            Queue();
            Require(bridge.FrameVersion == 1 && api.DisposedCount == 3,
                "A failed native conversion must be disposed and leave the previous complete frame intact.");
            results.Add("PASS: asynchronous AR camera conversion publishes one owned complete image, never blocks on pending work and disposes failures/pause");
        }
        finally
        {
            bridge.Pause();
            if (bridge.GetCurrentTexture() != null) UnityEngine.Object.DestroyImmediate(bridge.GetCurrentTexture());
        }
    }

    private static void CheckHandResultDispatch(List<string> results)
    {
        var previousSource = Mediapipe.Unity.Sample.ImageSourceProvider.ImageSource;
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("ARHandResultThreadFixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            Mediapipe.Unity.Sample.ImageSourceProvider.SetCustomSource(null);
            var runner = root.AddComponent<Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerRunner>();
            var callback = runner.GetType().GetMethod("OnHandLandmarkDetectionOutput", BindingFlags.Instance | BindingFlags.NonPublic);
            var update = runner.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            var applicationPause = runner.GetType().GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic);
            Set(runner, "_acceptResults", 1);
            Set(runner, "_usesDirectArCpuInput", true);
            int calls = 0;
            int subscriberThread = 0;
            var delivered = default(Mediapipe.Tasks.Vision.HandLandmarker.HandLandmarkerResult);
            runner.OnHandResult += result =>
            {
                calls++;
                subscriberThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                delivered = result;
            };
            var source = Mediapipe.Tasks.Vision.HandLandmarker.HandLandmarkerResult.Alloc(1);
            var hand = Mediapipe.Tasks.Components.Containers.NormalizedLandmarks.Alloc(1);
            hand.landmarks.Add(default);
            source.handLandmarks.Add(hand);
            void Send(Mediapipe.Tasks.Vision.HandLandmarker.HandLandmarkerResult result, long timestamp = 1L) =>
                System.Threading.Tasks.Task.Run(() => callback.Invoke(runner, new object[] { result, null, timestamp })).GetAwaiter().GetResult();
            Send(source);
            Require(calls == 0 && (int)Get(runner, "_arCpuResultReady") == 1,
                "The native callback must acknowledge completion without invoking Unity/event subscribers on its worker thread.");
            hand.landmarks.Clear(); // The native result storage is reused after each callback.
            source.handLandmarks.Clear();
            update.Invoke(runner, null);
            Require(calls == 1 && subscriberThread == System.Threading.Thread.CurrentThread.ManagedThreadId &&
                    delivered.handLandmarks.Count == 1 && delivered.handLandmarks[0].landmarks.Count == 1,
                "Main-thread delivery must own a deep copy that survives native result-list reuse.");
            update.Invoke(runner, null);
            Require(calls == 1, "A result snapshot must be delivered exactly once.");
            Send(source);
            Send(default);
            update.Invoke(runner, null);
            Require(calls == 2 && delivered.handLandmarks == null,
                "The bounded callback slot must coalesce to the newest result, including a lost hand.");
            Set(runner, "_minimumResultTimestamp", 10L);
            Set(runner, "_arCpuResultReady", 0);
            Send(source, 9L);
            update.Invoke(runner, null);
            Require(calls == 2 && (int)Get(runner, "_arCpuResultReady") == 1,
                "A pre-resume inference must be discarded while still acknowledging its CPU-buffer completion.");
            Send(source, 10L);
            update.Invoke(runner, null);
            Require(calls == 3, "A fresh inference at the resume cutoff must still be delivered.");
            Send(source, 11L);
            runner.Pause();
            Send(source, 11L);
            update.Invoke(runner, null);
            Require(calls == 3 && !(bool)Get(runner, "_hasPendingResult"),
                "Pausing must discard queued and late native results instead of replaying stale hand gestures.");
            applicationPause.Invoke(runner, new object[] { true });
            runner.Resume();
            Require((bool)Get(runner, "_restartAfterApplicationPause"),
                "A resume requested while backgrounded must defer native/camera startup.");
            runner.Pause();
            applicationPause.Invoke(runner, new object[] { false });
            Require(runner.IsPaused && !(bool)Get(runner, "_restartAfterApplicationPause"),
                "A later welcome-screen pause must cancel the pending foreground restart.");
            runner.Stop();
            runner.Stop();
            Send(source, 100L);
            Require(!(bool)Get(runner, "_hasPendingResult"), "Stopped runners must ignore late callbacks idempotently.");
            results.Add("PASS: native hand callbacks deep-copy/coalesce into one main-thread result; pause/stop discard stale gestures and background resume is deferred/cancelable");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            Mediapipe.Unity.Sample.ImageSourceProvider.SetCustomSource(previousSource);
        }
    }

    private static void CheckDepthProbeHandMask(List<string> results, Matrix4x4 croppedPortrait)
    {
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        var buildHull = typeof(ButtonInteractor).GetMethod("BuildHandMaskHull", StaticPrivate);
        var contains = typeof(ButtonInteractor).GetMethod("IsPointInHandMask", StaticPrivate);
        var points = new Vector2[21];
        var hull = new Vector2[42];
        points[0] = new Vector2(100f, 100f);
        points[1] = new Vector2(200f, 220f);
        points[2] = new Vector2(100f, 220f);
        points[3] = new Vector2(200f, 100f);
        for (int i = 4; i < points.Length; i++) points[i] = new Vector2(120f + i, 130f + i);
        int count = (int)buildHull.Invoke(null, new object[] { points, points.Length, hull });
        bool Contains(Vector2 point) => (bool)contains.Invoke(null, new object[] { point, hull, count, 8f });
        Require(count == 4 && Contains(new Vector2(150f, 150f)) && Contains(new Vector2(200f, 170f)),
            "All hand landmarks must contribute to a conservative closed hull, including palm and finger gaps.");
        Require(Contains(new Vector2(94f, 150f)) && !Contains(new Vector2(90f, 150f)) &&
                !Contains(new Vector2(91f, 91f)),
            "Hand depth rejection must add only the 8-pixel silhouette margin, not block the whole screen.");
        for (int i = 0; i < points.Length; i++) points[i] = new Vector2(100f, 100f);
        Require((int)buildHull.Invoke(null, new object[] { points, points.Length, hull }) == 0,
            "Missing/degenerate hand geometry must not manufacture a whole-screen mask.");
        points[5] = new Vector2(float.NaN, 100f);
        Require((int)buildHull.Invoke(null, new object[] { points, points.Length, hull }) == 0,
            "Non-finite hand geometry must not enter the depth mask.");
        results.Add("PASS: conservative 21-landmark hand hull rejects palm/fingers with an 8-pixel margin only");

        var previousSource = Mediapipe.Unity.Sample.ImageSourceProvider.ImageSource;
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("ARHandDepthMaskFixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            Mediapipe.Unity.Sample.ImageSourceProvider.SetCustomSource(null);
            var runner = root.AddComponent<Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerRunner>();
            runner.Pause();
            Require(runner.IsPaused, "A welcome-screen pause before Bootstrap must preserve paused state without a camera source.");
            runner.Resume();
            Require(!runner.IsPaused, "A resume before initial Play must leave startup to Bootstrap without starting an invalid coroutine.");
            runner.Stop();
            runner.Stop();
            Require(runner.IsPaused, "Stopping an unstarted runner must be safe and idempotent.");
            results.Add("PASS: welcome-screen pause/resume/stop are safe before camera Bootstrap and initial Play");
            var input = root.AddComponent<ButtonInteractor>();
            var imagePoints = (Vector2[])Get(input, "handMaskImagePoints");
            Vector2 bottomLeft = new Vector2(0.3f, 0.3f);
            Vector2 topRight = new Vector2(0.6f, 0.65f);
            imagePoints[0] = ToImage(bottomLeft, croppedPortrait);
            imagePoints[1] = ToImage(topRight, croppedPortrait);
            imagePoints[2] = ToImage(new Vector2(bottomLeft.x, topRight.y), croppedPortrait);
            imagePoints[3] = ToImage(new Vector2(topRight.x, bottomLeft.y), croppedPortrait);
            for (int i = 4; i < imagePoints.Length; i++)
                imagePoints[i] = ToImage(new Vector2(0.4f, 0.45f), croppedPortrait);
            var bridge = new CustomARFrameBridge(null);
            Set(bridge, "_displayMatrix", croppedPortrait);
            Set(bridge, "_hasDisplayMatrix", true);
            typeof(ButtonInteractor).GetMethod("UpdateHandDepthProbeMask", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(input, new object[] { bridge });
            Set(input, "<IsHandVisible>k__BackingField", true);
            Set(input, "isHandVisible", true);
            Set(input, "hasRawHandMaskLandmarks", true);
            Set(input, "lastHandResultTicks", System.Diagnostics.Stopwatch.GetTimestamp());
            Vector2 palm = new Vector2(Screen.width * 0.45f, Screen.height * 0.45f);
            Require(input.IsPointOverTrackedHand(palm) &&
                    !input.IsPointOverTrackedHand(new Vector2(Screen.width * 0.9f, Screen.height * 0.9f)),
                "The mask must use the same crop-correct AR screen mapping as the finger pointer.");
            Set(input, "hasRawHandMaskLandmarks", false);
            Require(!input.IsPointOverTrackedHand(palm),
                "Fixtures/input without all 21 landmarks must not inherit a stale or whole-screen mask.");
            Set(input, "hasRawHandMaskLandmarks", true);
            Set(input, "lastHandResultTicks", 0L);
            Require(!input.IsPointOverTrackedHand(palm), "Expired hand results must immediately invalidate depth rejection.");
            Set(input, "lastHandResultTicks", System.Diagnostics.Stopwatch.GetTimestamp());
            Set(input, "isHandVisible", false);
            Require(!input.IsPointOverTrackedHand(palm), "Lost hands must immediately invalidate depth rejection.");
            Set(input, "isHandVisible", true);
            Mediapipe.Unity.Sample.ImageSourceProvider.SetCustomSource(bridge);
            bridge.Pause();
            Require(!input.IsPointOverTrackedHand(palm), "A paused/stopped image source must invalidate the hand mask.");
            results.Add("PASS: crop-correct hand depth mask expires on missing landmarks, lost/stale hands and paused camera");

            Mediapipe.Unity.Sample.ImageSourceProvider.SetCustomSource(null);
            var panel = new GameObject("HandMaskCoordinates", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            var finger = new GameObject("HandMaskFinger", typeof(RectTransform));
            finger.transform.SetParent(panel.transform, false);
            input.videoPanel = panel.GetComponent<RectTransform>();
            input.debugPoint = finger.GetComponent<RectTransform>();
            input.interactiveButtons = new List<RectTransform>();
            foreach (string invalidField in new[] { "targetX", "targetY", "targetThumbX", "targetThumbY", "rawDistance" })
            {
                Set(input, "targetX", 0.5f);
                Set(input, "targetY", 0.5f);
                Set(input, "targetThumbX", 0.5f);
                Set(input, "targetThumbY", 0.5f);
                Set(input, "rawDistance", 0.01f);
                Set(input, invalidField, float.NaN);
                Set(input, "lastHandResultTicks", System.Diagnostics.Stopwatch.GetTimestamp());
                Set(input, "isHandVisible", true);
                input.isPinching = true;
                typeof(ButtonInteractor).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(input, null);
                Require(!input.IsHandVisible && !input.isPinching && !input.JustPinched &&
                        !input.IsPointOverTrackedHand(palm),
                    "Non-finite index/thumb/pinch input must hide the pointer, clear pinch and invalidate the depth mask.");
            }
            results.Add("PASS: non-finite finger, thumb or pinch data cannot preserve a center-fallback pointer or held pinch");
        }
        finally
        {
            Mediapipe.Unity.Sample.ImageSourceProvider.SetCustomSource(previousSource);
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CheckRoundTrips(Matrix4x4 matrix)
    {
        foreach (Vector2 viewport in new[]
        {
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            new Vector2(0.12f, 0.83f), new Vector2(0.93f, 0.24f)
        })
            Require(CustomARFrameBridge.TryMapImagePointToViewport(ToImage(viewport, matrix), matrix,
                    out Vector2 recovered) && Near(recovered, viewport),
                "Coordinate round-trip must agree with ARCoreBackground's row-vector shader sampling.");
    }

    private static Vector2 ToImage(Vector2 viewport, Matrix4x4 matrix) => new Vector2(
        viewport.x * matrix.m00 + viewport.y * matrix.m10 + matrix.m20,
        viewport.x * matrix.m01 + viewport.y * matrix.m11 + matrix.m21);

    private static bool Near(Vector2 actual, Vector2 expected) => (actual - expected).sqrMagnitude < 0.00000001f;

    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static object Get(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("AR camera coordinate regression: " + message);
    }
}
