using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>題材固有：未読を既読スキップしない。</summary>
    public sealed class SkipTests
    {
        [Test]
        public void SkipStopsImmediatelyOnUnreadText()
        {
            var f = AppLab.Started();
            var hash = f.StoryHash;
            f.Tap(InputButton.SkipToggle);
            f.StepTicks(200);
            Assert.That(f.SkipToggled, Is.False, "未読で切り替えが解除される");
            Assert.That(f.StoryHash, Is.EqualTo(hash), "未読の文から進まない");
        }

        [Test]
        public void HeldSkipDoesNotPassUnreadText()
        {
            var f = AppLab.Started();
            var hash = f.StoryHash;
            f.Press(InputButton.SkipHold, 1);
            f.StepTicks(300);
            Assert.That(f.StoryHash, Is.EqualTo(hash));
            Assert.That(f.IsRevealed, Is.True, "文字送りは普通に進む");
        }

        [Test]
        public void SkipStopsJustBeforeTheFirstUnreadText()
        {
            // 既読 2 文 → 未読 1 文 → 既読 の並び。
            var f = AppLab.Started(settings: new FlowSettings { TicksPerChar = 2 });
            var ids = AppLab.Script.TextIds;
            f.Log.MarkRead(ids[0]);
            f.Log.MarkRead(ids[1]);
            f.Log.MarkRead(ids[3]);
            f.Tap(InputButton.SkipToggle);
            f.StepTicks(10); // 4 Tick ごとに 2 文送って、3 文目で止まった直後
            Assert.That(f.Runner!.Current.TextId, Is.EqualTo(ids[2]));
            Assert.That(f.IsCurrentRead, Is.False);
            Assert.That(f.SkipToggled, Is.False);
            Assert.That(f.VisibleChars, Is.LessThan(f.FullText.Length), "未読の文は表示途中から始まる");
            f.StepTicks(300);
            Assert.That(f.Runner.Current.TextId, Is.EqualTo(ids[2]), "止まった後も勝手に進まない");
        }

        [Test]
        public void SecondPlaythroughSkipsToFirstChoiceAndStops()
        {
            var read = new MemorySaveStore();
            var f = AppLab.Started(read: read);
            AppLab.ToChoice(f);
            AppLab.Choose(f, 0);
            f.Tap(InputButton.Cancel);
            f.FlushIntents();
            f.Click(NovelFlow.PauseTitle);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));

            var g = AppLab.Started(read: read);
            Assert.That(g.IsCurrentRead, Is.True, "既読は次の周へ持ち越す");
            g.Press(InputButton.SkipHold, 2);
            g.StepTicks(5000);
            Assert.That(g.IsChoice, Is.True, "選択肢で止まる");
            g.StepTicks(100);
            Assert.That(g.IsChoice, Is.True, "押し続けても選ばない");

            // 選んだ先は既読の枝（0）なら進み、別の枝（1）は未読ですぐ止まる。
            AppLab.Choose(g, 1);
            var at = g.Runner!.Current.TextId;
            g.StepTicks(100);
            Assert.That(g.Runner.Current.TextId, Is.EqualTo(at));
            Assert.That(g.IsCurrentRead, Is.False);
        }

        [Test]
        public void ReleasingHoldStopsSkip()
        {
            var f = AppLab.Started();
            var ids = AppLab.Script.TextIds;
            foreach (var id in ids) { f.Log.MarkRead(id); }
            f.Press(InputButton.SkipHold, 1);
            f.StepTicks(8);
            var at = f.Runner!.Current.TextId;
            Assert.That(at, Is.Not.EqualTo(ids[0]));
            f.Release(InputButton.SkipHold, 1);
            f.StepTicks(100);
            Assert.That(f.Runner.Current.TextId, Is.EqualTo(at));
        }

        [Test]
        public void TwoSkipKeysAreHeldSeparately()
        {
            var f = AppLab.Started();
            foreach (var id in AppLab.Script.TextIds) { f.Log.MarkRead(id); }
            f.Press(InputButton.SkipHold, 1);
            f.Press(InputButton.SkipHold, 2);
            f.Release(InputButton.SkipHold, 1);
            f.StepTicks(8);
            Assert.That(f.SkipActive, Is.True, "右 Ctrl を押したまま");
            f.Release(InputButton.SkipHold, 2);
            f.FlushIntents();
            Assert.That(f.SkipActive, Is.False);
        }

        [Test]
        public void SkipStopsAtEnding()
        {
            var f = AppLab.Started();
            foreach (var id in AppLab.Script.TextIds) { f.Log.MarkRead(id); }
            var route = AppLab.Route("bad");
            for (int i = 0; i < route.Length; i++)
            {
                f.Tap(InputButton.SkipToggle);
                f.StepTicks(3000);
                Assert.That(f.IsChoice, Is.True);
                Assert.That(f.SkipToggled, Is.False, "選択肢で切り替えが解除される");
                AppLab.Choose(f, route[i]);
            }

            f.Tap(InputButton.SkipToggle);
            f.StepTicks(3000);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(f.SkipToggled, Is.False);
        }

        [Test]
        public void FocusLossStopsSkip()
        {
            var f = AppLab.Started();
            foreach (var id in AppLab.Script.TextIds) { f.Log.MarkRead(id); }
            f.Press(InputButton.SkipHold, 1);
            f.Tap(InputButton.SkipToggle);
            f.FlushIntents();
            f.OnFocusChanged(false);
            Assert.That(f.SkipActive, Is.False);
            var at = f.StoryHash;
            f.OnFocusChanged(true);
            f.StepTicks(100);
            Assert.That(f.StoryHash, Is.EqualTo(at));
        }

        [Test]
        public void SkippedReadTextIsShownInFull()
        {
            var f = AppLab.Started();
            foreach (var id in AppLab.Script.TextIds) { f.Log.MarkRead(id); }
            f.Press(InputButton.SkipHold, 1);
            f.FlushIntents();
            Assert.That(f.IsRevealed, Is.True);
            Assert.That(f.VisibleText, Is.EqualTo(f.FullText));
        }
    }
}
