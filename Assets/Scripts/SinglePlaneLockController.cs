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
        if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
        if (planeManager == null) planeManager = FindObjectOfType<ARPlaneManager>();
    }

    /// <summary>
    /// Khóa mặt phẳng mục tiêu: Ẩn visual các mặt phẳng khác và dừng quét mặt phẳng mới
    /// nhưng DUY TRÌ BÁM mặt phẳng hiện tại để triệt tiêu 100% hiện tượng trôi hoặc bay theo camera.
    /// </summary>
    public void SetManualLocked(TrackableId planeId)
    {
        LockedPlaneId = planeId;
        HasLockedPlane = true;

        if (planeManager != null)
        {
            // Yêu cầu ARCore dừng phát hiện mặt phẳng mới nhưng KHÔNG TẮT planeManager
            // để duy trì tracking SLAM bám chặt vào mặt bàn thật
            planeManager.requestedDetectionMode = PlaneDetectionMode.None;

            foreach (var plane in planeManager.trackables)
            {
                if (plane.trackableId != planeId)
                {
                    plane.gameObject.SetActive(false);
                }
                else
                {
                    plane.gameObject.SetActive(true);
                }
            }
        }
        Debug.Log("<color=cyan>[SinglePlaneLockController]</color> Đã khóa thành công bàn mạch và duy trì bám mặt bàn thật.");
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
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            foreach (var plane in planeManager.trackables)
            {
                plane.gameObject.SetActive(true);
            }
        }
        Debug.Log("<color=cyan>[SinglePlaneLockController]</color> Đã mở khóa mặt phẳng và kích hoạt lại quét AR.");
    }
}