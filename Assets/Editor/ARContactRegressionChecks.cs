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
            CheckWireGeometry(root, results);
            CheckWirePinch(root, results);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (syntheticMesh != null) UnityEngine.Object.DestroyImmediate(syntheticMesh);
        }
    }

    private static void CheckWirePinch(GameObject root, List<string> results)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var fixture = Child(root, "WirePinchFixture");
        fixture.transform.position = Vector3.up * 500f;
        var camera = Child(fixture, "Camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.pixelRect = new Rect(0f, 0f, 1000f, 600f);
        camera.aspect = 1000f / 600f;
        camera.nearClipPlane = 0.02f;
        var headset = camera.gameObject.AddComponent<PhoneHeadsetAR>();
        var placement = Child(fixture, "Placement").AddComponent<TapToPlaceController>();
        placement.enabled = false;
        var input = Child(fixture, "Input").AddComponent<ButtonInteractor>();
        input.enabled = false;
        var second = Child(fixture, "OtherInput").AddComponent<ButtonInteractor>();
        second.enabled = false;
        var wires = Child(fixture, "Wires").AddComponent<WireConnectionController>();
        wires.enabled = false;
        wires.buttonInteractor = input;
        wires.tapToPlaceController = placement;
        wires.isWireMode = true;
        typeof(WireConnectionController).GetField("mainCamera", flags).SetValue(wires, camera);
        var body = Child(fixture, "Component");
        body.transform.localPosition = new Vector3(0f, 0f, 1f);
        var terminal = Child(body, "Terminal").AddComponent<Terminal>();
        terminal.gameObject.AddComponent<BoxCollider>().size = Vector3.one * 0.008f;
        placement.placedObjectsHistory.Add(body);
        var near = typeof(WireConnectionController).GetMethod("FindTerminalNear", flags);
        var process = typeof(WireConnectionController).GetMethod("ProcessWirePinch", flags);
        var cancel = typeof(WireConnectionController).GetMethod("CancelSelection", flags | BindingFlags.Public);
        object request = typeof(WireConnectionController).GetField("primaryWirePinch", flags).GetValue(wires);
        object otherRequest = typeof(WireConnectionController).GetField("secondaryWirePinch", flags).GetValue(wires);
        Vector2 center = camera.WorldToScreenPoint(terminal.transform.position);
        void SetInput(ButtonInteractor hand, Vector2 point, bool started, bool visible = true, bool ui = false)
        {
            typeof(ButtonInteractor).GetField("<CurrentScreenPos>k__BackingField", flags).SetValue(hand, point);
            typeof(ButtonInteractor).GetField("<JustPinched>k__BackingField", flags).SetValue(hand, started);
            typeof(ButtonInteractor).GetField("<IsHandVisible>k__BackingField", flags).SetValue(hand, visible);
            hand.PinchConsumedByUI = ui;
            hand.isPinching = true;
        }
        void Step(ButtonInteractor hand, object state) => process.Invoke(wires, new[] { hand, state });
        void ResetSelection()
        {
            cancel.Invoke(wires, null);
            typeof(WireConnectionController).GetField("lastGestureFrame", flags).SetValue(wires, -1);
        }
        try
        {
            foreach (bool vr in new[] { false, true })
            {
                input.SetHeadsetCamera(vr ? camera : null);
                Require((Transform)near.Invoke(wires, new object[] { center + Vector2.right * 14f }) == terminal.transform,
                    "A small allowance around a tiny terminal must work in AR and camera-aligned VR.");
                Require(near.Invoke(wires, new object[] { center + Vector2.right * 80f }) == null,
                    "Terminal padding must remain bounded rather than accepting a pinch far from the visible pole.");
            }
            results.Add("PASS: AR and VR terminal targets accept a small screen-space margin while rejecting distant pinches");
            SetInput(input, center + Vector2.up * 100f, true);
            Step(input, request);
            Require(!wires.HasSelectedTerminal, "A missed fresh pinch must not choose a distant terminal.");
            SetInput(input, center, false);
            Step(input, request);
            Require(wires.HasSelectedTerminal, "A fresh held pinch must be allowed to settle onto the tiny terminal briefly after its first missed sample.");
            Step(input, request);
            Require(wires.HasSelectedTerminal, "The same held pinch must not immediately toggle its selected terminal off.");
            SetInput(second, center, true);
            Step(second, otherRequest);
            Require(wires.HasSelectedTerminal, "Two hands pinching the same terminal on the same frame must not select then immediately deselect it.");
            results.Add("PASS: wire pinch tolerates a brief aiming miss, commits once per gesture and ignores duplicate same-frame selections from the other hand");
            ResetSelection();
            SetInput(input, center, true, ui: true);
            Step(input, request);
            SetInput(input, center, false);
            Step(input, request);
            Require(!wires.HasSelectedTerminal, "A UI-origin pinch must never become a terminal selection when the pointer leaves the UI.");
            SetInput(input, center + Vector2.up * 100f, true);
            Step(input, request);
            request.GetType().GetField("expiresAt").SetValue(request, Time.unscaledTime - 0.01f);
            SetInput(input, center, false);
            Step(input, request);
            Require(!wires.HasSelectedTerminal, "A held gesture must not auto-select after its short intent window expires.");
            SetInput(input, center + Vector2.up * 100f, true);
            Step(input, request);
            SetInput(input, center, false, visible: false);
            Step(input, request);
            Require(!wires.HasSelectedTerminal, "A lost hand must not select a terminal from its retained cursor appearance.");
            SetInput(input, center, false);
            Step(input, request);
            Require(wires.HasSelectedTerminal, "A newly valid live pointer may complete an unexpired wire pinch after a brief occlusion.");
            results.Add("PASS: wire intent rejects UI ownership, expired gestures and invisible pointers, then resumes only with fresh visible input");
            ResetSelection();
        }
        finally
        {
            placement.placedObjectsHistory.Clear();
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void CheckWireGeometry(GameObject root, List<string> results)
    {
        var fixture = Child(root, "WirePerformanceFixture");
        fixture.SetActive(false);
        var wires = fixture.AddComponent<WireConnectionController>();
        var a = Child(fixture, "TerminalA").transform;
        var b = Child(fixture, "TerminalB").transform;
        a.localPosition = Vector3.left * 0.15f;
        b.localPosition = Vector3.right * 0.15f;
        var obj = Child(fixture, "Wire");
        var filter = obj.AddComponent<MeshFilter>();
        var renderer = obj.AddComponent<MeshRenderer>();
        var mesh = new Mesh();
        filter.sharedMesh = mesh;
        var pivotA = Child(obj, "PlugA");
        var pivotB = Child(obj, "PlugB");
        var record = new WireConnectionController.WireRecord(a, b, obj, filter, renderer, mesh, pivotA, pivotB, null, null, Color.red);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var update = typeof(WireConnectionController).GetMethod("UpdateWireGeometry", flags);
        var revision = typeof(WireConnectionController.WireRecord).GetField("geometryRevision", flags);
        var buffer = typeof(WireConnectionController.WireRecord).GetField("vertices", flags);
        void Draw(bool branch = false) => update.Invoke(wires, new object[]
            { record, branch, branch, a.position + new Vector3(0.04f, 0.02f, 0f), false, false, Vector3.zero });
        try
        {
            Draw();
            Require(mesh.vertexCount > 0, "The first wire draw must create visible geometry.");
            int first = (int)revision.GetValue(record);
            var vertices = buffer.GetValue(record);
            for (int i = 0; i < 60; i++) Draw();
            Require((int)revision.GetValue(record) == first,
                "Stationary wires must not regenerate and upload their tube mesh on idle frames.");
            results.Add("PASS: stationary wires retain their mesh across 60 idle updates without redundant geometry uploads");
            var before = mesh.bounds;
            b.position += new Vector3(0.05f, 0.03f, 0f);
            Draw();
            Require((int)revision.GetValue(record) == first + 1 && ReferenceEquals(vertices, buffer.GetValue(record)) &&
                !mesh.bounds.Equals(before) && Vector3.Distance(pivotB.transform.position, wires.GetTerminalBaseAnchor(b)) < 0.00001f,
                "Dragging an endpoint must update the wire and plug immediately while reusing existing geometry buffers.");
            Draw(true);
            Require((int)revision.GetValue(record) == first + 2,
                "Adding a junction must invalidate the cached shape even when the terminals did not move.");
            wires.radialSegments += 2;
            Draw(true);
            Require(mesh.vertexCount == (Mathf.Max(16, wires.curveSegments) + 1) * (wires.radialSegments + 1),
                "Changing wire topology must allocate the correct buffer sizes and rebuild a valid mesh.");
            obj.transform.localScale = Vector3.one * 2f;
            int prior = (int)revision.GetValue(record);
            Draw(true);
            Require((int)revision.GetValue(record) == prior + 1,
                "Changing the wire container transform must refresh world-to-local geometry.");
            results.Add("PASS: wires update immediately after endpoint, junction, segment and container changes; drag updates reuse buffers");
            wires.enabled = false;
            fixture.SetActive(true);
            wires.activeWires.Add(record);
            wires.UpdateActiveWirePositionsExternal();
            int idleRevision = (int)revision.GetValue(record);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 120; i++) wires.UpdateActiveWirePositionsExternal();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Require((int)revision.GetValue(record) == idleRevision && allocated <= 1024,
                "Warm idle wire updates must reuse grouping collections and geometry buffers; allocated bytes=" + allocated);
            a.gameObject.SetActive(false);
            wires.UpdateActiveWirePositionsExternal();
            Require(!obj.activeSelf && wires.activeWires.Count == 1,
                "An invisible endpoint must hide the wire without destroying its cached connection.");
            a.gameObject.SetActive(true);
            wires.UpdateActiveWirePositionsExternal();
            Require(obj.activeSelf && mesh.vertexCount > 0,
                "Restoring endpoint visibility must recover the wire after pooled grouping lists were cleared.");
            results.Add("PASS: 120 warm idle wire updates stay within 1 KB of managed allocations and preserve hide/restore behavior");
        }
        finally
        {
            wires.activeWires.Clear();
            UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(fixture);
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
