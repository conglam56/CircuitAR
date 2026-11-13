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
            native = frozen + Vector3.up * 0.05f;
            Tick();
            Require(Vector3.Distance(body.transform.position, native) < 0.00001f,
                "Recovery must immediately adopt the current native height without smoothing it through the floor.");
            results.Add("PASS: anchored presentation preserves hierarchy and edits, freezes during tracking loss and resynchronizes on recovery");

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
            CheckNativeCoordinateCorrections(root, results);
            CheckUnregisteredAnchorOwnership(root, results);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void CheckNativeCoordinateCorrections(GameObject root, List<string> results)
    {
        var fixture = new GameObject("NativeCoordinateFrameFixture");
        fixture.transform.SetParent(root.transform, false);
        fixture.SetActive(false);
        var native = new GameObject("NativeTrackedAnchor");
        native.transform.SetParent(fixture.transform, false);
        native.transform.position = new Vector3(0.2f, 0.75f, 1f);
        var anchor = native.AddComponent<ARAnchor>();
        anchor.enabled = false;
        var body = new GameObject("Placed_NativeFrameTarget");
        body.transform.SetParent(native.transform, false);
        var cameraObject = new GameObject("TrackingCamera");
        cameraObject.transform.SetParent(fixture.transform, false);
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = new Vector3(0f, 1.6f, 0f);
        var owner = fixture.AddComponent<TapToPlaceController>();
        owner.enabled = false;
        fixture.SetActive(true);
        try
        {
            typeof(TapToPlaceController).GetField("mainCamera", Private).SetValue(owner, camera);
            typeof(TapToPlaceController).GetMethod("AdoptInstantPlacementAnchor", Private).Invoke(owner, new object[] { body, anchor, true });
            var stabilizer = body.GetComponent<ARPlacedObjectStabilizer>();
            stabilizer.enabled = false;
            var update = typeof(ARPlacedObjectStabilizer).GetMethod("UpdatePresentation", Private);
            void Tick(bool tracked) => update.Invoke(stabilizer, new object[] { tracked });
            void CorrectFrame(Vector3 offset, Quaternion rotation)
            {
                camera.transform.SetPositionAndRotation(rotation * camera.transform.position + offset, rotation * camera.transform.rotation);
                native.transform.SetPositionAndRotation(rotation * native.transform.position + offset, rotation * native.transform.rotation);
            }
            Tick(true);
            Vector3 cameraSpace = camera.worldToCameraMatrix.MultiplyPoint3x4(body.transform.position);
            CorrectFrame(new Vector3(0.35f, 0.15f, -0.25f), Quaternion.Euler(0f, 20f, 0f));
            Tick(true);
            Vector3 corrected = camera.worldToCameraMatrix.MultiplyPoint3x4(body.transform.position);
            Require(Vector3.Distance(cameraSpace, corrected) < 0.00001f,
                "A shared native camera/anchor coordinate correction must preserve camera-relative registration; freezing Unity world coordinates makes the object slide over the real scene.");
            results.Add("PASS: common AR camera/anchor corrections preserve camera-relative registration instead of freezing an obsolete Unity world pose");
            Vector3 world = body.transform.position;
            camera.transform.position -= camera.transform.forward * 0.8f;
            Tick(true);
            Require(Vector3.Distance(body.transform.position, world) < 0.00001f,
                "Physical camera retreat alone must leave a released object's native-anchor contact unchanged.");
            results.Add("PASS: physical camera movement alone keeps the released object on its native contact");
            cameraSpace = camera.worldToCameraMatrix.MultiplyPoint3x4(body.transform.position);
            Tick(false);
            CorrectFrame(new Vector3(-0.4f, 0.1f, 0.15f), Quaternion.Euler(0f, -15f, 0f));
            Tick(false);
            Tick(true);
            corrected = camera.worldToCameraMatrix.MultiplyPoint3x4(body.transform.position);
            Require(Vector3.Distance(cameraSpace, corrected) < 0.00001f,
                "Relocalization must resume the recovered native coordinate frame rather than retain a stale pre-loss Unity pose.");
            results.Add("PASS: tracking recovery restores native-frame registration after a shared camera/anchor relocalization");
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
    }

    private static void CheckUnregisteredAnchorOwnership(GameObject root, List<string> results)
    {
        var fixture = new GameObject("UnregisteredAnchorFixture");
        fixture.transform.SetParent(root.transform, false);
        fixture.SetActive(false);
        var manager = fixture.AddComponent<ARAnchorManager>();
        manager.enabled = false;
        var owner = fixture.AddComponent<TapToPlaceController>();
        owner.enabled = false;
        owner.arAnchorManager = manager;
        var native = new GameObject("NativeAnchorAwaitingRegistration");
        native.transform.SetParent(fixture.transform, false);
        var anchor = native.AddComponent<ARAnchor>();
        anchor.enabled = false;
        var body = new GameObject("Placed_NativeOwnership");
        body.transform.SetParent(native.transform, false);
        body.transform.localPosition = new Vector3(0.1f, 0.75f, 1f);
        fixture.SetActive(true);
        try
        {
            typeof(TapToPlaceController).GetMethod("AdoptInstantPlacementAnchor", Private)
                .Invoke(owner, new object[] { body, anchor, true });
            var stabilizer = body.GetComponent<ARPlacedObjectStabilizer>();
            stabilizer.enabled = false;
            typeof(ARPlacedObjectStabilizer).GetMethod("LateUpdate", Private).Invoke(stabilizer, null);
            Require(anchor != null && body.transform.parent == native.transform &&
                body.GetComponent<ARInstantPlacementAnchor>() == null,
                "A missing manager lookup alone must not detach content or start a replacement-anchor loop; it is not a native removal event.");
            results.Add("PASS: a temporarily unregistered native anchor keeps ownership without detaching content or starting replacement-anchor retries");
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
