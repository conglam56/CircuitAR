using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Managed hot-path checks; device FPS and native crash recovery require Android.</summary>
internal static class ARRuntimePerformanceRegressionChecks
{
    public static void Run(List<string> results)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("AROverlapPerformanceFixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var controller = root.AddComponent<TapToPlaceController>();
            controller.enabled = false;
            typeof(TapToPlaceController).GetField("nextOverlapDiagnosticAt",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, float.PositiveInfinity);
            var candidate = Body(root, "Candidate", Vector3.zero);
            var neighbour = Body(root, "Placed_Neighbour", Vector3.right * 0.1f);
            controller.NotifyComponentToggled(neighbour, true);
            controller.NotifyComponentToggled(neighbour, true);
            Require(controller.placedObjectsHistory.Count == 1, "Redo must not duplicate collision roots.");
            Require(controller.CheckOverlap(Vector3.zero, candidate, out var hit) && hit == neighbour,
                "Registered same-level components must block overlapping placement.");
            Require(!controller.CheckOverlap(Vector3.right * 0.5f, candidate, out _),
                "Separated footprints must remain placeable.");
            Require(!controller.CheckOverlap(Vector3.up * 0.75f, candidate, out _),
                "Floor/table footprints at different heights must not block each other.");
            neighbour.SetActive(false);
            Require(!controller.CheckOverlap(Vector3.zero, candidate, out _),
                "Hidden components must not block placement.");
            neighbour.SetActive(true);
            controller.NotifyComponentToggled(neighbour, false);
            Require(!controller.CheckOverlap(Vector3.zero, candidate, out _),
                "Removing a collision root must take effect immediately, without a scene rescan.");
            controller.NotifyComponentToggled(neighbour, true);
            candidate.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
            neighbour.transform.rotation = Quaternion.Euler(0f, -29f, 0f);
            Require(controller.CheckOverlap(Vector3.zero, candidate, out _),
                "Redo and rotated SAT footprints must preserve overlap detection.");
            results.Add("PASS: collision registry preserves overlap, rotation, height separation, hide and undo/redo behavior");

            // Warm all generic calls and reusable List capacities before counting.
            for (int i = 0; i < 64; i++) controller.CheckOverlap(Vector3.zero, candidate, out _);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                controller.CheckOverlap(Vector3.zero, candidate, out _);
                controller.CheckOverlap(Vector3.right * 0.5f, candidate, out _);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Require(allocated == 0, "Warmed preview/drag collision checks allocated " + allocated + " managed bytes.");
            results.Add("PASS: 512 warmed preview/drag collision checks allocate zero managed bytes");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static GameObject Body(GameObject parent, string name, Vector3 position)
    {
        var body = new GameObject(name);
        body.transform.SetParent(parent.transform, false);
        body.transform.position = position;
        body.AddComponent<BoxCollider>().size = new Vector3(0.12f, 0.04f, 0.08f);
        return body;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
