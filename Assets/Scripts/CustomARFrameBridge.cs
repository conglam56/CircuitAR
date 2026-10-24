using System.Collections;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Mediapipe.Unity;

public class CustomARFrameBridge : ImageSource
{
    private const float CpuFramesPerSecond = 30f;
    private const int MaxInferenceImageDimension = 640;
    private readonly ARCameraManager cameraManager;

    private Texture2D _texture;
    private bool _isPlaying = false;
    private bool _acceptCpuFrames;
    private int _frameReceivedCount = 0; // DEBUG: đếm số lần frameReceived gọi tới
    private float _nextCpuFrameAt;
    private int _frameVersion;
    private XRCpuImage.AsyncConversion _cpuConversion;
    private XRCpuImage _pendingCpuImage;
    private bool _cpuConversionPending;
    private Vector2Int _pendingDimensions;
    private long _cpuConversionStartedAt;
    public int FrameVersion { get { CompleteCpuConversionIfReady(); return _frameVersion; } }
    private readonly object _displayMatrixLock = new object();
    private Matrix4x4 _displayMatrix;
    private bool _hasDisplayMatrix;
    private bool _coordinateDiagnosticLogged;
    private bool _coordinateFallbackDiagnosticLogged;
    private long _latestFrameReadyTimestamp;
    private long _perfWindowStartedAt;
    private int _perfCameraFrames;
    private int _perfConvertedFrames;
    private int _perfAcquireMisses;
    private long _perfConversionTicks;
    private long _perfMaxConversionTicks;
    public long LatestFrameReadyTimestamp => Interlocked.Read(ref _latestFrameReadyTimestamp);

    public CustomARFrameBridge(ARCameraManager cameraManager)
    {
        this.cameraManager = cameraManager;
        Debug.Log("[ARBridge] Constructor chay, cameraManager la null? " + (cameraManager == null));
    }

    public override string sourceName => "ARFoundation_Camera";
    public override string[] sourceCandidateNames => new string[] { "ARFoundation_Camera" };

    public override ResolutionStruct[] availableResolutions => new ResolutionStruct[] { new ResolutionStruct { width = textureWidth, height = textureHeight } };

    public override double frameRate => CpuFramesPerSecond;

    public override bool isPrepared { get { CompleteCpuConversionIfReady(); return _texture != null && _frameVersion > 0; } }
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
        ClearDisplayMatrix();
        _acceptCpuFrames = true;
        _nextCpuFrameAt = 0f;
        _coordinateDiagnosticLogged = false;
        _coordinateFallbackDiagnosticLogged = false;
        ResetPerformanceWindow();
        Debug.Log("[ARBridge] Play() bat dau, dang dang ky su kien frameReceived...");
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnCameraFrameReceived;
            cameraManager.frameReceived += OnCameraFrameReceived;
            Debug.Log("[ARBridge] Da dang ky frameReceived. cameraManager.enabled = " + cameraManager.enabled);
        }
        else
        {
            Debug.LogError("[ARBridge] cameraManager dang NULL, khong the dang ky su kien!");
        }
        yield return new WaitUntil(() => isPrepared);
        Debug.Log("[ARBridge] isPrepared = true, Play() hoan tat.");
        _isPlaying = true;
    }

    public override IEnumerator Resume()
    {
        _acceptCpuFrames = true;
        _nextCpuFrameAt = 0f;
        ResetPerformanceWindow();
        _isPlaying = true;
        yield return null;
    }

    public override void Pause()
    {
        _acceptCpuFrames = false;
        DisposeCpuConversion();
        _isPlaying = false;
        ClearDisplayMatrix();
    }

    public override void Stop()
    {
        _acceptCpuFrames = false;
        DisposeCpuConversion();
        _isPlaying = false;
        Interlocked.Exchange(ref _latestFrameReadyTimestamp, 0);
        ClearDisplayMatrix();
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
        CompleteCpuConversionIfReady();
        return _texture;
    }

    private void DisposeCpuConversion()
    {
        if (_pendingCpuImage.valid)
        {
            _pendingCpuImage.Dispose();
            _pendingCpuImage = default;
        }
        if (!_cpuConversionPending) return;
        _cpuConversionPending = false;
        _cpuConversion.Dispose();
        _cpuConversion = default;
    }

    private void CompleteCpuConversionIfReady()
    {
        if (!_cpuConversionPending || _cpuConversion.status == XRCpuImage.AsyncConversionStatus.Processing) return;
        try
        {
            if (!_acceptCpuFrames || _cpuConversion.status != XRCpuImage.AsyncConversionStatus.Ready) return;
            var convertedData = _cpuConversion.GetData<byte>();
            if (convertedData.Length != _pendingDimensions.x * _pendingDimensions.y * 4) return;
            if (_texture == null || _texture.width != _pendingDimensions.x || _texture.height != _pendingDimensions.y)
            {
                if (_texture != null) UnityEngine.Object.Destroy(_texture);
                _texture = new Texture2D(_pendingDimensions.x, _pendingDimensions.y, TextureFormat.RGBA32, false);
                resolution = new ResolutionStruct { width = _pendingDimensions.x, height = _pendingDimensions.y };
            }
            // Copy before disposing the native request. Inference owns a separate
            // texture-frame copy, so the next completed camera image is safe.
            convertedData.CopyTo(_texture.GetRawTextureData<byte>());
            long completedAt = Stopwatch.GetTimestamp();
            long conversionTicks = completedAt - _cpuConversionStartedAt;
            _perfConversionTicks += conversionTicks;
            _perfMaxConversionTicks = System.Math.Max(_perfMaxConversionTicks, conversionTicks);
            _perfConvertedFrames++;
            Interlocked.Exchange(ref _latestFrameReadyTimestamp, completedAt);
            _frameVersion++;
        }
        catch (System.Exception error)
        {
            _perfAcquireMisses++;
            Debug.LogWarning("[ARBridge] Async CPU frame was discarded: " + error.Message);
        }
        finally { DisposeCpuConversion(); }
    }

    /// <summary>
    /// Map both landmarks with one camera-frame snapshot. The CPU image is copied
    /// without flips; MediaPipe's inference rotation does not rotate its returned
    /// source-image coordinates. Do not also apply the manual webcam axis flags.
    /// </summary>
    public bool TryMapImagePointsToViewport(Vector2 indexImagePoint, Vector2 thumbImagePoint,
        out Vector2 indexViewportPoint, out Vector2 thumbViewportPoint)
    {
        Matrix4x4 matrix;
        bool hasMatrix;
        lock (_displayMatrixLock)
        {
            matrix = _displayMatrix;
            hasMatrix = _hasDisplayMatrix;
        }

        bool indexMapped = TryMapImagePointToViewport(indexImagePoint,
            hasMatrix ? matrix : Matrix4x4.zero, out indexViewportPoint);
        bool thumbMapped = TryMapImagePointToViewport(thumbImagePoint,
            hasMatrix ? matrix : Matrix4x4.zero, out thumbViewportPoint);
        return indexMapped && thumbMapped;
    }

    /// <summary>
    /// Invert the exact row-vector affine transform used by ARCoreBackground:
    /// cameraUV = mul(float4(viewportUV, 1, 0), displayMatrix). Unity's usual
    /// MultiplyPoint convention is different: this matrix stores its offset in
    /// row 2. Viewport output is bottom-left-origin, ready for ScreenPointToRay.
    /// No clamping: a landmark cropped out of the AR image is not a screen-edge hit.
    /// </summary>
    public static bool TryMapImagePointToViewport(Vector2 imagePoint, Matrix4x4 matrix,
        out Vector2 viewportPoint)
    {
        // Preserve the previously calibrated uncropped portrait mapping while
        // the provider has not supplied a usable display matrix yet.
        viewportPoint = new Vector2(1f - imagePoint.y, 1f - imagePoint.x);
        if (!IsFinite(imagePoint.x) || !IsFinite(imagePoint.y))
        {
            viewportPoint = new Vector2(0.5f, 0.5f);
            return false;
        }

        float determinant = matrix.m00 * matrix.m11 - matrix.m10 * matrix.m01;
        if (!IsFinite(matrix.m00) || !IsFinite(matrix.m01) ||
            !IsFinite(matrix.m10) || !IsFinite(matrix.m11) ||
            !IsFinite(matrix.m20) || !IsFinite(matrix.m21) ||
            !IsFinite(determinant) || Mathf.Abs(determinant) < 0.000001f) return false;

        float imageX = imagePoint.x - matrix.m20;
        float imageY = imagePoint.y - matrix.m21;
        Vector2 mapped = new Vector2(
            (imageX * matrix.m11 - imageY * matrix.m10) / determinant,
            (imageY * matrix.m00 - imageX * matrix.m01) / determinant);
        if (!IsFinite(mapped.x) || !IsFinite(mapped.y)) return false;
        viewportPoint = mapped;
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    // Resize the entire raw CPU image, never its crop. Normalized landmarks then
    // keep the same display-matrix mapping at every camera capture resolution.
    public static Vector2Int GetInferenceDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0) return Vector2Int.zero;
        float scale = Mathf.Min(1f, MaxInferenceImageDimension / (float)Mathf.Max(width, height));
        return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(width * scale)),
            Mathf.Max(1, Mathf.RoundToInt(height * scale)));
    }

    private void ClearDisplayMatrix()
    {
        lock (_displayMatrixLock)
        {
            _hasDisplayMatrix = false;
            _displayMatrix = Matrix4x4.zero;
        }
    }

    private void ResetPerformanceWindow()
    {
        _perfWindowStartedAt = Stopwatch.GetTimestamp();
        _perfCameraFrames = 0;
        _perfConvertedFrames = 0;
        _perfAcquireMisses = 0;
        _perfConversionTicks = 0;
        _perfMaxConversionTicks = 0;
        Interlocked.Exchange(ref _latestFrameReadyTimestamp, 0);
    }

    private void LogPerformanceIfDue()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = (now - _perfWindowStartedAt) / (double)Stopwatch.Frequency;
        if (elapsed < 2d) return;
        double meanMs = _perfConvertedFrames == 0 ? 0d :
            _perfConversionTicks * 1000d / Stopwatch.Frequency / _perfConvertedFrames;
        long latestFrameAt = LatestFrameReadyTimestamp;
        double frameReadyAgeMs = latestFrameAt == 0 ? -1d : (now - latestFrameAt) * 1000d / Stopwatch.Frequency;
        Debug.Log($"[ARHandPerf] bridge cameraFps={_perfCameraFrames / elapsed:F1}; copyFps={_perfConvertedFrames / elapsed:F1}; " +
            $"convertAsyncMeanMs={meanMs:F2}; convertAsyncMaxMs={_perfMaxConversionTicks * 1000d / Stopwatch.Frequency:F2}; " +
            $"acquireMisses={_perfAcquireMisses}; latestReadyAgeMs={frameReadyAgeMs:F1}; image={textureWidth}x{textureHeight}");
        _perfWindowStartedAt = now;
        _perfCameraFrames = 0;
        _perfConvertedFrames = 0;
        _perfAcquireMisses = 0;
        _perfConversionTicks = 0;
        _perfMaxConversionTicks = 0;
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        _frameReceivedCount++;
        if (eventArgs.displayMatrix.HasValue)
        {
            lock (_displayMatrixLock)
            {
                _displayMatrix = eventArgs.displayMatrix.Value;
                _hasDisplayMatrix = true;
            }
        }
        if (!_acceptCpuFrames) return;
        CompleteCpuConversionIfReady();
        _perfCameraFrames++;
        LogPerformanceIfDue();
        // Native AR background continues independently. Only the small, readable
        // inference copy is produced here; it is never uploaded then read back.
        if (_cpuConversionPending || Time.unscaledTime < _nextCpuFrameAt) return;
        _nextCpuFrameAt = Mathf.Max(_nextCpuFrameAt + 1f / CpuFramesPerSecond, Time.unscaledTime);

        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            _perfAcquireMisses++;
            if (_frameReceivedCount <= 5 || _frameReceivedCount % 60 == 0)
            {
                Debug.LogWarning("[ARBridge] TryAcquireLatestCpuImage THAT BAI, lan thu " + _frameReceivedCount);
            }
            return;
        }

        var format = TextureFormat.RGBA32;
        Vector2Int inferenceDimensions = GetInferenceDimensions(image.width, image.height);

        if (!_coordinateDiagnosticLogged && eventArgs.displayMatrix.HasValue)
        {
            Matrix4x4 matrix = eventArgs.displayMatrix.Value;
            if (TryMapImagePointToViewport(new Vector2(0.5f, 0.5f), matrix, out Vector2 center))
            {
                Debug.Log($"[ARCoordinates] CPU={image.width}x{image.height}; screen={UnityEngine.Screen.width}x{UnityEngine.Screen.height}; " +
                    $"Drows=({matrix.m00:F4},{matrix.m01:F4});({matrix.m10:F4},{matrix.m11:F4});({matrix.m20:F4},{matrix.m21:F4}); " +
                    $"imageCenterViewport=({center.x:F4},{center.y:F4}); inverse-display-matrix mapping, no extra manual rotation.");
                _coordinateDiagnosticLogged = true;
            }
        }
        if (!_coordinateDiagnosticLogged && !_coordinateFallbackDiagnosticLogged && _frameReceivedCount >= 5)
        {
            Debug.LogWarning("[ARCoordinates] No usable display matrix yet; temporarily using uncropped portrait landmark mapping.");
            _coordinateFallbackDiagnosticLogged = true;
        }

        var conversionParams = new XRCpuImage.ConversionParams(image, format, XRCpuImage.Transformation.None);
        conversionParams.outputDimensions = inferenceDimensions;

        try
        {
            _cpuConversionStartedAt = Stopwatch.GetTimestamp();
            _pendingDimensions = inferenceDimensions;
            _cpuConversion = image.ConvertAsync(conversionParams);
            _cpuConversionPending = true;
            _pendingCpuImage = image;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[ARBridge] Loi khi Convert CPU texture: " + e.Message);
            image.Dispose();
        }
    }
}
