using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using TMPro;

/// <summary>
/// WelcomeScreenController - Quản lý Màn Hình Chính xuất hiện đầu tiên khi mở app CircuitAR.
/// 
/// ĐẶC ĐIỂM & CHỨC NĂNG CỐT LÕI:
/// 1. TUYỆT ĐỐI KHÔNG TRUY CẬP CAMERA TRƯỚC KHI VÀO CHỨC NĂNG:
///    - Khi vừa mở app, ARSession, ARCameraManager và MediaPipe HandLandmarkerRunner đều được giữ ở
///      trạng thái TẮT / TẠM DỪNG (Disabled / Paused).
///    - Không phát sinh yêu cầu cấp quyền Camera hay khởi chạy camera phần cứng của điện thoại khi ở Màn Hình Chính.
/// 2. NÚT 1: "TỰ LẮP MẠCH":
///    - Khi bấm: Ẩn Màn Hình Chính, BẬT Camera & ARSession, kích hoạt MediaPipe HandLandmarkerRunner,
///      bật căn chỉnh 2 điểm mép bàn (TwoPointSpatialCalibrator) và Menu Bong Bóng Chat AR (ARFloatingBubbleMenu).
///    - Đồng thời hiển thị NÚT "‹ QUAY LẠI" ở góc trên bên trái màn hình.
/// 3. NÚT "‹ QUAY LẠI" (BACK BUTTON TRONG CHỨC NĂNG AR):
///    - Xuất hiện khi vào chức năng AR, nằm ở góc trên bên trái màn hình với thiết kế công nghệ sang trọng.
///    - Hỗ trợ cả Chạm ngón tay (Touch Button) và Chụm ngón tay (Hand Pinch via ButtonInteractor).
///    - Hỗ trợ phím cứng Back của Android (KeyCode.Escape).
///    - Khi bấm: Ngay lập tức DỪNG camera, tắt ARSession, tạm dừng MediaPipe, dọn dẹp các UI AR và
///      quay trở về Màn Hình Chính an toàn, tiết kiệm pin tối đa.
/// 4. NÚT 2: "QUÉT MẠCH AI":
///    - Placeholder tính năng đang phát triển: Bấm vào hiện popup Toast ngắn "Tính năng đang phát triển,
///      vui lòng quay lại sau!" kèm nút "ĐÃ HIỂU" rồi trở về màn hình chính bình thường.
/// </summary>
public class WelcomeScreenController : MonoBehaviour
{
    [Header("--- THAM CHIẾU HỆ THỐNG AR & CAMERA ---")]
    [Tooltip("ARSession của AR Foundation")]
    public ARSession arSession;

    [Tooltip("ARCameraManager trên Main Camera")]
    public ARCameraManager arCameraManager;

    [Tooltip("Bộ chạy MediaPipe phát hiện bàn tay")]
    public Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerRunner handLandmarkerRunner;

    [Tooltip("Bộ tương tác cử chỉ tay (Pinch / Hover)")]
    public ButtonInteractor buttonInteractor;

    [Tooltip("Bộ căn chỉnh mặt bàn 2 điểm mép")]
    public TwoPointSpatialCalibrator spatialCalibrator;

    [Tooltip("Menu Bong Bóng Chat AR (Messenger FAB)")]
    public ARFloatingBubbleMenu bubbleMenu;

    [Header("--- GIAO DIỆN MÀN HÌNH CHÍNH ---")]
    public Canvas welcomeCanvas;
    public CanvasScaler welcomeScaler;
    public CanvasGroup welcomeCanvasGroup;

    [Header("--- THAM CHIẾU NÚT BẤM CHÍNH ---")]
    public Button btnManualCircuit;     // Nút 1: Tự Lắp Mạch
    public Button btnAIScan;            // Nút 2: Quét Mạch AI

    [Header("--- NÚT QUAY LẠI (BACK BUTTON TRONG AR) ---")]
    public Canvas backButtonCanvas;     // Canvas riêng cho nút Quay Lại
    public CanvasScaler backButtonScaler;
    public Button btnBackToWelcome;     // Nút "‹ QUAY LẠI"
    public RectTransform rtBackBtn;

    [Header("--- POPUP TOAST 'TÍNH NĂNG ĐANG PHÁT TRIỂN' ---")]
    public GameObject modalToastRoot;   // Lớp phủ mờ toàn màn hình
    public RectTransform modalToastCard;// Thẻ thông báo ở giữa
    public CanvasGroup modalToastGroup; // Hiệu ứng fade in/out popup
    public Button btnToastDismiss;      // Nút "ĐÃ HIỂU"

    [Header("--- CẤU HÌNH & TRẠNG THÁI ---")]
    [Tooltip("Tự động tạo giao diện nếu chưa gán trong Inspector")]
    public bool autoGenerateUI = true;

    [Tooltip("Trạng thái màn hình chính đang hiển thị")]
    public bool isWelcomeScreenActive = true;

    private static Sprite circleSprite;
    private static Sprite roundedRectSprite;
    private static TMP_FontAsset _safeFontAsset;
    private Coroutine toastCoroutine;
    private Coroutine fadeTransitionCoroutine;

    /// <summary>
    /// Đảm bảo tự động khởi tạo Màn Hình Chính CircuitAR ngay khi Scene được nạp
    /// (Kể cả khi scene chưa được lưu lại trong Unity Editor)
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void RuntimeInit()
    {
        EnsureInstance();
    }

    /// <summary>
    /// Lấy hoặc tự động tạo WelcomeScreenController trong Scene
    /// </summary>
    public static WelcomeScreenController EnsureInstance()
    {
        WelcomeScreenController instance = null;
#if UNITY_2023_1_OR_NEWER
        instance = Object.FindFirstObjectByType<WelcomeScreenController>(FindObjectsInactive.Include);
#else
        instance = Object.FindObjectOfType<WelcomeScreenController>(true);
#endif
        if (instance == null)
        {
            GameObject host = GameObject.Find("GestureManager");
            if (host == null)
            {
                host = new GameObject("WelcomeScreenManager");
            }
            instance = host.AddComponent<WelcomeScreenController>();
            Debug.Log("<color=green>[WelcomeScreenController]</color> Đã tự động tạo và kích hoạt WelcomeScreenController!");
        }
        return instance;
    }

    /// <summary>
    /// Nạp font an toàn, không bao giờ bị null
    /// </summary>
    public static TMP_FontAsset GetSafeFont()
    {
        if (_safeFontAsset != null) return _safeFontAsset;
        if (TMP_Settings.defaultFontAsset != null)
        {
            _safeFontAsset = TMP_Settings.defaultFontAsset;
            return _safeFontAsset;
        }
        _safeFontAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        return _safeFontAsset;
    }

    void Awake()
    {
        EnsureEventSystemExists();
        AutoFindReferences();

        // 1. TRIỆT ĐỂ: Dựng giao diện Màn Hình Chính ngay trong Awake ở frame 0!
        if (welcomeCanvas == null && autoGenerateUI)
        {
            BuildWelcomeScreenUI();
        }

        if (backButtonCanvas == null && autoGenerateUI)
        {
            BuildBackButtonUI();
        }

        // 2. TRIỆT ĐỂ: Tắt toàn bộ Camera & ARSession ngay trong Awake để điện thoại
        // KHÔNG truy cập camera hay hỏi quyền lúc vừa bật app!
        StopARAndCamera();
        SuppressARInitialState();

        // Ẩn Nút Quay Lại & Menu AR khi ở Màn Hình Chính
        SetBackButtonVisible(false);
        if (bubbleMenu != null)
        {
            bubbleMenu.SetMenuVisible(false);
        }

        // TRIỆT ĐỂ: Tắt hộp thoại Modal Toast nếu có
        if (modalToastRoot != null)
        {
            modalToastRoot.SetActive(false);
            if (modalToastGroup != null)
            {
                modalToastGroup.alpha = 0f;
                modalToastGroup.interactable = false;
                modalToastGroup.blocksRaycasts = false;
            }
        }

        isWelcomeScreenActive = true;
    }

    void Start()
    {
        EnsureEventSystemExists();
        AutoFindReferences();

        if (welcomeCanvas == null && autoGenerateUI)
        {
            BuildWelcomeScreenUI();
        }

        if (backButtonCanvas == null && autoGenerateUI)
        {
            BuildBackButtonUI();
        }

        if (isWelcomeScreenActive)
        {
            StopARAndCamera();
            SuppressARInitialState();
            SetBackButtonVisible(false);
            if (bubbleMenu != null)
            {
                bubbleMenu.SetMenuVisible(false);
            }
            if (modalToastRoot != null)
            {
                modalToastRoot.SetActive(false);
                if (modalToastGroup != null)
                {
                    modalToastGroup.alpha = 0f;
                    modalToastGroup.interactable = false;
                    modalToastGroup.blocksRaycasts = false;
                }
            }
        }
    }

    void Update()
    {
        UpdateCanvasOrientation();

        // Hỗ trợ phím cứng Back của Android (KeyCode.Escape)
        HandleAndroidBackButton();
    }

    /// <summary>
    /// Bắt phím cứng Back của Android hoặc phím Escape trên bàn phím
    /// </summary>
    private void HandleAndroidBackButton()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            // Nếu đang mở Modal Toast: Đóng Toast
            if (modalToastRoot != null && modalToastRoot.activeSelf)
            {
                DismissAIModalToast();
                return;
            }

            // Nếu đang trong chức năng AR: Quay lại Màn Hình Chính
            if (!isWelcomeScreenActive)
            {
                ReturnToWelcomeScreen();
                return;
            }
        }
    }

    /// <summary>
    /// Đảm bảo EventSystem và StandaloneInputModule luôn tồn tại để touch / click UI hoạt động
    /// </summary>
    private void EnsureEventSystemExists()
    {
        if (EventSystem.current == null)
        {
#if UNITY_2023_1_OR_NEWER
            EventSystem existing = FindFirstObjectByType<EventSystem>();
#else
            EventSystem existing = FindObjectOfType<EventSystem>();
#endif
            if (existing == null)
            {
                GameObject esObj = new GameObject("EventSystem_WelcomeScreen", typeof(EventSystem), typeof(StandaloneInputModule));
                Debug.Log("<color=cyan>[WelcomeScreenController]</color> Đã tự động khởi tạo EventSystem & StandaloneInputModule cho cảm ứng UI.");
            }
        }
    }

    /// <summary>
    /// Tự động tìm kiếm các bộ điều khiển AR / Calibrator / BubbleMenu / Camera trong Scene
    /// </summary>
    public void AutoFindReferences()
    {
#if UNITY_2023_1_OR_NEWER
        if (arSession == null) arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
        if (arCameraManager == null) arCameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
        if (handLandmarkerRunner == null) handLandmarkerRunner = FindFirstObjectByType<Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerRunner>(FindObjectsInactive.Include);
        if (buttonInteractor == null) buttonInteractor = FindFirstObjectByType<ButtonInteractor>(FindObjectsInactive.Include);
        if (spatialCalibrator == null) spatialCalibrator = FindFirstObjectByType<TwoPointSpatialCalibrator>(FindObjectsInactive.Include);
        if (bubbleMenu == null) bubbleMenu = FindFirstObjectByType<ARFloatingBubbleMenu>(FindObjectsInactive.Include);
#else
        if (arSession == null) arSession = FindObjectOfType<ARSession>(true);
        if (arCameraManager == null) arCameraManager = FindObjectOfType<ARCameraManager>(true);
        if (handLandmarkerRunner == null) handLandmarkerRunner = FindObjectOfType<Mediapipe.Unity.Sample.HandLandmarkDetection.HandLandmarkerRunner>(true);
        if (buttonInteractor == null) buttonInteractor = FindObjectOfType<ButtonInteractor>(true);
        if (spatialCalibrator == null) spatialCalibrator = FindObjectOfType<TwoPointSpatialCalibrator>(true);
        if (bubbleMenu == null) bubbleMenu = FindObjectOfType<ARFloatingBubbleMenu>(true);
#endif
    }

    /// <summary>
    /// TẮT HOÀN TOÀN CAMERA & AR SESSION ĐỂ KHÔNG TRUY CẬP CAMERA KHI Ở MÀN HÌNH CHÍNH
    /// </summary>
    public void StopARAndCamera()
    {
        if (arSession != null && arSession.enabled)
        {
            arSession.enabled = false;
            Debug.Log("<color=yellow>[WelcomeScreenController]</color> Đã TẮT ARSession (Khóa Camera AR).");
        }

        if (arCameraManager != null && arCameraManager.enabled)
        {
            arCameraManager.enabled = false;
            Debug.Log("<color=yellow>[WelcomeScreenController]</color> Đã TẮT ARCameraManager.");
        }

        if (handLandmarkerRunner != null)
        {
            handLandmarkerRunner.Pause();
            Debug.Log("<color=yellow>[WelcomeScreenController]</color> Đã TẠM DỪNG MediaPipe HandLandmarkerRunner.");
        }
    }

    /// <summary>
    /// BẬT CAMERA & AR SESSION KHI NGƯỜI DÙNG BẤM 'TỰ LẮP MẠCH'
    /// </summary>
    public void StartARAndCamera()
    {
        if (arSession != null && !arSession.enabled)
        {
            arSession.enabled = true;
            Debug.Log("<color=green>[WelcomeScreenController]</color> Đã BẬT ARSession (Mở Camera AR).");
        }

        if (arCameraManager != null && !arCameraManager.enabled)
        {
            arCameraManager.enabled = true;
            Debug.Log("<color=green>[WelcomeScreenController]</color> Đã BẬT ARCameraManager.");
        }

        if (handLandmarkerRunner != null)
        {
            if (handLandmarkerRunner.IsPaused)
            {
                handLandmarkerRunner.Resume();
            }
            else
            {
                handLandmarkerRunner.Play();
            }
            Debug.Log("<color=green>[WelcomeScreenController]</color> Đã KHỞI CHẠY MediaPipe HandLandmarkerRunner.");
        }
    }

    /// <summary>
    /// Tạm ẩn các UI căn chỉnh AR và ngưng bắt cử chỉ tay khi đang ở Màn Hình Chính
    /// </summary>
    private void SuppressARInitialState()
    {
        if (spatialCalibrator != null)
        {
            if (spatialCalibrator.spatialCalibrationUI != null) spatialCalibrator.spatialCalibrationUI.SetActive(false);
            if (spatialCalibrator.reticleUI != null) spatialCalibrator.reticleUI.SetActive(false);
            spatialCalibrator.enabled = false;
        }
    }

    /// <summary>
    /// Cập nhật kích thước Canvas theo chiều dọc / chiều ngang của điện thoại
    /// </summary>
    private void UpdateCanvasOrientation()
    {
        bool isLandscape = Screen.width > Screen.height;

        if (welcomeScaler != null)
        {
            if (isLandscape && welcomeScaler.referenceResolution.x < welcomeScaler.referenceResolution.y)
            {
                welcomeScaler.referenceResolution = new Vector2(1920, 1080);
                welcomeScaler.matchWidthOrHeight = 1f;
            }
            else if (!isLandscape && welcomeScaler.referenceResolution.x > welcomeScaler.referenceResolution.y)
            {
                welcomeScaler.referenceResolution = new Vector2(1080, 1920);
                welcomeScaler.matchWidthOrHeight = 0f;
            }
        }

        if (backButtonScaler != null)
        {
            if (isLandscape && backButtonScaler.referenceResolution.x < backButtonScaler.referenceResolution.y)
            {
                backButtonScaler.referenceResolution = new Vector2(1920, 1080);
                backButtonScaler.matchWidthOrHeight = 1f;
            }
            else if (!isLandscape && backButtonScaler.referenceResolution.x > backButtonScaler.referenceResolution.y)
            {
                backButtonScaler.referenceResolution = new Vector2(1080, 1920);
                backButtonScaler.matchWidthOrHeight = 0f;
            }
        }
    }

    // =========================================================================
    // XỬ LÝ SỰ KIỆN NÚT BẤM (BUTTON ONCLICK)
    // =========================================================================

    /// <summary>
    /// NÚT 1: "TỰ LẮP MẠCH"
    /// Bắt đầu truy cập Camera, ARSession, MediaPipe và chuyển sang giao diện AR TỨC THÌ (0ms delay)
    /// </summary>
    public void OnClickStartManualCircuit()
    {
        if (!isWelcomeScreenActive) return;
        Debug.Log("<color=green>[WelcomeScreenController]</color> Người dùng chọn 'TỰ LẮP MẠCH'. Khởi động luồng AR & Camera!");

        isWelcomeScreenActive = false;

        // 1. TỨC THÌ: Ẩn ngay lập tức Màn Hình Chính để không bị trễ hay chờ đợi!
        if (welcomeCanvas != null)
        {
            welcomeCanvas.gameObject.SetActive(false);
        }
        if (welcomeCanvasGroup != null)
        {
            welcomeCanvasGroup.alpha = 0f;
            welcomeCanvasGroup.interactable = false;
            welcomeCanvasGroup.blocksRaycasts = false;
        }

        // 2. Kích hoạt Menu Bong Bóng Chat AR & Nút Quay Lại ngay lập tức
        if (bubbleMenu != null)
        {
            bubbleMenu.SetMenuVisible(true);
        }
        SetBackButtonVisible(true);

        // 3. KÍCH HOẠT CAMERA VÀ AR SESSION NGAY TẠI ĐÂY (CHỈ KHI VÀO CHỨC NĂNG)
        StartARAndCamera();

        // 4. Kích hoạt căn chỉnh mặt phẳng mép bàn
        if (spatialCalibrator != null)
        {
            spatialCalibrator.RestartCalibration();
        }

        Debug.Log("<color=cyan>[WelcomeScreenController]</color> Đã vào thành công chế độ AR Pinch & Căn Chỉnh Mép Bàn!");
    }

    /// <summary>
    /// NÚT "‹ QUAY LẠI": Dừng Camera/AR và trở về Màn Hình Chính tức thì
    /// </summary>
    public void ReturnToWelcomeScreen()
    {
        Debug.Log("<color=cyan>[WelcomeScreenController]</color> Người dùng bấm 'QUAY LẠI' -> Tắt Camera và trở về Màn Hình Chính.");

        isWelcomeScreenActive = true;

        // 1. TẮT HOÀN TOÀN CAMERA & AR SESSION ĐỂ TIẾT KIỆM PIN VÀ GIẢI PHÓNG PHẦN CỨNG
        StopARAndCamera();

        // 2. Ẩn Nút Quay Lại và Menu Bong Bóng Chat
        SetBackButtonVisible(false);
        if (bubbleMenu != null)
        {
            bubbleMenu.SetMenuVisible(false);
            if (bubbleMenu.isBarExpanded) bubbleMenu.RetractMenu();
        }

        // 3. Ngưng căn chỉnh mặt phẳng & ẩn UI căn chỉnh
        SuppressARInitialState();

        // 4. Đảm bảo Modal Toast tắt hoàn toàn
        if (modalToastRoot != null)
        {
            modalToastRoot.SetActive(false);
            if (modalToastGroup != null)
            {
                modalToastGroup.alpha = 0f;
                modalToastGroup.interactable = false;
                modalToastGroup.blocksRaycasts = false;
            }
        }

        // 5. Hiển thị lại Màn Hình Chính tức thì
        if (welcomeCanvas != null)
        {
            welcomeCanvas.gameObject.SetActive(true);
        }
        if (welcomeCanvasGroup != null)
        {
            welcomeCanvasGroup.alpha = 1f;
            welcomeCanvasGroup.interactable = true;
            welcomeCanvasGroup.blocksRaycasts = true;
        }

        // Bật lại khả năng bấm các nút
        if (btnManualCircuit != null) btnManualCircuit.interactable = true;
        if (btnAIScan != null) btnAIScan.interactable = true;
    }

    /// <summary>
    /// NÚT 2: "QUÉT MẠCH AI"
    /// Placeholder: Hiển thị Toast / Popup thông báo tính năng đang phát triển
    /// </summary>
    public void OnClickShowAIPlaceholder()
    {
        Debug.Log("<color=yellow>[WelcomeScreenController]</color> Người dùng chọn 'QUÉT MẠCH AI' -> Hiển thị thông báo đang phát triển.");
        ShowAIModalToast();
    }

    /// <summary>
    /// Hiển thị Modal Toast thông báo với hiệu ứng nảy mượt mà và tự động ẩn sau 3 giây
    /// </summary>
    public void ShowAIModalToast()
    {
        if (modalToastRoot == null) return;

        modalToastRoot.SetActive(true);
        if (modalToastGroup != null)
        {
            modalToastGroup.interactable = true;
            modalToastGroup.blocksRaycasts = true;
        }

        if (toastCoroutine != null) StopCoroutine(toastCoroutine);
        toastCoroutine = StartCoroutine(AnimateToastPopup(true));
    }

    /// <summary>
    /// Đóng Modal Toast khi bấm "ĐÃ HIỂU" hoặc chạm ra ngoài
    /// </summary>
    public void DismissAIModalToast()
    {
        if (modalToastRoot == null || !modalToastRoot.activeSelf) return;

        if (toastCoroutine != null) StopCoroutine(toastCoroutine);
        toastCoroutine = StartCoroutine(AnimateToastPopup(false));
    }

    private IEnumerator AnimateToastPopup(bool showing)
    {
        float duration = 0.22f;
        float elapsed = 0f;

        Vector3 startScale = showing ? new Vector3(0.7f, 0.7f, 1f) : Vector3.one;
        Vector3 endScale = showing ? Vector3.one : new Vector3(0.7f, 0.7f, 1f);
        float startAlpha = showing ? 0f : 1f;
        float endAlpha = showing ? 1f : 0f;

        if (showing && modalToastGroup != null) modalToastGroup.alpha = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            float ease = showing
                ? 1f + 1.2f * Mathf.Pow(t - 1f, 3) + 1.2f * Mathf.Pow(t - 1f, 2)
                : Mathf.Sin(t * Mathf.PI * 0.5f);

            if (modalToastCard != null) modalToastCard.localScale = Vector3.LerpUnclamped(startScale, endScale, ease);
            if (modalToastGroup != null) modalToastGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
            yield return null;
        }

        if (modalToastCard != null) modalToastCard.localScale = endScale;
        if (modalToastGroup != null) modalToastGroup.alpha = endAlpha;

        if (!showing)
        {
            modalToastRoot.SetActive(false);
            if (modalToastGroup != null)
            {
                modalToastGroup.alpha = 0f;
                modalToastGroup.interactable = false;
                modalToastGroup.blocksRaycasts = false;
            }
        }
        else
        {
            yield return new WaitForSeconds(3.0f);
            if (modalToastRoot != null && modalToastRoot.activeSelf)
            {
                DismissAIModalToast();
            }
        }
    }

    /// <summary>
    /// Bật/Tắt hiển thị Nút Quay Lại
    /// </summary>
    public void SetBackButtonVisible(bool visible)
    {
        if (backButtonCanvas != null)
        {
            backButtonCanvas.gameObject.SetActive(visible);
        }
    }

    // =========================================================================
    // DỰNG NÚT "‹ QUAY LẠI" (GÓC TRÊN BÊN TRÁI MÀN HÌNH AR)
    // =========================================================================

    private void BuildBackButtonUI()
    {
        EnsureSprites();

        // Canvas riêng cho thanh điều hướng AR (SortingOrder = 1000, luôn nổi trên cùng)
        GameObject canvasObj = new GameObject("ARBackButton_Canvas");
        backButtonCanvas = canvasObj.AddComponent<Canvas>();
        backButtonCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        backButtonCanvas.sortingOrder = 1000;

        backButtonScaler = canvasObj.AddComponent<CanvasScaler>();
        backButtonScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        backButtonScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        backButtonScaler.referenceResolution = new Vector2(1080, 1920);
        backButtonScaler.matchWidthOrHeight = 0f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // Nút bấm bo tròn "‹ QUAY LẠI" ở góc trên bên trái
        GameObject btnObj = new GameObject("Btn_BackToWelcome", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(canvasObj.transform, false);
        rtBackBtn = btnObj.GetComponent<RectTransform>();
        rtBackBtn.anchorMin = new Vector2(0f, 1f);
        rtBackBtn.anchorMax = new Vector2(0f, 1f);
        rtBackBtn.pivot = new Vector2(0f, 1f);
        rtBackBtn.sizeDelta = new Vector2(230, 80);
        rtBackBtn.anchoredPosition = new Vector2(40f, -65f); // Góc trên bên trái an toàn

        Image btnImg = btnObj.GetComponent<Image>();
        btnImg.sprite = roundedRectSprite;
        btnImg.type = Image.Type.Sliced;
        btnImg.color = new Color(0.05f, 0.09f, 0.16f, 0.92f); // Kính tối màu
        btnImg.raycastTarget = true;

        Outline outline = btnObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0.9f, 1f, 0.95f); // Viền Cyan phát sáng
        outline.effectDistance = new Vector2(2f, -2f);

        btnBackToWelcome = btnObj.GetComponent<Button>();
        btnBackToWelcome.targetGraphic = btnImg;
        btnBackToWelcome.onClick.AddListener(ReturnToWelcomeScreen);

        // Chữ "‹ QUAY LẠI"
        GameObject textObj = new GameObject("Text", typeof(RectTransform));
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.font = GetSafeFont();
        tmp.text = "‹ QUAY LẠI";
        tmp.fontSize = 24;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        // Đăng ký nút vào ButtonInteractor để có thể chụm tay (Pinch) ngoài việc chạm tay
        if (buttonInteractor != null)
        {
            buttonInteractor.RegisterButton(rtBackBtn);
        }

        Debug.Log("<color=cyan>[WelcomeScreenController]</color> Đã dựng hoàn tất Nút Quay Lại tại góc trên bên trái.");
    }

    // =========================================================================
    // DỰNG GIAO DIỆN MÀN HÌNH CHÍNH (WELCOME SCREEN)
    // =========================================================================

    private void BuildWelcomeScreenUI()
    {
        EnsureSprites();

        // 1. TẠO CANVAS CHÍNH
        GameObject canvasObj = new GameObject("WelcomeScreen_Canvas");
        welcomeCanvas = canvasObj.AddComponent<Canvas>();
        welcomeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        welcomeCanvas.sortingOrder = 3000; // Đặt cực cao để luôn luôn nằm trên cùng và hiển thị tức thì

        welcomeScaler = canvasObj.AddComponent<CanvasScaler>();
        welcomeScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        welcomeScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        welcomeScaler.referenceResolution = new Vector2(1080, 1920);
        welcomeScaler.matchWidthOrHeight = 0f;

        canvasObj.AddComponent<GraphicRaycaster>();
        welcomeCanvasGroup = canvasObj.AddComponent<CanvasGroup>();
        welcomeCanvasGroup.alpha = 1f;
        welcomeCanvasGroup.interactable = true;
        welcomeCanvasGroup.blocksRaycasts = true;

        // 2. HÌNH NỀN TỐI SANG TRỌNG CÔNG NGHỆ (Opaque Dark Slate: #080D1A)
        GameObject bgObj = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgObj.transform.SetParent(canvasObj.transform, false);
        RectTransform bgRT = bgObj.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        Image bgImg = bgObj.GetComponent<Image>();
        bgImg.color = new Color(0.045f, 0.07f, 0.12f, 1f); // 100% che kín hoàn toàn
        bgImg.raycastTarget = true;

        // 3. VÙNG TIÊU ĐỀ & BRANDING (TOP - CENTER)
        BuildHeaderSection(canvasObj.transform);

        // 4. VÙNG 2 NÚT LỚN Ở GIỮA MÀN HÌNH (CENTER)
        BuildCenterButtonsSection(canvasObj.transform);

        // 5. VÙNG CHÂN TRANG (FOOTER)
        BuildFooterSection(canvasObj.transform);

        // 6. POPUP TOAST 'TÍNH NĂNG ĐANG PHÁT TRIỂN' (MODAL DIALOG)
        BuildModalToastPopup(canvasObj.transform);

        welcomeCanvas.gameObject.SetActive(true);
        welcomeCanvas.enabled = true;

        Debug.Log("<color=green>[WelcomeScreenController]</color> Đã khởi tạo hoàn tất Màn Hình Chính CircuitAR với 2 nút lớn!");
    }

    private void BuildHeaderSection(Transform parent)
    {
        GameObject headerObj = new GameObject("Header_Section", typeof(RectTransform));
        headerObj.transform.SetParent(parent, false);
        RectTransform headerRT = headerObj.GetComponent<RectTransform>();
        headerRT.anchorMin = new Vector2(0.5f, 0.5f);
        headerRT.anchorMax = new Vector2(0.5f, 0.5f);
        headerRT.pivot = new Vector2(0.5f, 0.5f);
        headerRT.sizeDelta = new Vector2(900, 360);
        headerRT.anchoredPosition = new Vector2(0, 480f);

        // 1. Icon Biểu Trưng Logo Mạch Điện Phát Sáng
        GameObject logoBadge = new GameObject("Logo_Badge", typeof(RectTransform), typeof(Image));
        logoBadge.transform.SetParent(headerRT, false);
        RectTransform logoRT = logoBadge.GetComponent<RectTransform>();
        logoRT.anchorMin = new Vector2(0.5f, 1f);
        logoRT.anchorMax = new Vector2(0.5f, 1f);
        logoRT.pivot = new Vector2(0.5f, 1f);
        logoRT.sizeDelta = new Vector2(130, 130);
        logoRT.anchoredPosition = new Vector2(0, 0);

        Image logoImg = logoBadge.GetComponent<Image>();
        logoImg.sprite = circleSprite;
        logoImg.color = new Color(0.06f, 0.14f, 0.26f, 0.95f);

        Outline logoOutline = logoBadge.AddComponent<Outline>();
        logoOutline.effectColor = new Color(0f, 0.95f, 1f, 0.9f);
        logoOutline.effectDistance = new Vector2(3f, -3f);

        Sprite mainIconSprite = UIIconAssets.GetMainBubbleSprite();
        if (mainIconSprite != null)
        {
            GameObject iconInner = new GameObject("Logo_Icon", typeof(RectTransform), typeof(Image));
            iconInner.transform.SetParent(logoBadge.transform, false);
            RectTransform iconInnerRT = iconInner.GetComponent<RectTransform>();
            iconInnerRT.anchorMin = Vector2.zero;
            iconInnerRT.anchorMax = Vector2.one;
            iconInnerRT.offsetMin = new Vector2(16, 16);
            iconInnerRT.offsetMax = new Vector2(-16, -16);

            Image iconInnerImg = iconInner.GetComponent<Image>();
            iconInnerImg.sprite = mainIconSprite;
            iconInnerImg.preserveAspect = true;
            iconInnerImg.raycastTarget = false;
        }

        // 2. Tên App Chính: "CircuitAR"
        GameObject titleObj = new GameObject("App_Title", typeof(RectTransform));
        titleObj.transform.SetParent(headerRT, false);
        RectTransform titleRT = titleObj.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0.5f, 0.5f);
        titleRT.anchorMax = new Vector2(0.5f, 0.5f);
        titleRT.pivot = new Vector2(0.5f, 0.5f);
        titleRT.sizeDelta = new Vector2(800, 90);
        titleRT.anchoredPosition = new Vector2(0, -65f);

        TextMeshProUGUI titleTMP = titleObj.AddComponent<TextMeshProUGUI>();
        titleTMP.font = GetSafeFont();
        titleTMP.text = "CircuitAR";
        titleTMP.fontSize = 66;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = new Color(0.92f, 0.98f, 1f, 1f);
        titleTMP.characterSpacing = 4f;

        // 3. Phụ đề
        GameObject subtitleObj = new GameObject("App_Subtitle", typeof(RectTransform));
        subtitleObj.transform.SetParent(headerRT, false);
        RectTransform subtitleRT = subtitleObj.GetComponent<RectTransform>();
        subtitleRT.anchorMin = new Vector2(0.5f, 0.5f);
        subtitleRT.anchorMax = new Vector2(0.5f, 0.5f);
        subtitleRT.pivot = new Vector2(0.5f, 0.5f);
        subtitleRT.sizeDelta = new Vector2(860, 50);
        subtitleRT.anchoredPosition = new Vector2(0, -125f);

        TextMeshProUGUI subTMP = subtitleObj.AddComponent<TextMeshProUGUI>();
        subTMP.font = GetSafeFont();
        subTMP.text = "PHÒNG THÍ NGHIỆM MẠCH ĐIỆN TƯƠNG TÁC AR";
        subTMP.fontSize = 23;
        subTMP.fontStyle = FontStyles.Bold;
        subTMP.alignment = TextAlignmentOptions.Center;
        subTMP.color = new Color(0.35f, 0.82f, 0.98f, 0.95f);
        subTMP.characterSpacing = 2f;

        // 4. Đường gạch phát sáng Neon
        GameObject lineObj = new GameObject("Divider_Line", typeof(RectTransform), typeof(Image));
        lineObj.transform.SetParent(headerRT, false);
        RectTransform lineRT = lineObj.GetComponent<RectTransform>();
        lineRT.anchorMin = new Vector2(0.5f, 0.5f);
        lineRT.anchorMax = new Vector2(0.5f, 0.5f);
        lineRT.pivot = new Vector2(0.5f, 0.5f);
        lineRT.sizeDelta = new Vector2(460, 3);
        lineRT.anchoredPosition = new Vector2(0, -165f);

        Image lineImg = lineObj.GetComponent<Image>();
        lineImg.color = new Color(0f, 0.85f, 1f, 0.7f);
        lineImg.raycastTarget = false;
    }

    private void BuildCenterButtonsSection(Transform parent)
    {
        GameObject buttonsGroup = new GameObject("Buttons_CenterGroup", typeof(RectTransform));
        buttonsGroup.transform.SetParent(parent, false);
        RectTransform groupRT = buttonsGroup.GetComponent<RectTransform>();
        groupRT.anchorMin = new Vector2(0.5f, 0.5f);
        groupRT.anchorMax = new Vector2(0.5f, 0.5f);
        groupRT.pivot = new Vector2(0.5f, 0.5f);
        groupRT.sizeDelta = new Vector2(860, 480);
        groupRT.anchoredPosition = new Vector2(0, -30f);

        // NÚT 1: "TỰ LẮP MẠCH" (780px x 155px)
        Sprite toolboxSprite = UIIconAssets.GetToolboxSprite();
        GameObject btn1Obj = CreateBigActionButton(
            parent: groupRT,
            name: "Btn_ManualCircuit",
            size: new Vector2(780, 155),
            anchoredPos: new Vector2(0, 95f),
            bgColor: new Color(0.04f, 0.55f, 0.75f, 0.98f),
            glowColor: new Color(0f, 0.95f, 1f, 1f),
            iconSprite: toolboxSprite,
            titleText: "TỰ LẮP MẠCH",
            subtitleText: "Khởi động AR & cử chỉ tay lắp ráp mạch điện",
            onClick: OnClickStartManualCircuit
        );
        btnManualCircuit = btn1Obj.GetComponent<Button>();

        // NÚT 2: "QUÉT MẠCH AI" (780px x 155px)
        Sprite scanSprite = UIIconAssets.GetScanSprite();
        GameObject btn2Obj = CreateBigActionButton(
            parent: groupRT,
            name: "Btn_AIScan",
            size: new Vector2(780, 155),
            anchoredPos: new Vector2(0, -95f),
            bgColor: new Color(0.38f, 0.16f, 0.74f, 0.98f),
            glowColor: new Color(0.75f, 0.45f, 1f, 1f),
            iconSprite: scanSprite,
            titleText: "QUÉT MẠCH AI",
            subtitleText: "Nhận diện mạch thực tế qua camera AI",
            onClick: OnClickShowAIPlaceholder
        );
        btnAIScan = btn2Obj.GetComponent<Button>();

        // Huy hiệu "SẮP RA MẮT"
        GameObject badgeObj = new GameObject("Badge_ComingSoon", typeof(RectTransform), typeof(Image));
        badgeObj.transform.SetParent(btn2Obj.transform, false);
        RectTransform badgeRT = badgeObj.GetComponent<RectTransform>();
        badgeRT.anchorMin = new Vector2(1f, 1f);
        badgeRT.anchorMax = new Vector2(1f, 1f);
        badgeRT.pivot = new Vector2(1f, 0.5f);
        badgeRT.sizeDelta = new Vector2(170, 36);
        badgeRT.anchoredPosition = new Vector2(-20, 0);

        Image badgeImg = badgeObj.GetComponent<Image>();
        badgeImg.sprite = roundedRectSprite;
        badgeImg.type = Image.Type.Sliced;
        badgeImg.color = new Color(0.96f, 0.62f, 0.05f, 0.95f);
        badgeImg.raycastTarget = false;

        GameObject badgeTextObj = new GameObject("Text", typeof(RectTransform));
        badgeTextObj.transform.SetParent(badgeObj.transform, false);
        RectTransform badgeTextRT = badgeTextObj.GetComponent<RectTransform>();
        badgeTextRT.anchorMin = Vector2.zero;
        badgeTextRT.anchorMax = Vector2.one;
        badgeTextRT.offsetMin = Vector2.zero;
        badgeTextRT.offsetMax = Vector2.zero;

        TextMeshProUGUI badgeTMP = badgeTextObj.AddComponent<TextMeshProUGUI>();
        badgeTMP.font = GetSafeFont();
        badgeTMP.text = "SẮP RA MẮT";
        badgeTMP.fontSize = 17;
        badgeTMP.fontStyle = FontStyles.Bold;
        badgeTMP.alignment = TextAlignmentOptions.Center;
        badgeTMP.color = new Color(0.08f, 0.05f, 0.01f, 1f);
        badgeTMP.raycastTarget = false;
    }

    private GameObject CreateBigActionButton(
        Transform parent,
        string name,
        Vector2 size,
        Vector2 anchoredPos,
        Color bgColor,
        Color glowColor,
        Sprite iconSprite,
        string titleText,
        string subtitleText,
        System.Action onClick
    )
    {
        GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);
        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        Image img = btnObj.GetComponent<Image>();
        img.sprite = roundedRectSprite;
        img.type = Image.Type.Sliced;
        img.color = bgColor;
        img.raycastTarget = true;

        Button btn = btnObj.GetComponent<Button>();
        btn.targetGraphic = img;

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.colorMultiplier = 1f;
        btn.colors = cb;

        if (onClick != null) btn.onClick.AddListener(() => onClick());

        Outline outline = btnObj.AddComponent<Outline>();
        outline.effectColor = glowColor;
        outline.effectDistance = new Vector2(3f, -3f);

        if (iconSprite != null)
        {
            GameObject iconCircle = new GameObject("Icon_Circle", typeof(RectTransform), typeof(Image));
            iconCircle.transform.SetParent(btnObj.transform, false);
            RectTransform icRT = iconCircle.GetComponent<RectTransform>();
            icRT.anchorMin = new Vector2(0f, 0.5f);
            icRT.anchorMax = new Vector2(0f, 0.5f);
            icRT.pivot = new Vector2(0.5f, 0.5f);
            icRT.sizeDelta = new Vector2(105, 105);
            icRT.anchoredPosition = new Vector2(75, 0);

            Image icImg = iconCircle.GetComponent<Image>();
            icImg.sprite = circleSprite;
            icImg.color = new Color(0.04f, 0.08f, 0.16f, 0.6f);
            icImg.raycastTarget = false;

            GameObject iconInner = new GameObject("Icon_Inner", typeof(RectTransform), typeof(Image));
            iconInner.transform.SetParent(iconCircle.transform, false);
            RectTransform iiRT = iconInner.GetComponent<RectTransform>();
            iiRT.anchorMin = Vector2.zero;
            iiRT.anchorMax = Vector2.one;
            iiRT.offsetMin = new Vector2(12, 12);
            iiRT.offsetMax = new Vector2(-12, -12);

            Image iiImg = iconInner.GetComponent<Image>();
            iiImg.sprite = iconSprite;
            iiImg.preserveAspect = true;
            iiImg.raycastTarget = false;
        }

        GameObject titleObj = new GameObject("Text_Title", typeof(RectTransform));
        titleObj.transform.SetParent(btnObj.transform, false);
        RectTransform titleRT = titleObj.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0f, 0.5f);
        titleRT.anchorMax = new Vector2(1f, 0.5f);
        titleRT.pivot = new Vector2(0f, 0.5f);
        titleRT.offsetMin = new Vector2(150, 6);
        titleRT.offsetMax = new Vector2(-60, 50);

        TextMeshProUGUI titleTMP = titleObj.AddComponent<TextMeshProUGUI>();
        titleTMP.font = GetSafeFont();
        titleTMP.text = titleText;
        titleTMP.fontSize = 36;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.MidlineLeft;
        titleTMP.color = Color.white;
        titleTMP.raycastTarget = false;

        GameObject subObj = new GameObject("Text_Subtitle", typeof(RectTransform));
        subObj.transform.SetParent(btnObj.transform, false);
        RectTransform subRT = subObj.GetComponent<RectTransform>();
        subRT.anchorMin = new Vector2(0f, 0.5f);
        subRT.anchorMax = new Vector2(1f, 0.5f);
        subRT.pivot = new Vector2(0f, 0.5f);
        subRT.offsetMin = new Vector2(150, -42);
        subRT.offsetMax = new Vector2(-60, 6);

        TextMeshProUGUI subTMP = subObj.AddComponent<TextMeshProUGUI>();
        subTMP.font = GetSafeFont();
        subTMP.text = subtitleText;
        subTMP.fontSize = 19;
        subTMP.fontStyle = FontStyles.Normal;
        subTMP.alignment = TextAlignmentOptions.MidlineLeft;
        subTMP.color = new Color(0.9f, 0.95f, 1f, 0.85f);
        subTMP.raycastTarget = false;

        GameObject arrowObj = new GameObject("Arrow_Icon", typeof(RectTransform));
        arrowObj.transform.SetParent(btnObj.transform, false);
        RectTransform arrowRT = arrowObj.GetComponent<RectTransform>();
        arrowRT.anchorMin = new Vector2(1f, 0.5f);
        arrowRT.anchorMax = new Vector2(1f, 0.5f);
        arrowRT.pivot = new Vector2(1f, 0.5f);
        arrowRT.sizeDelta = new Vector2(60, 60);
        arrowRT.anchoredPosition = new Vector2(-25, 0);

        TextMeshProUGUI arrowTMP = arrowObj.AddComponent<TextMeshProUGUI>();
        arrowTMP.font = GetSafeFont();
        arrowTMP.text = "›";
        arrowTMP.fontSize = 54;
        arrowTMP.fontStyle = FontStyles.Bold;
        arrowTMP.alignment = TextAlignmentOptions.Center;
        arrowTMP.color = new Color(1f, 1f, 1f, 0.6f);
        arrowTMP.raycastTarget = false;

        return btnObj;
    }

    private void BuildFooterSection(Transform parent)
    {
        GameObject footerObj = new GameObject("Footer_Section", typeof(RectTransform));
        footerObj.transform.SetParent(parent, false);
        RectTransform footerRT = footerObj.GetComponent<RectTransform>();
        footerRT.anchorMin = new Vector2(0.5f, 0f);
        footerRT.anchorMax = new Vector2(0.5f, 0f);
        footerRT.pivot = new Vector2(0.5f, 0f);
        footerRT.sizeDelta = new Vector2(800, 100);
        footerRT.anchoredPosition = new Vector2(0, 60f);

        TextMeshProUGUI ftTMP = footerObj.AddComponent<TextMeshProUGUI>();
        ftTMP.font = GetSafeFont();
        ftTMP.text = "CircuitAR • Phiên bản 1.2 • Google MediaPipe XR Engine\nChạm tay để chọn chế độ hoạt động";
        ftTMP.fontSize = 19;
        ftTMP.fontStyle = FontStyles.Normal;
        ftTMP.alignment = TextAlignmentOptions.Center;
        ftTMP.color = new Color(0.45f, 0.55f, 0.68f, 0.85f);
        ftTMP.lineSpacing = 25f;
        ftTMP.raycastTarget = false;
    }

    // =========================================================================
    // MODAL DIALOG / TOAST "TÍNH NĂNG ĐANG PHÁT TRIỂN"
    // =========================================================================

    private void BuildModalToastPopup(Transform parent)
    {
        modalToastRoot = new GameObject("ModalToast_Backdrop", typeof(RectTransform), typeof(Image));
        modalToastRoot.transform.SetParent(parent, false);
        RectTransform bdRT = modalToastRoot.GetComponent<RectTransform>();
        bdRT.anchorMin = Vector2.zero;
        bdRT.anchorMax = Vector2.one;
        bdRT.offsetMin = Vector2.zero;
        bdRT.offsetMax = Vector2.zero;

        Image bdImg = modalToastRoot.GetComponent<Image>();
        bdImg.color = new Color(0f, 0f, 0f, 0.72f);
        bdImg.raycastTarget = true;

        Button bdBtn = modalToastRoot.AddComponent<Button>();
        bdBtn.onClick.AddListener(DismissAIModalToast);

        modalToastGroup = modalToastRoot.AddComponent<CanvasGroup>();

        GameObject cardObj = new GameObject("Card_Dialog", typeof(RectTransform), typeof(Image));
        cardObj.transform.SetParent(modalToastRoot.transform, false);
        modalToastCard = cardObj.GetComponent<RectTransform>();
        modalToastCard.anchorMin = new Vector2(0.5f, 0.5f);
        modalToastCard.anchorMax = new Vector2(0.5f, 0.5f);
        modalToastCard.pivot = new Vector2(0.5f, 0.5f);
        modalToastCard.sizeDelta = new Vector2(760, 420);
        modalToastCard.anchoredPosition = Vector2.zero;

        Image cardImg = cardObj.GetComponent<Image>();
        cardImg.sprite = roundedRectSprite;
        cardImg.type = Image.Type.Sliced;
        cardImg.color = new Color(0.07f, 0.11f, 0.19f, 0.98f);
        cardImg.raycastTarget = true;

        Outline cardOutline = cardObj.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.96f, 0.62f, 0.05f, 0.95f);
        cardOutline.effectDistance = new Vector2(3f, -3f);

        GameObject iconObj = new GameObject("Icon_Badge", typeof(RectTransform), typeof(Image));
        iconObj.transform.SetParent(cardObj.transform, false);
        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 1f);
        iconRT.anchorMax = new Vector2(0.5f, 1f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.sizeDelta = new Vector2(90, 90);
        iconRT.anchoredPosition = new Vector2(0, -20f);

        Image iconImg = iconObj.GetComponent<Image>();
        iconImg.sprite = circleSprite;
        iconImg.color = new Color(0.96f, 0.62f, 0.05f, 0.25f);
        iconImg.raycastTarget = false;

        Outline iconOutline = iconObj.AddComponent<Outline>();
        iconOutline.effectColor = new Color(0.96f, 0.62f, 0.05f, 0.9f);
        iconOutline.effectDistance = new Vector2(2f, -2f);

        GameObject iconInner = new GameObject("Icon_Char", typeof(RectTransform));
        iconInner.transform.SetParent(iconObj.transform, false);
        RectTransform iiRT = iconInner.GetComponent<RectTransform>();
        iiRT.anchorMin = Vector2.zero;
        iiRT.anchorMax = Vector2.one;
        iiRT.offsetMin = Vector2.zero;
        iiRT.offsetMax = Vector2.zero;

        TextMeshProUGUI iiTMP = iconInner.AddComponent<TextMeshProUGUI>();
        iiTMP.font = GetSafeFont();
        iiTMP.text = "!";
        iiTMP.fontSize = 54;
        iiTMP.fontStyle = FontStyles.Bold;
        iiTMP.alignment = TextAlignmentOptions.Center;
        iiTMP.color = new Color(0.96f, 0.62f, 0.05f, 1f);
        iiTMP.raycastTarget = false;

        GameObject titleObj = new GameObject("Title_Text", typeof(RectTransform));
        titleObj.transform.SetParent(cardObj.transform, false);
        RectTransform titleRT = titleObj.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0.5f, 1f);
        titleRT.anchorMax = new Vector2(0.5f, 1f);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.sizeDelta = new Vector2(700, 50);
        titleRT.anchoredPosition = new Vector2(0, -85f);

        TextMeshProUGUI titleTMP = titleObj.AddComponent<TextMeshProUGUI>();
        titleTMP.font = GetSafeFont();
        titleTMP.text = "QUÉT MẠCH AI";
        titleTMP.fontSize = 30;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = new Color(0.96f, 0.62f, 0.05f, 1f);
        titleTMP.characterSpacing = 2f;
        titleTMP.raycastTarget = false;

        GameObject msgObj = new GameObject("Message_Text", typeof(RectTransform));
        msgObj.transform.SetParent(cardObj.transform, false);
        RectTransform msgRT = msgObj.GetComponent<RectTransform>();
        msgRT.anchorMin = new Vector2(0.5f, 0.5f);
        msgRT.anchorMax = new Vector2(0.5f, 0.5f);
        msgRT.pivot = new Vector2(0.5f, 0.5f);
        msgRT.sizeDelta = new Vector2(700, 110);
        msgRT.anchoredPosition = new Vector2(0, -10f);

        TextMeshProUGUI msgTMP = msgObj.AddComponent<TextMeshProUGUI>();
        msgTMP.font = GetSafeFont();
        msgTMP.text = "Tính năng đang phát triển, vui lòng quay lại sau!\n<size=18><color=#94A3B8>Hệ thống nhận diện AI và sinh mạch AR tự động đang được hoàn thiện.</color></size>";
        msgTMP.fontSize = 24;
        msgTMP.fontStyle = FontStyles.Bold;
        msgTMP.alignment = TextAlignmentOptions.Center;
        msgTMP.color = Color.white;
        msgTMP.lineSpacing = 20f;
        msgTMP.raycastTarget = false;

        GameObject dismissBtnObj = new GameObject("Btn_Dismiss", typeof(RectTransform), typeof(Image), typeof(Button));
        dismissBtnObj.transform.SetParent(cardObj.transform, false);
        RectTransform disRT = dismissBtnObj.GetComponent<RectTransform>();
        disRT.anchorMin = new Vector2(0.5f, 0f);
        disRT.anchorMax = new Vector2(0.5f, 0f);
        disRT.pivot = new Vector2(0.5f, 0f);
        disRT.sizeDelta = new Vector2(320, 75);
        disRT.anchoredPosition = new Vector2(0, 35f);

        Image disImg = dismissBtnObj.GetComponent<Image>();
        disImg.sprite = roundedRectSprite;
        disImg.type = Image.Type.Sliced;
        disImg.color = new Color(0.96f, 0.62f, 0.05f, 0.95f);
        disImg.raycastTarget = true;

        btnToastDismiss = dismissBtnObj.GetComponent<Button>();
        btnToastDismiss.targetGraphic = disImg;
        btnToastDismiss.onClick.AddListener(DismissAIModalToast);

        GameObject disTextObj = new GameObject("Text", typeof(RectTransform));
        disTextObj.transform.SetParent(dismissBtnObj.transform, false);
        RectTransform dtRT = disTextObj.GetComponent<RectTransform>();
        dtRT.anchorMin = Vector2.zero;
        dtRT.anchorMax = Vector2.one;
        dtRT.offsetMin = Vector2.zero;
        dtRT.offsetMax = Vector2.zero;

        TextMeshProUGUI dtTMP = disTextObj.AddComponent<TextMeshProUGUI>();
        dtTMP.font = GetSafeFont();
        dtTMP.text = "ĐÃ HIỂU";
        dtTMP.fontSize = 24;
        dtTMP.fontStyle = FontStyles.Bold;
        dtTMP.alignment = TextAlignmentOptions.Center;
        dtTMP.color = new Color(0.08f, 0.05f, 0.01f, 1f);
        dtTMP.raycastTarget = false;

        // TẮT HOÀN TOÀN MODAL TOAST NGAY KHI VỪA TẠO XONG ĐỂ KHÔNG BAO GIỜ TỰ HIỆN:
        modalToastRoot.SetActive(false);
        if (modalToastGroup != null)
        {
            modalToastGroup.alpha = 0f;
            modalToastGroup.interactable = false;
            modalToastGroup.blocksRaycasts = false;
        }
    }

    // =========================================================================
    // HELPER SPRITES NGUYÊN BẢN (PROCEDURAL CIRCLE & ROUNDED RECT)
    // =========================================================================

    private static void EnsureSprites()
    {
        if (circleSprite == null) circleSprite = CreateProceduralCircle(128);
        if (roundedRectSprite == null) roundedRectSprite = CreateProceduralRoundedRect(128, 128, 36);
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
