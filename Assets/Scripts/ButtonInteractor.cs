using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using Mediapipe.Tasks.Vision.HandLandmarker;

public class ButtonInteractor : MonoBehaviour
{
    [Header("Tham chiếu Hệ thống")]
    public HandLandmarkerRunner handRunner;
    public RectTransform videoPanel;
    public RectTransform debugPoint;

    [Header("Kết nối Logic Game")]
    public MenuHUDController menuHUD; // <-- Thêm biến này để kết nối với HUD

    [Header("Danh sách Nút tương tác")]
    public List<RectTransform> interactiveButtons;

    [Header("Cấu hình màu sắc")]
    public Color hoverColor = new Color(0.7f, 0.9f, 1f);
    public Color pressColor = Color.green;

    [Header("Cấu hình Pinch & Chống Spam")]
    public float pinchThreshold = 0.03f;
    public float clickCooldown = 0.5f;
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

        if (videoPanel == null) return;

        if (!visible)
        {
            foreach (var btn in interactiveButtons)
            {
                if (btn != null && buttonImages.ContainsKey(btn))
                    buttonImages[btn].color = originalColors[btn];
            }
            if (debugPoint != null) debugPoint.gameObject.SetActive(false);
            return;
        }

        Vector3[] corners = new Vector3[4];
        videoPanel.GetWorldCorners(corners);
        Vector3 bottomEdge = Vector3.Lerp(corners[0], corners[3], x);
        Vector3 topEdge = Vector3.Lerp(corners[1], corners[2], x);
        Vector3 fingerWorldPos = Vector3.Lerp(bottomEdge, topEdge, y);

        if (debugPoint != null)
        {
            debugPoint.gameObject.SetActive(true);
            debugPoint.position = fingerWorldPos;
        }

        Canvas videoCanvas = videoPanel.GetComponentInParent<Canvas>();
        Camera videoCam = (videoCanvas != null && videoCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? videoCanvas.worldCamera : null;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(videoCam, fingerWorldPos);

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
                    // <-- GỌI SANG MENU HUD ĐỂ LƯU LINH KIỆN VÀO BỘ NHỚ
                    if (menuHUD != null)
                    {
                        menuHUD.ReceiveClick(btn);
                    }

                    lastClickTime = Time.time;
                }
            }
            else
            {
                img.color = originalColors[btn];
            }
        }

        if (pinch && !wasPinching && anyButtonHovered)
        {
            wasPinching = true;
        }
        else if (!pinch)
        {
            wasPinching = false;
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

        float outX = latNguocTrucX ? (1.0f - indexTip.x) : indexTip.x;
        float outY = latNguocTrucY ? indexTip.y : (1.0f - indexTip.y);

        float dx = thumbTip.x - indexTip.x;
        float dy = thumbTip.y - indexTip.y;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);

        lock (_lock)
        {
            targetX = outX;
            targetY = outY;
            isPinching = (distance < pinchThreshold);
            isHandVisible = true;
        }
    }
}