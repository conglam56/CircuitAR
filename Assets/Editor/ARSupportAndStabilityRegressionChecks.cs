using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

/// <summary>Measured-surface policy and bounded presentation checks, not native tracking tests.</summary>
internal static class ARSupportAndStabilityRegressionChecks
{
    public static void Run(List<string> results)
    {
        Pose table = new Pose(Vector3.up * 0.75f, Quaternion.identity);
        Pose floor = Pose.identity;
        Require(ARSupportPolicy.PreferMeasuredDepth(table, floor) &&
            ARSupportPolicy.PreferMeasuredDepth(floor, table),
            "A finite measured floor/table must override a conflicting broad estimated plane, in either height direction.");
        Require(!ARSupportPolicy.PreferMeasuredDepth(table, new Pose(Vector3.up * 0.76f, Quaternion.identity)) &&
            !ARSupportPolicy.PreferMeasuredDepth(table, new Pose(Vector3.one * float.NaN, Quaternion.identity)),
            "Small co-planar measurement noise must retain the plane anchor, and invalid depth cannot win.");
        results.Add("PASS: validated depth overrides conflicting table/floor height, not co-planar noise or invalid data");

        var level = new ARSupportPolicy.LevelTransition();
        Vector2 pointer = new Vector2(400f, 500f);
        level.Begin(0.75f);
        Require(level.Accept(table.position, pointer, 0f) && !level.IsPending,
            "Normal same-level dragging must not incur a dwell delay.");
        Require(!level.Accept(floor.position, pointer, 1f) && level.IsPending &&
            !level.Accept(floor.position, pointer, 1.34f) &&
            level.Accept(floor.position, pointer, 1.36f) && !level.IsPending &&
            level.Accept(Vector3.right * 0.02f, pointer + Vector2.right * 10f, 1.37f),
            "A deliberate held floor target must change the accepted support height after 350 ms, not remain trapped at table height.");
        results.Add("PASS: table-to-floor drag changes height only after a stable measured-target hold and then follows the new level immediately");

        level.Begin(0.75f);
        Require(!level.Accept(floor.position, pointer, 2f) &&
            !level.Accept(floor.position, pointer + Vector2.right * 60f, 2.3f) &&
            !level.Accept(floor.position, pointer + Vector2.right * 60f, 2.5f),
            "Moving the pointer away must restart the floor-switch timer.");
        level.CancelPending();
        Require(!level.Accept(floor.position, pointer, 3f), "A missed support must cancel its pending dwell.");
        Require(!level.Accept(Vector3.one * float.NaN, pointer, 4f) && !level.IsPending &&
            !level.Accept(floor.position, new Vector2(float.NaN, 0f), 4f),
            "Invalid target/pointer values cannot finish a support transition.");
        results.Add("PASS: support switching is canceled by misses, moving pointers and invalid geometry instead of extrapolating into air");

        var filter = new ARSupportPolicy.PoseDeadband();
        Require(filter.Apply(floor, false).position == Vector3.zero,
            "The first tracked pose must be presented immediately.");
        Pose jitter = new Pose(Vector3.right * 0.001f, Quaternion.Euler(0f, 0.2f, 0f));
        Pose held = filter.Apply(jitter, false);
        Require(held.position == floor.position && Quaternion.Angle(held.rotation, floor.rotation) < 0.001f,
            "Micro-position/rotation noise should not shake the presentation.");
        Pose correction = new Pose(new Vector3(0.03f, -0.02f, 0f), Quaternion.Euler(0f, 3f, 0f));
        Pose corrected = filter.Apply(correction, false);
        Require(corrected.position == correction.position && Quaternion.Angle(corrected.rotation, correction.rotation) < 0.001f,
            "A real SLAM correction must snap immediately, not lerp behind the camera or stay at the old floor height.");
        Require(filter.Apply(jitter, true).position == jitter.position,
            "Tracking recovery must force a native-pose resynchronization.");
        results.Add("PASS: only sub-2mm/sub-0.3degree anchor noise is held; real pose corrections and recovery snap immediately");

        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("ARBoundedStabilityFixture");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false);
        try
        {
            var anchorRoot = new GameObject("ManagedAnchor");
            anchorRoot.transform.SetParent(root.transform, false);
            var anchor = anchorRoot.AddComponent<ARAnchor>();
            anchor.enabled = false;
            var body = new GameObject("Placed_Fixture");
            body.transform.SetParent(anchorRoot.transform, false);
            var stabilizer = body.AddComponent<ARPlacedObjectStabilizer>();
            stabilizer.enabled = false;
            root.SetActive(true);
            stabilizer.Bind(null, anchor);
            var update = typeof(ARPlacedObjectStabilizer).GetMethod("UpdatePresentation", BindingFlags.Instance | BindingFlags.NonPublic);
            void Tick(bool tracked = true) => update.Invoke(stabilizer, new object[] { tracked });
            Tick();
            anchorRoot.transform.position = Vector3.right * 0.001f;
            Tick();
            Require(body.transform.position.sqrMagnitude < 0.00000001f && body.transform.parent == anchorRoot.transform,
                "Presentation noise suppression must retain the genuine anchor hierarchy.");
            anchorRoot.transform.position = Vector3.right * 0.01f;
            Tick();
            Require(Vector3.Distance(body.transform.position, anchorRoot.transform.position) < 0.00001f,
                "Cumulative drift must not stick inside a series of ignored per-frame deltas.");
            body.transform.localPosition = Vector3.forward * 0.1f;
            body.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Vector3 edited = body.transform.position;
            Tick();
            Require(Vector3.Distance(body.transform.position, edited) < 0.00001f &&
                Quaternion.Angle(body.transform.rotation, Quaternion.Euler(0f, 90f, 0f)) < 0.01f,
                "Dragging, undo/redo and manual rotation must rebind rather than get undone by the stabilizer.");
            stabilizer.SuspendFiltering = true;
            anchorRoot.transform.position += Vector3.right * 0.001f;
            Vector3 native = body.transform.position;
            Tick();
            Require(body.transform.position == native, "An anchor handoff must temporarily bypass micro filtering.");
            stabilizer.SuspendFiltering = false;
            Tick(false);
            anchorRoot.transform.position += Vector3.up * 0.05f;
            native = body.transform.position;
            Tick();
            Require(Vector3.Distance(body.transform.position, native) < 0.00001f,
                "Recovery must immediately adopt the current native height without smoothing it through the floor.");
            results.Add("PASS: anchored presentation preserves hierarchy, cumulative corrections, undo/rotation and native-anchor handoff/recovery");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
