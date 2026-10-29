using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Deterministic checks that do not require an AR device or a running scene.</summary>
public static class WirePerformanceRegressionChecks
{
    public static void Run(List<string> results)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject controllerObject = new GameObject("WireRegressionController");
        GameObject terminalA = new GameObject("TerminalA");
        GameObject terminalB = new GameObject("TerminalB");
        GameObject wireObject = new GameObject("WireRegressionMesh");
        Mesh mesh = new Mesh();
        SceneManager.MoveGameObjectToScene(controllerObject, scene);
        SceneManager.MoveGameObjectToScene(terminalA, scene);
        SceneManager.MoveGameObjectToScene(terminalB, scene);
        SceneManager.MoveGameObjectToScene(wireObject, scene);
        WireConnectionController controller = controllerObject.AddComponent<WireConnectionController>();
        try
        {
            terminalB.transform.position = new Vector3(0.3f, 0f, 0.1f);
            var wire = new WireConnectionController.WireRecord(
                terminalA.transform, terminalB.transform, wireObject, null, null, mesh,
                null, null, null, null, Color.red);
            controller.activeWires.Add(wire);
            MethodInfo update = typeof(WireConnectionController).GetMethod(
                "UpdateActiveWirePositions", BindingFlags.Instance | BindingFlags.NonPublic);
            Action updateWires = (Action)Delegate.CreateDelegate(typeof(Action), controller, update);

            updateWires();
            Check(mesh.vertexCount > 0, "Initial wire geometry was empty.");
            Vector3[] originalBuffer = wire.vertices;
            int[] originalIndices = wire.triangles;
            Vector3 originalEnd = originalBuffer[originalBuffer.Length - 1];

            // A static frame must not recompute or re-upload geometry.
            Vector3 sentinel = new Vector3(100f, 100f, 100f);
            wire.vertices[0] = sentinel;
            updateWires();
            Check(wire.vertices[0] == sentinel, "A static wire was rebuilt.");
            for (int i = 0; i < 64; i++) updateWires();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) updateWires();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "Static wire updates allocated " + allocated + " managed bytes.");
            results.Add("PASS: static wire frames skip mesh rebuilds and 256 warmed updates allocate zero managed bytes");

            terminalB.transform.position += new Vector3(0.1f, 0f, 0f);
            updateWires();
            Check(ReferenceEquals(originalBuffer, wire.vertices), "Dragging allocated a new vertex buffer.");
            Check(ReferenceEquals(originalIndices, wire.triangles), "Dragging recreated triangle indices.");
            Check(wire.vertices[0] != sentinel, "Moving a terminal did not refresh the wire.");
            Check(wire.vertices[wire.vertices.Length - 1] != originalEnd, "Wire endpoint did not follow its terminal.");
            results.Add("PASS: wire movement follows terminals while reusing vertex buffers and triangle topology");

            controller.curveSegments += 2;
            updateWires();
            Check(mesh.vertexCount == (controller.curveSegments + 1) * (controller.radialSegments + 1),
                "Changing segment count produced an invalid vertex count.");
            foreach (int index in mesh.triangles)
                Check(index >= 0 && index < mesh.vertexCount, "Wire has an out-of-range triangle index.");

            terminalA.SetActive(false);
            updateWires();
            Check(!wireObject.activeSelf, "Wire remained visible when tracking hid its terminal.");
            terminalA.SetActive(true);
            updateWires();
            Check(wireObject.activeSelf, "Wire failed to reappear after tracking recovered.");

            // Newly coincident terminals must also recover with valid mesh topology.
            wire.hasGeometry = false;
            mesh.Clear();
            terminalB.transform.position = terminalA.transform.position;
            updateWires();
            terminalB.transform.position = new Vector3(0.2f, 0f, 0f);
            updateWires();
            Check(mesh.vertexCount > 0 && mesh.triangles.Length > 0,
                "Wire failed to recover after coincident endpoints separated.");
            results.Add("PASS: wire topology resize, tracking hide/recovery and coincident endpoint recovery remain valid");
        }
        finally
        {
            // The runtime owns deferred destruction; the edit-mode harness owns these fixtures.
            controller.activeWires.Clear();
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(wireObject);
            UnityEngine.Object.DestroyImmediate(terminalA);
            UnityEngine.Object.DestroyImmediate(terminalB);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Wire performance regression: " + message);
    }
}
