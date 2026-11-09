using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Deterministic footprint and placement-history checks without native AR.</summary>
internal static class ARPlacementGeometryRegressionChecks
{
    [MenuItem("CircuitAR/Validate Placement Geometry Cache")]
    public static void RunBatch()
    {
        var results = new List<string>();
        Run(results);
        Debug.Log("[ARPlacementGeometryRegressionChecks] " + string.Join("\n", results));
    }

    public static void Run(List<string> results)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        var fixture = new GameObject("ARPlacementGeometryFixture");
        SceneManager.MoveGameObjectToScene(fixture, scene);
        fixture.transform.position = Vector3.up * 100f;
        var cache = new ARPlacementGeometryCache();
        try
        {
            GameObject component = Child(fixture, "Placed_CachedBody");
            GameObject body = Child(component, "Body");
            var box = body.AddComponent<BoxCollider>();
            box.center = new Vector3(0.025f, 0.02f, 0.03f);
            box.size = new Vector3(0.12f, 0.04f, 0.08f);
            GameObject terminal = Child(component, "Terminal");
            terminal.tag = "Terminal";
            terminal.AddComponent<BoxCollider>().size = Vector3.one;
            var initial = Read(cache, component);
            Near(initial.center, new Vector2(0.025f, 0.03f), "Body center must include the authored collider offset.");
            Near(initial.halfExtents, new Vector2(0.06f, 0.04f), "Terminal colliders must not enlarge the component body.");
            Require(initial.corners == null, "Footprint queries must not allocate corner arrays.");

            component.transform.localScale = new Vector3(2f, 1f, 0.5f);
            component.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var transformed = Read(cache, component);
            Near(transformed.center, new Vector2(0.015f, -0.05f), "A cached center must follow signed scale and yaw.");
            Near(transformed.halfExtents, new Vector2(0.12f, 0.02f), "A cached footprint must reflect changed root scale.");
            Near(initial.center, new Vector2(0.025f, 0.03f), "A second query must not mutate a previously returned footprint.");
            component.transform.localScale = Vector3.one;
            component.transform.rotation = Quaternion.identity;
            box.size = new Vector3(0.08f, 0.04f, 0.16f);
            cache.Invalidate(component);
            Near(Read(cache, component).halfExtents, new Vector2(0.04f, 0.08f), "Invalidating geometry must pick up changed collider dimensions.");
            results.Add("PASS: cached body geometry preserves offsets, excludes terminals, follows yaw/scale and rebuilds after edits");

            GameObject rendererOnly = Child(fixture, "Placed_RendererOnly");
            GameObject low = Cube(rendererOnly, "Base", new Vector3(0.12f, 0.04f, 0.08f), Vector3.zero);
            Cube(rendererOnly, "RaisedSwitchHandle", new Vector3(0.8f, 0.04f, 0.8f), Vector3.up * 0.2f);
            var beforeYaw = Read(cache, rendererOnly);
            Near(beforeYaw.halfExtents, new Vector2(0.06f, 0.04f), "Raised renderer details must not inflate the base footprint.");
            rendererOnly.transform.rotation = Quaternion.Euler(0f, 43f, 0f);
            cache.Invalidate(rendererOnly);
            Near(Read(cache, rendererOnly).halfExtents, beforeYaw.halfExtents, "Recapturing a rotated renderer must not cache a world AABB as body size.");
            Require(low != null, "Renderer-only fixture must remain alive.");
            results.Add("PASS: renderer fallback measures the base and remains stable when recaptured after rotation");

            var random = new System.Random(7241);
            for (int i = 0; i < 300; i++)
            {
                var a = RandomFootprint(random);
                var b = RandomFootprint(random);
                bool expected = ReferenceOverlap(a, b, out int expectedAxis);
                bool actual = ARPlacementGeometryCache.Overlaps(a, b, out int actualAxis);
                Require(actual == expected && actualAxis == expectedAxis,
                    "Analytic SAT must agree with corner projection for separated and rotated rectangles.");
                Require(actual == ARPlacementGeometryCache.Overlaps(b, a, out _), "Overlap must be symmetric.");
            }
            var touchingA = RandomFootprint(new System.Random(1));
            touchingA.center = Vector2.zero;
            touchingA.axis1 = Vector2.right;
            touchingA.axis2 = Vector2.up;
            touchingA.halfExtents = new Vector2(0.1f, 0.05f);
            var touchingB = touchingA;
            touchingB.center = Vector2.right * 0.2f;
            Require(ARPlacementGeometryCache.Overlaps(touchingA, touchingB, out _), "Touching rectangle boundaries must still overlap.");
            touchingB.center += Vector2.right * 0.001f;
            Require(!ARPlacementGeometryCache.Overlaps(touchingA, touchingB, out _), "A positive gap must separate the rectangles.");
            results.Add("PASS: allocation-free SAT agrees with corner projections for 300 pairs, touching edges and positive gaps");

            // Repeated queries after warm-up must keep scene traversal, temporary
            // arrays and SAT axis arrays out of the main placement frame loop.
            Read(cache, component);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 512; i++)
            {
                cache.TryGetFootprint(component, component.transform.position, component.transform.rotation, out var a);
                cache.TryGetFootprint(rendererOnly, rendererOnly.transform.position, rendererOnly.transform.rotation, out var b);
                ARPlacementGeometryCache.Overlaps(a, b, out _);
            }
            Require(GC.GetAllocatedBytesForCurrentThread() == before, "Warm footprint and SAT queries must not allocate managed memory.");
            results.Add("PASS: 512 warm footprint/SAT iterations allocate no managed memory");

            var controller = Child(fixture, "Controller").AddComponent<TapToPlaceController>();
            controller.enabled = false;
            GameObject candidate = Child(fixture, "Candidate");
            candidate.AddComponent<BoxCollider>().size = new Vector3(0.1f, 0.04f, 0.1f);
            typeof(TapToPlaceController).GetField("previewAnchor", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, candidate);
            controller.placedObjectsHistory.Add(component);
            Require(controller.CheckOverlap(candidate.transform.position, null, out var overlapping) && overlapping == component,
                "History-registered active components must block a new placement.");
            var action = new PlaceComponentAction(component, controller);
            action.Undo();
            Require(!controller.CheckOverlap(candidate.transform.position, null, out _), "Undo must stop an inactive component blocking placement.");
            action.Redo();
            Require(controller.CheckOverlap(candidate.transform.position, null, out _), "Redo must restore overlap participation.");
            results.Add("PASS: placement history follows active state through undo and redo without requiring scene discovery");
        }
        finally
        {
            cache.Clear();
            UnityEngine.Object.DestroyImmediate(fixture);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static GameObject Cube(GameObject parent, string name, Vector3 scale, Vector3 position)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent.transform, false);
        cube.transform.localScale = scale;
        cube.transform.localPosition = position;
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        return cube;
    }

    private static TapToPlaceController.FootprintOBB2D Read(ARPlacementGeometryCache cache, GameObject obj)
    {
        Require(cache.TryGetFootprint(obj, obj.transform.position, obj.transform.rotation, out var footprint), "A live component must have a footprint.");
        return footprint;
    }

    private static TapToPlaceController.FootprintOBB2D RandomFootprint(System.Random random)
    {
        float yaw = (float)random.NextDouble() * Mathf.PI * 2f;
        return new TapToPlaceController.FootprintOBB2D
        {
            center = new Vector2((float)random.NextDouble(), (float)random.NextDouble()),
            halfExtents = new Vector2(0.02f + (float)random.NextDouble() * 0.2f, 0.02f + (float)random.NextDouble() * 0.2f),
            axis1 = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw)),
            axis2 = new Vector2(-Mathf.Sin(yaw), Mathf.Cos(yaw))
        };
    }

    private static bool ReferenceOverlap(TapToPlaceController.FootprintOBB2D a,
        TapToPlaceController.FootprintOBB2D b, out int separatingAxisIndex)
    {
        separatingAxisIndex = -1;
        Vector2[] axes = { a.axis1, a.axis2, b.axis1, b.axis2 };
        Vector2[] cornersA = Corners(a);
        Vector2[] cornersB = Corners(b);
        for (int i = 0; i < axes.Length; i++)
        {
            float minA = float.PositiveInfinity, maxA = float.NegativeInfinity;
            float minB = float.PositiveInfinity, maxB = float.NegativeInfinity;
            for (int j = 0; j < 4; j++)
            {
                float projectionA = Vector2.Dot(cornersA[j], axes[i]);
                float projectionB = Vector2.Dot(cornersB[j], axes[i]);
                minA = Mathf.Min(minA, projectionA);
                maxA = Mathf.Max(maxA, projectionA);
                minB = Mathf.Min(minB, projectionB);
                maxB = Mathf.Max(maxB, projectionB);
            }
            if (maxA < minB || maxB < minA)
            {
                separatingAxisIndex = i;
                return false;
            }
        }
        return true;
    }

    private static Vector2[] Corners(TapToPlaceController.FootprintOBB2D footprint)
    {
        Vector2 right = footprint.axis1 * footprint.halfExtents.x;
        Vector2 forward = footprint.axis2 * footprint.halfExtents.y;
        return new[]
        {
            footprint.center - right - forward, footprint.center + right - forward,
            footprint.center + right + forward, footprint.center - right + forward
        };
    }

    private static void Near(Vector2 actual, Vector2 expected, string message) =>
        Require(Vector2.Distance(actual, expected) < 0.0001f, message + " Actual=" + actual + "; expected=" + expected);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
