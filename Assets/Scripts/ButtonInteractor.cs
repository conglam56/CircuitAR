using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using Mediapipe.Tasks.Vision.HandLandmarker;

public class ButtonInteractor : MonoBehaviour
{
    [Header("KÉO OBJECT 'Screen' VÀO ĐÂY")]
    public RectTransform videoPanel;

    [Header("Kéo Nút Vàng (Debug Point) vào đây")]
    public RectTransform debugPoint;

    [Header("Tham chiếu Hệ thống")]
    public HandLandmarkerRunner handRunner;

    [Header("Kết nối Logic Game")]
    public MenuHUDController menuHUD;

    [Header("Danh sách Nút tương tác")]
    public List<RectTransform> interactiveButtons;

    [Header("Cấu hình màu sắc")]
    public Color hoverColor = new Color(0.7f, 0.9f, 1f);
    public Color pressColor = Color.green;

    [Header("Cấu hình Pinch & Chống Spam")]
    [Range(0.02f, 0.08f)] public float pinchThreshold = 0.042f;
    public float clickCooldown = 0.45f;

    [Header("Bảng Điều Khiển Trục Tọa Độ")]
    public bool hoanDoiTrucXY = true; // Bật sẵn để sửa lỗi lên thành trái
    public bool latNguocTrucX = false;
    public bool latNguocTrucY = false;

    // MỚI THÊM: để các script khác (ví dụ TapToPlaceController) đọc được toạ độ tay
    // và trạng thái pinch, không phụ thuộc việc có đang hover nút menu hay không.
    public Vector2 CurrentScreenPos { get; private set; }
    public bool JustPinched { get; private set; }

    private Dictionary<RectTransform, Image> buttonImages = new Dictionary<RectTransform, Image>();
    private Dictionary<RectTransform, Color> originalColors = new Dictionary<RectTransform, Color>();

    private float targetX = 0f;
    private float targetY = 0f;
    private bool isHandVisible = false;
    public bool isPinching = false;
    private bool wasPinching = false;       // dùng riêng cho logic click nút menu (giữ nguyên như cũ)
    private bool wasPinchingGlobal = false; // MỚI: dùng riêng cho JustPinched, không phụ thuộc hover nút nào
    private float lastClickTime = 0f;
    private object _lock = new object();

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
        float x, y;
        bool visible, pinch;

        lock (_lock)
        {
            x = targetX;
            y = targetY;
            visible = isHandVisible;
            pinch = isPinching;
        }

        if (!visible || videoPanel == null)
        {
            foreach (var btn in interactiveButtons)
            {
                if (btn != null && buttonImages.ContainsKey(btn))
                    buttonImages[btn].color = originalColors[btn];
            }
            if (debugPoint != null) debugPoint.gameObject.SetActive(false);

            JustPinched = false;
            wasPinchingGlobal = false;
            return;
        }

        Vector2 screenPos = Vector2.zero;

        if (debugPoint != null)
        {
            debugPoint.gameObject.SetActive(true);

            if (debugPoint.parent != videoPanel)
            {
                debugPoint.SetParent(videoPanel);
            }

            debugPoint.anchorMin = new Vector2(x, 1.0f - y);
            debugPoint.anchorMax = new Vector2(x, 1.0f - y);
            debugPoint.anchoredPosition = Vector2.zero;

            Canvas videoCanvas = videoPanel.GetComponentInParent<Canvas>();
            Camera uiCam = (videoCanvas != null && videoCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? videoCanvas.worldCamera : null;
            screenPos = RectTransformUtility.WorldToScreenPoint(uiCam, debugPoint.position);
        }

        CurrentScreenPos = screenPos;
        bool anyButtonHovered = false;

        foreach (var btn in interactiveButtons)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy || !buttonImages.ContainsKey(btn)) continue;

            Canvas btnCanvas = btn.GetComponentInParent<Canvas>();
            Camera btnCam = (btnCanvas != null && btnCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? btnCanvas.worldCamera : null;

            // ĐOẠN CẬP NHẬT QUAN TRỌNG: 
            // Nếu menu là 3D (World Space) mà bạn quên chưa gán Event Camera trên Unity, 
            // code sẽ tự động dùng Camera AR chính để tính toán góc nhìn.
            if (btnCam == null && btnCanvas != null && btnCanvas.renderMode == RenderMode.WorldSpace)
            {
                btnCam = Camera.main;
            }

            // Tính toán va chạm giữa ngón tay (2D) và nút bấm (3D)
            bool isHovering = RectTransformUtility.RectangleContainsScreenPoint(btn, screenPos, btnCam);
            Image img = buttonImages[btn];

            if (isHovering)
            {
                anyButtonHovered = true;
                img.color = pinch ? pressColor : hoverColor;

                if (pinch && !wasPinching && Time.time - lastClickTime > clickCooldown)
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

        if (pinch && !wasPinching && anyButtonHovered) wasPinching = true;
        else if (!pinch) wasPinching = false;

        if (pinch && !wasPinchingGlobal)
        {
            JustPinched = true;
            wasPinchingGlobal = true;
        }
        else
        {
            JustPinched = false;
            if (!pinch) wasPinchingGlobal = false;
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

        // Xử lý hoán đổi trục X và Y khi camera bị xoay 90 độ
        if (hoanDoiTrucXY)
        {
            float temp = rawX;
            rawX = rawY;
            rawY = temp;
        }

        if (latNguocTrucX) rawX = 1.0f - rawX;
        if (latNguocTrucY) rawY = 1.0f - rawY;

        // Tính khoảng cách pinch (chỉ dùng tọa độ nguyên bản để tránh sai số khi xoay lật)
        float dx = thumbTip.x - indexTip.x;
        float dy = thumbTip.y - indexTip.y;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);

        lock (_lock)
        {
            targetX = rawX;
            targetY = rawY;
            isPinching = (distance < pinchThreshold);
            isHandVisible = true;
        }
    }
}