using UnityEngine;

public class MenuHUDController : MonoBehaviour
{
    [Header("Tham chiếu")]
    public PinchDetector pinchDetector;

    [Header("Các nút linh kiện (kéo 4 nút vào đây theo đúng thứ tự)")]
    public RectTransform[] menuButtons;
    public string[] componentNames; // ví dụ: "Pin", "Day", "CongTac", "BongDen" - viết không dấu cho đơn giản

    // Linh kiện người dùng vừa chọn từ menu — script Tap-to-place ở Phần 2 sẽ đọc biến này
    public string SelectedComponent { get; private set; }
    public bool HasSelection { get; private set; }

    void Update()
    {
        if (pinchDetector == null) return;

        // Chỉ xử lý đúng khoảnh khắc pinch VỪA xảy ra, không xử lý liên tục khi giữ pinch
        if (pinchDetector.JustPinched)
        {
            Vector2 cursorPos = pinchDetector.CurrentCursorScreenPos;

            for (int i = 0; i < menuButtons.Length; i++)
            {
                if (menuButtons[i] == null) continue;

                bool isInside = RectTransformUtility.RectangleContainsScreenPoint(
                    menuButtons[i], cursorPos, null);

                if (isInside)
                {
                    SelectedComponent = componentNames[i];
                    HasSelection = true;
                    Debug.Log("Da chon: " + SelectedComponent);
                    break; // chỉ chọn 1 nút mỗi lần pinch
                }
            }
        }
    }

    // Gọi hàm này sau khi đã đặt xong linh kiện, để reset lại lựa chọn
    public void ClearSelection()
    {
        HasSelection = false;
        SelectedComponent = null;
    }
}