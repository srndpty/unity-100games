using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Game.Unity.Evidence
{
    /// <summary>
    /// 起動引数 <c>-capture &lt;出力先フォルダ&gt;</c> があるとき、シーン上の <see cref="ICaptureScenario"/> を実行して
    /// スクリーンショットを撮り、終了する（`dev capture NNN` から使う）。
    /// 撮影中は <see cref="IsRunning"/> が true になる。ゲーム側は乱数に <see cref="Seed"/> を使い、
    /// 実時間やプレイヤー入力では進めず台本に進行を任せ、音も鳴らさない。これで同じビルドなら毎回同じ画像になる。
    /// 画像と manifest.json は一時フォルダに書き、全部撮れたときだけ出力先へ移す（途中で失敗しても新旧が混ざらない）。
    /// </summary>
    public static class EvidenceCapture
    {
        /// <summary>撮影時に使う固定シード。</summary>
        public const int Seed = 1;

        private const string Argument = "-capture";
        private const string ManifestFile = "manifest.json";

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

        private static async Awaitable RunAsync(string directory)
        {
            var staging = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".staging";
            var exitCode = 0;
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }

                Directory.CreateDirectory(staging);
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
                File.WriteAllText(Path.Combine(staging, ManifestFile), JsonUtility.ToJson(manifest, prettyPrint: true) + "\n");
                Commit(staging, directory);
                Debug.Log($"[EvidenceCapture] 保存: {directory}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                exitCode = 1;
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }

            Application.Quit(exitCode);
        }

        private static void Commit(string staging, string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (var file in Directory.GetFiles(staging))
            {
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)), overwrite: true);
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
