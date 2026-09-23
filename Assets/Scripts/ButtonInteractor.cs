using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using Mediapipe.Tasks.Vision.HandLandmarker;

public class ButtonInteractor : MonoBehaviour
{
    [Header("KÉO OBJECT 'Screen' VÀO ĐÂY")]
    public RectTransform videoPanel;

    [Header("Kéo Nút Vàng (Debug Point - Ngón Trỏ) vào đây")]
    public RectTransform debugPoint;

    [Header("Kéo Nút Ngón Cái (Thumb Point) vào đây (Tự động tạo nếu để trống)")]
    public RectTransform thumbPoint;

    [Header("Tham chiếu Hệ thống")]
    public HandLandmarkerRunner handRunner;

    [Header("Kết nối Logic Game")]
    public MenuHUDController menuHUD;

    [Header("Danh sách Nút tương tác")]
    public List<RectTransform> interactiveButtons;

    [Header("Cấu hình màu sắc con trỏ")]
    public Color hoverColor = new Color(0.7f, 0.9f, 1f);
    public Color pressColor = Color.green;
    public Color indexNormalColor = new Color(1f, 0.88f, 0.1f, 0.95f); // Vàng tươi ngón trỏ
    public Color thumbNormalColor = new Color(0.15f, 0.85f, 1f, 0.95f); // Cyan ngón cái

    [Header("Cấu hình Pinch Hysteresis, Ổn định & Chống Spam")]
    [Tooltip("Ngưỡng khoảng cách bắt đầu chụm ngón tay (Pinch ON)")]
    [Range(0.02f, 0.08f)] public float pinchThreshold = 0.040f; // pinchEnterThreshold
    [Tooltip("Ngưỡng khoảng cách nhả ngón tay (Pinch OFF - Hysteresis chống rung mép)")]
    [Range(0.03f, 0.10f)] public float pinchExitThreshold = 0.052f;
    [Tooltip("Số frame liên tục phải thỏa điều kiện để xác nhận trạng thái (chống nhiễu 1 frame)")]
    public int minStablePinchFrames = 2;
    [Tooltip("Thời gian giãn cách tối thiểu giữa 2 lần kích hoạt Pinch (giây)")]
    public float pinchDebounceTime = 0.25f;
    public float clickCooldown = 0.45f;

    [Header("Bảng Điều Khiển Trục Tọa Độ")]
    public bool hoanDoiTrucXY = true; // Bật sẵn để sửa lỗi lên thành trái
    public bool latNguocTrucX = false;
    public bool latNguocTrucY = false;

    // Tọa độ màn hình ngón trỏ và ngón cái cho các hệ thống khác sử dụng
    public Vector2 CurrentScreenPos { get; private set; }
    public Vector2 CurrentThumbScreenPos { get; private set; }
    public bool JustPinched { get; private set; }

    private Dictionary<RectTransform, Image> buttonImages = new Dictionary<RectTransform, Image>();
    private Dictionary<RectTransform, Color> originalColors = new Dictionary<RectTransform, Color>();

    private float targetX = 0f;
    private float targetY = 0f;
    private float targetThumbX = 0f;
    private float targetThumbY = 0f;
    private float rawDistance = 1f;
    private bool isHandVisible = false;
    public bool isPinching = false;
    private bool wasPinching = false;       // dùng riêng cho logic click nút menu
    private bool wasPinchingGlobal = false; // dùng riêng cho JustPinched
    private int consecutivePinchFrames = 0;
    private int consecutiveReleaseFrames = 0;
    private float lastGlobalPinchTime = 0f;
    private float lastClickTime = 0f;
    private object _lock = new object();

    private Image debugPointImage;
    private Image thumbPointImage;

    void Start()
    {
        for (int i = interactiveButtons.Count - 1; i >= 0; i--)
        {
            var btn = interactiveButtons[i];
            if (btn != null)
            {
                RegisterButton(btn);
            }
        }

        SetupFingerPointVisuals();
    }

    /// <summary>
    /// Thiết lập kích thước và tự động tạo chấm ngón cái nếu chưa có
    /// </summary>
    private void SetupFingerPointVisuals()
    {
        if (debugPoint != null)
        {
            debugPoint.sizeDelta = new Vector2(28f, 28f);
            debugPointImage = debugPoint.GetComponent<Image>();
            if (debugPointImage != null) debugPointImage.color = indexNormalColor;
        }

        if (thumbPoint == null && videoPanel != null)
        {
            GameObject thumbObj = new GameObject("ThumbPoint_Auto", typeof(RectTransform), typeof(Image));
            thumbObj.transform.SetParent(videoPanel, false);
            thumbPoint = thumbObj.GetComponent<RectTransform>();
            thumbPoint.sizeDelta = new Vector2(24f, 24f);

            thumbPointImage = thumbObj.GetComponent<Image>();
            if (debugPointImage != null && debugPointImage.sprite != null)
            {
                thumbPointImage.sprite = debugPointImage.sprite;
            }
            thumbPointImage.color = thumbNormalColor;
        }
        else if (thumbPoint != null)
        {
            thumbPoint.sizeDelta = new Vector2(24f, 24f);
            thumbPointImage = thumbPoint.GetComponent<Image>();
            if (thumbPointImage != null) thumbPointImage.color = thumbNormalColor;
        }
    }

    /// <summary>
    /// Đăng ký nút UI động vào hệ thống nhận diện cử chỉ MediaPipe
    /// </summary>
    public void RegisterButton(RectTransform btn)
    {
        if (btn == null) return;
        if (!interactiveButtons.Contains(btn))
        {
            interactiveButtons.Add(btn);
        }
        Image img = btn.GetComponent<Image>();
        if (img != null && !buttonImages.ContainsKey(btn))
        {
            buttonImages[btn] = img;
            originalColors[btn] = img.color;
        }
    }

    /// <summary>
    /// Hủy đăng ký nút UI khỏi hệ thống cử chỉ
    /// </summary>
    public void UnregisterButton(RectTransform btn)
    {
        if (btn == null) return;
        if (buttonImages.ContainsKey(btn))
        {
            buttonImages[btn].color = originalColors[btn];
            buttonImages.Remove(btn);
            originalColors.Remove(btn);
        }
        interactiveButtons.Remove(btn);
    }

    void OnEnable() { if (handRunner != null) handRunner.OnHandResult += ProcessHandResult; }
    void OnDisable() { if (handRunner != null) handRunner.OnHandResult -= ProcessHandResult; }

    void Update()
    {
        float x, y, tx, ty, dist;
        bool visible;

        lock (_lock)
        {
            x = targetX;
            y = targetY;
            tx = targetThumbX;
            ty = targetThumbY;
            dist = rawDistance;
            visible = isHandVisible;
        }

        if (!visible || videoPanel == null)
        {
            foreach (var btn in interactiveButtons)
            {
                if (btn != null)
                {
                    if (buttonImages.ContainsKey(btn))
                        buttonImages[btn].color = originalColors[btn];

                    HoldToActivateButton holdBtn = btn.GetComponent<HoldToActivateButton>();
                    if (holdBtn != null) holdBtn.ResetHoldState();
                }
            }
            if (debugPoint != null) debugPoint.gameObject.SetActive(false);
            if (thumbPoint != null) thumbPoint.gameObject.SetActive(false);

            JustPinched = false;
            wasPinchingGlobal = false;
            consecutivePinchFrames = 0;
            consecutiveReleaseFrames = 0;
            isPinching = false;
            return;
        }

        // --- 1. HYSTERESIS VÀ TEMPORAL STABILITY CHO PINCH DETECTION ---
        if (dist < pinchThreshold)
        {
            consecutivePinchFrames++;
            consecutiveReleaseFrames = 0;
        }
        else if (dist > pinchExitThreshold)
        {
            consecutiveReleaseFrames++;
            consecutivePinchFrames = 0;
        }

        if (!isPinching && consecutivePinchFrames >= minStablePinchFrames)
        {
            isPinching = true;
        }
        else if (isPinching && consecutiveReleaseFrames >= minStablePinchFrames)
        {
            isPinching = false;
        }

        // --- 2. CẬP NHẬT TỌA ĐỘ VÀ VISUAL CHO 2 ĐẦU NGÓN TAY (INDEX + THUMB) ---
        Vector2 screenPos = Vector2.zero;
        Vector2 thumbScreenPos = Vector2.zero;
        Canvas videoCanvas = videoPanel.GetComponentInParent<Canvas>();
        Camera uiCam = (videoCanvas != null && videoCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? videoCanvas.worldCamera : null;

        // Cập nhật chấm ngón trỏ
        if (debugPoint != null)
        {
            debugPoint.gameObject.SetActive(true);
            if (debugPoint.parent != videoPanel) debugPoint.SetParent(videoPanel);
            debugPoint.anchorMin = new Vector2(x, 1.0f - y);
            debugPoint.anchorMax = new Vector2(x, 1.0f - y);
            debugPoint.anchoredPosition = Vector2.zero;

            // Visual feedback khi pinch: phóng to 1.3x và đổi màu rực rỡ
            float indexScale = isPinching ? 1.32f : 1f;
            debugPoint.localScale = Vector3.one * indexScale;
            if (debugPointImage != null)
            {
                debugPointImage.color = isPinching ? pressColor : indexNormalColor;
            }

            screenPos = RectTransformUtility.WorldToScreenPoint(uiCam, debugPoint.position);
        }

        // Cập nhật chấm ngón cái
        if (thumbPoint != null)
        {
            thumbPoint.gameObject.SetActive(true);
            if (thumbPoint.parent != videoPanel) thumbPoint.SetParent(videoPanel);
            thumbPoint.anchorMin = new Vector2(tx, 1.0f - ty);
            thumbPoint.anchorMax = new Vector2(tx, 1.0f - ty);
            thumbPoint.anchoredPosition = Vector2.zero;

            float thumbScale = isPinching ? 1.32f : 1f;
            thumbPoint.localScale = Vector3.one * thumbScale;
            if (thumbPointImage != null)
            {
                thumbPointImage.color = isPinching ? pressColor : thumbNormalColor;
            }

            thumbScreenPos = RectTransformUtility.WorldToScreenPoint(uiCam, thumbPoint.position);
        }

        CurrentScreenPos = screenPos;
        CurrentThumbScreenPos = thumbScreenPos;
        bool anyButtonHovered = false;

        // --- 3. TƯƠNG TÁC GIAO DIỆN UI (HOVER / CLICK / HOLD) ---
        foreach (var btn in interactiveButtons)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy || !buttonImages.ContainsKey(btn)) continue;

            Canvas btnCanvas = btn.GetComponentInParent<Canvas>();
            Camera btnCam = (btnCanvas != null && btnCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? btnCanvas.worldCamera : null;

            if (btnCam == null && btnCanvas != null && btnCanvas.renderMode == RenderMode.WorldSpace)
            {
                btnCam = Camera.main;
            }

            bool isHovering = RectTransformUtility.RectangleContainsScreenPoint(btn, screenPos, btnCam);
            Image img = buttonImages[btn];

            HoldToActivateButton holdComp = btn.GetComponent<HoldToActivateButton>();
            if (holdComp != null)
            {
                holdComp.SetPinchHoverState(isHovering, isPinching);
                if (isHovering)
                {
                    anyButtonHovered = true;
                    img.color = isPinching ? pressColor : hoverColor;
                }
                else
                {
                    img.color = originalColors[btn];
                }
                continue;
            }

            if (isHovering)
            {
                anyButtonHovered = true;
                img.color = isPinching ? pressColor : hoverColor;

                if (isPinching && !wasPinching && Time.time - lastClickTime > clickCooldown)
                {
                    Button uiBtn = btn.GetComponent<Button>();
                    if (uiBtn != null && uiBtn.interactable)
                    {
                        uiBtn.onClick.Invoke();
                    }
                    if (menuHUD != null) menuHUD.ReceiveClick(btn);
                    lastClickTime = Time.time;
                }
            }
            else
            {
                img.color = originalColors[btn];
            }
        }

        if (isPinching && !wasPinching && anyButtonHovered) wasPinching = true;
        else if (!isPinching) wasPinching = false;

        // --- 4. TÍNH TOÁN JUST PINCHED VỚI DEBOUNCE CHỐNG DOUBLE CLICK ---
        if (isPinching && !wasPinchingGlobal)
        {
            if (Time.time - lastGlobalPinchTime >= pinchDebounceTime)
            {
                JustPinched = true;
                wasPinchingGlobal = true;
                lastGlobalPinchTime = Time.time;
            }
            else
            {
                JustPinched = false;
            }
        }
        else
        {
            JustPinched = false;
            if (!isPinching) wasPinchingGlobal = false;
        }
    }

    private void ProcessHandResult(HandLandmarkerResult result)
    {
        if (result.handLandmarks == null || result.handLandmarks.Count == 0)
        {
            lock (_lock) { isHandVisible = false; }
            return;
        }

        var hand = result.handLandmarks[0];
        var indexTip = hand.landmarks[8];
        var thumbTip = hand.landmarks[4];

        float rawX = indexTip.x;
        float rawY = indexTip.y;
        float rawThumbX = thumbTip.x;
        float rawThumbY = thumbTip.y;

        // Xử lý hoán đổi trục X và Y khi camera bị xoay 90 độ
        if (hoanDoiTrucXY)
        {
            float temp = rawX;
            rawX = rawY;
            rawY = temp;

            float tempT = rawThumbX;
            rawThumbX = rawThumbY;
            rawThumbY = tempT;
        }

        if (latNguocTrucX)
        {
            rawX = 1.0f - rawX;
            rawThumbX = 1.0f - rawThumbX;
        }
        if (latNguocTrucY)
        {
            rawY = 1.0f - rawY;
            rawThumbY = 1.0f - rawThumbY;
        }

        // Tính khoảng cách pinch (dùng tọa độ nguyên bản để tránh sai số khi xoay lật)
        float dx = thumbTip.x - indexTip.x;
        float dy = thumbTip.y - indexTip.y;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);

        lock (_lock)
        {
            targetX = rawX;
            targetY = rawY;
            targetThumbX = rawThumbX;
            targetThumbY = rawThumbY;
            rawDistance = distance;
            isHandVisible = true;
        }
    }
}