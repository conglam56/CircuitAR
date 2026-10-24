using UnityEngine;

/// <summary>Bounded decisions on already measured surfaces, never an estimated floor.</summary>
public static class ARSupportPolicy
{
    public static bool CanShareAnchor(UnityEngine.XR.ARSubsystems.TrackingState state, bool pending,
        Vector3 anchorPosition, Vector3 existingContact, Vector3 measuredContact)
    {
        return state == UnityEngine.XR.ARSubsystems.TrackingState.Tracking && !pending &&
            IsFinite(anchorPosition) && IsFinite(existingContact) && IsFinite(measuredContact) &&
            Mathf.Abs(anchorPosition.y - measuredContact.y) <= 0.025f &&
            Mathf.Abs(existingContact.y - measuredContact.y) <= 0.025f &&
            (existingContact - measuredContact).sqrMagnitude <= 1f &&
            (anchorPosition - measuredContact).sqrMagnitude <= 1f;
    }

    public static bool PreferMeasuredDepth(Pose plane, Pose depth)
    {
        return IsFinite(plane.position) && IsFinite(depth.position) &&
            Mathf.Abs(plane.position.y - depth.position.y) > 0.025f;
    }

    private static bool IsFinite(Vector3 p) =>
        !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
        !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);

    // A plane refit may lower an early contact substantially. It must never
    // automatically lift a floor object onto a newly merged tabletop.
    public static bool CanCorrectContactHeight(float current, float measured) =>
        IsFinite(new Vector3(current, measured, 0f)) &&
        Mathf.Abs(measured - current) > 0.012f &&
        measured - current >= -0.6f && measured - current <= 0.03f;

    public static bool TryProjectContactVertically(Vector3 contact, Vector3 planePoint,
        Vector3 normal, out Vector3 projected)
    {
        projected = default;
        if (!IsFinite(contact) || !IsFinite(planePoint) || !IsFinite(normal) ||
            normal.sqrMagnitude < 0.9f || Vector3.Dot(normal.normalized, Vector3.up) < 0.9659258f)
            return false;
        projected = contact;
        projected.y = planePoint.y - (normal.x * (contact.x - planePoint.x) +
            normal.z * (contact.z - planePoint.z)) / normal.y;
        return IsFinite(projected);
    }

    public sealed class ContactConfirmation
    {
        private bool pending;
        private float firstHeight, startedAt, lastAt;
        private int observations;
        public void Reset() { pending = false; observations = 0; }

        // Called only for separate, finite plane + depth observations. Repeated
        // reads of a cached ray do not qualify as additional evidence.
        public bool Observe(float currentHeight, float measuredHeight, float now)
        {
            if (!CanCorrectContactHeight(currentHeight, measuredHeight) ||
                float.IsNaN(now) || float.IsInfinity(now))
            {
                Reset();
                return false;
            }
            if (!pending || now < lastAt || now - lastAt > 2f ||
                Mathf.Abs(firstHeight - measuredHeight) > 0.01f)
            {
                pending = true;
                firstHeight = measuredHeight;
                startedAt = lastAt = now;
                observations = 1;
                return false;
            }
            if (now <= lastAt) return false;
            lastAt = now;
            observations++;
            return observations >= 3 && now - startedAt >= 0.6f;
        }
    }

    public sealed class LevelTransition
    {
        public const float HoldDuration = 0.35f;
        public bool IsPending { get; private set; }
        public float PendingHeight => pendingPoint.y;
        private bool initialized;
        private float acceptedHeight;
        private float startedAt;
        private Vector3 pendingPoint;
        private Vector2 pendingPointer;

        public void Begin(float height)
        {
            initialized = true;
            acceptedHeight = height;
            CancelPending();
        }

        public bool Accept(Vector3 measuredContact, Vector2 pointer, float now)
        {
            if (!IsFinite(measuredContact) || !IsFinite(new Vector3(pointer.x, pointer.y, 0f)) ||
                float.IsNaN(now) || float.IsInfinity(now))
            {
                CancelPending();
                return false;
            }
            if (!initialized) Begin(measuredContact.y);
            if (Mathf.Abs(measuredContact.y - acceptedHeight) <= 0.05f)
            {
                CancelPending();
                return true;
            }
            if (!IsPending || now < startedAt ||
                Mathf.Abs(measuredContact.y - pendingPoint.y) > 0.025f ||
                Vector3.Distance(measuredContact, pendingPoint) > 0.08f ||
                Vector2.Distance(pointer, pendingPointer) > 45f)
            {
                IsPending = true;
                startedAt = now;
                pendingPoint = measuredContact;
                pendingPointer = pointer;
                return false;
            }
            if (now - startedAt < HoldDuration) return false;
            acceptedHeight = measuredContact.y;
            CancelPending();
            return true;
        }

        public void CancelPending() { IsPending = false; }
    }

    public sealed class PoseDeadband
    {
        private bool initialized;
        private Pose presented;
        public void Reset() { initialized = false; }
        public Pose Apply(Pose tracked, bool forceSnap)
        {
            if (!initialized || forceSnap || Vector3.Distance(tracked.position, presented.position) > 0.002f ||
                Quaternion.Angle(tracked.rotation, presented.rotation) > 0.3f)
            {
                presented = tracked;
                initialized = true;
            }
            return presented;
        }
    }
}
