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
