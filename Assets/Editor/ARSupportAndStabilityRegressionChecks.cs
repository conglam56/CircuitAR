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

        Require(ARSupportPolicy.CanCorrectContactHeight(0.008f, 0f) &&
            ARSupportPolicy.CanCorrectContactHeight(0.45f, 0f) &&
            !ARSupportPolicy.CanCorrectContactHeight(0.75f, 0f) &&
            !ARSupportPolicy.CanCorrectContactHeight(0f, 0.23f) &&
            !ARSupportPolicy.CanCorrectContactHeight(0f, 0.02f) &&
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

        var recoveryCheck = new ARSupportPolicy.RecoveryConfirmation();
        Require(!recoveryCheck.Observe(table, 0f) && !recoveryCheck.Observe(table, 0.13f) &&
            recoveryCheck.Observe(table, 0.26f), "Recovery must require distinct observations spanning 250ms.");
        recoveryCheck.Reset();
        Require(!recoveryCheck.Observe(table, 1f) && !recoveryCheck.Observe(table, 1f) &&
            !recoveryCheck.Observe(new Pose(Vector3.up * 0.9f, Quaternion.identity), 1.3f) &&
            !recoveryCheck.Observe(new Pose(Vector3.one * float.NaN, Quaternion.identity), 1.6f),
            "Duplicate timestamps, jumping poses and invalid data cannot confirm a recovered contact.");
        results.Add("PASS: tracking recovery needs consistent finite observations across 250ms instead of accepting its first pose");

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
        results.Add("PASS: micro anchor noise is held; large corrections and recovery snap immediately");
        filter.Reset();
        filter.Apply(floor, false);
        Pose medium = new Pose(Vector3.right * 0.01f, Quaternion.Euler(0f, 1f, 0f));
        Pose firstStep = filter.Apply(medium, false, 1f / 60f);
        Require(firstStep.position.x > 0f && firstStep.position.x < 0.01f,
            "A medium anchor correction must be damped instead of snapping by a centimetre.");
        for (int i = 0; i < 30; i++) held = filter.Apply(medium, false, 1f / 60f);
        Require(Vector3.Distance(held.position, medium.position) <= 0.0031f &&
            Quaternion.Angle(held.rotation, medium.rotation) <= 0.36f,
            "Damping must converge within the deadband instead of hiding cumulative movement.");
        filter.Reset();
        filter.Apply(floor, false);
        for (int i = 0; i < 60; i++)
        {
            held = filter.Apply(new Pose(Vector3.right * (i % 2 == 0 ? 0.008f : -0.008f), Quaternion.identity),
                false, 1f / 60f);
            Require(Mathf.Abs(held.position.x) < 0.003f,
                "Alternating 8mm native jitter must not make a resting component jump between both extremes.");
        }
        results.Add("PASS: medium anchor jitter is damped, sustained movement converges and alternating native noise stays bounded");

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
            Require(stabilizer.AuthoredContactLocalPosition == Vector3.zero,
                "Presentation offsets must not be mistaken for authored ground-contact changes.");
            anchorRoot.transform.position = Vector3.right * 0.01f;
            Tick();
            Require(body.transform.position.x >= 0f && body.transform.position.x < 0.01f,
                "A moderate native update must not jump the resting presentation to the full tracked displacement.");
            anchorRoot.transform.position = Vector3.right * 0.04f;
            Tick();
            Require(Vector3.Distance(body.transform.position, anchorRoot.transform.position) < 0.00001f,
                "Cumulative drift beyond the snap boundary must immediately resynchronize.");
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
            Vector3 frozen = body.transform.position;
            for (int i = 0; i < 4; i++)
            {
                anchorRoot.transform.position += Vector3.up * 0.0125f;
                Tick(false);
                Require(Vector3.Distance(body.transform.position, frozen) < 0.00001f,
                    "A lost anchor must not move the displayed component through inherited parent motion.");
            }
            Require(Vector3.Distance(stabilizer.AuthoredContactLocalPosition, Vector3.forward * 0.1f) < 0.00001f,
                "Freezing a lost anchor must preserve the authored contact coordinate rather than baking in its drift.");
            for (int i = 0; i < 5; i++) Tick();
            Require(Vector3.Distance(body.transform.position, frozen) < 0.00001f && stabilizer.IsRecovering,
                "A recovered tracking flag alone cannot lift a resting object to an unverified native height.");
            anchorRoot.transform.position += Vector3.forward * 2f;
            Tick();
            Require(Vector3.Distance(body.transform.position, frozen) < 0.00001f,
                "An unverified recovery jump must not teleport the component out of view.");
            results.Add("PASS: anchored presentation preserves edits, freezes during loss and rejects unsupported recovery height/position jumps");

            body.transform.position = new Vector3(0.2f, 0.45f, 0.3f);
            Vector3 fixedXZ = body.transform.position;
            Quaternion fixedRotation = body.transform.rotation;
            var stepContact = typeof(TapToPlaceController).GetMethod("StepContactCorrection", BindingFlags.Static | BindingFlags.NonPublic);
            stepContact.Invoke(null, new object[] { body.transform, 0.47f, 0.05f });
            Require(body.transform.position == fixedXZ, "A contact correction cannot lift the component.");
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
            var supportId = new TrackableId(991, 4);
            typeof(TapToPlaceController).GetMethod("RememberPlacedContact", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { body, supportId });
            typeof(TapToPlaceController).GetMethod("AdoptInstantPlacementAnchor", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { body, destinationAnchor, true });
            var contacts = (System.Collections.IDictionary)typeof(TapToPlaceController)
                .GetField("placedContacts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            object remembered = contacts[body];
            Require((TrackableId)remembered.GetType().GetField("planeId").GetValue(remembered) == supportId,
                "Native promotion must retain the measured plane binding so the next drag can use its immediate support path.");
            results.Add("PASS: native anchor promotion retains the measured support plane for responsive subsequent drags");
            CheckTrackingRecovery(root, results);
            CheckRemovedAnchorRetention(root, results);

        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void CheckTrackingRecovery(GameObject root, List<string> results)
    {
        var fixture = new GameObject("RecoveryPlaneFixture");
        fixture.transform.SetParent(root.transform, false);
        fixture.SetActive(false);
        var planes = fixture.AddComponent<ARPlaneManager>();
        planes.enabled = false;
        var controller = fixture.AddComponent<TapToPlaceController>();
        controller.enabled = false;
        controller.arPlaneManager = planes;
        var id = new TrackableId(992, 2);
        var polygon = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
        var plane = (ARPlane)typeof(ARPlacementRegressionChecks).GetMethod("CreateTrackedPlane", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { fixture, planes, id, polygon, TrackingState.Tracking, PlaneAlignment.HorizontalUp });
        plane.transform.position = Vector3.up * 0.75f;
        var parent = new GameObject("NativeRecoveryAnchor");
        parent.transform.SetParent(fixture.transform, false);
        parent.transform.position = new Vector3(0.2f, 0.75f, 0.3f);
        var anchor = parent.AddComponent<ARAnchor>();
        anchor.enabled = false;
        var body = new GameObject("Placed_RecoveryContact");
        body.transform.SetParent(parent.transform, false);
        var stabilizer = body.AddComponent<ARPlacedObjectStabilizer>();
        stabilizer.enabled = false;
        fixture.SetActive(true);
        var sessionState = typeof(ARSession).GetField("s_State", BindingFlags.Static | BindingFlags.NonPublic);
        object previousState = sessionState.GetValue(null);
        sessionState.SetValue(null, ARSessionState.SessionTracking);
        try
        {
            typeof(TapToPlaceController).GetMethod("AdoptInstantPlacementAnchor", Private).Invoke(controller, new object[] { body, anchor, true });
            typeof(TapToPlaceController).GetMethod("RememberPlacedContact", Private).Invoke(controller, new object[] { body, id });
            var update = typeof(ARPlacedObjectStabilizer).GetMethod("UpdatePresentation", Private);
            void Tick(bool tracked) => update.Invoke(stabilizer, new object[] { tracked });
            Tick(true);
            Pose retained = new Pose(body.transform.position, body.transform.rotation);
            Tick(false);
            parent.transform.position += Vector3.up * 0.35f;
            Tick(true);
            Require(stabilizer.IsRecovering && body.transform.position == retained.position,
                "The first recovered 35cm native height jump must stay frozen on the last tabletop contact.");
            var confirmation = typeof(ARPlacedObjectStabilizer).GetField("recovery", Private).GetValue(stabilizer);
            var type = confirmation.GetType();
            Pose expected = new Pose(new Vector3(retained.position.x, plane.transform.position.y + controller.surfaceContactOffset, retained.position.z), retained.rotation);
            type.GetField("pending", Private).SetValue(confirmation, true);
            type.GetField("first", Private).SetValue(confirmation, expected);
            type.GetField("startedAt", Private).SetValue(confirmation, Time.unscaledTime - 0.3f);
            type.GetField("lastAt", Private).SetValue(confirmation, Time.unscaledTime - 0.1f);
            type.GetField("observations", Private).SetValue(confirmation, 2);
            Tick(true);
            Require(!stabilizer.IsRecovering && Vector3.Distance(body.transform.position, expected.position) < 0.00001f,
                "A consistent recovered contact must resolve onto its own measured table, not the raised native origin.");
            Tick(true);
            Require(!stabilizer.IsRecovering && Vector3.Distance(body.transform.position, expected.position) < 0.00001f,
                "Rebinding the corrected contact must prevent the native height error returning on the next frame.");
            results.Add("PASS: recovery rejects the first native height jump, confirms its measured tabletop and keeps that corrected binding on later frames");
            var retreatCamera = new GameObject("RetreatCamera");
            retreatCamera.transform.SetParent(fixture.transform, false);
            var camera = retreatCamera.AddComponent<Camera>();
            camera.enabled = false;
            typeof(TapToPlaceController).GetField("mainCamera", Private).SetValue(controller, camera);
            for (int i = 0; i < 8; i++)
            {
                camera.transform.position += Vector3.back * 0.08f;
                parent.transform.position += Vector3.right * 0.01f;
                Tick(true);
                Require(Vector3.Distance(body.transform.position, expected.position) < 0.003f,
                    "Repeated small native corrections must not accumulate into camera-follow after the object was released.");
            }
            parent.transform.position -= Vector3.right * 0.08f;
            Tick(true);
            results.Add("PASS: eight small native drifts during camera retreat cannot pull a released component away from its committed support");
            Tick(false);
            parent.transform.position += Vector3.forward * 0.5f; // Still inside the same large polygon.
            Tick(true);
            Require(stabilizer.IsRecovering && Vector3.Distance(body.transform.position, expected.position) < 0.00001f,
                "Being above the same table does not authorize a 50cm anchor jump away from its recorded contact.");
            parent.transform.position -= Vector3.forward * 0.5f;
            parent.transform.position += Vector3.right * 0.5f;
            plane.transform.position += Vector3.right * 0.5f;
            Tick(true);
            expected.position += Vector3.right * 0.5f;
            type.GetField("pending", Private).SetValue(confirmation, true);
            type.GetField("first", Private).SetValue(confirmation, expected);
            type.GetField("startedAt", Private).SetValue(confirmation, Time.unscaledTime - 0.3f);
            type.GetField("lastAt", Private).SetValue(confirmation, Time.unscaledTime - 0.1f);
            type.GetField("observations", Private).SetValue(confirmation, 2);
            Tick(true);
            Require(!stabilizer.IsRecovering && Vector3.Distance(body.transform.position, expected.position) < 0.00001f,
                "A consistent common correction of both the anchor and its recorded plane contact must still be accepted.");
            var tilted = new object[] { body, new Pose(expected.position, Quaternion.Euler(35f, 0f, 0f)), expected, Pose.identity };
            Require(!(bool)typeof(TapToPlaceController).GetMethod("TryConstrainRecoveredContact", Private).Invoke(controller, tilted),
                "A tilted native pose cannot flip an upright component off its measured horizontal support.");
            results.Add("PASS: recovery distinguishes a native-only teleport from a legitimate shared plane/anchor correction and rejects unsupported tilt");
            typeof(ARPlacementRegressionChecks).GetMethod("SetPlaneTracking", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { plane, id, TrackingState.Limited, PlaneAlignment.HorizontalUp });
            Tick(false);
            parent.transform.position += Vector3.forward * 2f;
            Tick(true);
            Require(stabilizer.IsRecovering && Vector3.Distance(body.transform.position, expected.position) < 0.00001f,
                "A limited/out-of-bound plane cannot authorize a recovered native teleport.");
            results.Add("PASS: recovery cannot use a lost or out-of-bound support plane to move an object out of view");
        }
        finally
        {
            sessionState.SetValue(null, previousState);
            if (plane != null) typeof(ARPlane).GetMethod("DisposeNativeContainers", Private).Invoke(plane, null);
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void CheckRemovedAnchorRetention(GameObject root, List<string> results)
    {
        var fixture = new GameObject("RemovedAnchorFixture");
        fixture.transform.SetParent(root.transform, false);
        fixture.SetActive(false);
        var manager = fixture.AddComponent<ARAnchorManager>();
        manager.enabled = false;
        var controller = fixture.AddComponent<TapToPlaceController>();
        controller.enabled = false;
        controller.arAnchorManager = manager;
        var nativeRoot = new GameObject("NativeAnchorToRemove");
        nativeRoot.transform.SetParent(fixture.transform, false);
        var anchor = nativeRoot.AddComponent<ARAnchor>();
        anchor.enabled = false;
        var bodies = new List<GameObject>();
        var original = new List<Pose>();
        for (int i = 0; i < 3; i++)
        {
            var body = new GameObject("Placed_Retained_" + i);
            body.transform.SetParent(nativeRoot.transform, false);
            body.transform.localPosition = new Vector3(i * 0.2f, 0.75f, 1f);
            bodies.Add(body);
            original.Add(new Pose(body.transform.position, body.transform.rotation));
            if (i == 2) body.SetActive(false); // An undone/deleted history target must stay inactive.
        }
        fixture.SetActive(true);
        var sessionState = typeof(ARSession).GetField("s_State", BindingFlags.Static | BindingFlags.NonPublic);
        object previousState = sessionState.GetValue(null);
        sessionState.SetValue(null, ARSessionState.SessionTracking);
        try
        {
            foreach (var body in bodies)
                typeof(TapToPlaceController).GetMethod("AdoptInstantPlacementAnchor", Private).Invoke(controller, new object[] { body, anchor, true });
            controller.placedObjectsHistory.Add(bodies[0]);
            controller.placedObjectsHistory.Add(bodies[1]);
            controller.History.RecordAction(new PlaceComponentAction(bodies[0], controller));
            Require(!anchor.destroyOnRemoval, "Owned anchors must opt out of provider-driven recursive content destruction.");
            var secondInput = new GameObject("RecoverySecondInput");
            secondInput.transform.SetParent(fixture.transform, false);
            var input = secondInput.AddComponent<ButtonInteractor>();
            input.enabled = false;
            controller.SetSecondHand(input);
            foreach (string name in new[] { "draggedObject", "isDragging", "dragStartPos", "dragStartRot", "lastValidDragPos", "lastValidDragRot" })
            {
                object value = name == "draggedObject" ? bodies[0] : name == "isDragging" ? (object)true :
                    name.EndsWith("Rot") ? original[0].rotation : (object)original[0].position;
                typeof(TapToPlaceController).GetField(name, Private).SetValue(controller, value);
            }
            object secondState = typeof(TapToPlaceController).GetField("secondDrag", Private).GetValue(controller);
            foreach (string name in new[] { "draggedObject", "isDragging", "dragStartPos", "dragStartRot", "lastValidDragPos", "lastValidDragRot" })
            {
                object value = name == "draggedObject" ? bodies[1] : name == "isDragging" ? (object)true :
                    name.EndsWith("Rot") ? original[1].rotation : (object)original[1].position;
                secondState.GetType().GetField(name).SetValue(secondState, value);
            }
            nativeRoot.transform.position += Vector3.up * 0.4f;
            var removed = new Unity.XR.CoreUtils.Collections.ReadOnlyList<KeyValuePair<TrackableId, ARAnchor>>(
                new List<KeyValuePair<TrackableId, ARAnchor>> { new(anchor.trackableId, anchor) });
            manager.trackablesChanged.Invoke(new ARTrackablesChangedEventArgs<ARAnchor>(null, null, removed));
            for (int i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i];
                Require(body != null && Vector3.Distance(body.transform.position, original[i].position) < 0.00001f &&
                    body.activeSelf == (i != 2), "Native removal must preserve every shared member's last presented pose and authored active state.");
                var recovery = body.GetComponent<ARInstantPlacementAnchor>();
                Require(recovery != null && recovery.SupportRoot == body.transform.parent &&
                    recovery.SupportRoot.GetComponent<ARAnchor>() == null &&
                    (bool)typeof(ARInstantPlacementAnchor).GetField("refinementStopped", Private).GetValue(recovery),
                    "Retained members must retry a fresh world anchor without reusing an old screen pixel.");
            }
            Require(controller.placedObjectsHistory.Count == 2 && controller.History.UndoCount == 1,
                "Provider removal must not delete objects from active placement or undo history.");
            Require(!controller.IsObjectBeingDragged(bodies[0]) && !controller.IsObjectBeingDragged(bodies[1]),
                "Both held objects must release onto their last valid contacts when their shared native anchor is removed.");
            results.Add("PASS: anchor removal during simultaneous two-hand drags restores both valid contacts without recording a phantom jump");
            Require(controller.UndoLastPlacedComponent() && !bodies[0].activeSelf && controller.RedoLastAction() && bodies[0].activeSelf,
                "Undo/redo must keep working on the retained object reference after its native parent disappears.");
            results.Add("PASS: removed shared anchors retain active and undone components at their last poses and retry native binding without replaying stale rays");
            results.Add("PASS: native anchor removal preserves placement history and existing undo/redo object references");
        }
        finally
        {
            sessionState.SetValue(null, previousState);
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
