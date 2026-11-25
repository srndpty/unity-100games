using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class FlowTests
    {
        private static void Press(BrawlFlow flow, IntentKind kind, double time) => flow.Apply(new Intent(kind, time));

        private static BrawlFlow Started(double time = 0)
        {
            var flow = AppLab.Flow();
            Press(flow, IntentKind.Submit, time);
            Press(flow, IntentKind.Submit, time);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Playing));
            return flow;
        }

        [Test]
        public void TitleHowtoPlayResultAndReplay()
        {
            var flow = AppLab.Flow(seed: 5);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Title));
            Press(flow, IntentKind.Submit, 0);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Howto), "タイトルの次は操作説明");
            Press(flow, IntentKind.Cancel, 0);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Title));
            Press(flow, IntentKind.Submit, 0);
            Press(flow, IntentKind.Submit, 0);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Playing));
            Assert.That(flow.MatchSeed, Is.EqualTo(5));

            flow.Pilot = new Autopilot(5);
            for (int i = 0; i < 20000 && flow.Screen == BrawlScreen.Playing; i++) { flow.StepTick(); }
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Result));
            Assert.That(flow.Brawl!.Outcome, Is.EqualTo(Outcome.Cleared));
            uint hash = flow.Brawl.Hash();
            Press(flow, IntentKind.AttackDown, 1000);
            flow.StepTick();
            Assert.That(flow.Brawl.Hash(), Is.EqualTo(hash), "結果の後は進まない");

            Press(flow, IntentKind.Submit, 1000);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Playing), "再戦");
            Assert.That(flow.MatchSeed, Is.EqualTo(6), "再戦は新しいシード");
            Assert.That(flow.Brawl!.Tick, Is.Zero);
            Assert.That(flow.Brawl.Lives, Is.EqualTo(new BrawlConfig().Lives));
            Assert.That(flow.Brawl.Score, Is.Zero);
            Press(flow, IntentKind.Back, 1000);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Title));
            Assert.That(flow.Brawl, Is.Null);
        }

        [Test]
        public void PauseStopsTimeAndResumeContinues()
        {
            var flow = Started();
            flow.Advance(1.0);
            long tick = flow.Brawl!.Tick;
            Assert.That(tick, Is.GreaterThan(0));
            Press(flow, IntentKind.Cancel, 1.0);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Paused));
            flow.Advance(5.0);
            Assert.That(flow.Brawl.Tick, Is.EqualTo(tick), "一時停止の間は進まない");
            Press(flow, IntentKind.Cancel, 5.0);
            flow.Advance(5.5);
            Assert.That(flow.Brawl.Tick, Is.GreaterThan(tick));
        }

        [Test]
        public void FrameTickBudgetIsShared()
        {
            var flow = Started();
            flow.Advance(10.0);
            Assert.That(flow.Brawl!.Tick, Is.EqualTo(BrawlFlow.MaxTicksPerFrame), "処理落ちでは 1 フレーム MaxTicksPerFrame Tick まで");
        }

        [Test]
        public void FocusLossPausesAndDropsBufferedInput()
        {
            var flow = Started();
            Press(flow, IntentKind.AttackDown, 0.05);
            flow.Advance(0.07);
            Assert.That(flow.Brawl!.AttackBuffer > 0 || flow.Brawl.Player.Move != null, Is.True);
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Paused));
            Assert.That(flow.Brawl.AttackBuffer, Is.Zero, "先行入力を捨てる");
            Assert.That(flow.Brawl.Held, Is.EqualTo(Pad.Neutral));
            Press(flow, IntentKind.Submit, 0.2);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Paused), "フォーカスが無い間の入力は捨てる");
            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(BrawlScreen.Paused), "復帰は手動");
            Assert.That(flow.KeyPad(), Is.EqualTo(Pad.Neutral), "押していたキーは離れている");
        }

        [Test]
        public void SameKeysAtDifferentFrameRatesGiveTheSameState()
        {
            var events = new List<Intent>
            {
                new(IntentKind.RightDown, 0.20),
                new(IntentKind.UpDown, 0.45),
                new(IntentKind.UpUp, 0.70),
                new(IntentKind.AttackDown, 1.05),
                new(IntentKind.AttackUp, 1.09),
                new(IntentKind.AttackDown, 1.21),
                new(IntentKind.AttackUp, 1.25),
                new(IntentKind.JumpDown, 1.80),
                new(IntentKind.JumpUp, 1.84),
                new(IntentKind.RightUp, 2.30),
                new(IntentKind.SpecialDown, 2.61),
                new(IntentKind.SpecialUp, 2.66),
            };

            uint? expected = null;
            foreach (var frame in new[] { 1.0 / 30, 1.0 / 60, 1.0 / 144, 1.0 / 240, -1 })
            {
                var flow = Started();
                var random = new IntGeometry.SeededRandom(9);
                double now = 0;
                int next = 0;
                while (now < 3.5)
                {
                    double step = frame > 0 ? frame : (5 + random.Next(40)) / 1000.0;
                    now = Math.Min(now + step, 3.5);
                    while (next < events.Count && events[next].Time <= now) { flow.Apply(events[next++]); }
                    flow.Advance(now);
                }

                Assert.That(flow.Brawl!.Tick, Is.EqualTo(210));
                uint hash = flow.Brawl.Hash();
                expected ??= hash;
                Assert.That(hash, Is.EqualTo(expected.Value), $"frame={frame}");
            }
        }

        [TestCase(IntentKind.AttackDown, IntentKind.AttackUp)]
        [TestCase(IntentKind.JumpDown, IntentKind.JumpUp)]
        [TestCase(IntentKind.SpecialDown, IntentKind.SpecialUp)]
        public void TapInsideOneTickIsNotLost(IntentKind down, IntentKind up)
        {
            var flow = Started();
            flow.Apply(new Intent(down, 0.003));
            flow.Apply(new Intent(up, 0.008));
            flow.Advance(0.020);
            var p = flow.Brawl!.Player;
            Assert.That(flow.Brawl.Tick, Is.EqualTo(1));
            if (down == IntentKind.JumpDown) { Assert.That(p.State, Is.EqualTo(ActorState.Jump)); }
            else { Assert.That(p.Move?.Id, Is.EqualTo(down == IntentKind.AttackDown ? "jab1" : "special")); }
            Assert.That(flow.KeyPad(), Is.EqualTo(Pad.Neutral), "押下の印は渡した Tick で消える");
        }

        [Test]
        public void HoldingAttackFiresOnceAndPauseDropsPresses()
        {
            var flow = Started();
            Press(flow, IntentKind.AttackDown, 0.003);
            flow.Advance(0.5);
            flow.EndFrame();
            for (double t = 0.5; t < 2.0; t += 1.0 / 60) { flow.Advance(t); }
            Assert.That(flow.Brawl!.AttacksStarted, Is.EqualTo(1), "押したままでは 1 回");

            var paused = Started();
            Press(paused, IntentKind.AttackDown, 0.003);
            Press(paused, IntentKind.AttackUp, 0.004);
            Press(paused, IntentKind.Cancel, 0.005);
            Assert.That(paused.KeyPad(), Is.EqualTo(Pad.Neutral), "一時停止で押下の印も捨てる");
            Press(paused, IntentKind.Cancel, 1.0);
            paused.Advance(1.02);
            Assert.That(paused.Brawl!.AttacksStarted, Is.Zero);
        }

        [Test]
        public void KeyBitsKeepTwoKeysForOneDirection()
        {
            var flow = Started();
            flow.Apply(new Intent(IntentKind.LeftDown, 0, 1));
            flow.Apply(new Intent(IntentKind.LeftDown, 0, 2));
            flow.Apply(new Intent(IntentKind.LeftUp, 0, 1));
            Assert.That(flow.KeyPad().X, Is.EqualTo(-1), "もう 1 つのキーを押したまま");
            flow.Apply(new Intent(IntentKind.LeftUp, 0, 2));
            Assert.That(flow.KeyPad().X, Is.Zero);
            flow.Apply(new Intent(IntentKind.LeftDown, 0));
            flow.Apply(new Intent(IntentKind.RightDown, 0));
            Assert.That(flow.KeyPad().X, Is.Zero, "左右同時は打ち消す");
        }

        [Test]
        public void AutopilotClearsTheStageOnEverySeed()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var brawl = AppLab.Play(seed, new Autopilot(seed), 15000);
                Assert.That(brawl.Outcome, Is.EqualTo(Outcome.Cleared), $"seed {seed}: 区画 {brawl.SectionIndex} 残機 {brawl.Lives}");
                Assert.That(brawl.Defeats, Is.GreaterThanOrEqualTo(TotalEnemies() - 2), "ボスの護衛のほかは全て倒した");
            }
        }

        /// <summary>整数と Tick だけの規則なので、操縦役の通しの最終ハッシュは .NET と Mono で一致する。</summary>
        [Test]
        public void AutopilotRunHashIsFixed()
        {
            var brawl = AppLab.Play(1, new Autopilot(1), 15000);
            Assert.That(brawl.Outcome, Is.EqualTo(Outcome.Cleared));
            Assert.That((brawl.Tick, brawl.Hash()), Is.EqualTo((6160L, 4278129782u)));
        }

        [Test]
        public void SlowHumanLikePilotStillClearsMostRuns()
        {
            int cleared = 0;
            int livesLost = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                var brawl = AppLab.Play(seed, new Autopilot(seed, 6, false), 20000);
                if (brawl.Outcome == Outcome.Cleared) { cleared++; }
                livesLost += brawl.Deaths;
            }

            Assert.That(cleared, Is.GreaterThanOrEqualTo(8), "判断が 0.1 秒ごと・緊急技なしでも通せる");
            Assert.That(livesLost, Is.GreaterThan(0), "無傷では通れない程度には手強い");
        }

        [Test]
        public void IdlePlayerLoses()
        {
            var brawl = new Brawl(AppLab.Table(), new BrawlConfig(), AppLab.Stage(), 4);
            for (int t = 0; t < 60000 && brawl.Outcome == Outcome.Running; t++) { brawl.Step(Pad.Neutral); }
            Assert.That(brawl.Outcome, Is.EqualTo(Outcome.Lost));
            Assert.That(brawl.Lives, Is.Zero);
        }

        [Test]
        public void PilotIsReproducibleAfterAnAbandonedRun()
        {
            var pilot = new Autopilot(3);
            var first = AppLab.Play(3, pilot, 15000);
            AppLab.Play(8, pilot, 700);
            var again = AppLab.Play(3, pilot, 15000);
            Assert.That(again.Hash(), Is.EqualTo(first.Hash()), "途中でやめた試合の後でも同じ結果");
        }

        [Test]
        public void FeedbackReportsEventsBetweenFrames()
        {
            var brawl = new Brawl(AppLab.Table(), new BrawlConfig(), StageData.Arena(), 1) { AiEnabled = false };
            var feedback = new CombatFeedback();
            Assert.That(feedback.Observe(brawl), Is.EqualTo(CombatCue.None));
            var foe = brawl.Spawn(ActorKind.Grunt, brawl.Player.X + (18 * Game.Core.Px.One), brawl.Player.Z, -1);
            brawl.Step(new Pad(0, 0, true, false, false));
            for (int i = 0; i < 6; i++) { brawl.Step(Pad.Neutral); }
            var cue = feedback.Observe(brawl);
            Assert.That(cue.HasFlag(CombatCue.Swing), Is.True);
            Assert.That(cue.HasFlag(CombatCue.Hit), Is.True, "描画の間に当たっても鳴らす");
            Assert.That(feedback.Observe(brawl), Is.EqualTo(CombatCue.None), "同じ出来事は 1 回");
            Assert.That(foe.Health, Is.LessThan(foe.Def!.Health));
        }

        [Test]
        public void ConstructorRejectsBrokenInputs()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new BrawlFlow(null!, AppLab.Stage(), new BrawlConfig(), 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BrawlFlow(AppLab.Table(), AppLab.Stage(), new BrawlConfig { MaxActiveEnemies = 0 }, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Autopilot(1, 0));
        }

        private static int TotalEnemies()
        {
            int total = 0;
            var sections = AppLab.Stage().Sections;
            for (int s = 0; s < sections.Count; s++) { total += sections[s].Waves.Count; }
            return total;
        }
    }
}
