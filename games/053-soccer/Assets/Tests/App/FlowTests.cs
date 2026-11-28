using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class FlowTests
    {
        /// <summary>1 分の試合（テストを短くする）。</summary>
        private static SoccerConfig Short => new() { MatchTicks = 60 * Pitch.TickRate };

        private static SoccerFlow Flow(int seed = 1, SoccerConfig? config = null) => new(config ?? Short, seed, new FlowSettings { EndBannerTicks = 10 });

        private static void Tap(SoccerFlow f, InputButton b)
        {
            f.Tap(b);
            f.FlushIntents();
        }

        [Test]
        public void Title_StartsAMatch_WithTheKickoffTakerControlled()
        {
            var f = Flow();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
            Tap(f, InputButton.Confirm);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Playing));
            Assert.That(f.Match, Is.Not.Null);
            Assert.That(f.Controlled, Is.EqualTo(f.Match!.Taker), "キックオフを蹴る青の選手を操作する");
        }

        [Test]
        public void OpponentTactics_CycleOnTheTitle()
        {
            var f = Flow();
            Tap(f, InputButton.Down);
            Assert.That(f.MenuCursor, Is.EqualTo(SoccerFlow.TitleTactics));
            var first = f.Opponent;
            Tap(f, InputButton.Right);
            Assert.That(f.Opponent, Is.Not.EqualTo(first));
            Tap(f, InputButton.Left);
            Assert.That(f.Opponent, Is.EqualTo(first));
            Tap(f, InputButton.Confirm);
            Assert.That(f.OpponentChoice, Is.EqualTo(1), "決定でも次の方針へ");
            Assert.That(f.TitleItems[SoccerFlow.TitleTactics].Label, Does.EndWith(f.Opponent.Name));
            Tap(f, InputButton.Up);
            Tap(f, InputButton.Confirm);
            Assert.That(f.RedAi!.Tactics, Is.EqualTo(SoccerFlow.OpponentTactics[1]));
        }

        [Test]
        public void FullMatch_ReachesTheResult_AndBackToTitle_RematchUsesANewSeed()
        {
            var f = Flow(seed: 5);
            f.Autopilot = true;
            Tap(f, InputButton.Confirm);
            var firstSeed = f.Match!.Seed;
            for (int i = 0; i < 200 && f.Screen == FlowScreen.Playing; i++) { f.StepTicks(30); }
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Result));
            Assert.That(f.Match!.Phase, Is.EqualTo(MatchPhase.Ended));
            Assert.That(f.Cues.Whistle, Is.EqualTo(1));
            Assert.That(f.Outcome, Is.EqualTo(Math.Sign(f.Match.Score(Team.Blue) - f.Match.Score(Team.Red))));
            Assert.That(f.Stats.Of(Team.Blue).Passes + f.Stats.Of(Team.Red).Passes, Is.GreaterThan(5));
            Tap(f, InputButton.Confirm);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(f.Match, Is.Null);
            Tap(f, InputButton.Confirm);
            Assert.That(f.Match!.Seed, Is.Not.EqualTo(firstSeed));
        }

        [Test]
        public void AfterTheWhistle_InputsAreIgnored()
        {
            var f = Flow();
            f.Autopilot = true;
            Tap(f, InputButton.Confirm);
            for (int i = 0; i < 400 && !f.Finished; i++) { f.StepTicks(10); }
            Assert.That(f.Finished, Is.True);
            var hash = f.MatchHash;
            f.Autopilot = false;
            Tap(f, InputButton.Shoot);
            Tap(f, InputButton.Switch);
            f.StepTicks(3);
            Assert.That(f.MatchHash, Is.EqualTo(hash));
        }

        [Test]
        public void Pause_StopsTheMatch_FocusLossPauses_AndNeedsManualResume()
        {
            var f = Flow();
            Tap(f, InputButton.Confirm);
            f.StepTicks(10);
            Tap(f, InputButton.Cancel);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Paused));
            var hash = f.MatchHash;
            f.StepTicks(30);
            Assert.That(f.MatchHash, Is.EqualTo(hash), "一時停止の間は進まない");
            Tap(f, InputButton.Cancel);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Playing));

            f.Press(InputButton.Right);
            f.OnFocusChanged(false);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Paused));
            f.OnFocusChanged(true);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Paused), "戻っても一時停止のまま");
            f.Click(SoccerFlow.PauseResume);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Playing));
        }

        [Test]
        public void PlayTime_IsNotCutByTheTickCap()
        {
            var f = Flow();
            Tap(f, InputButton.Confirm);
            f.UpdateFrame(1.0);
            Assert.That(f.PlaySeconds, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(f.Match!.Clock, Is.EqualTo(8), "1 フレームの Tick の上限");
        }

        [Test]
        public void ClickFromAnOldScreen_IsDropped()
        {
            var f = Flow();
            var title = f.Version;
            Tap(f, InputButton.Down);
            Tap(f, InputButton.Down);
            Tap(f, InputButton.Confirm);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.HowTo));
            f.Enqueue(Intent.Click(SoccerFlow.TitleQuit, title));
            f.FlushIntents();
            Assert.That(f.QuitRequested, Is.False);
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.HowTo));
        }

        [Test]
        public void InvalidSettings_AreRejected()
        {
            Assert.Throws<ArgumentException>(() => _ = new SoccerFlow(new SoccerConfig { Gravity = 0 }, 1));
            Assert.Throws<ArgumentException>(() => _ = new SoccerFlow(new SoccerConfig(), 1, new FlowSettings { MaxTicksPerFrame = 0 }));
        }

        [Test]
        public void HumanKeys_MoveTheControlledPlayer()
        {
            var f = Flow();
            Tap(f, InputButton.Confirm);
            f.StepTicks(Short.RestartDelayTicks + 5);

            // キックオフの蹴る人は動けない。パスで始める。
            Tap(f, InputButton.Pass);
            f.StepTicks(1);
            Assert.That(f.Match!.Phase, Is.EqualTo(MatchPhase.Play));
            var me = f.Controlled;
            f.Press(InputButton.Up, 0);
            f.StepTicks(20);
            Assert.That(f.Match.Players[me].Velocity.Y > 0 || f.Controlled != me, Is.True);
        }

        [Test]
        public void SameSeedAndInputs_SameMatch()
        {
            uint Run()
            {
                var f = Flow(seed: 9);
                f.Autopilot = true;
                Tap(f, InputButton.Confirm);
                f.StepTicks(1500);
                return f.MatchHash;
            }

            Assert.That(Run(), Is.EqualTo(Run()));
        }

        [Test]
        public void Settings_WithoutCpu_OrWithATackleMarginBeyondTheReach_AreRejected()
        {
            var noCpu = new FlowSettings { Cpu = null! };
            Assert.That(noCpu.Validate(), Has.Some.Contains("Cpu"));
            Assert.That(noCpu.Copy().Cpu, Is.Null, "コピーは検証より先に呼ばれても例外にしない");
            var ex = Assert.Throws<ArgumentException>(() => _ = new SoccerFlow(new SoccerConfig(), 1, noCpu));
            Assert.That(ex!.Message, Does.Contain("Cpu"));

            var margin = new FlowSettings { Cpu = new AiSkill { TackleMargin = 400 } };
            Assert.That(margin.Validate(), Is.Empty, "単独では範囲内");
            Assert.That(margin.Validate(new SoccerConfig { TackleRadius = 400 }), Has.Some.Contains("TackleMargin"));
            Assert.Throws<ArgumentException>(() => _ = new SoccerFlow(new SoccerConfig { TackleRadius = 400 }, 1, margin));
        }
    }
}
