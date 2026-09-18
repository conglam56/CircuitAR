using UnityEngine;
using TMPro; // Sử dụng TextMeshPro (hoặc UnityEngine.UI nếu dùng Text thường)
using UnityEngine.UI;

public class ARScanStatusUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Kéo Text thông báo trên màn hình vào đây")]
    public TextMeshProUGUI statusText;

    [Tooltip("Khung viền hoặc biểu tượng ngắm ở giữa màn hình (tùy chọn)")]
    public Image crosshairIndicator;

    [Header("Màu sắc trạng thái")]
    public Color scanningColor = Color.yellow;
    public Color lockedColor = Color.green;

    public static ARScanStatusUI Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        SetStatus("Đang xác định mặt phẳng...", scanningColor);
    }

    public void SetStatus(string message, Color color)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = color;
        }

        if (crosshairIndicator != null)
        {
            crosshairIndicator.color = color;
        }
    }

    public void OnPlaneLocked(Vector3 planePos)
    {
        SetStatus("ĐÃ KHÓA THÀNH CÔNG!", lockedColor);

        // Tự động ẩn thông báo sau 3 giây để màn hình thoáng đãng
        Invoke(nameof(HideStatus), 3.5f);
    }

    private void HideStatus()
    {
        if (statusText != null) statusText.gameObject.SetActive(false);
        if (crosshairIndicator != null) crosshairIndicator.gameObject.SetActive(false);
    }
}