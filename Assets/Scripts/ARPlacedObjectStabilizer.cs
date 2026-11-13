using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Filter anchor jitter without delaying authored movement. Tracking recovery is
/// confirmed against measured support; tracking loss preserves the last displayed pose.
/// </summary>
[DefaultExecutionOrder(100)]
public class ARPlacedObjectStabilizer : MonoBehaviour
{
    public bool SuspendFiltering { get; set; }
    // Contact verification must compare authored coordinates, not the temporary
    // local offset that cancels native-anchor jitter or lost tracking.
    private bool BindingMatches => bound && transform.parent == boundParent &&
        (transform.localPosition - expectedLocalPosition).sqrMagnitude < 0.00000001f &&
        Quaternion.Angle(transform.localRotation, expectedLocalRotation) < 0.01f;
    public Vector3 AuthoredContactLocalPosition => BindingMatches ? authoredLocalPosition : transform.localPosition;
    public Quaternion AuthoredContactLocalRotation => BindingMatches ? authoredLocalRotation : transform.localRotation;
    public bool IsRecovering { get; private set; }
    internal bool HasUnverifiedAnchorJump => anchor != null && hasPresented && BindingMatches &&
        IsDiscontinuous(new Pose(boundParent.TransformPoint(authoredLocalPosition), boundParent.rotation * authoredLocalRotation));
    internal Pose RetainedPose => hasPresented && BindingMatches && (owner == null || !owner.IsObjectBeingDragged(gameObject))
        ? lastPresented : new Pose(transform.position, transform.rotation);
    private TapToPlaceController owner;
    private ARAnchor anchor;
    private Transform boundParent;
    private Vector3 authoredLocalPosition, expectedLocalPosition;
    private Quaternion authoredLocalRotation, expectedLocalRotation;
    private bool bound, needsRecoverySnap;
    private Pose lastPresented;
    private bool hasPresented;
    private readonly ARSupportPolicy.RecoveryConfirmation recovery = new();
    private readonly ARSupportPolicy.PoseDeadband filter = new ARSupportPolicy.PoseDeadband();

    public void Bind(TapToPlaceController controller, ARAnchor nativeAnchor)
    {
        owner = controller;
        anchor = nativeAnchor;
        if (anchor != null)
        {
            anchor.destroyOnRemoval = false; // Content/undo history must survive provider removal.
            owner?.ObservePlacementAnchors();
        }
        CaptureBinding();
    }

    private void CaptureBinding()
    {
        boundParent = transform.parent;
        authoredLocalPosition = expectedLocalPosition = transform.localPosition;
        authoredLocalRotation = expectedLocalRotation = transform.localRotation;
        filter.Reset();
        recovery.Reset();
        IsRecovering = false;
        needsRecoverySnap = true;
        bound = boundParent != null;
        lastPresented = new Pose(transform.position, transform.rotation);
        hasPresented = true;
    }

    private void OnEnable() { bound = false; hasPresented = false; IsRecovering = false; recovery.Reset(); }

    private void LateUpdate()
    {
        if (transform.parent != boundParent)
            anchor = GetComponentInParent<ARAnchor>();
        if (anchor != null && owner != null && !owner.IsPlacementAnchorRegistered(anchor))
        {
            owner.RetainRemovedPlacementAnchor(anchor);
            return;
        }
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
                FreezePresentation();
                IsRecovering = true;
                recovery.Reset();
            }
            needsRecoverySnap = true;
            return;
        }
        Pose target = new Pose(boundParent.TransformPoint(authoredLocalPosition),
            boundParent.rotation * authoredLocalRotation);
        bool discontinuity = hasPresented && IsDiscontinuous(target);
        if (!IsRecovering && !discontinuity && owner != null &&
            owner.TryGetCommittedPositionReference(gameObject, out Vector3 reference) &&
            (target.position - reference).sqrMagnitude > 0.006f * 0.006f)
        {
            // Small per-frame native drift can accumulate into camera-follow
            // without ever crossing the large-jump threshold. Require the same
            // committed support reference, not just a Tracking flag.
            FreezePresentation();
            return;
        }
        if (IsRecovering || discontinuity)
        {
            IsRecovering = true;
            Pose supported = target;
            bool measured = owner != null && owner.TryConstrainRecoveredContact(gameObject, target, lastPresented, out supported);
            if (measured) target = supported;
            bool small = Vector3.Distance(target.position, lastPresented.position) <= 0.03f &&
                Mathf.Abs(target.position.y - lastPresented.position.y) <= 0.012f && Quaternion.Angle(target.rotation, lastPresented.rotation) <= 3f;
            if ((!measured && !small) || !recovery.Observe(target, Time.unscaledTime))
            {
                if (!measured && !small) recovery.Reset();
                FreezePresentation();
                return;
            }
            // A validated support may compensate an incorrect native height.
            // Rebind that corrected contact rather than applying the bad offset again next frame.
            transform.SetPositionAndRotation(target.position, target.rotation);
            CaptureBinding();
            owner?.RefreshPlacedContactBinding(gameObject);
        }
        Pose stable = filter.Apply(target, needsRecoverySnap, Time.unscaledDeltaTime);
        lastPresented = stable;
        hasPresented = true;
        needsRecoverySnap = false;
        transform.SetPositionAndRotation(stable.position, stable.rotation);
        expectedLocalPosition = transform.localPosition;
        expectedLocalRotation = transform.localRotation;
    }
    private bool IsDiscontinuous(Pose target) => Vector3.Distance(target.position, lastPresented.position) > 0.12f ||
        target.position.y - lastPresented.position.y > 0.035f || Quaternion.Angle(target.rotation, lastPresented.rotation) > 10f;

    private void FreezePresentation()
    {
        transform.SetPositionAndRotation(lastPresented.position, lastPresented.rotation);
        expectedLocalPosition = transform.localPosition;
        expectedLocalRotation = transform.localRotation;
    }

}
