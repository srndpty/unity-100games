using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Game.Unity.Evidence
{
    /// <summary>
    /// 起動引数 <c>-capture &lt;出力先フォルダ&gt;</c> があるとき、シーン上の <see cref="ICaptureScenario"/> を実行して
    /// スクリーンショットを撮り、終了する（`dev capture NNN` から使う）。
    /// 撮影中は <see cref="IsRunning"/> が true になる。ゲーム側はプレイヤー入力を無視して手元の操作が紛れ込まないようにし、音も鳴らさない。
    /// </summary>
    public static class EvidenceCapture
    {
        private const string Argument = "-capture";

        // 起動引数は実行中に変わらないので一度だけ読む。Awake からも参照できるよう、シーン読み込みの前後に依存しない形にする。
        // 撮影しない通常起動では null。
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
            _ = RunAsync(OutputDirectory);
        }

        private static async Awaitable RunAsync(string directory)
        {
            var exitCode = 0;
            try
            {
                Directory.CreateDirectory(directory);
                await FindScenario().RunAsync(new CaptureContext(directory));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                exitCode = 1;
            }

            Application.Quit(exitCode);
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
