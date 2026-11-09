using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps authored component footprints out of the placement frame loop. Geometry is
/// stored in root-local space; root motion, rotation and scale are applied at query
/// time. Call Invalidate after changing child transforms, meshes or collider shapes.
/// </summary>
public sealed class ARPlacementGeometryCache
{
    private struct LocalFootprint
    {
        public Vector2 center;
        public Vector2 halfExtents;
    }

    private readonly Dictionary<GameObject, LocalFootprint> footprints =
        new Dictionary<GameObject, LocalFootprint>();
    private readonly List<BoxCollider> boxes = new List<BoxCollider>();
    private readonly List<Renderer> renderers = new List<Renderer>();

    public void Invalidate(GameObject obj)
    {
        if (!ReferenceEquals(obj, null)) footprints.Remove(obj);
    }

    public void Remove(GameObject obj) => Invalidate(obj);

    public void Clear()
    {
        footprints.Clear();
        boxes.Clear();
        renderers.Clear();
    }

    public bool TryGetFootprint(GameObject obj, Vector3 worldPosition, Quaternion worldRotation,
        out TapToPlaceController.FootprintOBB2D footprint, float margin = 0f)
    {
        footprint = default;
        if (obj == null) return false;
        if (!footprints.TryGetValue(obj, out LocalFootprint local))
        {
            local = Capture(obj);
            footprints.Add(obj, local);
        }

        Vector3 scale = obj.transform.lossyScale;
        Vector3 right3 = worldRotation * Vector3.right;
        Vector3 forward3 = worldRotation * Vector3.forward;
        Vector2 right = new Vector2(right3.x, right3.z);
        Vector2 forward = new Vector2(forward3.x, forward3.z);
        right = right.sqrMagnitude < 0.0001f ? Vector2.right : right.normalized;
        forward = forward.sqrMagnitude < 0.0001f ? Vector2.up : forward.normalized;

        footprint.center = new Vector2(worldPosition.x, worldPosition.z) +
            right * (local.center.x * scale.x) + forward * (local.center.y * scale.z);
        footprint.halfExtents = new Vector2(
            Mathf.Max(0f, Mathf.Clamp(local.halfExtents.x * Mathf.Abs(scale.x), 0.02f, 0.18f) + margin),
            Mathf.Max(0f, Mathf.Clamp(local.halfExtents.y * Mathf.Abs(scale.z), 0.02f, 0.18f) + margin));
        footprint.axis1 = right;
        footprint.axis2 = forward;
        // SAT below projects the center and half-extents directly. No corner array
        // is allocated or borrowed from another query, so results are value-safe.
        footprint.corners = null;
        return true;
    }

    /// <summary>Allocation-free SAT for the same four axes as the corner implementation.</summary>
    public static bool Overlaps(TapToPlaceController.FootprintOBB2D a,
        TapToPlaceController.FootprintOBB2D b, out int separatingAxisIndex)
    {
        separatingAxisIndex = -1;
        Vector2 delta = b.center - a.center;
        for (int i = 0; i < 4; i++)
        {
            Vector2 axis = i == 0 ? a.axis1 : i == 1 ? a.axis2 : i == 2 ? b.axis1 : b.axis2;
            if (axis.sqrMagnitude < 0.0001f) continue;
            float radiusA = a.halfExtents.x * Mathf.Abs(Vector2.Dot(a.axis1, axis)) +
                a.halfExtents.y * Mathf.Abs(Vector2.Dot(a.axis2, axis));
            float radiusB = b.halfExtents.x * Mathf.Abs(Vector2.Dot(b.axis1, axis)) +
                b.halfExtents.y * Mathf.Abs(Vector2.Dot(b.axis2, axis));
            if (Mathf.Abs(Vector2.Dot(delta, axis)) > radiusA + radiusB)
            {
                separatingAxisIndex = i;
                return false;
            }
        }
        return true;
    }

    private LocalFootprint Capture(GameObject obj)
    {
        Transform root = obj.transform;
        var fallback = new LocalFootprint
        {
            center = Vector2.zero,
            halfExtents = new Vector2(0.045f, 0.045f)
        };
        boxes.Clear();
        obj.GetComponentsInChildren(false, boxes);
        foreach (BoxCollider box in boxes)
        {
            if (box == null || box.CompareTag("Terminal")) continue;
            Bounds bounds = TransformBounds(box.center, box.size * 0.5f,
                root.worldToLocalMatrix * box.transform.localToWorldMatrix);
            boxes.Clear();
            return FromBounds(bounds, fallback);
        }
        boxes.Clear();

        // Preserve the body-first fallback: only geometry within 3.5 cm of the
        // lowest renderer contributes. Transform local bounds instead of world
        // AABBs so rotating the root cannot inflate and permanently cache its size.
        renderers.Clear();
        obj.GetComponentsInChildren(false, renderers);
        float lowest = float.PositiveInfinity;
        float rootYScale = Mathf.Max(0.0001f, Mathf.Abs(root.lossyScale.y));
        Matrix4x4 worldToRoot = root.worldToLocalMatrix;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer is LineRenderer) continue;
            Bounds bounds = TransformBounds(renderer.localBounds.center, renderer.localBounds.extents,
                worldToRoot * renderer.transform.localToWorldMatrix);
            lowest = Mathf.Min(lowest, bounds.min.y);
        }
        bool found = false;
        Bounds combined = default;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer is LineRenderer) continue;
            Bounds bounds = TransformBounds(renderer.localBounds.center, renderer.localBounds.extents,
                worldToRoot * renderer.transform.localToWorldMatrix);
            if ((bounds.min.y - lowest) * rootYScale > 0.035f) continue;
            if (!found) combined = bounds;
            else combined.Encapsulate(bounds);
            found = true;
        }
        renderers.Clear();
        return found ? FromBounds(combined, fallback) : fallback;
    }

    private static LocalFootprint FromBounds(Bounds bounds, LocalFootprint fallback)
    {
        if (bounds.size.x <= 0f || bounds.size.z <= 0f) return fallback;
        return new LocalFootprint
        {
            center = new Vector2(bounds.center.x, bounds.center.z),
            halfExtents = new Vector2(bounds.extents.x, bounds.extents.z)
        };
    }

    private static Bounds TransformBounds(Vector3 center, Vector3 extents, Matrix4x4 matrix)
    {
        Vector3 x = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
        Vector3 y = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
        Vector3 z = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
        Vector3 transformedExtents = new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(matrix.MultiplyPoint3x4(center), transformedExtents * 2f);
    }
}
