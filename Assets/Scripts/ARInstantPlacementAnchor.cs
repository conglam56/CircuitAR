using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Keeps an immediate placement in world space while native tracking catches up.
/// Estimated contacts are never presented to the owner as measured surfaces.
/// </summary>
[DisallowMultipleComponent]
public sealed class ARInstantPlacementAnchor : MonoBehaviour
{
    private const float SurfaceRefinementTimeout = 5f;
    private const float AnchorTimeout = 2f;
    private TapToPlaceController owner;
    private ARRaycastManager raycasts;
    private ARAnchorManager anchors;
    private Camera arCamera;
    private Transform supportRoot;
    private ARRaycast trackedRaycast;
    private Vector2 initialScreenPoint;
    private float initialDistance;
    private float refinementDeadline;
    private float requestDeadline;
    private float promotionNotBefore;
    private float promotionRetryDelay = 0.5f;
    private float nextSupportProbeAt;
    private int generation;
    private bool configured;
    private bool runtimeActive;
    private bool paused;
    private bool refinementStopped;
    private bool usedInitialRay;
    private bool requestInFlight;
    private bool nativeRequestOutstanding;
    private bool promotionExhausted;
    private bool hasMeasuredSurface;

    public bool IsEstimated => !hasMeasuredSurface || requestInFlight;
    public Transform SupportRoot => supportRoot;

    public void Initialize(TapToPlaceController controller, ARRaycastManager raycastManager,
        ARAnchorManager anchorManager, Camera camera, Vector2 screenPoint,
        float estimatedDistance, Transform placementSupport, bool measuredSurface = false)
    {
        CancelActiveWork();
        owner = controller;
        raycasts = raycastManager;
        anchors = anchorManager;
        arCamera = camera;
        initialScreenPoint = screenPoint;
        initialDistance = float.IsNaN(estimatedDistance) || float.IsInfinity(estimatedDistance)
            ? 1f : Mathf.Clamp(estimatedDistance, 0.1f, 10f);
        supportRoot = placementSupport;
        hasMeasuredSurface = measuredSurface;
        promotionRetryDelay = 0.5f;
        promotionNotBefore = 0f;
        nextSupportProbeAt = 0f;
        refinementStopped = false;
        usedInitialRay = false;
        configured = true;
        if (CanRun()) BeginRuntime();
    }

    /// <summary>User manipulation permanently takes precedence over native ray refinement.</summary>
    public void StopRefinement()
    {
        refinementStopped = true;
        CancelActiveWork();
        // The grab caller establishes its drag state later in this frame.
        promotionNotBefore = Time.realtimeSinceStartup + 0.05f;
    }

    private bool CanRun() => configured && isActiveAndEnabled && !paused &&
        owner != null && owner.isActiveAndEnabled && gameObject.activeInHierarchy &&
        ARSession.state == ARSessionState.SessionTracking;

    private bool IsBeingDragged() => owner != null && owner.IsObjectBeingDragged(gameObject);

    private void OnEnable() { runtimeActive = false; }

    private void Update()
    {
        if (!CanRun())
        {
            if (runtimeActive || trackedRaycast != null || requestInFlight) CancelActiveWork();
            return;
        }
        if (IsBeingDragged())
        {
            if (!refinementStopped) StopRefinement();
            return;
        }
        if (!runtimeActive) BeginRuntime();
        if (requestInFlight)
        {
            // Also bounds a provider whose TryAddAnchorAsync has not returned yet.
            // Its eventual result is discarded by the generation check below.
            if (Time.realtimeSinceStartup >= requestDeadline)
            {
                generation++;
                requestInFlight = false;
                SchedulePromotionRetry();
            }
            return;
        }
        // An expired result is discarded by generation, but the provider still
        // owns that request until it returns. Never queue overlapping retries.
        if (nativeRequestOutstanding || Time.realtimeSinceStartup < promotionNotBefore) return;
        if (promotionExhausted) promotionExhausted = false;

        if (trackedRaycast != null)
        {
            if (HasMeasuredHorizontalContact(trackedRaycast) &&
                owner.TryRefineInstantPlacement(gameObject, supportRoot, trackedRaycast.plane))
            {
                hasMeasuredSurface = true;
                RemoveTrackedRaycast();
            }
            else if (Time.realtimeSinceStartup < refinementDeadline) return;
            else RemoveTrackedRaycast();
        }

        // Do not promote an estimated pixel/depth into a native anchor that can
        // keep changing range as the camera retreats. Placement is already visible
        // at a fixed world pose; verify real support independently in the background.
        if (!hasMeasuredSurface)
        {
            if (Time.realtimeSinceStartup >= nextSupportProbeAt)
            {
                nextSupportProbeAt = Time.realtimeSinceStartup + 0.25f;
                hasMeasuredSurface = owner.TryConfirmFixedInstantContact(gameObject, supportRoot, !refinementStopped);
            }
            if (!hasMeasuredSurface) return;
        }

        if (anchors == null || !anchors.isActiveAndEnabled || anchors.subsystem == null ||
            !anchors.subsystem.running) return;
        requestInFlight = true;
        nativeRequestOutstanding = true;
        requestDeadline = Time.realtimeSinceStartup + AnchorTimeout;
        // A drag moves the child rather than its old support. Anchor the authored
        // object pose in that case, preserving its world pose during adoption.
        Transform reference = !refinementStopped && supportRoot != null ? supportRoot : transform;
        PromoteAsync(new Pose(reference.position, reference.rotation), generation,
            transform.parent, transform.localPosition, transform.localRotation);
    }

    private void BeginRuntime()
    {
        runtimeActive = true;
        promotionExhausted = false;
        if (refinementStopped || hasMeasuredSurface || raycasts == null ||
            !raycasts.isActiveAndEnabled || raycasts.subsystem == null ||
            !raycasts.subsystem.running || raycasts.descriptor == null ||
            !raycasts.descriptor.supportsTrackedRaycasts) return;

        Vector2 screenPoint = initialScreenPoint;
        float distance = initialDistance;
        if (usedInitialRay)
        {
            // The original pixel no longer identifies this contact after a pause
            // or camera movement. Reproject the fixed world point on recovery.
            if (arCamera == null) return;
            Vector3 worldPoint = supportRoot != null ? supportRoot.position : transform.position;
            Vector3 screen = arCamera.WorldToScreenPoint(worldPoint);
            if (!IsFinite(screen) || screen.z <= 0f || !arCamera.pixelRect.Contains(screen)) return;
            screenPoint = screen;
            distance = Vector3.Distance(arCamera.transform.position, worldPoint);
        }
        usedInitialRay = true;
        try
        {
            trackedRaycast = raycasts.AddRaycast(screenPoint, distance);
            refinementDeadline = Time.realtimeSinceStartup + SurfaceRefinementTimeout;
        }
        catch (System.Exception)
        {
            // Providers can advertise tracked raycasts but reject this request.
            // The immediate world-space contact still supports a session anchor.
            RemoveTrackedRaycast();
        }
    }

    private static bool HasMeasuredHorizontalContact(ARRaycast raycast)
    {
        ARPlane plane = raycast.plane;
        return !raycast.pending && raycast.trackingState == TrackingState.Tracking &&
            plane != null && !plane.pending && plane.subsumedBy == null &&
            plane.trackingState == TrackingState.Tracking &&
            plane.alignment == PlaneAlignment.HorizontalUp &&
            Vector3.Dot(plane.transform.up, Vector3.up) >= 0.9659258f &&
            IsFinite(raycast.transform.position);
    }

    private bool IsCurrentRequest(int request, Transform parent, Vector3 localPosition, Quaternion localRotation) =>
        this != null && request == generation && CanRun() && !IsBeingDragged() &&
        transform.parent == parent && (transform.localPosition - localPosition).sqrMagnitude < 0.00000001f &&
        Quaternion.Angle(transform.localRotation, localRotation) < 0.1f &&
        Time.realtimeSinceStartup < requestDeadline;

    private async void PromoteAsync(Pose pose, int request, Transform parent,
        Vector3 localPosition, Quaternion localRotation)
    {
        ARAnchor replacement = null;
        try
        {
            var result = await anchors.TryAddAnchorAsync(pose);
            if (result.status.IsSuccess()) replacement = result.value;
            while (replacement != null && IsCurrentRequest(request, parent, localPosition, localRotation) &&
                (replacement.pending || replacement.trackingState != TrackingState.Tracking))
                await Awaitable.NextFrameAsync();
            if (replacement == null || !IsCurrentRequest(request, parent, localPosition, localRotation) ||
                replacement.pending || replacement.trackingState != TrackingState.Tracking) return;

            Transform previousSupport = supportRoot;
            transform.SetParent(replacement.transform, true);
            ARAnchor adopted = replacement;
            replacement = null; // Ownership transfers before any owner callback.
            supportRoot = null;
            RemoveEmptySupport(previousSupport);
            owner.AdoptInstantPlacementAnchor(gameObject, adopted, hasMeasuredSurface);
            configured = false;
            Destroy(this);
        }
        catch (System.Exception ex)
        {
            if (this != null && request == generation)
                Debug.LogWarning("[ARPlacement] Native anchor unavailable; retaining the placed position: " + ex.Message);
        }
        finally
        {
            nativeRequestOutstanding = false;
            if (replacement != null) Destroy(replacement.gameObject);
            if (this != null && request == generation && configured)
            {
                requestInFlight = false;
                SchedulePromotionRetry();
            }
        }
    }

    private void RemoveTrackedRaycast()
    {
        ARRaycast previous = trackedRaycast;
        trackedRaycast = null;
        if (previous == null) return;
        try { if (raycasts != null) raycasts.RemoveRaycast(previous); }
        catch (System.Exception) { /* Session shutdown may have already stopped the provider. */ }
        // Native removal normally destroys this next subsystem update; explicit
        // destruction also covers a stopped/disabled manager with no next update.
        if (previous != null) Destroy(previous.gameObject);
    }

    private void SchedulePromotionRetry()
    {
        promotionExhausted = true;
        promotionNotBefore = Time.realtimeSinceStartup + promotionRetryDelay;
        promotionRetryDelay = Mathf.Min(4f, promotionRetryDelay * 2f);
    }

    private void CancelActiveWork()
    {
        generation++;
        requestInFlight = false;
        runtimeActive = false;
        RemoveTrackedRaycast();
    }

    private void OnDisable() { CancelActiveWork(); }

    private void OnApplicationPause(bool value)
    {
        paused = value;
        if (value) CancelActiveWork();
    }

    private void OnDestroy()
    {
        CancelActiveWork();
        RemoveEmptySupportAfterFrame(supportRoot);
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
        !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

    private static void RemoveEmptySupport(Transform support)
    {
        if (support != null && support.childCount == 0 && support.GetComponent<ARAnchor>() == null)
        {
            if (Application.isPlaying) Destroy(support.gameObject);
            else DestroyImmediate(support.gameObject);
        }
    }

    private static async void RemoveEmptySupportAfterFrame(Transform support)
    {
        if (support == null) return;
        if (!Application.isPlaying)
        {
            RemoveEmptySupport(support);
            return;
        }
        // OnDestroy of the child precedes its Transform being removed from the
        // parent. Waiting one frame also avoids destroying a live undo target.
        try { await Awaitable.NextFrameAsync(); }
        catch (System.OperationCanceledException) { return; }
        RemoveEmptySupport(support);
    }
}
