using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>コンボ・掴み・緊急技・ヒットストップ・多重ヒット・ダウン・残機。</summary>
    public sealed class CombatTests
    {
        private static readonly Pad _right = new(1, 0, false, false, false);

        [Test]
        public void ComboChainsOnlyAfterAHit()
        {
            var brawl = Lab.Arena();
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 18);
            var p = brawl.Player;
            for (int press = 0; press < 3; press++)
            {
                Lab.Tap(brawl, Lab.Attack);
                Lab.Idle(brawl, 6);
            }

            Lab.Until(brawl, b => p.Move == null);
            Assert.That(brawl.HitCount, Is.EqualTo(3), "ジャブ → フック → アッパー");
            Assert.That(foe.State, Is.EqualTo(ActorState.Knockdown), "3 段目でダウン");

            var whiff = Lab.Arena();
            for (int press = 0; press < 2; press++)
            {
                Lab.Tap(whiff, Lab.Attack);
                Lab.Idle(whiff, 6);
                Assert.That(whiff.Player.Move?.Id, Is.EqualTo("jab1"), "空振りは 1 段目からやり直し");
                Lab.Until(whiff, b => b.Player.Move == null);
            }
        }

        [Test]
        public void ChainWindowBoundaryIsExact()
        {
            var jab = Lab.Table()[ActorKind.Player].Find("jab1");
            int lastWindowTick = jab.Startup + jab.Active + jab.Window - 1;
            foreach (var (pressAt, chains) in new[] { (lastWindowTick, true), (lastWindowTick + 1, false) })
            {
                var brawl = Lab.Arena(new BrawlConfig { BufferTicks = 1 });
                Lab.Foe(brawl, ActorKind.Grunt, 18);
                var p = brawl.Player;
                brawl.Step(Lab.Attack);
                Lab.Until(brawl, b => p.MoveTick == pressAt - 1 && p.HitStop == 0, 200, Pad.Neutral);
                brawl.Step(Lab.Attack);
                Assert.That(p.Move?.Id, Is.EqualTo(chains ? "jab2" : "jab1"), $"硬直の始めから {pressAt - jab.Startup - jab.Active} Tick 目");
            }
        }

        [Test]
        public void BufferedPressFiresOnceWhenRecoveryEnds()
        {
            var brawl = Lab.Arena();
            var p = brawl.Player;
            brawl.Step(Lab.Attack);
            var jab = p.Move!;
            Lab.Until(brawl, b => p.MoveTick == jab.Total - 3);
            Lab.Tap(brawl, Lab.Attack);
            Assert.That(p.Move, Is.SameAs(jab), "まだ硬直");
            long activation = p.Activation;
            brawl.Step(Pad.Neutral);
            Assert.That(p.Activation, Is.EqualTo(activation + 1), "硬直が解けた Tick に先行入力で出る");
            Lab.Until(brawl, b => p.Move == null);
            Lab.Idle(brawl, 20);
            Assert.That(p.Activation, Is.EqualTo(activation + 1), "1 回押せば 1 回だけ");
        }

        [Test]
        public void ExpiredBufferDoesNotFire()
        {
            var config = new BrawlConfig();
            var brawl = Lab.Arena(config);
            var p = brawl.Player;
            brawl.Step(Lab.Attack);
            var jab = p.Move!;
            Lab.Until(brawl, b => p.MoveTick == jab.Total - config.BufferTicks - 1);
            Lab.Tap(brawl, Lab.Attack);
            long activation = p.Activation;
            Lab.Idle(brawl, 30);
            Assert.That(p.Activation, Is.EqualTo(activation), "先行入力は BufferTicks で消える");
        }

        [Test]
        public void OneActivationHitsEachTargetOnce()
        {
            var brawl = Lab.Arena();
            var foe = Lab.Foe(brawl, ActorKind.Brute, 20);
            var jab = brawl.Table[ActorKind.Player].Find("jab1");
            Assert.That(jab.Active, Is.GreaterThan(1), "持続が 2 Tick 以上の技で確かめる");
            Lab.Tap(brawl, Lab.Attack);
            Lab.Until(brawl, b => b.Player.Move == null);
            Assert.That(brawl.HitCount, Is.EqualTo(1));
            Assert.That(foe.Health, Is.EqualTo(foe.Def!.Health - jab.Damage));

            Lab.Idle(brawl, 30);
            Lab.Tap(brawl, Lab.Attack);
            Lab.Until(brawl, b => b.Player.Move == null);
            Assert.That(brawl.HitCount, Is.EqualTo(2), "次の発動では再び当たる");
        }

        [Test]
        public void HitStopFreezesOnlyTheTwoInvolved()
        {
            var brawl = Lab.Arena();
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 18);
            var other = Lab.Foe(brawl, ActorKind.Brute, -120);
            var p = brawl.Player;
            brawl.Step(Lab.Attack);
            Lab.Until(brawl, b => b.HitCount == 1, 20);
            Assert.That(brawl.ForceMove(other, "smash"), Is.True);
            int stop = p.HitStop;
            Assert.That(stop, Is.GreaterThan(0));
            int moveTick = p.MoveTick;
            int foeTimer = foe.Timer;
            int foeX = foe.X;
            int otherTick = other.MoveTick;
            Lab.Tap(brawl, Lab.Attack);
            Lab.Idle(brawl, stop - 2);
            Assert.That(p.MoveTick, Is.EqualTo(moveTick), "当てた側は止まる");
            Assert.That((foe.Timer, foe.X), Is.EqualTo((foeTimer, foeX)), "当たった側も止まる");
            Assert.That(other.MoveTick, Is.EqualTo(otherTick + stop), "関係ない敵は動き続ける");
            Assert.That(brawl.AttackBuffer, Is.GreaterThan(0), "止まりの間も先行入力は残る");
            brawl.Step(Pad.Neutral);
            Assert.That(p.HitStop, Is.Zero);
            Lab.Until(brawl, b => p.Move?.Id != "jab1", 40);
            Assert.That(p.Move?.Id, Is.EqualTo("jab2"), "止まりの間の押下が止まり明けに効く");
        }

        [Test]
        public void BrutesShrugOffTheFirstJab()
        {
            var brawl = Lab.Arena();
            var brute = Lab.Foe(brawl, ActorKind.Brute, 20);
            Assert.That(brawl.ForceMove(brute, "smash"), Is.True);
            Lab.Tap(brawl, Lab.Attack);
            Lab.Until(brawl, b => b.HitCount == 1, 20);
            Assert.That(brute.State, Is.EqualTo(ActorState.Attack), "ジャブでは怯まない");
            Assert.That(brute.Move?.Id, Is.EqualTo("smash"));

            var second = Lab.Arena();
            var grunt = Lab.Foe(second, ActorKind.Grunt, 18);
            Lab.Tap(second, Lab.Attack);
            Lab.Until(second, b => b.HitCount == 1, 20);
            Assert.That(grunt.State, Is.EqualTo(ActorState.HitStun), "チンピラは怯む");
        }

        [Test]
        public void TradesApplyBothHits()
        {
            var brawl = Lab.Arena();
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 18);
            var punch = foe.Def!.Find("punch1");
            var jab = brawl.Table[ActorKind.Player].Find("jab1");
            Assert.That(brawl.ForceMove(foe, "punch1"), Is.True);
            Lab.Idle(brawl, punch.Startup - jab.Startup - 1);
            Lab.Tap(brawl, Lab.Attack);
            Lab.Until(brawl, b => b.HitCount > 0, 30);
            Assert.That(brawl.HitCount, Is.EqualTo(2), "同じ Tick の攻撃は両方当たる");
            Assert.That(brawl.Player.State, Is.EqualTo(ActorState.HitStun));
            Assert.That(foe.State, Is.EqualTo(ActorState.HitStun));
        }

        [Test]
        public void DefeatedEnemyNeverAttacksAgain()
        {
            var brawl = Lab.Arena();
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 18);
            brawl.SetHealth(foe, 1);
            Assert.That(brawl.ForceMove(foe, "punch1"), Is.True);
            Lab.Tap(brawl, Lab.Attack);
            Lab.Until(brawl, b => b.HitCount > 0, 30);
            Assert.That(foe.Defeated, Is.True);
            Assert.That(foe.Move, Is.Null, "発生前に倒した敵の技は消える");
            int hurt = brawl.PlayerHurtCount;
            Assert.That(Lab.Until(brawl, b => !foe.Active, 400), Is.True, "倒れた敵は消える");
            Assert.That(brawl.PlayerHurtCount, Is.EqualTo(hurt), "倒れてから消えるまで攻撃しない");
            Assert.That(brawl.ActiveEnemies, Is.Zero);
            Assert.That(brawl.Defeats, Is.EqualTo(1));
        }

        [Test]
        public void DefeatScoresOnlyOnce()
        {
            var brawl = Lab.Arena();
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 18);
            brawl.SetHealth(foe, 1);
            var special = brawl.Table[ActorKind.Player].Find("special");
            Lab.Tap(brawl, Lab.Attack);
            Lab.Until(brawl, b => b.HitCount > 0, 30);
            long score = brawl.Score;
            Lab.Tap(brawl, Lab.Special);
            Lab.Idle(brawl, special.Total);
            Assert.That(brawl.HitCount, Is.EqualTo(1), "倒れている敵には当たらない");
            Assert.That(brawl.Score, Is.EqualTo(score));
            Assert.That(brawl.Defeats, Is.EqualTo(1));
        }

        [Test]
        public void GrabKneesTwiceThenThrows()
        {
            var config = new BrawlConfig();
            var brawl = Lab.Arena(config);
            var foe = Lab.Foe(brawl, ActorKind.Brute, 30);
            var p = brawl.Player;
            Assert.That(Lab.Until(brawl, b => p.State == ActorState.Grab, 30, _right), Is.True);
            Assert.That(foe.State, Is.EqualTo(ActorState.Grabbed));
            Assert.That(foe.Z, Is.EqualTo(p.Z));
            var knee = brawl.Table[ActorKind.Player].Find("knee");
            var toss = brawl.Table[ActorKind.Player].Find("throw");
            for (int k = 0; k < config.MaxKnees; k++)
            {
                Lab.Tap(brawl, Lab.Attack);
                Lab.Until(brawl, b => p.Move == null, 60);
            }

            Assert.That(foe.Health, Is.EqualTo(foe.Def!.Health - (config.MaxKnees * knee.Damage)));
            Assert.That(foe.State, Is.EqualTo(ActorState.Grabbed), "膝では離さない");
            Lab.Tap(brawl, Lab.Attack);
            Lab.Until(brawl, b => foe.State != ActorState.Grabbed, 60);
            Assert.That(foe.State, Is.EqualTo(ActorState.Knockdown), "3 回目は投げ");
            Assert.That(foe.VX, Is.GreaterThan(0), "向いている側へ");
            Assert.That(foe.Health, Is.EqualTo(foe.Def.Health - (config.MaxKnees * knee.Damage) - toss.Damage));
        }

        [Test]
        public void GrabWithDirectionThrowsThatWay()
        {
            var brawl = Lab.Arena();
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 30);
            var p = brawl.Player;
            Lab.Until(brawl, b => p.State == ActorState.Grab, 30, _right);
            Lab.Tap(brawl, new Pad(-1, 0, true, false, false));
            Lab.Until(brawl, b => foe.State != ActorState.Grabbed, 60);
            Assert.That(foe.State, Is.EqualTo(ActorState.Knockdown));
            Assert.That(foe.VX, Is.LessThan(0), "後ろへ投げる");
        }

        [Test]
        public void GrabTimesOutAndReleases()
        {
            var config = new BrawlConfig();
            var brawl = Lab.Arena(config);
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 30);
            var p = brawl.Player;
            Lab.Until(brawl, b => p.State == ActorState.Grab, 30, _right);
            Lab.Idle(brawl, config.GrabTicks - 1);
            Assert.That(p.State, Is.EqualTo(ActorState.Grab));
            brawl.Step(Pad.Neutral);
            Assert.That(p.State, Is.Not.EqualTo(ActorState.Grab));
            Assert.That(foe.State, Is.EqualTo(ActorState.HitStun));
            Assert.That((p.Partner, foe.Partner), Is.EqualTo((-1, -1)));
        }

        [Test]
        public void KnockedDownAndAirborneEnemiesCannotBeGrabbed()
        {
            var brawl = Lab.Arena();
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 18);
            Lab.Tap(brawl, Lab.Special);
            Lab.Until(brawl, b => b.Player.Move == null, 60);
            Assert.That(foe.State, Is.EqualTo(ActorState.Knockdown));
            Lab.Run(brawl, _right, 20);
            Assert.That(brawl.Player.State, Is.Not.EqualTo(ActorState.Grab));
        }

        [Test]
        public void SpecialCostsHealthAndIsInvulnerable()
        {
            var brawl = Lab.Arena();
            var p = brawl.Player;
            var special = brawl.Table[ActorKind.Player].Find("special");
            var brute = Lab.Foe(brawl, ActorKind.Brute, 20, 8000);
            Lab.Tap(brawl, Lab.Special);
            Assert.That(p.Health, Is.EqualTo(p.Def!.Health - special.Cost));
            Assert.That(p.Invuln, Is.GreaterThanOrEqualTo(special.Startup + special.Active - 2));
            Assert.That(Brawl.Hurtable(p), Is.False, "発生と持続の間は無敵");
            Lab.Until(brawl, b => p.Move == null, 60);
            Assert.That(brute.Health, Is.EqualTo(brute.Def!.Health), "奥行きの外の敵には当たらない");

            brawl.SetHealth(p, special.Cost);
            long activation = p.Activation;
            Lab.Tap(brawl, Lab.Special);
            Assert.That(p.Activation, Is.EqualTo(activation), "体力が代償以下なら出ない（自分で倒れない）");
            Assert.That(p.Health, Is.EqualTo(special.Cost));
        }

        [Test]
        public void SpecialBreaksOutOfHitStun()
        {
            var brawl = Lab.Arena();
            var p = brawl.Player;
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 18);
            Assert.That(brawl.ForceMove(foe, "punch1"), Is.True);
            Lab.Until(brawl, b => p.State == ActorState.HitStun, 30);
            Lab.Until(brawl, b => p.HitStop == 0, 30);
            Lab.Tap(brawl, Lab.Special);
            Assert.That(p.Move?.Id, Is.EqualTo("special"));
            Lab.Until(brawl, b => foe.State == ActorState.Knockdown, 30);
            Assert.That(foe.State, Is.EqualTo(ActorState.Knockdown), "攻撃中の敵も吹き飛ばす");
        }

        [Test]
        public void PlayerLosesLivesThenTheGame()
        {
            var config = new BrawlConfig { Lives = 2 };
            var brawl = Lab.Arena(config);
            var p = brawl.Player;
            var brute = Lab.Foe(brawl, ActorKind.Brute, 20);
            brawl.SetHealth(p, 1);
            Assert.That(brawl.ForceMove(brute, "smash"), Is.True);
            Assert.That(Lab.Until(brawl, b => b.Deaths == 1, 400), Is.True);
            Assert.That(brawl.Lives, Is.EqualTo(1));
            Assert.That(p.State, Is.EqualTo(ActorState.Idle), "その場で復帰");
            Assert.That(p.Health, Is.EqualTo(p.Def!.Health));
            Assert.That(p.Invuln, Is.EqualTo(config.RespawnInvuln));
            Assert.That(Brawl.Hurtable(p), Is.False);
            Lab.Idle(brawl, config.RespawnInvuln);
            Assert.That(Brawl.Hurtable(p), Is.True, "無敵は RespawnInvuln Tick");

            brawl.SetHealth(p, 1);
            brawl.Place(brute, p.X + (20 * Lab.Px), p.Z, -1);
            Lab.Until(brawl, b => brute.State == ActorState.Idle, 200);
            Assert.That(brawl.ForceMove(brute, "smash"), Is.True);
            Assert.That(Lab.Until(brawl, b => b.Outcome == Outcome.Lost, 400), Is.True);
            uint hash = brawl.Hash();
            Lab.Run(brawl, Lab.Attack, 5);
            Lab.Run(brawl, Lab.Special, 5);
            Assert.That(brawl.Hash(), Is.EqualTo(hash), "終わった後の入力は状態を変えない");
        }

        [Test]
        public void JumpAttackOnlyOncePerJump()
        {
            var brawl = Lab.Arena();
            var p = brawl.Player;
            brawl.Step(Lab.Jump);
            Lab.Tap(brawl, Lab.Attack);
            long activation = p.Activation;
            Assert.That(p.Move?.Id, Is.EqualTo("jumpkick"));
            Lab.Until(brawl, b => p.Move == null, 60);
            Lab.Tap(brawl, Lab.Attack);
            Assert.That(p.Activation, Is.EqualTo(activation), "着地まで 1 回");
            Lab.Until(brawl, b => p.State == ActorState.Idle, 120);
            Assert.That(p.Y, Is.Zero);
        }

        [Test]
        public void SameInputsGiveTheSameHashAndClonesDiverge()
        {
            static Brawl Play(int ticks)
            {
                var brawl = Lab.Arena();
                Lab.Foe(brawl, ActorKind.Grunt, 18);
                Lab.Foe(brawl, ActorKind.Brute, -30, 2000);
                for (int t = 0; t < ticks; t++)
                {
                    int phase = t % 40;
                    var pad = new Pad(phase < 10 ? 1 : phase < 20 ? -1 : 0, phase % 7 == 0 ? 1 : 0, phase % 5 == 0, phase == 33, t == 77);
                    brawl.Step(pad);
                }

                return brawl;
            }

            var a = Play(400);
            var b = Play(400);
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            var clone = a.Clone();
            Assert.That(clone.Hash(), Is.EqualTo(a.Hash()));
            clone.Step(Lab.Attack);
            a.Step(Pad.Neutral);
            Assert.That(clone.Hash(), Is.Not.EqualTo(a.Hash()), "複製は元と状態を共有しない");
            Assert.That(clone.Player, Is.Not.SameAs(a.Player));
        }

        /// <summary>整数だけの規則なので .NET と Mono で同じ値になる（固定値は両方で確かめる）。</summary>
        [Test]
        public void ScriptedBrawlHashIsFixed()
        {
            var brawl = Lab.Arena(seed: 7);
            Lab.Foe(brawl, ActorKind.Grunt, 18);
            Lab.Foe(brawl, ActorKind.Kicker, 60, -3000);
            Lab.Foe(brawl, ActorKind.Brute, -40, 2500);
            for (int t = 0; t < 3000; t++)
            {
                int phase = t % 53;
                var pad = new Pad(phase < 12 ? 1 : phase < 24 ? -1 : 0, phase % 9 == 0 ? 1 : phase % 11 == 0 ? -1 : 0, phase % 4 == 0, phase == 30, phase == 45 && t % 3 == 0);
                brawl.Step(pad);
            }

            Assert.That(brawl.HitCount, Is.GreaterThanOrEqualTo(4), "台本で当てている");
            Assert.That(brawl.Hash(), Is.EqualTo(113378702u));
        }

        [Test]
        public void HashSeesHeldButtonsAndBuffer()
        {
            var a = Lab.Arena();
            var b = Lab.Arena();
            a.Step(Lab.Attack);
            b.Step(Lab.Attack);
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            a.Step(Lab.Attack);
            b.Step(Pad.Neutral);
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()), "押したままかどうかで以後が分かれる");
        }

        [Test]
        public void InvalidInputsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Pad(2, 0, false, false, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Pad(0, -2, false, false, false));
            var brawl = Lab.Arena(new BrawlConfig { Capacity = 2, MaxActiveEnemies = 2, MaxAttackers = 1 });
            Assert.Throws<ArgumentOutOfRangeException>(() => brawl.Spawn(ActorKind.Grunt, 0, -1, 1));
            Assert.Throws<ArgumentException>(() => brawl.Spawn(ActorKind.Player, 0, 0, 1));
            brawl.Spawn(ActorKind.Grunt, 0, 0, 1);
            var last = brawl.Spawn(ActorKind.Grunt, 0, 0, 1);
            Assert.Throws<InvalidOperationException>(() => brawl.Spawn(ActorKind.Grunt, 0, 0, 1), "置き場が満杯");
            Assert.Throws<ArgumentException>(() => brawl.ForceMove(brawl.Player, "knee"), "掴みの技は外から出せない");
            Assert.Throws<ArgumentOutOfRangeException>(() => brawl.SetHealth(last, 0));
        }

        [TestCase(nameof(BrawlConfig.Capacity), 0)]
        [TestCase(nameof(BrawlConfig.Capacity), BrawlConfig.MaxCapacity + 1)]
        [TestCase(nameof(BrawlConfig.MaxActiveEnemies), 0)]
        [TestCase(nameof(BrawlConfig.MaxActiveEnemies), 9)]
        [TestCase(nameof(BrawlConfig.MaxAttackers), 0)]
        [TestCase(nameof(BrawlConfig.BufferTicks), 0)]
        [TestCase(nameof(BrawlConfig.Lives), 0)]
        [TestCase(nameof(BrawlConfig.DepthDen), 0)]
        [TestCase(nameof(BrawlConfig.ZMax), 100)]
        public void InvalidConfigsAreRejected(string field, int value)
        {
            var config = new BrawlConfig();
            typeof(BrawlConfig).GetField(field)!.SetValue(config, value);
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Copy().Validate());
        }
    }
}
