using UnityEngine;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using Mediapipe.Tasks.Vision.HandLandmarker;

public class PinchDetector : MonoBehaviour
{
    [Header("Tham chiếu MediaPipe")]
    public HandLandmarkerRunner handRunner;

    [Header("Giao diện UI")]
    public RectTransform cursorUI;

    [Header("QUAN TRỌNG: kéo đúng RawImage đang hiển thị hình camera vào đây")]
    public RectTransform videoDisplayRect;

    [Header("Sửa lỗi lệch trục (thử tick/bỏ tick nếu cursor bị lật gương)")]
    public bool latNguocTrucX = true;
    public bool latNguocTrucY = false;

    [Header("Cấu hình Pinch")]
    public float pinchThreshold = 0.03f;

    [Header("Cấu hình độ mượt")]
    [Range(1f, 30f)]
    public float smoothSpeed = 15f;

    // --- Dữ liệu công khai để các script khác (MenuHUDController, TapToPlaceController...) đọc ---
    public Vector2 CurrentCursorScreenPos { get; private set; }
    public bool JustPinched { get; private set; }

    // --- Biến chia sẻ giữa luồng ngầm MediaPipe và luồng chính Unity ---
    private Vector2 targetCursorPos;
    private bool isPinchDetected = false;
    private bool isHandVisible = false;
    private object _lock = new object();
    private bool wasPinching = false;

    // Lưu 4 góc của vùng hiển thị camera (đo ở luồng chính, dùng ở luồng ngầm)
    private Vector2 videoBottomLeft;
    private Vector2 videoTopRight;

    void OnEnable()
    {
        if (handRunner != null)
            handRunner.OnHandResult += ProcessHandResult;
    }

    void OnDisable()
    {
        if (handRunner != null)
            handRunner.OnHandResult -= ProcessHandResult;
    }

    void Update()
    {
        // 1. Đo đúng 4 góc thật của vùng hiển thị camera trên màn hình (luồng chính)
        

        // 2. Lấy dữ liệu an toàn từ luồng ngầm
        Vector2 currentTarget;
        bool pinch;
        bool visible;

        lock (_lock)
        {
            currentTarget = targetCursorPos;
            pinch = isPinchDetected;
            visible = isHandVisible;
        }

        // 3. Di chuyển cursor mượt tới đúng vị trí đã tính
        if (cursorUI != null && visible)
        {
            cursorUI.position = Vector2.Lerp(cursorUI.position, currentTarget, Time.deltaTime * smoothSpeed);
        }

        // 4. Cập nhật vị trí công khai cho script khác đọc
        CurrentCursorScreenPos = cursorUI != null ? (Vector2)cursorUI.position : currentTarget;

        // 5. Bắt sự kiện pinch — JustPinched chỉ true đúng 1 frame ngay lúc vừa chụm tay
        if (pinch && !wasPinching)
        {
            wasPinching = true;
            JustPinched = true;
            Debug.Log("🎯 PINCH DETECTED! - Đã Click!");
        }
        else
        {
            JustPinched = false;
            if (!pinch && wasPinching) wasPinching = false;
        }
    }

    // Chạy trên luồng ngầm của MediaPipe — không được đụng vào UI ở đây
    private void ProcessHandResult(HandLandmarkerResult result)
    {
        if (result.handLandmarks == null || result.handLandmarks.Count == 0)
        {
            lock (_lock) { isHandVisible = false; }
            return;
        }

        var hand = result.handLandmarks[0];
        var thumbTip = hand.landmarks[4];
        var indexTip = hand.landmarks[8];
     
        float nx = latNguocTrucX ? (1.0f - indexTip.x) : indexTip.x;
        float ny = latNguocTrucY ? indexTip.y : (1.0f - indexTip.y);

        float screenX = nx * Screen.width;
        float screenY = ny * Screen.height;

        float dx = thumbTip.x - indexTip.x;
        float dy = thumbTip.y - indexTip.y;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);

        lock (_lock)
        {
            targetCursorPos = new Vector2(screenX, screenY);
            isPinchDetected = (distance < pinchThreshold);
            isHandVisible = true;
        }
    }
}