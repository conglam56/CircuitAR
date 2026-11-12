using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>Input timing checks without starting a camera, AR session or native model.</summary>
internal static class HandInputLatencyRegressionChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("CircuitAR/Validate Hand Input Latency")]
    public static void Run()
    {
        var results = new List<string>();
        Run(results);
        Debug.Log("[HandInputLatencyChecks]\n" + string.Join("\n", results));
    }

    public static void Run(List<string> results)
    {
        CheckCursorFilter(results);
        CheckHandIdentity(results);
        var previousSource = Mediapipe.Unity.Sample.ImageSourceProvider.ImageSource;
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("HandInputLatencyFixture");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            Mediapipe.Unity.Sample.ImageSourceProvider.SetCustomSource(null);
            var input = root.AddComponent<ButtonInteractor>();
            input.interactiveButtons = new List<RectTransform>();
            input.videoPanel = Child(root, "HandCoordinates").GetComponent<RectTransform>();
            input.debugPoint = Child(input.videoPanel.gameObject, "Finger").GetComponent<RectTransform>();
            input.minStablePinchFrames = 2;
            Set(input, "targetX", 0.5f);
            Set(input, "targetY", 0.5f);
            Set(input, "targetThumbX", 0.5f);
            Set(input, "targetThumbY", 0.5f);
            long lastSample = 0;
            void NewSample(float distance)
            {
                lastSample = Math.Max(Stopwatch.GetTimestamp(), lastSample + 1);
                Set(input, "lastHandResultTicks", lastSample);
                Set(input, "rawDistance", distance);
                Set(input, "isHandVisible", true);
                Update(input);
            }
            void RenderSameSample()
            {
                for (int i = 0; i < 8; i++) Update(input);
            }

            NewSample(0.01f);
            RenderSameSample();
            Require(input.IsHandVisible && !input.isPinching && !input.JustPinched,
                "Repeated rendering of one closed-hand inference cannot confirm a pinch.");
            NewSample(0.01f);
            Require(input.isPinching && input.JustPinched,
                "The second distinct closed-hand result must confirm the pinch immediately.");
            RenderSameSample();
            Require(input.isPinching && !input.JustPinched,
                "A held pinch must emit exactly one edge regardless of render frame count.");
            NewSample(1f);
            RenderSameSample();
            Require(input.isPinching, "One open-hand result cannot be counted as multiple release samples.");
            NewSample(1f);
            Require(!input.isPinching, "Two distinct open-hand results must release the gesture.");
            results.Add("PASS: pinch and release count distinct inference samples independently of render FPS");

            NewSample(0.01f);
            NewSample((input.pinchThreshold + input.pinchExitThreshold) * 0.5f);
            NewSample(0.01f);
            Require(!input.isPinching,
                "A hysteresis deadband sample interrupts consecutive pinch confirmation.");
            NewSample(0.01f);
            Require(input.isPinching, "Pinch confirmation must recover after two consecutive valid results.");
            results.Add("PASS: ambiguous hand samples reset confirmation without changing held gesture hysteresis");

            Set(input, "isHandVisible", false);
            Set(input, "lastHandResultTicks", Stopwatch.GetTimestamp());
            Set(input, "lastVisibleHandAt", Time.unscaledTime - 1f);
            Update(input);
            Require(!input.IsHandVisible && !input.isPinching,
                "Sustained hand loss must clear gesture state.");
            Set(input, "targetX", 0.1f);
            Set(input, "targetY", 0.2f);
            NewSample(1f);
            Require(input.IsHandVisible && !input.isPinching,
                "The first reacquired open-hand sample must restore the cursor immediately.");
            var displayed = (Vector2)Get(input, "displayedCursorAnchor");
            Require((displayed - new Vector2(0.2f, 0.9f)).sqrMagnitude < 0.000001f,
                "Reacquisition must snap to the current fingertip instead of travelling from the lost position.");
            results.Add("PASS: hand reacquisition restores the cursor on its first result without a stale-position tail");

            NewSample(1f);
            NewSample(1f);
            Set(input, "lastGlobalPinchTime", float.NegativeInfinity);
            NewSample(0.01f);
            Set(input, "isHandVisible", false);
            Set(input, "lastHandResultTicks", Stopwatch.GetTimestamp());
            Set(input, "lastVisibleHandAt", Time.unscaledTime - 0.1f);
            Update(input);
            Require(!input.IsHandVisible && !input.JustPinched, "A short inference gap must not create a pointer action.");
            NewSample(0.01f);
            Require(input.isPinching && input.JustPinched,
                "A brief missing sample must not erase the previous valid pinch confirmation sample in AR.");
            results.Add("PASS: AR pinch confirmation survives a brief missing sample without acting on a stale pointer");

            if (Screen.width > Screen.height)
            {
                var headsetCamera = Child(root, "OcclusionHeadsetCamera").AddComponent<Camera>();
                try
                {
                    Set(input, "targetX", 0.5f);
                    Set(input, "targetY", 0.5f);
                    Set(input, "targetThumbX", 0.5f);
                    Set(input, "targetThumbY", 0.5f);
                    Set(input, "lastGlobalPinchTime", float.NegativeInfinity);
                    input.SetHeadsetCamera(headsetCamera);
                    NewSample(1f);
                    NewSample(0.01f);
                    Require(input.isPinching, "A deliberate headset pinch must confirm on its first fresh closed sample after opening.");
                    Set(input, "isHandVisible", false);
                    Set(input, "lastHandResultTicks", Stopwatch.GetTimestamp());
                    Set(input, "lastVisibleHandAt", Time.unscaledTime - 0.1f);
                    Update(input);
                    Require(input.IsGestureTemporarilyOccluded && input.isPinching && !(bool)Get(input, "headsetNeedsOpenHand"),
                        "A fresh brief headset hand loss must retain the gesture without demanding a new open hand.");
                    Require(!input.IsHandVisible && !input.JustPinched && input.GetHeadsetCursor().z > 0f &&
                        !input.IsPointerOverUI && !(bool)Get(input, "hasWorldDragPoint") && (int)Get(input, "handMaskHullCount") == 0,
                        "Brief missing inference must retain only cursor appearance, with no valid world pointer or depth mask.");
                    NewSample(0.01f);
                    Require(input.isPinching && !input.JustPinched,
                        "Reacquiring the still-closed hand must resume the held gesture without generating another pinch edge.");
                    Set(input, "isHandVisible", false);
                    Set(input, "lastHandResultTicks", Stopwatch.GetTimestamp());
                    Set(input, "lastVisibleHandAt", Time.unscaledTime - 1f);
                    Update(input);
                    Require(input.GetHeadsetCursor() == Vector4.zero,
                        "Cursor retention must expire after sustained hand loss rather than showing a stale finger indefinitely.");
                    results.Add("PASS: cursor survives short inference gaps visually while stale pointer actions stay blocked and long loss hides it");
                    NewSample(0.01f);
                    Require(!input.isPinching && (bool)Get(input, "headsetNeedsOpenHand"),
                        "Sustained loss must still require opening before any new headset grab.");
                    NewSample(1f);
                    Set(input, "isHandVisible", false);
                    Set(input, "lastHandResultTicks", Stopwatch.GetTimestamp());
                    Set(input, "lastVisibleHandAt", Time.unscaledTime - 0.1f);
                    Update(input);
                    Require(!input.IsHandVisible && !(bool)Get(input, "headsetNeedsOpenHand"),
                        "Losing one OPEN-hand sample must not require another open gesture just before pinching.");
                    Set(input, "lastGlobalPinchTime", float.NegativeInfinity);
                    NewSample(0.01f);
                    input.SetPlacementFeedback(ButtonInteractor.PlacementFeedback.SearchingSurface);
                    Require(input.isPinching && input.JustPinched && input.GetHeadsetCursor().w == 2f,
                        "A fresh pinch after an open-hand gap must turn the headset cursor active even while placement feedback draws a ring.");
                    var graphic = (HandCursorGraphic)Get(input, "cursorVisualGraphic");
                    Require(graphic.color.g > 0.9f && graphic.color.r < 0.3f,
                        "The ordinary AR cursor must also show the recognized held-pinch color.");
                    input.SetPlacementFeedback(ButtonInteractor.PlacementFeedback.None);
                    results.Add("PASS: open-hand occlusion does not swallow the next pinch, and active pinch color takes priority over the searching ring");
                    NewSample(1f);
                    results.Add("PASS: headset pinch survives transient hand loss and resumes without rearming; sustained loss still blocks stale grabs");
                }
                finally
                {
                    input.SetHeadsetCamera(null);
                    UnityEngine.Object.DestroyImmediate(headsetCamera.gameObject);
                }
            }

            var runner = root.AddComponent<Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerRunner>();
            var baseType = typeof(Mediapipe.Unity.Sample.BaseRunner);
            var getTimestamp = baseType.GetMethod("GetCurrentTimestampMillisec", PrivateInstance);
            var clock = (Stopwatch)baseType.GetField("_stopwatch", PrivateInstance).GetValue(runner);
            long Timestamp() => (long)getTimestamp.Invoke(runner, null);
            Require(Timestamp() == -1, "An unstarted clock must not submit a valid video timestamp.");
            clock.Start();
            long previous = Timestamp();
            for (int i = 0; i < 16; i++)
            {
                long current = Timestamp();
                Require(current > previous, "Inference timestamps must increase even within one millisecond.");
                previous = current;
            }
            clock.Stop();
            Require(Timestamp() == -1, "A stopped clock must not submit a valid video timestamp.");
            results.Add("PASS: inference clock is strictly increasing for rapid calls and rejects stopped submissions");
            CheckSourceFreshness(input, runner, results);
            CheckRenderPolicy(root, results);
        }
        finally
        {
            // The fixture intentionally stays inactive, so Unity may not invoke
            // its ordinary MonoBehaviour.OnDestroy. Update creates this owned
            // overlay outside the fixture hierarchy; release it explicitly.
            var input = root.GetComponent<ButtonInteractor>();
            if (input != null && Get(input, "cursorOverlayRoot") is GameObject overlay)
            {
                UnityEngine.Object.DestroyImmediate(overlay);
                Set(input, "cursorOverlayRoot", null);
            }
            // Restore the user's source after fixture cleanup.
            UnityEngine.Object.DestroyImmediate(root);
            Mediapipe.Unity.Sample.ImageSourceProvider.SetCustomSource(previousSource);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CheckHandIdentity(List<string> results)
    {
        var result = Mediapipe.Tasks.Vision.HandLandmarker.HandLandmarkerResult.Alloc(2);
        Mediapipe.Tasks.Components.Containers.Classifications Hand(string name) =>
            Mediapipe.Tasks.Components.Containers.Classifications.CreateFrom(new Mediapipe.ClassificationList
            {
                Classification = { new Mediapipe.Classification { Label = name, Score = 0.99f } }
            });
        result.handLandmarks.Add(default);
        result.handLandmarks.Add(default);
        result.handedness.Add(Hand("Left"));
        result.handedness.Add(Hand("Right"));
        var find = typeof(ButtonInteractor).GetMethod("FindHandIndex", BindingFlags.Static | BindingFlags.NonPublic);
        int Index(int channel) => (int)find.Invoke(null, new object[] { result, channel });
        Require(Index(1) == 0 && Index(2) == 1, "Each hand channel must select its own handedness.");
        result.handedness.Reverse();
        Require(Index(1) == 1 && Index(2) == 0, "Reordering inference results must not swap hand channels.");
        result.handedness.RemoveAt(1);
        result.handLandmarks.RemoveAt(1);
        Require(Index(1) == -1 && Index(2) == 0, "Losing one hand must not transfer its pointer to the remaining hand.");
        results.Add("PASS: two-hand identities survive result reordering and loss without assigning both pointers to one hand");
        CheckTemporalHandIdentity(results);

    }

    private static void CheckTemporalHandIdentity(List<string> results)
    {
        var result = Mediapipe.Tasks.Vision.HandLandmarker.HandLandmarkerResult.Alloc(2);
        Mediapipe.Tasks.Components.Containers.NormalizedLandmarks Palm(float x)
        {
            var points = Mediapipe.Tasks.Components.Containers.NormalizedLandmarks.Alloc(21);
            for (int i = 0; i < 21; i++) points.landmarks.Add(
                Mediapipe.Tasks.Components.Containers.NormalizedLandmark.CreateFrom(new Mediapipe.NormalizedLandmark { X = x, Y = 0.5f }));
            return points;
        }
        Mediapipe.Tasks.Components.Containers.Classifications Label(string name, float score) =>
            Mediapipe.Tasks.Components.Containers.Classifications.CreateFrom(new Mediapipe.ClassificationList
            { Classification = { new Mediapipe.Classification { Label = name, Score = score } } });
        result.handLandmarks.Add(Palm(0.25f));
        result.handLandmarks.Add(Palm(0.75f));
        result.handedness.Add(Label("Left", 0.99f));
        result.handedness.Add(Label("Right", 0.99f));
        var trackerType = typeof(ButtonInteractor).GetNestedType("HandIdentityTracker", BindingFlags.NonPublic);
        object tracker = Activator.CreateInstance(trackerType, true);
        var find = trackerType.GetMethod("Find");
        long ticks = Stopwatch.GetTimestamp();
        int Find(int channel) => (int)find.Invoke(tracker, new object[] { result, channel, ticks });
        Require(Find(1) == 0 && Find(2) == 1, "Two hands must initially use their confident handedness labels.");
        ticks += Stopwatch.Frequency / 30;
        result.handLandmarks.Reverse();
        result.handedness.Reverse();
        Require(Find(1) == 1 && Find(2) == 0, "Reordering detections must preserve both physical palm identities.");
        ticks += Stopwatch.Frequency / 30;
        result.handedness[0] = Label("Left", 0.49f);
        result.handedness[1] = Label("Left", 0.51f);
        Require(Find(1) == 1 && Find(2) == 0,
            "Duplicate/low-confidence handedness labels must not suppress or exchange valid tracked palms.");
        foreach (float separation in new[] { 0.2f, 0.08f, 0.02f })
        {
            ticks += Stopwatch.Frequency / 30;
            result.handLandmarks[0] = Palm(0.5f + separation * 0.5f);
            result.handLandmarks[1] = Palm(0.5f - separation * 0.5f);
            Require(Find(1) == 1 && Find(2) == 0,
                "Bringing the palms close while handedness is ambiguous must preserve two distinct input owners.");
        }
        ticks += Stopwatch.Frequency / 30;
        result.handLandmarks.RemoveAt(1);
        result.handedness.RemoveAt(1);
        result.handedness[0] = Label("Left", 0.99f);
        Require(Find(1) == -1 && Find(2) == 0,
            "One occluded hand must not steal the remaining palm when its handedness briefly flips.");
        ticks += Stopwatch.Frequency / 30;
        result.handLandmarks.Add(Palm(0.49f));
        result.handedness.Add(Label("Right", 0.99f));
        Require(Find(1) == 1 && Find(2) == 0, "Both hands must reacquire their own palms despite swapped labels.");
        results.Add("PASS: joint palm tracking preserves two independent hands through reordering, duplicate labels, low handedness confidence and single-hand occlusion");
        trackerType.GetMethod("Reset").Invoke(tracker, null);
        ticks += Stopwatch.Frequency / 30;
        Require(Find(0) == 0, "Single-pointer AR should initially use the first valid hand.");
        ticks += Stopwatch.Frequency / 30;
        result.handLandmarks.Reverse();
        result.handedness.Reverse();
        Require(Find(0) == 1,
            "Single-pointer AR must retain the same palm when two-hand inference changes its result order.");
        results.Add("PASS: the ordinary AR cursor stays with its current hand instead of jumping between reordered detections");
    }

    private static void CheckSourceFreshness(ButtonInteractor input,
        Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerRunner runner, List<string> results)
    {
        var result = Mediapipe.Tasks.Vision.HandLandmarker.HandLandmarkerResult.Alloc(1);
        var points = Mediapipe.Tasks.Components.Containers.NormalizedLandmarks.Alloc(21);
        for (int i = 0; i < 21; i++)
        {
            var proto = new Mediapipe.NormalizedLandmark { X = 0.5f, Y = i == 0 ? 0.7f : 0.5f };
            if (i == 4) proto.X = 0.505f;
            points.landmarks.Add(Mediapipe.Tasks.Components.Containers.NormalizedLandmark.CreateFrom(proto));
        }
        result.handLandmarks.Add(points);
        input.handRunner = runner;
        Set(runner, "_usesDirectArCpuInput", true);
        long expectedSource = 0;
        int delivered = 0;
        var processResult = typeof(ButtonInteractor).GetMethod("ProcessHandResult", PrivateInstance);
        Action<Mediapipe.Tasks.Vision.HandLandmarker.HandLandmarkerResult> consumer = hand =>
        {
            Require(runner.LatestResultSourceReadyTimestamp == expectedSource,
                "The runner must publish the current source timestamp before delivering any result.");
            processResult.Invoke(input, new object[] { hand });
            delivered++;
        };
        runner.OnHandResult += consumer;
        try
        {
            void Deliver(bool live, long sourceTicks)
            {
                expectedSource = sourceTicks;
                Set(runner, "_arSubmittedAtTicks", Stopwatch.GetTimestamp());
                Set(runner, "_arSubmittedFrameReadyTicks", sourceTicks);
                string method = live ? "OnHandLandmarkDetectionOutput" : "PublishResultNow";
                runner.GetType().GetMethod(method, PrivateInstance).Invoke(runner,
                    live ? new object[] { result, null, 0L } : new object[] { result });
                Require((long)Get(input, "lastHandResultTicks") == sourceTicks,
                    "Input freshness and gesture identity must use the source image time, not callback receipt time.");
                Update(input);
            }

            input.isPinching = true;
            Deliver(false, Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 0.6));
            Require(!input.IsHandVisible && !input.isPinching && !input.JustPinched,
                "A newly received inference from an old image cannot show a cursor or preserve a pinch.");

            Set(input, "lastGlobalPinchTime", float.NegativeInfinity);
            long freshSource = Stopwatch.GetTimestamp();
            Deliver(true, freshSource);
            Require(input.IsHandVisible && !input.isPinching,
                "A fresh source image must restore the cursor on its first result without confirming a one-sample pinch.");
            Deliver(true, freshSource);
            Require(input.IsHandVisible && !input.isPinching,
                "Delivering the same source image twice cannot satisfy distinct-sample pinch stability.");
            Deliver(true, Math.Max(Stopwatch.GetTimestamp(), freshSource + 1));
            Require(input.IsHandVisible && input.isPinching && input.JustPinched && delivered == 4,
                "Two distinct fresh source images must confirm a pinch after a slow inference is rejected.");
            results.Add("PASS: stale image results stay stale; synchronous/live consumers receive source time before cursor/pinch processing");
        }
        finally
        {
            runner.OnHandResult -= consumer;
            input.handRunner = null;
            Set(runner, "_usesDirectArCpuInput", false);
        }
    }

    private static void CheckRenderPolicy(GameObject root, List<string> results)
    {
        int originalTargetFrameRate = Application.targetFrameRate;
        var session = Child(root, "DisabledARSession").AddComponent<ARSession>();
        var policy = new ARPlacementRenderPolicy();
        try
        {
            Application.targetFrameRate = 24;
            session.matchFrameRateRequested = true;
            policy.Apply(session);
            Require(policy.IsActive && !session.matchFrameRateRequested && Application.targetFrameRate == 60,
                "Placement rendering must request 60 FPS without blocking on the AR capture cadence.");
            policy.Apply(session);
            policy.Restore();
            Require(!policy.IsActive && session.matchFrameRateRequested && Application.targetFrameRate == 24,
                "Repeated Apply must preserve the original session and render settings for restoration.");

            policy.Apply(null);
            Require(policy.IsActive && Application.targetFrameRate == 60,
                "A not-yet-created AR session must not prevent responsive cursor rendering.");
            policy.Restore();
            policy.Restore();
            Require(Application.targetFrameRate == 24,
                "Null-session restore and repeated restore must preserve the original render target.");

            policy.Apply(null);
            policy.Apply(session);
            policy.Restore();
            Require(Application.targetFrameRate == 24 && session.matchFrameRateRequested,
                "Finding the session after startup must not capture the policy's own 60 FPS as the original target.");

            policy.Apply(session);
            Application.targetFrameRate = 90;
            session.matchFrameRateRequested = true;
            policy.Restore();
            Require(Application.targetFrameRate == 90 && session.matchFrameRateRequested,
                "Restoration must not overwrite another mode's newer render/session request.");
            results.Add("PASS: AR render policy decouples 60 FPS, restores original settings and respects later ownership");
        }
        finally
        {
            policy.Restore();
            Application.targetFrameRate = originalTargetFrameRate;
        }
    }

    private static void CheckCursorFilter(List<string> results)
    {
        var filter = typeof(ButtonInteractor).GetMethod("FilterCursorAnchor", BindingFlags.Static | BindingFlags.NonPublic);
        Vector2 Filter(Vector2 current, Vector2 target, float delta) => (Vector2)filter.Invoke(null,
            new object[] { current, target, new Vector2(1080f, 2400f), delta, 60f });
        var start = new Vector2(0.2f, 0.2f);
        var far = new Vector2(0.8f, 0.7f);
        Require(Filter(start, far, 1f / 60f) == far && Filter(start, far, 1f / 30f) == far,
            "Large intentional cursor movement must reach the current result in one render frame.");
        var near = start + new Vector2(1f / 1080f, 0f);
        Vector2 filtered = Filter(start, near, 1f / 60f);
        Require(filtered.x > start.x && filtered.x < near.x && filtered.y == start.y,
            "Small landmark jitter must remain filtered without overshooting the fingertip.");
        Require(Filter(start, near, 0f) == start,
            "A zero-time update must not invent cursor motion.");
        results.Add("PASS: cursor snaps intentional movement at 30/60 FPS and filters small jitter without prediction");
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, PrivateInstance).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, PrivateInstance).SetValue(target, value);
    private static void Update(ButtonInteractor input) => typeof(ButtonInteractor).GetMethod("Update", PrivateInstance).Invoke(input, null);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Hand input latency regression: " + message);
    }
}
