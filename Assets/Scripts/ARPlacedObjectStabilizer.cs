using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Filter anchor jitter without delaying authored movement. Large SLAM corrections
/// and tracking recovery snap immediately; tracking loss preserves the last displayed pose.
/// </summary>
[DefaultExecutionOrder(100)]
public class ARPlacedObjectStabilizer : MonoBehaviour
{
    public bool SuspendFiltering { get; set; }
    // Contact verification must compare authored coordinates, not the temporary
    // local offset that cancels native-anchor jitter or lost tracking.
    public Vector3 AuthoredContactLocalPosition => bound && transform.parent == boundParent &&
        (transform.localPosition - expectedLocalPosition).sqrMagnitude < 0.00000001f &&
        Quaternion.Angle(transform.localRotation, expectedLocalRotation) < 0.01f
            ? authoredLocalPosition : transform.localPosition;
    private TapToPlaceController owner;
    private ARAnchor anchor;
    private Transform boundParent;
    private Vector3 authoredLocalPosition, expectedLocalPosition;
    private Quaternion authoredLocalRotation, expectedLocalRotation;
    private bool bound, needsRecoverySnap;
    private Pose lastPresented;
    private bool hasPresented;
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
        lastPresented = new Pose(transform.position, transform.rotation);
        hasPresented = true;
    }

    private void OnEnable() { bound = false; hasPresented = false; }

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
            (owner != null && owner.IsObjectBeingDragged(gameObject)))
        {
            // Drag, rotation, undo/redo and reparenting author a NEW binding.
            // The filter must not undo any of these user operations.
            CaptureBinding();
        }
        if (!tracking || !bound)
        {
            if (bound && hasPresented)
            {
                // A Limited/lost anchor may still move its Transform. Counteract that
                // inherited motion without replacing the authored local binding.
                transform.SetPositionAndRotation(lastPresented.position, lastPresented.rotation);
                expectedLocalPosition = transform.localPosition;
                expectedLocalRotation = transform.localRotation;
            }
            needsRecoverySnap = true;
            return;
        }
        Pose target = new Pose(boundParent.TransformPoint(authoredLocalPosition),
            boundParent.rotation * authoredLocalRotation);
        Pose stable = filter.Apply(target, needsRecoverySnap, Time.unscaledDeltaTime);
        lastPresented = stable;
        hasPresented = true;
        needsRecoverySnap = false;
        transform.SetPositionAndRotation(stable.position, stable.rotation);
        expectedLocalPosition = transform.localPosition;
        expectedLocalRotation = transform.localRotation;
    }
}
