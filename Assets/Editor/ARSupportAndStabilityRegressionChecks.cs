using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>Measured-surface policy and bounded presentation checks, not native tracking tests.</summary>
internal static class ARSupportAndStabilityRegressionChecks
{
    public static void Run(List<string> results)
    {
        Require(ARSupportPolicy.CanShareAnchor(TrackingState.Tracking, false, Vector3.zero,
                Vector3.right * 0.1f, new Vector3(0.4f, 0.01f, 0.1f)),
            "Nearby measured contacts on one support must share a tracked anchor, regardless of prefab type.");
        Require(!ARSupportPolicy.CanShareAnchor(TrackingState.Tracking, false, Vector3.zero, Vector3.zero, Vector3.up * 0.75f) &&
            !ARSupportPolicy.CanShareAnchor(TrackingState.Tracking, false, Vector3.up * 0.75f, Vector3.zero, Vector3.right * 0.1f) &&
            !ARSupportPolicy.CanShareAnchor(TrackingState.Tracking, false, Vector3.zero, Vector3.zero, Vector3.right * 1.1f) &&
            !ARSupportPolicy.CanShareAnchor(TrackingState.Limited, false, Vector3.zero, Vector3.zero, Vector3.right * 0.1f) &&
            !ARSupportPolicy.CanShareAnchor(TrackingState.Tracking, true, Vector3.zero, Vector3.zero, Vector3.right * 0.1f) &&
            !ARSupportPolicy.CanShareAnchor(TrackingState.Tracking, false, Vector3.zero, Vector3.zero, Vector3.one * float.NaN),
            "A shared anchor cannot bridge floor/table heights (including old table anchors retaining a dragged floor item), distance, missing tracking, pending anchors or invalid data.");
        results.Add("PASS: nearby same-level measured contacts share anchors; table/floor, distance and native-tracking boundaries remain separate");
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

        Require(ARSupportPolicy.CanCorrectContactHeight(0.45f, 0f) &&
            !ARSupportPolicy.CanCorrectContactHeight(0.75f, 0f) &&
            !ARSupportPolicy.CanCorrectContactHeight(0f, 0.23f) &&
            !ARSupportPolicy.CanCorrectContactHeight(0f, 0.008f) &&
            !ARSupportPolicy.CanCorrectContactHeight(float.NaN, 0f),
            "Contact reconciliation may lower an obsolete floor contact, but cannot cross a tabletop height, amplify jitter or accept invalid data.");
        var contactConfirmation = new ARSupportPolicy.ContactConfirmation();
        Require(!contactConfirmation.Observe(0.45f, 0f, 0f) &&
            !contactConfirmation.Observe(0.45f, 0.002f, 0.3f) &&
            !contactConfirmation.Observe(0.45f, 0.002f, 0.3f) &&
            contactConfirmation.Observe(0.45f, -0.001f, 0.61f),
            "A ground correction requires three distinct observations and a 600ms stable-height dwell.");
        results.Add("PASS: floor contact correction is bounded and needs three stable, distinct measurements rather than cached frames");
        contactConfirmation.Reset();
        Require(!contactConfirmation.Observe(0.45f, 0f, 1f) &&
            !contactConfirmation.Observe(0.45f, 0.04f, 1.7f) &&
            !contactConfirmation.Observe(0.45f, 0.04f, 1.9f),
            "Changing measured heights must restart confirmation instead of completing an obsolete floor correction.");
        contactConfirmation.Reset();
        Require(!contactConfirmation.Observe(0.45f, 0.04f, 2.5f) &&
            !contactConfirmation.Observe(0.45f, float.NaN, 3f) &&
            !contactConfirmation.Observe(0.45f, 0.04f, 3.5f) &&
            !contactConfirmation.Observe(0.45f, 0.04f, 3.8f) &&
            !contactConfirmation.Observe(0.45f, 0.04f, 6f),
            "Misses/invalid data and stale observations must cancel ground contact confirmation.");
        results.Add("PASS: changing/missing/stale support cannot finish an automatic height correction");
        Vector3 oldContact = new Vector3(0.2f, 0.4f, 0.1f);
        Vector3 slopeNormal = Quaternion.Euler(10f, 0f, 0f) * Vector3.up;
        Require(ARSupportPolicy.TryProjectContactVertically(oldContact, Vector3.zero, slopeNormal, out Vector3 foot) &&
            foot.x == oldContact.x && foot.z == oldContact.z &&
            Mathf.Abs(Vector3.Dot(foot, slopeNormal)) < 0.00001f &&
            !ARSupportPolicy.TryProjectContactVertically(oldContact, Vector3.zero, Vector3.right, out _) &&
            !ARSupportPolicy.TryProjectContactVertically(oldContact, Vector3.zero, Vector3.one * float.NaN, out _),
            "Contact correction must project vertically at the object's fixed XZ, never along the changing camera/finger ray.");
        results.Add("PASS: ground projection preserves world XZ and rejects steep/invalid support geometry");

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

            body.transform.position = new Vector3(0.2f, 0.45f, 0.3f);
            Vector3 fixedXZ = body.transform.position;
            Quaternion fixedRotation = body.transform.rotation;
            var stepContact = typeof(TapToPlaceController).GetMethod("StepContactCorrection", BindingFlags.Static | BindingFlags.NonPublic);
            for (int i = 0; i < 10; i++)
            {
                stepContact.Invoke(null, new object[] { body.transform, 0f, 0.05f });
                Tick();
                Require(body.transform.position.x == fixedXZ.x && body.transform.position.z == fixedXZ.z &&
                    body.transform.parent == anchorRoot.transform && Quaternion.Angle(body.transform.rotation, fixedRotation) < 0.001f,
                    "Settling must retain world XZ, orientation and the native anchor hierarchy, including the presentation filter.");
            }
            Require(Mathf.Abs(body.transform.position.y) < 0.0001f,
                "The presentation filter must accept the final authored floor contact rather than restoring its obsolete height.");
            results.Add("PASS: floor settling reaches the measured height without camera-follow, rotation or stabilizer rollback");

            var neighbour = new GameObject("Placed_Neighbour");
            neighbour.transform.SetParent(anchorRoot.transform, false);
            var controller = root.AddComponent<TapToPlaceController>();
            controller.enabled = false;
            var bindings = (Dictionary<GameObject, ARAnchor>)typeof(TapToPlaceController)
                .GetField("objectAnchors", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            bindings[body] = anchor;
            bindings[neighbour] = anchor;
            var destination = new GameObject("SharedDestination");
            destination.transform.SetParent(root.transform, false);
            destination.transform.SetPositionAndRotation(new Vector3(0.4f, 0f, 0.3f), Quaternion.Euler(0f, 25f, 0f));
            var destinationAnchor = destination.AddComponent<ARAnchor>();
            destinationAnchor.enabled = false;
            Pose contact = new Pose(new Vector3(0.6f, 0f, 0.3f), Quaternion.Euler(0f, 90f, 0f));
            Vector3 neighbourBefore = neighbour.transform.position;
            typeof(TapToPlaceController).GetMethod("AdoptSupportAnchor", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { body, destinationAnchor, contact, anchor });
            Require(body.transform.parent == destination.transform && bindings[body] == destinationAnchor &&
                Vector3.Distance(body.transform.position, contact.position) < 0.00001f &&
                Quaternion.Angle(body.transform.rotation, contact.rotation) < 0.001f,
                "Anchor sharing must preserve the measured contact and rotation instead of snapping onto the anchor's origin.");
            Require(anchor != null && neighbour.transform.parent == anchorRoot.transform &&
                neighbour.transform.position == neighbourBefore && bindings[neighbour] == anchor,
                "Reanchoring one member cannot destroy or move a sibling's shared anchor.");
            results.Add("PASS: sharing/reanchoring preserves each measured contact, orientation and the previous anchor's remaining members");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
