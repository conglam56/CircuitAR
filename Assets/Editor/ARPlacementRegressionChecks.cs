using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;

/// <summary>Editor checks for the input/visibility regressions; device tracking is tested on Android.</summary>
internal static class ARPlacementRegressionChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    // Batch mode entry point for validating and packaging exactly the same saved sources.
    public static void RunAndBuild()
    {
        Run();
        BuildTestApk();
    }

    [MenuItem("CircuitAR/Build AR Placement Test APK")]
    public static void BuildTestApk()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new InvalidOperationException("The Editor must be outside Play mode with Android active.");

        var scenes = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
            if (scene.enabled) scenes.Add(scene.path);
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Builds"));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = "Builds/CircuitAR-surface-fix.apk",
            target = BuildTarget.Android,
            options = BuildOptions.Development
        });
        string summary = report.summary.result + ": " + report.summary.totalErrors + " errors; " + report.summary.outputPath;
        WriteReport("ar-placement-apk-build.txt", "ARPlacementTestApkBuild.txt", summary);
        Debug.Log("[ARPlacementTestAPK] " + summary);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new InvalidOperationException(summary);
    }

    [MenuItem("CircuitAR/Validate AR Placement")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run placement checks outside Play mode.");

        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("ARPlacementRegressionFixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        var results = new List<string>();
        var sessionStateField = typeof(ARSession).GetField("s_State", BindingFlags.Static | BindingFlags.NonPublic);
        var originalSessionState = (ARSessionState)sessionStateField.GetValue(null);
        try
        {
            // Exercise cold-start branches without starting/resetting the user's AR session.
            sessionStateField.SetValue(null, ARSessionState.SessionInitializing);
            var canvas = Child(root, "Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var panel = Child(canvas.gameObject, "HandCoordinates", typeof(RectTransform), typeof(RawImage));
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            var finger = Child(panel, "Finger", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            var screen = panel.AddComponent<Mediapipe.Unity.Screen>();
            Set(screen, "_screen", panel.GetComponent<RawImage>());
            panel.SetActive(false);
            screen.Initialize(new CustomARFrameBridge(null));
            Require(panel.activeInHierarchy && finger.gameObject.activeInHierarchy,
                "AR source must leave the finger hierarchy active.");
            Require(!panel.GetComponent<RawImage>().enabled && !panel.GetComponent<RawImage>().raycastTarget,
                "The CPU camera RawImage must not draw over native AR or intercept input.");
            results.Add("PASS: AR screen keeps finger hierarchy active and CPU background hidden");

            var input = Child(root, "Input").AddComponent<ButtonInteractor>();
            input.videoPanel = panelRect;
            input.debugPoint = finger;
            input.interactiveButtons = new List<RectTransform>();
            Call(input, "SetupFingerPointVisuals");
            SceneManager.MoveGameObjectToScene((GameObject)Get(input, "cursorOverlayRoot"), scene);
            var cursorVisual = (RectTransform)Get(input, "cursorVisual");
            var cursorGraphic = cursorVisual.GetComponent<HandCursorGraphic>();
            var cursorOutline = cursorVisual.GetComponent<Outline>();
            var cursorHalo = cursorVisual.Find("PinchHalo");
            Require(cursorGraphic != null && !cursorGraphic.IsRing && cursorGraphic.color == input.cursorColor,
                "The hand cursor must use the filled circular UI mesh.");
            Require(cursorOutline != null && cursorHalo != null &&
                cursorHalo.GetComponent<HandCursorGraphic>().IsRing &&
                !cursorHalo.GetComponent<HandCursorGraphic>().enabled,
                "The XR cursor must have a dark outline and a hidden idle pinch halo.");
            Require(!finger.GetComponent<Image>().enabled &&
                ((GameObject)Get(input, "cursorOverlayRoot")).GetComponent<Canvas>().sortingOrder == 10000,
                "The visible cursor must use its own topmost overlay instead of the MediaPipe image canvas.");
            Require(input.thumbPoint != null && !input.thumbPoint.GetComponent<Image>().enabled,
                "The thumb landmark must remain available for pinch detection without drawing a second cursor.");
            Call(input, "BeginPinchVisual");
            Call(input, "UpdateCursorVisual", input.pinchCompressDuration);
            Require(cursorVisual.localScale.x <= input.pinchPressedScale + 0.01f &&
                cursorHalo.GetComponent<HandCursorGraphic>().enabled,
                "Pinch feedback must compress the dot and start the confirmation halo.");
            Call(input, "UpdateCursorVisual", input.pinchHaloDuration + input.pinchReleaseDuration);
            Require(Mathf.Approximately(cursorVisual.localScale.x, 1f) &&
                !cursorHalo.GetComponent<HandCursorGraphic>().enabled,
                "Pinch feedback must return to its idle size and hide the halo.");
            results.Add("PASS: XR hand cursor uses one white dot with a one-shot pinch pulse");

            foreach (ButtonInteractor.PlacementFeedback feedback in Enum.GetValues(typeof(ButtonInteractor.PlacementFeedback)))
            {
                input.SetPlacementFeedback(feedback);
                bool shouldBeHollow = feedback == ButtonInteractor.PlacementFeedback.SearchingSurface ||
                                      feedback == ButtonInteractor.PlacementFeedback.Anchoring;
                Require(input.CurrentPlacementFeedback == feedback && cursorGraphic.IsRing == shouldBeHollow &&
                        cursorGraphic.color == input.cursorColor,
                    "Placement readiness must use a hollow/filled white cursor, without tinting the hand feedback red.");
                Call(input, "BeginPinchVisual");
                Call(input, "UpdateCursorVisual", input.pinchCompressDuration);
                Require(cursorVisual.localScale.x <= input.pinchPressedScale + 0.01f &&
                        cursorHalo.GetComponent<HandCursorGraphic>().enabled && cursorGraphic.IsRing == shouldBeHollow,
                    "Searching/anchoring feedback must preserve the same pinch compression and one-shot halo.");
                Call(input, "UpdateCursorVisual", input.pinchHaloDuration + input.pinchReleaseDuration);
                Require(Mathf.Approximately(cursorVisual.localScale.x, 1f) &&
                        !cursorHalo.GetComponent<HandCursorGraphic>().enabled,
                    "Every readiness state must finish its pinch pulse at the original idle size.");
            }
            input.SetPlacementFeedback(ButtonInteractor.PlacementFeedback.None);
            results.Add("PASS: placement readiness uses white circle/ring states and preserves pinch pulse timing");

            Set(input, "isHandVisible", true);
            Set(input, "lastHandResultTicks", 0L);
            input.isPinching = true;
            Call(input, "Update");
            Require(!input.IsHandVisible && !input.isPinching && !input.JustPinched,
                "An expired hand result must clear all world gesture state.");
            results.Add("PASS: expired MediaPipe result cannot keep a pinch alive");

            var normalizedPinch = typeof(ButtonInteractor).GetMethod("GetNormalizedPinchDistance",
                BindingFlags.Static | BindingFlags.NonPublic);
            float PinchDistance(float gap, float palm) => (float)normalizedPinch.Invoke(null,
                new object[] { Vector2.zero, new Vector2(gap, 0f), palm });
            Require(PinchDistance(0.01f, 0.2f) < input.pinchThreshold &&
                    PinchDistance(0.005f, 0.1f) < input.pinchThreshold &&
                    PinchDistance(0.08f, 0.2f) > input.pinchExitThreshold &&
                    PinchDistance(0.04f, 0.1f) > input.pinchExitThreshold,
                "An open hand must release pinch even when farther away; a closed hand must pinch at either distance.");
            results.Add("PASS: pinch/release thresholds stay consistent as hand distance changes");

            var uiButton = Child(canvas.gameObject, "ToolboxButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var buttonRect = uiButton.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(400, 400);
            input.RegisterButton(buttonRect);
            Set(input, "targetX", 0.5f);
            Set(input, "targetY", 0.5f);
            Set(input, "rawDistance", 0.01f);
            Set(input, "lastHandResultTicks", System.Diagnostics.Stopwatch.GetTimestamp());
            Canvas.ForceUpdateCanvases();
            // Preview-scene overlay geometry can use Editor dimensions rather than
            // Game View Screen dimensions. Place the target in the same pixel space
            // as ButtonInteractor instead of relying on that Canvas's default center.
            Vector2 uiPinchPoint = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            buttonRect.position = new Vector3(uiPinchPoint.x, uiPinchPoint.y, 0f);
            Require(RectTransformUtility.RectangleContainsScreenPoint(buttonRect, uiPinchPoint, null),
                "The UI fixture button must contain the intended screen-space pinch point.");
            Call(input, "Update");
            Call(input, "Update");
            Require(input.IsHandVisible && input.IsPointerOverUI && input.PinchConsumedByUI,
                $"A toolbox pinch must be consumed by the UI (hand={input.IsHandVisible}, pinch={input.isPinching}, hover={input.IsPointerOverUI}, consumed={input.PinchConsumedByUI}, screen={input.CurrentScreenPos}, rect={buttonRect.rect}, image={uiButton.GetComponent<Image>().isActiveAndEnabled}, canvas={canvas.isActiveAndEnabled}).");
            uiButton.SetActive(false);
            Call(input, "Update");
            Require(input.PinchConsumedByUI, "Moving off a button must not turn the same pinch into a world gesture.");
            results.Add("PASS: UI consumes the whole pinch until release");

            Set(input, "isHandVisible", false);
            Set(input, "lastHandResultTicks", System.Diagnostics.Stopwatch.GetTimestamp());
            Call(input, "Update");
            Require(!input.IsHandVisible && input.IsGestureTemporarilyOccluded &&
                    input.isPinching && input.PinchConsumedByUI && !input.JustPinched,
                "One fresh no-hand sample must hide the pointer but retain the active UI gesture owner.");
            HandFrames(input, 0.01f, 1);
            Require(input.IsHandVisible && !input.IsGestureTemporarilyOccluded &&
                    input.PinchConsumedByUI && !input.JustPinched,
                "Reacquiring the same closed hand must not synthesize a world pinch after a UI gesture.");
            Set(input, "isHandVisible", false);
            Set(input, "lastVisibleHandAt", Time.unscaledTime - 0.2f);
            Call(input, "Update");
            Require(!input.IsHandVisible && !input.IsGestureTemporarilyOccluded &&
                    !input.isPinching && !input.PinchConsumedByUI,
                "A sustained missing hand must clear all gesture state after its bounded grace.");
            results.Add("PASS: short hand dropout hides the pointer without reclassifying UI ownership or creating another pinch edge");

            HandFrames(input, 1f);
            Set(input, "lastGlobalPinchTime", float.NegativeInfinity);
            HandFrames(input, 0.01f);
            Require(input.JustPinched && !input.PinchConsumedByUI,
                "A pinch beginning outside the UI must belong to the world.");
            int lateClicks = 0;
            uiButton.GetComponent<Button>().onClick.AddListener(() => lateClicks++);
            uiButton.SetActive(true);
            HandFrames(input, 0.01f, 1);
            Require(input.IsPointerOverUI && !input.PinchConsumedByUI && lateClicks == 0,
                "A button appearing under a held world pinch must not steal it or invoke onClick.");
            results.Add("PASS: a world pinch cannot be stolen by UI midway through the gesture");

            HandFrames(input, 1f);
            var buttonImage = uiButton.GetComponent<Image>();
            var available = typeof(ButtonInteractor).GetMethod("IsButtonAvailable", BindingFlags.Static | BindingFlags.NonPublic);
            bool Available() => (bool)available.Invoke(null, new object[] { buttonRect, buttonImage });
            Require(Available(), "A visible interactive button must remain available.");
            buttonImage.enabled = false;
            HandFrames(input, 0.01f);
            Require(!input.IsPointerOverUI && !input.PinchConsumedByUI,
                "A disabled button graphic must not block world placement.");
            buttonImage.enabled = true;
            var group = uiButton.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            Require(!Available(), "Transparent UI must not block placement.");
            group.alpha = 1f;
            group.blocksRaycasts = false;
            Require(!Available(), "Non-raycast UI must not block placement.");
            group.blocksRaycasts = true;
            canvas.enabled = false;
            Require(!Available(), "A disabled canvas must not leave invisible gesture targets.");
            canvas.enabled = true;
            uiButton.SetActive(false);
            HandFrames(input, 1f);
            results.Add("PASS: hidden, disabled and non-raycast UI cannot consume a placement pinch");

            var controller = Child(root, "Placement").AddComponent<TapToPlaceController>();
            controller.buttonInteractor = input;
            var camera = Child(root, "StartupCamera", typeof(Camera)).GetComponent<Camera>();
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 20f;
            camera.aspect = 9f / 16f;
            camera.fieldOfView = 60f;
            Set(controller, "mainCamera", camera);
            var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template.name = "RegressionComponent";
            template.transform.SetParent(root.transform);
            controller.componentPrefabs = new[] { template };
            controller.componentNames = new[] { "Test" };
            controller.StartPreview("Test");
            var preview = (GameObject)Get(controller, "previewAnchor");
            var hud = (GameObject)Get(controller, "placementStatusRoot");
            Require(controller.CurrentPrefabToPlace == template && preview.activeSelf,
                "Selecting a component must show its 3D preview immediately, even before AR finds a surface.");
            Require(!(bool)Get(controller, "canPlace") && controller.placedObjectsHistory.Count == 0,
                "An immediate preview must not count as a placed object before a real surface and anchor exist.");
            Require(hud == null, "Selecting a component must not create the removed top status banner.");
            Require(controller.GetComponentsInChildren<Canvas>(true).Length == 0 &&
                    controller.GetComponentsInChildren<Camera>(true).Length == 0,
                "Selecting a component must not create a preview card or a thumbnail camera.");
            results.Add("PASS: selection immediately shows a 3D preview without a preview card and blocks ungrounded placement");

            var startupVisual = (GameObject)Get(controller, "previewVisual");
            Vector3 physicalScale = startupVisual.transform.localScale;
            float startupDistance = (float)typeof(TapToPlaceController)
                .GetMethod("GetUngroundedPreviewDistance", PrivateInstance).Invoke(controller, null);
            Require(startupDistance < 0.6f && startupDistance > camera.nearClipPlane,
                "A small real-scale component must be framed nearby, not forced out to the old 0.9 m minimum.");
            Set(input, "<IsHandVisible>k__BackingField", false);
            Call(controller, "ShowUngroundedPreview");
            Vector2 initialFootprint = RequirePreviewFramed(camera, startupVisual);
            camera.transform.SetPositionAndRotation(new Vector3(3f, 1.4f, -2f), Quaternion.Euler(8f, 35f, 0f));
            Call(controller, "ShowUngroundedPreview");
            Vector2 recoveredFootprint = RequirePreviewFramed(camera, startupVisual);
            Require(recoveredFootprint.x >= initialFootprint.x * 0.65f &&
                    recoveredFootprint.y >= initialFootprint.y * 0.65f &&
                    startupVisual.transform.localScale == physicalScale,
                "Refining the camera origin before tracking must keep the no-hand preview visible at its physical scale.");
            sessionStateField.SetValue(null, ARSessionState.SessionTracking);
            camera.transform.position += new Vector3(2f, 0.3f, 1f);
            Call(controller, "ShowUngroundedPreview");
            RequirePreviewFramed(camera, startupVisual);
            camera.aspect = 0.7f;
            camera.projectionMatrix = Matrix4x4.Perspective(44f, camera.aspect, camera.nearClipPlane, camera.farClipPlane);
            camera.transform.position += new Vector3(-1f, 0f, 1f);
            Call(controller, "ShowUngroundedPreview");
            RequirePreviewFramed(camera, startupVisual);
            Require(startupVisual.transform.localScale == physicalScale,
                "The first tracked frame and late camera projection must reframe without resizing the component.");
            results.Add("PASS: cold-start preview stays visible at real scale through camera-origin and projection initialization");

            Set(input, "<IsHandVisible>k__BackingField", true);
            Set(input, "<IsPointerOverUI>k__BackingField", true);
            Set(input, "<PinchConsumedByUI>k__BackingField", false);
            input.isPinching = true;
            bool WorldPointer() => (bool)typeof(TapToPlaceController).GetMethod("HasWorldPlacementPointer", PrivateInstance)
                .Invoke(controller, null);
            Require(WorldPointer(), "A held world-owned pinch must remain valid while crossing UI geometry.");
            input.isPinching = false;
            Require(!WorldPointer(), "Ordinary hover over UI must not move or place a world preview.");
            input.isPinching = true;
            Set(input, "<PinchConsumedByUI>k__BackingField", true);
            Require(!WorldPointer(), "A UI-started pinch must never become a world placement gesture.");
            Set(input, "<PinchConsumedByUI>k__BackingField", false);
            Set(input, "<IsPointerOverUI>k__BackingField", false);
            Set(input, "<IsHandVisible>k__BackingField", false);
            Require(!WorldPointer(), "Expired hand results cannot own a world placement pointer.");
            Set(input, "<IsHandVisible>k__BackingField", true);
            results.Add("PASS: placement honors whole-gesture UI ownership instead of canceling world pinch on hover");

            var planeManagerObject = Child(root, "ManagedPlaneFixture");
            planeManagerObject.SetActive(false);
            var planeManager = planeManagerObject.AddComponent<ARPlaneManager>();
            controller.arPlaneManager = planeManager;
            TrackableId testPlaneId = new TrackableId(101, 202);
            Vector2[] square =
            {
                new Vector2(-1f, -1f), new Vector2(1f, -1f),
                new Vector2(1f, 1f), new Vector2(-1f, 1f)
            };
            var trackedPlane = CreateTrackedPlane(planeManagerObject, planeManager, testPlaneId, square);
            Pose capturedPose = new Pose(new Vector3(0.2f, 0f, 0.3f), Quaternion.identity);
            Call(controller, "ClearPlacementRequest");
            Require(!CaptureSurface(controller, capturedPose, testPlaneId),
                "A tracked surface alone cannot invent a placement request without a pinch.");
            Set(controller, "placementRequestExpiresAt", Time.unscaledTime + 2f);
            Require(!CaptureSurface(controller, capturedPose, TrackableId.invalidId),
                "A placement request may never capture an absent plane.");
            SetPlaneTracking(trackedPlane, testPlaneId, TrackingState.Limited);
            Require(!CaptureSurface(controller, capturedPose, testPlaneId),
                "A limited-tracking plane is not an acceptable captured contact point.");
            SetPlaneTracking(trackedPlane, testPlaneId, TrackingState.Tracking, PlaneAlignment.Vertical);
            Require(!CaptureSurface(controller, capturedPose, testPlaneId),
                "A vertical tracked plane cannot satisfy a floor/table placement.");
            SetPlaneTracking(trackedPlane, testPlaneId, TrackingState.Tracking);
            Require(!CaptureSurface(controller, new Pose(new Vector3(1.1f, 0f, 0f), Quaternion.identity), testPlaneId) &&
                    !CaptureSurface(controller, new Pose(new Vector3(0f, 0.1f, 0f), Quaternion.identity), testPlaneId),
                "Capturing must reject a point outside the measured polygon or above its plane.");
            sessionStateField.SetValue(null, ARSessionState.SessionInitializing);
            Require(!CaptureSurface(controller, capturedPose, testPlaneId),
                "Session initialization must not authorize a captured real-surface contact.");
            sessionStateField.SetValue(null, ARSessionState.SessionTracking);
            Require(CaptureSurface(controller, capturedPose, testPlaneId) &&
                    (bool)Get(controller, "placementRequestHasSurface") &&
                    (TrackableId)Get(controller, "placementRequestPlaneId") == testPlaneId,
                "A real measured polygon point must be capturable before native anchors are ready.");
            Vector3 capturedLocalPoint = (Vector3)Get(controller, "placementRequestLocalPoint");
            Require(!CaptureSurface(controller, new Pose(new Vector3(-0.5f, 0f, -0.5f), Quaternion.identity), testPlaneId) &&
                    (Vector3)Get(controller, "placementRequestLocalPoint") == capturedLocalPoint,
                "A second ray must not replace the first captured surface point.");
            Set(input, "<JustPinched>k__BackingField", false);
            Call(controller, "UpdatePlacementRequest", true, new Vector2(700f, 800f));
            input.isPinching = false;
            Call(controller, "UpdatePlacementRequest", true, new Vector2(100f, 200f));
            Require((bool)Get(controller, "placementRequestHasSurface") &&
                    (Vector3)Get(controller, "placementRequestLocalPoint") == capturedLocalPoint,
                "Moving or releasing the fingers during a valid surface transition must not move or discard its contact.");
            trackedPlane.transform.SetPositionAndRotation(new Vector3(0.5f, 0.12f, -0.2f), Quaternion.Euler(0f, 25f, 0f));
            Require(ResolveSurface(controller, out Pose refinedPose, out TrackableId refinedId) &&
                    refinedId == testPlaneId &&
                    Vector3.Distance(refinedPose.position, trackedPlane.transform.TransformPoint(capturedLocalPoint)) < 0.0001f,
                "A captured point must follow the original plane's local coordinate refinement, not a reprojected screen ray.");
            Set(input, "<IsHandVisible>k__BackingField", false);
            Call(controller, "UpdatePlacementRequest", false, new Vector2(900f, 900f));
            Require((bool)Get(controller, "placementRequestHasSurface") &&
                    (Vector3)Get(controller, "placementRequestLocalPoint") == capturedLocalPoint,
                "A brief loss of hand data after accepting a real point must not cancel its short visual/native transition.");
            Set(input, "<IsHandVisible>k__BackingField", true);
            input.isPinching = true;
            Set(input, "<JustPinched>k__BackingField", true);
            Call(controller, "UpdatePlacementRequest", true, new Vector2(50f, 50f));
            Require((bool)Get(controller, "placementRequestHasSurface") &&
                    (Vector3)Get(controller, "placementRequestLocalPoint") == capturedLocalPoint,
                "A second world pinch while the accepted contact is settling must not replace its point.");
            Set(input, "<JustPinched>k__BackingField", false);
            int capturedGeneration = (int)Get(controller, "placementGeneration");
            Set(controller, "placementRetryEligible", true);
            float capturedRetryStart = Time.unscaledTime;
            Call(controller, "RetryPlacementIfStillHeld", capturedGeneration, preview);
            Require((bool)Get(controller, "placementRequestHasSurface") &&
                    !(bool)Get(controller, "placementRequestAwaitingSurface") &&
                    (Vector3)Get(controller, "placementRequestLocalPoint") == capturedLocalPoint &&
                    (float)Get(controller, "nextPlacementAttemptAt") >= capturedRetryStart + 0.25f &&
                    controller.placedObjectsHistory.Count == 0,
                "A native-anchor retry must keep the measured plane-local point and throttle attempts, never recapture the moving finger ray.");
            Set(controller, "nextPlacementAttemptAt", 0f);
            Set(controller, "placementRetryEligible", false);
            SetPlaneTracking(trackedPlane, testPlaneId, TrackingState.Limited);
            Require(!ResolveSurface(controller, out _, out _), "A captured point cannot resolve while its plane loses tracking.");
            SetPlaneTracking(trackedPlane, testPlaneId, TrackingState.Tracking);
            var measuredBoundary = (NativeArray<Vector2>)Get(trackedPlane, "m_Boundary");
            for (int i = 0; i < measuredBoundary.Length; i++) measuredBoundary[i] = square[i] * 0.1f;
            Require(!ResolveSurface(controller, out _, out _),
                "A captured point outside a refined polygon cannot become an infinite-plane placement.");
            for (int i = 0; i < measuredBoundary.Length; i++) measuredBoundary[i] = square[i];
            PlaneDictionary(planeManager).Remove(testPlaneId);
            Require(!ResolveSurface(controller, out _, out _), "Removing the captured plane must block that placement.");
            PlaneDictionary(planeManager).Add(testPlaneId, trackedPlane);
            Set(input, "<JustPinched>k__BackingField", true);
            Set(input, "<PinchConsumedByUI>k__BackingField", true);
            Call(controller, "UpdatePlacementRequest", false, Vector2.zero);
            RequireNoPlacementRequest(controller, "A new explicitly UI-consumed pinch must cancel a prior accepted placement.");
            Set(input, "<JustPinched>k__BackingField", false);
            Set(input, "<PinchConsumedByUI>k__BackingField", false);
            Call(controller, "ClearPlacementRequest");
            RequireNoPlacementRequest(controller, "Cancel must discard captured plane coordinates along with the request.");
            trackedPlane.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            results.Add("PASS: pinch captures one measured plane-local contact, survives finger release, and rejects invalidated geometry");

            Set(controller, "placementRequestExpiresAt", Time.unscaledTime + 2f);
            Require(CaptureSurface(controller, capturedPose, testPlaneId), "Pending-native checks require a captured real polygon point.");
            Set(controller, "placementPending", true);
            Set(controller, "placementRetryEligible", true);
            bool ShouldCancelPending() => (bool)typeof(TapToPlaceController)
                .GetMethod("ShouldCancelPendingPlacement", PrivateInstance).Invoke(controller, null);
            input.isPinching = false;
            Require(!ShouldCancelPending(), "Normal pinch release must not cancel an already accepted native-anchor request.");
            Set(input, "<IsHandVisible>k__BackingField", false);
            Require(!ShouldCancelPending(), "A brief hand loss must not cancel an already accepted native-anchor request.");
            sessionStateField.SetValue(null, ARSessionState.SessionInitializing);
            Require(ShouldCancelPending(), "Loss of session tracking must invalidate a pending native placement.");
            sessionStateField.SetValue(null, ARSessionState.SessionTracking);
            Set(input, "<IsHandVisible>k__BackingField", true);
            Set(input, "<JustPinched>k__BackingField", true);
            Set(input, "<PinchConsumedByUI>k__BackingField", true);
            Require(ShouldCancelPending(), "A new UI-owned pinch must invalidate a pending native placement.");
            Set(input, "<JustPinched>k__BackingField", false);
            Set(input, "<PinchConsumedByUI>k__BackingField", false);
            for (int i = 0; i < measuredBoundary.Length; i++) measuredBoundary[i] = square[i] * 0.1f;
            Require(ShouldCancelPending(), "A captured point removed from the measured polygon must cancel its pending native placement.");
            for (int i = 0; i < measuredBoundary.Length; i++) measuredBoundary[i] = square[i];
            int pendingGeneration = (int)Get(controller, "placementGeneration");
            Vector3 pendingPreviewPosition = preview.transform.position;
            Set(controller, "surfaceTransitionActive", true);
            Call(controller, "CancelPendingPlacementAttempt");
            Require((int)Get(controller, "placementGeneration") == pendingGeneration + 1 &&
                    !(bool)Get(controller, "placementPending") && !(bool)Get(controller, "placementRetryEligible") &&
                    !(bool)Get(controller, "surfaceTransitionActive") && !((bool)Get(controller, "canPlace")) &&
                    preview.activeSelf && preview.transform.position == pendingPreviewPosition,
                "Pending cancellation must invalidate native callbacks and retries while keeping the visible preview untouched.");
            RequireNoPlacementRequest(controller, "Pending cancellation must discard its captured polygon point.");
            sessionStateField.SetValue(null, ARSessionState.SessionTracking);
            input.isPinching = true;
            Call(controller, "UpdatePreviewAndPlacement");
            RequireNoPlacementRequest(controller, "Returning to tracking without a new pinch must not resurrect the canceled placement.");
            Set(controller, "placementRetryEligible", true);
            Call(controller, "RetryPlacementIfStillHeld", pendingGeneration, preview);
            RequireNoPlacementRequest(controller, "An old native callback cannot revive placement after pending cancellation increments its generation.");
            Set(controller, "placementRetryEligible", false);
            results.Add("PASS: pending native placement survives release/hand loss but invalid session, UI or geometry cancels callbacks safely");

            Vector3 savedCameraPosition = camera.transform.position;
            Quaternion savedCameraRotation = camera.transform.rotation;
            Matrix4x4 savedProjection = camera.projectionMatrix;
            float savedCameraAspect = camera.aspect;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 2f, 0f), Quaternion.Euler(90f, 0f, 0f));
            camera.orthographic = true;
            camera.orthographicSize = 2f;
            camera.aspect = 1f;
            camera.ResetProjectionMatrix();
            var nearbySurface = typeof(TapToPlaceController).GetMethod("TryFindNearbyTrackedSurface", PrivateInstance);
            bool Nearby(Vector3 world, out Pose pose)
            {
                Vector3 projected = camera.WorldToScreenPoint(world);
                var args = new object[] { new Vector2(projected.x, projected.y), Pose.identity, TrackableId.invalidId };
                bool found = (bool)nearbySurface.Invoke(controller, args);
                pose = (Pose)args[1];
                return found;
            }
            Require(Nearby(new Vector3(1.02f, 0f, 0.2f), out Pose nearbyPose) &&
                    Vector3.Distance(nearbyPose.position, new Vector3(1f, 0f, 0.2f)) < 0.001f,
                "A 2 cm finger-ray miss may recover only the exact measured table edge, not a point outside it.");
            Require(!Nearby(new Vector3(1.03f, 0f, 0.2f), out _),
                "Nearby edge recovery must reject points beyond the 2.5 cm physical limit instead of extending the table indefinitely.");
            Vector3 missedScreenPoint = camera.WorldToScreenPoint(new Vector3(1.3f, 0f, 0.2f));
            Set(input, "<CurrentScreenPos>k__BackingField", new Vector2(missedScreenPoint.x, missedScreenPoint.y));
            Set(controller, "hasSmoothedPlacementScreenPosition", false);
            Set(controller, "hasPreviewSurfacePose", false);
            Set(controller, "surfaceTransitionActive", false);
            Call(controller, "UpdatePreviewAndPlacement");
            Require(!(bool)Get(controller, "hasPreviewSurfacePose") && !(bool)Get(controller, "surfaceTransitionActive") &&
                    input.CurrentPlacementFeedback == ButtonInteractor.PlacementFeedback.SearchingSurface &&
                    (bool?)Get(controller, "previewIsInvalid") == false,
                "A missed hand ray must not initialize the preview from an unrelated grid point or plane center elsewhere in view.");
            Set(controller, "hasPreviewSurfacePose", true);
            Set(controller, "cooldownTimer", 0f);
            Set(input, "<JustPinched>k__BackingField", true);
            input.isPinching = true;
            Call(controller, "UpdatePreviewAndPlacement");
            Require((bool)Get(controller, "placementRequestAwaitingSurface") &&
                    !(bool)Get(controller, "placementRequestHasSurface"),
                "A new pinch missing the polygon must await a real hit even if the preview had a valid surface earlier.");
            Set(input, "<JustPinched>k__BackingField", false);
            Set(controller, "placementRequestExpiresAt", Time.unscaledTime - 10f);
            Vector3 recoveredScreenPoint = camera.WorldToScreenPoint(new Vector3(0.3f, 0f, 0.4f));
            Set(input, "<CurrentScreenPos>k__BackingField", new Vector2(recoveredScreenPoint.x, recoveredScreenPoint.y));
            Set(controller, "hasSmoothedPlacementScreenPosition", false);
            Call(controller, "UpdatePreviewAndPlacement");
            Require((bool)Get(controller, "placementRequestHasSurface") &&
                    (TrackableId)Get(controller, "placementRequestPlaneId") == testPlaneId &&
                    Vector3.Distance((Vector3)Get(controller, "placementRequestLocalPoint"), new Vector3(0.3f, 0f, 0.4f)) < 0.001f &&
                    controller.placedObjectsHistory.Count == 0,
                "Keeping the same pinch held after a missed request expires must capture its next real hit without requiring another pinch edge or fabricating an anchor.");
            Call(controller, "ClearPlacementRequest");
            camera.orthographic = false;
            camera.aspect = savedCameraAspect;
            camera.projectionMatrix = savedProjection;
            camera.transform.SetPositionAndRotation(savedCameraPosition, savedCameraRotation);
            results.Add("PASS: nearby surface recovery stays on the measured polygon and rejects misses beyond 2.5 cm");
            results.Add("PASS: a missed pointer cannot jump the ghost to an arbitrary visible grid/plane center");
            results.Add("PASS: a held pinch recovers after a missed existing-surface ray without needing another pinch edge");

            Set(controller, "hasPreviewSurfacePose", false);
            Set(controller, "surfaceTransitionActive", false);
            Set(controller, "canPlace", true);
            Vector3 transitionStart = preview.transform.position;
            Vector3 transitionTarget = transitionStart + new Vector3(0.8f, -0.3f, 1.2f);
            Vector3 transitionScale = startupVisual.transform.localScale;
            Quaternion transitionRotation = Quaternion.Euler(0f, 60f, 0f);
            Call(controller, "UpdatePreviewSurfacePose", transitionTarget, transitionRotation, testPlaneId);
            Require((bool)Get(controller, "surfaceTransitionActive") && !(bool)Get(controller, "canPlace") &&
                    Vector3.Distance(preview.transform.position, transitionStart) < 0.01f &&
                    startupVisual.transform.localScale == transitionScale,
                "The first measured surface must start a smooth pose transition, not teleport or resize the visible preview.");
            Set(controller, "surfaceTransitionStartedAt", Time.unscaledTime - controller.previewSurfaceTransitionDuration * 0.5f);
            Call(controller, "UpdatePreviewSurfacePose", transitionTarget, transitionRotation, testPlaneId);
            Require(Vector3.Distance(preview.transform.position, transitionStart) > 0.05f &&
                    Vector3.Distance(preview.transform.position, transitionTarget) > 0.05f &&
                    (bool)Get(controller, "surfaceTransitionActive") && startupVisual.transform.localScale == transitionScale,
                "Halfway through the transition the preview must be between the ghost and measured poses at identical real scale.");
            Set(controller, "placementRequestExpiresAt", Time.unscaledTime + 2f);
            Require(CaptureSurface(controller, capturedPose, testPlaneId), "The transition fixture must retain a genuine contact.");
            Require(!(bool)typeof(TapToPlaceController).GetMethod("TryCommitPlacementRequest", PrivateInstance).Invoke(controller, null) &&
                    controller.placedObjectsHistory.Count == 0 && (bool)Get(controller, "placementRequestHasSurface"),
                "A pinch cannot commit midway through its visible transition or discard the captured real contact.");
            Set(controller, "surfaceTransitionStartedAt", Time.unscaledTime - controller.previewSurfaceTransitionDuration - 0.01f);
            Call(controller, "UpdatePreviewSurfacePose", transitionTarget, transitionRotation, testPlaneId);
            Require(!(bool)Get(controller, "surfaceTransitionActive") &&
                    Vector3.Distance(preview.transform.position, transitionTarget) < 0.001f &&
                    Quaternion.Angle(preview.transform.rotation, transitionRotation) < 0.1f &&
                    startupVisual.transform.localScale == transitionScale,
                "The completed transition must reach the measured pose without changing component scale.");
            Call(controller, "ClearPlacementRequest");
            controller.arPlaneManager = null;
            Set(controller, "hasPreviewSurfacePose", true);
            results.Add("PASS: first real-surface transition is smooth, scale-preserving and cannot commit midway");

            Set(input, "<IsHandVisible>k__BackingField", true);
            input.isPinching = true;
            Set(controller, "hasPreviewSurfacePose", true);
            Set(controller, "worldPinchArmed", false);
            Set(controller, "cooldownTimer", 0.4f);
            Set(input, "<JustPinched>k__BackingField", true);
            Vector2 pinchPoint = new Vector2(210f, 320f);
            Call(controller, "UpdatePlacementRequest", true, pinchPoint);
            float requestDeadline = (float)Get(controller, "placementRequestExpiresAt");
            Require(requestDeadline >= Time.unscaledTime + 0.7f &&
                (Vector2)Get(controller, "placementRequestScreenPosition") == pinchPoint,
                "A world pinch must queue even when the secondary latch is unarmed or selection cooldown has not ended.");
            Set(input, "<JustPinched>k__BackingField", false);
            Call(controller, "UpdatePlacementRequest", true, pinchPoint + new Vector2(150f, 120f));
            Require((Vector2)Get(controller, "placementRequestScreenPosition") == pinchPoint &&
                (float)Get(controller, "placementRequestExpiresAt") == requestDeadline,
                "Closing/moving the fingers must not shift or discard the captured placement ray.");
            Call(controller, "UpdatePlacementRequest", false, pinchPoint);
            Require(float.IsNegativeInfinity((float)Get(controller, "placementRequestExpiresAt")),
                "Losing the hand or entering UI must cancel the buffered request.");
            results.Add("PASS: one pinch queues through cooldown without a duplicate latch and keeps its original ray");

            sessionStateField.SetValue(null, ARSessionState.SessionInitializing);
            Set(controller, "hasPreviewSurfacePose", false);
            Set(controller, "cooldownTimer", 0f);
            Set(input, "<CurrentScreenPos>k__BackingField", pinchPoint);
            Set(input, "<JustPinched>k__BackingField", true);
            Call(controller, "UpdatePreviewAndPlacement");
            Require((bool)Get(controller, "placementRequestAwaitingSurface") &&
                    (float)Get(controller, "placementRequestExpiresAt") > Time.unscaledTime &&
                    !(bool)Get(controller, "canPlace") && controller.placedObjectsHistory.Count == 0,
                "A fresh held world pinch during SessionInitializing must remain pending without placing an ungrounded object.");
            Set(input, "<JustPinched>k__BackingField", false);
            Set(controller, "placementRequestExpiresAt", Time.unscaledTime - 100f);
            Vector2 startupPointer = pinchPoint + new Vector2(80f, 60f);
            Call(controller, "UpdatePlacementRequest", true, startupPointer);
            Require((float)Get(controller, "placementRequestExpiresAt") >= Time.unscaledTime + 0.3f &&
                    (Vector2)Get(controller, "placementRequestScreenPosition") == startupPointer,
                "An expired startup request must renew while that same pinch is held and follow its current pointer until a real hit.");
            Set(input, "<CurrentScreenPos>k__BackingField", startupPointer);
            Call(controller, "UpdatePreviewAndPlacement");
            Require((bool)Get(controller, "placementRequestAwaitingSurface") &&
                    (float)Get(controller, "placementRequestExpiresAt") > Time.unscaledTime,
                "The SessionInitializing gate must preserve the held startup request instead of silently discarding it.");
            input.isPinching = false;
            Call(controller, "UpdatePlacementRequest", true, startupPointer);
            RequireNoPlacementRequest(controller, "Releasing a startup pinch must cancel it.");
            input.isPinching = true;
            Call(controller, "UpdatePlacementRequest", true, startupPointer);
            RequireNoPlacementRequest(controller, "A held hand without a fresh pinch edge must not revive the canceled request.");
            results.Add("PASS: startup pinch survives initialization only while held and follows the pointer until a real surface");

            Set(input, "<JustPinched>k__BackingField", true);
            Call(controller, "UpdatePlacementRequest", true, startupPointer);
            Set(input, "<JustPinched>k__BackingField", false);
            Set(input, "<IsHandVisible>k__BackingField", false);
            Set(input, "<IsGestureTemporarilyOccluded>k__BackingField", true);
            Call(controller, "UpdatePreviewAndPlacement");
            Require((bool)Get(controller, "placementRequestAwaitingSurface") &&
                    !(bool)Get(controller, "placementRequestHasSurface") && !controller.IsDragging &&
                    controller.placedObjectsHistory.Count == 0 && !(bool)Get(controller, "canPlace"),
                "Short loss of an already-owned startup pinch must retain intent without capturing a stale ray or fabricating placement.");
            Set(input, "<IsGestureTemporarilyOccluded>k__BackingField", false);
            Call(controller, "UpdatePreviewAndPlacement");
            RequireNoPlacementRequest(controller, "Long hand loss must still cancel a startup pinch.");
            Set(input, "<IsHandVisible>k__BackingField", true);
            results.Add("PASS: startup world pinch tolerates a brief dropout without stale-ray placement and cancels sustained loss");

            foreach (bool lostHand in new[] { false, true })
            {
                Set(input, "<JustPinched>k__BackingField", true);
                Call(controller, "UpdatePlacementRequest", true, startupPointer);
                Set(input, "<JustPinched>k__BackingField", false);
                Set(input, "<IsHandVisible>k__BackingField", !lostHand);
                Set(input, "<IsPointerOverUI>k__BackingField", !lostHand);
                Set(input, "<PinchConsumedByUI>k__BackingField", !lostHand);
                Call(controller, "UpdatePreviewAndPlacement");
                RequireNoPlacementRequest(controller, "Entering UI or losing the hand must cancel a startup placement.");
                Set(input, "<IsHandVisible>k__BackingField", true);
                Set(input, "<IsPointerOverUI>k__BackingField", false);
                Set(input, "<PinchConsumedByUI>k__BackingField", false);
                Call(controller, "UpdatePreviewAndPlacement");
                RequireNoPlacementRequest(controller, "Leaving UI or recovering the hand must not auto-resume an old pinch.");
            }
            Set(input, "<JustPinched>k__BackingField", true);
            Call(controller, "UpdatePlacementRequest", true, startupPointer);
            Set(input, "<JustPinched>k__BackingField", false);
            controller.StartPreview("Test");
            preview = (GameObject)Get(controller, "previewAnchor");
            Call(controller, "UpdatePreviewAndPlacement");
            RequireNoPlacementRequest(controller, "Replacing a selection while pinch is held must not inherit its placement request.");
            input.isPinching = false;
            results.Add("PASS: startup pinch cancellation cannot resume after UI, lost hand, release or selection replacement");

            Set(input, "<IsHandVisible>k__BackingField", true);
            Set(input, "<IsPointerOverUI>k__BackingField", false);
            Set(input, "<PinchConsumedByUI>k__BackingField", false);
            Set(input, "<JustPinched>k__BackingField", false);
            input.isPinching = true;
            int retryGeneration = (int)Get(controller, "placementGeneration");
            Set(controller, "placementRetryEligible", true);
            float retryStartedAt = Time.unscaledTime;
            Call(controller, "RetryPlacementIfStillHeld", retryGeneration, preview);
            Require((bool)Get(controller, "placementRequestAwaitingSurface") &&
                    (float)Get(controller, "nextPlacementAttemptAt") >= retryStartedAt + 0.25f &&
                    (float)Get(controller, "placementRequestExpiresAt") > (float)Get(controller, "nextPlacementAttemptAt") &&
                    controller.placedObjectsHistory.Count == 0 && !(bool)Get(controller, "placementRetryEligible"),
                "A native-anchor warmup failure may requeue the same held pinch at most four times per second, never fabricate placement.");
            void ResetRetryRequest()
            {
                Set(controller, "placementRequestAwaitingSurface", false);
                Set(controller, "placementRequestExpiresAt", float.NegativeInfinity);
                Set(controller, "nextPlacementAttemptAt", 0f);
                Set(controller, "placementRetryEligible", true);
            }
            ResetRetryRequest();
            Set(controller, "placementPending", true);
            input.isPinching = false;
            Call(controller, "ObservePendingPlacementGesture");
            input.isPinching = true;
            Call(controller, "RetryPlacementIfStillHeld", retryGeneration, preview);
            RequireNoPlacementRequest(controller,
                "Release followed by a new held pinch while the native callback is pending must not resurrect the old attempt.");
            ResetRetryRequest();
            Set(input, "<JustPinched>k__BackingField", true);
            Call(controller, "ObservePendingPlacementGesture");
            Set(input, "<JustPinched>k__BackingField", false);
            Call(controller, "RetryPlacementIfStillHeld", retryGeneration, preview);
            RequireNoPlacementRequest(controller, "A new pinch edge while awaiting a native callback must invalidate the old gesture's retry.");
            Set(controller, "placementPending", false);
            ResetRetryRequest();
            Call(controller, "RetryPlacementIfStillHeld", retryGeneration - 1, preview);
            RequireNoPlacementRequest(controller, "A callback from a stale placement generation must not retry.");
            ResetRetryRequest();
            var stalePreview = Child(root, "StalePreview");
            Call(controller, "RetryPlacementIfStillHeld", retryGeneration, stalePreview);
            RequireNoPlacementRequest(controller, "A callback for a different preview must not retry the current selection.");
            ResetRetryRequest();
            GameObject canceledPreview = preview;
            controller.ClearPreview();
            Call(controller, "RetryPlacementIfStillHeld", retryGeneration, canceledPreview);
            RequireNoPlacementRequest(controller, "Clearing the selection must invalidate outstanding native-anchor retries.");
            Require(controller.CurrentPrefabToPlace == null && controller.placedObjectsHistory.Count == 0,
                "A canceled warmup retry must create neither a replacement selection nor a placed object.");
            controller.StartPreview("Test");
            preview = (GameObject)Get(controller, "previewAnchor");
            Set(controller, "placementRetryEligible", false);
            Set(controller, "nextPlacementAttemptAt", 0f);
            Set(controller, "placementPending", false);
            input.isPinching = false;
            results.Add("PASS: native-anchor warmup retries are throttled and cannot survive release, a new gesture or a canceled/stale selection");

            var boundary = new NativeArray<Vector2>(new[]
            {
                new Vector2(1, 1), new Vector2(3, 1), new Vector2(3, 2),
                new Vector2(2, 2), new Vector2(2, 3), new Vector2(1, 3)
            }, Allocator.Temp);
            try
            {
                var contains = typeof(TapToPlaceController).GetMethod("IsPointInSurfaceBoundary",
                    BindingFlags.Static | BindingFlags.NonPublic);
                bool Contains(Vector2 point) => (bool)contains.Invoke(null, new object[] { point, boundary });
                Require(Contains(new Vector2(1.5f, 1.5f)) && Contains(new Vector2(1f, 2f)),
                    "Tracked surface interior and exact edge must accept a placement ray.");
                Require(!Contains(new Vector2(2.5f, 2.5f)) && !Contains(new Vector2(3.01f, 1.5f)),
                    "A point inside the plane bounds but outside its actual polygon must be rejected.");
                var nearestBoundary = typeof(TapToPlaceController).GetMethod("TryGetClosestSurfaceBoundaryPoint",
                    BindingFlags.Static | BindingFlags.NonPublic);
                var closestArgs = new object[] { new Vector2(2.5f, 2.5f), boundary, Vector2.zero };
                Require((bool)nearestBoundary.Invoke(null, closestArgs), "A valid measured polygon must expose a nearest edge point.");
                Vector2 closest = (Vector2)closestArgs[2];
                Require(Contains(closest) && Mathf.Abs(Vector2.Distance(closest, new Vector2(2.5f, 2.5f)) - 0.5f) < 0.001f,
                    "Concave boundary snapping must use an actual measured segment, not a bounding-box corner or invented diagonal.");
                closestArgs[0] = new Vector2(3.01f, 1.5f);
                Require((bool)nearestBoundary.Invoke(null, closestArgs) &&
                        Vector2.Distance((Vector2)closestArgs[2], new Vector2(3f, 1.5f)) < 0.0001f,
                    "A near-edge ray must snap to the exact measured edge instead of retaining a point outside the plane.");
            }
            finally { boundary.Dispose(); }
            results.Add("PASS: polygon tests and nearest-edge snapping respect concave boundaries instead of infinite planes/bounds");
            var visual = (GameObject)Get(controller, "previewVisual");
            int materialCount = Resources.FindObjectsOfTypeAll<Material>().Length;
            Material sourceMaterial = template.GetComponent<Renderer>().sharedMaterial;
            for (int i = 0; i < 40; i++) Call(controller, "UpdatePreviewVisual", i % 2 == 0);
            Require(visual.GetComponent<Renderer>().sharedMaterial == sourceMaterial &&
                Resources.FindObjectsOfTypeAll<Material>().Length == materialCount,
                "Preview tint transitions must not clone native materials or replace the shared source.");
            results.Add("PASS: repeated preview tint transitions allocate no native material copies");
            preview.transform.position = new Vector3(0.2f, -1f, 1.4f);
            preview.SetActive(true);
            Set(controller, "hasPreviewSurfacePose", true);
            Vector3 frozenPose = preview.transform.position;
            sessionStateField.SetValue(null, ARSessionState.SessionTracking);
            Call(controller, "UpdatePreviewAndPlacement");
            Require(preview.activeSelf && preview.transform.position == frozenPose &&
                    (bool?)Get(controller, "previewIsInvalid") == false &&
                    input.CurrentPlacementFeedback == ButtonInteractor.PlacementFeedback.SearchingSurface,
                "A missing ray must freeze the last measured preview in its normal materials and report searching, not turn the component red.");
            results.Add("PASS: a missed surface ray does not recolor or teleport the last measured preview");
            sessionStateField.SetValue(null, ARSessionState.SessionInitializing);
            Call(controller, "UpdatePreviewAndPlacement");
            Require(preview.activeSelf && preview.transform.position == frozenPose,
                "Tracking loss must preserve the preview's world pose without deactivating it.");
            Require(!((bool)Get(controller, "canPlace")), "Tracking loss must block placement.");
            results.Add("PASS: preview remains at its world pose during tracking loss");

            sessionStateField.SetValue(null, ARSessionState.SessionTracking);
            bool canCreateAnchor = (bool)typeof(TapToPlaceController)
                .GetMethod("CanCreateSurfaceAnchor", PrivateInstance)
                .Invoke(controller, new object[] { TrackableId.invalidId });
            Require(!canCreateAnchor,
                "SessionTracking alone must not authorize placement without a real plane and running native anchor subsystem.");
            Call(controller, "TryPlaceObject", new Pose(frozenPose, Quaternion.identity), TrackableId.invalidId);
            Require(controller.placedObjectsHistory.Count == 0 && controller.CurrentPrefabToPlace == template,
                "A missing native anchor must never be reported as successful placement.");
            results.Add("PASS: missing anchor leaves selection pending and creates no placed object");
            controller.ClearPreview();
            Require(controller.CurrentPrefabToPlace == null,
                "Cancel must clear pending placement.");
            Require(float.IsNegativeInfinity((float)Get(controller, "placementRequestExpiresAt")),
                "Cancel must discard buffered pinch requests.");
            results.Add("PASS: cancellation clears pending selection without a status banner");
            var sameDragLevel = typeof(TapToPlaceController).GetMethod("IsSameDragSupportLevel",
                BindingFlags.Static | BindingFlags.NonPublic);
            bool SameLevel(float a, float b) => (bool)sameDragLevel.Invoke(null, new object[] { a, b });
            Require(SameLevel(0.75f, 0.76f) && !SameLevel(0.75f, 0f) &&
                    !SameLevel(0.75f, -2f) && !SameLevel(0f, float.NaN),
                "Dragging must tolerate support refinement but reject a distant floor or invalid depth instead of shrinking the object.");
            results.Add("PASS: dragging keeps the measured support level instead of jumping to a distant floor");
            var originalManager = controller.arPlaneManager;
            Vector3 oldCameraPosition = camera.transform.position;
            Quaternion oldCameraRotation = camera.transform.rotation;
            var dragFixture = Child(root, "DragSurfaceFixture");
            dragFixture.SetActive(false);
            var dragManager = dragFixture.AddComponent<ARPlaneManager>();
            var tableId = new TrackableId(780, 1);
            var table = CreateTrackedPlane(dragFixture, dragManager, tableId, new[]
            {
                new Vector2(-0.3f, -0.3f), new Vector2(0.3f, -0.3f),
                new Vector2(0.3f, 0.3f), new Vector2(-0.3f, 0.3f)
            });
            var lower = CreateTrackedPlane(dragFixture, dragManager, new TrackableId(780, 2), new[]
            {
                new Vector2(-4f, -4f), new Vector2(4f, -4f),
                new Vector2(4f, 4f), new Vector2(-4f, 4f)
            });
            lower.transform.position = Vector3.down;
            try
            {
                controller.arPlaneManager = dragManager;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 1f, -1f), Quaternion.Euler(45f, 0f, 0f));
                Set(controller, "dragStartPos", Vector3.up * controller.surfaceContactOffset);
                bool DragHit(Vector3 target)
                {
                    Vector3 screenTarget = camera.WorldToScreenPoint(target);
                    object[] arguments = { new Vector2(screenTarget.x, screenTarget.y), null, null };
                    return (bool)typeof(TapToPlaceController).GetMethod("TryGetDragSurface", PrivateInstance).Invoke(controller, arguments);
                }
                Require(DragHit(Vector3.zero), "A drag must still move on the original measured tabletop.");
                Require(!DragHit(Vector3.right * 0.5f), "A drag outside the table polygon must not hit the lower floor instead.");
                var levelTransition = (ARSupportPolicy.LevelTransition)Get(controller, "dragLevelTransition");
                Set(levelTransition, "startedAt", Time.unscaledTime - ARSupportPolicy.LevelTransition.HoldDuration - 0.01f);
                Require(DragHit(Vector3.right * 0.5f), "A stable deliberate hold over the measured lower floor must allow changing support levels.");
                levelTransition.Begin(controller.surfaceContactOffset);
                SetPlaneTracking(table, tableId, TrackingState.Limited, PlaneAlignment.HorizontalUp);
                Require(!DragHit(Vector3.zero), "Losing tabletop tracking must freeze the drag, not jump to the tracked floor.");
            }
            finally
            {
                controller.arPlaneManager = originalManager;
                camera.transform.SetPositionAndRotation(oldCameraPosition, oldCameraRotation);
            }
            results.Add("PASS: drag rays reject accidental floor jumps but allow a stable deliberate hold over the lower measured floor");

            controller.StartPreview("Test");
            Set(controller, "placementRequestHasSurface", true);
            Set(controller, "placementRequestIsDepth", true);
            Set(controller, "placementRequestPlaneId", TrackableId.invalidId);
            Set(controller, "placementRequestDepthPose", new Pose(new Vector3(0.2f, 0.4f, 1f), Quaternion.identity));
            Set(controller, "placementRequestExpiresAt", Time.unscaledTime + 1f);
            Require(!(bool)typeof(TapToPlaceController).GetMethod("CanCreateMeasuredSurfaceAnchor", PrivateInstance)
                    .Invoke(controller, new object[] { TrackableId.invalidId, true }),
                "Marking a request as depth cannot authorize placement without a real depth/anchor backend.");
            Call(controller, "TryPlaceObject", new Pose(Vector3.forward, Quaternion.identity), TrackableId.invalidId);
            Require(controller.placedObjectsHistory.Count == 0 && !(bool)Get(controller, "placementPending"),
                "Depth-only placement must still wait for a genuine native anchor, not a fake empty Transform.");
            controller.ClearPreview();
            Require(!(bool)Get(controller, "placementRequestIsDepth") &&
                    !(bool)Get(controller, "placementRequestHasSurface") &&
                    !(bool)Get(controller, "previewSurfaceIsDepth"),
                "Cancel/selection replacement must discard both depth source and its accepted world pose.");
            results.Add("PASS: depth requests cannot bypass native backend gates and cancellation clears their source state");

            sessionStateField.SetValue(null, ARSessionState.SessionTracking);
            controller.StartPreview("Test");
            preview = (GameObject)Get(controller, "previewAnchor");
            Set(input, "<IsHandVisible>k__BackingField", true);
            Set(input, "<IsPointerOverUI>k__BackingField", false);
            Set(input, "<PinchConsumedByUI>k__BackingField", false);
            foreach (Vector2 viewport in new[] { new Vector2(0.12f, 0.8f), new Vector2(0.85f, 0.15f) })
            {
                Set(input, "<CurrentScreenPos>k__BackingField", new Vector2(viewport.x * Screen.width, viewport.y * Screen.height));
                Call(controller, "ShowUngroundedPreview");
                Vector3 actual = camera.WorldToViewportPoint(preview.transform.TransformPoint(
                    (Vector3)Get(controller, "previewBoundsLocalCenter")));
                Require(Vector2.Distance(new Vector2(actual.x, actual.y), viewport) < 0.001f &&
                        !(bool)Get(controller, "canPlace") && controller.placedObjectsHistory.Count == 0,
                    "An unplaced ghost must follow the live cursor immediately outside the old central clamp, without authorizing an unmeasured placement.");
            }
            controller.ClearPreview();
            results.Add("PASS: unmeasured hand preview follows the whole-screen cursor without central clamp or extra world-space lag");

            // Cover the shipped models too: a cube cannot expose prefab bounds,
            // rotated renderer or real Android portrait framing regressions.
            sessionStateField.SetValue(null, ARSessionState.SessionInitializing);
            Set(input, "<IsHandVisible>k__BackingField", false);
            camera.aspect = 1080f / 2400f;
            camera.fieldOfView = 60f;
            camera.ResetProjectionMatrix();
            camera.transform.SetPositionAndRotation(new Vector3(1f, 1.2f, -2f), Quaternion.Euler(48f, 30f, 0f));
            foreach (string prefabPath in new[]
            {
                "Assets/Prefabs/Components/lamp.prefab",
                AssetDatabase.GUIDToAssetPath("9c16deacc8f5f8145b4564b239f3889a"),
                AssetDatabase.GUIDToAssetPath("02be59721dd2011488715b0c07fc4330")
            })
            {
                GameObject shippedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Require(shippedPrefab != null, "Missing real component prefab: " + prefabPath);
                controller.componentPrefabs = new[] { shippedPrefab };
                controller.componentNames = new[] { "Shipped" };
                controller.scaleMultiplier = 0.03f;
                controller.StartPreview("Shipped");
                var shippedVisual = (GameObject)Get(controller, "previewVisual");
                RequirePreviewFramed(camera, shippedVisual);
                Require(shippedVisual.transform.localScale == Vector3.one * 0.03f &&
                        !(bool)Get(controller, "canPlace"),
                    "A real prefab must be visible without resizing it or authorizing an unmeasured placement.");
                controller.ClearPreview();
            }
            results.Add("PASS: shipped lamp, battery and voltmeter stay visibly framed at physical scale on 1080x2400 startup");

            ARCameraCoordinateRegressionChecks.Run(results);
            MeasuredDepthSurfaceRegressionChecks.Run(results);
            ARDragUsabilityRegressionChecks.Run(results);
            ARContactRegressionChecks.Run(results);
            ARSurfaceGuidanceRegressionChecks.Run(results);
            ARSupportAndStabilityRegressionChecks.Run(results);

            string report = string.Join(Environment.NewLine, results);
            WriteReport("ar-placement-checks.txt", "ARPlacementRegressionChecks.txt", report);
            Debug.Log("[ARPlacementRegressionChecks] " + results.Count + " checks passed.\n" + report);
        }
        catch (Exception ex)
        {
            WriteReport("ar-placement-checks.txt", "ARPlacementRegressionChecks.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            throw;
        }
        finally
        {
            sessionStateField.SetValue(null, originalSessionState);
            var controllers = root.GetComponentsInChildren<TapToPlaceController>(true);
            foreach (var controller in controllers) controller.ClearPreview();
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static Vector2 RequirePreviewFramed(Camera camera, GameObject visual)
    {
        Bounds bounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
        {
            if (renderer is LineRenderer) continue;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        Require(hasBounds, "The preview must contain visible model geometry.");
        Vector2 min = Vector2.one * float.PositiveInfinity;
        Vector2 max = Vector2.one * float.NegativeInfinity;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 viewport = camera.WorldToViewportPoint(bounds.center +
                Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
            Require(viewport.z > camera.nearClipPlane && viewport.x >= 0.02f && viewport.x <= 0.98f &&
                    viewport.y >= 0.02f && viewport.y <= 0.98f,
                "The entire startup preview must remain inside the camera frustum, including after camera initialization.");
            min = Vector2.Min(min, viewport);
            max = Vector2.Max(max, viewport);
        }
        Vector2 footprint = max - min;
        Require(Mathf.Max(footprint.x, footprint.y) >= 0.15f,
            "The startup preview must be clearly visible, not a tiny object far in front of the camera.");
        return footprint;
    }

    private static void WriteReport(string temporaryName, string persistentName, string contents)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string temporaryDirectory = Path.Combine(projectRoot, "Temp");
        string persistentDirectory = Path.Combine(projectRoot, "Logs");
        Directory.CreateDirectory(temporaryDirectory);
        Directory.CreateDirectory(persistentDirectory);
        File.WriteAllText(Path.Combine(temporaryDirectory, temporaryName), contents);
        File.WriteAllText(Path.Combine(persistentDirectory, persistentName), contents);
    }

    private static void RequireNoPlacementRequest(TapToPlaceController controller, string message)
    {
        Require(!(bool)Get(controller, "placementRequestAwaitingSurface") &&
                !(bool)Get(controller, "placementRequestHasSurface") &&
                float.IsNegativeInfinity((float)Get(controller, "placementRequestExpiresAt")), message);
    }

    // Model only the managed data AR Foundation receives from its provider. These
    // fixtures cannot create native anchors and never claim to test device tracking.
    private static ARPlane CreateTrackedPlane(GameObject root, ARPlaneManager manager, TrackableId id,
        Vector2[] polygon, TrackingState trackingState = TrackingState.Tracking,
        PlaneAlignment alignment = PlaneAlignment.HorizontalUp)
    {
        var plane = Child(root, "TrackedPlane_" + id).AddComponent<ARPlane>();
        SetPlaneTracking(plane, id, trackingState, alignment);
        Set(plane, "m_Boundary", new NativeArray<Vector2>(polygon, Allocator.Persistent));
        PlaneDictionary(manager).Add(id, plane);
        return plane;
    }

    private static void SetPlaneTracking(ARPlane plane, TrackableId id, TrackingState trackingState,
        PlaneAlignment alignment = PlaneAlignment.HorizontalUp)
    {
        var data = new BoundedPlane(id, TrackableId.invalidId, Pose.identity, Vector2.zero,
            new Vector2(2f, 2f), alignment, trackingState, IntPtr.Zero, PlaneClassifications.None);
        typeof(ARTrackable<BoundedPlane, ARPlane>).GetMethod("SetSessionRelativeData", PrivateInstance)
            .Invoke(plane, new object[] { data });
    }

    private static Dictionary<TrackableId, ARPlane> PlaneDictionary(ARPlaneManager manager)
    {
        for (Type type = manager.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField("m_Trackables", PrivateInstance | BindingFlags.DeclaredOnly);
            if (field != null) return (Dictionary<TrackableId, ARPlane>)field.GetValue(manager);
        }
        throw new MissingFieldException("ARPlaneManager trackable dictionary was not found.");
    }

    private static bool CaptureSurface(TapToPlaceController controller, Pose pose, TrackableId id) =>
        (bool)typeof(TapToPlaceController).GetMethod("TryCapturePlacementRequestSurface", PrivateInstance)
            .Invoke(controller, new object[] { pose, id });

    private static bool ResolveSurface(TapToPlaceController controller, out Pose pose, out TrackableId id)
    {
        var args = new object[] { Pose.identity, TrackableId.invalidId };
        bool success = (bool)typeof(TapToPlaceController).GetMethod("TryResolvePlacementRequestSurface", PrivateInstance)
            .Invoke(controller, args);
        pose = (Pose)args[0];
        id = (TrackableId)args[1];
        return success;
    }

    private static GameObject Child(GameObject parent, string name, params Type[] components)
    {
        var child = new GameObject(name, components);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static void HandFrames(ButtonInteractor input, float distance, int count = 2)
    {
        for (int i = 0; i < count; i++)
        {
            Set(input, "isHandVisible", true);
            Set(input, "lastHandResultTicks", System.Diagnostics.Stopwatch.GetTimestamp());
            Set(input, "rawDistance", distance);
            Call(input, "Update");
        }
    }

    private static object Get(object instance, string name) => instance.GetType().GetField(name, PrivateInstance).GetValue(instance);
    private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, PrivateInstance).SetValue(instance, value);
    private static void Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, PrivateInstance).Invoke(instance, args);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
