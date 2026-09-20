using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ARFloatingBubbleMenu - Menu Bong Bóng Chat AR (Messenger Floating Action Bubble).
/// 100% điều khiển bằng cử chỉ tay MediaPipe (Zero-touchscreen):
/// 1. Nút Bong Bóng Chat chính (Main Bubble): Bấm/Pinch vào là XÒE RA 3 bong bóng hình tròn, bấm lại là THỤT VÀO.
/// 2. 3 Bong bóng tròn vệ tinh xòe ra hình cánh quạt, hiển thị chính xác các icon/hình ảnh người dùng yêu cầu:
///    - Ô 1: Quét lại mặt phẳng (icon_scan)
///    - Ô 2: Reset mạch điện (icon_reset)
///    - Ô 3: Kho dụng cụ linh kiện (icon_toolbox)
///    Tất cả icon được nhúng trực tiếp qua UIIconAssets (Base64), đảm bảo 100% hiển thị trên mọi thiết bị Android.
/// 3. Toàn bộ chữ hiển thị dùng tiếng Việt chuẩn, loại bỏ hoàn toàn emoji để không bao giờ bị lỗi ô vuông.
/// 4. Vị trí công thái học: Nằm ở VÙNG GIỮA PHÍA DƯỚI (cách đáy 200px, không sát rìa, không sát camera).
/// </summary>
public class ARFloatingBubbleMenu : MonoBehaviour
{
    [Header("--- THAM CHIẾU HỆ THỐNG ---")]
    public ButtonInteractor buttonInteractor;
    public TapToPlaceController tapToPlaceController;
    public WireConnectionController wireConnectionController;
    public SinglePlaneLockController singlePlaneLockController;
    public TwoPointSpatialCalibrator spatialCalibrator;
    public MenuHUDController menuHUDController;

    [Header("--- CẤU HÌNH VỊ TRÍ & KÍCH THƯỚC ---")]
    [Tooltip("Độ cao của bong bóng chat so với mép dưới màn hình (px). Vị trí vàng vùng giữa phía dưới.")]
    [Range(120f, 350f)] public float menuBottomOffset = 200f;

    [Tooltip("Kích thước đường kính của các bong bóng tròn (px)")]
    public float bubbleDiameter = 180f;

    [Tooltip("Tự động tạo giao diện bong bóng chat nếu chưa gán thủ công")]
    public bool autoGenerateUI = true;

    [Tooltip("Tự động hủy hoàn toàn khung menu 3D xanh cũ")]
    public bool disableOldSpatialMenu = true;

    [Header("--- THAM CHIẾU UI BONG BÓNG ---")]
    public RectTransform mainChatBubble;        // Bong bóng chat điều khiển chính
    public RectTransform btnScanBubble;         // Ô tròn 1: Quét lại
    public RectTransform btnResetBubble;        // Ô tròn 2: Reset mạch
    public RectTransform btnToolboxBubble;      // Ô tròn 3: Kho dụng cụ
    public RectTransform toolboxDrawer;         // Khay linh kiện
    public RectTransform[] componentButtons;    // Các nút linh kiện trong kho
    public TextMeshProUGUI mainBubbleLabelText; // Chữ dưới bong bóng chính

    [Header("--- TRẠNG THÁI HIỆN TẠI ---")]
    public bool isBarExpanded = false;
    public bool isToolboxOpen = false;

    private static Sprite circleSprite;
    private static Sprite roundedRectSprite;
    private Canvas targetCanvas;
    private CanvasScaler targetScaler;
    private Coroutine animCoroutine;
    private Coroutine drawerAnimCoroutine;

    // Vị trí mục tiêu khi xòe ra
    private Vector2 posScanTarget;
    private Vector2 posResetTarget;
    private Vector2 posToolboxTarget;
    private Vector2 posMainBubble;

    void Awake()
    {
        AutoFindReferences();
        KillOldSpatialMenu();
    }

    void Start()
    {
        AutoFindReferences();
        KillOldSpatialMenu();

        if (autoGenerateUI && mainChatBubble == null)
        {
            BuildChatBubbleMenuUI();
        }
        else
        {
            RegisterAllButtonsToInteractor();
        }

        // Khởi tạo trạng thái ban đầu: Thụt vào (collapsed) để màn hình AR thoáng đãng
        SetMenuStateImmediate(isBarExpanded);

        // Nếu Màn Hình Chính (Welcome Screen) đang kích hoạt lúc khởi động thì tạm ẩn menu AR
        WelcomeScreenController welcome = WelcomeScreenController.EnsureInstance();
        if (welcome != null && welcome.isWelcomeScreenActive)
        {
            SetMenuVisible(false);
        }
    }

    public Canvas MenuCanvas => targetCanvas;

    public void SetMenuVisible(bool visible)
    {
        if (targetCanvas != null)
        {
            targetCanvas.gameObject.SetActive(visible);
        }
        else
        {
            GameObject canvasObj = GameObject.Find("ARFloatingMenu_Canvas");
            if (canvasObj != null)
            {
                canvasObj.SetActive(visible);
            }
        }
    }

    void Update()
    {
        UpdateCanvasOrientation();
    }

    private void UpdateCanvasOrientation()
    {
        if (targetScaler == null) return;
        bool isLandscape = Screen.width > Screen.height;
        if (isLandscape && targetScaler.referenceResolution.x < targetScaler.referenceResolution.y)
        {
            targetScaler.referenceResolution = new Vector2(1920, 1080);
            targetScaler.matchWidthOrHeight = 1f;
        }
        else if (!isLandscape && targetScaler.referenceResolution.x > targetScaler.referenceResolution.y)
        {
            targetScaler.referenceResolution = new Vector2(1080, 1920);
            targetScaler.matchWidthOrHeight = 0f;
        }
    }

    public void KillOldSpatialMenu()
    {
        if (!disableOldSpatialMenu) return;

#if UNITY_2023_1_OR_NEWER
        SpatialMenuFollow[] smfs = FindObjectsByType<SpatialMenuFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        SpatialMenuFollow[] smfs = FindObjectsOfType<SpatialMenuFollow>(true);
#endif
        foreach (var sm in smfs)
        {
            if (sm != null)
            {
                sm.enabled = false;
                sm.gameObject.SetActive(false);
            }
        }

        GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var go in all)
        {
            if (go != null && (go.name == "AR_Menu" || (go.name == "Canvas" && go.GetComponent<SpatialMenuFollow>() != null)))
            {
                go.SetActive(false);
            }
        }

        if (menuHUDController != null && menuHUDController.menuButtons != null)
        {
            foreach (var btn in menuHUDController.menuButtons)
            {
                if (btn != null)
                {
                    if (buttonInteractor != null) buttonInteractor.UnregisterButton(btn);
                    btn.gameObject.SetActive(false);
                }
            }
        }
    }

    public void AutoFindReferences()
    {
#if UNITY_2023_1_OR_NEWER
        if (buttonInteractor == null) buttonInteractor = FindFirstObjectByType<ButtonInteractor>();
        if (tapToPlaceController == null) tapToPlaceController = FindFirstObjectByType<TapToPlaceController>();
        if (wireConnectionController == null) wireConnectionController = FindFirstObjectByType<WireConnectionController>();
        if (singlePlaneLockController == null) singlePlaneLockController = FindFirstObjectByType<SinglePlaneLockController>();
        if (spatialCalibrator == null) spatialCalibrator = FindFirstObjectByType<TwoPointSpatialCalibrator>();
        if (menuHUDController == null) menuHUDController = FindFirstObjectByType<MenuHUDController>();
#else
        if (buttonInteractor == null) buttonInteractor = FindObjectOfType<ButtonInteractor>();
        if (tapToPlaceController == null) tapToPlaceController = FindObjectOfType<TapToPlaceController>();
        if (wireConnectionController == null) wireConnectionController = FindObjectOfType<WireConnectionController>();
        if (singlePlaneLockController == null) singlePlaneLockController = FindObjectOfType<SinglePlaneLockController>();
        if (spatialCalibrator == null) spatialCalibrator = FindObjectOfType<TwoPointSpatialCalibrator>();
        if (menuHUDController == null) menuHUDController = FindObjectOfType<MenuHUDController>();
#endif
    }

    // =========================================================================
    // CƠ CHẾ BONG BÓNG CHAT: BẤM VÀO LÀ XÒE RA, BẤM VÀO LẦN NỮA LÀ THỤT VÀO
    // =========================================================================

    public void ToggleChatBubbleMenu()
    {
        if (isBarExpanded)
        {
            RetractMenu();
        }
        else
        {
            FanOutMenu();
        }
    }

    public void FanOutMenu()
    {
        isBarExpanded = true;
        if (mainBubbleLabelText != null) mainBubbleLabelText.text = "THU GỌN";

        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(AnimateFanOut(true));
    }

    public void RetractMenu()
    {
        isBarExpanded = false;
        if (mainBubbleLabelText != null) mainBubbleLabelText.text = "MENU AR";

        if (isToolboxOpen) ToggleToolboxDrawer();

        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(AnimateFanOut(false));
    }

    public void CollapseBar() => RetractMenu();
    public void ExpandBar() => FanOutMenu();

    private IEnumerator AnimateFanOut(bool expanding)
    {
        float duration = 0.22f;
        float elapsed = 0f;

        if (expanding)
        {
            if (btnScanBubble != null) { btnScanBubble.gameObject.SetActive(true); btnScanBubble.localScale = Vector3.zero; }
            if (btnResetBubble != null) { btnResetBubble.gameObject.SetActive(true); btnResetBubble.localScale = Vector3.zero; }
            if (btnToolboxBubble != null) { btnToolboxBubble.gameObject.SetActive(true); btnToolboxBubble.localScale = Vector3.zero; }
        }

        Vector2 startScan = expanding ? posMainBubble : posScanTarget;
        Vector2 endScan = expanding ? posScanTarget : posMainBubble;

        Vector2 startReset = expanding ? posMainBubble : posResetTarget;
        Vector2 endReset = expanding ? posResetTarget : posMainBubble;

        Vector2 startToolbox = expanding ? posMainBubble : posToolboxTarget;
        Vector2 endToolbox = expanding ? posToolboxTarget : posMainBubble;

        Vector3 startScale = expanding ? Vector3.zero : Vector3.one;
        Vector3 endScale = expanding ? Vector3.one : Vector3.zero;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Hiệu ứng nảy lò xo (Overshoot Spring Bounce) cho bong bóng xòe ra cực đã mắt
            float ease;
            if (expanding)
            {
                float c = 1.6f;
                ease = 1f + (c + 1f) * Mathf.Pow(t - 1f, 3) + c * Mathf.Pow(t - 1f, 2);
            }
            else
            {
                ease = Mathf.Sin(t * Mathf.PI * 0.5f);
            }

            Vector2 curScan = Vector2.LerpUnclamped(startScan, endScan, ease);
            Vector2 curReset = Vector2.LerpUnclamped(startReset, endReset, ease);
            Vector2 curToolbox = Vector2.LerpUnclamped(startToolbox, endToolbox, ease);
            Vector3 curScale = Vector3.LerpUnclamped(startScale, endScale, Mathf.Clamp01(ease));

            if (btnScanBubble != null) { btnScanBubble.anchoredPosition = curScan; btnScanBubble.localScale = curScale; }
            if (btnResetBubble != null) { btnResetBubble.anchoredPosition = curReset; btnResetBubble.localScale = curScale; }
            if (btnToolboxBubble != null) { btnToolboxBubble.anchoredPosition = curToolbox; btnToolboxBubble.localScale = curScale; }

            yield return null;
        }

        if (btnScanBubble != null) { btnScanBubble.anchoredPosition = endScan; btnScanBubble.localScale = endScale; }
        if (btnResetBubble != null) { btnResetBubble.anchoredPosition = endReset; btnResetBubble.localScale = endScale; }
        if (btnToolboxBubble != null) { btnToolboxBubble.anchoredPosition = endToolbox; btnToolboxBubble.localScale = endScale; }

        if (!expanding)
        {
            if (btnScanBubble != null) btnScanBubble.gameObject.SetActive(false);
            if (btnResetBubble != null) btnResetBubble.gameObject.SetActive(false);
            if (btnToolboxBubble != null) btnToolboxBubble.gameObject.SetActive(false);
        }
    }

    private void SetMenuStateImmediate(bool expanded)
    {
        if (expanded)
        {
            if (btnScanBubble != null) { btnScanBubble.gameObject.SetActive(true); btnScanBubble.anchoredPosition = posScanTarget; btnScanBubble.localScale = Vector3.one; }
            if (btnResetBubble != null) { btnResetBubble.gameObject.SetActive(true); btnResetBubble.anchoredPosition = posResetTarget; btnResetBubble.localScale = Vector3.one; }
            if (btnToolboxBubble != null) { btnToolboxBubble.gameObject.SetActive(true); btnToolboxBubble.anchoredPosition = posToolboxTarget; btnToolboxBubble.localScale = Vector3.one; }
            if (mainBubbleLabelText != null) mainBubbleLabelText.text = "THU GỌN";
        }
        else
        {
            if (btnScanBubble != null) { btnScanBubble.anchoredPosition = posMainBubble; btnScanBubble.localScale = Vector3.zero; btnScanBubble.gameObject.SetActive(false); }
            if (btnResetBubble != null) { btnResetBubble.anchoredPosition = posMainBubble; btnResetBubble.localScale = Vector3.zero; btnResetBubble.gameObject.SetActive(false); }
            if (btnToolboxBubble != null) { btnToolboxBubble.anchoredPosition = posMainBubble; btnToolboxBubble.localScale = Vector3.zero; btnToolboxBubble.gameObject.SetActive(false); }
            if (mainBubbleLabelText != null) mainBubbleLabelText.text = "MENU AR";
        }

        if (toolboxDrawer != null)
        {
            toolboxDrawer.localScale = Vector3.zero;
            toolboxDrawer.gameObject.SetActive(false);
        }
    }

    public void ToggleToolboxDrawer()
    {
        if (toolboxDrawer == null) return;

        isToolboxOpen = !isToolboxOpen;
        if (drawerAnimCoroutine != null) StopCoroutine(drawerAnimCoroutine);

        if (isToolboxOpen)
        {
            toolboxDrawer.gameObject.SetActive(true);
            drawerAnimCoroutine = StartCoroutine(AnimateScale(toolboxDrawer, Vector3.one, 0.18f));
        }
        else
        {
            drawerAnimCoroutine = StartCoroutine(AnimateScale(toolboxDrawer, Vector3.zero, 0.15f, () => toolboxDrawer.gameObject.SetActive(false)));
        }
    }

    // =========================================================================
    // HÀNH ĐỘNG CÁC NÚT (QUÉT LẠI, RESET MẠCH, CHỌN LINH KIỆN)
    // =========================================================================

    public void OnClickScan()
    {
        Debug.Log("<color=yellow>[ARFloatingBubbleMenu]</color> Thực hiện QUÉT LẠI toàn bộ...");

        if (wireConnectionController != null) wireConnectionController.ClearAllWires();
        if (tapToPlaceController != null) tapToPlaceController.ClearPreview();

        DestroyAllPlacedComponents();
        DestroyCalibratedBoard();

        if (singlePlaneLockController != null) singlePlaneLockController.UnlockAndRescan();
        if (spatialCalibrator != null) spatialCalibrator.RestartCalibration();

        if (isToolboxOpen) ToggleToolboxDrawer();
    }

    public void OnClickResetCircuit()
    {
        Debug.Log("<color=orange>[ARFloatingBubbleMenu]</color> Thực hiện RESET MẠCH ĐIỆN...");

        if (wireConnectionController != null) wireConnectionController.ClearAllWires();
        if (tapToPlaceController != null) tapToPlaceController.ClearPreview();

        DestroyAllPlacedComponents();

        if (isToolboxOpen) ToggleToolboxDrawer();
    }

    public void OnClickToolbox()
    {
        ToggleToolboxDrawer();
    }

    public void OnSelectComponent(string componentName)
    {
        Debug.Log("<color=green>[ARFloatingBubbleMenu]</color> Đã chọn linh kiện: " + componentName);

        if (tapToPlaceController != null)
        {
            tapToPlaceController.StartPreview(componentName);
        }

        if (menuHUDController != null)
        {
            menuHUDController.Select(componentName);
        }

        // Tự động đóng khay dụng cụ ngay khi chọn xong
        if (isToolboxOpen) ToggleToolboxDrawer();
    }

    private void DestroyAllPlacedComponents()
    {
#if UNITY_2023_1_OR_NEWER
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
#else
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
#endif
        int count = 0;
        foreach (var obj in allObjects)
        {
            if (obj != null && obj.name.StartsWith("Placed_"))
            {
                Destroy(obj);
                count++;
            }
        }

        GameObject boardAnchor = GameObject.Find("Board_Anchor");
        if (boardAnchor != null)
        {
            for (int i = boardAnchor.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = boardAnchor.transform.GetChild(i);
                if (child.name != "ActiveCircuitBoard")
                {
                    Destroy(child.gameObject);
                    count++;
                }
            }
        }
        Debug.Log("<color=yellow>[ARFloatingBubbleMenu]</color> Đã dọn dẹp " + count + " linh kiện.");
    }

    private void DestroyCalibratedBoard()
    {
        GameObject anchor = GameObject.Find("Board_Anchor");
        if (anchor != null) Destroy(anchor);

        GameObject board = GameObject.Find("ActiveCircuitBoard");
        if (board != null) Destroy(board);
    }

    private void RegisterAllButtonsToInteractor()
    {
        if (buttonInteractor == null) AutoFindReferences();
        if (buttonInteractor == null) return;

        if (mainChatBubble != null) buttonInteractor.RegisterButton(mainChatBubble);
        if (btnScanBubble != null) buttonInteractor.RegisterButton(btnScanBubble);
        if (btnResetBubble != null) buttonInteractor.RegisterButton(btnResetBubble);
        if (btnToolboxBubble != null) buttonInteractor.RegisterButton(btnToolboxBubble);

        if (componentButtons != null)
        {
            foreach (var b in componentButtons)
            {
                if (b != null) buttonInteractor.RegisterButton(b);
            }
        }
    }

    private IEnumerator AnimateScale(RectTransform target, Vector3 targetScale, float duration, System.Action onComplete = null)
    {
        if (target == null) yield break;

        Vector3 startScale = target.localScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (target == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = Mathf.Sin(t * Mathf.PI * 0.5f);
            target.localScale = Vector3.Lerp(startScale, targetScale, ease);
            yield return null;
        }

        if (target != null) target.localScale = targetScale;
        onComplete?.Invoke();
    }

    // =========================================================================
    // DỰNG GIAO DIỆN BONG BÓNG CHAT VÀ 3 Ô TRÒN CHỨA HÌNH ẢNH ICON THỰC TẾ
    // =========================================================================

    private void BuildChatBubbleMenuUI()
    {
        targetCanvas = FindOrCreateMenuCanvas();
        EnsureSprites();

        Transform oldRoot = targetCanvas.transform.Find("ChatBubbleMenu_Root");
        if (oldRoot != null) Destroy(oldRoot.gameObject);

        // Container toàn màn hình
        GameObject rootObj = new GameObject("ChatBubbleMenu_Root", typeof(RectTransform));
        rootObj.transform.SetParent(targetCanvas.transform, false);
        RectTransform rootRT = rootObj.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        // Tính toán tọa độ công thái học vùng giữa phía dưới
        posMainBubble = new Vector2(0, menuBottomOffset);
        posScanTarget = new Vector2(-270, menuBottomOffset + 200f);
        posResetTarget = new Vector2(0, menuBottomOffset + 245f);
        posToolboxTarget = new Vector2(270, menuBottomOffset + 200f);

        // Nạp các Sprite chất lượng cao trực tiếp từ UIIconAssets (nhúng Base64, 100% hoạt động trên Android)
        Sprite scanSprite = UIIconAssets.GetScanSprite();
        Sprite resetSprite = UIIconAssets.GetResetSprite();
        Sprite toolboxSprite = UIIconAssets.GetToolboxSprite();
        Sprite mainBubbleSprite = UIIconAssets.GetMainBubbleSprite();

        // 1. Ô TRÒN 1: QUÉT LẠI MẶT PHẲNG (Bên trái)
        GameObject scanObj = CreateCircularIconButton(
            parent: rootRT,
            name: "Btn_Bubble_Scan",
            iconSprite: scanSprite,
            ringColor: new Color(0f, 0.85f, 1f, 0.95f), // Viền cyan phát sáng
            size: new Vector2(bubbleDiameter, bubbleDiameter),
            anchoredPos: posScanTarget,
            labelText: "QUÉT MẶT PHẲNG",
            onClick: OnClickScan
        );
        btnScanBubble = scanObj.GetComponent<RectTransform>();

        // 2. Ô TRÒN 2: RESET MẠCH ĐIỆN (Ở giữa trên)
        GameObject resetObj = CreateCircularIconButton(
            parent: rootRT,
            name: "Btn_Bubble_Reset",
            iconSprite: resetSprite,
            ringColor: new Color(1f, 0.65f, 0.1f, 0.95f), // Viền cam phát sáng
            size: new Vector2(bubbleDiameter, bubbleDiameter),
            anchoredPos: posResetTarget,
            labelText: "RESET MẠCH",
            onClick: OnClickResetCircuit
        );
        btnResetBubble = resetObj.GetComponent<RectTransform>();

        // 3. Ô TRÒN 3: KHO DỤNG CỤ (Bên phải)
        GameObject toolObj = CreateCircularIconButton(
            parent: rootRT,
            name: "Btn_Bubble_Toolbox",
            iconSprite: toolboxSprite,
            ringColor: new Color(0.1f, 0.92f, 0.7f, 0.95f), // Viền ngọc phát sáng
            size: new Vector2(bubbleDiameter, bubbleDiameter),
            anchoredPos: posToolboxTarget,
            labelText: "KHO DỤNG CỤ",
            onClick: OnClickToolbox
        );
        btnToolboxBubble = toolObj.GetComponent<RectTransform>();

        // 4. BONG BÓNG CHAT CHÍNH (MAIN CHAT BUBBLE - NÚT BẤM ĐỂ XÒE RA / THỤT VÀO)
        GameObject mainObj = CreateMainChatBubble(
            parent: rootRT,
            name: "Btn_Main_ChatBubble",
            iconSprite: mainBubbleSprite,
            ringColor: new Color(0f, 0.95f, 1f, 1f),
            size: new Vector2(bubbleDiameter, bubbleDiameter),
            anchoredPos: posMainBubble,
            onClick: ToggleChatBubbleMenu
        );
        mainChatBubble = mainObj.GetComponent<RectTransform>();

        // 5. KHAY DỤNG CỤ LINH KIỆN (Bung lên phía trên khi bấm Ô Kho dụng cụ)
        GameObject drawerObj = new GameObject("Toolbox_Drawer", typeof(RectTransform), typeof(Image));
        drawerObj.transform.SetParent(rootRT, false);
        toolboxDrawer = drawerObj.GetComponent<RectTransform>();
        toolboxDrawer.anchorMin = new Vector2(0.5f, 0f);
        toolboxDrawer.anchorMax = new Vector2(0.5f, 0f);
        toolboxDrawer.pivot = new Vector2(0.5f, 0f);
        toolboxDrawer.anchoredPosition = new Vector2(0, menuBottomOffset + 380f);
        toolboxDrawer.sizeDelta = new Vector2(900, 280);

        Image drawerImg = drawerObj.GetComponent<Image>();
        drawerImg.sprite = roundedRectSprite;
        drawerImg.type = Image.Type.Sliced;
        drawerImg.color = new Color(0.04f, 0.07f, 0.14f, 0.97f);

        Outline drawerOutline = drawerObj.AddComponent<Outline>();
        drawerOutline.effectColor = new Color(0.1f, 0.9f, 0.7f, 0.8f);
        drawerOutline.effectDistance = new Vector2(3f, -3f);

        // Lưới 3x2 các linh kiện - Dùng tiếng Việt chuẩn, không dùng emoji tránh lỗi ô vuông
        string[] compNames = new string[] { "Pin", "Den", "Cong Tac", "Ampe Ke", "Von Ke" };
        string[] compLabels = new string[] { "PIN", "ĐÈN", "KHÓA K", "AMPE KẾ", "VÔN KẾ" };
        Color[] compColors = new Color[]
        {
            new Color(0.12f, 0.60f, 0.35f, 0.96f),
            new Color(0.88f, 0.65f, 0.12f, 0.96f),
            new Color(0.35f, 0.45f, 0.60f, 0.96f),
            new Color(0.15f, 0.50f, 0.90f, 0.96f),
            new Color(0.75f, 0.22f, 0.70f, 0.96f)
        };

        List<RectTransform> compBtnList = new List<RectTransform>();
        float[] colX = new float[] { -285f, 0f, 285f };
        float row1Y = 60f;
        float row2Y = -60f;

        // Hàng 1
        for (int i = 0; i < 3; i++)
        {
            string comp = compNames[i];
            GameObject itemObj = CreateComponentButton(
                parent: toolboxDrawer,
                name: "Btn_Comp_" + comp,
                bgColor: compColors[i],
                size: new Vector2(270, 115),
                anchoredPos: new Vector2(colX[i], row1Y),
                label: compLabels[i],
                onClick: () => OnSelectComponent(comp)
            );
            compBtnList.Add(itemObj.GetComponent<RectTransform>());
        }

        // Hàng 2
        for (int i = 3; i < 5; i++)
        {
            string comp = compNames[i];
            GameObject itemObj = CreateComponentButton(
                parent: toolboxDrawer,
                name: "Btn_Comp_" + comp,
                bgColor: compColors[i],
                size: new Vector2(270, 115),
                anchoredPos: new Vector2(colX[i - 3], row2Y),
                label: compLabels[i],
                onClick: () => OnSelectComponent(comp)
            );
            compBtnList.Add(itemObj.GetComponent<RectTransform>());
        }

        // Nút Đóng khay dụng cụ
        GameObject closeDrawerObj = CreateComponentButton(
            parent: toolboxDrawer,
            name: "Btn_Close_Drawer",
            bgColor: new Color(0.40f, 0.20f, 0.25f, 0.96f),
            size: new Vector2(270, 115),
            anchoredPos: new Vector2(colX[2], row2Y),
            label: "ĐÓNG",
            onClick: ToggleToolboxDrawer
        );
        compBtnList.Add(closeDrawerObj.GetComponent<RectTransform>());

        componentButtons = compBtnList.ToArray();

        RegisterAllButtonsToInteractor();
        Debug.Log("<color=green>[ARFloatingBubbleMenu]</color> Đã khởi tạo hoàn tất Menu Bong Bóng Chat với 3 ô tròn icon!");
    }

    /// <summary>
    /// Tạo nút tròn có hình ảnh icon bên trong và viền phát sáng
    /// </summary>
    private GameObject CreateCircularIconButton(
        Transform parent,
        string name,
        Sprite iconSprite,
        Color ringColor,
        Vector2 size,
        Vector2 anchoredPos,
        string labelText,
        System.Action onClick)
    {
        GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        Image img = btnObj.GetComponent<Image>();
        img.sprite = circleSprite;
        img.color = new Color(0.08f, 0.12f, 0.20f, 0.96f);

        Button btn = btnObj.GetComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(() => onClick());

        // Viền tròn phát sáng
        Outline outline = btnObj.AddComponent<Outline>();
        outline.effectColor = ringColor;
        outline.effectDistance = new Vector2(3.5f, -3.5f);

        // Ảnh icon bên trong ô tròn - Căn chính giữa hoàn hảo
        if (iconSprite != null)
        {
            GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(btnObj.transform, false);
            RectTransform iconRT = iconObj.GetComponent<RectTransform>();
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = new Vector2(6, 6);
            iconRT.offsetMax = new Vector2(-6, -6);

            Image iconImg = iconObj.GetComponent<Image>();
            iconImg.sprite = iconSprite;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
        }

        // Nhãn chữ bo tròn bên dưới nút (Không dùng emoji để tránh lỗi ô vuông trên Android)
        if (!string.IsNullOrEmpty(labelText))
        {
            GameObject pillObj = new GameObject("Label_Pill", typeof(RectTransform), typeof(Image));
            pillObj.transform.SetParent(btnObj.transform, false);
            RectTransform pillRT = pillObj.GetComponent<RectTransform>();
            pillRT.anchorMin = new Vector2(0.5f, 0f);
            pillRT.anchorMax = new Vector2(0.5f, 0f);
            pillRT.pivot = new Vector2(0.5f, 1f);
            pillRT.sizeDelta = new Vector2(size.x + 24, 44);
            pillRT.anchoredPosition = new Vector2(0, -8);

            Image pillImg = pillObj.GetComponent<Image>();
            pillImg.sprite = roundedRectSprite;
            pillImg.type = Image.Type.Sliced;
            pillImg.color = new Color(0.05f, 0.08f, 0.14f, 0.95f);
            pillImg.raycastTarget = false;

            Outline pillOutline = pillObj.AddComponent<Outline>();
            pillOutline.effectColor = new Color(ringColor.r, ringColor.g, ringColor.b, 0.7f);
            pillOutline.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject textObj = new GameObject("Text", typeof(RectTransform));
            textObj.transform.SetParent(pillObj.transform, false);
            RectTransform textRT = textObj.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(4, 0);
            textRT.offsetMax = new Vector2(-4, 0);

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            if (tmp.font == null && TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = labelText;
            tmp.fontSize = 18;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 13;
            tmp.fontSizeMax = 19;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }

        return btnObj;
    }

    /// <summary>
    /// Tạo Bong bóng chat chính (Toggle Button)
    /// </summary>
    private GameObject CreateMainChatBubble(
        Transform parent,
        string name,
        Sprite iconSprite,
        Color ringColor,
        Vector2 size,
        Vector2 anchoredPos,
        System.Action onClick)
    {
        GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        Image img = btnObj.GetComponent<Image>();
        img.sprite = circleSprite;
        img.color = new Color(0.06f, 0.12f, 0.22f, 0.98f);

        Button btn = btnObj.GetComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(() => onClick());

        Outline outline = btnObj.AddComponent<Outline>();
        outline.effectColor = ringColor;
        outline.effectDistance = new Vector2(4f, -4f);

        // Icon chính bên trong bong bóng
        if (iconSprite != null)
        {
            GameObject iconObj = new GameObject("MainIcon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(btnObj.transform, false);
            RectTransform iconRT = iconObj.GetComponent<RectTransform>();
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = new Vector2(8, 8);
            iconRT.offsetMax = new Vector2(-8, -8);

            Image iconImg = iconObj.GetComponent<Image>();
            iconImg.sprite = iconSprite;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
        }

        // Nhãn điều khiển bên dưới bong bóng
        GameObject pillObj = new GameObject("MainBubble_LabelPill", typeof(RectTransform), typeof(Image));
        pillObj.transform.SetParent(btnObj.transform, false);
        RectTransform pillRT = pillObj.GetComponent<RectTransform>();
        pillRT.anchorMin = new Vector2(0.5f, 0f);
        pillRT.anchorMax = new Vector2(0.5f, 0f);
        pillRT.pivot = new Vector2(0.5f, 1f);
        pillRT.sizeDelta = new Vector2(size.x + 20, 48);
        pillRT.anchoredPosition = new Vector2(0, -8);

        Image pillImg = pillObj.GetComponent<Image>();
        pillImg.sprite = roundedRectSprite;
        pillImg.type = Image.Type.Sliced;
        pillImg.color = new Color(0.06f, 0.10f, 0.18f, 0.96f);
        pillImg.raycastTarget = false;

        Outline pillOutline = pillObj.AddComponent<Outline>();
        pillOutline.effectColor = new Color(0f, 0.9f, 1f, 0.8f);
        pillOutline.effectDistance = new Vector2(2f, -2f);

        GameObject textObj = new GameObject("Text", typeof(RectTransform));
        textObj.transform.SetParent(pillObj.transform, false);
        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        mainBubbleLabelText = textObj.AddComponent<TextMeshProUGUI>();
        if (mainBubbleLabelText.font == null && TMP_Settings.defaultFontAsset != null) mainBubbleLabelText.font = TMP_Settings.defaultFontAsset;
        mainBubbleLabelText.text = "MENU AR";
        mainBubbleLabelText.fontSize = 21;
        mainBubbleLabelText.fontStyle = FontStyles.Bold;
        mainBubbleLabelText.alignment = TextAlignmentOptions.Center;
        mainBubbleLabelText.color = new Color(0.2f, 0.95f, 1f, 1f);
        mainBubbleLabelText.raycastTarget = false;

        return btnObj;
    }

    private GameObject CreateComponentButton(
        Transform parent,
        string name,
        Color bgColor,
        Vector2 size,
        Vector2 anchoredPos,
        string label,
        System.Action onClick)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);

        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        Image img = obj.GetComponent<Image>();
        img.sprite = roundedRectSprite;
        img.type = Image.Type.Sliced;
        img.color = bgColor;

        Button btn = obj.GetComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(() => onClick());

        GameObject textObj = new GameObject("Label", typeof(RectTransform));
        textObj.transform.SetParent(obj.transform, false);
        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        if (tmp.font == null && TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = label;
        tmp.fontSize = 24;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        return obj;
    }

    private Canvas FindOrCreateMenuCanvas()
    {
        GameObject existingObj = GameObject.Find("ARFloatingMenu_Canvas");
        if (existingObj != null)
        {
            Canvas existingCanvas = existingObj.GetComponent<Canvas>();
            if (existingCanvas != null)
            {
                targetScaler = existingObj.GetComponent<CanvasScaler>();
                return existingCanvas;
            }
        }

        GameObject canvasObj = new GameObject("ARFloatingMenu_Canvas");
        Canvas c = canvasObj.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 999;

        targetScaler = canvasObj.AddComponent<CanvasScaler>();
        targetScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        targetScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        bool isLandscape = Screen.width > Screen.height;
        if (isLandscape)
        {
            targetScaler.referenceResolution = new Vector2(1920, 1080);
            targetScaler.matchWidthOrHeight = 1f;
        }
        else
        {
            targetScaler.referenceResolution = new Vector2(1080, 1920);
            targetScaler.matchWidthOrHeight = 0f;
        }

        canvasObj.AddComponent<GraphicRaycaster>();
        return c;
    }

    private static void EnsureSprites()
    {
        if (circleSprite == null) circleSprite = CreateProceduralCircle(128);
        if (roundedRectSprite == null) roundedRectSprite = CreateProceduralRoundedRect(128, 128, 32);
    }

    private static Sprite CreateProceduralCircle(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];
        float radius = size * 0.5f;
        Vector2 center = new Vector2(radius, radius);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                if (dist > radius)
                {
                    colors[y * size + x] = Color.clear;
                }
                else if (dist > radius - 1.5f)
                {
                    colors[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(radius - dist));
                }
                else
                {
                    colors[y * size + x] = Color.white;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static Sprite CreateProceduralRoundedRect(int width, int height, int radius)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] colors = new Color[width * height];
        float r = radius;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Max(0, Mathf.Abs(x + 0.5f - width * 0.5f) - (width * 0.5f - r));
                float dy = Mathf.Max(0, Mathf.Abs(y + 0.5f - height * 0.5f) - (height * 0.5f - r));
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > r)
                {
                    colors[y * width + x] = Color.clear;
                }
                else if (dist > r - 1.5f)
                {
                    colors[y * width + x] = new Color(1, 1, 1, Mathf.Clamp01(r - dist));
                }
                else
                {
                    colors[y * width + x] = Color.white;
                }
            }
        }

        tex.SetPixels(colors);
        tex.Apply();
        Vector4 border = new Vector4(radius, radius, radius, radius);
        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }
}


/// <summary>
/// UIIconAssets - NhÃºng trá»±c tiáº¿p dá»¯ liá»‡u hÃ¬nh áº£nh icon dÆ°á»›i dáº¡ng Base64.
/// Äáº£m báº£o 100% hiá»ƒn thá»‹ hoÃ n háº£o trÃªn Android APK, khÃ´ng phá»¥ thuá»™c vÃ o Ä‘Æ°á»ng dáº«n file hay import meta.
/// </summary>
public static class UIIconAssets
{
    private static Sprite _scanSprite;
    private static Sprite _resetSprite;
    private static Sprite _toolboxSprite;
    private static Sprite _mainBubbleSprite;

    private static Sprite CreateSpriteFromBase64(string base64)
    {
        try
        {
            byte[] bytes = System.Convert.FromBase64String(base64);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(bytes))
            {
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[UIIconAssets] Error decode icon: " + e.Message);
        }
        return null;
    }

    public static Sprite GetScanSprite()
    {
        if (_scanSprite == null) _scanSprite = CreateSpriteFromBase64(ScanBase64);
        return _scanSprite;
    }

    public static Sprite GetResetSprite()
    {
        if (_resetSprite == null) _resetSprite = CreateSpriteFromBase64(ResetBase64);
        return _resetSprite;
    }

    public static Sprite GetToolboxSprite()
    {
        if (_toolboxSprite == null) _toolboxSprite = CreateSpriteFromBase64(ToolboxBase64);
        return _toolboxSprite;
    }

    public static Sprite GetMainBubbleSprite()
    {
        if (_mainBubbleSprite == null) _mainBubbleSprite = CreateSpriteFromBase64(MainBase64);
        return _mainBubbleSprite;
    }

    private const string ScanBase64 = "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";
    private const string ResetBase64 = "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";
    private const string ToolboxBase64 = "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";
    private const string MainBase64 = "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";
}