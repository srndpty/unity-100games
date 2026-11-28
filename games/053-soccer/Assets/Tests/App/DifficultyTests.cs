using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>相手の CPU の強さを、実際の入力経路（<see cref="HumanControl"/>）を通るキー操作の人間役で確かめる。</summary>
    public sealed class DifficultyTests
    {
        /// <summary>難易度の比較の条件（相手の走る速さ・役割を決め直す間隔・タックルの慎重さ）。</summary>
        internal static readonly (string Name, int Speed, int RoleTicks, int TackleMargin)[] Conditions =
        {
            ("A Full", 100, 15, 100),
            ("B 速さだけ 75%", 75, 15, 100),
            ("C 決め直しだけ 30", 100, 30, 100),
            ("D 慎重さだけ 300", 100, 15, 300),
            ("E 既定（弱め）", 75, 30, 300),
        };

        internal static string Line(string name, int matches, Func<int, MatchStats> play)
        {
            int bg = 0, rg = 0, bs = 0, rs = 0, bp = 0, rp = 0, bpass = 0, bdone = 0, rpass = 0, rdone = 0, bt = 0, rt = 0;
            for (int i = 0; i < matches; i++)
            {
                var s = play(i);
                var b = s.Of(Team.Blue);
                var r = s.Of(Team.Red);
                bg += b.Goals;
                rg += r.Goals;
                bs += b.Shots;
                rs += r.Shots;
                bp += b.PossessionTicks;
                rp += r.PossessionTicks;
                bpass += b.Passes;
                bdone += b.PassesCompleted;
                rpass += r.Passes;
                rdone += r.PassesCompleted;
                bt += b.Tackles;
                rt += r.Tackles;
            }

            static string Rate(int a, int b) => b == 0 ? "-" : $"{a * 100 / b}%";
            return $"| {name} | {bg}-{rg} | {bs}-{rs} | {Rate(bg, bs)}-{Rate(rg, rs)} | {Rate(bp, bp + rp)} | {Rate(bdone, bpass)}-{Rate(rdone, rpass)} | {bt}-{rt} |";
        }

        [Test]
        public void KeyPilot_PlaysAFullMatchThroughTheRealInput_AndBeatsTheDefaultCpu()
        {
            // 既定の相手（走る速さ 75 %・AiSkill.Weak）に、キー操作の人間役が 3 分 × 3 シードで勝ち越す。
            var config = new SoccerConfig { MatchTicks = 3 * 60 * Pitch.TickRate, RedSpeedPercent = 75 };
            int blue = 0, red = 0, shots = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                var match = new Match(config, seed + 300);
                var s = KeyPilot.Play(match, AiSkill.Weak, Tactics.Balanced);
                Assert.That(match.Phase, Is.EqualTo(MatchPhase.Ended));
                blue += match.Score(Team.Blue);
                red += match.Score(Team.Red);
                shots += s.Of(Team.Blue).Shots;
            }

            TestContext.WriteLine($"key pilot vs default {blue}-{red} shots {shots}");
            Assert.That(shots, Is.GreaterThan(0), "K のシュートが入力経路を通る");
            Assert.That(blue, Is.GreaterThan(red));
        }

        /// <summary>比較実験（相手の方針はバランス、各条件 16 シード × 3 分）。結果は README に記録する。</summary>
        [Test]
        [Explicit("比較実験（数十秒）")]
        public void Ablation_KeyPilot()
        {
            TestContext.Progress.WriteLine("| 条件 | 得点 | シュート | 決定率 | 青の保持率 | パス成功率 | タックル |");
            foreach (var (name, speed, roleTicks, margin) in Conditions)
            {
                var config = new SoccerConfig { MatchTicks = 3 * 60 * Pitch.TickRate, RedSpeedPercent = speed };
                var skill = new AiSkill { RoleTicks = roleTicks, TackleMargin = margin };
                TestContext.Progress.WriteLine(Line(name, 16, i => KeyPilot.Play(new Match(config, i + 500), skill, Tactics.Balanced)));
            }
        }
    }
}
