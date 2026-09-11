using UnityEngine;

public class MenuHUDController : MonoBehaviour
{
    [Header("Các nút linh kiện (kéo 4 nút vào đây theo đúng thứ tự)")]
    public RectTransform[] menuButtons;
    public string[] componentNames; // ví dụ: "Pin", "Day", "CongTac", "BongDen"

    // Linh kiện người dùng vừa chọn từ menu — script Tap-to-place sẽ đọc biến này
    public string SelectedComponent { get; private set; }
    public bool HasSelection { get; private set; }

    // Hàm này sẽ được ButtonInteractor gọi thẳng sang khi có nút bị bấm
    public void ReceiveClick(RectTransform clickedButton)
    {
        for (int i = 0; i < menuButtons.Length; i++)
        {
            if (menuButtons[i] == clickedButton)
            {
                SelectedComponent = componentNames[i];
                HasSelection = true;
                Debug.Log("Da chon: " + SelectedComponent);
                break; // chỉ chọn 1 nút mỗi lần pinch
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