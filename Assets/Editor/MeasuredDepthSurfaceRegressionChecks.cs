using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>Deterministic geometry/configuration checks, not a substitute for on-device depth testing.</summary>
internal static class MeasuredDepthSurfaceRegressionChecks
{
    [MenuItem("CircuitAR/Validate Measured Depth Surface")]
    public static void Run()
    {
        var results = new List<string>();
        Run(results);
        Debug.Log("[MeasuredDepthSurfaceChecks]\n" + string.Join("\n", results));
    }

    public static void Run(ICollection<string> results)
    {
        var camera = new Vector3(0f, 0.8f, -0.5f);
        var ray = new Ray(camera, -camera.normalized);
        var patch = Ring(Vector3.zero, 0.1f, Quaternion.identity);
        Require(MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera, patch, out var pose) &&
                pose.position.sqrMagnitude < 0.000001f && pose.rotation == Quaternion.identity,
            "A flat measured patch surrounding the pointer must produce its real contact height.");
        results.Add("PASS: measured depth accepts a finite surrounding horizontal patch");

        var noisyNormals = new List<Pose>();
        for (int i = 0; i < patch.Count; i++)
            noisyNormals.Add(new Pose(patch[i].position, Quaternion.Euler(35f + i * 17f, i * 10f, i * 20f)));
        Require(MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera, noisyNormals, out var noisyPose) &&
            noisyPose.position.sqrMagnitude < 0.000001f,
            "Flat measured geometry must remain usable when individual depth-hit normals are unreliable.");
        results.Add("PASS: noisy pixel normals do not discard a genuinely horizontal measured patch");

        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera,
                Ring(Vector3.zero, 0.1f, Quaternion.Euler(30f, 0f, 0f)), out _) &&
                !MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera,
                Ring(Vector3.zero, 0.1f, Quaternion.Euler(90f, 0f, 0f)), out _),
            "Physically sloped and vertical measured geometry must not count as a horizontal support.");
        var insufficient = new List<Pose> { patch[0], patch[1], patch[2] };
        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera, insufficient, out _),
            "Three points alone must not be treated as a supported patch.");
        results.Add("PASS: insufficient, sloped and vertical depth samples cannot place components");

        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera,
            Ring(Vector3.zero, 0.02f, Quaternion.Euler(30f, 0f, 0f)), out _),
            "A small sloped patch cannot pass merely because its height range fits inside the height-cluster tolerance.");
        var warped = Ring(Vector3.zero, 0.06f, Quaternion.identity);
        for (int i = 0; i < warped.Count; i++)
        {
            var sample = warped[i];
            sample.position.y = 0.01f * Mathf.Sin(i * Mathf.PI * 0.5f);
            warped[i] = sample;
        }
        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera, warped, out _),
            "A warped depth patch must fail the geometric residual check even if its height samples are one cluster.");
        results.Add("PASS: geometry fitting rejects small slopes and nonplanar patches instead of mistaking them for floor noise");

        var beyondEdgeRay = new Ray(camera, (new Vector3(0.13f, 0f, 0f) - camera).normalized);
        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(beyondEdgeRay, camera, patch, out _),
            "A pointer outside measured support must not extend a table into empty space.");
        var line = new List<Pose>();
        for (int i = 0; i < 8; i++)
            line.Add(new Pose(new Vector3(-0.1f + i * 0.025f, 0f, 0f), Quaternion.identity));
        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera, line, out _),
            "Collinear samples must not pass merely because their bounds touch the pointer.");
        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera,
                Ring(Vector3.zero, 0.2f, Quaternion.identity), out _),
            "Widely spaced samples must not invent a large patch between them.");
        results.Add("PASS: depth support cannot extrapolate beyond an edge or span an unmeasured large gap");

        var mixed = new List<Pose>();
        for (int i = 0; i < patch.Count; i++)
        {
            var sample = patch[i];
            if (i % 2 == 0)
                sample.position += Vector3.down * 0.2f;
            mixed.Add(sample);
        }
        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera, mixed, out _),
            "Two interleaved support heights must not bridge a table/floor gap.");
        var splitEdge = new List<Pose>();
        for (int i = 0; i < patch.Count; i++)
        {
            var sample = patch[i];
            if (sample.position.x < 0f)
                sample.position += Vector3.down * 0.3f;
            splitEdge.Add(sample);
        }
        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera, splitEdge, out _),
            "A mixed table edge and lower floor must not become one flat support.");
        var elevatedPatch = Ring(new Vector3(0f, 0.2f, 0f), 0.1f, Quaternion.identity);
        var elevatedRay = new Ray(camera, (new Vector3(0f, 0.2f, 0f) - camera).normalized);
        Require(MeasuredDepthSurfaceProvider.TryValidatePatch(elevatedRay, camera, elevatedPatch, out var elevated) &&
                Mathf.Abs(elevated.position.y - 0.2f) < 0.00001f,
            "A real table patch must retain its measured elevation instead of assuming floor y=0.");
        results.Add("PASS: measured heights separate table/floor layers and retain elevated real supports");

        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(new Ray(Vector3.up * 6f, Vector3.down),
                Vector3.up * 6f, patch, out _) &&
                !MeasuredDepthSurfaceProvider.TryValidatePatch(new Ray(Vector3.up * 0.05f, Vector3.down),
                Vector3.up * 0.05f, Ring(Vector3.zero, 0.02f, Quaternion.identity), out _),
            "Out-of-range and close foreground hand-like depths must be rejected.");
        var invalid = Ring(Vector3.zero, 0.1f, Quaternion.identity);
        for (int i = 0; i < invalid.Count; i++)
            invalid[i] = new Pose(new Vector3(float.NaN, 0f, 0f), Quaternion.identity);
        Require(!MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera, invalid, out _),
            "Invalid native sample values must not enter a contact pose.");
        results.Add("PASS: invalid and out-of-range depth data never become a contact pose");

        var cache = new MeasuredDepthSurfaceProvider.FinitePatchCache();
        Require(cache.TryCapture(ray, camera, patch, 10f, out _),
            "The finite cache must start from a genuinely validated surrounding patch.");
        Vector3 movedCamera = camera + Vector3.right * 0.04f;
        Vector3 movedContact = new Vector3(0.05f, 0f, 0.02f);
        var movedRay = new Ray(movedCamera, (movedContact - movedCamera).normalized);
        Require(cache.TryProject(movedRay, movedCamera, 10.05f, out var projected) &&
                Vector3.Distance(projected.position, movedContact) < 0.00001f &&
                projected.position.sqrMagnitude > 0.001f,
            "A moving finger/camera must project its current ray, not reuse the old contact or freeze until the next depth query.");
        Require(cache.TryProject(new Ray(camera, (new Vector3(-0.04f, 0f, 0f) - camera).normalized),
                camera, 10.08f, out var secondProjected) && secondProjected.position.x < -0.039f,
            "Two different rays between native depth queries must move continuously within measured support.");
        results.Add("PASS: a fresh finite depth patch reprojects moving rays continuously between native queries");

        Require(!cache.TryProject(beyondEdgeRay, camera, 10.05f, out _) &&
                !cache.TryProject(ray, camera, 10f + MeasuredDepthSurfaceProvider.FinitePatchCache.MaximumAge + 0.001f, out _) &&
                !cache.TryProject(ray, camera, 9.99f, out _) &&
                !cache.TryProject(new Ray(camera, Vector3.forward), camera, 10.05f, out _) &&
                !cache.TryProject(new Ray(Vector3.down, Vector3.up), Vector3.down, 10.05f, out _) &&
                !cache.TryProject(new Ray(Vector3.up * 6f, Vector3.down), Vector3.up * 6f, 10.05f, out _) &&
                !cache.TryProject(new Ray(new Vector3(float.NaN, 1f, 0f), Vector3.down), camera, 10.05f, out _),
            "Cached projection must reject table edges, expired/backwards time, invalid rays, below-surface and out-of-range cameras.");
        Require(!cache.TryCapture(ray, camera, insufficient, 10.06f, out _) &&
                !cache.TryProject(ray, camera, 10.07f, out _),
            "A failed new measurement must invalidate the old patch immediately.");
        Require(cache.TryCapture(elevatedRay, camera, elevatedPatch, 11f, out _) &&
                cache.TryProject(elevatedRay, camera, 11.04f, out var cachedElevated) &&
                Mathf.Abs(cachedElevated.position.y - 0.2f) < 0.00001f,
            "Cached support must retain its real measured height, not assume floor zero.");
        cache.Reset();
        Require(!cache.TryProject(elevatedRay, camera, 11.05f, out _),
            "Reset/session/hand invalidation must leave no reusable finite support.");
        results.Add("PASS: finite depth reuse never extends edges, survives failed measurements or outlives its 120 ms lifetime");

        var tiltedCache = new MeasuredDepthSurfaceProvider.FinitePatchCache();
        var shallowSlope = Ring(new Vector3(0f, 0.2f, 0f), 0.05f, Quaternion.Euler(0f, 0f, 10f));
        Vector3 slopeTarget = new Vector3(0.02f, 0.2f + Mathf.Tan(10f * Mathf.Deg2Rad) * 0.02f, 0.01f);
        Require(tiltedCache.TryCapture(elevatedRay, camera, shallowSlope, 20f, out _) &&
            tiltedCache.TryProject(new Ray(camera, (slopeTarget - camera).normalized), camera, 20.05f, out var slopePose) &&
            Vector3.Distance(slopePose.position, slopeTarget) < 0.00001f,
            "Finite reuse must intersect the measured fitted support rather than flattening its height to the previous pointer.");
        results.Add("PASS: cached contact follows the measured shallow support geometry as the pointer/camera moves");

        Vector2 screenSize = new Vector2(1000f, 2000f);
        Vector2 screenPoint = screenSize * 0.5f;
        float clearRadius = MeasuredDepthSurfaceProvider.ChooseProbeRadius(screenPoint, screenSize, null);
        float adaptedRadius = MeasuredDepthSurfaceProvider.ChooseProbeRadius(screenPoint, screenSize,
            probe => (probe - screenPoint).sqrMagnitude < 70f * 70f);
        Require(Mathf.Approximately(clearRadius, 35f) && adaptedRadius > 70f && adaptedRadius <= 120f,
            "Cheap hand-mask checks must expand a blocked 35 px ring outward to real unmasked surrounding probes, within 12% of the short screen side.");
        float unobstructedRadius = MeasuredDepthSurfaceProvider.ChooseProbeRadius(screenPoint, screenSize, probe => false);
        float fullyBlockedRadius = MeasuredDepthSurfaceProvider.ChooseProbeRadius(screenPoint, screenSize, probe => true);
        Require(Mathf.Approximately(unobstructedRadius, clearRadius) && fullyBlockedRadius >= clearRadius && fullyBlockedRadius <= 120f,
            "Unmasked probes must keep the smallest ring; an entirely blocked hand must not cause unbounded expansion.");
        Require(MeasuredDepthSurfaceProvider.ChooseProbeRadius(screenPoint,
                new Vector2(float.PositiveInfinity, float.PositiveInfinity), probe => true) == 16f &&
                MeasuredDepthSurfaceProvider.ChooseProbeRadius(new Vector2(float.NaN, 1f), screenSize, probe => true) == 16f,
            "Invalid screen values must not create an unbounded adaptive search.");
        results.Add("PASS: depth probe rings adapt around recognized hands without unbounded screen expansion");

        var widerButFinite = Ring(Vector3.zero, 0.16f, Quaternion.identity);
        Require(MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera, widerButFinite, out _) &&
                !MeasuredDepthSurfaceProvider.TryValidatePatch(ray, camera,
                    Ring(Vector3.zero, 0.18f, Quaternion.identity), out _) &&
                !MeasuredDepthSurfaceProvider.TryValidatePatch(beyondEdgeRay, camera,
                    Ring(Vector3.zero, 0.1f, Quaternion.identity), out _),
            "Adaptive screen rings must retain the existing 35 cm world-span ceiling and finite hull edge checks.");
        results.Add("PASS: expanded screen probes retain the original measured world-span and edge safeguards");

        var stability = new MeasuredDepthSurfaceProvider.PatchStability();
        Require(!stability.Observe(pose, Vector2.zero, 10) &&
                !stability.Observe(pose, Vector2.zero, 10) &&
                stability.Observe(pose, Vector2.zero, 11) &&
                stability.Observe(pose, Vector2.zero, 11),
            "Two calls in one frame must not count as two render-frame observations.");
        Require(!stability.Observe(new Pose(Vector3.up * 0.05f, Quaternion.identity), Vector2.zero, 12),
            "An abrupt measured height change must reacquire stability.");
        stability.Reset();
        Require(!stability.Observe(pose, Vector2.zero, 20) &&
                !stability.Observe(pose, Vector2.zero, 22) &&
                stability.Observe(pose, Vector2.zero, 23),
            "Missing render frames must not carry stale sample stability forward.");
        Require(!stability.Observe(pose, Vector2.right * 150f, 24),
            "A large pointer jump must revalidate the new patch.");
        results.Add("PASS: depth stability requires consecutive frames and ignores duplicate same-frame queries");

        var fixture = new GameObject("MeasurementOnlyOcclusionFixture");
        fixture.SetActive(false);
        try
        {
            var manager = fixture.AddComponent<AROcclusionManager>();
            manager.enabled = false;
            manager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
            manager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            MeasuredDepthSurfaceProvider.ApplyMeasurementOnlyConfiguration(manager, false);
            Require(manager.requestedOcclusionPreferenceMode == OcclusionPreferenceMode.NoOcclusion &&
                    manager.requestedEnvironmentDepthMode == EnvironmentDepthMode.Disabled,
                "Disabling visual compositing must happen before any depth request.");
            MeasuredDepthSurfaceProvider.ApplyMeasurementOnlyConfiguration(manager, true);
            Require(manager.requestedOcclusionPreferenceMode == OcclusionPreferenceMode.NoOcclusion &&
                    manager.requestedEnvironmentDepthMode == EnvironmentDepthMode.Fastest &&
                    manager.requestedHumanDepthMode == HumanSegmentationDepthMode.Disabled &&
                    manager.requestedHumanStencilMode == HumanSegmentationStencilMode.Disabled && !manager.enabled,
                "Measurement configuration must request depth without visual/human occlusion or starting an Editor subsystem.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
        results.Add("PASS: depth measurements are requested with NoOcclusion, never automatic depth compositing");
    }

    private static List<Pose> Ring(Vector3 center, float radius, Quaternion rotation)
    {
        var samples = new List<Pose>(8);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4f;
            samples.Add(new Pose(center + rotation * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius, rotation));
        }
        return samples;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Measured depth regression: " + message);
    }
}
