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

    // Thanh trắng điều khiển kéo di chuyển menu trong không gian 3D (Drag Handle / Home Indicator)
    private RectTransform dragHandleHitbox;
    private Image dragHandleVisual;
    private Outline dragHandleOutline;
    private bool isDraggingMenu;
    private float dragDistance;
    private Vector3 dragOffset;

    private float lastHandlePinchTime = -1f;

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

        // Xử lý kéo di chuyển menu khi cầm vào thanh trắng
        if (panel != null && panel.gameObject.activeSelf)
        {
            UpdateMenuDrag();
            UpdateWorkStatus();
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
        dragHandleHitbox = null;
        dragHandleVisual = null;
        dragHandleOutline = null;
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
        // Nút Căn trước mặt ở góc trên bên phải header
        AddButton(home, "Căn trước mặt", new Vector2(242f, 160f), new Vector2(170f, 56f), Neutral, RecenterMenu);

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

        // Tạo thanh trắng dưới menu (Drag Handle dạng Home Indicator) để cầm và di chuyển menu trong không gian 3D
        var handleObj = new GameObject("Drag Handle Hitbox", typeof(RectTransform), typeof(Image));
        handleObj.transform.SetParent(panel.transform, false);
        dragHandleHitbox = handleObj.GetComponent<RectTransform>();
        dragHandleHitbox.sizeDelta = new Vector2(220f, 44f);
        dragHandleHitbox.anchoredPosition = new Vector2(0f, -200f);

        var hitImg = handleObj.GetComponent<Image>();
        hitImg.color = new Color(0f, 0f, 0f, 0.005f); // Nhận diện cử chỉ tay MediaPipe qua raycast
        hitImg.raycastTarget = true;

        // Thanh trắng trực quan bo tròn góc
        var barObj = new GameObject("Visual Bar", typeof(RectTransform), typeof(Image), typeof(Outline));
        barObj.transform.SetParent(dragHandleHitbox, false);
        var barRT = barObj.GetComponent<RectTransform>();
        barRT.sizeDelta = new Vector2(180f, 12f);
        barRT.anchoredPosition = Vector2.zero;

        dragHandleVisual = barObj.GetComponent<Image>();
        dragHandleVisual.sprite = WelcomeScreenController.GetRoundedRectSprite();
        dragHandleVisual.type = Image.Type.Sliced;
        dragHandleVisual.pixelsPerUnitMultiplier = 3f;
        dragHandleVisual.color = new Color32(255, 255, 255, 215);
        dragHandleVisual.raycastTarget = false;

        dragHandleOutline = barObj.GetComponent<Outline>();
        dragHandleOutline.effectColor = new Color32(0, 0, 0, 120);
        dragHandleOutline.effectDistance = new Vector2(1.5f, -1.5f);
        dragHandleOutline.enabled = true;

        buttons.Add(dragHandleHitbox);
        hand.RegisterButton(dragHandleHitbox);

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
        // Tự động định vị thanh kéo bám sát mép đáy của trang hiện tại
        float bottomY = currentPage.anchoredPosition.y - currentPage.sizeDelta.y * 0.5f - 22f;
        dragHandleHitbox.anchoredPosition = new Vector2(0f, bottomY);
    }

    private void UpdateMenuDrag()
    {
        if (panel == null || hand == null || arCamera == null || dragHandleHitbox == null) return;

        // Tuyệt đối không nhận kéo menu nếu người dùng đang kéo linh kiện trên bàn hoặc đang đặt linh kiện mới
        bool isHandlingComponent = placement != null && (placement.IsDragging || placement.CurrentPrefabToPlace != null);

        bool isHoveringHandle = hand.IsHandVisible && !isHandlingComponent &&
            RectTransformUtility.RectangleContainsScreenPoint(dragHandleHitbox, hand.CurrentScreenPos, arCamera);

        // Bắt đầu cầm kéo menu CHỈ KHI người dùng chủ động pinch ngay tại thanh trắng (JustPinched)
        // Tuyệt đối không nhận nhầm khi đang kéo vật từ dưới lên rồi ngón tay lướt qua thanh
        if (!isDraggingMenu)
        {
            if (isHoveringHandle && hand.JustPinched && !isHandlingComponent)
            {
                // Chụm ngón tay 2 lần liên tiếp (double-pinch) trên thanh trắng -> Tự động căn menu về ngay trước mặt
                if (Time.unscaledTime - lastHandlePinchTime < 0.45f)
                {
                    RecenterMenu();
                    lastHandlePinchTime = -1f;
                    return;
                }
                lastHandlePinchTime = Time.unscaledTime;

                isDraggingMenu = true;
                hand.PinchConsumedByUI = true;
                dragDistance = Mathf.Clamp(Vector3.Distance(arCamera.transform.position, panel.transform.position), 0.35f, 1.2f);
                Ray ray = arCamera.ScreenPointToRay(hand.CurrentScreenPos);
                Vector3 hitPoint = ray.GetPoint(dragDistance);
                dragOffset = panel.transform.position - hitPoint;
            }
        }
        else
        {
            // Di chuyển menu theo vị trí ngón tay trong không gian 3D
            if (hand.isPinching && hand.IsHandVisible)
            {
                hand.PinchConsumedByUI = true;
                Ray ray = arCamera.ScreenPointToRay(hand.CurrentScreenPos);
                Vector3 desiredPos = ray.GetPoint(dragDistance) + dragOffset;

                // 1. Chống giật văng đột ngột: Nếu vị trí mong muốn giật quá xa (> 0.45m trong 1 frame),
                // đây chắc chắn là lỗi nhiễu landmark MediaPipe hoặc tay ra vào mép khung hình
                Vector3 displacement = desiredPos - panel.transform.position;
                if (displacement.magnitude > 0.45f)
                {
                    desiredPos = panel.transform.position + displacement.normalized * 0.45f;
                }

                // 2. Di chuyển mượt mà có giới hạn tốc độ tối đa (2.0 m/s)
                float maxSpeed = 2.0f;
                Vector3 step = Vector3.MoveTowards(panel.transform.position, desiredPos, maxSpeed * Time.unscaledDeltaTime);
                panel.transform.position = Vector3.Lerp(panel.transform.position, step, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));

                // 3. Giữ menu luôn nằm trong hình nón quan sát phía trước mặt người dùng, không bao giờ văng ra sau lưng hay trôi mất sang phải
                Vector3 localPos = arCamera.transform.InverseTransformPoint(panel.transform.position);
                localPos.z = Mathf.Clamp(localPos.z, 0.35f, 1.2f);
                localPos.x = Mathf.Clamp(localPos.x, -0.60f, 0.60f); // Giới hạn lệch trái/phải tối đa 60cm
                localPos.y = Mathf.Clamp(localPos.y, -0.45f, 0.45f); // Giới hạn lệch trên/dưới tối đa 45cm
                panel.transform.position = arCamera.transform.TransformPoint(localPos);

                // 4. Menu tự động quay mặt nhẹ nhàng về hướng camera (billboard)
                Vector3 toCam = panel.transform.position - arCamera.transform.position;
                if (toCam.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(toCam, Vector3.up);
                    panel.transform.rotation = Quaternion.Slerp(panel.transform.rotation, targetRot, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
                }
            }
            else
            {
                // Nhả ngón tay -> Cố định menu tại vị trí mới trong World Space
                isDraggingMenu = false;
            }
        }

        // Hiệu ứng phản hồi thị giác trên thanh kéo
        if (dragHandleVisual != null)
        {
            if (isDraggingMenu)
            {
                dragHandleVisual.color = new Color32(100, 220, 255, 255); // Xanh cyan khi đang cầm kéo
                dragHandleVisual.rectTransform.localScale = Vector3.Lerp(dragHandleVisual.rectTransform.localScale, new Vector3(1.15f, 1.25f, 1f), Time.deltaTime * 20f);
            }
            else if (isHoveringHandle)
            {
                dragHandleVisual.color = new Color32(255, 255, 255, 255); // Trắng sáng khi hover
                dragHandleVisual.rectTransform.localScale = Vector3.Lerp(dragHandleVisual.rectTransform.localScale, new Vector3(1.08f, 1.15f, 1f), Time.deltaTime * 20f);
            }
            else
            {
                dragHandleVisual.color = new Color32(255, 255, 255, 200); // Trắng ngà nhẹ nhàng
                dragHandleVisual.rectTransform.localScale = Vector3.Lerp(dragHandleVisual.rectTransform.localScale, Vector3.one, Time.deltaTime * 15f);
            }
        }
    }

    public void RecenterMenu()
    {
        if (panel == null || arCamera == null) return;
        const float distance = PhoneHeadsetStereo.ConvergenceDistance;
        Vector3 targetPos = arCamera.ViewportToWorldPoint(new Vector3(0.51f, 0.56f, distance));
        panel.transform.position = targetPos;
        Vector3 toCam = panel.transform.position - arCamera.transform.position;
        if (toCam.sqrMagnitude > 0.01f)
            panel.transform.rotation = Quaternion.LookRotation(toCam, Vector3.up);
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
        button.onClick.AddListener(() => pendingMenuAction = action);
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
