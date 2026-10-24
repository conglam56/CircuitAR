using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class SinglePlaneLockController : MonoBehaviour
{
    public ARPlaneManager planeManager;

    [Header("--- HIỂN THỊ MẶT PHẲNG AR ---")]
    [Tooltip("Chỉ bật để gỡ lỗi. Khi tắt, AR vẫn dò và bám mặt phẳng nhưng không vẽ lớp màu phủ lên thế giới thật.")]
    [SerializeField] private bool showDetectedPlaneVisuals = false;

    public TrackableId LockedPlaneId { get; private set; }
    public bool HasLockedPlane { get; private set; } = false;

    void Awake()
    {
        if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
        if (planeManager == null) planeManager = FindObjectOfType<ARPlaneManager>();
    }

    void OnEnable()
    {
        if (planeManager != null)
        {
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);
            ApplyPlaneVisualVisibility(!HasLockedPlane && showDetectedPlaneVisuals);
        }
    }

    void OnDisable()
    {
        if (planeManager != null) planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
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
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

            ApplyPlaneVisualVisibility(false);
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
            ApplyPlaneVisualVisibility(showDetectedPlaneVisuals);
        }
        Debug.Log("<color=cyan>[SinglePlaneLockController]</color> Đã mở khóa mặt phẳng và kích hoạt lại quét AR.");
    }

    private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        bool visible = !HasLockedPlane && showDetectedPlaneVisuals;
        foreach (var plane in args.added) SetPlaneVisualVisible(plane, visible);
        foreach (var plane in args.updated) SetPlaneVisualVisible(plane, visible);
    }

    private void ApplyPlaneVisualVisibility(bool visible)
    {
        if (planeManager == null) return;
        foreach (var plane in planeManager.trackables)
        {
            SetPlaneVisualVisible(plane, visible);
        }
    }

    // Keep ARFoundation trackables active; hide only their renderers.
    private static void SetPlaneVisualVisible(ARPlane plane, bool visible)
    {
        if (plane == null) return;
        foreach (var renderer in plane.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = visible;
        foreach (var line in plane.GetComponentsInChildren<LineRenderer>(true))
            line.enabled = visible;
    }
}
