using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>敵の攻撃権・倒れた敵・同時出現の上限・区画・多敵の安定。</summary>
    public sealed class EnemyTests
    {
        /// <summary>プレイヤーの周りに敵を置き、考えさせる（プレイヤーは動かない）。</summary>
        private static Brawl Ring(int count, BrawlConfig? config = null, ActorKind kind = ActorKind.Grunt)
        {
            var brawl = Lab.Arena(config ?? new BrawlConfig { Lives = 9 });
            brawl.AiEnabled = true;
            for (int i = 0; i < count; i++)
            {
                int side = (i & 1) == 0 ? 1 : -1;
                Lab.Foe(brawl, kind, side * (60 + (i * 12)), (i - (count / 2)) * 2000);
            }

            return brawl;
        }

        [Test]
        public void AttackersNeverExceedTheLimit()
        {
            var config = new BrawlConfig { Lives = 9, MaxAttackers = 2 };
            var brawl = Ring(6, new BrawlConfig { Lives = 9, Capacity = 8, MaxActiveEnemies = 8, MaxAttackers = 2 });
            int peak = 0;
            for (int t = 0; t < 3000 && brawl.Outcome == Outcome.Running; t++)
            {
                brawl.Step(Pad.Neutral);
                int attacking = 0;
                for (int i = 1; i < brawl.SlotCount; i++)
                {
                    var e = brawl.At(i);
                    if (e.Active && e.State == ActorState.Attack) { attacking++; }
                }

                Assert.That(brawl.Attackers, Is.LessThanOrEqualTo(config.MaxAttackers), $"tick {t}");
                Assert.That(attacking, Is.LessThanOrEqualTo(brawl.Attackers), "攻撃しているのは権利を持つ敵だけ");
                peak = Math.Max(peak, brawl.Attackers);
            }

            Assert.That(peak, Is.EqualTo(config.MaxAttackers), "上限までは同時に攻める");
        }

        [Test]
        public void ArmoredEnemyKeepsItsRightWhenHitWithoutFlinching()
        {
            var brawl = Lab.Arena(new BrawlConfig { Lives = 9, MaxActiveEnemies = 2, MaxAttackers = 1 });
            brawl.AiEnabled = true;
            var brute = Lab.Foe(brawl, ActorKind.Brute, 60);
            var grunt = Lab.Foe(brawl, ActorKind.Grunt, -90, 3000);
            Assert.That(Lab.Until(brawl, b => brute.HasToken && brute.State == ActorState.Attack, 600), Is.True, "大男が攻撃権を得て振りかぶる");
            Lab.Tap(brawl, Lab.Attack);
            Assert.That(Lab.Until(brawl, b => b.HitCount > 0, 20), Is.True, "ジャブが大男に当たる");
            Assert.That(brute.State, Is.EqualTo(ActorState.Attack), "ジャブでは怯まず振り下ろしを続ける");
            Assert.That(brute.HasToken, Is.True, "攻撃を続ける間は攻撃権も持ったまま");
            for (int t = 0; t < 30; t++)
            {
                brawl.Step(Pad.Neutral);
                int attacking = (brute.State == ActorState.Attack ? 1 : 0) + (grunt.State == ActorState.Attack ? 1 : 0);
                Assert.That(attacking, Is.LessThanOrEqualTo(1), $"tick {t}: 同時に攻める敵は MaxAttackers まで");
                Assert.That(brawl.Attackers, Is.LessThanOrEqualTo(1));
            }
        }

        [Test]
        public void AttackersStayWithinTheLimitWhileThePlayerFightsBack()
        {
            var config = new BrawlConfig { Lives = 9, Capacity = 8, MaxActiveEnemies = 8, MaxAttackers = 2 };
            var brawl = Lab.Arena(config);
            brawl.AiEnabled = true;
            for (int i = 0; i < 6; i++)
            {
                var kind = i % 2 == 0 ? ActorKind.Brute : ActorKind.Grunt;
                Lab.Foe(brawl, kind, (i % 2 == 0 ? 1 : -1) * (50 + (i * 10)), (i - 3) * 1500);
            }

            var random = new IntGeometry.SeededRandom(3);
            int hits = 0;
            for (int t = 0; t < 4000 && brawl.Outcome == Outcome.Running; t++)
            {
                brawl.Step(new Pad(random.Range(-1, 1), random.Range(-1, 1), random.Next(3) == 0, false, false));
                int attacking = 0;
                for (int i = 1; i < brawl.SlotCount; i++)
                {
                    var e = brawl.At(i);
                    if (e.Active && e.State == ActorState.Attack)
                    {
                        attacking++;
                        Assert.That(e.HasToken, Is.True, $"tick {t}: 攻撃中の敵は攻撃権を持つ");
                    }
                }

                Assert.That(attacking, Is.LessThanOrEqualTo(config.MaxAttackers), $"tick {t}");
                hits = brawl.HitCount - brawl.PlayerHurtCount;
            }

            Assert.That(hits, Is.GreaterThan(20), "プレイヤーも殴り返している");
        }

        [Test]
        public void AttackRightsRotateFairly()
        {
            var brawl = Ring(5, new BrawlConfig { Lives = 9, MaxActiveEnemies = 5, MaxAttackers = 2 });
            var waitingGrants = new Dictionary<int, int>();
            var worst = 0;
            int granted = brawl.TokensGranted;
            var tokens = new int[brawl.SlotCount];
            for (int t = 0; t < 4000 && brawl.Outcome == Outcome.Running; t++)
            {
                var waitingBefore = new List<int>();
                for (int i = 1; i < brawl.SlotCount; i++)
                {
                    if (brawl.At(i).Active && brawl.At(i).Waited > 0) { waitingBefore.Add(i); }
                }

                brawl.Step(Pad.Neutral);
                for (int i = 1; i < brawl.SlotCount; i++)
                {
                    var e = brawl.At(i);
                    if (!e.Active) { continue; }
                    int gained = e.Tokens - tokens[i];
                    tokens[i] = e.Tokens;
                    if (gained > 0 || e.Waited == 0) { waitingGrants.Remove(i); }
                }

                int newGrants = brawl.TokensGranted - granted;
                granted = brawl.TokensGranted;
                if (newGrants == 0) { continue; }
                foreach (int i in waitingBefore)
                {
                    if (!waitingGrants.ContainsKey(i) && brawl.At(i).Waited == 0) { continue; }
                    if (brawl.At(i).Waited == 0) { continue; }
                    waitingGrants.TryGetValue(i, out int seen);
                    seen += newGrants;
                    waitingGrants[i] = seen;
                    worst = Math.Max(worst, seen);
                }
            }

            int min = int.MaxValue;
            int max = 0;
            for (int i = 1; i < brawl.SlotCount; i++)
            {
                if (!brawl.At(i).Active) { continue; }
                min = Math.Min(min, brawl.At(i).Tokens);
                max = Math.Max(max, brawl.At(i).Tokens);
            }

            TestContext.WriteLine($"tokens min={min} max={max} worst={worst} tick={brawl.Tick} lives={brawl.Lives}");
            Assert.That(min, Is.GreaterThan(5), "全員が何度も攻める");
            Assert.That(max - min, Is.LessThanOrEqualTo(Math.Max(3, min / 2)), "攻撃権の回数が偏らない（歩く時間・押し合い・プレイヤーのダウンで少しずれる）");
            Assert.That(worst, Is.LessThanOrEqualTo(4), "待っている間に他の敵へ渡る権利は (敵の数 − 1) 回まで");
        }

        [Test]
        public void TokenReturnsImmediatelyWhenTheHolderIsHit()
        {
            var brawl = Ring(1);
            var foe = brawl.At(1);
            Assert.That(Lab.Until(brawl, b => foe.HasToken, 300), Is.True);
            brawl.Place(foe, brawl.Player.X + (18 * Lab.Px), brawl.Player.Z, -1);
            brawl.AiEnabled = false;
            Lab.Tap(brawl, Lab.Attack);
            Lab.Until(brawl, b => b.HitCount > 0, 20);
            Assert.That(foe.HasToken, Is.False, "怯んだ Tick に返す");
            Assert.That(foe.Cooldown, Is.GreaterThan(0));
        }

        [Test]
        public void TokenTimesOutWhenTheHolderCannotReachThePlayer()
        {
            var config = new BrawlConfig { Lives = 9, TokenTimeout = 12, TokenCooldown = 30 };
            var brawl = Ring(1, config, ActorKind.Brute);
            var foe = brawl.At(1);
            Assert.That(Lab.Until(brawl, b => foe.HasToken, 100), Is.True);
            brawl.Place(foe, brawl.CameraX + config.ViewWidth - Lab.Px, brawl.Player.Z, -1);
            brawl.Place(brawl.Player, brawl.PlayerMinX, brawl.Player.Z, 1);
            Lab.Idle(brawl, config.TokenTimeout);
            Assert.That(foe.HasToken, Is.True);
            brawl.Step(Pad.Neutral);
            Assert.That(foe.HasToken, Is.False, "TokenTimeout を過ぎたら返す");
            Assert.That(foe.Cooldown, Is.EqualTo(config.TokenCooldown - 1), "返した Tick から数える");
        }

        [Test]
        public void KnockedDownAndDefeatedEnemiesStopRequesting()
        {
            var brawl = Ring(1);
            var foe = brawl.At(1);
            brawl.Place(foe, brawl.Player.X + (18 * Lab.Px), brawl.Player.Z, -1);
            brawl.SetHealth(foe, 1);
            Lab.Tap(brawl, Lab.Attack);
            Lab.Until(brawl, b => foe.Defeated, 20);
            int hurt = brawl.PlayerHurtCount;
            while (foe.Active)
            {
                Assert.That(foe.Waited, Is.Zero, "倒れた敵は攻撃権を求めない");
                Assert.That(foe.HasToken, Is.False);
                Assert.That(foe.Move, Is.Null);
                brawl.Step(Pad.Neutral);
            }

            Assert.That(brawl.PlayerHurtCount, Is.EqualTo(hurt));
            Assert.That(brawl.ActiveEnemies, Is.Zero, "消えて置き場が空く");
        }

        [Test]
        public void EnemyKilledMidSwingDoesNotLandIt()
        {
            var brawl = Lab.Arena();
            var brute = Lab.Foe(brawl, ActorKind.Brute, 20);
            brawl.SetHealth(brute, 1);
            Assert.That(brawl.ForceMove(brute, "smash"), Is.True);
            Lab.Tap(brawl, Lab.Special);
            Lab.Until(brawl, b => brute.Defeated, 20);
            Assert.That(brute.Phase, Is.EqualTo(MovePhase.None), "振り下ろしは消える");
            Lab.Idle(brawl, 60);
            Assert.That(brawl.PlayerHurtCount, Is.Zero);
        }

        [Test]
        public void SpawnsRespectTheActiveLimitAndRefill()
        {
            var config = new BrawlConfig { MaxActiveEnemies = 2, MaxAttackers = 1, Lives = 9 };
            var text = "start 96\nsection 0\nwave grunt right 40 1\nwave grunt right 60 1\nwave grunt left 20 1\nsection 600\nwave boss right 40 1\n";
            var brawl = new Brawl(Lab.Table(), config, StageData.Parse(text), 3) { AiEnabled = false };
            Lab.Idle(brawl, 5);
            Assert.That(brawl.ActiveEnemies, Is.EqualTo(2));
            Assert.That(brawl.Pending, Is.EqualTo(1), "上限を超えて出ない");
            var first = brawl.At(1);
            brawl.Place(first, brawl.Player.X + (18 * Lab.Px), brawl.Player.Z, -1);
            brawl.SetHealth(first, 1);
            Lab.Tap(brawl, Lab.Attack);
            Lab.Until(brawl, b => b.Pending == 0, 400);
            Assert.That(brawl.Pending, Is.Zero, "空いたら次が出る");
            Assert.That(brawl.ActiveEnemies, Is.EqualTo(2));
            Assert.That(brawl.Locked, Is.True, "残りがいる間は区画が終わらない");
        }

        [Test]
        public void ClearingASectionUnlocksAndWalkingEntersTheNext()
        {
            var text = "start 96\nsection 0\nwave grunt right 40 1\nsection 600\nwave boss right 40 30\n";
            var brawl = new Brawl(Lab.Table(), new BrawlConfig(), StageData.Parse(text), 3) { AiEnabled = false };
            Lab.Idle(brawl, 2);
            var foe = brawl.At(1);
            brawl.Place(foe, brawl.Player.X + (18 * Lab.Px), brawl.Player.Z, -1);
            brawl.SetHealth(foe, 1);
            Lab.Tap(brawl, Lab.Attack);
            Assert.That(Lab.Until(brawl, b => !b.Locked, 400), Is.True);
            int camera = brawl.CameraX;
            Lab.Idle(brawl, 30);
            Assert.That(brawl.CameraX, Is.EqualTo(camera), "歩かなければ画面は進まない");
            Assert.That(Lab.Until(brawl, b => b.SectionIndex == 1, 2000, new Pad(1, 0, false, false, false)), Is.True);
            Assert.That(brawl.Locked, Is.True);
            Assert.That(brawl.CameraX, Is.EqualTo(brawl.Stage.Sections[1].CameraX));
            Assert.That(brawl.Player.X, Is.GreaterThanOrEqualTo(brawl.PlayerMinX), "プレイヤーは画面の中");
        }

        [Test]
        public void DefeatingTheBossClearsTheStage()
        {
            var text = "start 96\nsection 0\nwave boss right 40 1\nwave grunt left 40 1000\n";
            var brawl = new Brawl(Lab.Table(), new BrawlConfig(), StageData.Parse(text), 3) { AiEnabled = false };
            Lab.Idle(brawl, 2);
            var boss = brawl.At(1);
            Assert.That(boss.Kind, Is.EqualTo(ActorKind.Boss));
            brawl.Place(boss, brawl.Player.X + (20 * Lab.Px), brawl.Player.Z, -1);
            brawl.SetHealth(boss, 1);
            Lab.Tap(brawl, Lab.Attack);
            Assert.That(Lab.Until(brawl, b => b.Outcome != Outcome.Running, 400), Is.True);
            Assert.That(brawl.Outcome, Is.EqualTo(Outcome.Cleared), "ボスを倒せば護衛が残っていても面クリア");
            uint hash = brawl.Hash();
            Lab.Run(brawl, Lab.Attack, 10);
            Assert.That(brawl.Hash(), Is.EqualTo(hash), "クリア後の入力は状態を変えない");
        }

        [TestCase("start 96\nsection 0\n", "敵のいない区画")]
        [TestCase("start 96\nsection 0\nwave boss right 40 1\nsection 400\nwave grunt right 40 1\n", "最後でない区画のボス")]
        [TestCase("start 96\nsection 0\nwave grunt right 40 1\n", "ボスがいない")]
        [TestCase("start 96\nsection 0\nwave boss right 40 1\nwave boss right 40 1\n", "ボスが 2 体")]
        [TestCase("start 96\nsection 0\nwave boss right 200 1\n", "帯の外の z")]
        [TestCase("start 96\nsection 0\nwave grunt right 40 30\nwave boss right 40 1\n", "遅れの順でない")]
        [TestCase("start 96\nsection 400\nwave grunt right 40 1\nsection 400\nwave boss right 40 1\n", "同じ位置の区画")]
        [TestCase("start 1000\nsection 0\nwave boss right 40 1\n", "画面の外の開始位置")]
        public void BrokenStagesAreRejected(string text, string why)
        {
            Assert.Throws<FormatException>(() => StageData.Parse(text).Check(Lab.Table(), new BrawlConfig()), why);
        }

        [TestCase("start 96\nsection 0\nwave dragon right 40 1\n")]
        [TestCase("start 96\nsection 0\nwave grunt up 40 1\n")]
        [TestCase("start 96\nwave grunt right 40 1\n")]
        [TestCase("section 0\nwave boss right 40 1\n")]
        public void MalformedStagesAreRejected(string text)
        {
            Assert.Throws<FormatException>(() => StageData.Parse(text));
        }

        [Test]
        public void RealStageIsValid()
        {
            var stage = Lab.Stage();
            stage.Check(Lab.Table(), new BrawlConfig());
            Assert.That(stage.Sections.Count, Is.EqualTo(5), "4 区画 ＋ ボス区画");
            Assert.That(stage.Sections[stage.Sections.Count - 1].HasBoss, Is.True);
            var kinds = new HashSet<ActorKind>();
            foreach (var section in stage.Sections)
            {
                foreach (var wave in section.Waves) { kinds.Add(wave.Kind); }
            }

            Assert.That(kinds, Is.EquivalentTo(new[] { ActorKind.Grunt, ActorKind.Brute, ActorKind.Kicker, ActorKind.Boss }), "敵 3 種＋ボス");
        }

        /// <summary>出現の上限を置き場の最大にして長く暴れても、上限・帯・端・攻撃権が守られる。</summary>
        [Test]
        public void CrowdedBrawlStaysWithinItsInvariants()
        {
            var config = new BrawlConfig { Lives = 9, Capacity = BrawlConfig.MaxCapacity, MaxActiveEnemies = BrawlConfig.MaxCapacity, MaxAttackers = 4 };
            var sb = new System.Text.StringBuilder("start 96\n");
            for (int s = 0; s < 3; s++)
            {
                sb.Append("section ").Append(s * 500).Append('\n');
                for (int i = 0; i < 40; i++)
                {
                    string kind = (i % 3) == 0 ? "brute" : (i % 3) == 1 ? "kicker" : "grunt";
                    sb.Append("wave ").Append(kind).Append((i & 1) == 0 ? " left " : " right ").Append(i * 2 % 96).Append(' ').Append(i * 4).Append('\n');
                }
            }

            sb.Append("section 1600\nwave boss right 48 1\n");
            var brawl = new Brawl(Lab.Table(), config, StageData.Parse(sb.ToString()), 11);
            var random = new IntGeometry.SeededRandom(5);
            int peak = 0;
            for (int t = 0; t < 20000 && brawl.Outcome == Outcome.Running; t++)
            {
                var pad = new Pad(random.Range(-1, 1), random.Range(-1, 1), random.Next(3) == 0, random.Next(40) == 0, random.Next(90) == 0);
                brawl.Step(pad);
                peak = Math.Max(peak, brawl.ActiveEnemies);
                Assert.That(brawl.ActiveEnemies, Is.LessThanOrEqualTo(config.MaxActiveEnemies));
                Assert.That(brawl.Attackers, Is.LessThanOrEqualTo(config.MaxAttackers));
                for (int i = 0; i < brawl.SlotCount; i++)
                {
                    var a = brawl.At(i);
                    if (!a.Active) { continue; }
                    Assert.That(a.Z, Is.InRange(config.ZMin, config.ZMax));
                    Assert.That(a.Y, Is.GreaterThanOrEqualTo(0));
                    if (i == 0) { Assert.That(a.X, Is.InRange(brawl.PlayerMinX, brawl.PlayerMaxX)); }
                    else { Assert.That(a.X, Is.InRange(brawl.EnemyMinX, brawl.EnemyMaxX)); }
                    if (a.Partner >= 0) { Assert.That(brawl.At(a.Partner).Partner, Is.EqualTo(i), "掴みは両側で対"); }
                }
            }

            Assert.That(peak, Is.GreaterThan(16), "多数の敵が同時に出た");
        }
    }
}
