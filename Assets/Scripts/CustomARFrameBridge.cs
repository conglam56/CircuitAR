using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Mediapipe.Unity;

public class CustomARFrameBridge : ImageSource
{
    private readonly ARCameraManager cameraManager;

    private Texture2D _texture;
    private bool _isPlaying = false;
    private int _frameReceivedCount = 0; // DEBUG: đếm số lần frameReceived gọi tới

    public CustomARFrameBridge(ARCameraManager cameraManager)
    {
        this.cameraManager = cameraManager;
        Debug.Log("[ARBridge] Constructor chay, cameraManager la null? " + (cameraManager == null));
    }

    public override string sourceName => "ARFoundation_Camera";
    public override string[] sourceCandidateNames => new string[] { "ARFoundation_Camera" };

    public override ResolutionStruct[] availableResolutions => new ResolutionStruct[] { new ResolutionStruct { width = textureWidth, height = textureHeight } };

    public override double frameRate => 30.0;

    public override bool isPrepared => _texture != null;
    public override bool isPlaying => _isPlaying;
    public override bool isFrontFacing => false;
    public override bool isHorizontallyFlipped => false;
    public override bool isVerticallyFlipped => true;
    public override RotationAngle rotation => RotationAngle.Rotation90;

    public override int textureWidth => _texture != null ? _texture.width : 0;
    public override int textureHeight => _texture != null ? _texture.height : 0;

    public override void SelectSource(int sourceId) { }

    public override IEnumerator Play()
    {
        Debug.Log("[ARBridge] Play() bat dau, dang dang ky su kien frameReceived...");
        if (cameraManager == null)
        {
            Debug.LogError("[ARBridge] cameraManager dang NULL, khong the dang ky su kien!");
            yield break;
        }

        cameraManager.frameReceived += OnCameraFrameReceived;
        Debug.Log("[ARBridge] Da dang ky frameReceived. cameraManager.enabled = " + cameraManager.enabled);

        float timeout = 8.0f;
        float elapsed = 0f;
        while (!isPrepared && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!isPrepared)
        {
            Debug.LogError($"[ARBridge] Timeout sau {timeout}s khong nhan duoc frame tu ARCameraManager!");
            yield break;
        }

        Debug.Log("[ARBridge] isPrepared = true, Play() hoan tat.");
        _isPlaying = true;
    }

    public override IEnumerator Resume()
    {
        _isPlaying = true;
        yield return null;
    }

    public override void Pause()
    {
        _isPlaying = false;
    }

    public override void Stop()
    {
        _isPlaying = false;
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnCameraFrameReceived;
        }
        if (_texture != null)
        {
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
        }
    }

    public override Texture GetCurrentTexture()
    {
        return _texture;
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        _frameReceivedCount++;
        if (_frameReceivedCount <= 5 || _frameReceivedCount % 60 == 0)
        {
            Debug.Log($"[ARBridge] OnCameraFrameReceived duoc goi, lan thu {_frameReceivedCount}");
        }

        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            if (_frameReceivedCount <= 5 || _frameReceivedCount % 60 == 0)
            {
                Debug.LogWarning("[ARBridge] TryAcquireLatestCpuImage THAT BAI, lan thu " + _frameReceivedCount);
            }
            return;
        }

        var format = TextureFormat.RGBA32;

        if (_texture == null || _texture.width != image.width || _texture.height != image.height)
        {
            _texture = new Texture2D(image.width, image.height, format, false);
            resolution = new ResolutionStruct { width = image.width, height = image.height };
            Debug.Log($"[ARBridge] Da tao texture moi: {image.width}x{image.height}");
        }

        var conversionParams = new XRCpuImage.ConversionParams(image, format, XRCpuImage.Transformation.None);
        var rawTextureData = _texture.GetRawTextureData<byte>();

        try
        {
            image.Convert(conversionParams, rawTextureData);
            _texture.Apply();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ARBridge] Loi khi Convert/Apply texture: " + e.Message);
        }
        finally
        {
            image.Dispose();
        }
    }
}