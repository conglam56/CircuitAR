using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Finds a small, measured horizontal depth patch before ARCore has a plane polygon.
/// This never enables depth compositing and never estimates a floor distance.
/// </summary>
public sealed class MeasuredDepthSurfaceProvider
{
    private const int MinimumSupportSamples = 4;
    private const float HeightTolerance = 0.015f;
    private const float MinimumRange = 0.15f;
    private const float MaximumRange = 5f;
    private const float MaximumPatchSpan = 0.35f;
    private const float MinimumUpDot = 0.9659258f; // cos(15 degrees)
    private static readonly Vector2[] ProbeDirections =
    {
        Vector2.right, new Vector2(0.7071068f, 0.7071068f), Vector2.up,
        new Vector2(-0.7071068f, 0.7071068f), Vector2.left,
        new Vector2(-0.7071068f, -0.7071068f), Vector2.down,
        new Vector2(0.7071068f, -0.7071068f)
    };

    private Camera camera;
    private ARRaycastManager raycasts;
    private AROcclusionManager occlusion;
    private ButtonInteractor handInput;
    private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>(8);
    private readonly List<Pose> samples = new List<Pose>(8);
    private readonly PatchStability stability = new PatchStability();
    private readonly FinitePatchCache finitePatch = new FinitePatchCache();
    private int lastQueryFrame = -1;
    private float nextQueryAt;
    private int querySequence;
    private Supported reportedSupport = Supported.Unknown;
    private bool hasReportedSupport;
    private bool reportedMissingManager;

    // Placement probes require a hand by default. A separate, idle contact
    // verifier measures the environment without requiring a pointing gesture.
    public bool RequireVisibleHand { get; set; } = true;

    public int LastSupportSampleCount => samples.Count;
    public int HandMaskedProbeCount { get; private set; }
    public float LastProbeRadiusPixels { get; private set; }
    public bool IsConfirmingSurface { get; private set; }
    public Pose ConfirmingSurfacePose { get; private set; }
    public int LastNativeQueryCount { get; private set; }
    public int LastNativeHitCount { get; private set; }
    public int LastNativeNoHitCount { get; private set; }
    public int LastUnreliableNormalCount { get; private set; }
    public int LastRejectedRangeCount { get; private set; }
    public int LastRejectedHeightCount { get; private set; }
    public int LastRejectedInvalidCount { get; private set; }

    /// <summary>Depth is running for measurements, but its rendering keyword is disabled.</summary>
    public bool IsReady => IsAndroidPlayer && camera != null && raycasts != null &&
        raycasts.isActiveAndEnabled && raycasts.subsystem != null && raycasts.subsystem.running &&
        ARSession.state == ARSessionState.SessionTracking && occlusion != null &&
        occlusion.isActiveAndEnabled && occlusion.subsystem != null && occlusion.subsystem.running &&
        occlusion.descriptor?.environmentDepthImageSupported == Supported.Supported &&
        occlusion.currentEnvironmentDepthMode != EnvironmentDepthMode.Disabled &&
        occlusion.currentOcclusionPreferenceMode == OcclusionPreferenceMode.NoOcclusion;

    private static bool IsAndroidPlayer => !Application.isEditor && Application.platform == RuntimePlatform.Android;

    public void Configure(Camera arCamera, ARRaycastManager raycastManager, ButtonInteractor handInput = null)
    {
        if (this.handInput != handInput)
        {
            this.handInput = handInput;
            Reset();
        }
        if (camera != arCamera || raycasts != raycastManager)
        {
            camera = arCamera;
            raycasts = raycastManager;
            occlusion = camera != null ? camera.GetComponent<AROcclusionManager>() : null;
            hasReportedSupport = false;
            reportedMissingManager = false;
            Reset();
        }
        else if (occlusion == null && camera != null)
        {
            occlusion = camera.GetComponent<AROcclusionManager>();
        }

        if (!EnsureDepthReady() || (RequireVisibleHand && this.handInput != null && !this.handInput.IsHandVisible))
            Reset();
    }

    private bool EnsureDepthReady()
    {
        // Editor fixtures and other providers must not start/change a user's AR session.
        if (!IsAndroidPlayer || camera == null || raycasts == null)
            return false;
        if (occlusion == null)
        {
            if (!reportedMissingManager)
            {
                Debug.Log("[ARPlacementDepth] No existing AR camera occlusion manager; using measured plane polygons only.");
                reportedMissingManager = true;
            }
            return false;
        }

        // Set this FIRST, including before enabling the subsystem. Generating measurements
        // must never restore ARCORE_ENVIRONMENT_DEPTH_ENABLED and half-clip the components.
        ApplyMeasurementOnlyConfiguration(occlusion, false);
        if (ARSession.state != ARSessionState.SessionTracking)
            return false;
        if (!occlusion.enabled)
            occlusion.enabled = true;

        // ARCore cannot reliably report depth support before its session has started.
        if (occlusion.descriptor == null)
            return false;
        var support = occlusion.descriptor.environmentDepthImageSupported;
        if (!hasReportedSupport || support != reportedSupport)
        {
            Debug.Log($"[ARPlacementDepth] environmentDepth={support}; visualOcclusion=disabled; measured fallback only.");
            reportedSupport = support;
            hasReportedSupport = true;
        }
        if (support != Supported.Supported)
            return false;

        ApplyMeasurementOnlyConfiguration(occlusion, true);
        return IsReady;
    }

    /// <summary>Separates acquiring depth from rendering depth occlusion.</summary>
    public static void ApplyMeasurementOnlyConfiguration(AROcclusionManager manager, bool requestDepth)
    {
        if (manager == null)
            return;
        if (manager.requestedOcclusionPreferenceMode != OcclusionPreferenceMode.NoOcclusion)
            manager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;
        if (manager.requestedHumanStencilMode != HumanSegmentationStencilMode.Disabled)
            manager.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
        if (manager.requestedHumanDepthMode != HumanSegmentationDepthMode.Disabled)
            manager.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
        if (requestDepth && manager.requestedEnvironmentDepthMode != EnvironmentDepthMode.Fastest)
            manager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
    }

    public bool TryGetSurface(Vector2 screenPoint, out Pose pose)
    {
        pose = default;
        if (!IsFinite(screenPoint.x) || !IsFinite(screenPoint.y) ||
            screenPoint.x < 0f || screenPoint.y < 0f || screenPoint.x >= Screen.width || screenPoint.y >= Screen.height ||
            !EnsureDepthReady() || (RequireVisibleHand && handInput != null && !handInput.IsHandVisible))
        {
            Reset();
            return false;
        }

        int frame = Time.frameCount;
        // Keep native measurement work at 10 Hz, but intersect the CURRENT ray with
        // the recently validated finite world patch each render frame. Reusing the
        // old contact or requiring a 1-pixel-stationary finger made dragging stop/go.
        if (frame == lastQueryFrame || Time.unscaledTime < nextQueryAt)
        {
            return finitePatch.TryProject(camera.ScreenPointToRay(screenPoint),
                camera.transform.position, Time.unscaledTime, out pose);
        }
        nextQueryAt = Mathf.Max(nextQueryAt + 0.1f, Time.unscaledTime);
        querySequence++;
        lastQueryFrame = frame;
        // Any failed fresh measurement must invalidate the old support, not keep
        // carrying it across a table edge until its timeout.
        finitePatch.Reset();
        samples.Clear();
        IsConfirmingSurface = false;
        HandMaskedProbeCount = 0;
        ResetNativeDiagnostics();

        System.Func<Vector2, bool> handMask = handInput != null
            ? handInput.IsPointOverTrackedHand : (System.Func<Vector2, bool>)null;
        float radius = ChooseProbeRadius(screenPoint, new Vector2(Screen.width, Screen.height), handMask);
        LastProbeRadiusPixels = radius;
        foreach (var direction in ProbeDirections)
        {
            var probe = screenPoint + direction * radius;
            if (probe.x < 0f || probe.y < 0f || probe.x >= Screen.width || probe.y >= Screen.height)
                continue;
            // A palm can be horizontal and geometrically resemble a small table.
            // Exclude recognized hand pixels BEFORE collecting support, rather than
            // relying on surface normals to distinguish skin from the environment.
            if (handInput != null && handInput.IsPointOverTrackedHand(probe))
            {
                HandMaskedProbeCount++;
                continue;
            }
            if (TryGetDepthSample(probe, out var sample))
                samples.Add(sample);
        }

        // Do not use the center pixel as support: it usually contains the pointing finger.
        // Real samples on several sides must enclose the target in a small measured patch.
        var ray = camera.ScreenPointToRay(screenPoint);
        if (!finitePatch.TryCapture(ray, camera.transform.position, samples, Time.unscaledTime, out var measuredPose))
        {
            stability.Reset();
            return false;
        }

        if (!stability.Observe(measuredPose, screenPoint, querySequence))
        {
            IsConfirmingSurface = true;
            ConfirmingSurfacePose = measuredPose;
            finitePatch.Reset();
            return false;
        }
        pose = measuredPose;
        return true;
    }

    private bool TryGetDepthSample(Vector2 screenPoint, out Pose pose)
    {
        pose = default;
        hits.Clear();
        LastNativeQueryCount++;
        if (!raycasts.Raycast(screenPoint, hits, TrackableType.Depth))
        {
            LastNativeNoHitCount++;
            return false;
        }
        foreach (var hit in hits)
        {
            if ((hit.hitType & TrackableType.Depth) == 0)
                continue;
            // Reject an unsuitable first surface rather than looking through the hand
            // for another hit and pretending the foreground was not present.
            pose = hit.pose;
            LastNativeHitCount++;
            // Pixel normals can be noisy even when neighbouring measured points
            // form a flat floor. Validate the patch's 3D geometry below instead.
            if (Vector3.Dot(pose.rotation * Vector3.up, Vector3.up) < MinimumUpDot)
                LastUnreliableNormalCount++;
            var rejection = GetSampleRejection(pose, camera.transform.position);
            switch (rejection)
            {
                case SampleRejection.Range: LastRejectedRangeCount++; break;
                case SampleRejection.Height: LastRejectedHeightCount++; break;
                case SampleRejection.Invalid: LastRejectedInvalidCount++; break;
            }
            return rejection == SampleRejection.None;
        }
        LastNativeNoHitCount++;
        return false;
    }

    private void ResetNativeDiagnostics()
    {
        LastNativeQueryCount = 0;
        LastNativeHitCount = 0;
        LastNativeNoHitCount = 0;
        LastUnreliableNormalCount = 0;
        LastRejectedRangeCount = 0;
        LastRejectedHeightCount = 0;
        LastRejectedInvalidCount = 0;
    }

    public void Reset()
    {
        nextQueryAt = 0f;
        querySequence = 0;
        lastQueryFrame = -1;
        samples.Clear();
        HandMaskedProbeCount = 0;
        LastProbeRadiusPixels = 0f;
        IsConfirmingSurface = false;
        ResetNativeDiagnostics();
        stability.Reset();
        finitePatch.Reset();
    }

    /// <summary>
    /// Select a small surrounding ring clear of the recognized hand before doing
    /// native hit tests. Expanding screen probes never expands accepted geometry:
    /// all actual samples still have to fit the same 35 cm measured patch.
    /// </summary>
    public static float ChooseProbeRadius(Vector2 screenPoint, Vector2 screenSize,
        System.Func<Vector2, bool> isHandMasked)
    {
        if (!IsFinite(screenPoint.x) || !IsFinite(screenPoint.y) ||
            !IsFinite(screenSize.x) || !IsFinite(screenSize.y) || screenSize.x <= 0f || screenSize.y <= 0f)
            return 16f;
        float shortSide = Mathf.Max(1f, Mathf.Min(screenSize.x, screenSize.y));
        float radius = Mathf.Clamp(shortSide * 0.035f, 16f, 60f);
        float maximumRadius = Mathf.Max(radius, shortSide * 0.12f);
        if (isHandMasked == null) return radius;

        int bestCount = -1;
        float bestRadius = radius;
        var available = new List<Vector2>(8);
        while (true)
        {
            available.Clear();
            foreach (Vector2 direction in ProbeDirections)
            {
                Vector2 probe = screenPoint + direction * radius;
                if (probe.x < 0f || probe.y < 0f || probe.x >= screenSize.x || probe.y >= screenSize.y ||
                    isHandMasked(probe)) continue;
                available.Add(direction * radius);
            }
            if (available.Count > bestCount)
            {
                bestCount = available.Count;
                bestRadius = radius;
            }
            if (available.Count >= MinimumSupportSamples &&
                ContainsSupportedPoint(ConvexHull(available), Vector2.zero))
                return radius;
            if (radius >= maximumRadius) return bestRadius;
            radius = Mathf.Min(maximumRadius, radius * 1.5f);
        }
    }

    /// <summary>
    /// Pure geometry check. A dominant near-horizontal height cluster must surround the
    /// target; no infinite-plane extension or bridging between different heights is used.
    /// </summary>
    public static bool TryValidatePatch(Ray targetRay, Vector3 cameraPosition,
        IReadOnlyList<Pose> measuredSamples, out Pose pose)
    {
        return TryValidatePatch(targetRay, cameraPosition, measuredSamples, out pose, out _, out _);
    }

    private static bool TryValidatePatch(Ray targetRay, Vector3 cameraPosition,
        IReadOnlyList<Pose> measuredSamples, out Pose pose, out List<Vector2> supportedHull, out Vector3 supportNormal)
    {
        pose = default;
        supportedHull = null;
        supportNormal = Vector3.up;
        if (measuredSamples == null || measuredSamples.Count < MinimumSupportSamples ||
            !IsFinite(cameraPosition) || !IsFinite(targetRay.origin) || !IsFinite(targetRay.direction) ||
            targetRay.direction.y >= -0.01f)
            return false;

        // At most eight ring probes are collected at runtime. Keep the helper bounded
        // even when passed arbitrary data by another caller.
        int count = Mathf.Min(measuredSamples.Count, 16);
        int bestSeed = -1;
        int bestCount = 0;
        for (int seed = 0; seed < count; seed++)
        {
            if (!IsUsableSample(measuredSamples[seed], cameraPosition))
                continue;
            int sameHeightCount = 0;
            for (int sample = 0; sample < count; sample++)
                if (IsUsableSample(measuredSamples[sample], cameraPosition) &&
                    Mathf.Abs(measuredSamples[sample].position.y - measuredSamples[seed].position.y) <= HeightTolerance)
                    sameHeightCount++;
            if (sameHeightCount > bestCount)
            {
                bestSeed = seed;
                bestCount = sameHeightCount;
            }
        }
        if (bestSeed < 0 || bestCount < MinimumSupportSamples)
            return false;

        float seedHeight = measuredSamples[bestSeed].position.y;
        var points = new List<Vector2>(bestCount);
        var heights = new List<float>(bestCount);
        var support = new List<Vector3>(bestCount);
        for (int i = 0; i < count; i++)
        {
            var sample = measuredSamples[i];
            if (IsUsableSample(sample, cameraPosition) && Mathf.Abs(sample.position.y - seedHeight) <= HeightTolerance)
            {
                points.Add(new Vector2(sample.position.x, sample.position.z));
                heights.Add(sample.position.y);
                support.Add(sample.position);
            }
        }
        heights.Sort();
        float height = heights[heights.Count / 2];

        // A second sizable layer indicates mixed table/floor/hand geometry rather than
        // one supported patch, even if the two projected hulls happen to overlap.
        for (int seed = 0; seed < count; seed++)
        {
            if (!IsUsableSample(measuredSamples[seed], cameraPosition) ||
                Mathf.Abs(measuredSamples[seed].position.y - height) <= HeightTolerance)
                continue;
            int otherLevelCount = 0;
            for (int i = 0; i < count; i++)
                if (IsUsableSample(measuredSamples[i], cameraPosition) &&
                    Mathf.Abs(measuredSamples[i].position.y - measuredSamples[seed].position.y) <= HeightTolerance)
                    otherLevelCount++;
            if (otherLevelCount >= MinimumSupportSamples)
                return false;
        }

        if (!TryFitHorizontalSupport(support, out Vector3 supportPoint, out supportNormal) ||
            !new Plane(supportNormal, supportPoint).Raycast(targetRay, out float distanceAlongRay) || distanceAlongRay <= 0f)
            return false;
        var position = targetRay.GetPoint(distanceAlongRay);
        float range = Vector3.Distance(position, cameraPosition);
        if (!IsFinite(position) || range < MinimumRange || range > MaximumRange || cameraPosition.y <= position.y + 0.02f)
            return false;
        for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
                if ((points[i] - points[j]).sqrMagnitude > MaximumPatchSpan * MaximumPatchSpan)
                    return false;

        var hull = ConvexHull(points);
        if (hull.Count < 3 || !ContainsSupportedPoint(hull, new Vector2(position.x, position.z)))
            return false;

        pose = new Pose(position, Quaternion.identity);
        supportedHull = hull;
        return true;
    }

    private static bool TryFitHorizontalSupport(List<Vector3> samples, out Vector3 center, out Vector3 normal)
    {
        center = Vector3.zero;
        normal = Vector3.up;
        foreach (Vector3 point in samples) center += point;
        center /= samples.Count;
        double xx = 0, zz = 0, xz = 0, xy = 0, zy = 0;
        foreach (Vector3 point in samples)
        {
            Vector3 d = point - center;
            xx += (double)d.x * d.x;
            zz += (double)d.z * d.z;
            xz += (double)d.x * d.z;
            xy += (double)d.x * d.y;
            zy += (double)d.z * d.y;
        }
        double determinant = xx * zz - xz * xz;
        if (xx < 0.000001 || zz < 0.000001 || determinant <= xx * zz * 0.0001) return false;
        float slopeX = (float)((xy * zz - zy * xz) / determinant);
        float slopeZ = (float)((zy * xx - xy * xz) / determinant);
        normal = new Vector3(-slopeX, 1f, -slopeZ).normalized;
        if (!IsFinite(normal) || Vector3.Dot(normal, Vector3.up) < MinimumUpDot) return false;
        float squaredResidual = 0f;
        foreach (Vector3 point in samples)
        {
            Vector3 d = point - center;
            float residual = d.y - slopeX * d.x - slopeZ * d.z;
            if (Mathf.Abs(residual) > 0.012f) return false;
            squaredResidual += residual * residual;
        }
        // Keep the fit within 6 mm RMS; a warped/mixed patch is not a floor.
        return squaredResidual / samples.Count <= 0.006f * 0.006f;
    }

    /// <summary>
    /// Bounded geometric cache only. Runtime callers must confirm temporal stability
    /// before using it. It never retains an infinite plane or the old screen contact.
    /// </summary>
    public sealed class FinitePatchCache
    {
        public const float MaximumAge = 0.12f;
        private List<Vector2> hull;
        private Plane supportPlane;
        private float observedAt;

        public bool TryCapture(Ray targetRay, Vector3 cameraPosition, IReadOnlyList<Pose> measuredSamples,
            float now, out Pose pose)
        {
            Reset();
            pose = default;
            if (!IsFinite(now) || !TryValidatePatch(targetRay, cameraPosition, measuredSamples,
                out pose, out var supportedHull, out var normal)) return false;
            hull = supportedHull;
            supportPlane = new Plane(normal, pose.position);
            observedAt = now;
            return true;
        }

        public bool TryProject(Ray targetRay, Vector3 cameraPosition, float now, out Pose pose)
        {
            pose = default;
            float age = now - observedAt;
            if (hull == null || !IsFinite(now) || age < 0f || age > MaximumAge ||
                !IsFinite(cameraPosition) || !IsFinite(targetRay.origin) || !IsFinite(targetRay.direction) ||
                targetRay.direction.y >= -0.01f)
                return false;
            if (!supportPlane.Raycast(targetRay, out float distance) || distance <= 0f) return false;
            Vector3 position = targetRay.GetPoint(distance);
            float range = Vector3.Distance(position, cameraPosition);
            if (!IsFinite(position) || range < MinimumRange || range > MaximumRange || cameraPosition.y <= position.y + 0.02f ||
                !ContainsSupportedPoint(hull, new Vector2(position.x, position.z))) return false;
            pose = new Pose(position, Quaternion.identity);
            return true;
        }

        public void Reset()
        {
            hull = null;
            supportPlane = default;
            observedAt = float.NegativeInfinity;
        }
    }

    private static bool IsUsableSample(Pose sample, Vector3 cameraPosition)
    {
        return GetSampleRejection(sample, cameraPosition) == SampleRejection.None;
    }

    private enum SampleRejection { None, Invalid, Range, Height }

    private static SampleRejection GetSampleRejection(Pose sample, Vector3 cameraPosition)
    {
        if (!IsFinite(cameraPosition) || !IsFinite(sample.position) || !IsFinite(sample.rotation.x) || !IsFinite(sample.rotation.y) ||
            !IsFinite(sample.rotation.z) || !IsFinite(sample.rotation.w))
            return SampleRejection.Invalid;
        float rotationLength = sample.rotation.x * sample.rotation.x + sample.rotation.y * sample.rotation.y +
            sample.rotation.z * sample.rotation.z + sample.rotation.w * sample.rotation.w;
        if (rotationLength < 0.9f || rotationLength > 1.1f)
            return SampleRejection.Invalid;
        float range = Vector3.Distance(sample.position, cameraPosition);
        if (range < MinimumRange || range > MaximumRange)
            return SampleRejection.Range;
        if (cameraPosition.y <= sample.position.y + 0.02f)
            return SampleRejection.Height;
        return SampleRejection.None;
    }

    private static List<Vector2> ConvexHull(List<Vector2> points)
    {
        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        var hull = new List<Vector2>(points.Count * 2);
        foreach (var point in points)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        int lowerCount = hull.Count;
        for (int i = points.Count - 2; i >= 0; i--)
        {
            var point = points[i];
            while (hull.Count > lowerCount && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        if (hull.Count > 1)
            hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static bool ContainsSupportedPoint(List<Vector2> hull, Vector2 target)
    {
        float twiceArea = 0f;
        for (int i = 0; i < hull.Count; i++)
        {
            var next = hull[(i + 1) % hull.Count];
            twiceArea += Cross(hull[i], next);
            if (Cross(next - hull[i], target - hull[i]) < -0.000001f)
                return false;
        }
        return twiceArea >= 0.0002f; // Reject a collinear/narrow line, not just its AABB.
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

    /// <summary>
    /// Two consecutive render-frame observations, not two calls in one frame, confirm a patch.
    /// Native depth can reuse an image between these frames; this is a temporal jitter guard,
    /// not a claim that two independent sensor images have been acquired.
    /// </summary>
    public sealed class PatchStability
    {
        private int lastFrame = -1;
        private Pose previous;
        private Vector2 previousPointer;
        private int consecutive;
        private bool ready;

        public bool Observe(Pose pose, Vector2 screenPoint, int frame)
        {
            if (frame == lastFrame)
                return ready;
            bool stable = consecutive > 0 && frame == lastFrame + 1 &&
                Mathf.Abs(pose.position.y - previous.position.y) <= 0.03f &&
                (pose.position - previous.position).sqrMagnitude <= 0.08f * 0.08f &&
                (screenPoint - previousPointer).sqrMagnitude <= 90f * 90f;
            consecutive = stable ? consecutive + 1 : 1;
            previous = pose;
            previousPointer = screenPoint;
            lastFrame = frame;
            ready = consecutive >= 2;
            return ready;
        }

        public void Reset()
        {
            lastFrame = -1;
            consecutive = 0;
            ready = false;
        }
    }
}
