#nullable enable
using System;
using System.Globalization;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <c>dev capture 055</c> の撮影台本。シードは <see cref="EvidenceCapture.Seed"/> に固定し、プレイヤーの側は操縦役（<see cref="Pilot"/>）がキーで打つ。
    /// CPU の探索は Tick の分（<see cref="FlowSettings.PlayoutsPerTick"/>）だけで進むので、毎回同じ画像になる。
    /// タイトル・遊び方・対局中（カーソル）・CPU の考え中・一時停止・死石の確定・結果・コウの拒否を撮る。
    /// </summary>
    public sealed class SekiteiCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public const int MaxTicks = 200_000;
        public const int PilotPlayouts = 300;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            if (capture == null) { throw new ArgumentNullException(nameof(capture)); }
            var game = FindAnyObjectByType<SekiteiGame>();
            if (game == null) { throw new InvalidOperationException("SekiteiGame がありません。"); }
            await capture.WaitUntilAsync(() => game.Hud != null, 10f);
            game.Reinitialize(EvidenceCapture.Seed);
            var flow = game.Flow;
            var mcts = game.Settings.ToMcts();
            capture.Record("pilot", $"プレイヤー（黒）は操縦役（{PilotPlayouts} 回の MCTS をキー入力に変える）");
            capture.Record("playoutsPerTick", game.Settings.PlayoutsPerTick.ToString(CultureInfo.InvariantCulture));
            capture.Record("cpu", $"弱い（{game.Settings.WeakPlayouts} 回）");

            async Awaitable Shoot(string name, string note)
            {
                game.Render();
                var g = flow.Game;
                var state = g == null ? "" : $" (moves={g.MoveCount}, hash=0x{g.StateHash():X8})";
                await capture.ShootAsync(name, note + state);
            }

            void Press(InputButton b)
            {
                flow.Press(b);
                flow.StepTicks(1);
            }

            void PlayMoves(int count)
            {
                var target = flow.Game!.MoveCount + count;
                for (var i = 0; i < MaxTicks && flow.Screen == FlowScreen.Playing && flow.Game.MoveCount < target; i++)
                {
                    Pilot.Act(flow, mcts, PilotPlayouts);
                    flow.StepTicks(1);
                }
            }

            await Shoot("01_title", "タイトル");
            Press(InputButton.Down);
            Press(InputButton.Left);
            await Shoot("02_level", "CPU の強さを選ぶ（弱い。撮影の時間を短くするため）");
            Press(InputButton.Down);
            Press(InputButton.Down);
            Press(InputButton.Confirm);
            await Shoot("03_howto", "遊び方");
            Press(InputButton.Confirm); // タイトルへ（選択は先頭に戻る）
            Press(InputButton.Confirm); // 対局を始める
            if (flow.Screen != FlowScreen.Playing) { throw new InvalidOperationException("対局が始まりません"); }

            PlayMoves(16);
            for (var i = 0; i < MaxTicks && !flow.IsPlayerTurn; i++) { flow.StepTicks(1); }
            Press(InputButton.Left);
            await Shoot("04_playing", "対局中（矢印キーのカーソル・最後の手の印）");

            // プレイヤーが打った直後、CPU が考えている途中。
            Pilot.Act(flow, mcts, PilotPlayouts);
            for (var i = 0; i < MaxTicks && !(flow.IsCpuThinking && flow.BusyTicks == 0 && flow.Cpu!.Search != null && flow.Cpu.Search.Done >= flow.Cpu.Search.Target / 2); i++) { flow.StepTicks(1); }
            await Shoot("05_cpu_thinking", "CPU の考え中（回した数と見込みの勝率）");

            Press(InputButton.Cancel);
            await Shoot("06_pause", "一時停止（再開・投了・タイトルへ）");
            Press(InputButton.Cancel);

            for (var i = 0; i < MaxTicks && flow.Screen == FlowScreen.Playing; i++)
            {
                Pilot.Act(flow, mcts, PilotPlayouts);
                flow.StepTicks(1);
            }

            if (flow.Screen != FlowScreen.Scoring) { throw new InvalidOperationException($"死石の確定に着きません（{flow.Screen}、{flow.Game!.End}）"); }
            await Shoot("07_scoring", "死石の確定（推定した死石を薄く、地を小さな四角で表示）");
            Press(InputButton.Cancel);
            await Shoot("08_result", "結果（面積の内訳）");

            // コウ：白が 1 子を取った直後、黒の即時の取り返しは拒否される。
            var config = game.Settings.ToConfig();
            flow.GameFactory = _ =>
            {
                var g = new GoGame(config, BoardText.Parse(KoPosition), Stone.White);
                if (g.TryPlay(Stone.White, Points.Parse("C2")) != MoveVerdict.Ok) { throw new InvalidOperationException("コウの局面を作れません"); }
                return g;
            };
            Press(InputButton.Confirm); // もう一度
            for (var i = 0; i < 20; i++) { flow.StepTicks(1); }
            flow.Click(Points.Parse("B2"));
            flow.StepTicks(1);
            if (flow.LastRejection != MoveVerdict.Ko) { throw new InvalidOperationException($"コウの拒否になりません（{flow.LastRejection}）"); }
            await Shoot("09_ko", "コウ：取られた直後の 1 子の取り返しは打てない");
            flow.GameFactory = null;
        }

        private const string KoPosition = @"
            . . . . . . . . .
            . . . . . . . . .
            . . . . . . . . .
            . . . . . . . . .
            . . . . . . . . .
            . . . . . . . . .
            . O X . . . . . .
            O X . X . . . . .
            . O X . . . . . .";
    }
}
