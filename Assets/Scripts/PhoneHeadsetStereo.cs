using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Renders only virtual objects for each eye. The tracked AR camera supplies the shared background.</summary>
public sealed class PhoneHeadsetStereo : IDisposable
{
    public const float ConvergenceDistance = 0.85f;
    private const float EyeSeparation = 0.060f;
    private const int MaxEyeDimension = 960;

    private readonly Camera source;
    private readonly int sourceCullingMask;
    private readonly Camera[] eyes = new Camera[2];
    private readonly RenderTexture[] textures = new RenderTexture[2];
    private readonly RTHandle[] targets = new RTHandle[2];
    private Vector2Int targetSize;

    public RTHandle Left => targets[0];
    public RTHandle Right => targets[1];
    public RenderTexture LeftTexture => textures[0];
    public RenderTexture RightTexture => textures[1];
    public bool IsReady { get; private set; }
    internal int SceneCullingMask => sourceCullingMask;

    public PhoneHeadsetStereo(Camera source)
    {
        this.source = source;
        sourceCullingMask = source.cullingMask;
        for (int i = 0; i < eyes.Length; i++)
        {
            var eyeObject = new GameObject(i == 0 ? "Headset Left Eye" : "Headset Right Eye");
            eyeObject.transform.SetParent(source.transform, false);
            var eye = eyeObject.AddComponent<Camera>();
            eye.enabled = false;
            eye.CopyFrom(source);
            eye.enabled = false;
            eye.targetTexture = null;
            eye.ResetWorldToCameraMatrix();
            eye.ResetCullingMatrix();
            eye.depth = source.depth - (eyes.Length - i);
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = Color.clear;
            eye.allowHDR = false;
            eye.allowMSAA = false;
            eye.allowDynamicResolution = false;
            eye.useOcclusionCulling = false;
            var additionalData = eye.GetUniversalAdditionalCameraData();
            additionalData.allowXRRendering = false;
            additionalData.renderShadows = false;
            additionalData.requiresColorOption = CameraOverrideOption.Off;
            additionalData.requiresDepthOption = CameraOverrideOption.Off;
            eyes[i] = eye;
        }
        Prepare();
        RenderPipelineManager.beginContextRendering += UpdatePoses;
    }

    public void Prepare()
    {
        bool landscape = source.pixelWidth > source.pixelHeight;
        if (landscape)
        {
            int halfWidth = source.pixelWidth / 2;
            int height = source.pixelHeight;
            float scale = Mathf.Min(1f, MaxEyeDimension / (float)Mathf.Max(halfWidth, height));
            var size = new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(halfWidth * scale)),
                                      Mathf.Max(1, Mathf.RoundToInt(height * scale)));
            if (targetSize != size)
            {
                for (int i = 0; i < eyes.Length; i++)
                {
                    ReleaseTarget(i);
                    var texture = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32)
                    {
                        name = eyes[i].name,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    // Expose the color attachment to Render Graph separately
                    // from the depth buffer that URP needs while drawing this eye.
                    targets[i] = RTHandles.Alloc(new RenderTargetIdentifier(texture), texture.name);
                    textures[i] = texture;
                    eyes[i].targetTexture = texture;
                }
                targetSize = size;
            }
        }
        if (landscape == IsReady) return;
        IsReady = landscape;
        source.cullingMask = landscape ? 0 : sourceCullingMask;
        foreach (var eye in eyes) eye.enabled = landscape;
    }

    private void UpdatePoses(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!IsReady || !cameras.Contains(source)) return;
        for (int i = 0; i < eyes.Length; i++)
        {
            float offset = GetEyeOffset(i == 1);
            eyes[i].transform.localPosition = Vector3.right * offset;
            eyes[i].projectionMatrix = MakeEyeProjection(source.projectionMatrix, offset, source.orthographic);
        }
    }

    public static Matrix4x4 MakeEyeProjection(Matrix4x4 centerProjection, float eyeOffset, bool orthographic)
    {
        // Each lens uses the center half of the tracked camera's horizontal FoV.
        // Parallel eye axes converge on the same pixels at the interaction distance.
        centerProjection.m00 *= 2f;
        centerProjection.m02 *= 2f;
        centerProjection.m03 *= 2f;
        if (orthographic) centerProjection.m03 += centerProjection.m00 * eyeOffset;
        else centerProjection.m02 -= centerProjection.m00 * eyeOffset / ConvergenceDistance;
        return centerProjection;
    }

    public static bool TryProjectIntoEye(Camera source, Vector3 worldPoint, bool rightEye, out Vector2 screenPoint,
        bool clipToLens = true)
    {
        screenPoint = default;
        float offset = GetEyeOffset(rightEye);
        Vector3 view = source.worldToCameraMatrix.MultiplyPoint3x4(worldPoint);
        if (-view.z <= source.nearClipPlane) return false;
        view.x -= offset;
        Matrix4x4 projection = MakeEyeProjection(source.projectionMatrix, offset, source.orthographic);
        Vector4 clip = projection * new Vector4(view.x, view.y, view.z, 1f);
        if (clip.w <= 0f) return false;

        // Undo the eye half's crop. Hand input and UI use the source camera's viewport.
        var viewport = new Vector2(0.5f + clip.x / (4f * clip.w), 0.5f + clip.y / (2f * clip.w));
        if (clipToLens && !ContainsSourcePoint(viewport)) return false;
        screenPoint = source.pixelRect.min + Vector2.Scale(viewport, source.pixelRect.size);
        return true;
    }

    internal static bool TryGetEyeRay(Camera source, Vector2 screenPoint, bool rightEye, out Ray ray,
        bool clipToLens = true)
    {
        ray = default;
        Vector2 viewport = source.ScreenToViewportPoint(screenPoint);
        if (clipToLens && !ContainsSourcePoint(viewport)) return false;

        float offset = GetEyeOffset(rightEye);
        Matrix4x4 clipToView = MakeEyeProjection(source.projectionMatrix, offset, source.orthographic).inverse;
        Matrix4x4 viewToWorld = source.cameraToWorldMatrix;
        Vector2 ndc = SourceToEyeNdc(viewport);
        Vector3 near = clipToView.MultiplyPoint(new Vector3(ndc.x, ndc.y, -1f));
        Vector3 middle = clipToView.MultiplyPoint(new Vector3(ndc.x, ndc.y, 0f));
        ray = new Ray(viewToWorld.MultiplyPoint3x4(near + Vector3.right * offset),
            viewToWorld.MultiplyVector(middle - near));
        return true;
    }

    private static float GetEyeOffset(bool rightEye) => (rightEye ? 0.5f : -0.5f) * EyeSeparation;

    private static Vector2 SourceToEyeNdc(Vector2 viewport) =>
        new Vector2(4f * (viewport.x - 0.5f), 2f * (viewport.y - 0.5f));

    public static bool ContainsSourcePoint(Vector2 viewportPoint)
    {
        Vector2 warped = SourceToEyeNdc(viewportPoint);
        if (Mathf.Abs(warped.x) > 1f || Mathf.Abs(warped.y) > 1f) return false;
        // Invert the compositor's mild radial warp to reject points in its black border.
        Vector2 lens = warped;
        for (int i = 0; i < 3; i++) lens = warped / (0.93f + 0.12f * lens.sqrMagnitude);
        float x2 = lens.x * lens.x;
        float y2 = lens.y * lens.y;
        return x2 * x2 + y2 * y2 < 0.87f;
    }

    public void Dispose()
    {
        RenderPipelineManager.beginContextRendering -= UpdatePoses;
        if (source != null) source.cullingMask = sourceCullingMask;
        for (int i = 0; i < eyes.Length; i++)
        {
            ReleaseTarget(i);
            if (eyes[i] != null)
            {
                eyes[i].enabled = false;
                CoreUtils.Destroy(eyes[i].gameObject);
            }
        }
        IsReady = false;
    }

    private void ReleaseTarget(int index)
    {
        if (eyes[index] != null) eyes[index].targetTexture = null;
        targets[index]?.Release();
        targets[index] = null;
        CoreUtils.Destroy(textures[index]);
        textures[index] = null;
    }
}
