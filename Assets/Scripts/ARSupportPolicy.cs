using UnityEngine;

/// <summary>Bounded decisions on already measured surfaces, never an estimated floor.</summary>
public static class ARSupportPolicy
{
    public static bool PreferMeasuredDepth(Pose plane, Pose depth)
    {
        return IsFinite(plane.position) && IsFinite(depth.position) &&
            Mathf.Abs(plane.position.y - depth.position.y) > 0.025f;
    }

    private static bool IsFinite(Vector3 p) =>
        !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z) &&
        !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);

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
