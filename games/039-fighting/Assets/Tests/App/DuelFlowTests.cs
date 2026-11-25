using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class DuelFlowTests
    {
        private const int MatchLimit = 40000;

        [Test]
        public void TitleSelectRoundResultAndRematchResetTheMatch()
        {
            var flow = Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Select));
            flow.Apply(new Intent(IntentKind.RightDown, 0));
            Assert.That(flow.PlayerCharacter, Is.EqualTo(1));
            flow.Apply(new Intent(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Intro));
            Assert.That(flow.MatchSeed, Is.EqualTo(flow.BootSeed));
            Assert.That(flow.CpuCharacter, Is.EqualTo(0));

            int intro = flow.Config.IntroTicks;
            for (int i = 0; i < intro; i++) { flow.StepTick(); }
            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Fighting));
            var health = flow.Match!.Duel.At(0).Health;
            flow.Apply(new Intent(IntentKind.Cancel, 1));
            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Paused));
            flow.Apply(new Intent(IntentKind.Back, 1));
            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Title));
            Assert.That(flow.HasMatch, Is.False);

            flow.Apply(new Intent(IntentKind.Submit, 2));
            flow.Apply(new Intent(IntentKind.Submit, 2));
            Assert.That(flow.MatchSeed, Is.EqualTo(flow.BootSeed + 1));
            Assert.That(flow.PlayerCharacter, Is.EqualTo(1));
            RunToResult(flow);
            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Result));
            int wins = flow.Match!.Wins[1];
            int endHealth = flow.Match.Duel.At(0).Health;
            flow.Apply(new Intent(IntentKind.LightDown, 3));
            flow.StepTick();
            Assert.That(flow.Match.Wins[1], Is.EqualTo(wins));
            Assert.That(flow.Match.Duel.At(0).Health, Is.EqualTo(endHealth));

            flow.Apply(new Intent(IntentKind.Submit, 4));
            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Intro));
            Assert.That(flow.MatchSeed, Is.EqualTo(flow.BootSeed + 2));
            Assert.That(flow.Match!.Round, Is.EqualTo(1));
            Assert.That(flow.Match.Wins[0], Is.EqualTo(0));
            Assert.That(flow.Match.Wins[1], Is.EqualTo(0));
            Assert.That(flow.Match.Duel.At(0).Health, Is.EqualTo(flow.Match.Duel.At(0).MaxHealth));
            Assert.That(health, Is.GreaterThan(0));
        }

        [Test]
        public void IdlePlayerLosesBothRounds()
        {
            var flow = Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.Submit, 0));
            RunToResult(flow);
            Assert.That(flow.Match!.Result, Is.EqualTo(MatchResult.Right));
            Assert.That(flow.Match.Wins[0], Is.EqualTo(0));
            Assert.That(flow.Match.Wins[1], Is.EqualTo(2));
        }

        [Test]
        public void CpuVersusCpuFinishesEverySeed()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var match = Play(seed, PilotStyle.Cpu, PilotStyle.Cpu);
                Assert.That(match.IsOver, Is.True, $"seed {seed}");
                Assert.That(match.Duel.Tick, Is.LessThan(MatchLimit));
            }
        }

        [Test]
        public void HumanLikePilotBeatsTheCpuOftenEnough()
        {
            int wins = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                var match = Play(seed, PilotStyle.Human, PilotStyle.Cpu);
                if (match.Result == MatchResult.Left) { wins++; }
            }

            TestContext.WriteLine($"human wins {wins}/20");
            Assert.That(wins, Is.EqualTo(3), "反応 8 Tick の CPU に、反応 16 Tick でキーを 6 Tick 保持する操縦役は 20 戦中 3 勝");
        }

        [Test]
        public void SameSeedAfterAnAbandonedMatchMatchesAFreshRun()
        {
            var fresh = Play(3, PilotStyle.Cpu, PilotStyle.Cpu).Duel.Hash();
            var pilot = new CpuPilot(0, 9, PilotStyle.Cpu);
            var cpu = new CpuPilot(1, 9, PilotStyle.Cpu);
            var abandoned = NewMatch();
            for (int i = 0; i < 400 && !abandoned.IsOver; i++) { Step(abandoned, pilot, cpu); }
            pilot.Reset(3);
            cpu.Reset(3);
            var again = NewMatch();
            while (!again.IsOver) { Step(again, pilot, cpu); }
            Assert.That(again.Duel.Hash(), Is.EqualTo(fresh));
        }

        [Test]
        public void SameKeysGiveTheSameStateAtAnyFrameRate()
        {
            var keys = new List<Intent>
            {
                new(IntentKind.RightDown, 0.90),
                new(IntentKind.LightDown, 1.10),
                new(IntentKind.LightUp, 1.20),
                new(IntentKind.RightUp, 1.40),
                new(IntentKind.DownDown, 1.55),
                new(IntentKind.SweepDown, 1.70),
                new(IntentKind.SweepUp, 1.80),
                new(IntentKind.DownUp, 1.90),
            };

            uint? expected = null;
            int? ticks = null;
            foreach (var frame in new[] { 1.0 / 30, 1.0 / 60, 1.0 / 144, 1.0 / 240, -1.0 })
            {
                var flow = Flow();
                flow.Apply(new Intent(IntentKind.Submit, 0));
                flow.Apply(new Intent(IntentKind.Submit, 0));
                var jitter = new[] { 0.008, 0.021, 0.013, 0.033, 0.017, 0.025, 0.011, 0.019, 0.028, 0.015 };
                var jitterIndex = 0;
                var next = 0;
                for (var now = 0.0; now < 2.0;)
                {
                    var step = frame > 0 ? frame : jitter[jitterIndex++ % jitter.Length];
                    now = Math.Min(2.0, now + step);
                    while (next < keys.Count && keys[next].Time <= now)
                    {
                        flow.Apply(keys[next]);
                        next++;
                    }

                    flow.Advance(now);
                }

                Assert.That(flow.Match!.Duel.Tick, Is.EqualTo(ticks ??= flow.Match.Duel.Tick), $"frame {frame}");
                var hash = flow.Match.Duel.Hash();
                expected ??= hash;
                Assert.That(hash, Is.EqualTo(expected), $"frame {frame}");
            }
        }

        [Test]
        public void FocusLossDropsTheBuffer([Values(true, false)] bool releaseFirst)
        {
            var flow = Flow(new DuelConfig { IntroTicks = 2, RoundEndTicks = 2 });
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.StepTick();
            flow.StepTick();
            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Fighting));
            flow.Apply(new Intent(IntentKind.HeavyDown, 0));
            for (int i = 0; i < 10; i++) { flow.StepTick(); }
            Assert.That(flow.Match!.Duel.At(0).State, Is.EqualTo(FighterState.Attack));
            var up = new Intent(IntentKind.HeavyUp, 0);
            flow.Apply(new Intent(IntentKind.LightDown, 0));
            if (releaseFirst) { flow.Apply(up); }
            flow.SetFocus(false);
            if (!releaseFirst) { flow.Apply(up); }

            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Paused));
            var tick = flow.Match.Duel.Tick;
            flow.Advance(2);
            Assert.That(flow.Match.Duel.Tick, Is.EqualTo(tick));
            flow.SetFocus(true);
            flow.Apply(new Intent(IntentKind.Submit, 2));
            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Fighting));
            var sawLight = false;
            for (int i = 0; i < 40; i++)
            {
                flow.StepTick();
                var move = flow.Match.Duel.At(0).Move;
                if (move != null && move.Id == "light") { sawLight = true; }
            }

            Assert.That(sawLight, Is.False);
        }

        [TestCase(0, false, false)]
        [TestCase(1, false, false)]
        [TestCase(0, true, false)]
        [TestCase(1, true, false)]
        [TestCase(0, false, true)]
        public void CombatFeedback_ReportsBothSidesWithoutRepeating(int actor, bool block, bool ko)
        {
            var feedback = new CombatFeedback();
            var duel = new Duel(Table(), new(), 0, 0);
            duel.At(0).X = 220 * 256;
            duel.At(1).X = 250 * 256;
            int victim = 1 - actor;
            int health = ko ? 1 : duel.At(victim).Health;
            duel.At(victim).Health = health;
            var guard = block ? new Pad(actor == 0 ? 6 : 4, false, false, false) : Pad.Neutral;
            var light = new Pad(5, true, false, false);
            Assert.That(feedback.Observe(duel), Is.EqualTo(CombatCue.None));
            duel.Step(actor == 0 ? light : guard, actor == 1 ? light : guard);
            Assert.That(feedback.Observe(duel), Is.EqualTo(CombatCue.Swing));
            Assert.That(feedback.Observe(duel), Is.EqualTo(CombatCue.None));
            for (int i = 0; i < 80; i++) { duel.Step(actor == 0 ? Pad.Neutral : guard, actor == 1 ? Pad.Neutral : guard); }
            var expected = block ? CombatCue.Guard : CombatCue.Hit;
            if (ko) { expected |= CombatCue.Ko; }
            Assert.That(feedback.Observe(duel), Is.EqualTo(expected), "描画を挟まず硬直が終わっても通知する");
            Assert.That(duel.At(victim).Health, block ? Is.EqualTo(health) : Is.LessThan(health));
            Assert.That(feedback.Observe(duel), Is.EqualTo(CombatCue.None));
            Assert.That(feedback.Observe(duel.Clone()), Is.EqualTo(CombatCue.Swing | expected));
            Assert.That(feedback.Observe(null), Is.EqualTo(CombatCue.None));
            Assert.That(feedback.Observe(new Duel(Table(), new(), 0, 0)), Is.EqualTo(CombatCue.None));
        }

        private static void RunToResult(DuelFlow flow)
        {
            int guard = 0;
            while (flow.Screen != DuelScreen.Result && guard++ < MatchLimit) { flow.StepTick(); }
            Assert.That(flow.Screen, Is.EqualTo(DuelScreen.Result));
        }

        private static Match Play(int seed, PilotStyle left, PilotStyle right)
        {
            var match = NewMatch();
            var a = new CpuPilot(0, seed, left);
            var b = new CpuPilot(1, seed, right);
            int guard = 0;
            while (!match.IsOver && guard++ < MatchLimit) { Step(match, a, b); }
            return match;
        }

        private static void Step(Match match, CpuPilot left, CpuPilot right)
        {
            if (match.Phase == MatchPhase.Fight) { match.Step(left.PadFor(match.Duel), right.PadFor(match.Duel)); }
            else { match.Step(Pad.Neutral, Pad.Neutral); }
        }

        private static Match NewMatch() => new(Table(), new(), 0, 1);

        private static DuelFlow Flow(DuelConfig? config = null, [CallerFilePath] string source = "") =>
            new(Table(source), config ?? new(), 11);

        private static MoveTable Table([CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            var path = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "Assets", "Game", "Data", "Moves.txt"));
            return MoveTable.Parse(File.ReadAllText(path));
        }
    }
}
