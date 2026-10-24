using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Suppress only sub-2mm/sub-0.3degree anchor noise. Larger SLAM corrections and
/// tracking recovery snap immediately; never lerp a world pose behind the camera.
/// </summary>
[DefaultExecutionOrder(100)]
public class ARPlacedObjectStabilizer : MonoBehaviour
{
    public bool SuspendFiltering { get; set; }
    private TapToPlaceController owner;
    private ARAnchor anchor;
    private Transform boundParent;
    private Vector3 authoredLocalPosition, expectedLocalPosition;
    private Quaternion authoredLocalRotation, expectedLocalRotation;
    private bool bound, needsRecoverySnap;
    private readonly ARSupportPolicy.PoseDeadband filter = new ARSupportPolicy.PoseDeadband();

    public void Bind(TapToPlaceController controller, ARAnchor nativeAnchor)
    {
        owner = controller;
        anchor = nativeAnchor;
        CaptureBinding();
    }

    private void CaptureBinding()
    {
        boundParent = transform.parent;
        authoredLocalPosition = expectedLocalPosition = transform.localPosition;
        authoredLocalRotation = expectedLocalRotation = transform.localRotation;
        filter.Reset();
        needsRecoverySnap = true;
        bound = boundParent != null;
    }

    private void OnEnable() { bound = false; }

    private void LateUpdate()
    {
        if (transform.parent != boundParent)
            anchor = GetComponentInParent<ARAnchor>();
        bool tracking = ARSession.state == ARSessionState.SessionTracking &&
            anchor != null && !anchor.pending && anchor.trackingState == TrackingState.Tracking;
        UpdatePresentation(tracking);
    }

    private void UpdatePresentation(bool tracking)
    {
        if (SuspendFiltering)
        {
            CaptureBinding();
            return;
        }
        if (!bound || transform.parent != boundParent ||
            (transform.localPosition - expectedLocalPosition).sqrMagnitude > 0.00000001f ||
            Quaternion.Angle(transform.localRotation, expectedLocalRotation) > 0.01f ||
            (owner != null && owner.IsDragging && owner.DraggedObject == gameObject))
        {
            // Drag, rotation, undo/redo and reparenting author a NEW binding.
            // The filter must not undo any of these user operations.
            CaptureBinding();
        }
        if (!tracking || !bound)
        {
            needsRecoverySnap = true;
            return;
        }
        Pose target = new Pose(boundParent.TransformPoint(authoredLocalPosition),
            boundParent.rotation * authoredLocalRotation);
        Pose stable = filter.Apply(target, needsRecoverySnap);
        needsRecoverySnap = false;
        transform.SetPositionAndRotation(stable.position, stable.rotation);
        expectedLocalPosition = transform.localPosition;
        expectedLocalRotation = transform.localRotation;
    }
}
