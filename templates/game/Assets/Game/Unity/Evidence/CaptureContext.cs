using System;
using System.IO;
using UnityEngine;

namespace Game.Unity.Evidence
{
    /// <summary>撮影台本から使う操作。スクリーンショットの保存と、条件待ち。</summary>
    public sealed class CaptureContext
    {
        private readonly string _directory;

        public CaptureContext(string directory)
        {
            _directory = directory;
        }

        /// <summary>現在のフレームを描画し終えた時点の画面を <c>&lt;name&gt;.png</c> として保存する。</summary>
        public async Awaitable ShootAsync(string name)
        {
            await Awaitable.EndOfFrameAsync();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var path = Path.Combine(_directory, name + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Debug.Log($"[EvidenceCapture] 保存: {path}");
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>条件を満たすまでフレームを進める。実時間で <paramref name="timeoutSeconds"/> を超えたら失敗とする。</summary>
        public async Awaitable WaitUntilAsync(Func<bool> condition, float timeoutSeconds)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    throw new TimeoutException($"{timeoutSeconds} 秒以内に条件を満たしませんでした");
                }

                await Awaitable.NextFrameAsync();
            }
        }
    }
}
