using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlane))]
public class RectanglePlaneVisualizer : MonoBehaviour
{
    public Vector2 minSize = new Vector2(0.6f, 0.6f);

    private ARPlane _plane;
    private Vector2 currentSize;
    private Transform visualChild;
    private bool isRotationLocked = false; // Cờ đánh dấu đã khóa góc xoay ban đầu chưa

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

        // 1. Chỉ hiển thị khi mặt phẳng đạt độ tin cậy và kích thước đủ lớn (> 20cm)
        if (_plane.trackingState != UnityEngine.XR.ARSubsystems.TrackingState.Tracking ||
            _plane.size.x < 0.4f || _plane.size.y < 0.4f)
        {
            if (visualChild.gameObject.activeSelf) visualChild.gameObject.SetActive(false);
            return;
        }
        else
        {
            if (!visualChild.gameObject.activeSelf) visualChild.gameObject.SetActive(true);
        }

        // 2. Khóa góc xoay ban đầu cố định trong thế giới thực
        if (!isRotationLocked && Camera.main != null)
        {
            float initialCameraYaw = Camera.main.transform.eulerAngles.y;
            visualChild.rotation = Quaternion.Euler(90f, initialCameraYaw, 0f);
            isRotationLocked = true;
        }

        // 3. CẢI TIẾN: Đồng bộ vị trí của lưới mượt mà theo tâm thực tế của ARPlane (chống giật)
        visualChild.position = Vector3.Lerp(visualChild.position, _plane.center, Time.deltaTime * 5f);
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
            visualChild.localScale = new Vector3(currentSize.x, currentSize.y, 1f);
        }
    }
}