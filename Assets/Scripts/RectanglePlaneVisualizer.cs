using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARPlane))]
public class RectanglePlaneVisualizer : MonoBehaviour
{
    private ARPlane _plane;
    private Transform visualChild;

    void Awake()
    {
        _plane = GetComponent<ARPlane>();
        if (transform.childCount > 0) visualChild = transform.GetChild(0);

        // Tự động tắt mọi LineRenderer nếu vô tình còn sót lại
        LineRenderer lr = GetComponent<LineRenderer>();
        if (lr != null) lr.enabled = false;
    }

    private static SinglePlaneLockController s_LockCtrl;

    void Start()
    {
        if (s_LockCtrl == null)
        {
            s_LockCtrl = FindFirstObjectByType<SinglePlaneLockController>();
        }
    }

    void Update()
    {
        if (visualChild == null)
        {
            if (transform.childCount > 0) visualChild = transform.GetChild(0);
            if (visualChild == null) return;
        }

        // Tắt tấm mặt phẳng khi đang trong giai đoạn ngắm chọn 2 điểm
        bool isLocked = (s_LockCtrl != null && s_LockCtrl.HasLockedPlane && s_LockCtrl.LockedPlaneId == _plane.trackableId);

        if (!isLocked)
        {
            if (visualChild.gameObject.activeSelf) visualChild.gameObject.SetActive(false);
            return;
        }

        if (!visualChild.gameObject.activeSelf) visualChild.gameObject.SetActive(true);

        // Khi đã khóa: giữ cố định tại tâm
        visualChild.position = _plane.center;
    }

    public void ExpandToIncludePoint(Vector3 worldPoint) { }
}