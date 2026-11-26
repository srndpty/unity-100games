#nullable enable
using System;
using System.Globalization;
using System.IO;
using Game.App;
using Game.Core;
using Game.Core.Runtime;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <c>dev capture 046</c> の撮影台本。乱数を使わないので、入力と Tick だけで進めて毎回同じ画像になる。
    /// 1 周目（バッド）・2 周目（既読のスキップから真相）・3 周目（通常）を読み、場面ごとに撮る。
    /// </summary>
    public sealed class NovelCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public const int MaxActs = 5000;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            if (capture == null) { throw new ArgumentNullException(nameof(capture)); }
            var game = FindAnyObjectByType<NovelGame>();
            if (game == null) { throw new InvalidOperationException("NovelGame がありません。"); }
            await capture.WaitUntilAsync(() => game.Hud != null, 10f);

            var dir = Path.Combine(Application.temporaryCachePath, "lantern_capture");
            if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
            var quick = new FileSaveStore(Path.Combine(dir, "quick.txt"));
            var read = new FileSaveStore(Path.Combine(dir, "read.txt"));
            game.Reinitialize(quick, read);
            var flow = game.Flow;
            var script = game.Script;
            capture.Record("fingerprint", script.Fingerprint.ToString("X8", CultureInfo.InvariantCulture));
            capture.Record("ticksPerChar", game.Settings.TicksPerChar.ToString(CultureInfo.InvariantCulture));

            async Awaitable Shoot(string name, string note)
            {
                game.Render();
                await capture.ShootAsync(name, $"{note} (tick={flow.Ticks}, hash=0x{flow.StoryHash:X8}, read={flow.ReadCount}/{flow.TextCount})");
            }

            void Tap(InputButton b)
            {
                flow.Tap(b);
                flow.StepTicks(1);
            }

            // 文を全文にして送る。選択肢・結果では止まる。
            void Next()
            {
                if (!flow.IsRevealed) { Tap(InputButton.Confirm); }
                Tap(InputButton.Confirm);
            }

            void Until(Func<bool> done, string what)
            {
                for (int i = 0; i < MaxActs && !done(); i++)
                {
                    if (flow.IsChoice || flow.Screen != FlowScreen.Reading) { throw new InvalidOperationException($"{what} の前に止まりました（{flow.Screen}）"); }
                    Next();
                }

                if (!done()) { throw new InvalidOperationException($"{what} に着きません"); }
            }

            void Choose(int option)
            {
                flow.Click(option);
                flow.StepTicks(1);
            }

            await Shoot("01_title", "タイトル");
            Tap(InputButton.Down);
            Tap(InputButton.Down);
            Tap(InputButton.Confirm);
            await Shoot("02_howto", "操作説明");
            Tap(InputButton.Confirm);

            // 1 周目：バッド（灯台を見に行く → 蓮の部屋の前で待つ → 澪を疑う）
            var bad = Scenario.RouteTo(Compiled(game), "bad");
            if (flow.Screen != FlowScreen.Title || flow.MenuCursor != NovelFlow.TitleNew) { throw new InvalidOperationException("タイトルの「はじめから」に戻っていません"); }
            Tap(InputButton.Confirm);
            flow.StepTicks(18);
            await Shoot("03_narration", "地の文の文字送り（表示の途中）");
            Until(() => flow.SpeakerName.Length > 0, "最初の台詞");
            Tap(InputButton.Confirm);
            await Shoot("04_speech", "台詞と立ち絵");
            Until(() => !flow.Portrait(StageSlot.Left).Character.Equals("", StringComparison.Ordinal) && !flow.Portrait(StageSlot.Right).Character.Equals("", StringComparison.Ordinal), "二人の立ち絵");
            Tap(InputButton.Confirm);
            await Shoot("05_two_portraits", "宿の広間（二人の立ち絵）");
            Until(() => flow.IsChoice, "最初の選択肢");
            await Shoot("06_choice", "選択肢");
            Tap(InputButton.QuickSave);
            await Shoot("07_quicksave", "選択肢でクイックセーブ");
            Choose(1);
            Next();
            Next();
            Tap(InputButton.QuickLoad);
            await Shoot("08_quickload", "クイックロードで選ぶ前に戻った");
            for (int c = 0; c < bad.Count; c++)
            {
                Until(() => flow.IsChoice, $"選択肢 {c + 1}");
                Choose(bad[c]);
            }

            Until(() => flow.Screen == FlowScreen.Result || flow.Background == NovelStage.ResultBackground, "バッドの夜明け");
            Tap(InputButton.Confirm);
            await Shoot("09_bad_dawn", "消えた灯の夜明け");
            for (int i = 0; i < MaxActs && flow.Screen != FlowScreen.Result; i++) { Next(); }
            await Shoot("10_result_bad", "結果（消えた灯）");
            Tap(InputButton.Confirm);

            // 2 周目：既読のスキップから真相へ
            var truth = Scenario.RouteTo(Compiled(game), "truth");
            Tap(InputButton.Confirm);
            flow.StepTicks(80);
            await Shoot("11_read_color", "既読の文（青く表示）");
            flow.Press(InputButton.SkipHold, 1);
            flow.StepTicks(10);
            await Shoot("12_skipping", "既読のスキップ中");
            flow.StepTicks(4000);
            if (!flow.IsChoice) { throw new InvalidOperationException("スキップが最初の選択肢で止まりません"); }
            flow.Release(InputButton.SkipHold, 1);
            flow.StepTicks(1);
            Choose(truth[0]);

            // 1 周目に読んだ枝はスキップで次の選択肢まで進み、まだ読んでいない枝ですぐ止まる。
            Tap(InputButton.SkipToggle);
            flow.StepTicks(4000);
            if (!flow.IsChoice) { throw new InvalidOperationException("スキップが 2 つ目の選択肢で止まりません"); }
            Choose(truth[1]);
            Tap(InputButton.SkipToggle);
            flow.StepTicks(20);
            if (flow.IsCurrentRead || flow.SkipToggled) { throw new InvalidOperationException("未読でスキップが止まりません"); }
            await Shoot("13_skip_stopped", "未読の文でスキップが止まった");
            for (int c = 2; c < truth.Count; c++)
            {
                Until(() => flow.IsChoice, $"選択肢 {c + 1}");
                Choose(truth[c]);
            }

            Until(() => flow.Background == "storeroom", "油の倉庫");
            Until(() => flow.SpeakerName == "宗助", "宗助の台詞");
            Tap(InputButton.Confirm);
            await Shoot("14_truth_storeroom", "倉庫で宗助を見つける");
            for (int i = 0; i < MaxActs && flow.Screen != FlowScreen.Result; i++) { Next(); }
            await Shoot("15_result_truth", "結果（灯は戻る）");
            Tap(InputButton.Confirm);

            // 3 周目：通常
            var normal = Scenario.RouteTo(Compiled(game), "normal");
            Tap(InputButton.Confirm);
            for (int c = 0; c < normal.Count; c++)
            {
                Until(() => flow.IsChoice, $"選択肢 {c + 1}");
                if (c == 2) { await Shoot("16_last_choice", "午前三時の広間の選択肢"); }
                Choose(normal[c]);
            }

            for (int i = 0; i < MaxActs && flow.Screen != FlowScreen.Result; i++) { Next(); }
            await Shoot("17_result_normal", "結果（夜明けの船、3 つの結末に到達）");
            if (flow.EndingsReached != 3) { throw new InvalidOperationException($"到達した結末が {flow.EndingsReached}"); }
            Tap(InputButton.Confirm);

            if (!quick.TryWrite("kind=quick\nversion=1\nbroken", out var failure)) { throw new InvalidOperationException(failure); }
            game.Reinitialize(quick, read);
            flow = game.Flow;
            await Shoot("18_title_broken_save", "壊れたクイックセーブの案内（つづきからを選べない）");
            quick.Delete();
            read.Delete();
        }

        private static Game.Core.Script.CompileResult Compiled(NovelGame game) => game.Compiled;
    }
}
