using System;
using System.IO;
using UnityEngine;

namespace Game.Unity.Evidence
{
    /// <summary>撮影台本から使う操作。スクリーンショットの保存、記録の追加、条件待ち。</summary>
    public sealed class CaptureContext
    {
        public const int JpegQuality = 85;
        private readonly string _directory;
        private readonly CaptureManifest _manifest;

        public CaptureContext(string directory, CaptureManifest manifest)
        {
            _directory = directory;
            _manifest = manifest;
            Record("imageEncoding", "JPEG (quality " + JpegQuality + ")");
        }

        /// <summary>撮影条件（設定値など）を manifest.json に残す。</summary>
        public void Record(string key, string value)
        {
            _manifest.Info.Add(new CaptureManifest.Entry { Key = key, Value = value });
        }

        /// <summary>
        /// 画面を <c>&lt;name&gt;.jpg</c> として保存する。直前に状態を変えていても反映されるよう、1 フレーム描画させてから撮る。
        /// <paramref name="note"/> には撮った時点（Tick 数など）を書く。
        /// </summary>
        public async Awaitable ShootAsync(string name, string note)
        {
            await Awaitable.NextFrameAsync();
            await Awaitable.EndOfFrameAsync();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                SaveFrame(texture, name, note);
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>不透明な画面画像をJPEG品質85で保存し、保存したバイト列のハッシュを記録する。Textureの破棄は呼び出し側が行う。</summary>
        public void SaveFrame(Texture2D texture, string name, string note)
        {
            var file = name + ".jpg";
            var jpeg = texture.EncodeToJPG(JpegQuality);
            File.WriteAllBytes(Path.Combine(_directory, file), jpeg);
            _manifest.Shots.Add(new CaptureManifest.Shot { File = file, Sha256 = EvidencePublisher.Sha256Of(jpeg), Note = note });
            Debug.Log($"[EvidenceCapture] 撮影: {file}（{note}）");
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
