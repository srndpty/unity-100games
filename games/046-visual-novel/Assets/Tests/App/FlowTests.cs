using System.Linq;
using Game.Core.Save;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>画面の流れ・文字送り・選択・クイックセーブ・結果と再プレイ。</summary>
    public sealed class FlowTests
    {
        // ---- 文字送り ----

        [Test]
        public void TextRevealsOverTicksAndConfirmShowsAll()
        {
            var f = AppLab.Started(settings: new FlowSettings { TicksPerChar = 2 });
            Assert.That(f.VisibleChars, Is.EqualTo(1));
            f.StepTicks(4);
            Assert.That(f.VisibleChars, Is.EqualTo(3));
            var hash = f.StoryHash;
            f.Tap(InputButton.Confirm);
            f.FlushIntents();
            Assert.That(f.IsRevealed, Is.True);
            Assert.That(f.VisibleText, Is.EqualTo(f.FullText));
            Assert.That(f.StoryHash, Is.EqualTo(hash), "表示途中の決定は全文を出すだけ");
            Assert.That(f.ReadCount, Is.EqualTo(0));
            f.Tap(InputButton.Confirm);
            f.FlushIntents();
            Assert.That(f.StoryHash, Is.Not.EqualTo(hash));
            Assert.That(f.ReadCount, Is.EqualTo(1), "送った文が既読になる");
            Assert.That(f.VisibleChars, Is.EqualTo(1), "次の文は最初から");
        }

        [Test]
        public void SpeakerAndStageAreExposed()
        {
            var f = AppLab.Started();
            Assert.That(f.Background, Is.EqualTo("lighthouse"));
            Assert.That(f.SpeakerName, Is.EqualTo(""));
            for (int i = 0; i < 50 && f.SpeakerName.Length == 0; i++) { AppLab.Next(f); }
            Assert.That(f.SpeakerName, Is.EqualTo("宗助"));
            Assert.That(f.SpeakerColor, Is.EqualTo(0xB4D89Cu));
            Assert.That(f.Portrait(Game.Core.Runtime.StageSlot.Center), Is.EqualTo(("sosuke", "calm")));
        }

        [Test]
        public void ChoiceByKeysAndCursorWraps()
        {
            var f = AppLab.Started();
            AppLab.ToChoice(f);
            Assert.That(f.Options.Count, Is.EqualTo(3));
            f.Tap(InputButton.Up);
            f.FlushIntents();
            Assert.That(f.ChoiceCursor, Is.EqualTo(2));
            f.Tap(InputButton.Down);
            f.Tap(InputButton.Down);
            f.Tap(InputButton.Confirm);
            f.FlushIntents();
            Assert.That(f.IsChoice, Is.False);
            Assert.That(f.Runner!.Value("trust"), Is.EqualTo(1), "2 つ目（澪と部屋を調べる）を選んだ");
        }

        // ---- 二重の入力 ----

        [Test]
        public void TwoClicksInOneFrameAdvanceOnce()
        {
            var f = AppLab.Started(settings: new FlowSettings { TicksPerChar = 0 });
            var v = f.Version;
            f.Enqueue(Intent.Click(-1, v));
            f.Enqueue(Intent.Click(-1, v));
            f.FlushIntents();
            Assert.That(f.ReadCount, Is.EqualTo(1));
        }

        [Test]
        public void DoubleClickOnOptionChoosesOnce()
        {
            var f = AppLab.Started();
            AppLab.ToChoice(f);
            var v = f.Version;
            f.Enqueue(Intent.Click(0, v));
            f.Enqueue(Intent.Click(0, v));
            f.FlushIntents();
            Assert.That(f.Runner!.Value("clue"), Is.EqualTo(1), "代入は 1 回だけ");
        }

        [Test]
        public void ClickPressedOnTitleDoesNotActOnStory()
        {
            var f = AppLab.NewFlow();
            var v = f.Version;
            f.Enqueue(Intent.Click(NovelFlow.TitleNew, v));
            f.Enqueue(Intent.Click(-1, v));
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Reading));
            Assert.That(f.ReadCount, Is.EqualTo(0), "タイトルで押したクリックは本文を送らない");
        }

        // ---- 一時停止・枠を隠す ----

        [Test]
        public void PauseAndResume()
        {
            var f = AppLab.Started();
            var hash = f.StoryHash;
            f.Tap(InputButton.Cancel);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Paused));
            f.StepTicks(100);
            Assert.That(f.VisibleChars, Is.EqualTo(1), "一時停止の間は文字送りも止まる");
            f.Tap(InputButton.Confirm);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Reading));
            Assert.That(f.StoryHash, Is.EqualTo(hash));
        }

        [Test]
        public void HiddenWindowConsumesConfirmWithoutAdvancing()
        {
            var f = AppLab.Started(settings: new FlowSettings { TicksPerChar = 0 });
            f.Tap(InputButton.HideWindow);
            f.Tap(InputButton.Confirm);
            f.FlushIntents();
            Assert.That(f.WindowHidden, Is.False);
            Assert.That(f.ReadCount, Is.EqualTo(0));
        }

        // ---- 題材固有：ロードで選択前の状態に戻る ----

        [Test]
        public void QuickLoadReturnsToBeforeTheChoice()
        {
            var quick = new MemorySaveStore();
            var f = AppLab.Started(quick);
            AppLab.ToChoice(f);
            AppLab.Choose(f, 0);
            AppLab.ToChoice(f);
            f.Tap(InputButton.QuickSave);
            f.FlushIntents();
            Assert.That(f.Notice, Is.EqualTo("クイックセーブしました"));
            var saved = f.StoryHash;
            var clue = f.Runner!.Value("clue");

            AppLab.Choose(f, 0);
            AppLab.Next(f);
            Assert.That(f.Runner.Value("clue"), Is.EqualTo(clue + 1));

            f.Tap(InputButton.QuickLoad);
            f.FlushIntents();
            Assert.That(f.IsChoice, Is.True);
            Assert.That(f.StoryHash, Is.EqualTo(saved));
            Assert.That(f.Runner.Value("clue"), Is.EqualTo(clue));
            Assert.That(f.Background, Is.EqualTo("corridor"));

            AppLab.Choose(f, 1);
            Assert.That(f.Runner.Value("spare_key"), Is.EqualTo(1), "別の選択肢を選び直せる");
        }

        [Test]
        public void ContinueFromTitleResumesSavedPoint()
        {
            var quick = new MemorySaveStore();
            var f = AppLab.Started(quick);
            for (int i = 0; i < 7; i++) { AppLab.Next(f); }
            f.Tap(InputButton.QuickSave);
            f.FlushIntents();
            var saved = f.StoryHash;

            var g = AppLab.NewFlow(quick);
            Assert.That(g.TitleItems[NovelFlow.TitleContinue].Enabled, Is.True);
            g.Click(NovelFlow.TitleContinue);
            g.FlushIntents();
            Assert.That(g.Screen, Is.EqualTo(FlowScreen.Reading));
            Assert.That(g.StoryHash, Is.EqualTo(saved));
        }

        [Test]
        public void ContinueIsDisabledWithReason()
        {
            var g = AppLab.NewFlow();
            Assert.That(g.TitleItems[NovelFlow.TitleContinue].Enabled, Is.False);
            Assert.That(g.TitleItems[NovelFlow.TitleContinue].Detail, Is.EqualTo("クイックセーブがありません"));

            var broken = new MemorySaveStore { Content = "kind=quick\nversion=1\n" };
            var h = AppLab.NewFlow(broken);
            Assert.That(h.TitleItems[NovelFlow.TitleContinue].Enabled, Is.False);
            Assert.That(h.TitleItems[NovelFlow.TitleContinue].Detail, Does.StartWith("クイックセーブを読めませんでした"));
            h.Click(NovelFlow.TitleContinue);
            h.FlushIntents();
            Assert.That(h.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(h.Notice, Does.StartWith("クイックセーブを読めませんでした"));
            Assert.That(h.Cues.Reject, Is.EqualTo(1));
        }

        [Test]
        public void QuickSaveWriteFailureIsReported()
        {
            var quick = new MemorySaveStore { WriteFailure = "ディスクがいっぱいです" };
            var f = AppLab.Started(quick);
            f.Tap(InputButton.QuickSave);
            f.FlushIntents();
            Assert.That(f.Notice, Is.EqualTo("クイックセーブできませんでした：ディスクがいっぱいです"));
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Reading));
        }

        [Test]
        public void QuickLoadWithoutSaveKeepsStory()
        {
            var f = AppLab.Started();
            AppLab.Next(f);
            var hash = f.StoryHash;
            f.Tap(InputButton.QuickLoad);
            f.FlushIntents();
            Assert.That(f.StoryHash, Is.EqualTo(hash));
            Assert.That(f.Notice, Is.EqualTo("クイックセーブがありません"));
        }

        // ---- 結末と再プレイ ----

        [TestCase("truth", "灯は戻る")]
        [TestCase("normal", "夜明けの船")]
        [TestCase("bad", "消えた灯")]
        public void EachEndingReachesResult(string ending, string name)
        {
            var f = AppLab.NewFlow();
            new ChoicePilot(AppLab.Route(ending)).RunToEnd(f);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(f.EndingName, Is.EqualTo(name));
            Assert.That(f.Endings.Single(e => e.Current).Name, Is.EqualTo(name));
            Assert.That(f.EndingsReached, Is.EqualTo(1));
        }

        [Test]
        public void AllEndingsAccumulateAcrossReplays()
        {
            var read = new MemorySaveStore();
            var f = AppLab.NewFlow(read: read);
            foreach (var e in new[] { "bad", "normal", "truth" })
            {
                new ChoicePilot(AppLab.Route(e)).RunToEnd(f);
                f.Tap(InputButton.Confirm);
                f.FlushIntents();
                Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
            }

            Assert.That(f.EndingsReached, Is.EqualTo(3));
            var g = AppLab.NewFlow(read: read);
            Assert.That(g.EndingsReached, Is.EqualTo(3), "到達した結末はファイルに残る");
        }

        [Test]
        public void ReplayStartsFromInitialStateButKeepsReadLog()
        {
            var f = AppLab.NewFlow();
            new ChoicePilot(AppLab.Route("truth")).RunToEnd(f);
            f.Tap(InputButton.Confirm);
            f.FlushIntents();
            var read = f.ReadCount;
            f.Click(NovelFlow.TitleNew);
            f.FlushIntents();
            var fresh = AppLab.Started();
            Assert.That(f.StoryHash, Is.EqualTo(fresh.StoryHash), "変数・表示は初期化される");
            Assert.That(f.ReadCount, Is.EqualTo(read));
            Assert.That(f.IsCurrentRead, Is.True);
        }

        [Test]
        public void ResultIgnoresOtherInput()
        {
            var quick = new MemorySaveStore();
            var f = AppLab.NewFlow(quick);
            new ChoicePilot(AppLab.Route("bad")).RunToEnd(f);
            var cues = f.Cues.Save + f.Cues.Load;
            foreach (var b in new[] { InputButton.Up, InputButton.Down, InputButton.QuickSave, InputButton.QuickLoad, InputButton.SkipToggle, InputButton.HideWindow })
            {
                f.Tap(b);
            }

            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(quick.Content, Is.Null, "結末の後は保存しない");
            Assert.That(f.Cues.Save + f.Cues.Load, Is.EqualTo(cues));
        }

        [Test]
        public void TitleToHowToAndBack()
        {
            var f = AppLab.NewFlow();
            f.Tap(InputButton.Down);
            f.Tap(InputButton.Down);
            f.Tap(InputButton.Confirm);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.HowTo));
            f.Tap(InputButton.Cancel);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
            f.Click(NovelFlow.TitleQuit);
            f.FlushIntents();
            Assert.That(f.QuitRequested, Is.True);
        }

        // ---- 既読の記録の保存 ----

        [Test]
        public void ReadLogIsSavedAtChoiceEndingAndTitle()
        {
            var read = new MemorySaveStore();
            var f = AppLab.Started(read: read);
            AppLab.Next(f);
            Assert.That(read.Writes, Is.EqualTo(0), "文を送るたびには書かない");
            AppLab.ToChoice(f);
            AppLab.Choose(f, 2);
            Assert.That(read.Writes, Is.EqualTo(1));
            AppLab.Next(f);
            f.Tap(InputButton.Cancel);
            f.FlushIntents();
            f.Click(NovelFlow.PauseTitle);
            f.FlushIntents();
            Assert.That(read.Writes, Is.EqualTo(2));
            Assert.That(NovelSaveCodec.TryDecodeRead(AppLab.Script, read.Content, out var log, out _), Is.True);
            Assert.That(log!.ReadCount, Is.EqualTo(f.ReadCount));
        }

        [Test]
        public void BrokenReadLogStartsEmptyWithNotice()
        {
            var read = new MemorySaveStore { Content = "壊れたデータ\n" };
            var f = AppLab.NewFlow(read: read);
            Assert.That(f.ReadCount, Is.EqualTo(0));
            Assert.That(f.Notice, Does.StartWith("既読の記録を読めなかったので、空から始めます"));

            var failing = new MemorySaveStore { ReadFailure = "アクセスが拒否されました" };
            Assert.That(AppLab.NewFlow(read: failing).Notice, Does.Contain("アクセスが拒否されました"));
        }

        [Test]
        public void ReadLogWriteFailureIsReportedAndPlayContinues()
        {
            var read = new MemorySaveStore { WriteFailure = "書き込み禁止" };
            var f = AppLab.Started(read: read);
            AppLab.ToChoice(f);
            AppLab.Choose(f, 0);
            Assert.That(f.Notice, Is.EqualTo("既読の記録を保存できませんでした：書き込み禁止"));
            AppLab.Next(f);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Reading));
        }

        [Test]
        public void NoticeExpires()
        {
            var f = AppLab.Started();
            f.Tap(InputButton.QuickSave);
            f.FlushIntents();
            f.StepTicks(149);
            Assert.That(f.Notice, Is.Not.Empty);
            f.StepTicks(1);
            Assert.That(f.Notice, Is.Empty);
        }

        [Test]
        public void UpdateFrameCapsTicksPerFrame()
        {
            var f = AppLab.Started(settings: new FlowSettings { TicksPerChar = 1, MaxTicksPerFrame = 8 });
            f.UpdateFrame(10.0);
            Assert.That(f.Ticks, Is.EqualTo(8));
            f.UpdateFrame(1.0 / 60);
            Assert.That(f.Ticks, Is.EqualTo(9));
        }

        [Test]
        public void InvalidSettingsAreRejected()
        {
            Assert.Throws<System.ArgumentException>(() => AppLab.NewFlow(settings: new FlowSettings { TicksPerChar = -1 }));
            Assert.Throws<System.ArgumentException>(() => AppLab.NewFlow(settings: new FlowSettings { SkipIntervalTicks = 0 }));
        }
    }
}
