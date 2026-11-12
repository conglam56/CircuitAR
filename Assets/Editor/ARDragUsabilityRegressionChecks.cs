using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>Managed drag/input checks; no AR session or native anchors are started.</summary>
internal static class ARDragUsabilityRegressionChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    public static void Run(List<string> results)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Components" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
                Require(filter.sharedMesh == null || filter.sharedMesh.isReadable,
                    $"Pinch picking needs CPU access to {filter.sharedMesh?.name} in {path}.");
        }
        results.Add("PASS: placed component models expose the mesh data needed for pinch picking");

        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("ARDragUsabilityRegressionFixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        // Keep overlap checks away from the user's open scene geometry.
        root.transform.position = new Vector3(0f, 100f, 0f);
        root.SetActive(false);
        var stateField = typeof(ARSession).GetField("s_State", PrivateStatic);
        var originalState = stateField.GetValue(null);
        TapToPlaceController controller = null;
        try
        {
            var camera = Child(root, "FixtureCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 20f;
            camera.fieldOfView = 60f;
            camera.pixelRect = new Rect(0f, 0f, 1000f, 2000f);
            camera.aspect = 0.5f;

            var input = Child(root, "FixtureInput").AddComponent<ButtonInteractor>();
            input.enabled = false;
            input.interactiveButtons = new List<RectTransform>();
            controller = Child(root, "FixtureController").AddComponent<TapToPlaceController>();
            controller.enabled = false;
            controller.buttonInteractor = input;
            Set(controller, "mainCamera", camera);
            var wire = Child(root, "FixtureWireMode").AddComponent<WireConnectionController>();
            wire.enabled = false;
            controller.wireConnectionController = wire;
            var near = Cube(root, "Placed_NearDragTarget", new Vector3(0f, 0f, 2f));
            var far = Cube(root, "Placed_FarDragTarget", new Vector3(0f, 0f, 3f));
            var terminal = Child(Child(near, "Visual"), "TerminalChild");
            terminal.tag = "Terminal";
            root.SetActive(true);
            stateField.SetValue(null, ARSessionState.SessionTracking);

            Require((GameObject)Static("FindPlacedRoot", terminal.transform) == near,
                "A Terminal child must resolve to its placed component in Move mode.");
            Require(Static("FindPlacedRoot", root.transform) == null,
                "Unrelated scene objects must not resolve to a placed component.");
            results.Add("PASS: Move-mode Terminal hits resolve to their placed component instead of a dead zone");

            bool Reuse(TrackingState state, bool pending, Vector3 point) =>
                (bool)Static("CanReuseDragAnchor", state, pending, Vector3.zero, point);
            Require(Reuse(TrackingState.Tracking, false, Vector3.zero) &&
                    Reuse(TrackingState.Tracking, false, Vector3.right),
                "A tracked non-pending native anchor must support nearby moves up to one metre.");
            Require(!Reuse(TrackingState.Tracking, true, Vector3.zero) &&
                    !Reuse(TrackingState.Limited, false, Vector3.zero) &&
                    !Reuse(TrackingState.None, false, Vector3.zero) &&
                    !Reuse(TrackingState.Tracking, false, Vector3.right * 1.01f),
                "Pending, untracked or distant anchors must not be reused.");
            results.Add("PASS: nearby drag release reuses only tracked non-pending anchors within one metre");

            controller.placedObjectsHistory.Add(near);
            Rect rectangle = ProjectedRect(camera, near.GetComponent<Renderer>().bounds);
            float margin = Mathf.Max(22f, camera.pixelHeight * 0.022f);
            Vector2 nearEdge = new Vector2(rectangle.xMax + margin * 0.6f, rectangle.center.y);
            Require(Nearby(controller, nearEdge) == near,
                "A tap just outside the rendered component must be accepted within the tap margin.");
            Require(Nearby(controller, new Vector2(rectangle.xMax + margin + 3f, rectangle.center.y)) == null,
                "A tap beyond the tap margin must not select the component.");
            results.Add("PASS: handheld taps retain their screen-space selection margin");

            controller.placedObjectsHistory.Insert(0, far);
            Vector2 center = camera.WorldToScreenPoint(near.transform.position);
            Require(Nearby(controller, center) == near,
                "Overlapping projected targets must prefer the nearer component, independent of history order.");
            far.SetActive(false);
            near.GetComponent<Renderer>().enabled = false;
            Require(Nearby(controller, center) == null,
                "Disabled renderers and inactive objects must not remain invisible drag targets.");
            near.GetComponent<Renderer>().enabled = true;
            near.transform.localPosition = Vector3.back * 2f;
            Require(Nearby(controller, center) == null,
                "A component behind the camera must not be selected from a projected rectangle.");
            near.transform.localPosition = Vector3.forward * 2f;
            results.Add("PASS: nearby picking prefers visible nearest-depth components and ignores hidden/behind-camera targets");

            CheckHeadsetPinch(controller, camera, near, results);
            results.Add("PASS: headset pinches grab only where both eyes hit the same component and clear one-eye edge hits");

            SetInput(input, visible: true, pinching: true, justPinched: true);
            Set(input, "<CurrentScreenPos>k__BackingField", center);
            Set(controller, "worldPinchArmed", false);
            Set(controller, "cooldownTimer", 0f);
            Call(controller, "HandleDragAndDrop");
            Require(controller.IsDragging && controller.DraggedObject == near,
                "A fresh world pinch on the mesh must begin a drag even with disabled colliders and the obsolete arming latch false.");
            Require(Vector2.Distance((Vector2)Get(controller, "dragPointerOffset"), Vector2.zero) < 0.1f,
                "Beginning a centered grab must preserve its original projected base point.");
            results.Add("PASS: a fresh pinch on the visible mesh starts a drag without an additional arming gesture");

            Vector3 stablePosition = near.transform.position;
            SetInput(input, visible: false, pinching: false, justPinched: false);
            near.transform.position = stablePosition + Vector3.right * 0.1f;
            Call(controller, "HandleDragAndDrop");
            Require(controller.IsDragging && near.transform.position == stablePosition,
                "A briefly occluded hand must freeze the last measured position without dropping or moving the component.");
            SetInput(input, visible: true, pinching: true, justPinched: false);
            Call(controller, "HandleDragAndDrop");
            Require(controller.IsDragging && float.IsNegativeInfinity((float)Get(controller, "dragHandLostAt")),
                "Fresh held-hand input must resume the same drag and clear its hand-loss deadline.");
            SetInput(input, visible: false, pinching: false, justPinched: false);
            Set(controller, "dragHandLostAt", Time.unscaledTime - 0.36f);
            Call(controller, "HandleDragAndDrop");
            Require(!controller.IsDragging && controller.DraggedObject == null,
                "A genuinely lost hand must end the drag after its finite grace period.");
            results.Add("PASS: transient hand loss freezes/resumes a drag while sustained loss ends it without stale-pointer motion");

            SetInput(input, visible: true, pinching: true, justPinched: true);
            Call(controller, "HandleDragAndDrop");
            Require(controller.IsDragging, "The release-grace fixture must begin a fresh drag.");
            SetInput(input, visible: true, pinching: false, justPinched: false);
            // Exercise exactly 100 ms accumulated release without waiting or
            // depending on the Editor's variable last-frame delta time.
            Set(controller, "unpinchGraceTimer", 0.1f - Time.deltaTime);
            Call(controller, "HandleDragAndDrop");
            Require(controller.IsDragging && near.transform.position == stablePosition &&
                    Mathf.Abs((float)Get(controller, "unpinchGraceTimer") - 0.1f) < 0.001f,
                "A short visible-hand unpinch must preserve the drag and its measured world position.");
            SetInput(input, visible: true, pinching: true, justPinched: false);
            Call(controller, "HandleDragAndDrop");
            Require(controller.IsDragging && Mathf.Approximately((float)Get(controller, "unpinchGraceTimer"), 0f),
                "Reclosing the pinch during release grace must resume the same drag and reset its release timer.");
            SetInput(input, visible: true, pinching: false, justPinched: false);
            Set(controller, "unpinchGraceTimer", 0.23f);
            Call(controller, "HandleDragAndDrop");
            Require(!controller.IsDragging && controller.DraggedObject == null && near.transform.position == stablePosition,
                "A stable visible-hand release beyond 220 ms must end the drag without moving the component.");
            results.Add("PASS: short unpinch survives/recloses while stable release beyond 220 ms ends the drag");

            SetInput(input, visible: true, pinching: true, justPinched: true);
            wire.isWireMode = true;
            Call(controller, "HandleDragAndDrop");
            Require(!controller.IsDragging, "Wire mode must continue to block component movement.");
            wire.isWireMode = false;
            Set(input, "<PinchConsumedByUI>k__BackingField", true);
            Call(controller, "HandleDragAndDrop");
            Require(!controller.IsDragging && controller.SelectedComponent == near,
                "A pinch started on UI must preserve the current selection without becoming a component grab.");
            results.Add("PASS: Wire mode and UI-owned pinch gestures still cannot begin component dragging");

            CheckEmptyPinchDeselect(controller, near, nearEdge);
            results.Add("PASS: a handheld pinch in the tap margin clears selection without a detected plane");

            near.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Rect rotatedBounds = ProjectedRect(camera, near.GetComponent<Renderer>().bounds);
            Vector2 emptyCorner = rotatedBounds.max - new Vector2(3f, 3f);
            Require(Nearby(controller, emptyCorner) == near,
                "The test point must be inside the rotated component's projected bounds.");
            controller.SelectComponent(near);
            CheckEmptyPinchDeselect(controller, near, emptyCorner);
            results.Add("PASS: a pinch inside projected bounds but outside the actual mesh does not grab the component");
            CheckTwoHands(controller, camera, near, far, results);
        }
        finally
        {
            stateField.SetValue(null, originalState);
            if (controller != null)
            {
                controller.ClearSelectedComponent();
                foreach (string field in new[] { "selectionHighlightMat", "selectionInvalidMat" })
                {
                    var material = Get(controller, field) as Material;
                    if (material != null) UnityEngine.Object.DestroyImmediate(material);
                }
            }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CheckTwoHands(TapToPlaceController controller, Camera camera,
        GameObject leftObject, GameObject rightObject, List<string> results)
    {
        var primary = controller.buttonInteractor;
        var secondary = Child(controller.gameObject, "SecondHandFixture").AddComponent<ButtonInteractor>();
        secondary.interactiveButtons = new List<RectTransform>();
        controller.SetSecondHand(secondary);
        leftObject.transform.localPosition = new Vector3(-0.3f, 0f, 2f);
        rightObject.transform.localPosition = new Vector3(0.3f, 0f, 2f);
        leftObject.transform.localRotation = Quaternion.identity;
        rightObject.SetActive(true);
        Vector2 leftPoint = camera.WorldToScreenPoint(leftObject.transform.position);
        Vector2 rightPoint = camera.WorldToScreenPoint(rightObject.transform.position);
        var operation = typeof(TapToPlaceController).GetNestedType("HandOperation", BindingFlags.NonPublic);
        void Second(string name) => Call(controller, "ProcessSecondHand", Enum.Parse(operation, name));
        try
        {
            Set(controller, "cooldownTimer", 0f);
            SetInput(primary, true, true, true);
            Set(primary, "<CurrentScreenPos>k__BackingField", leftPoint);
            Call(controller, "HandleDragAndDrop");
            SetInput(secondary, true, true, true);
            Set(secondary, "<CurrentScreenPos>k__BackingField", rightPoint);
            Second("Drag");
            Require(controller.IsObjectBeingDragged(leftObject) && controller.IsObjectBeingDragged(rightObject) &&
                controller.buttonInteractor == primary,
                "Both hands must retain independent object ownership and restore the primary context.");
            Require(leftObject.GetComponent<Renderer>().sharedMaterial == rightObject.GetComponent<Renderer>().sharedMaterial,
                "Both held objects must retain their selection feedback.");
            results.Add("PASS: two hands hold separate components simultaneously with separate drag contexts and highlights");

            SetInput(secondary, false, false, false);
            Second("Drag");
            object state = Get(controller, "secondDrag");
            state.GetType().GetField("dragHandLostAt").SetValue(state, Time.unscaledTime - 0.36f);
            Second("Drag");
            Require(controller.IsObjectBeingDragged(leftObject) && !controller.IsObjectBeingDragged(rightObject),
                "Losing the second hand must release only its own component.");
            results.Add("PASS: losing one hand releases its component without dropping the other hand's object");

            SetInput(secondary, true, true, true);
            Set(secondary, "<CurrentScreenPos>k__BackingField", leftPoint);
            Second("Drag");
            Require(controller.IsObjectBeingDragged(leftObject) && !(bool)state.GetType().GetField("isDragging").GetValue(state),
                "A second hand must not steal the first hand's component.");
            Set(secondary, "<PinchConsumedByUI>k__BackingField", true);
            Second("Drag");
            controller.wireConnectionController.isWireMode = true;
            SetInput(primary, true, true, false);
            Call(controller, "HandleDragAndDrop");
            Require(controller.IsObjectBeingDragged(leftObject),
                "UI input and entering wire mode with the second hand must preserve the first hand's drag.");
            results.Add("PASS: a held component cannot be stolen and survives the other hand using UI or entering wire mode");

            Set(controller, "previewInput", secondary);
            SetInput(primary, true, false, false);
            SetInput(secondary, true, true, false);
            Require((bool)typeof(TapToPlaceController).GetMethod("IsWorldPinchHeld", PrivateInstance).Invoke(controller, null),
                "Async placement must continue to read the preview owner's gesture after restoring the primary context.");
            Set(controller, "previewInput", null);
            results.Add("PASS: pending placement uses its owning hand independently of the active drag context");
        }
        finally
        {
            controller.wireConnectionController.isWireMode = false;
            controller.CancelPlacement();
            controller.SetSecondHand(null);
            controller.ClearSelectedComponent();
            UnityEngine.Object.DestroyImmediate(secondary.gameObject);
        }
    }

    private static void CheckHeadsetPinch(TapToPlaceController controller, Camera camera, GameObject near,
        List<string> results)
    {
        var headset = camera.gameObject.AddComponent<PhoneHeadsetAR>();
        var landscape = new RenderTexture(960, 432, 24);
        Rect pixelRect = camera.pixelRect;
        float aspect = camera.aspect;
        Vector3 position = near.transform.localPosition;
        Vector3 scale = near.transform.localScale;
        camera.targetTexture = landscape;
        camera.aspect = (float)landscape.width / landscape.height;
        near.transform.localPosition = Vector3.forward * 0.38f;
        near.transform.localScale = new Vector3(0.1f, 0.025f, 0.025f);
        var stereo = new PhoneHeadsetStereo(camera);
        try
        {
            Set(headset, "stereo", stereo);
            var eyeCameras = (Camera[])Get(stereo, "eyes");
            Require(stereo.SharesEyeView && ReferenceEquals(stereo.LeftTexture, stereo.RightTexture) &&
                ReferenceEquals(stereo.Left, stereo.Right) && eyeCameras[0].enabled && !eyeCameras[1].enabled,
                "Camera-aligned phone passthrough must draw identical virtual content only once and share the result across both lenses.");
            results.Add("PASS: phone passthrough shares one rendered virtual view and one target between both eye composites");
            Set(headset, "cameraAlignedProjection", false); // Retain coverage of the optional geometric stereo helpers.
            stereo.Prepare();
            Require(!stereo.SharesEyeView && stereo.LeftTexture != stereo.RightTexture && eyeCameras[0].enabled && eyeCameras[1].enabled,
                "Geometric stereo must retain two distinct render targets and eye cameras rather than sharing incompatible views.");
            Set(headset, "eyeSeparation", 0.064f);
            Set(headset, "convergenceDistance", 1.2f);
            Vector3 convergencePoint = camera.transform.TransformPoint(new Vector3(0.08f, 0.03f, 1.2f));
            Require(PhoneHeadsetStereo.TryProjectIntoEye(camera, convergencePoint, false, out Vector2 leftAtConvergence) &&
                PhoneHeadsetStereo.TryProjectIntoEye(camera, convergencePoint, true, out Vector2 rightAtConvergence) &&
                Vector2.Distance(leftAtConvergence, rightAtConvergence) < 0.01f,
                "Both eyes must meet at the configured convergence depth without vertical disparity.");
            Require(Mathf.Approximately(headset.ViewScale, 1f), "The default headset image must use the reduced view scale.");
            Set(headset, "eyeSeparation", 0.060f);
            Set(headset, "convergenceDistance", PhoneHeadsetStereo.ConvergenceDistance);
            results.Add("PASS: headset eye projections honor configurable separation and convergence and default to a smaller image");
            Require(stereo.IsReady && camera.cullingMask == 0,
                "The fixture must exercise headset rendering with the source camera's object layers masked out.");
            Matrix4x4 fitted = PhoneHeadsetStereo.MakeEyeProjection(camera.projectionMatrix, 0f, false);
            float focalPixelsX = landscape.width * 0.5f * fitted.m00;
            float focalPixelsY = landscape.height * fitted.m11;
            Require(Mathf.Abs(focalPixelsX - focalPixelsY) < 0.01f &&
                Mathf.Abs(fitted.m11 * PhoneHeadsetStereo.SceneFieldScale - camera.projectionMatrix.m11) < 0.0001f,
                "The widened SBS view must reduce magnification equally in both axes without changing eye-frame aspect.");
            results.Add("PASS: full-height SBS eye coverage preserves pixel aspect without the shrunken letterbox eye frame");
            var input = controller.buttonInteractor;
            input.SetHeadsetCamera(camera);
            Require(input.minStablePinchFrames == 1, "Headset gestures must not wait for a second slow inference result.");
            Vector2 center = camera.WorldToScreenPoint(near.transform.position);
            SetInput(input, visible: true, pinching: true, justPinched: true);
            Set(input, "<CurrentScreenPos>k__BackingField", center);
            Call(controller, "HandleDragAndDrop");
            Require(controller.IsDragging && controller.SelectedComponent == near &&
                (int)Get(controller, "dragEye") >= 0 &&
                near.transform.localPosition == Vector3.forward * 0.38f &&
                near.GetComponent<Renderer>().sharedMaterial == Get(controller, "selectionHighlightMat") as Material,
                "A direct pinch in the shared silhouette must grab and highlight the component without jumping.");
            Call(controller, "ReleaseDraggedObject");
            CheckEmptyPinchDeselect(controller, near, center + Vector2.up * 23f);

            for (int eye = 0; eye < 2; eye++)
            {
                Vector3 edge = near.transform.position + Vector3.right * (eye == 0 ? 0.04f : -0.04f);
                Require(PhoneHeadsetStereo.TryProjectIntoEye(camera, edge, eye == 1, out Vector2 edgePointer),
                    "The edge point must be visible through the corresponding lens.");
                controller.SelectComponent(near);
                CheckEmptyPinchDeselect(controller, near, edgePointer);
            }
            Set(headset, "cameraAlignedProjection", true);
            stereo.Prepare();
            Require(stereo.SharesEyeView && ReferenceEquals(stereo.LeftTexture, stereo.RightTexture) && !eyeCameras[1].enabled,
                "Returning to camera-aligned passthrough must release the second target and stop drawing the duplicate camera.");
            results.Add("PASS: switching projection modes preserves distinct geometric eyes and restores the single-draw passthrough path");
            foreach (float depth in new[] { 0.3f, 0.85f, 1.5f })
            {
                Vector3 contact = camera.transform.TransformPoint(new Vector3(0.02f, 0.01f, depth));
                Vector2 sourcePoint = camera.WorldToScreenPoint(contact);
                Require(PhoneHeadsetStereo.TryProjectIntoEye(camera, contact, false, out Vector2 left) &&
                    PhoneHeadsetStereo.TryProjectIntoEye(camera, contact, true, out Vector2 right) &&
                    Vector2.Distance(left, sourcePoint) < 0.01f && Vector2.Distance(right, sourcePoint) < 0.01f,
                    "Phone passthrough must keep both virtual-eye projections registered to the single source camera at every contact depth.");
            }
            results.Add("PASS: phone passthrough aligns virtual content with the real camera in both eyes at near and far support depths");
            CheckHeadsetMenu(headset, controller, camera, near, results);
            input.SetHeadsetCamera(null);
        }
        finally
        {
            stereo.Dispose();
            camera.targetTexture = null;
            camera.pixelRect = pixelRect;
            camera.aspect = aspect;
            near.transform.localPosition = position;
            near.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(landscape);
            UnityEngine.Object.DestroyImmediate(headset);
        }
    }

    private static void CheckHeadsetMenu(PhoneHeadsetAR headset, TapToPlaceController controller, Camera camera,
        GameObject component, List<string> results)
    {
        ButtonInteractor input = controller.buttonInteractor;
        Set(headset, "arCamera", camera);
        Set(headset, "placement", controller);
        Set(headset, "wires", controller.wireConnectionController);
        Set(headset, "hand", input);
        Set(headset, "<IsActive>k__BackingField", true);
        try
        {
            var focusObject = new GameObject("HeadsetFocusFixture");
            focusObject.SetActive(false);
            focusObject.transform.SetParent(camera.transform, false);
            var focus = focusObject.AddComponent<ARCameraManager>();
            focus.autoFocusRequested = false;
            Set(headset, "focusCamera", focus);
            Call(headset, "OnApplicationFocus", true);
            Require(focus.autoFocusRequested, "Returning to headset mode must request camera autofocus.");
            Set(headset, "focusCamera", null);
            UnityEngine.Object.DestroyImmediate(focusObject);
            results.Add("PASS: headset focus recovery requests autofocus on the physical AR camera");
            Call(headset, "CreatePanel");
            var panel = (Canvas)Get(headset, "panel");
            SceneManager.MoveGameObjectToScene(panel.gameObject, camera.gameObject.scene);
            Require(panel.transform.Find("MENU").Find("Căn trước mặt") == null,
                "The headset home page must not include a recenter button.");
            const string alignmentKey = "PhoneHeadset.ImageOffset";
            bool hadAlignment = PlayerPrefs.HasKey(alignmentKey);
            float savedAlignment = PlayerPrefs.GetFloat(alignmentKey);
            const string scaleKey = "PhoneHeadset.ViewScale";
            bool hadScale = PlayerPrefs.HasKey(scaleKey);
            float savedScale = PlayerPrefs.GetFloat(scaleKey);
            try
            {
                panel.transform.Find("MENU").Find("Chỉnh kính").GetComponent<Button>().onClick.Invoke();
                Call(headset, "LateUpdate");
                var alignment = (RectTransform)Get(headset, "currentPage");
                Require(alignment.name == "CHỈNH KÍNH", "The headset alignment page must be reachable from the menu.");
                alignment.Find("Hai ảnh gần nhau").GetComponent<Button>().onClick.Invoke();
                Call(headset, "LateUpdate");
                Require(Mathf.Abs(headset.ImageOffset + 0.01f) < 0.0001f &&
                    Mathf.Abs(PlayerPrefs.GetFloat(alignmentKey) - headset.ImageOffset) < 0.0001f,
                    "The inward alignment step must update and remember the image offset.");
                Require(alignment.Find("Cảnh nhỏ hơn") == null && alignment.Find("Cảnh lớn hơn") == null,
                    "Manual scene sizing must be removed from headset controls.");
                PlayerPrefs.SetFloat(scaleKey, 0.65f); // Old installations may still contain this setting.
                var reopened = Child(controller.gameObject, "ReopenedHeadsetPreferences").AddComponent<PhoneHeadsetAR>();
                try
                {
                    Call(headset, "OnApplicationPause", true);
                    Call(headset, "OnApplicationFocus", false);
                    Call(reopened, "RestoreHeadsetPreferences");
                    Require(Mathf.Abs(reopened.ViewScale - headset.ViewScale) < 0.0001f &&
                        Mathf.Abs(reopened.ImageOffset - headset.ImageOffset) < 0.0001f,
                        "A new headset session must restore alignment even before the old session exits VR.");
                    results.Add("PASS: headset preferences restore in a new session after backgrounding without exiting VR");
                }
                finally { UnityEngine.Object.DestroyImmediate(reopened.gameObject); }
                results.Add("PASS: automatic view fitting ignores obsolete scale preferences and manual size controls are absent");
                alignment.Find("Hai ảnh xa nhau").GetComponent<Button>().onClick.Invoke();
                Call(headset, "LateUpdate");
                Require(Mathf.Abs(headset.ImageOffset) < 0.0001f,
                    "The outward alignment step must reverse the inward step.");
                alignment.Find("Quay lại").GetComponent<Button>().onClick.Invoke();
                Call(headset, "LateUpdate");
                results.Add("PASS: headset image alignment can be adjusted inward and outward on-device and is remembered");
            }
            finally
            {
                if (hadAlignment) PlayerPrefs.SetFloat(alignmentKey, savedAlignment);
                else PlayerPrefs.DeleteKey(alignmentKey);
                if (hadScale) PlayerPrefs.SetFloat(scaleKey, savedScale);
                else PlayerPrefs.DeleteKey(scaleKey);
                PlayerPrefs.Save();
            }
            var toolbar = (RectTransform)Get(headset, "componentToolbar");
            var rotate = ((RectTransform)Get(headset, "rotateAction")).GetComponent<Button>();
            var delete = ((RectTransform)Get(headset, "deleteAction")).GetComponent<Button>();
            Require(!toolbar.gameObject.activeSelf && toolbar.parent == panel.transform,
                "The headset must have one contextual toolbar, independent of the menu pages.");
            controller.SelectComponent(component);
            Set(controller, "draggedObject", component);
            Set(controller, "isDragging", true);
            Call(headset, "LateUpdate");
            Require(!toolbar.gameObject.activeSelf, "The toolbar must stay hidden while moving the selected component.");
            Set(controller, "isDragging", false);
            Set(controller, "draggedObject", null);
            Call(headset, "LateUpdate");
            Require(toolbar.gameObject.activeSelf &&
                camera.WorldToViewportPoint(toolbar.position).y > camera.WorldToViewportPoint(component.transform.position).y,
                "Releasing a selected component must show the toolbar above it.");

            Vector3 componentPosition = component.transform.position;
            component.transform.position = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.83f, 0.6f));
            Call(headset, "LateUpdate");
            Require(toolbar.gameObject.activeSelf && camera.WorldToViewportPoint(toolbar.position).y < 0.83f,
                "A component at the top of the view must get a reachable toolbar below or beside it.");
            component.transform.position = componentPosition;

            controller.wireConnectionController.isWireMode = true;
            Call(headset, "LateUpdate");
            Require(!toolbar.gameObject.activeSelf, "The contextual toolbar must stay hidden in wire mode.");
            controller.wireConnectionController.isWireMode = false;
            Call(headset, "LateUpdate");

            Require(PhoneHeadsetStereo.TryProjectIntoEye(camera, rotate.transform.position, false, out Vector2 rotatePoint),
                "The rotate action must be in view.");
            var hitAction = typeof(PhoneHeadsetAR).GetMethod("TryGetComponentActionAt", PrivateInstance);
            var hit = new object[] { rotatePoint, null };
            Require((bool)hitAction.Invoke(headset, hit) && (RectTransform)hit[1] == rotate.transform,
                "The world-space toolbar must accept pinch coordinates from its rendered eye projection.");
            var rotateRect = (RectTransform)rotate.transform;
            Vector3 rotateEdge = rotateRect.TransformPoint(new Vector3(rotateRect.rect.xMin - 5f, 0f, 0f));
            Require(PhoneHeadsetStereo.TryProjectIntoEye(camera, rotateEdge, true, out Vector2 rotateEdgePoint),
                "The extra rotate target must be visible through the right lens.");
            hit = new object[] { rotateEdgePoint, null };
            Require((bool)hitAction.Invoke(headset, hit) && (RectTransform)hit[1] == rotateRect,
                "A pinch just outside the rendered rotate button must hit its larger target.");
            Vector3 padding = toolbar.TransformPoint(new Vector3(0f, 50f, 0f));
            Require(PhoneHeadsetStereo.TryProjectIntoEye(camera, padding, false, out Vector2 paddingPoint),
                "The toolbar padding must be visible in the test.");
            hit = new object[] { paddingPoint, null };
            Require((bool)hitAction.Invoke(headset, hit) && hit[1] == null,
                "The toolbar padding must claim the pointer without hitting a button behind it.");
            results.Add("PASS: the headset toolbar appears near the released component, fits the lens edge and hides during wiring");

            Quaternion before = component.transform.rotation;
            Vector3 rotateScale = rotateRect.localScale;
            Set(input, "lastClickTime", Time.time);
            Call(input, "ActivatePinchedButton", rotateRect, true);
            Call(headset, "LateUpdate");
            Require(Mathf.Abs(Quaternion.Angle(before, component.transform.rotation) - 90f) < 0.1f &&
                rotateRect.localScale == rotateScale,
                "The headset Rotate button must work during the menu cooldown and keep its hit area steady.");

            delete.onClick.Invoke();
            Call(headset, "LateUpdate");
            Require(component.activeSelf, "A quick click must not bypass the delete hold.");
            var hold = delete.GetComponent<HoldToActivateButton>();
            Set(hold, "currentHoldTimer", 0.3f);
            hold.SetPinchHoverState(true, true);
            hold.SetPinchHoverState(true, false);
            Call(hold, "Update");
            Require((float)Get(hold, "currentHoldTimer") == 0f && component.activeSelf,
                "Releasing a partial delete hold must immediately clear its progress.");
            hold.onHoldComplete.Invoke();
            controller.ClearSelectedComponent();
            Call(headset, "LateUpdate");
            Require(component.activeSelf, "A queued delete must be ignored if the selection is cleared.");
            controller.SelectComponent(component);
            Call(headset, "LateUpdate");
            hold.onHoldComplete.Invoke();
            Call(headset, "LateUpdate");
            Require(!component.activeSelf && controller.SelectedComponent == null && !toolbar.gameObject.activeSelf,
                "Confirming the delete hold must remove the selected component and hide the toolbar.");
            Require(controller.UndoLastPlacedComponent() && component.activeSelf &&
                controller.UndoLastPlacedComponent() && Quaternion.Angle(before, component.transform.rotation) < 0.1f,
                "Undo must restore both a headset deletion and a headset rotation.");
            results.Add("PASS: headset Rotate and held Delete use the shared undo history and a brief pinch cannot delete");

            CheckTwoHandInventory(headset, controller, camera, results);
            var grip = (RectTransform)Get(headset, "dragHandleHitbox");
            // Menu mặc định cố định; bật kéo để kiểm tra riêng cơ chế kéo menu
            Require(grip.gameObject.activeInHierarchy, "The menu drag bar must be visible by default.");
            grip.gameObject.SetActive(true);
            panel.transform.position += camera.ViewportToWorldPoint(new Vector3(0.5f, 0.34f, 0.68f)) - grip.position;
            Vector2 GripPointer()
            {
                Vector3 gripEdge = grip.TransformPoint(new Vector3(grip.rect.xMax - 2f, 0f));
                Require(PhoneHeadsetStereo.TryProjectIntoEye(camera, gripEdge, false, out Vector2 point),
                    "The widened handle edge must be visible in the left eye.");
                Set(input, "<CurrentScreenPos>k__BackingField", point);
                return point;
            }

            var handlePointer = typeof(PhoneHeadsetAR).GetMethod("HandleMenuPointer", PrivateInstance);
            bool PointAtGrip(Vector2 point, bool pinchStarted) =>
                (bool)handlePointer.Invoke(headset, new object[] { point, pinchStarted, false, input });

            // A fresh grip must also work after startup tracking was unavailable.
            Set(headset, "menuDragNeedsRelease", true);
            Vector2 edgePointer = GripPointer();
            Require(RectTransformUtility.RectangleContainsScreenPoint(grip, edgePointer, camera),
                "The camera-aligned grip edge must match its visible source-camera hit box.");
            SetInput(input, visible: true, pinching: true, justPinched: false);
            Require(PointAtGrip(edgePointer, false) && !input.PinchConsumedByUI && !(bool)Get(headset, "isDraggingMenu"),
                "Sliding an already held pinch over the grip must not start a menu drag.");

            Set(controller, "currentPrefabToPlace", component);
            Require(PointAtGrip(edgePointer, true) && input.PinchConsumedByUI && (bool)Get(headset, "isDraggingMenu"),
                "A fresh pinch precisely on the grip must capture it immediately without repeated dwell attempts.");

            Call(headset, "LateUpdate");
            var bar = (Image)Get(headset, "dragHandleVisual");
            Require(((Color32)bar.color).Equals(new Color32(100, 220, 255, 255)),
                "The headset handle must turn cyan on the first captured frame.");
            Vector3 panelStart = panel.transform.position;
            Quaternion panelRotation = panel.transform.rotation;
            Call(headset, "MoveMenu", edgePointer + Vector2.left * 70f, 0.05f);
            Vector3 movedPanel = panel.transform.position;
            Require(Vector3.Distance(panelStart, movedPanel) > 0.01f &&
                Mathf.Abs(Vector3.Dot(movedPanel - panelStart, camera.transform.forward)) < 0.001f &&
                Quaternion.Angle(panelRotation, panel.transform.rotation) < 0.01f,
                "Dragging sideways must move the menu on its world plane without changing depth or orientation.");
            Vector3 cameraStart = camera.transform.position;
            try
            {
                camera.transform.position += camera.transform.forward * 0.18f;
                Call(headset, "MoveMenu", edgePointer + Vector2.left * 100f, 0.05f);
                Require((bool)Get(headset, "isDraggingMenu") &&
                    Mathf.Abs(((Plane)Get(headset, "menuDragPlane")).GetDistanceToPoint(panel.transform.position)) < 0.001f,
                    "A modest head translation must not interrupt dragging; the menu must stay on its captured world plane.");
            }
            finally { camera.transform.position = cameraStart; }

            SetInput(input, visible: true, pinching: false, justPinched: false);
            Set(headset, "menuReleaseTimer", 0f);
            Call(headset, "LateUpdate");
            Require((bool)Get(headset, "isDraggingMenu"), "A one-frame pinch fluctuation must not interrupt menu dragging.");
            Set(headset, "menuReleaseTimer", 0.15f);
            Call(headset, "LateUpdate");
            Require(!(bool)Get(headset, "isDraggingMenu"), "A sustained open hand must release the menu.");
            SetInput(input, visible: true, pinching: true, justPinched: false);
            Vector2 nextGrip = GripPointer();
            Require(PointAtGrip(nextGrip, true) && (bool)Get(headset, "isDraggingMenu"),
                "A quick second pinch must immediately resume menu dragging.");
            Quaternion beforeHeadRotation = camera.transform.rotation;
            camera.transform.rotation = Quaternion.Euler(0f, 15f, 0f) * beforeHeadRotation;
            Call(headset, "LateUpdate");
            Require((bool)Get(headset, "isDraggingMenu") &&
                Mathf.Abs(((Plane)Get(headset, "menuDragPlane")).GetDistanceToPoint(panel.transform.position)) < 0.001f,
                "Ordinary head movement must retain the grip and its fixed world plane.");
            Vector3 beforeMissingHand = panel.transform.position;
            SetInput(input, visible: false, pinching: true, justPinched: false);
            Set(headset, "menuHandLostAt", Time.unscaledTime - 0.1f);
            Call(headset, "LateUpdate");
            Require((bool)Get(headset, "isDraggingMenu") && panel.transform.position == beforeMissingHand,
                "A brief lost hand sample must freeze the menu without dropping the grip.");
            SetInput(input, visible: true, pinching: true, justPinched: false);
            camera.transform.rotation = beforeHeadRotation;
            Call(headset, "LateUpdate");
            Require((bool)Get(headset, "isDraggingMenu"), "The same held gesture must resume after a short occlusion.");
            results.Add("PASS: menu dragging captures immediately, tolerates ordinary head motion and resumes after brief hand loss");
            SetInput(input, visible: true, pinching: false, justPinched: false);
            Set(headset, "menuReleaseTimer", 0.15f);
            Call(headset, "LateUpdate");
            Vector3 releasedPanel = panel.transform.position;
            try
            {
                camera.transform.position += camera.transform.up * 0.2f;
                Call(headset, "ShowPage", (RectTransform)panel.transform.Find("KHO LINH KIỆN"));
                Call(headset, "LateUpdate");
                Require(panel.transform.position == releasedPanel,
                    "Changing to a taller page must keep the released menu at the same world position.");
                foreach (Vector3 side in new[] { Vector3.right, Vector3.left, Vector3.forward })
                {
                    camera.transform.position = releasedPanel + side;
                    Call(headset, "FaceMenuTowardViewer");
                    Require(panel.transform.position == releasedPanel &&
                        Vector3.Dot(panel.transform.forward, Vector3.ProjectOnPlane(releasedPanel - camera.transform.position, Vector3.up).normalized) > 0.999f,
                        "The menu must face a viewer at either side or behind it while staying in place.");
                    Quaternion beforeRoll = camera.transform.rotation;
                    camera.transform.rotation *= Quaternion.Euler(0f, 0f, 70f);
                    Call(headset, "FaceMenuTowardViewer");
                    Require(panel.transform.position == releasedPanel && Vector3.Dot(panel.transform.up, Vector3.up) > 0.999f,
                        "Rolling/removing the phone must not roll the menu with the headset.");
                    camera.transform.rotation = beforeRoll;
                }
            }
            finally { camera.transform.position = cameraStart; }
            results.Add("PASS: the headset menu keeps its world position and faces viewers at either side or behind it");
            var stateField = typeof(ARSession).GetField("s_State", PrivateStatic);
            stateField.SetValue(null, ARSessionState.SessionInitializing);
            Call(headset, "LateUpdate");
            Require(panel.gameObject.activeSelf, "A temporary AR tracking loss must not hide the headset menu.");
            stateField.SetValue(null, ARSessionState.SessionTracking);
            Call(headset, "LateUpdate"); // Let the current tracking-recovery policy settle first.
            Vector3 fixedMenuPosition = panel.transform.position;
            Quaternion originalCameraRotation = camera.transform.rotation;
            try
            {
                // Look away, turn around, and move to either side without gripping.
                foreach (float yaw in new[] { 90f, 180f, 270f, 0f })
                {
                    camera.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * originalCameraRotation;
                    Vector3 walkStart = camera.transform.position;
                    Vector3 walkEnd = cameraStart + camera.transform.right * 0.5f;
                    for (int frame = 1; frame <= 20; frame++)
                    {
                        camera.transform.position = Vector3.Lerp(walkStart, walkEnd, frame / 20f);
                        Call(headset, "LateUpdate");
                    }
                    Require(panel.transform.position == fixedMenuPosition && !panel.transform.IsChildOf(camera.transform),
                        $"Head rotation and walking must never recenter a released menu into the user's gaze. expected={fixedMenuPosition:F6}; actual={panel.transform.position:F6}; dragging={Get(headset, "isDraggingMenu")}; parent={panel.transform.parent}");
                }
                stateField.SetValue(null, ARSessionState.SessionInitializing);
                Call(headset, "LateUpdate");
                stateField.SetValue(null, ARSessionState.SessionTracking);
                Call(headset, "LateUpdate");
                Require(panel.transform.position == fixedMenuPosition,
                    "Tracking recovery while looking down must not drop the menu onto the table in front of the user.");
                camera.transform.rotation = Quaternion.Euler(70f, 0f, 0f);
                camera.transform.position = fixedMenuPosition + Vector3.up * 0.2f;
                stateField.SetValue(null, ARSessionState.SessionInitializing);
                Call(headset, "LateUpdate");
                stateField.SetValue(null, ARSessionState.SessionTracking);
                Call(headset, "LateUpdate");
                Require(panel.transform.position == fixedMenuPosition,
                    "Approaching the menu and looking down during recovery must never trigger automatic relocation.");
                results.Add("PASS: headset menu keeps its world position through walking, approaching, looking down and tracking recovery");
            }
            finally
            {
                camera.transform.SetPositionAndRotation(cameraStart, originalCameraRotation);
                stateField.SetValue(null, ARSessionState.SessionTracking);
            }
            var other = Child(controller.gameObject, "OtherMenuHand").AddComponent<ButtonInteractor>();
            other.interactiveButtons = new List<RectTransform>();
            try
            {
                SetInput(input, true, true, false);
                input.enabled = true;
                Call(headset, "ClaimControl", grip, input);
                Require(!(bool)typeof(PhoneHeadsetAR).GetMethod("CanProcessControl", PrivateInstance).Invoke(headset, new object[] { grip, other }), "The other hand cannot reset an owned UI hold.");
                SetInput(input, true, false, false);
                Require((bool)typeof(PhoneHeadsetAR).GetMethod("CanProcessControl", PrivateInstance).Invoke(headset, new object[] { grip, other }), "A released UI control must be available to the other hand.");
                results.Add("PASS: UI control ownership prevents cross-hand hold resets and permits takeover after release");
            }
            finally { input.enabled = false; UnityEngine.Object.DestroyImmediate(other.gameObject); }

        }
        finally
        {
            Set(controller, "currentPrefabToPlace", null);
            Set(headset, "<IsActive>k__BackingField", false);
            foreach (var button in (List<RectTransform>)Get(headset, "buttons")) input.UnregisterButton(button);
            var panel = (Canvas)Get(headset, "panel");
            if (panel != null) UnityEngine.Object.DestroyImmediate(panel.gameObject);
        }
    }

    private static void CheckTwoHandInventory(PhoneHeadsetAR headset, TapToPlaceController controller,
        Camera camera, List<string> results)
    {
        var primary = controller.buttonInteractor;
        var secondary = Child(controller.gameObject, "SecondInventoryHand").AddComponent<ButtonInteractor>();
        secondary.interactiveButtons = new List<RectTransform>();
        var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prefab.transform.SetParent(controller.transform, false);
        prefab.transform.localPosition = Vector3.right * 100f;
        var oldPrefabs = controller.componentPrefabs;
        var oldNames = controller.componentNames;
        int beforeCount = controller.placedObjectsHistory.Count;
        var existingVisibility = new Dictionary<GameObject, bool>();
        foreach (var existing in controller.placedObjectsHistory)
            if (existing != null) { existingVisibility[existing] = existing.activeSelf; existing.SetActive(false); }
        controller.componentPrefabs = new[] { prefab };
        controller.componentNames = new[] { "Pin" };
        controller.SetSecondHand(secondary);
        var spawned = new List<GameObject>();
        try
        {
            SetInput(primary, true, true, true);
            SetInput(secondary, true, true, true);
            Set(primary, "<PinchConsumedByUI>k__BackingField", true);
            Set(secondary, "<PinchConsumedByUI>k__BackingField", true);
            var panel = (Canvas)Get(headset, "panel");
            var inventory = (RectTransform)panel.transform.Find("KHO LINH KIỆN");
            var battery = inventory.Find("Pin").GetComponent<Button>();
            Call(headset, "NotifyControlHand", primary);
            battery.onClick.Invoke();
            Call(headset, "NotifyControlHand", secondary);
            battery.onClick.Invoke();
            Call(headset, "LateUpdate");
            object secondState = Get(controller, "secondDrag");
            var firstPreview = (GameObject)Get(controller, "previewAnchor");
            var secondPreview = (GameObject)secondState.GetType().GetField("previewAnchor").GetValue(secondState);
            Require(controller.placedObjectsHistory.Count == beforeCount && firstPreview != null && secondPreview != null && firstPreview != secondPreview,
                "Two inventory clicks must create independent previews, without placing anything or recording history.");
            SceneManager.MoveGameObjectToScene(firstPreview, controller.gameObject.scene);
            SceneManager.MoveGameObjectToScene(secondPreview, controller.gameObject.scene);
            var operation = typeof(TapToPlaceController).GetNestedType("HandOperation", BindingFlags.NonPublic);
            object frame = Enum.Parse(operation, "Frame");
            Set(controller, "cooldownTimer", 0f);
            secondState.GetType().GetField("cooldownTimer").SetValue(secondState, 0f);
            Call(controller, "UpdateActiveHand");
            Call(controller, "ProcessSecondHand", frame);
            Require(controller.placedObjectsHistory.Count == beforeCount && !controller.IsDragging,
                "A held menu pinch must not commit or select either preview.");
            SetInput(primary, true, false, false);
            SetInput(secondary, true, false, false);
            Call(controller, "UpdateActiveHand");
            Call(controller, "ProcessSecondHand", frame);
            Require(controller.placedObjectsHistory.Count == beforeCount &&
                !(bool)Get(controller, "requireMeasuredForPreview") &&
                !(bool)secondState.GetType().GetField("requireMeasuredForPreview").GetValue(secondState),
                "Releasing the menu pinch must not commit; both VR previews must allow the same instant fallback as Manual Assembly.");
            results.Add("PASS: each hand owns an AR preview, menu gestures cannot auto-place, and VR shares AR's immediate fallback");

            var managerRoot = Child(controller.gameObject, "PreviewSupportManager");
            managerRoot.SetActive(false);
            var manager = managerRoot.AddComponent<ARPlaneManager>();
            manager.enabled = false;
            ARPlane plane = null;
            bool oldTap = controller.enableTapToPlace;
            controller.enableTapToPlace = false;
            try
            {
                var polygon = new[] { new Vector2(-2f, -2f), new Vector2(2f, -2f), new Vector2(2f, 2f), new Vector2(-2f, 2f) };
                var id = new TrackableId(833, 1);
                plane = (ARPlane)typeof(ARPlacementRegressionChecks).GetMethod("CreateTrackedPlane", PrivateStatic).Invoke(null,
                    new object[] { managerRoot, manager, id, polygon, TrackingState.Tracking, PlaneAlignment.HorizontalUp });
                plane.transform.position = camera.transform.position + Vector3.down * 0.35f + camera.transform.forward;
                controller.arPlaneManager = manager;
                Vector3 left = plane.transform.position + camera.transform.right * -0.2f;
                Vector3 right = plane.transform.position + camera.transform.right * 0.2f;
                Set(primary, "<CurrentScreenPos>k__BackingField", (Vector2)camera.WorldToScreenPoint(left));
                Set(secondary, "<CurrentScreenPos>k__BackingField", (Vector2)camera.WorldToScreenPoint(right));
                // Open first, then confirm at the actual measured points, exactly as in Manual Assembly.
                SetInput(primary, true, false, false);
                SetInput(secondary, true, false, false);
                Call(controller, "UpdateActiveHand");
                Call(controller, "ProcessSecondHand", frame);
                Require(controller.placedObjectsHistory.Count == beforeCount, "Moving previews onto a table must still wait for confirmation.");
                SetInput(primary, true, true, true);
                SetInput(secondary, true, true, true);
                Call(controller, "UpdateActiveHand");
                Call(controller, "ProcessSecondHand", frame);
                Require(controller.placedObjectsHistory.Count == beforeCount + 2 && !controller.HasAnyPreview,
                    "Both owners must independently confirm onto the measured table using the shared AR placement flow.");
                spawned.Add(controller.placedObjectsHistory[beforeCount]);
                spawned.Add(controller.placedObjectsHistory[beforeCount + 1]);
                var contacts = (System.Collections.IDictionary)Get(controller, "placedContacts");
                foreach (var item in spawned)
                {
                    Require(!controller.IsObjectBeingDragged(item) && controller.SelectedComponent != item &&
                        Mathf.Abs(item.transform.position.y - plane.transform.position.y) < 0.0001f,
                        "A confirmed component must rest on measured support without silently becoming a drag.");
                    object contact = contacts[item];
                    Require((TrackableId)contact.GetType().GetField("planeId").GetValue(contact) == id,
                        "VR must retain the same measured plane binding as the Manual Assembly path.");
                    var support = item.GetComponent<ARInstantPlacementAnchor>()?.SupportRoot;
                    if (support != null) support.SetParent(controller.transform, true);
                }
                Vector3 firstPosition = spawned[0].transform.position;
                Vector3 secondPosition = spawned[1].transform.position;
                Vector3 cameraBefore = camera.transform.position;
                camera.transform.position -= camera.transform.forward * 0.3f;
                Call(controller, "HandleDragAndDrop");
                Call(controller, "ProcessSecondHand", frame);
                Require(spawned[0].transform.position == firstPosition && spawned[1].transform.position == secondPosition,
                    "Confirmed components must stay fixed when the viewer moves, even with the placement pinch held.");
                camera.transform.position = cameraBefore;
                results.Add("PASS: two previews commit independently through the shared measured AR path, keep support bindings and stay world-fixed");
            }
            finally
            {
                controller.arPlaneManager = null;
                controller.enableTapToPlace = oldTap;
                if (plane != null) UnityEngine.Object.DestroyImmediate(plane.gameObject);
                UnityEngine.Object.DestroyImmediate(manager.gameObject);
            }
        }
        finally
        {
            controller.CancelPlacement();
            controller.SetSecondHand(null);
            controller.ClearSelectedComponent();
            controller.componentPrefabs = oldPrefabs;
            controller.componentNames = oldNames;
            foreach (var entry in existingVisibility) if (entry.Key != null) entry.Key.SetActive(entry.Value);
            foreach (var item in spawned)
            {
                controller.placedObjectsHistory.Remove(item);
                var support = item != null ? item.transform.parent : null;
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
                if (support != null && support != controller.transform && support.childCount == 0)
                    UnityEngine.Object.DestroyImmediate(support.gameObject);
            }
            UnityEngine.Object.DestroyImmediate(prefab);
            UnityEngine.Object.DestroyImmediate(secondary.gameObject);
            SetInput(primary, true, false, false);
            Call(controller, "HandleDragAndDrop"); // Deliver the post-placement open hand before the next fixture.
        }
    }

    private static void CheckEmptyPinchDeselect(TapToPlaceController controller, GameObject selected, Vector2 point)
    {
        Require(controller.SelectedComponent == selected && controller.arPlaneManager == null,
            "The fixture must start with a selected object and no detected surface.");
        var input = controller.buttonInteractor;
        SetInput(input, visible: true, pinching: true, justPinched: true);
        Set(input, "<CurrentScreenPos>k__BackingField", point);
        Call(controller, "HandleDragAndDrop");
        Require(controller.SelectedComponent == null && !controller.IsDragging &&
            selected.GetComponent<Renderer>().sharedMaterial != Get(controller, "selectionHighlightMat") as Material,
            "An empty pinch must deselect and restore the renderer without requiring an AR raycast hit.");
    }

    private static GameObject Cube(GameObject root, string name, Vector3 position)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(root.transform, false);
        cube.transform.localPosition = position;
        cube.transform.localScale = Vector3.one * 0.2f;
        // Pinch picking must work against the visible mesh without a physics collider.
        cube.GetComponent<Collider>().enabled = false;
        return cube;
    }

    private static Rect ProjectedRect(Camera camera, Bounds bounds)
    {
        Vector2 min = Vector2.one * float.PositiveInfinity;
        Vector2 max = Vector2.one * float.NegativeInfinity;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 point = camera.WorldToScreenPoint(bounds.center +
                Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static GameObject Nearby(TapToPlaceController controller, Vector2 point)
    {
        var arguments = new object[] { point, null };
        bool found = (bool)controller.GetType().GetMethod("TryFindNearbyTapTarget", PrivateInstance)
            .Invoke(controller, arguments);
        Require(found == (arguments[1] != null), "Nearby target return value must match its out parameter.");
        return arguments[1] as GameObject;
    }

    private static void SetInput(ButtonInteractor input, bool visible, bool pinching, bool justPinched)
    {
        Set(input, "<IsHandVisible>k__BackingField", visible);
        Set(input, "<JustPinched>k__BackingField", justPinched);
        Set(input, "<IsPointerOverUI>k__BackingField", false);
        Set(input, "<PinchConsumedByUI>k__BackingField", false);
        input.isPinching = pinching;
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static object Static(string name, params object[] args) =>
        typeof(TapToPlaceController).GetMethod(name, PrivateStatic).Invoke(null, args);
    private static object Get(object instance, string name) => instance.GetType().GetField(name, PrivateInstance).GetValue(instance);
    private static void Set(object instance, string name, object value)
    {
        instance.GetType().GetField(name, PrivateInstance).SetValue(instance, value);
        if (instance is ButtonInteractor && name == "<CurrentScreenPos>k__BackingField")
        {
            typeof(ButtonInteractor).GetField("rawWorldDragPoint", PrivateInstance).SetValue(instance, value);
            typeof(ButtonInteractor).GetField("hasWorldDragPoint", PrivateInstance).SetValue(instance, true);
        }
    }
    private static void Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, PrivateInstance).Invoke(instance, args);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
