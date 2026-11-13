using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>Immediate placement and input ownership checks without a native XR provider.</summary>
internal static class ARInstantPlacementRegressionChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(List<string> results)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("ARInstantPlacementRegressionFixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = Vector3.up * 300f;
        root.SetActive(false);
        var stateField = typeof(ARSession).GetField("s_State", BindingFlags.Static | BindingFlags.NonPublic);
        object previousState = stateField.GetValue(null);
        EventSystem previousEvents = EventSystem.current;
        var previousRaycasters = new List<BaseRaycaster>(RaycasterManager.GetRaycasters());
        TapToPlaceController controller = null;
        GameObject support = null;
        GameObject canvasObject = null;
        EventSystem fixtureEvents = null;
        try
        {
            // Isolate the managed event registry without enabling/disabling or
            // dirtying any objects in the user's open scenes.
            RaycasterManager.GetRaycasters().Clear();
            var camera = Child(root, "Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 20f;
            camera.fieldOfView = 60f;
            camera.pixelRect = new Rect(0f, 0f, 1000f, 2000f);
            camera.aspect = 0.5f;
            camera.transform.localPosition = Vector3.up;
            camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            controller = Child(root, "Controller").AddComponent<TapToPlaceController>();
            Set(controller, "mainCamera", camera);
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = "InstantTestComponent";
            prefab.transform.SetParent(root.transform, false);
            prefab.transform.localPosition = Vector3.zero;
            controller.componentPrefabs = new[] { prefab };
            controller.componentNames = new[] { "Test" };
            controller.scaleMultiplier = 0.1f;
            root.SetActive(true);
            stateField.SetValue(null, ARSessionState.SessionTracking);
            Require(controller.enableInstantPlacement && controller.enableTapToPlace,
                "A new AR placement controller must enable immediate placement and screen taps by default.");
            Require(controller.buttonInteractor == null && controller.arRaycastManager == null &&
                controller.arPlaneManager == null && controller.arAnchorManager == null,
                "The immediate-placement fixture must have neither hand data nor native surface/anchor managers.");

            CheckComponentSizes(controller, scene, results);

            Vector3 contact = root.transform.position + Vector3.forward;
            Vector2 screenPoint = camera.WorldToScreenPoint(contact);
            var taps = new ARPlacementTapInput();
            Require(Process(taps, screenPoint, 7, down: true, held: true),
                "A camera-area press must begin a potential placement tap.");
            Require(Process(taps, screenPoint, 7, up: true) && taps.Confirmed,
                "A short stationary camera-area release must confirm one placement tap.");
            Require(!Process(taps, screenPoint, 7) && !taps.Confirmed,
                "A released tap must not confirm again on an idle frame.");
            results.Add("PASS: camera-area taps confirm once on release without requiring a tracked hand");

            controller.StartPreview("Test");
            MovePreview(controller, scene);
            Set(controller, "cooldownTimer", 0f);
            Set(controller, "placementTapThisFrame", true);
            Call(controller, "UpdatePlacementRequest", true, screenPoint);
            Require((bool)Get(controller, "placementRequestFromTap") &&
                (Vector2)Get(controller, "placementRequestScreenPosition") == screenPoint,
                "A confirmed screen tap must create a placement request without dereferencing hand input.");
            Set(controller, "placementTapThisFrame", false);
            Vector3 previewScale = ((GameObject)Get(controller, "previewVisual")).transform.localScale;
            Pose requested = new Pose(contact, Quaternion.Euler(0f, 20f, 0f));
            Set(controller, "placementRequestHasEstimate", true);
            Set(controller, "placementRequestEstimate", requested);
            Call(controller, "UpdatePlacementRequest", false, screenPoint + Vector2.right * 50f);
            Require((bool)Get(controller, "placementRequestFromTap") &&
                (bool)Get(controller, "placementRequestHasEstimate") &&
                (Vector2)Get(controller, "placementRequestScreenPosition") == screenPoint &&
                ((Pose)Get(controller, "placementRequestEstimate")).position == contact,
                "A queued release tap must keep its captured pixel and estimate after the pointer disappears or moves.");
            Require(Place(controller, requested, screenPoint),
                "SessionTracking must allow immediate estimated placement with no measured planes or native anchor manager.");
            Require(controller.placedObjectsHistory.Count == 1 && controller.History.UndoCount == 1 &&
                Get(controller, "previewAnchor") == null && controller.CurrentPrefabToPlace == null &&
                !(bool)Get(controller, "placementPending"),
                "An immediate commit must finish preview and record one history action in the same call.");
            GameObject placed = controller.placedObjectsHistory[0];
            Require(placed.transform.GetChild(0).localScale == previewScale,
                "Committing must retain the normalized preview body scale instead of reverting to the imported prefab scale.");
            results.Add("PASS: normalized component scale survives immediate placement");
            var helper = placed.GetComponent<ARInstantPlacementAnchor>();
            for (int i = 0; i < 6; i++)
            {
                Call(helper, "SchedulePromotionRetry");
                float delay = (float)Get(helper, "promotionNotBefore") - Time.realtimeSinceStartup;
                Require(delay > 0f && delay <= 4.01f,
                    "Native anchor failure must schedule a bounded background retry instead of permanently abandoning tracking.");
            }
            Set(helper, "promotionNotBefore", 0f);
            Call(helper, "Update");
            Require(!(bool)Get(helper, "promotionExhausted"),
                "The exhausted attempt must become eligible again after its backoff.");
            results.Add("PASS: unavailable native anchors use bounded background retries and recover after their backoff");
            Require(helper != null && (bool)Get(helper, "configured") && helper.IsEstimated &&
                helper.SupportRoot == placed.transform.parent && !placed.transform.IsChildOf(camera.transform),
                "The placed component must own a configured estimated-support helper outside the camera hierarchy.");
            support = helper.SupportRoot.gameObject;
            SceneManager.MoveGameObjectToScene(support, scene);
            support.transform.SetParent(root.transform, true);
            Require(Vector3.Distance(placed.transform.position, contact) < 0.0001f &&
                Mathf.Abs(placed.GetComponentInChildren<Renderer>().bounds.min.y - contact.y) < 0.001f &&
                placed.GetComponentInChildren<Collider>().enabled,
                "Immediate commit must preserve the requested contact, align the visible base and restore object interaction.");
            results.Add("PASS: estimated placement completes immediately, restores colliders and enters undo history without waiting for a plane/native anchor");

            Vector3 stablePosition = placed.transform.position;
            Quaternion stableRotation = placed.transform.rotation;
            Vector3 cameraPosition = camera.transform.position;
            camera.transform.position += new Vector3(0.2f, 0.1f, 0.05f);
            Call(helper, "Update");
            Require(Vector3.Distance(placed.transform.position, stablePosition) < 0.0001f &&
                Quaternion.Angle(placed.transform.rotation, stableRotation) < 0.01f,
                "Moving the camera must not move or rotate an already committed estimated component.");
            camera.transform.position = cameraPosition;
            results.Add("PASS: committed estimated content remains in world space through camera movement");

            int beforeUndo = (int)Get(helper, "generation");
            Require(controller.UndoLastPlacedComponent() && !placed.activeSelf &&
                controller.placedObjectsHistory.Count == 0,
                "Undo must hide the committed object and remove it from active placement history.");
            // Non-ExecuteAlways behaviours are not driven by the edit-mode
            // player loop. Exercise the runtime lifecycle explicitly here.
            Call(helper, "OnDisable");
            Require((int)Get(helper, "generation") > beforeUndo &&
                !(bool)Get(helper, "runtimeActive") && helper.SupportRoot == support.transform,
                "Undo must cancel support work while preserving the inactive object and its support for redo.");
            Call(helper, "Update");
            Require(!(bool)Get(helper, "runtimeActive"),
                "A late support update must never restart work for an undone/inactive object.");
            Require(controller.RedoLastAction() && placed.activeSelf && controller.placedObjectsHistory.Count == 1 &&
                controller.placedObjectsHistory[0] == placed && controller.History.UndoCount == 1 &&
                Vector3.Distance(placed.transform.position, stablePosition) < 0.0001f,
                "Redo must restore the same object once at the previously committed world pose.");
            Call(helper, "OnEnable");
            Call(helper, "Update");
            Require((bool)Get(helper, "runtimeActive"), "Redo must allow configured background support work to resume.");
            results.Add("PASS: undo cancels pending support without destroying it; redo restores the same world pose and history entry");

            Set(controller, "isDragging", true);
            Set(controller, "draggedObject", placed);
            Set(controller, "dragStartPos", stablePosition);
            Vector2 dragPoint = camera.WorldToScreenPoint(stablePosition + new Vector3(0.15f, 0f, 0.05f));
            Set(controller, "dragFreePlane", new Plane(camera.transform.forward, stablePosition));
            Require(DragSurface(controller, dragPoint, out Pose dragPose) &&
                Mathf.Abs(Vector3.Dot(dragPose.position - stablePosition, camera.transform.forward)) < 0.0001f &&
                (bool)Get(controller, "dragSurfaceIsEstimated"),
                "An estimated object must remain draggable without a measured plane.");
            Vector2 lowerPointer = (Vector2)camera.WorldToScreenPoint(stablePosition) + Vector2.down * 100f;
            Require(DragSurface(controller, lowerPointer, out Pose lowerPose) && lowerPose.position.y < stablePosition.y - 0.01f,
                "Lowering the hand must lower an estimated object, even before a real surface is detected.");
            results.Add("PASS: lowering an estimated component changes its height instead of locking it in mid-air");
            controller.enableInstantPlacement = false;
            Require(!DragSurface(controller, dragPoint, out _),
                "Disabling instant placement must preserve the measured-only drag restriction.");
            controller.enableInstantPlacement = true;
            var estimates = (HashSet<GameObject>)Get(controller, "instantPlacedObjects");
            estimates.Remove(placed);
            Require(!DragSurface(controller, dragPoint, out _),
                "Objects without an estimated-support marker must never gain an unmeasured drag surface.");
            estimates.Add(placed);
            int beforeStop = (int)Get(helper, "generation");
            helper.StopRefinement();
            Require((int)Get(helper, "generation") > beforeStop && (bool)Get(helper, "refinementStopped") &&
                helper.SupportRoot == support.transform &&
                Vector3.Distance(placed.transform.position, stablePosition) < 0.0001f,
                "Grabbing must invalidate old refinement requests without deleting support or changing the placed pose.");
            Set(controller, "isDragging", false);
            Set(controller, "draggedObject", null);
            results.Add("PASS: only estimated objects use free hand-plane drag fallback, and grabbing permanently cancels native ray refinement");

            CheckAuthoringCancellation(root, camera, results);
            CheckWorldFixedEstimatedPlacement(root, results);

            controller.StartPreview("Test");
            MovePreview(controller, scene);
            Set(controller, "cooldownTimer", 0f);
            Require(!Place(controller, requested, screenPoint) && controller.placedObjectsHistory.Count == 1 &&
                controller.History.UndoCount == 1 && Get(controller, "previewAnchor") != null,
                "Immediate placement must reject an exact overlap without duplicating history or consuming the preview.");
            stateField.SetValue(null, ARSessionState.SessionInitializing);
            Require(!Place(controller, new Pose(contact + Vector3.right * 0.5f, Quaternion.identity), screenPoint) &&
                controller.placedObjectsHistory.Count == 1 && !(bool)Get(controller, "placementPending"),
                "Immediate placement must still reject an AR session that has not begun world tracking.");
            stateField.SetValue(null, ARSessionState.SessionTracking);
            controller.ClearPreview();
            results.Add("PASS: immediate placement retains overlap rejection and initial session-tracking requirements");

            var input = Child(root, "NoHandInput").AddComponent<ButtonInteractor>();
            input.enabled = false;
            controller.buttonInteractor = input;
            controller.StartPreview("Test");
            MovePreview(controller, scene);
            Set(controller, "cooldownTimer", 0.2f);
            Vector2 queuedPixel = camera.WorldToScreenPoint(contact + Vector3.right * 0.25f);
            Set(controller, "placementTapThisFrame", true);
            Call(controller, "UpdatePlacementRequest", true, queuedPixel);
            Set(controller, "placementTapThisFrame", false);
            Call(controller, "UpdatePreviewAndPlacement");
            Require((bool)Get(controller, "placementRequestHasEstimate") &&
                controller.placedObjectsHistory.Count == 1 && Get(controller, "previewAnchor") != null,
                "A release tap during preview cooldown must capture its estimate while keeping the preview pending.");
            Pose queuedPose = (Pose)Get(controller, "placementRequestEstimate");
            camera.transform.position += Vector3.right * 0.15f;
            Set(controller, "cooldownTimer", 0f);
            Call(controller, "UpdatePreviewAndPlacement");
            Require(controller.placedObjectsHistory.Count == 2 && controller.History.UndoCount == 2 &&
                Vector3.Distance(controller.placedObjectsHistory[1].transform.position, queuedPose.position) < 0.0001f &&
                Get(controller, "previewAnchor") == null,
                "After cooldown, a released tap with no visible hand must commit once at its captured world pose despite camera movement.");
            GameObject queuedSupport = controller.placedObjectsHistory[1].transform.parent.gameObject;
            SceneManager.MoveGameObjectToScene(queuedSupport, scene);
            queuedSupport.transform.SetParent(root.transform, true);
            Call(controller, "UpdatePreviewAndPlacement");
            Require(controller.placedObjectsHistory.Count == 2 && controller.History.UndoCount == 2,
                "A completed release tap must not create an additional component on the following frame.");
            camera.transform.position = cameraPosition;
            results.Add("PASS: a tap queued during cooldown commits exactly once at its captured pose after hand release and camera motion");

            CheckCancelledTaps(results);
            CheckUIOrigin(root, scene, ref canvasObject, ref fixtureEvents, results);
        }
        finally
        {
            if (fixtureEvents != null) Call(fixtureEvents, "OnDisable");
            if (previousEvents != null) EventSystem.current = previousEvents;
            stateField.SetValue(null, previousState);
            if (controller != null)
            {
                controller.ClearSelectedComponent();
                controller.ClearPreview();
            }
            if (canvasObject != null) UnityEngine.Object.DestroyImmediate(canvasObject);
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            RaycasterManager.GetRaycasters().Clear();
            foreach (BaseRaycaster raycaster in previousRaycasters)
                if (raycaster != null) RaycasterManager.GetRaycasters().Add(raycaster);
        }
    }

    private static void CheckComponentSizes(TapToPlaceController controller, Scene scene, List<string> results)
    {
        var previousPrefabs = controller.componentPrefabs;
        var previousNames = controller.componentNames;
        var terminal = typeof(TapToPlaceController).GetMethod("IsTerminalVisual", BindingFlags.Static | BindingFlags.NonPublic);
        try
        {
            foreach (string name in new[] { "Battery", "lamp", "key", "ammetr", "voltmetr" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Components/" + name + ".prefab");
                Require(prefab != null, "The shipped component prefab must exist: " + name);
                controller.componentPrefabs = new[] { prefab };
                controller.componentNames = new[] { name };
                controller.StartPreview(name);
                MovePreview(controller, scene);
                var visual = (GameObject)Get(controller, "previewVisual");
                Bounds bounds = default;
                bool found = false;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled || renderer is LineRenderer || (bool)terminal.Invoke(null, new object[] { renderer.transform, visual.transform })) continue;
                    if (found) bounds.Encapsulate(renderer.bounds);
                    else { bounds = renderer.bounds; found = true; }
                }
                float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                Require(found && Mathf.Abs(longest - controller.componentBodySize) < 0.0005f,
                    "All inventory bodies must share a comparable physical size: " + name + " size=" + longest);
                controller.ClearPreview();
            }
            results.Add("PASS: all five shipped component bodies normalize to the same physical size despite different imported units");
        }
        finally
        {
            controller.ClearPreview();
            controller.componentPrefabs = previousPrefabs;
            controller.componentNames = previousNames;
        }
    }

    private static void CheckWorldFixedEstimatedPlacement(GameObject root, List<string> results)
    {
        var fixture = Child(root, "WorldFixedInstantPlacement");
        fixture.SetActive(false);
        var rig = Child(fixture, "MovingCameraRig");
        var camera = Child(rig, "Camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.localPosition = Vector3.up;
        camera.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
        var manager = rig.AddComponent<ARRaycastManager>();
        manager.enabled = false;
        var owner = Child(fixture, "Placement").AddComponent<TapToPlaceController>();
        Set(owner, "mainCamera", camera);
        owner.arRaycastManager = manager;
        var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prefab.transform.SetParent(fixture.transform, false);
        prefab.transform.localPosition = Vector3.right * 20f;
        owner.componentNames = new[] { "Test" };
        owner.componentPrefabs = new[] { prefab };
        fixture.SetActive(true);
        GameObject support = null;
        try
        {
            owner.StartPreview("Test");
            MovePreview(owner, root.scene);
            Set(owner, "cooldownTimer", 0f);
            Pose contact = new Pose(root.transform.position + new Vector3(2f, 0f, 1f), Quaternion.identity);
            Require(Place(owner, contact, camera.WorldToScreenPoint(contact.position)), "The camera-rig fixture must place immediately without a measured plane.");
            var body = owner.placedObjectsHistory[0];
            var helper = body.GetComponent<ARInstantPlacementAnchor>();
            support = helper.SupportRoot.gameObject;
            Require(support.transform.parent == null && !body.transform.IsChildOf(rig.transform),
                "Estimated support must not inherit the raycast manager or any camera-rig transform.");
            SceneManager.MoveGameObjectToScene(support, root.scene);
            for (int i = 0; i < 30; i++)
            {
                rig.transform.position += new Vector3(0.003f, 0f, -0.025f);
                rig.transform.rotation = Quaternion.Euler(0f, i * 0.5f, 0f);
                Call(helper, "Update");
                Require(Vector3.Distance(body.transform.position, contact.position) < 0.0001f && helper.IsEstimated &&
                    !(bool)Get(helper, "requestInFlight"),
                    "A committed unmeasured object must remain world-fixed through camera retreat without adopting an unconfirmed native anchor.");
            }
            results.Add("PASS: a committed estimate stays fixed through 30 camera-rig retreat/rotation updates, independently of manager parenting and unconfirmed native promotion");
        }
        finally
        {
            owner.CancelPlacement();
            if (support != null) UnityEngine.Object.DestroyImmediate(support);
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void CheckAuthoringCancellation(GameObject root, Camera camera, List<string> results)
    {
        var mutationRoot = Child(root, "AuthoringCancellationFixture");
        var owner = Child(mutationRoot, "Controller").AddComponent<TapToPlaceController>();
        Set(owner, "mainCamera", camera);
        var support = Child(mutationRoot, "EstimatedSupport").transform;
        support.localPosition = new Vector3(2f, 0f, 1f);
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = "Placed_AuthoringCancellation";
        obj.transform.SetParent(support, false);
        obj.transform.localScale = Vector3.one * 0.1f;
        owner.placedObjectsHistory.Add(obj);
        var helper = obj.AddComponent<ARInstantPlacementAnchor>();
        helper.Initialize(owner, null, null, camera, camera.WorldToScreenPoint(obj.transform.position),
            Vector3.Distance(camera.transform.position, obj.transform.position), support);
        Vector3 initialPosition = obj.transform.position;
        Quaternion initialRotation = obj.transform.rotation;
        try
        {
            void CheckMutation(Action operation, Vector3 position, Quaternion rotation, string label)
            {
                Set(helper, "requestInFlight", true);
                Set(helper, "promotionExhausted", true);
                Set(helper, "runtimeActive", true);
                Set(helper, "refinementStopped", false);
                int before = (int)Get(helper, "generation");
                operation();
                Require((int)Get(helper, "generation") > before &&
                    !(bool)Get(helper, "requestInFlight") && !(bool)Get(helper, "runtimeActive") &&
                    (bool)Get(helper, "refinementStopped") && helper.SupportRoot == support,
                    label + " must invalidate the old native request and permanently stop ray refinement without deleting support.");
                Require(Vector3.Distance(obj.transform.position, position) < 0.0001f &&
                    Quaternion.Angle(obj.transform.rotation, rotation) < 0.01f,
                    label + " must preserve the pose authored by that operation.");
                Set(helper, "promotionNotBefore", 0f);
                Call(helper, "Update");
                Require((bool)Get(helper, "runtimeActive") && !(bool)Get(helper, "promotionExhausted") &&
                    !(bool)Get(helper, "requestInFlight") &&
                    Vector3.Distance(obj.transform.position, position) < 0.0001f &&
                    Quaternion.Angle(obj.transform.rotation, rotation) < 0.01f,
                    label + " must allow a fresh background-anchor attempt at the authored pose without waiting for a native manager.");
            }

            owner.SelectComponent(obj);
            Quaternion quarterTurn = initialRotation * Quaternion.Euler(0f, 90f, 0f);
            CheckMutation(owner.RotateSelectedComponent, initialPosition, quarterTurn, "Selected-object rotation");
            owner.ClearSelectedComponent();
            CheckMutation(() => Require(owner.History.Undo(), "Rotation history must be undoable."),
                initialPosition, initialRotation, "Rotation-history undo");
            CheckMutation(() => Require(owner.History.Redo(), "Rotation history must be redoable."),
                initialPosition, quarterTurn, "Rotation-history redo");
            Quaternion externalRotation = Quaternion.Euler(0f, 35f, 0f);
            CheckMutation(() =>
            {
                obj.transform.rotation = externalRotation;
                owner.NotifyComponentRotated(obj);
            }, initialPosition, externalRotation, "Unselected-object rotation notification");

            Vector3 movedPosition = initialPosition + new Vector3(0.2f, 0f, 0.1f);
            Quaternion movedRotation = Quaternion.Euler(0f, 50f, 0f);
            owner.History.RecordAction(new MoveComponentAction(obj, initialPosition, initialRotation,
                movedPosition, movedRotation, null));
            CheckMutation(() => Require(owner.History.Undo(), "Move history must be undoable."),
                initialPosition, initialRotation, "Move-history undo");
            CheckMutation(() => Require(owner.History.Redo(), "Move history must be redoable."),
                movedPosition, movedRotation, "Move-history redo");
            results.Add("PASS: rotation, rotation notifications and move/rotation undo-redo cancel stale native requests while preserving authored poses and allowing fresh promotion");
        }
        finally
        {
            owner.ClearSelectedComponent();
            foreach (string field in new[] { "selectionHighlightMat", "selectionInvalidMat" })
                if (Get(owner, field) is Material material) UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(mutationRoot);
        }
    }

    private static void CheckCancelledTaps(List<string> results)
    {
        var taps = new ARPlacementTapInput();
        Vector2 point = new Vector2(100f, 100f);
        Process(taps, point, 1, down: true, held: true);
        Vector2 far = point + Vector2.right * Mathf.Max(1000f, Screen.width);
        Process(taps, far, 1, held: true);
        Require(!Process(taps, point, 1, up: true) && !taps.Confirmed,
            "A swipe must remain cancelled even if the finger returns to its starting position before release.");
        Process(taps, point, 1, down: true, held: true);
        Process(taps, point, 1, held: true, cancelled: true);
        Require(!Process(taps, point, 1, up: true) && !taps.Confirmed,
            "Multi-touch/cancelled input must not confirm later when one finger remains.");
        Process(taps, point, 1, down: true, held: true);
        Require(!Process(taps, point, 2, up: true) && !taps.Confirmed,
            "A different pointer must not finish another pointer's placement tap.");
        Process(taps, point, 1, down: true, held: true);
        Set(taps, "beganTime", Time.unscaledTime - 0.5f);
        Require(!Process(taps, point, 1, up: true) && !taps.Confirmed,
            "Long presses must not be accepted as short placement taps.");
        results.Add("PASS: swipes, multi-touch cancellation, pointer replacement and long holds cannot confirm placement taps");
    }

    private static void CheckUIOrigin(GameObject root, Scene scene, ref GameObject canvasObject,
        ref EventSystem fixtureEvents, List<string> results)
    {
        var events = Child(root, "FixtureEventSystem").AddComponent<EventSystem>();
        fixtureEvents = events;
        // Like the runtime anchor helper above, these non-ExecuteAlways
        // components need explicit lifecycle setup in an edit-mode preview.
        Call(events, "OnEnable");
        EventSystem.current = events;
        canvasObject = new GameObject("InstantPlacementTestCanvas", typeof(RectTransform), typeof(Canvas));
        SceneManager.MoveGameObjectToScene(canvasObject, scene);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var raycaster = canvasObject.AddComponent<ARInstantPlacementFixtureRaycaster>();
        if (!RaycasterManager.GetRaycasters().Contains(raycaster))
            RaycasterManager.GetRaycasters().Add(raycaster);
        var blocker = new GameObject("PlacementMenuButton", typeof(RectTransform), typeof(Image));
        blocker.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = blocker.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(120f, 120f);
        raycaster.Blocker = blocker.GetComponent<Image>();
        Canvas.ForceUpdateCanvases();
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.position);
        var hits = new List<RaycastResult>();
        events.RaycastAll(new PointerEventData(events) { position = point }, hits);
        Require(hits.Exists(hit => hit.gameObject == blocker && hit.module == raycaster),
            "The UI ownership fixture must route its menu graphic through the EventSystem and a GraphicRaycaster module.");
        var taps = new ARPlacementTapInput();
        Require(!Process(taps, point, 0, down: true, held: true),
            "A press originating on menu UI must never start a world-placement tap.");
        Require(!Process(taps, point + Vector2.right * 5f, 0, up: true) && !taps.Confirmed,
            "Releasing a UI-origin gesture must not place an object behind the menu.");
        results.Add("PASS: UI-origin touches are blocked by GraphicRaycaster ownership and never confirm world placement");
    }

    private static bool Process(ARPlacementTapInput taps, Vector2 point, int id,
        bool down = false, bool held = false, bool up = false, bool cancelled = false) =>
        (bool)Call(taps, "Process", point, id, down, held, up, cancelled);

    private static bool Place(TapToPlaceController controller, Pose pose, Vector2 point) =>
        (bool)Call(controller, "PlaceInstantObject", pose, point, TrackableId.invalidId, false);

    private static bool DragSurface(TapToPlaceController controller, Vector2 point, out Pose pose)
    {
        var args = new object[] { point, Pose.identity, TrackableId.invalidId };
        bool found = (bool)typeof(TapToPlaceController).GetMethod("TryGetDragSurface", Private).Invoke(controller, args);
        pose = (Pose)args[1];
        return found;
    }

    private static void MovePreview(TapToPlaceController controller, Scene scene) =>
        SceneManager.MoveGameObjectToScene((GameObject)Get(controller, "previewAnchor"), scene);

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

/// <summary>
/// Headless preview canvases have no draw depth. Native canvas rendering is not
/// under test: preserve rectangle hit testing and actual EventSystem module
/// ownership while bypassing GraphicRaycaster's depth == -1 draw requirement.
/// </summary>
[AddComponentMenu("")]
internal sealed class ARInstantPlacementFixtureRaycaster : GraphicRaycaster
{
    public Graphic Blocker { get; set; }

    public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
    {
        if (Blocker == null || !Blocker.raycastTarget || !Blocker.gameObject.activeInHierarchy ||
            !RectTransformUtility.RectangleContainsScreenPoint(Blocker.rectTransform, eventData.position, eventCamera))
            return;
        resultAppendList.Add(new RaycastResult
        {
            gameObject = Blocker.gameObject,
            module = this,
            screenPosition = eventData.position,
            distance = 0f,
            index = resultAppendList.Count
        });
    }
}
