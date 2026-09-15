using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlane))]
public class RectanglePlaneVisualizer : MonoBehaviour
{
    public Vector2 minSize = new Vector2(0.6f, 0.6f);

    private ARPlane _plane;
    private Vector2 currentSize;
    private Transform visualChild;

    void Awake()
    {
        _plane = GetComponent<ARPlane>();
        currentSize = minSize;

        // Tìm object con (cái hình Quad hiển thị lưới caro) để điều khiển độc lập
        if (transform.childCount > 0)
        {
            visualChild = transform.GetChild(0);
        }
    }

    void OnEnable()
    {
        _plane.boundaryChanged += OnBoundaryChanged;
        ApplyScale();
    }

    void OnDisable()
    {
        _plane.boundaryChanged -= OnBoundaryChanged;
    }

    void Update()
    {
        if (visualChild == null) return;

        // 1. DIỆT MẶT PHẲNG MINI: Nếu diện tích quét được dưới 20cm, tàng hình luôn tấm lưới
        if (_plane.size.x < 0.2f || _plane.size.y < 0.2f)
        {
            if (visualChild.gameObject.activeSelf) visualChild.gameObject.SetActive(false);
            return;
        }
        else
        {
            if (!visualChild.gameObject.activeSelf) visualChild.gameObject.SetActive(true);
        }

        // 2. ÉP LƯỚI SONG SONG VỚI ĐIỆN THOẠI VÀ CHỐNG XÊ DỊCH CHÉO
        // Lấy góc quay trái/phải (trục Y) của Camera hiện tại
        float cameraYaw = Camera.main.transform.eulerAngles.y;

        // Ghi đè góc xoay của lưới: Ép nó nằm úp (X=90) và luôn song song với điện thoại (Y=cameraYaw)
        visualChild.rotation = Quaternion.Euler(90f, cameraYaw, 0f);
    }

    private void OnBoundaryChanged(ARPlaneBoundaryChangedEventArgs args)
    {
        if (_plane.size.x > currentSize.x) currentSize.x = _plane.size.x;
        if (_plane.size.y > currentSize.y) currentSize.y = _plane.size.y;
        ApplyScale();
    }

    public void ExpandToIncludePoint(Vector3 worldPoint)
    {
        // Chuyển tọa độ thế giới sang tọa độ cục bộ của mặt phẳng
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        float distanceX = Mathf.Abs(localPoint.x);
        float distanceZ = Mathf.Abs(localPoint.z);

        float requiredWidth = distanceX * 2.0f + 0.1f;
        float requiredLength = distanceZ * 2.0f + 0.1f;

        bool isExpanded = false;
        if (requiredWidth > currentSize.x) { currentSize.x = requiredWidth; isExpanded = true; }
        if (requiredLength > currentSize.y) { currentSize.y = requiredLength; isExpanded = true; }

        if (isExpanded) ApplyScale();
    }

    private void ApplyScale()
    {
        if (visualChild != null)
        {
            // Áp dụng độ giãn nở trực tiếp lên thằng con (Visual) thay vì thằng cha
            // Điều này giúp mặt phẳng giữ nguyên hình chữ nhật vuông vức, không bị méo lệch khi ARCore cập nhật
            visualChild.localScale = new Vector3(currentSize.x, currentSize.y, 1f);
        }
    }
}