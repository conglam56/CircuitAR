using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Geometry contact checks, isolated from native AR and the user's open scene.</summary>
internal static class ARContactRegressionChecks
{
    public static void Run(List<string> results)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("ARContactRegressionFixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        var controller = root.AddComponent<TapToPlaceController>();
        controller.enabled = false;
        Mesh syntheticMesh = null;
        try
        {
            var anchor = Child(root, "ContactAnchor");
            const float floorY = 1.25f;
            anchor.transform.position = new Vector3(0.2f, floorY + controller.surfaceContactOffset, -0.3f);
            var visual = Child(anchor, "Visual");
            var filter = visual.AddComponent<MeshFilter>();
            visual.AddComponent<MeshRenderer>();
            syntheticMesh = new Mesh { name = "ContactRegressionTriangle" };
            syntheticMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0f, 0.8f, 0.5f)
            };
            syntheticMesh.triangles = new[] { 0, 1, 2 };
            syntheticMesh.RecalculateBounds();
            filter.sharedMesh = syntheticMesh;
            var oversizedGrabBox = visual.AddComponent<BoxCollider>();
            oversizedGrabBox.center = Vector3.down * 8f;
            oversizedGrabBox.size = Vector3.one * 20f;
            var terminal = Primitive(visual, "DecorativeTerminal", new Vector3(0f, -10f, 0f));
            terminal.tag = "Terminal";
            var disabledDecoration = Primitive(visual, "DisabledDecoration", new Vector3(0f, -20f, 0f));
            disabledDecoration.GetComponent<Renderer>().enabled = false;
            var line = Child(visual, "WireLine").AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPositions(new[] { Vector3.down * 30f, Vector3.down * 25f });

            foreach (float yaw in new[] { 0f, 41f, 167f })
            {
                anchor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                visual.transform.localPosition = new Vector3(0.1f, 0.4f, -0.2f);
                visual.transform.localRotation = Quaternion.Euler(20f, 35f, 10f);
                visual.transform.localScale = new Vector3(0.03f, 0.04f, 0.02f);
                Align(controller, anchor, visual);
                float actualBottom = float.PositiveInfinity;
                foreach (Vector3 vertex in syntheticMesh.vertices)
                    actualBottom = Mathf.Min(actualBottom, visual.transform.TransformPoint(vertex).y);
                Require(Mathf.Abs(actualBottom - anchor.transform.position.y) < 0.00001f,
                    "The actual rendered vertices must contact the anchor, not the oversized grab collider, terminals or invisible decoration.");
                Vector3 onceAligned = visual.transform.position;
                Align(controller, anchor, visual);
                Require(Vector3.Distance(onceAligned, visual.transform.position) < 0.00001f,
                    "Contact alignment must be idempotent at commit, after initial preview alignment.");
            }
            results.Add("PASS: rendered vertices contact the measured surface despite oversized grab collider, yaw and nonuniform scale");
            results.Add("PASS: terminals, disabled decoration and wire lines cannot lift the component body off its support");
            UnityEngine.Object.DestroyImmediate(visual);

            foreach (string path in new[]
            {
                "Assets/Prefabs/Components/lamp.prefab", "Assets/Prefabs/Components/Battery.prefab",
                "Assets/Prefabs/Components/voltmetr.prefab", "Assets/Prefabs/Components/ammetr.prefab",
                "Assets/Prefabs/Components/key.prefab"
            })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(prefab != null, "Missing shipped contact-test prefab: " + path);
                var model = UnityEngine.Object.Instantiate(prefab, anchor.transform, false);
                try
                {
                    foreach (float yaw in new[] { 0f, 37f, 103f, 281f })
                    {
                        anchor.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        model.transform.localScale = Vector3.one * 0.03f;
                        model.transform.localPosition = Vector3.up * 0.5f;
                        if (Mathf.Approximately(yaw, 0f))
                        {
                            float oldColliderBottom = LegacyInteractionColliderBottom(model);
                            if (!float.IsInfinity(oldColliderBottom))
                                Debug.Log($"[ARContact] {prefab.name}: old collider-based visible gap=" +
                                    $"{(LowestVisibleGeometry(model) - oldColliderBottom) * 1000f:F2} mm; " +
                                    "positive means the previous algorithm lifted visible geometry above its anchor.");
                        }
                        Align(controller, anchor, model);
                        float bottom = LowestVisibleGeometry(model);
                        Require(Mathf.Abs(bottom - (floorY + controller.surfaceContactOffset)) < 0.0001f,
                            "Shipped visible geometry must contact floor plus the configured contact offset: " + path);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            results.Add("PASS: shipped lamp, battery, voltmeter, ammeter and switch geometry contact their support at four yaw angles");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (syntheticMesh != null) UnityEngine.Object.DestroyImmediate(syntheticMesh);
        }
    }

    private static float LowestVisibleGeometry(GameObject visual)
    {
        float bottom = float.PositiveInfinity;
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer is LineRenderer) continue;
            bool terminal = false;
            for (Transform t = renderer.transform; t != null; t = t.parent)
            {
                if (t.CompareTag("Terminal")) { terminal = true; break; }
                if (t == visual.transform) break;
            }
            if (terminal) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) { bottom = Mathf.Min(bottom, renderer.bounds.min.y); continue; }
            if (mesh.isReadable)
            {
                foreach (Vector3 vertex in mesh.vertices)
                    bottom = Mathf.Min(bottom, renderer.transform.TransformPoint(vertex).y);
            }
            else
            {
                // Same player-safe representation used for non-readable FBX meshes.
                Bounds bounds = mesh.bounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    bottom = Mathf.Min(bottom, renderer.transform.TransformPoint(bounds.center +
                        Vector3.Scale(bounds.extents, new Vector3(x, y, z))).y);
            }
        }
        return bottom;
    }

    private static float LegacyInteractionColliderBottom(GameObject model)
    {
        BoxCollider biggest = null;
        float volume = 0f;
        foreach (BoxCollider box in model.GetComponentsInChildren<BoxCollider>())
        {
            if (box.CompareTag("Terminal")) continue;
            Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
            float candidate = Mathf.Abs(size.x * size.y * size.z);
            if (candidate > volume) { biggest = box; volume = candidate; }
        }
        if (biggest == null) return float.PositiveInfinity;
        float bottom = float.PositiveInfinity;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
            bottom = Mathf.Min(bottom, biggest.transform.TransformPoint(biggest.center +
                Vector3.Scale(biggest.size * 0.5f, new Vector3(x, y, z))).y);
        return bottom;
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static GameObject Primitive(GameObject parent, string name, Vector3 position)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent.transform, false);
        cube.transform.localPosition = position;
        return cube;
    }

    private static void Align(TapToPlaceController controller, GameObject anchor, GameObject visual) =>
        typeof(TapToPlaceController).GetMethod("AlignVisualBaseToAnchor", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, new object[] { anchor, visual });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
