#nullable enable
using System;
using System.Globalization;
using System.IO;
using Game.App;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <c>dev capture 050</c> の撮影台本。シードは <see cref="EvidenceCapture.Seed"/> に固定し、先輩の方針の操縦役で
    /// 入力（クリック・キー）と Tick だけを進めるので、毎回同じ画像になる。
    /// 1 日目の朝・結果・文字送り・出会い・選択肢・詳細・日曜の拒否・つづきから・夏祭り・流星群・ED・結果画面・壊れたセーブを撮る。
    /// </summary>
    public sealed class SummerCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public const int MaxActs = 20000;

        /// <summary>操縦役のシード（撮影の再現に使う）。</summary>
        public const int PilotSeed = 7;

        /// <summary>操縦役の 1 手のあとに進める Tick。</summary>
        public const int StepTicks = 20;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            if (capture == null) { throw new ArgumentNullException(nameof(capture)); }
            var game = FindAnyObjectByType<SummerGame>();
            if (game == null) { throw new InvalidOperationException("SummerGame がありません。"); }
            await capture.WaitUntilAsync(() => game.Hud != null, 10f);

            var dir = Path.Combine(Application.temporaryCachePath, "meteor_summer_capture");
            if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
            var store = new FileSaveStore(Path.Combine(dir, "save.txt"));
            game.Reinitialize(store, EvidenceCapture.Seed);
            var flow = game.Flow;
            var book = game.Book;
            var pilot = new Autopilot(book, PilotPolicy.Sumi, PilotSeed);
            capture.Record("fingerprint", book.Fingerprint.ToString("X8", CultureInfo.InvariantCulture));
            capture.Record("pilot", $"Sumi (seed {PilotSeed})");
            capture.Record("ticksPerChar", game.Settings.TicksPerChar.ToString(CultureInfo.InvariantCulture));

            async Awaitable Shoot(string name, string note)
            {
                game.Render();
                await capture.ShootAsync(name, $"{note} (day={flow.Day}, tick={flow.Ticks}, hash=0x{flow.SummerHash:X8})");
            }

            void Tap(InputButton b)
            {
                flow.Press(b);
                flow.StepTicks(1);
            }

            // 操縦役の 1 手。会話は全文を出してから送る。
            void Step()
            {
                if (flow.InText && !flow.IsChoice && !flow.IsRevealed)
                {
                    Tap(InputButton.Confirm);
                    return;
                }

                var expected = pilot.Drive(flow);

                // 実際に遊ぶ間合い（1 手に 1/3 秒）で進め、案内の期限を実時間と同じに減らす。
                flow.StepTicks(StepTicks);
                if (expected >= 0 && flow.Commands != expected) { throw new InvalidOperationException($"操縦役の命令が実行されません（{flow.Commands}/{expected}）"); }
            }

            void Until(Func<bool> done, string what)
            {
                for (int i = 0; i < MaxActs && !done(); i++)
                {
                    if (flow.Screen == FlowScreen.Result) { throw new InvalidOperationException($"{what} の前に終わりました"); }
                    Step();
                }

                if (!done()) { throw new InvalidOperationException($"{what} に着きません"); }
            }

            await Shoot("01_title", "タイトル");
            Tap(InputButton.Down);
            Tap(InputButton.Down);
            Tap(InputButton.Confirm);
            await Shoot("02_howto", "操作説明");
            Tap(InputButton.Confirm);
            Tap(InputButton.Confirm);
            await Shoot("03_morning", "1 日目の朝（行動の選択と能力値）");

            Step();
            await Shoot("04_report", "行動の結果（段階と能力値の増減）");
            Tap(InputButton.Confirm);
            flow.StepTicks(12);
            await Shoot("05_text_reveal", "1 日目の出来事の会話（文字送りの途中）");

            Until(() => flow.SceneIndex == book.EventIndex("e_sumi_first") && flow.SpeakerId.Length > 0 && flow.IsRevealed, "先輩との出会い");
            await Shoot("06_meet_sumi", "天文部で先輩に会う（立ち絵と名札）");
            Until(() => flow.IsChoice, "出会いの選択肢");
            await Shoot("07_choice", "選択肢");

            Until(() => flow.View == PlayView.Morning && flow.Day == 7, "7 日目（日曜）の朝");
            Tap(InputButton.Detail);
            await Shoot("08_detail", "能力値の詳細（Tab）");
            Tap(InputButton.Detail);
            flow.Click(book.ActionIndex("library"));
            flow.StepTicks(1);
            await Shoot("09_sunday", "日曜は図書室が閉まっている（拒否と案内）");

            Until(() => flow.View == PlayView.Morning && flow.Day == 10, "10 日目の朝");
            Tap(InputButton.Cancel);
            await Shoot("10_pause", "一時停止");
            flow.Click(SummerFlow.PauseTitle);
            flow.StepTicks(1);
            await Shoot("11_title_continue", "タイトル（つづきから：10 日目の朝から）");
            flow.Click(SummerFlow.TitleContinue);
            flow.StepTicks(1);
            if (flow.Day != 10 || flow.View != PlayView.Morning) { throw new InvalidOperationException("つづきから 10 日目の朝に戻りません"); }

            Until(() => flow.SceneIndex == book.EventIndex("e_sumi_roof") && flow.IsRevealed, "屋上の月");
            await Shoot("12_roof", "屋上で月を見る");
            Until(() => flow.IsChoice && flow.SceneIndex == book.EventIndex("e_festival_sumi"), "夏祭りの誘い");
            await Shoot("13_festival", "20 日目の夏祭り（先輩の誘い）");
            Until(() => flow.SceneIndex == book.EventIndex("e_meteor") && flow.IsRevealed, "流星群");
            await Shoot("14_meteor", "30 日目の夜の流星群");
            Until(() => flow.View == PlayView.Ending && flow.SpeakerId.Length > 0 && flow.IsRevealed, "ED の台詞");
            await Shoot("15_ending", "ED（先輩と星を数えた夏）");
            for (int i = 0; i < MaxActs && flow.Screen != FlowScreen.Result; i++) { Step(); }
            if (book.Endings[flow.EndingIndex].Id != "sumi") { throw new InvalidOperationException($"ED が {flow.EndingName}"); }
            await Shoot("16_result", "結果画面");
            Tap(InputButton.Confirm);

            if (!store.TryWrite("kind=summer\nversion=1\nbroken", out var failure)) { throw new InvalidOperationException(failure); }
            game.Reinitialize(store, EvidenceCapture.Seed);
            flow = game.Flow;
            await Shoot("17_title_broken_save", "壊れたセーブの案内（つづきからを選べない）");
            store.Delete();
        }
    }
}
