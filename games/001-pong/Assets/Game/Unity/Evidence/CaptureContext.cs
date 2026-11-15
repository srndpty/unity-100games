using System;
using System.IO;
using UnityEngine;

namespace Game.Unity.Evidence
{
    /// <summary>撮影台本から使う操作。スクリーンショットの保存、記録の追加、条件待ち。</summary>
    public sealed class CaptureContext
    {
        private readonly string _directory;
        private readonly CaptureManifest _manifest;

        public CaptureContext(string directory, CaptureManifest manifest)
        {
            _directory = directory;
            _manifest = manifest;
        }

        /// <summary>撮影条件（設定値など）を manifest.json に残す。</summary>
        public void Record(string key, string value)
        {
            _manifest.Info.Add(new CaptureManifest.Entry { Key = key, Value = value });
        }

        /// <summary>
        /// 画面を <c>&lt;name&gt;.png</c> として保存する。直前に状態を変えていても反映されるよう、1 フレーム描画させてから撮る。
        /// <paramref name="note"/> には撮った時点（Tick 数など）を書く。
        /// </summary>
        public async Awaitable ShootAsync(string name, string note)
        {
            await Awaitable.NextFrameAsync();
            await Awaitable.EndOfFrameAsync();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var file = name + ".png";
                var png = texture.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(_directory, file), png);
                _manifest.Shots.Add(new CaptureManifest.Shot { File = file, Sha256 = EvidencePublisher.Sha256Of(png), Note = note });
                Debug.Log($"[EvidenceCapture] 撮影: {file}（{note}）");
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
