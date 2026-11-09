using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>Keeps cursor/UI rendering independent of the AR camera's capture cadence.</summary>
public sealed class ARPlacementRenderPolicy
{
    private const int RenderFramesPerSecond = 60;
    private ARSession session;
    private bool previousMatchFrameRate;
    private int previousTargetFrameRate;
    public bool IsActive { get; private set; }

    public void Apply(ARSession arSession)
    {
        if (IsActive)
        {
            if (session == arSession) return;
            Restore();
        }

        previousTargetFrameRate = Application.targetFrameRate;
        session = arSession;
        IsActive = true;
        if (session != null)
        {
            previousMatchFrameRate = session.matchFrameRateRequested;
            // ARFoundation's matching mode blocks Update for the next native
            // camera frame and rewrites targetFrameRate to that capture rate.
            session.matchFrameRateRequested = false;
        }
        Application.targetFrameRate = RenderFramesPerSecond;
    }

    public void Restore()
    {
        if (!IsActive) return;
        // Another mode may have taken ownership since Apply. Only restore the
        // values while they still match this policy's request.
        if (session != null && !session.matchFrameRateRequested)
            session.matchFrameRateRequested = previousMatchFrameRate;
        if (Application.targetFrameRate == RenderFramesPerSecond)
            Application.targetFrameRate = previousTargetFrameRate;
        session = null;
        IsActive = false;
    }
}
