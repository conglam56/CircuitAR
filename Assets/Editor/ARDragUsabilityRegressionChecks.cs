using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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

            CheckHeadsetPinch(controller, camera, near);
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

    private static void CheckHeadsetPinch(TapToPlaceController controller, Camera camera, GameObject near)
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
            Require(stereo.IsReady && camera.cullingMask == 0,
                "The fixture must exercise headset rendering with the source camera's object layers masked out.");
            var input = controller.buttonInteractor;
            input.SetHeadsetCamera(camera);
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
    private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, PrivateInstance).SetValue(instance, value);
    private static void Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, PrivateInstance).Invoke(instance, args);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
