// Copyright (c) 2021 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Mediapipe.Unity.Sample
{
    public class Bootstrap : MonoBehaviour
    {
        [SerializeField] private AppSettings _appSettings;

        // MỚI THÊM: bật cái này để MediaPipe đọc hình từ AR Camera (ARCore) thay vì tự mở webcam riêng
        // — tránh xung đột "camera was passed NULL" khi chạy chung với AR Foundation.
        [Header("AR Camera Bridge (mới thêm)")]
        [SerializeField] private bool _useARCamera = false;

        public InferenceMode inferenceMode { get; private set; }
        public bool isFinished { get; private set; }
        private bool _isGlogInitialized;

        private void OnEnable()
        {
            var _ = StartCoroutine(Init());
        }

        private IEnumerator Init()
        {
            Debug.Log("The configuration for the sample app can be modified using AppSettings.asset.");
#if !DEBUG && !DEVELOPMENT_BUILD
      Debug.LogWarning("Logging for the MediaPipeUnityPlugin will be suppressed. To enable logging, please check the 'Development Build' option and build.");
#endif

            Logger.MinLogLevel = _appSettings.logLevel;

            Protobuf.SetLogHandler(Protobuf.DefaultLogHandler);

            Debug.Log("Setting global flags...");
            _appSettings.ResetGlogFlags();
            Glog.Initialize("MediaPipeUnityPlugin");
            _isGlogInitialized = true;

            Debug.Log("Initializing AssetLoader...");
            switch (_appSettings.assetLoaderType)
            {
                case AppSettings.AssetLoaderType.AssetBundle:
                    {
                        AssetLoader.Provide(new AssetBundleResourceManager("mediapipe"));
                        break;
                    }
                case AppSettings.AssetLoaderType.StreamingAssets:
                    {
                        AssetLoader.Provide(new StreamingAssetsResourceManager());
                        break;
                    }
                case AppSettings.AssetLoaderType.Local:
                    {
#if UNITY_EDITOR
                        AssetLoader.Provide(new LocalResourceManager());
                        break;
#else
            Debug.LogError("LocalResourceManager is only supported on UnityEditor." +
              "To avoid this error, consider switching to the StreamingAssetsResourceManager and copying the required resources under StreamingAssets, for example.");
            yield break;
#endif
                    }
                default:
                    {
                        Debug.LogError($"AssetLoaderType is unknown: {_appSettings.assetLoaderType}");
                        yield break;
                    }
            }

            DecideInferenceMode();
            if (inferenceMode == InferenceMode.GPU)
            {
                Debug.Log("Initializing GPU resources...");
                yield return GpuManager.Initialize();

                if (!GpuManager.IsInitialized)
                {
                    Debug.LogWarning("If your native library is built for CPU, change 'Preferable Inference Mode' to CPU from the Inspector Window for AppSettings");
                }
            }

            Debug.Log("Preparing ImageSource...");
            ImageSourceProvider.Initialize(
              _appSettings.BuildWebCamSource(), _appSettings.BuildStaticImageSource(), _appSettings.BuildVideoSource());

            // MỚI THÊM: nếu bật _useARCamera, tự tìm ARCameraManager trong Scene (vì Bootstrap là Prefab
            // được Instantiate lúc chạy nên không kéo-thả tham chiếu Scene vào Inspector được) và
            // dùng CustomARFrameBridge làm nguồn ảnh thay vì webcam mặc định.
            if (_useARCamera)
            {
                var arCameraManager = Object.FindFirstObjectByType<ARCameraManager>();
                if (arCameraManager != null)
                {
                    Debug.Log("Da tim thay ARCameraManager, dung AR Camera lam nguon anh cho MediaPipe.");
                    var arSource = new CustomARFrameBridge(arCameraManager);
                    ImageSourceProvider.SetCustomSource(arSource);
                }
                else
                {
                    Debug.LogError("Khong tim thay ARCameraManager trong Scene! Quay ve dung webcam mac dinh.");
                    ImageSourceProvider.Switch(_appSettings.defaultImageSource);
                }
            }
            else
            {
                ImageSourceProvider.Switch(_appSettings.defaultImageSource);
            }

            isFinished = true;
        }

        private void DecideInferenceMode()
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_WIN
            if (_appSettings.preferableInferenceMode == InferenceMode.GPU)
            {
                Debug.LogWarning("Current platform does not support GPU inference mode, so falling back to CPU mode");
            }
            inferenceMode = InferenceMode.CPU;
#else
      inferenceMode = _appSettings.preferableInferenceMode;
#endif
        }

        private void OnApplicationQuit()
        {
            GpuManager.Shutdown();

            if (_isGlogInitialized)
            {
                Glog.Shutdown();
            }

            Protobuf.ResetLogHandler();
        }
    }
}