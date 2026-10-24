using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>Managed UI/ownership checks, not a simulation of native AR tracking.</summary>
internal static class ARSurfaceGuidanceRegressionChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(List<string> results)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("ARSurfaceGuidanceFixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false);
        root.transform.position = Vector3.up * 200f;
        var state = typeof(ARSession).GetField("s_State", BindingFlags.Static | BindingFlags.NonPublic);
        object previousState = state.GetValue(null);
        TapToPlaceController controller = null;
        try
        {
            var hud = Child(root, "ScanPillOnly").AddComponent<ARScanFloatingHUD>();
            var input = Child(root, "Input").AddComponent<ButtonInteractor>();
            input.enabled = false;
            controller = Child(root, "Placement").AddComponent<TapToPlaceController>();
            controller.enabled = false;
            controller.buttonInteractor = input;
            var camera = Child(root, "Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.localPosition = new Vector3(0f, 1f, -1f);
            camera.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            Set(controller, "mainCamera", camera);
            Set(controller, "surfaceGuidanceHUD", hud);
            Set(controller, "surfaceGuidanceEnabled", true);
            root.SetActive(true);

            void Refresh()
            {
                Set(controller, "nextSurfaceGuidanceAt", 0f);
                Call(controller, "RefreshSurfaceGuidance");
            }
            state.SetValue(null, ARSessionState.SessionInitializing);
            Refresh();
            Require(hud.IsVisible && hud.CurrentState == ARScanFloatingHUD.ScanHUDState.Scanning &&
                hud.CurrentSubtitle.Contains("Lia"), "Cold AR must show scan/movement guidance before selecting a component.");
            Require(hud.transform.Find("Floating_Pill") != null && hud.GetComponentsInChildren<Canvas>(true).Length == 1 &&
                hud.GetComponentsInChildren<Button>(true).Length == 0,
                "Guidance must reuse only the scan pill, not a board, reticle, card or extra buttons.");
            var group = hud.GetComponent<CanvasGroup>();
            Require(!group.blocksRaycasts && !group.interactable, "The status pill must not intercept touch/pinch input.");
            foreach (Graphic graphic in hud.GetComponentsInChildren<Graphic>(true))
                Require(!graphic.raycastTarget, "Every pill graphic must leave input unobstructed.");
            results.Add("PASS: startup guidance reuses only the non-interactive top scan pill");

            state.SetValue(null, ARSessionState.SessionTracking);
            Refresh();
            Require(hud.CurrentState == ARScanFloatingHUD.ScanHUDState.Scanning &&
                !(bool)typeof(TapToPlaceController).GetMethod("HasGuidanceSupport", Private).Invoke(controller, null),
                "SessionTracking alone must not turn the readiness pill green without measured support and native-anchor readiness.");
            results.Add("PASS: readiness guidance cannot claim ready from SessionTracking alone");

            var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template.name = "GuidanceComponent";
            template.transform.SetParent(root.transform, false);
            controller.componentPrefabs = new[] { template };
            controller.componentNames = new[] { "Test" };
            controller.StartPreview("Test");
            Set(input, "<IsHandVisible>k__BackingField", false);
            Refresh();
            Require(hud.CurrentTitle.Contains("chưa") && controller.placedObjectsHistory.Count == 0,
                "A selected preview must clearly say it has not been placed.");
            Set(controller, "placementPending", true);
            Refresh();
            Require(hud.CurrentTitle.Contains("neo") && hud.CurrentState == ARScanFloatingHUD.ScanHUDState.Scanning,
                "A pending anchor must not be labeled a completed placement.");
            Set(controller, "placementPending", false);
            results.Add("PASS: preview and pending-anchor messages distinguish not-yet-placed from anchored content");

            // A disabled managed anchor checks hierarchy/input ownership only.
            // It cannot authorize a real placement; native readiness is tested separately.
            var anchorObject = Child(root, "ManagedAnchor");
            anchorObject.SetActive(false);
            var anchor = anchorObject.AddComponent<ARAnchor>();
            anchor.enabled = false;
            anchorObject.SetActive(true);
            anchor.transform.localPosition = Vector3.zero;
            Call(controller, "PlaceObject", anchor.transform.position, Quaternion.identity, anchor);
            GameObject placed = controller.placedObjectsHistory[0];
            Vector3 stable = placed.transform.position;
            Require(placed.transform.parent == anchor.transform && !placed.transform.IsChildOf(camera.transform),
                "Placed content must be owned by its world/native anchor, not camera hierarchy.");
            Set(input, "<IsHandVisible>k__BackingField", true);
            input.isPinching = true;
            Set(input, "<JustPinched>k__BackingField", true);
            Set(controller, "cooldownTimer", 0f);
            camera.transform.position += Vector3.right * 0.15f;
            Call(controller, "HandleDragAndDrop");
            Require(!controller.IsDragging && placed.transform.position == stable,
                "A placement pinch re-detected still closed must not become a camera-driven drag.");
            Set(input, "<IsHandVisible>k__BackingField", false);
            Call(controller, "HandleDragAndDrop");
            Require((bool)Get(controller, "dragNeedsOpenHandAfterPlacement"),
                "Losing the hand must not count as opening it after placement.");
            Set(input, "<IsHandVisible>k__BackingField", true);
            input.isPinching = false;
            Set(input, "<JustPinched>k__BackingField", false);
            Call(controller, "HandleDragAndDrop");
            Set(input, "<CurrentScreenPos>k__BackingField", (Vector2)camera.WorldToScreenPoint(placed.transform.position));
            input.isPinching = true;
            Set(input, "<JustPinched>k__BackingField", true);
            Physics.SyncTransforms();
            Call(controller, "HandleDragAndDrop");
            Require(controller.IsDragging && controller.DraggedObject == placed,
                "After a real open-hand sample, a fresh pinch must still grab normally.");
            results.Add("PASS: camera motion cannot move placed content through a held/re-detected placement pinch; fresh open-then-pinch can drag");

            controller.EndSurfaceGuidance();
            Require(!hud.IsVisible && !hud.gameObject.activeSelf && group.alpha == 0f,
                "Leaving AR must hide the guidance immediately, without leaving a welcome-screen overlay.");
            results.Add("PASS: leaving AR immediately removes the top guidance without changing placed content");
        }
        finally
        {
            state.SetValue(null, previousState);
            if (controller != null)
            {
                controller.ClearSelectedComponent();
                controller.ClearPreview();
                foreach (string field in new[] { "selectionHighlightMat", "selectionInvalidMat" })
                    if (Get(controller, field) is Material material) UnityEngine.Object.DestroyImmediate(material);
            }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }
    private static object Get(object value, string field) => value.GetType().GetField(field, Private).GetValue(value);
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, Private).SetValue(value, data);
    private static void Call(object value, string method, params object[] data) => value.GetType().GetMethod(method, Private).Invoke(value, data);
    private static void Require(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }
}
