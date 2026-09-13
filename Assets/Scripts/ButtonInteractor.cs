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
    public float pinchThreshold = 0.03f;
    public float clickCooldown = 0.5f;

    [Header("Bảng Điều Khiển Trục Tọa Độ")]
    public bool hoanDoiTrucXY = true; // Bật sẵn để sửa lỗi lên thành trái
    public bool latNguocTrucX = false;
    public bool latNguocTrucY = false;

    private Dictionary<RectTransform, Image> buttonImages = new Dictionary<RectTransform, Image>();
    private Dictionary<RectTransform, Color> originalColors = new Dictionary<RectTransform, Color>();

    private float targetX = 0f;
    private float targetY = 0f;
    private bool isHandVisible = false;
    private bool isPinching = false;
    private bool wasPinching = false;
    private float lastClickTime = 0f;
    private object _lock = new object();

    void Start()
    {
        foreach (var btn in interactiveButtons)
        {
            if (btn != null)
            {
                Image img = btn.GetComponent<Image>();
                if (img != null)
                {
                    buttonImages[btn] = img;
                    originalColors[btn] = img.color;
                }
            }
        }
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

        bool anyButtonHovered = false;

        foreach (var btn in interactiveButtons)
        {
            if (btn == null || !buttonImages.ContainsKey(btn)) continue;

            Canvas btnCanvas = btn.GetComponentInParent<Canvas>();
            Camera btnCam = (btnCanvas != null && btnCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? btnCanvas.worldCamera : null;

            bool isHovering = RectTransformUtility.RectangleContainsScreenPoint(btn, screenPos, btnCam);
            Image img = buttonImages[btn];

            if (isHovering)
            {
                anyButtonHovered = true;
                img.color = pinch ? pressColor : hoverColor;

                if (pinch && !wasPinching && Time.time - lastClickTime > clickCooldown)
                {
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