using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Game.Unity.Evidence
{
    /// <summary>
    /// 起動引数 <c>-capture &lt;evidence フォルダ&gt;</c> があるとき、シーン上の <see cref="ICaptureScenario"/> を実行して
    /// スクリーンショットを撮り、終了する（`dev capture NNN` から使う）。
    /// 撮影中は <see cref="IsRunning"/> が true になる。ゲーム側は乱数に <see cref="Seed"/> を使い、
    /// 実時間やプレイヤー入力では進めず台本に進行を任せ、音も鳴らさない。これで同じビルドなら毎回同じ画像になる。
    /// 画像と manifest.json は一時フォルダに書き、全部撮れて検証できたときだけ <c>capture/</c> として公開する（<see cref="EvidencePublisher"/>）。
    /// 失敗しても必ず終了し、終了コード 1 を返す。
    /// </summary>
    public static class EvidenceCapture
    {
        /// <summary>撮影時に使う固定シード。</summary>
        public const int Seed = 1;

        private const string Argument = "-capture";

        // 撮影しない通常起動では null。
        // 起動引数は実行中に変わらないので一度だけ読む。Awake からも参照できるよう、シーン読み込みの前後に依存しない形にする。
        private static readonly string OutputDirectory = ReadOutputDirectory();

        public static bool IsRunning => OutputDirectory != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            if (OutputDirectory == null)
            {
                return;
            }

            Application.runInBackground = true;
            _ = RunAsync(Path.GetFullPath(OutputDirectory));
        }

        // どこで失敗しても必ず Application.Quit まで到達させる（dev capture が終了を待ち続けないように）。
        private static async Awaitable RunAsync(string directory)
        {
            var exitCode = 0;
            string staging = null;
            try
            {
                Directory.CreateDirectory(directory);
                var publisher = new EvidencePublisher(directory);
                staging = publisher.CreateStaging();
                var manifest = new CaptureManifest
                {
                    Product = Application.productName,
                    Version = Application.version,
                    UnityVersion = Application.unityVersion,
                    BuildGuid = Application.buildGUID,
                    Seed = Seed,
                    CapturedAtUtc = DateTime.UtcNow.ToString("O"),
                };
                await FindScenario().RunAsync(new CaptureContext(staging, manifest));
                File.WriteAllText(Path.Combine(staging, EvidencePublisher.ManifestFileName), JsonUtility.ToJson(manifest, prettyPrint: true) + "\n");
                var shots = manifest.Shots.Select(shot => (shot.File, shot.Sha256)).ToArray();
                foreach (var warning in publisher.Publish(staging, shots))
                {
                    Debug.LogWarning($"[EvidenceCapture] {warning}");
                }

                staging = null; // 公開フォルダになったので後始末しない
                Debug.Log($"[EvidenceCapture] 公開: {publisher.PublishedDirectory}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                exitCode = 1;
            }

            if (staging != null && !TryDeleteDirectory(staging))
            {
                exitCode = 1;
            }

            Application.Quit(exitCode);
        }

        private static bool TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[EvidenceCapture] 一時フォルダを消せませんでした: {path}: {exception.Message}");
                return false;
            }
        }

        private static string ReadOutputDirectory()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, Argument);
            if (index < 0)
            {
                return null;
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"{Argument} の後に出力先フォルダを指定してください");
            }

            return args[index + 1];
        }

        private static ICaptureScenario FindScenario()
        {
            var scenarios = UnityEngine.Object.FindObjectsByType<MonoBehaviour>()
                .OfType<ICaptureScenario>()
                .ToArray();
            if (scenarios.Length != 1)
            {
                throw new InvalidOperationException($"シーンに ICaptureScenario がちょうど 1 つ必要です（{scenarios.Length} 個）");
            }

            return scenarios[0];
        }
    }
}
