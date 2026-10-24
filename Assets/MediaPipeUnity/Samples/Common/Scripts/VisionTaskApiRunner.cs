// Copyright (c) 2021 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using UnityEngine;

namespace Mediapipe.Unity.Sample
{
  public abstract class VisionTaskApiRunner<TTask> : BaseRunner where TTask : Tasks.Vision.Core.BaseVisionTaskApi
  {
    [SerializeField] protected Screen screen;

    private Coroutine _coroutine;
    protected TTask taskApi;

    public RunningMode runningMode;

        public override void Play()
        {
            if (screen != null)
            {
                // Chỉ ẩn ảnh camera CPU, không tắt cả cây chứa con trỏ ngón tay.
                screen.SetImageVisible(false);
            }

            if (_coroutine != null)
            {
                Stop();
            }
            base.Play();
            _coroutine = StartCoroutine(Run());
        }

        public override void Pause()
    {
      base.Pause();
      // Welcome.Awake pauses the runner before Bootstrap.Start has supplied an
      // image source. Preserve the requested state without dereferencing null.
      ImageSourceProvider.ImageSource?.Pause();
    }

    public override void Resume()
    {
      base.Resume();
      var source = ImageSourceProvider.ImageSource;
      // Initial startup belongs to BaseRunner.Start/Play after Bootstrap is ready.
      if (_coroutine != null && source != null) StartCoroutine(source.Resume());
    }

    public override void Stop()
    {
      base.Stop();
      if (_coroutine != null) StopCoroutine(_coroutine);
      _coroutine = null;
      ImageSourceProvider.ImageSource?.Stop();
      taskApi?.Close();
      taskApi = null;
    }

    protected abstract IEnumerator Run();

    protected static void SetupAnnotationController<T>(AnnotationController<T> annotationController, ImageSource imageSource, bool expectedToBeMirrored = false) where T : HierarchicalAnnotation
    {
      annotationController.isMirrored = expectedToBeMirrored;
      annotationController.imageSize = new Vector2Int(imageSource.textureWidth, imageSource.textureHeight);
    }
  }
}
