using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>Owns the headset controls while the existing AR session renders and tracks the circuit.</summary>
[RequireComponent(typeof(Camera))]
public sealed class PhoneHeadsetAR : MonoBehaviour
{
    private static readonly Color32 Backdrop = new(18, 24, 36, 249);
    private static readonly Color32 Header = new(35, 63, 138, 255);
    private static readonly Color32 Teal = new(21, 85, 93, 255);
    private static readonly Color32 Violet = new(77, 53, 137, 255);
    private static readonly Color32 Red = new(119, 47, 56, 255);
    private static readonly Color32 Neutral = new(42, 56, 77, 255);
    private const float MinimumMenuDepth = 0.60f;

    private static readonly (string Key, string Label)[] Components =
    {
        ("Pin", "Pin"), ("Den", "Đèn"), ("Cong Tac", "Công tắc"),
        ("Ampe Ke", "Ampe kế"), ("Von Ke", "Vôn kế")
    };

    private Camera arCamera;
    private TapToPlaceController placement;
    private WireConnectionController wires;
    private ButtonInteractor hand;
    private Action returnToMenu;
    private Canvas panel;
    private readonly List<RectTransform> buttons = new();
    private readonly List<Renderer> componentRenderers = new();
    private RectTransform componentToolbar;
    private RectTransform rotateAction;
    private RectTransform deleteAction;
    private GameObject toolbarTarget;
    private RectTransform currentPage;
    private Action pendingMenuAction;
    private RectTransform simulationPage;
    private TextMeshProUGUI simulationStatus;
    private TextMeshProUGUI wireHint;
    private bool previousTapToPlace;
    private ScreenOrientation previousOrientation;
    private bool previousFullscreen;
    private PhoneHeadsetStereo stereo;
    private readonly List<Canvas> hiddenOverlays = new();

    private RectTransform dragHandleHitbox;
    private Image dragHandleVisual;
    private bool isDraggingMenu;
    private bool isHoveringDragHandle;
    private Plane menuDragPlane;
    private Vector3 menuDragGripOffset;

    public bool IsActive { get; private set; }
    public PhoneHeadsetStereo Stereo => stereo;
    public Vector4 Cursor => hand != null ? hand.GetHeadsetCursor() : Vector4.zero;

    public void Enter(TapToPlaceController controller, ButtonInteractor interactor, Action onExit)
    {
        if (IsActive) return;
        arCamera = GetComponent<Camera>();
        placement = controller;
        wires = placement.wireConnectionController != null
            ? placement.wireConnectionController : FindFirstObjectByType<WireConnectionController>();
        if (wires != null) wires.IsWireMode = false;
        hand = interactor;
        returnToMenu = onExit;
        previousTapToPlace = placement.enableTapToPlace;
        placement.enableTapToPlace = false;
        previousOrientation = Screen.orientation;
        previousFullscreen = Screen.fullScreen;
        if (Application.isMobilePlatform)
        {
            Screen.fullScreen = true;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
        }
        hand.SetHeadsetCamera(arCamera);
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!canvas.enabled || canvas.renderMode == RenderMode.WorldSpace) continue;
            hiddenOverlays.Add(canvas);
            canvas.enabled = false;
        }
        stereo = new PhoneHeadsetStereo(arCamera);
        IsActive = true;
    }

    private void LateUpdate()
    {
        if (!IsActive) return;
        var action = pendingMenuAction;
        pendingMenuAction = null;
        action?.Invoke();
        if (!IsActive) return;
        stereo.Prepare();

        // Use the camera's full viewport for both AR raycasts and hand input.
        // The display pass splits only the final image.
        bool tracking = ARSession.state == ARSessionState.SessionTracking && stereo.IsReady;
        if (tracking && panel == null) CreatePanel();
        if (panel != null && panel.gameObject.activeSelf != tracking)
            panel.gameObject.SetActive(tracking);

        if (tracking && panel != null)
        {
            UpdateMenuDrag();
            UpdateComponentToolbar();
            UpdateWorkStatus();
        }
        else
        {
            isDraggingMenu = false;
            isHoveringDragHandle = false;
        }
    }

    public void Exit()
    {
        if (!IsActive) return;
        IsActive = false;
        pendingMenuAction = null;
        if (CircuitSimulationManager.Instance != null)
            CircuitSimulationManager.Instance.SetSimulationMode(false);
        isDraggingMenu = false;
        isHoveringDragHandle = false;
        dragHandleHitbox = null;
        dragHandleVisual = null;
        if (wires != null) wires.IsWireMode = false;
        wires = null;
        stereo?.Dispose();
        stereo = null;
        foreach (var overlay in hiddenOverlays)
            if (overlay != null) overlay.enabled = true;
        hiddenOverlays.Clear();
        if (hand != null)
        {
            hand.SetHeadsetCamera(null);
            foreach (var button in buttons) hand.UnregisterButton(button);
        }
        if (panel != null)
        {
            panel.gameObject.SetActive(false);
            Destroy(panel.gameObject);
        }
        panel = null;
        currentPage = null;
        simulationPage = null;
        simulationStatus = null;
        wireHint = null;
        buttons.Clear();
        componentRenderers.Clear();
        componentToolbar = rotateAction = deleteAction = null;
        toolbarTarget = null;
        if (placement != null)
        {
            placement.enableTapToPlace = previousTapToPlace;
            placement.CancelPlacement();
        }
        if (Application.isMobilePlatform)
        {
            Screen.orientation = previousOrientation;
            Screen.fullScreen = previousFullscreen;
        }
        returnToMenu = null;
    }

    private void OnDisable() => Exit();

    private void CreatePanel()
    {
        const float distance = PhoneHeadsetStereo.ConvergenceDistance;
        var root = new GameObject("Headset Controls", typeof(RectTransform), typeof(Canvas));
        panel = root.GetComponent<Canvas>();
        panel.renderMode = RenderMode.WorldSpace;
        panel.worldCamera = arCamera;

        // Anchor all pages to the same pose and leave room around the lenses.
        Vector3 position = arCamera.ViewportToWorldPoint(new Vector3(0.51f, 0.56f, distance));
        Vector3 rightEdge = arCamera.ViewportToWorldPoint(new Vector3(0.70f, 0.56f, distance));
        var rect = (RectTransform)root.transform;
        rect.SetPositionAndRotation(position, arCamera.transform.rotation);
        rect.localScale = Vector3.one * (2f * Vector3.Distance(position, rightEdge) / 600f);

        // Kích thước các trang lớn, thoáng đãng, các nút to gấp nhiều lần để pinch cực dễ
        var home = CreatePage("MENU", 410f, 160f, 64f);
        var toolbox = CreatePage("KHO LINH KIỆN", 510f, 205f, 72f);
        var wireTools = CreatePage("DÂY", 340f, 125f, 64f);
        var connecting = CreateWireWorkPage("Nối dây", wireTools, out var connectHint);
        var deleting = CreateWireWorkPage("Xóa dây", wireTools, out var deleteHint);
        simulationPage = CreateSimulationPage(home);

        // --- TRANG MENU CHÍNH (home) ---
        // Hàng 1 (3 nút chính to lớn): Kho linh kiện, Dây, Thoát kính (Cách tiêu đề 25px thoáng đãng)
        AddButton(home, "Kho linh kiện", new Vector2(-230f, 40f), new Vector2(210f, 126f), Teal,
            () => ShowPage(toolbox), UIIconAssets.GetHeadsetToolboxSprite());
        var wireButton = AddButton(home, "Dây", new Vector2(0f, 40f), new Vector2(210f, 126f), Violet,
            () => ShowPage(wireTools), UIIconAssets.GetWireModeSprite());
        wireButton.interactable = wires != null;
        AddButton(home, "Thoát kính", new Vector2(230f, 40f), new Vector2(210f, 126f), Red,
            () => returnToMenu?.Invoke(), UIIconAssets.GetExitSprite());

        AddButton(home, "Hoàn tác", new Vector2(-230f, -108f), new Vector2(210f, 114f), Violet,
            OnClickUndo, UIIconAssets.GetUndoSprite());
        AddButton(home, "Làm lại", new Vector2(230f, -108f), new Vector2(210f, 114f), Violet,
            OnClickRedo, UIIconAssets.GetRedoSprite());
        var simulate = AddButton(home, "Mô phỏng", new Vector2(0f, -108f), new Vector2(210f, 114f), Teal,
            StartSimulation, UIIconAssets.GetSimulationSprite());
        simulate.interactable = CircuitSimulationManager.Instance != null && CircuitSolver.Instance != null;

        // --- TRANG KHO LINH KIỆN (toolbox) ---
        AddBackButton(toolbox, () =>
        {
            placement.CancelPlacement();
            ShowPage(home);
        });

        // Nút Hủy đặt to lớn (170x66) bên phải header, cân đối hoàn hảo với Quay lại
        AddButton(toolbox, "Hủy đặt", new Vector2(242f, 205f), new Vector2(170f, 66f), Red, () =>
        {
            placement.CancelPlacement();
        });

        // Hàng 1: Pin, Đèn, Công tắc (Kích thước to 210x126, cách tiêu đề 54px thoáng đãng, không đè)
        AddButton(toolbox, "Pin", new Vector2(-230f, 52f), new Vector2(210f, 126f), Teal,
            () => placement.StartPreview("Pin"), UIIconAssets.GetComponentSprite("Pin"));

        AddButton(toolbox, "Đèn", new Vector2(0f, 52f), new Vector2(210f, 126f), Teal,
            () => placement.StartPreview("Den"), UIIconAssets.GetComponentSprite("Den"));

        AddButton(toolbox, "Công tắc", new Vector2(230f, 52f), new Vector2(210f, 126f), Teal,
            () => placement.StartPreview("Cong Tac"), UIIconAssets.GetComponentSprite("Cong Tac"));

        // Hàng 2: Ampe kế, Vôn kế, Hoàn tác, Làm lại (4 nút to 152x120, cách hàng 1 hợp lý)
        AddButton(toolbox, "Ampe kế", new Vector2(-246f, -95f), new Vector2(152f, 120f), Neutral,
            () => placement.StartPreview("Ampe Ke"), UIIconAssets.GetComponentSprite("Ampe Ke"));

        AddButton(toolbox, "Vôn kế", new Vector2(-82f, -95f), new Vector2(152f, 120f), Neutral,
            () => placement.StartPreview("Von Ke"), UIIconAssets.GetComponentSprite("Von Ke"));

        AddButton(toolbox, "Hoàn tác", new Vector2(82f, -95f), new Vector2(152f, 120f), Violet,
            OnClickUndo, UIIconAssets.GetUndoSprite());

        AddButton(toolbox, "Làm lại", new Vector2(246f, -95f), new Vector2(152f, 120f), Violet,
            OnClickRedo, UIIconAssets.GetRedoSprite());

        // --- TRANG DÂY (wireTools) ---
        AddBackButton(wireTools, () => ShowPage(home));
        AddButton(wireTools, "Nối dây", new Vector2(-165f, 10f), new Vector2(300f, 122f), Violet,
            () => StartWireMode(WireConnectionController.WireSubMode.Connect, connecting, connectHint), UIIconAssets.GetWireModeSprite());
        AddButton(wireTools, "Xóa dây", new Vector2(165f, 10f), new Vector2(300f, 122f), Red,
            () => StartWireMode(WireConnectionController.WireSubMode.Delete, deleting, deleteHint), UIIconAssets.GetWireModeSprite());

        dragHandleHitbox = CreateRect("Drag Handle Hitbox", panel.transform, Vector2.zero, new Vector2(300f, 68f));
        var bar = CreateSurface("Visual Bar", dragHandleHitbox, Vector2.zero, new Vector2(230f, 22f),
            new Color32(255, 255, 255, 215));
        dragHandleVisual = bar.GetComponent<Image>();
        var outline = bar.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(0, 0, 0, 120);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        CreateComponentToolbar();
        ShowPage(home);
    }

    private void ShowPage(RectTransform page)
    {
        if (currentPage != null) currentPage.gameObject.SetActive(false);
        currentPage = page;
        currentPage.gameObject.SetActive(true);
        UpdateDragHandlePosition();
    }

    private void UpdateDragHandlePosition()
    {
        if (dragHandleHitbox == null || currentPage == null) return;
        float bottomY = currentPage.anchoredPosition.y - (currentPage.sizeDelta.y + dragHandleHitbox.sizeDelta.y) * 0.5f;
        dragHandleHitbox.anchoredPosition = new Vector2(0f, bottomY);
    }

    internal bool HandleMenuPointer(Vector2 screenPoint, bool pinchStarted, bool overComponentAction)
    {
        if (!IsActive || panel == null || !panel.isActiveAndEnabled || !stereo.IsReady ||
            ARSession.state != ARSessionState.SessionTracking || !hand.IsHandVisible) return false;

        if (!hand.isPinching) isDraggingMenu = false;
        if (isDraggingMenu)
        {
            hand.PinchConsumedByUI = true;
            return true;
        }

        // A new pinch may take over from a released component still in its release grace period.
        isHoveringDragHandle = !overComponentAction && (!placement.IsDragging || pinchStarted) &&
            HitsInEitherEye(dragHandleHitbox, screenPoint);
        if (isHoveringDragHandle && pinchStarted && !hand.PinchConsumedByUI)
        {
            hand.PinchConsumedByUI = true;
            Vector3 forward = arCamera.transform.forward;
            float depth = Vector3.Dot(panel.transform.position - arCamera.transform.position, forward);
            Vector3 grip = dragHandleHitbox.position + forward * Mathf.Max(0f, MinimumMenuDepth - depth);
            menuDragPlane = new Plane(forward, grip);
            Ray ray = arCamera.ScreenPointToRay(screenPoint);
            if (menuDragPlane.Raycast(ray, out float distance))
            {
                menuDragGripOffset = grip - ray.GetPoint(distance);
                isDraggingMenu = true;
            }
        }
        return isHoveringDragHandle;
    }

    private bool HitsInEitherEye(RectTransform rect, Vector2 screenPoint, Vector2 padding = default)
    {
        var plane = new Plane(rect.forward, rect.position);
        Rect area = rect.rect;
        area.min -= padding;
        area.max += padding;
        for (int eye = 0; eye < 2; eye++)
        {
            if (PhoneHeadsetStereo.TryGetEyeRay(arCamera, screenPoint, eye == 1, out Ray ray) &&
                plane.Raycast(ray, out float distance) &&
                area.Contains(rect.InverseTransformPoint(ray.GetPoint(distance))))
                return true;
        }
        return false;
    }

    private void UpdateMenuDrag()
    {
        if (!hand.IsHandVisible || !hand.isPinching) isDraggingMenu = false;
        if (isDraggingMenu) MoveMenu(hand.CurrentScreenPos, Time.unscaledDeltaTime);

        bool hovered = isHoveringDragHandle && hand.IsHandVisible;
        dragHandleVisual.color = isDraggingMenu ? new Color32(100, 220, 255, 255) :
            hovered ? new Color32(220, 247, 255, 255) : new Color32(255, 255, 255, 215);
    }

    private void MoveMenu(Vector2 screenPoint, float deltaTime)
    {
        Ray ray = arCamera.ScreenPointToRay(screenPoint);
        if (!menuDragPlane.Raycast(ray, out float distance)) return;

        // Keep both the free drag and its viewport limits on the same world plane.
        Vector3 grip = ray.GetPoint(distance) + menuDragGripOffset;
        Vector3 viewport = arCamera.WorldToViewportPoint(grip);
        if (viewport.z <= arCamera.nearClipPlane) return;
        var bounded = new Vector2(Mathf.Clamp(viewport.x, 0.32f, 0.68f), Mathf.Clamp(viewport.y, 0.16f, 0.84f));
        if (bounded != (Vector2)viewport)
        {
            Ray boundedRay = arCamera.ViewportPointToRay(bounded);
            if (!menuDragPlane.Raycast(boundedRay, out distance)) return;
            grip = boundedRay.GetPoint(distance);
        }

        Vector3 position = panel.transform.position;
        Vector3 desired = grip - (dragHandleHitbox.position - position);
        if (arCamera.WorldToViewportPoint(desired).z < MinimumMenuDepth - 0.001f) return;
        deltaTime = Mathf.Min(deltaTime, 0.05f);
        Vector3 smoothed = Vector3.Lerp(position, desired, 1f - Mathf.Exp(-24f * deltaTime));
        panel.transform.position = Vector3.MoveTowards(position, smoothed, 2f * deltaTime);
    }

    private void CreateComponentToolbar()
    {
        componentToolbar = CreateSurface("Linh kiện đang chọn", panel.transform, Vector2.zero,
            new Vector2(380f, 104f), Backdrop);
        var rotate = AddButton(componentToolbar, "Xoay 90°", new Vector2(-79f, 0f), new Vector2(196f, 84f),
            Violet, () =>
            {
                if (CanEditToolbarTarget()) placement.RotateSelectedComponent();
            }, ARFloatingBubbleMenu.GetRotateIconSprite());
        rotateAction = (RectTransform)rotate.transform;

        const string deleteLabel = "Giữ để xóa";
        var delete = AddButton(componentToolbar, deleteLabel, new Vector2(113f, 0f), new Vector2(122f, 82f),
            Red, null, ARFloatingBubbleMenu.GetTrashIconSprite());
        deleteAction = (RectTransform)delete.transform;
        var progress = CreateSurface("Tiến trình giữ", deleteAction, new Vector2(0f, -35f),
            new Vector2(98f, 4f), Color.white).GetComponent<Image>();
        progress.type = Image.Type.Filled;
        progress.fillMethod = Image.FillMethod.Horizontal;
        progress.gameObject.SetActive(false);

        var hold = delete.gameObject.AddComponent<HoldToActivateButton>();
        hold.holdDuration = 0.55f;
        hold.pinchReleaseGrace = 0f;
        hold.defaultLabel = deleteLabel;
        hold.holdingLabel = "Xóa";
        hold.labelTMP = delete.GetComponentInChildren<TextMeshProUGUI>();
        hold.progressRingImage = progress;
        hold.onHoldComplete = () => pendingMenuAction = () =>
        {
            if (CanEditToolbarTarget()) placement.DeleteSelectedComponent();
        };
        componentToolbar.gameObject.SetActive(false);
    }

    internal bool IsComponentAction(RectTransform rect) => rect == rotateAction || rect == deleteAction;

    internal bool TryGetComponentActionAt(Vector2 screenPoint, out RectTransform action)
    {
        action = null;
        if (!CanEditToolbarTarget() || !HitsInEitherEye(componentToolbar, screenPoint)) return false;
        bool overRotate = HitsInEitherEye(rotateAction, screenPoint, new Vector2(10f, 5f));
        bool overDelete = HitsInEitherEye(deleteAction, screenPoint);
        if (overRotate && (!overDelete ||
            RectTransformUtility.RectangleContainsScreenPoint(rotateAction, screenPoint, arCamera)))
            action = rotateAction;
        else if (overDelete && !overRotate) action = deleteAction;
        return true;
    }

    private bool CanEditComponent()
    {
        return IsActive && stereo?.IsReady == true && ARSession.state == ARSessionState.SessionTracking && !isDraggingMenu &&
            placement != null && !placement.IsDragging && placement.CurrentPrefabToPlace == null &&
            placement.SelectedComponent != null && placement.SelectedComponent.activeInHierarchy &&
            (wires == null || !wires.IsWireMode) && CircuitSimulationManager.Instance?.isSimulationMode != true;
    }

    private bool CanEditToolbarTarget() => componentToolbar != null && componentToolbar.gameObject.activeInHierarchy &&
        CanEditComponent() && toolbarTarget == placement.SelectedComponent;

    private void UpdateComponentToolbar()
    {
        if (!CanEditComponent())
        {
            if (componentToolbar.gameObject.activeSelf) componentToolbar.gameObject.SetActive(false);
            return;
        }

        if (toolbarTarget != placement.SelectedComponent)
        {
            componentToolbar.gameObject.SetActive(false);
            toolbarTarget = placement.SelectedComponent;
            toolbarTarget.GetComponentsInChildren(true, componentRenderers);
        }
        bool visible = TryPositionComponentToolbar();
        if (componentToolbar.gameObject.activeSelf != visible) componentToolbar.gameObject.SetActive(visible);
    }

    private bool TryPositionComponentToolbar()
    {
        Bounds bounds = default;
        bool found = false;
        foreach (var renderer in componentRenderers)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                !renderer.gameObject.activeInHierarchy || renderer is LineRenderer) continue;
            if (found) bounds.Encapsulate(renderer.bounds);
            else bounds = renderer.bounds;
            found = true;
        }
        if (!found) return false;

        Vector3 center = arCamera.WorldToViewportPoint(bounds.center);
        if (center.z <= arCamera.nearClipPlane) return false;
        Vector2 min = center;
        Vector2 max = center;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = arCamera.WorldToViewportPoint(bounds.center +
                Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
            if (corner.z <= arCamera.nearClipPlane) return false;
            min = Vector2.Min(min, corner);
            max = Vector2.Max(max, corner);
        }
        var visiblePoint = new Vector2(Mathf.Clamp(0.5f, min.x, max.x), Mathf.Clamp(0.5f, min.y, max.y));
        if (!PhoneHeadsetStereo.ContainsSourcePoint(visiblePoint)) return false;

        const float halfWidth = 0.114f;
        float halfHeight = halfWidth * componentToolbar.rect.height / componentToolbar.rect.width *
            arCamera.pixelWidth / arCamera.pixelHeight;
        const float left = 0.30f, right = 0.70f, bottom = 0.11f, top = 0.89f;
        float rowX = Mathf.Clamp(center.x, left + halfWidth, right - halfWidth);
        float sideY = Mathf.Clamp(center.y, bottom + halfHeight, top - halfHeight);
        const float gapX = 0.018f, gapY = 0.025f;

        bool Place(Vector2 viewport)
        {
            if (viewport.x - halfWidth < left - 0.001f || viewport.x + halfWidth > right + 0.001f ||
                viewport.y - halfHeight < bottom - 0.001f || viewport.y + halfHeight > top + 0.001f) return false;
            Vector3 position = arCamera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, center.z));
            Vector3 edge = arCamera.ViewportToWorldPoint(new Vector3(viewport.x + halfWidth, viewport.y, center.z));
            componentToolbar.SetPositionAndRotation(position, arCamera.transform.rotation);
            componentToolbar.localScale = Vector3.one *
                (2f * Vector3.Distance(position, edge) / (componentToolbar.rect.width * panel.transform.lossyScale.x));
            return true;
        }

        return Place(new Vector2(rowX, max.y + gapY + halfHeight)) ||
            Place(new Vector2(max.x + gapX + halfWidth, sideY)) ||
            Place(new Vector2(min.x - gapX - halfWidth, sideY)) ||
            Place(new Vector2(rowX, min.y - gapY - halfHeight));
    }

    private void OnClickUndo()
    {
        if (placement != null)
        {
            placement.CancelPlacement();
            placement.UndoLastPlacedComponent();
        }
    }

    private void OnClickRedo()
    {
        if (placement != null)
        {
            placement.CancelPlacement();
            placement.RedoLastAction();
        }
    }

    private void StartWireMode(WireConnectionController.WireSubMode mode, RectTransform page, TextMeshProUGUI hint)
    {
        if (wires == null) return;
        placement.CancelPlacement();
        wires.SetWireSubMode(mode);
        ShowPage(page);
        wireHint = hint;
    }

    private RectTransform CreateWireWorkPage(string title, RectTransform wireTools, out TextMeshProUGUI hint)
    {
        var page = CreateSurface(title + " (đang thao tác)", panel.transform, new Vector2(0f, 110f),
            new Vector2(680f, 176f), Backdrop);
        AddBackButton(page, () =>
        {
            if (wires != null) wires.IsWireMode = false;
            wireHint = null;
            ShowPage(wireTools);
        });
        AddLabel(page, title, new Vector2(0f, 60f), new Vector2(620f, 36f), 30f);
        hint = AddLabel(page, string.Empty, new Vector2(0f, 24f), new Vector2(620f, 34f), 24f);
        page.gameObject.SetActive(false);
        return page;
    }

    private void StartSimulation()
    {
        var manager = CircuitSimulationManager.Instance;
        if (manager == null || CircuitSolver.Instance == null) return;
        placement.CancelPlacement();
        if (wires != null) wires.IsWireMode = false;
        manager.SetSimulationMode(true);
        ShowPage(simulationPage);
    }

    private RectTransform CreateSimulationPage(RectTransform home)
    {
        var page = CreateSurface("MÔ PHỎNG", panel.transform, new Vector2(0f, 110f),
            new Vector2(680f, 148f), Backdrop);
        AddButton(page, "Dừng mô phỏng", new Vector2(190f, 33f), new Vector2(250f, 62f), Red, () =>
        {
            if (CircuitSimulationManager.Instance != null)
                CircuitSimulationManager.Instance.SetSimulationMode(false);
            ShowPage(home);
        });
        simulationStatus = AddLabel(page, "Mạch hở", new Vector2(-145f, 33f), new Vector2(350f, 52f), 30f);
        AddLabel(page, "Chụm ngón tay vào công tắc để đóng hoặc mở mạch", new Vector2(0f, -37f),
            new Vector2(640f, 45f), 23f);
        page.gameObject.SetActive(false);
        return page;
    }

    private void UpdateWorkStatus()
    {
        if (wireHint != null && wires != null && wires.IsWireMode)
        {
            bool deleting = wires.CurrentSubMode == WireConnectionController.WireSubMode.Delete;
            string hint = wires.HasSelectedTerminal
                ? (deleting ? "Chọn đầu còn lại của dây cần xóa" : "Chọn cực thứ hai để nối dây")
                : (deleting ? "Chọn đầu thứ nhất của dây cần xóa" : "Chọn cực thứ nhất để nối dây");
            if (wireHint.text != hint) wireHint.text = hint;
        }

        if (currentPage != simulationPage || simulationStatus == null) return;
        var solver = CircuitSolver.Instance;
        if (solver == null) return;
        string status = solver.isShortCircuit ? "Ngắn mạch" : solver.isCircuitClosed ? "Mạch kín" : "Mạch hở";
        if (simulationStatus.text == status) return;
        simulationStatus.text = status;
        simulationStatus.color = solver.isShortCircuit ? new Color32(255, 150, 150, 255) : Color.white;
    }

    private RectTransform CreatePage(string title, float height, float headerY = 0f, float headerHeight = 66f)
    {
        const float width = 720f;
        var page = CreateSurface(title, panel.transform, Vector2.zero, new Vector2(width, height), Backdrop);
        float hy = headerY != 0f ? headerY : (height * 0.5f - 45f);
        var heading = CreateSurface("Tiêu đề", page, new Vector2(0f, hy),
            new Vector2(width - 50f, headerHeight), Header);
        // Tiêu đề luôn căn giữa hoàn hảo, không bị lệch
        AddLabel(heading, title, Vector2.zero, new Vector2(280f, 50f), 30f);
        page.gameObject.SetActive(false);
        return page;
    }

    private void AddBackButton(RectTransform page, Action action) =>
        AddButton(page, "Quay lại", new Vector2(0f, 53f - page.rect.height * 0.5f),
            new Vector2(276f, 78f), Header, action);

    private Button AddButton(RectTransform parent, string caption, Vector2 position, Vector2 size, Color color,
        Action action, Sprite glyph = null)
    {
        var rect = CreateSurface(caption, parent, position, size, color);
        var background = rect.GetComponent<Image>();
        background.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;
        // Apply page and mode changes after all pinch consumers have processed this frame.
        if (action != null) button.onClick.AddListener(() => pendingMenuAction = action);
        buttons.Add(rect);
        hand.RegisterButton(rect);

        if (glyph != null)
        {
            float iconDim = size.y >= 120f ? (size.x < 170f ? 52f : 60f) : 44f;
            float iconY = size.y >= 120f ? 20f : 14f;
            float labelY = size.y >= 120f ? -36f : -22f;
            float maxFont = size.y >= 120f ? 25f : 21f;

            var iconRect = CreateRect("Biểu tượng", rect, new Vector2(0f, iconY), new Vector2(iconDim, iconDim));
            var icon = iconRect.gameObject.AddComponent<Image>();
            icon.sprite = glyph;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            AddLabel(rect, caption, new Vector2(0f, labelY), new Vector2(size.x - 10f, 32f), maxFont);
        }
        else
        {
            float maxFont = size.y > 60f ? 26f : 22f;
            AddLabel(rect, caption, Vector2.zero,
                new Vector2(size.x - 12f, size.y - 8f), maxFont);
        }
        return button;
    }

    private static RectTransform CreateRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static RectTransform CreateSurface(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var rect = CreateRect(name, parent, position, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = WelcomeScreenController.GetRoundedRectSprite();
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 2.5f;
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private static TextMeshProUGUI AddLabel(RectTransform parent, string text, Vector2 position, Vector2 size, float maxSize)
    {
        var rect = CreateRect("Nhãn", parent, position, size);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = WelcomeScreenController.GetSafeFont();
        label.enableAutoSizing = true;
        label.fontSizeMin = 19f;
        label.fontSizeMax = maxSize;
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color32(247, 251, 255, 255);
        label.text = text;
        label.raycastTarget = false;
        return label;
    }
}
