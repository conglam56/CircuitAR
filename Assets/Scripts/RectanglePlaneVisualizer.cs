using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARPlane))]
public class RectanglePlaneVisualizer : MonoBehaviour
{
    [Header("--- KÍCH THƯỚC MẶT PHẲNG (METERS) ---")]
    [Tooltip("Kích thước mặt phẳng hình chữ nhật (Ngang x Dọc). Mặc định 1.2m x 0.8m")]
    public Vector2 fixedSize = new Vector2(1.20f, 0.80f);

    private ARPlane _plane;
    private Transform visualChild;

    void Awake()
    {
        _plane = GetComponent<ARPlane>();
        if (transform.childCount > 0) visualChild = transform.GetChild(0);

        // Tự động tắt mọi LineRenderer nếu vô tình còn sót lại
        LineRenderer lr = GetComponent<LineRenderer>();
        if (lr != null) lr.enabled = false;

        // Đảm bảo kích thước không bị nhỏ hơn 1m
        if (fixedSize.x < 0.5f) fixedSize.x = 1.20f;
        if (fixedSize.y < 0.3f) fixedSize.y = 0.80f;
    }

    void Update()
    {
        if (visualChild == null)
        {
            if (transform.childCount > 0) visualChild = transform.GetChild(0);
            if (visualChild == null) return;
        }

        // Ẩn hoàn toàn tấm mặt phẳng màu xanh nhạt cũ để không tạo ra mặt phẳng thứ 2
        // đè bên dưới thảm mạch thật ActiveCircuitBoard và không bị giật/nhảy theo ARCore SLAM
        if (visualChild.gameObject.activeSelf)
        {
            visualChild.gameObject.SetActive(false);
        }
    }

    public void ExpandToIncludePoint(Vector3 worldPoint) { }
}