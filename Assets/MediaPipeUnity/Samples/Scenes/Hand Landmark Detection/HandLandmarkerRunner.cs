// Copyright (c) 2023 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mediapipe.Unity.Sample.HandLandmarkDetection
{
    public class HandLandmarkerRunner : VisionTaskApiRunner<HandLandmarker>
    {
        public event System.Action<HandLandmarkerResult> OnHandResult;
        [SerializeField] private HandLandmarkerResultAnnotationController _handLandmarkerResultAnnotationController;

        private Experimental.TextureFramePool _textureFramePool;
        private Experimental.TextureFrame _arCpuFrameInFlight;
        private int _arCpuResultReady;
        private bool _usesDirectArCpuInput;
        private long _perfWindowStartedAt;
        private int _perfSubmissions;
        private int _perfResults;
        private long _perfInferenceTicks;
        private long _perfMaxInferenceTicks;
        private long _arSubmittedAtTicks;
        private long _arSubmittedFrameReadyTicks;
        private long _latestResultAtTicks;
        private long _latestResultSourceReadyTicks;

        public readonly HandLandmarkDetectionConfig config = new HandLandmarkDetectionConfig();

        public override void Stop()
        {
            base.Stop();
            // Close() above has drained native callbacks before buffers are freed.
            _arCpuFrameInFlight = null;
            _usesDirectArCpuInput = false;
            Volatile.Write(ref _arCpuResultReady, 0);
            ResetPerformanceWindow();
            _textureFramePool?.Dispose();
            _textureFramePool = null;
        }

        protected override IEnumerator Run()
        {
            Debug.Log($"Delegate = {config.Delegate}");
            Debug.Log($"Image Read Mode = {config.ImageReadMode}");
            Debug.Log($"Running Mode = {config.RunningMode}");
            Debug.Log($"NumHands = {config.NumHands}");
            Debug.Log($"MinHandDetectionConfidence = {config.MinHandDetectionConfidence}");
            Debug.Log($"MinHandPresenceConfidence = {config.MinHandPresenceConfidence}");
            Debug.Log($"MinTrackingConfidence = {config.MinTrackingConfidence}");
            Debug.Log("[ARBridge] SAP goi PrepareAssetAsync, path = " + config.ModelPath);
            Debug.Log("[ARBridge] ModelPath day du: '" + config.ModelPath + "'");
            yield return AssetLoader.PrepareAssetAsync(config.ModelPath);
            Debug.Log("[ARBridge] PrepareAssetAsync DA XONG (khong bi treo).");

            var options = config.GetHandLandmarkerOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnHandLandmarkDetectionOutput : null);
            taskApi = HandLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
            var imageSource = ImageSourceProvider.ImageSource;
            _usesDirectArCpuInput = imageSource is CustomARFrameBridge;
            ResetPerformanceWindow();

            // DEBUG: kiểm tra chính xác loại ImageSource đang được dùng tại thời điểm này
            Debug.Log("[ARBridge] HandLandmarkerRunner dang dung ImageSource loai: " +
              (imageSource != null ? imageSource.GetType().Name : "NULL"));

            yield return imageSource.Play();

            if (!imageSource.isPrepared)
            {
                Debug.LogError("Failed to start ImageSource, exiting...");
                yield break;
            }

            // Use RGBA32 as the input format.
            // TODO: When using GpuBuffer, MediaPipe assumes that the input format is BGRA, so maybe the following code needs to be fixed.
            _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight,
                TextureFormat.RGBA32, _usesDirectArCpuInput ? 2 : 10);

            // NOTE: The screen will be resized later, keeping the aspect ratio.
            screen.Initialize(imageSource);

            SetupAnnotationController(_handLandmarkerResultAnnotationController, imageSource);

            var transformationOptions = imageSource.GetTransformationOptions();
            var flipHorizontally = transformationOptions.flipHorizontally;
            var flipVertically = transformationOptions.flipVertically;
            var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

            AsyncGPUReadbackRequest req = default;
            var waitUntilReqDone = new WaitUntil(() => req.done);
            var waitForEndOfFrame = new WaitForEndOfFrame();
            var result = HandLandmarkerResult.Alloc(options.numHands);

            // NOTE: we can share the GL context of the render thread with MediaPipe (for now, only on Android)
            var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
            using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

            int lastProcessedARFrame = -1;
            ResetPerformanceWindow();
            while (true)
            {
                if (isPaused)
                {
                    yield return new WaitWhile(() => isPaused);
                }

                var arSource = imageSource as CustomARFrameBridge;
                if (arSource != null) LogPerformanceIfDue(arSource);

                // Keep exactly one AR inference in flight. It owns its CPU copy
                // until the native result callback has finished, so neither the
                // next camera frame nor pool reuse can overwrite its input. Once
                // ready, submit the latest camera frame, never a stale queue.
                if (_arCpuFrameInFlight != null)
                {
                    if (Volatile.Read(ref _arCpuResultReady) == 0)
                    {
                        yield return null;
                        continue;
                    }
                    _arCpuFrameInFlight.Release();
                    _arCpuFrameInFlight = null;
                }

                // Never run inference twice on the same CPU camera copy.
                if (arSource != null && arSource.FrameVersion == lastProcessedARFrame)
                {
                    yield return null;
                    continue;
                }
                if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
                {
                    yield return new WaitForEndOfFrame();
                    continue;
                }

                if (arSource != null) lastProcessedARFrame = arSource.FrameVersion;
                // Build the input Image
                Image image;
                if (arSource != null)
                {
                    Interlocked.Exchange(ref _arSubmittedFrameReadyTicks, arSource.LatestFrameReadyTimestamp);
                    // The bridge already provides an unflipped RGBA CPU image.
                    // Its transformation options request no texture flips; only
                    // the existing MediaPipe rotation below is needed. Avoid the
                    // old CPU upload -> Blit -> GPU readback -> CPU upload path.
                    var sourceTexture = (Texture2D)arSource.GetCurrentTexture();
                    textureFrame.GetRawTextureData<byte>().CopyFrom(sourceTexture.GetRawTextureData<byte>());
                    image = textureFrame.BuildCPUImage();
                    if (taskApi.runningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM)
                    {
                        Volatile.Write(ref _arCpuResultReady, 0);
                        _arCpuFrameInFlight = textureFrame;
                    }
                    else
                    {
                        // Synchronous detection consumes it before the next loop.
                        textureFrame.Release();
                    }
                }
                else switch (config.ImageReadMode)
                {
                    case ImageReadMode.GPU:
                        if (!canUseGpuImage)
                        {
                            throw new System.Exception("ImageReadMode.GPU is not supported");
                        }
                        textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                        image = textureFrame.BuildGPUImage(glContext);
                        // TODO: Currently we wait here for one frame to make sure the texture is fully copied to the TextureFrame before sending it to MediaPipe.
                        // This usually works but is not guaranteed. Find a proper way to do this. See: https://github.com/homuler/MediaPipeUnityPlugin/pull/1311
                        yield return waitForEndOfFrame;
                        break;
                    case ImageReadMode.CPU:
                        yield return waitForEndOfFrame;
                        textureFrame.ReadTextureOnCPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                        image = textureFrame.BuildCPUImage();
                        textureFrame.Release();
                        break;
                    case ImageReadMode.CPUAsync:
                    default:
                        req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                        yield return waitUntilReqDone;

                        if (req.hasError)
                        {
                            Debug.LogWarning($"Failed to read texture from the image source");
                            textureFrame.Release();
                            continue;
                        }
                        image = textureFrame.BuildCPUImage();
                        textureFrame.Release();
                        break;
                }

                if (arSource != null)
                {
                    Interlocked.Exchange(ref _arSubmittedAtTicks, Stopwatch.GetTimestamp());
                    Interlocked.Increment(ref _perfSubmissions);
                }
                switch (taskApi.runningMode)
                {
                    case Tasks.Vision.Core.RunningMode.IMAGE:
                        if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
                        {
                            _handLandmarkerResultAnnotationController.DrawNow(result);
                            OnHandResult?.Invoke(result);
                        }
                        else
                        {
                            _handLandmarkerResultAnnotationController.DrawNow(default);
                            OnHandResult?.Invoke(default);
                        }
                        break;
                    case Tasks.Vision.Core.RunningMode.VIDEO:
                        if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
                        {
                            _handLandmarkerResultAnnotationController.DrawNow(result);
                            OnHandResult?.Invoke(result);
                        }
                        else
                        {
                            _handLandmarkerResultAnnotationController.DrawNow(default);
                            OnHandResult?.Invoke(default);
                        }
                        break;
                    case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
                        taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
                        break;
                }
                if (arSource != null && taskApi.runningMode != Tasks.Vision.Core.RunningMode.LIVE_STREAM)
                    RecordArCpuResult();
            }
        }

        private void ResetPerformanceWindow()
        {
            _perfWindowStartedAt = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref _perfSubmissions, 0);
            Interlocked.Exchange(ref _perfResults, 0);
            Interlocked.Exchange(ref _perfInferenceTicks, 0);
            Interlocked.Exchange(ref _perfMaxInferenceTicks, 0);
            Interlocked.Exchange(ref _arSubmittedAtTicks, 0);
            Interlocked.Exchange(ref _arSubmittedFrameReadyTicks, 0);
            Interlocked.Exchange(ref _latestResultAtTicks, 0);
            Interlocked.Exchange(ref _latestResultSourceReadyTicks, 0);
        }

        // Called on MediaPipe's result thread: Stopwatch/Interlocked only, no
        // Unity time or logging. The main loop prints one compact line per 2s.
        private void RecordArCpuResult()
        {
            long now = Stopwatch.GetTimestamp();
            long submittedAt = Interlocked.Read(ref _arSubmittedAtTicks);
            if (submittedAt == 0) return;
            long elapsed = now - submittedAt;
            Interlocked.Add(ref _perfInferenceTicks, elapsed);
            long previousMax = Interlocked.Read(ref _perfMaxInferenceTicks);
            while (elapsed > previousMax)
            {
                long observed = Interlocked.CompareExchange(ref _perfMaxInferenceTicks, elapsed, previousMax);
                if (observed == previousMax) break;
                previousMax = observed;
            }
            Interlocked.Exchange(ref _latestResultAtTicks, now);
            Interlocked.Exchange(ref _latestResultSourceReadyTicks, Interlocked.Read(ref _arSubmittedFrameReadyTicks));
            Interlocked.Increment(ref _perfResults);
        }

        private static double AgeMilliseconds(long now, long timestamp) =>
            timestamp == 0 ? -1d : (now - timestamp) * 1000d / Stopwatch.Frequency;

        private void LogPerformanceIfDue(CustomARFrameBridge arSource)
        {
            long now = Stopwatch.GetTimestamp();
            double elapsed = (now - _perfWindowStartedAt) / (double)Stopwatch.Frequency;
            if (elapsed < 2d) return;
            int submissions = Interlocked.Exchange(ref _perfSubmissions, 0);
            int results = Interlocked.Exchange(ref _perfResults, 0);
            long inferenceTicks = Interlocked.Exchange(ref _perfInferenceTicks, 0);
            long maxTicks = Interlocked.Exchange(ref _perfMaxInferenceTicks, 0);
            double inferenceMeanMs = results == 0 ? 0d : inferenceTicks * 1000d / Stopwatch.Frequency / results;
            double inFlightMs = _arCpuFrameInFlight == null ? -1d :
                AgeMilliseconds(now, Interlocked.Read(ref _arSubmittedAtTicks));
            Debug.Log($"[ARHandPerf] runner submitFps={submissions / elapsed:F1}; resultFps={results / elapsed:F1}; " +
                $"inferMeanMs={inferenceMeanMs:F1}; inferMaxMs={maxTicks * 1000d / Stopwatch.Frequency:F1}; " +
                $"inFlightMs={inFlightMs:F1}; latestResultAgeMs={AgeMilliseconds(now, Interlocked.Read(ref _latestResultAtTicks)):F1}; " +
                $"resultSourceAgeMs={AgeMilliseconds(now, Interlocked.Read(ref _latestResultSourceReadyTicks)):F1}; " +
                $"latestReadyAgeMs={AgeMilliseconds(now, arSource.LatestFrameReadyTimestamp):F1}; delegate={config.Delegate}");
            _perfWindowStartedAt = now;
        }

        private void OnHandLandmarkDetectionOutput(HandLandmarkerResult result, Image image, long timestamp)
        {
            if (_usesDirectArCpuInput) RecordArCpuResult();
            try
            {
                _handLandmarkerResultAnnotationController.DrawLater(result);
                OnHandResult?.Invoke(result);
            }
            finally
            {
                if (_usesDirectArCpuInput) Volatile.Write(ref _arCpuResultReady, 1);
            }
        }
    }
}
