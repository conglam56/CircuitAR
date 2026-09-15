using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class SinglePlaneLockController : MonoBehaviour
{
    [Header("Tham chiếu")]
    public ARPlaneManager planeManager;

    [Header("Cấu hình")]
    [Tooltip("Diện tích tối thiểu (m2) để chấp nhận 1 mặt phẳng làm mặt phẳng cố định")]
    public float minPlaneArea = 0.1f;

    public TrackableId LockedPlaneId { get; private set; }
    public bool HasLockedPlane { get; private set; } = false;

    void OnEnable()
    {
        if (planeManager != null)
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);
    }

    void OnDisable()
    {
        if (planeManager != null)
            planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
    }

    private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        if (HasLockedPlane) return; // đã khóa rồi, không xử lý thêm

        foreach (var plane in args.added)
        {
            float area = plane.size.x * plane.size.y;
            if (area >= minPlaneArea)
            {
                LockToPlane(plane);
                return;
            }
        }

        // Cũng kiểm tra các mặt phẳng đã tồn tại (phòng trường hợp updated mới đạt đủ diện tích)
        foreach (var plane in args.updated)
        {
            float area = plane.size.x * plane.size.y;
            if (area >= minPlaneArea)
            {
                LockToPlane(plane);
                return;
            }
        }
    }

    private void LockToPlane(ARPlane chosenPlane)
    {
        LockedPlaneId = chosenPlane.trackableId;
        HasLockedPlane = true;

        Debug.Log("Da khoa mat phang co dinh: " + LockedPlaneId + ", dien tich = " + (chosenPlane.size.x * chosenPlane.size.y));

        // Ẩn/xóa mọi mặt phẳng khác không phải mặt phẳng đã chọn
        foreach (var plane in planeManager.trackables)
        {
            if (plane.trackableId != LockedPlaneId)
            {
                plane.gameObject.SetActive(false);
            }
        }

        // Dừng hẳn việc quét/phát hiện mặt phẳng mới -> không còn chồng lớp nữa
        planeManager.enabled = false;
    }

    // Gọi hàm này nếu người dùng muốn quét lại từ đầu (ví dụ có nút Reset trong UI)
    public void ResetLock()
    {
        HasLockedPlane = false;
        planeManager.enabled = true;

        foreach (var plane in planeManager.trackables)
        {
            plane.gameObject.SetActive(true);
        }
    }
}