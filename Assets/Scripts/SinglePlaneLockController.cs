using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class SinglePlaneLockController : MonoBehaviour
{
    public ARPlaneManager planeManager;
    public TrackableId LockedPlaneId { get; private set; }
    public bool HasLockedPlane { get; private set; } = false;

    void Awake()
    {
        if (planeManager == null) planeManager = FindObjectOfType<ARPlaneManager>();
    }

    // Hàm này chỉ chạy khi nút 3D TwoPointSpatialCalibrator hoàn thành đặt 2 điểm
    public void SetManualLocked(TrackableId planeId)
    {
        LockedPlaneId = planeId;
        HasLockedPlane = true;

        // Ẩn tất cả các mặt phẳng thừa
        if (planeManager != null)
        {
            foreach (var plane in planeManager.trackables)
            {
                plane.gameObject.SetActive(false);
            }
            // TẮT HOÀN TOÀN ARPlaneManager để dừng tạo mặt phẳng mới
            planeManager.enabled = false;
        }
    }

    /// <summary>
    /// Mở khóa mặt phẳng để bắt đầu quét lại từ đầu
    /// </summary>
    public void UnlockAndRescan()
    {
        LockedPlaneId = TrackableId.invalidId;
        HasLockedPlane = false;

        if (planeManager != null)
        {
            planeManager.enabled = true;
            foreach (var plane in planeManager.trackables)
            {
                plane.gameObject.SetActive(true);
            }
        }
        Debug.Log("<color=cyan>[SinglePlaneLock]</color> Đã mở khóa mặt phẳng và kích hoạt lại quét AR.");
    }
}