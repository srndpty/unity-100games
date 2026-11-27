using System;
using System.Linq;
using Game.Core.Data;
using Game.Core.Play;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class FlowTests
    {
        private static SummerBook B => AppLab.Book;

        private static SummerFlow Started(MemorySaveStore? store = null, int seed = 1, FlowSettings? settings = null)
        {
            var f = AppLab.Flow(store, seed, settings);
            AppLab.ClickNow(f, SummerFlow.TitleNew);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Playing));
            return f;
        }

        /// <summary>操縦役で、指定の日の朝まで進める。</summary>
        private static void DriveToMorning(SummerFlow f, Autopilot pilot, int day)
        {
            for (int i = 0; i < 5000 && !(f.View == PlayView.Morning && f.Day == day); i++)
            {
                pilot.Drive(f);
                f.FlushIntents();
            }

            Assert.That(f.Day, Is.EqualTo(day));
            Assert.That(f.View, Is.EqualTo(PlayView.Morning));
        }

        [Test]
        public void Title_HasItems_AndContinueIsDisabledWithoutSave()
        {
            var f = AppLab.Flow();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(f.TitleItems.Count, Is.EqualTo(4));
            Assert.That(f.TitleItems[SummerFlow.TitleContinue].Enabled, Is.False);
            AppLab.ClickNow(f, SummerFlow.TitleContinue);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(f.Cues.Reject, Is.EqualTo(1));
            Assert.That(f.Notice, Is.EqualTo("セーブがありません"));
        }

        [Test]
        public void HowTo_AndBack()
        {
            var f = AppLab.Flow();
            AppLab.ClickNow(f, SummerFlow.TitleHowTo);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.HowTo));
            AppLab.Tap(f, InputButton.Confirm);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
        }

        [Test]
        public void NewSummer_StartsOnDayOne_AndAutoSaves()
        {
            var store = new MemorySaveStore();
            var f = Started(store);
            Assert.That(f.Day, Is.EqualTo(1));
            Assert.That(f.WeekdayName, Is.EqualTo("月"));
            Assert.That(f.View, Is.EqualTo(PlayView.Morning));
            Assert.That(f.Actions.Count, Is.EqualTo(B.Actions.Count));
            Assert.That(store.Writes, Is.EqualTo(1), "日の始めに自動セーブ");
            Assert.That(f.Background, Is.EqualTo("room"));
        }

        [TestCase(PilotPolicy.Sumi, "sumi")]
        [TestCase(PilotPolicy.Shiori, "shiori")]
        [TestCase(PilotPolicy.Drift, "summer")]
        public void FullRound_TitleToEndingToTitle(PilotPolicy policy, string ending)
        {
            var store = new MemorySaveStore();
            var f = AppLab.Flow(store, seed: 3);
            AppLab.DriveToResult(f, new Autopilot(B, policy, 3));
            Assert.That(B.Endings[f.EndingIndex].Id, Is.EqualTo(ending));
            Assert.That(f.EndingName, Is.EqualTo(B.Endings[f.EndingIndex].Name));
            Assert.That(store.Content, Is.Null, "ED に着いたらセーブを消す");
            Assert.That(store.Writes, Is.EqualTo(B.Days), "朝ごとに 1 回");
            Assert.That(f.Cues.Ending, Is.EqualTo(1));

            // 結果画面の後の入力（行動のクリックなど）は夏休みを動かさない。
            var commands = f.Commands;
            AppLab.Tap(f, InputButton.Up);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(f.Commands, Is.EqualTo(commands));
            AppLab.Tap(f, InputButton.Confirm);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(f.TitleItems[SummerFlow.TitleContinue].Enabled, Is.False);
            Assert.That(f.SummerHash, Is.EqualTo(0u));
        }

        [Test]
        public void Rematch_UsesANewSeed()
        {
            var f = Started(seed: 10);
            var first = f.Summer!.Seed;
            AppLab.Tap(f, InputButton.Cancel);
            AppLab.ClickNow(f, SummerFlow.PauseTitle);
            AppLab.ClickNow(f, SummerFlow.TitleNew);
            Assert.That(f.Summer!.Seed, Is.Not.EqualTo(first));
            Assert.That(f.Day, Is.EqualTo(1));
        }

        [Test]
        public void Continue_ResumesTheSavedMorning()
        {
            var store = new MemorySaveStore();
            var f = Started(store, seed: 4);
            var pilot = new Autopilot(B, PilotPolicy.Sumi, 4);
            DriveToMorning(f, pilot, 6);
            var hash = f.SummerHash;
            AppLab.Tap(f, InputButton.Cancel);
            AppLab.ClickNow(f, SummerFlow.PauseTitle);
            Assert.That(f.TitleItems[SummerFlow.TitleContinue].Enabled, Is.True);
            Assert.That(f.TitleItems[SummerFlow.TitleContinue].Detail, Is.EqualTo("6 日目の朝から"));

            // 新しい画面の流れ（起動し直し）から再開しても同じ状態。
            var g = AppLab.Flow(store, seed: 999);
            AppLab.ClickNow(g, SummerFlow.TitleContinue);
            Assert.That(g.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(g.SummerHash, Is.EqualTo(hash));

            // 同じ操縦役で両方を最後まで進めると同じ結末。
            AppLab.ClickNow(f, SummerFlow.TitleContinue);
            AppLab.DriveToResult(f, new Autopilot(B, PilotPolicy.Sumi, 4));
            AppLab.DriveToResult(g, new Autopilot(B, PilotPolicy.Sumi, 4));
            Assert.That(g.SummerHash, Is.EqualTo(f.SummerHash));
        }

        [Test]
        public void BrokenSave_IsExplainedOnTheTitle()
        {
            var store = new MemorySaveStore { Content = "kind=summer\nversion=1\n" };
            var f = AppLab.Flow(store);
            var item = f.TitleItems[SummerFlow.TitleContinue];
            Assert.That(item.Enabled, Is.False);
            Assert.That(item.Detail.StartsWith("セーブを読めませんでした", StringComparison.Ordinal), Is.True, item.Detail);

            var failing = new MemorySaveStore { ReadFailure = "ロックされています" };
            var g = AppLab.Flow(failing);
            Assert.That(g.TitleItems[SummerFlow.TitleContinue].Detail, Does.EndWith("ロックされています"));
        }

        [Test]
        public void WriteFailure_IsANotice_AndPlayGoesOn()
        {
            var store = new MemorySaveStore { WriteFailure = "容量が足りません" };
            var f = Started(store);
            Assert.That(f.Notice, Is.EqualTo("セーブできませんでした：容量が足りません"));
            AppLab.ClickNow(f, B.ActionIndex("study"));
            Assert.That(f.View, Is.EqualTo(PlayView.Report));
        }

        [Test]
        public void Action_ShowsReport_ThenNextStage()
        {
            var f = Started(seed: 2);
            AppLab.ClickNow(f, B.ActionIndex("study"));
            Assert.That(f.Commands, Is.EqualTo(1));
            Assert.That(f.View, Is.EqualTo(PlayView.Report));
            Assert.That(f.ReportTitle.StartsWith("勉強 — ", StringComparison.Ordinal), Is.True, f.ReportTitle);
            Assert.That(f.ReportChanges, Has.Some.StartsWith("学力 +"));
            Assert.That(f.ReportChanges, Has.Some.EqualTo("疲労 +10"));
            AppLab.Tap(f, InputButton.Confirm);
            // 1 日目の夜は始まりのイベント。
            Assert.That(f.View, Is.EqualTo(PlayView.Scene));
            Assert.That(f.SceneIndex, Is.EqualTo(B.EventIndex("e_open")).Or.EqualTo(B.EventIndex("e_study_good")));
        }

        [Test]
        public void UnavailableAction_IsRejected_WithReason()
        {
            var f = Started(seed: 5);
            var pilot = new Autopilot(B, PilotPolicy.Drift, 5);
            DriveToMorning(f, pilot, 7);
            Assert.That(f.WeekdayName, Is.EqualTo("日"));
            var library = B.ActionIndex("library");
            Assert.That(f.Actions[library].Enabled, Is.False);
            var commands = f.Commands;
            var hash = f.SummerHash;
            AppLab.ClickNow(f, library);
            Assert.That(f.Commands, Is.EqualTo(commands));
            Assert.That(f.SummerHash, Is.EqualTo(hash));
            Assert.That(f.Notice, Is.EqualTo("日曜日は選べない"));
            Assert.That(f.View, Is.EqualTo(PlayView.Morning));
        }

        [Test]
        public void Keyboard_MovesTheCursor_AndConfirms()
        {
            var f = Started(seed: 6);
            Assert.That(f.MenuCursor, Is.EqualTo(0));
            AppLab.Tap(f, InputButton.Up);
            Assert.That(f.MenuCursor, Is.EqualTo(B.Actions.Count - 1), "上で折り返す");
            AppLab.Tap(f, InputButton.Down);
            AppLab.Tap(f, InputButton.Down);
            Assert.That(f.MenuCursor, Is.EqualTo(1));
            AppLab.Tap(f, InputButton.Confirm);
            Assert.That(f.Summer!.Action, Is.EqualTo(1));
        }

        [Test]
        public void ClickFromAnOldScreen_IsDropped()
        {
            var f = Started(seed: 7);
            var morning = f.Version;
            f.Click(B.ActionIndex("study"));
            f.Enqueue(Intent.Click(B.ActionIndex("rest"), morning));
            f.FlushIntents();
            Assert.That(f.Commands, Is.EqualTo(1), "2 回目は朝の画面で押したクリック");
            Assert.That(f.Summer!.Action, Is.EqualTo(B.ActionIndex("study")));
            Assert.That(f.View, Is.EqualTo(PlayView.Report));
        }

        [Test]
        public void TextReveal_FirstClickShowsAll_SecondClickOnTheSameFrameIsDropped()
        {
            var f = Started(seed: 8, settings: new FlowSettings { TicksPerChar = 2 });
            AppLab.ClickNow(f, B.ActionIndex("rest"));
            AppLab.Tap(f, InputButton.Confirm);
            Assert.That(f.View, Is.EqualTo(PlayView.Scene));
            Assert.That(f.IsRevealed, Is.False);
            Assert.That(f.VisibleText.Length, Is.EqualTo(1));
            f.StepTicks(4);
            Assert.That(f.VisibleChars, Is.EqualTo(3));

            var v = f.Version;
            f.Enqueue(Intent.Click(-1, v));
            f.Enqueue(Intent.Click(-1, v));
            f.FlushIntents();
            Assert.That(f.IsRevealed, Is.True);
            Assert.That(f.VisibleText, Is.EqualTo(f.FullText), "1 回目で全文、2 回目は送らない");
            var text = f.FullText;
            AppLab.ClickNow(f, -1);
            Assert.That(f.FullText, Is.Not.EqualTo(text), "全文が出ていれば送る");
        }

        [Test]
        public void Choice_ByKeyboardAndClick()
        {
            var f = Started(seed: 9);
            AppLab.ClickNow(f, B.ActionIndex("club"));
            AppLab.Tap(f, InputButton.Confirm);
            Assert.That(f.SceneIndex, Is.EqualTo(B.EventIndex("e_sumi_first")));
            for (int i = 0; i < 5 && !f.IsChoice; i++) { AppLab.Tap(f, InputButton.Confirm); }
            Assert.That(f.IsChoice, Is.True);
            Assert.That(f.Options, Has.Count.EqualTo(2));
            Assert.That(f.MenuItems, Has.Count.EqualTo(2));
            AppLab.Tap(f, InputButton.Down);
            Assert.That(f.MenuCursor, Is.EqualTo(1));
            var commands = f.Commands;
            AppLab.ClickNow(f, 5);
            Assert.That(f.Commands, Is.EqualTo(commands), "範囲外のクリックは何もしない");
            AppLab.ClickNow(f, 0);
            Assert.That(f.Commands, Is.EqualTo(commands + 1));
            Assert.That(f.Summer!.Flag(B.FlagIndex("met_sumi")), Is.True);
            Assert.That(f.Notice, Does.Contain("星野澄 +"), "選択の効果を案内する");
        }

        [Test]
        public void Speaker_AndFace_FromTheLine()
        {
            var f = Started(seed: 9);
            AppLab.ClickNow(f, B.ActionIndex("club"));
            AppLab.Tap(f, InputButton.Confirm);
            Assert.That(f.SpeakerId, Is.EqualTo("sumi"));
            Assert.That(f.SpeakerName, Is.EqualTo("星野澄"));
            Assert.That(f.SpeakerColor, Is.EqualTo(0x9fc8ffu));
            Assert.That(f.Face, Is.EqualTo("normal"));
            Assert.That(f.Background, Is.EqualTo("club"));
            AppLab.Tap(f, InputButton.Confirm);
            Assert.That(f.Face, Is.EqualTo("smile"));
        }

        [Test]
        public void FocusLoss_Pauses_AndNeedsManualResume()
        {
            var f = Started(seed: 11);
            f.Click(B.ActionIndex("study"));
            f.OnFocusChanged(false);
            f.FlushIntents();
            Assert.That(f.Commands, Is.EqualTo(0), "喪失の前に積んだ入力も捨てる");
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Paused));
            f.Press(InputButton.Cancel);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Paused), "フォーカスのない間の入力は当てない");
            f.OnFocusChanged(true);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Paused), "戻っても一時停止のまま");
            AppLab.Tap(f, InputButton.Cancel);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Playing));
        }

        [Test]
        public void Pause_StopsTheClockAndPlayTime()
        {
            var f = Started(new MemorySaveStore { WriteFailure = "x" }, seed: 12);
            Assert.That(f.Notice.Length, Is.GreaterThan(0));
            f.UpdateFrame(1.0);
            var ticks = f.Ticks;
            var seconds = f.PlaySeconds;
            Assert.That(seconds, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(ticks, Is.EqualTo(8), "1 フレームの Tick の上限");
            AppLab.Tap(f, InputButton.Cancel);
            f.UpdateFrame(10.0);
            Assert.That(f.Ticks, Is.EqualTo(ticks));
            Assert.That(f.PlaySeconds, Is.EqualTo(seconds));
            Assert.That(f.Notice.Length, Is.GreaterThan(0), "一時停止の間は案内の期限も減らない");
            AppLab.Tap(f, InputButton.Cancel);
            f.UpdateFrame(5.0);
            Assert.That(f.PlaySeconds, Is.EqualTo(6.0).Within(1e-9), "遊んだ時間は Tick の上限で切らない");
        }

        [Test]
        public void Detail_Toggles()
        {
            var f = Started();
            AppLab.Tap(f, InputButton.Detail);
            Assert.That(f.DetailOpen, Is.True);
            AppLab.Tap(f, InputButton.Detail);
            Assert.That(f.DetailOpen, Is.False);
        }

        [Test]
        public void Stats_ForTheHud()
        {
            var f = Started();
            Assert.That(f.Stats.Count, Is.EqualTo(B.Stats.Count));
            var sumi = f.Stats[B.StatIndex("sumi")];
            Assert.That(sumi.IsHeart, Is.True);
            Assert.That(sumi.Hearts, Is.EqualTo(0));
            Assert.That(new StatLine("x", "x", 100, 0, 100, true).Hearts, Is.EqualTo(5));
            Assert.That(new StatLine("x", "x", 39, 0, 100, true).Hearts, Is.EqualTo(1));
            Assert.That(new StatLine("x", "x", 40, 0, 100, true).Hearts, Is.EqualTo(2));
        }

        [Test]
        public void InvalidSettings_AreRejected()
        {
            Assert.Throws<ArgumentException>(() => AppLab.Flow(settings: new FlowSettings { TickRate = 0 }));
            Assert.Throws<ArgumentException>(() => AppLab.Flow(settings: new FlowSettings { TicksPerChar = 31 }));
        }

        // ---- レビュー対応 ----

        /// <summary>P2-1：結果の段は行動した日を出し、能力値は会話の始めの効果を会話が終わるまで出さない。</summary>
        [Test]
        public void Report_ShowsTheDayOfTheAction_AndSceneEffectsWaitForTheScene()
        {
            var seen = new int[3];
            foreach (var policy in new[] { PilotPolicy.Sumi, PilotPolicy.Shiori, PilotPolicy.Drift })
            {
                for (int seed = 0; seed < 6; seed++)
                {
                    var f = Started(seed: seed);
                    var pilot = new Autopilot(B, policy, seed);
                    for (int step = 0; step < 3000 && f.Screen != FlowScreen.Result; step++)
                    {
                        if (f.View != PlayView.Morning)
                        {
                            pilot.Drive(f);
                            f.FlushIntents();
                            continue;
                        }

                        var day = f.Day;
                        var weekday = f.WeekdayName;
                        var before = f.Stats.Select(s => s.Value).ToArray();
                        pilot.Drive(f);
                        f.FlushIntents();
                        Assert.That(f.View, Is.EqualTo(PlayView.Report));
                        Assert.That(f.Day, Is.EqualTo(day), "結果の段は行動した日");
                        Assert.That(f.WeekdayName, Is.EqualTo(weekday));

                        // 結果の段の能力値は、行動の効果だけが当たった値（会話の始めの効果はまだ出さない）。
                        var actionDelta = new int[before.Length];
                        foreach (var e in f.Summer!.Events)
                        {
                            if (e.Kind == SummerEventKind.SceneStarted) { break; }
                            if (e.Kind == SummerEventKind.StatChanged) { actionDelta[e.Index] += e.Value; }
                        }

                        Assert.That(f.Stats.Select(s => s.Value).ToArray(), Is.EqualTo(before.Select((v, i) => v + actionDelta[i]).ToArray()));
                        var kind = f.Summer.Phase == Phase.Action ? 0 : B.Events[f.Summer.Scene].Slot == Slot.After ? 1 : 2;
                        seen[kind]++;
                    }

                    Assert.That(f.Screen, Is.EqualTo(FlowScreen.Result));
                }
            }

            Assert.That(seen, Has.All.GreaterThan(0), "出来事のない日・行動の後のイベントの日・夜だけイベントの日を全て通った");
        }

        [Test]
        public void SceneDoEffects_AppearWhenTheSceneEnds()
        {
            // 勉強の後の「問題が解けた」（@do 学力 +2）が出る日を探し、会話の間は表示の学力に含めない。
            var study = B.StatIndex("study");
            for (int seed = 0; seed < 40; seed++)
            {
                var f = Started(seed: seed);
                AppLab.ClickNow(f, B.ActionIndex("study"));
                AppLab.Tap(f, InputButton.Confirm);
                if (f.SceneIndex != B.EventIndex("e_study_good")) { continue; }
                var actual = f.Summer!.Stat(study);
                Assert.That(f.Stats[study].Value, Is.EqualTo(actual - 2), "会話の間は @do を出さない");
                AppLab.Tap(f, InputButton.Confirm);
                Assert.That(f.Notice, Does.Contain("学力 +2"));
                Assert.That(f.Stats[study].Value, Is.EqualTo(actual), "会話が終わったら出す");
                return;
            }

            Assert.Fail("勉強の後の出来事が出るシードがない");
        }

        /// <summary>P2-3：つづきからは、セーブした朝までの遊んだ時間から数える。</summary>
        [Test]
        public void Continue_RestoresPlayTime()
        {
            var store = new MemorySaveStore();
            var f = Started(store, seed: 4);
            var pilot = new Autopilot(B, PilotPolicy.Sumi, 4);
            for (int i = 0; i < 5000; i++)
            {
                pilot.Drive(f);
                f.FlushIntents();
                if (f.View == PlayView.Morning && f.Day == 3) { break; }
                f.UpdateFrame(0.5);
            }

            var played = f.PlaySeconds;
            Assert.That(played, Is.GreaterThan(1.0));
            var g = AppLab.Flow(store, seed: 99);
            AppLab.ClickNow(g, SummerFlow.TitleContinue);
            Assert.That(g.PlaySeconds, Is.EqualTo(played).Within(0.001), "朝のセーブの時点までの時間");
            g.UpdateFrame(2.0);
            Assert.That(g.PlaySeconds, Is.EqualTo(played + 2.0).Within(0.001));
        }

        /// <summary>P2-4：ED を読み終えるまでセーブを消さない。</summary>
        [Test]
        public void Save_IsKeptWhileReadingTheEnding()
        {
            var store = new MemorySaveStore();
            var f = Started(store, seed: 2);
            var pilot = new Autopilot(B, PilotPolicy.Drift, 2);
            for (int i = 0; i < 5000 && f.View != PlayView.Ending; i++)
            {
                pilot.Drive(f);
                f.FlushIntents();
            }

            Assert.That(f.View, Is.EqualTo(PlayView.Ending));
            Assert.That(store.Content, Is.Not.Null, "ED の途中で終了しても 30 日目の朝から読み直せる");
            Assert.That(AppLab.Flow(store).TitleItems[SummerFlow.TitleContinue].Detail, Is.EqualTo("30 日目の朝から"));
            AppLab.DriveToResult(f, pilot);
            Assert.That(store.Content, Is.Null, "読み終えたら消す");
        }

        /// <summary>P2-6：詳細はモーダル。開いている間はクリック・Enter・選択を背後に通さない。</summary>
        [Test]
        public void Detail_IsModal()
        {
            var f = Started(seed: 9);
            AppLab.Tap(f, InputButton.Detail);
            var hash = f.SummerHash;
            var version = f.Version;
            AppLab.ClickNow(f, B.ActionIndex("study"));
            AppLab.Tap(f, InputButton.Confirm);
            AppLab.Tap(f, InputButton.Down);
            Assert.That(f.Commands, Is.EqualTo(0));
            Assert.That(f.SummerHash, Is.EqualTo(hash));
            Assert.That(f.MenuCursor, Is.EqualTo(0), "カーソルも動かない");
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Playing));

            // Esc は一時停止ではなく詳細を閉じる。閉じる前の版のクリックは通さない。
            AppLab.Tap(f, InputButton.Cancel);
            Assert.That(f.DetailOpen, Is.False);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Playing));
            f.Enqueue(Intent.Click(B.ActionIndex("study"), version));
            f.FlushIntents();
            Assert.That(f.Commands, Is.EqualTo(0), "開いている間の版のクリック");

            // 会話の途中で開いても送らない。
            AppLab.ClickNow(f, B.ActionIndex("club"));
            AppLab.Tap(f, InputButton.Confirm);
            Assert.That(f.View, Is.EqualTo(PlayView.Scene));
            var text = f.FullText;
            AppLab.Tap(f, InputButton.Detail);
            AppLab.ClickNow(f, -1);
            AppLab.Tap(f, InputButton.Confirm);
            Assert.That(f.FullText, Is.EqualTo(text));
            AppLab.Tap(f, InputButton.Detail);
            Assert.That(f.DetailOpen, Is.False);
        }

        /// <summary>P3-1：操縦役は上限の命令の数で ED に着かなければ例外にする。</summary>
        [Test]
        public void Autopilot_Throws_WhenTheLimitEndsBeforeTheEnding()
        {
            var s = new Summer(B, 1);
            Assert.Throws<InvalidOperationException>(() => new Autopilot(B, PilotPolicy.Sumi, 1).Play(s, 10));
            Assert.That(s.Phase, Is.Not.EqualTo(Phase.Ended));
        }

        [Test]
        public void Quit_FromTitle()
        {
            var f = AppLab.Flow();
            AppLab.ClickNow(f, SummerFlow.TitleQuit);
            Assert.That(f.QuitRequested, Is.True);
        }
    }
}
