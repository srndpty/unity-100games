using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>回復アイテムと、区画を片付けた後の画面の送り。</summary>
    public sealed class ItemAndCameraTests
    {
        private static readonly Pad _right = new(1, 0, false, false, false);

        private static void Defeat(Brawl brawl, Actor foe)
        {
            brawl.AiEnabled = false;
            brawl.Place(foe, brawl.Player.X + (18 * Lab.Px), brawl.Player.Z, -1);
            brawl.SetHealth(foe, 1);
            Lab.Tap(brawl, Lab.Attack);
            Assert.That(Lab.Until(brawl, b => !foe.Active, 400), Is.True, "倒れて消える");
        }

        [Test]
        public void FoodEnemyDropsAnItemWhereItVanishes()
        {
            var brawl = Lab.Arena();
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 18);
            brawl.AiEnabled = false;
            var plain = brawl.Spawn(ActorKind.Grunt, brawl.Player.X - (60 * Lab.Px), brawl.Player.Z, 1);
            var food = brawl.Spawn(ActorKind.Brute, brawl.Player.X + (90 * Lab.Px), brawl.Player.Z + (10 * Lab.Px), -1, true);
            Defeat(brawl, foe);
            Assert.That(brawl.ItemsOnGround, Is.Zero, "food の無い敵は落とさない");
            Assert.That(plain.Active, Is.True);

            Assert.That(food.Food, Is.True);
            brawl.Place(food, brawl.Player.X + (18 * Lab.Px), brawl.Player.Z + (2 * Lab.Px), -1);
            brawl.SetHealth(food, 1);
            Lab.Tap(brawl, Lab.Attack);
            Assert.That(Lab.Until(brawl, b => food.State == ActorState.Knockdown && food.Timer > 0, 200), Is.True, "倒れて横たわる");
            int x = food.X;
            int z = food.Z;
            Assert.That(Lab.Until(brawl, b => !food.Active, 400), Is.True);
            Assert.That(brawl.ItemsOnGround, Is.EqualTo(1), "food の敵は消えるときに落とす");
            var item = brawl.ItemAt(0);
            Assert.That((item.X, item.Z), Is.EqualTo((Math.Clamp(x, brawl.PlayerMinX, brawl.PlayerMaxX), z)), "消えた位置に落ちる");
        }

        [Test]
        public void DropIsClampedIntoPlayerReach()
        {
            var brawl = Lab.Arena();
            Assert.That(brawl.Drop(brawl.EnemyMaxX, 500 * Lab.Px), Is.True);
            var item = brawl.ItemAt(0);
            Assert.That(item.X, Is.EqualTo(brawl.PlayerMaxX), "画面の外に落ちても拾える所へ寄せる");
            Assert.That(item.Z, Is.EqualTo(new BrawlConfig().ZMax));
        }

        [Test]
        public void PickingUpHealsUpToTheMaximumOnce()
        {
            var config = new BrawlConfig();
            var brawl = Lab.Arena(config);
            var p = brawl.Player;
            brawl.SetHealth(p, p.Def!.Health - config.HealAmount - 100);
            brawl.Drop(p.X + (30 * Lab.Px), p.Z);
            brawl.Drop(p.X + (60 * Lab.Px), p.Z);
            Assert.That(Lab.Until(brawl, b => b.HealCount == 1, 60, _right), Is.True);
            Assert.That(p.Health, Is.EqualTo(p.Def.Health - 100));
            Assert.That(brawl.ItemsOnGround, Is.EqualTo(1), "拾った 1 個だけ消える");
            Lab.Run(brawl, _right, 3);
            Assert.That(brawl.HealCount, Is.EqualTo(1), "同じアイテムは 1 回");
            Assert.That(Lab.Until(brawl, b => b.HealCount == 2, 60, _right), Is.True);
            Assert.That(p.Health, Is.EqualTo(p.Def!.Health), "最大を超えない");
            Assert.That(brawl.ItemsOnGround, Is.Zero);
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        public void PickupNeedsTheSameDepth(int extra, bool picks)
        {
            var config = new BrawlConfig();
            var brawl = Lab.Arena(config);
            var p = brawl.Player;
            brawl.SetHealth(p, 100);
            int limit = config.ItemDepth + p.Def!.Body.Depth;
            brawl.Drop(p.X + (30 * Lab.Px), p.Z + limit - 1 + extra);
            Lab.Run(brawl, _right, 40);
            Assert.That(brawl.HealCount == 1, Is.EqualTo(picks), "奥行きの差が半幅の和ちょうどなら拾わない");
        }

        [Test]
        public void JumpingOverAnItemDoesNotPickItUp()
        {
            var brawl = Lab.Arena();
            var p = brawl.Player;
            brawl.SetHealth(p, 100);
            brawl.Drop(p.X + (40 * Lab.Px), p.Z);
            brawl.Step(new Pad(1, 0, false, true, false));
            Lab.Until(brawl, b => b.Player.State == ActorState.Land, 120, _right);
            Assert.That(p.X, Is.GreaterThan(brawl.ItemAt(0).X + (16 * Lab.Px)), "跳び越えた");
            Assert.That(brawl.HealCount, Is.Zero);
            Assert.That(brawl.ItemsOnGround, Is.EqualTo(1));
        }

        [Test]
        public void ItemsAreCappedAndHashedAndCloned()
        {
            var a = Lab.Arena();
            var b = Lab.Arena();
            a.Drop(a.Player.X + (100 * Lab.Px), a.Player.Z);
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()), "アイテムもハッシュに入る");
            var clone = a.Clone();
            Assert.That(clone.ItemAt(0), Is.EqualTo(a.ItemAt(0)));
            clone.SetHealth(clone.Player, 100);
            Lab.Run(clone, _right, 60);
            Assert.That(clone.ItemsOnGround, Is.Zero);
            Assert.That(a.ItemsOnGround, Is.EqualTo(1), "複製は元と共有しない");
            for (int i = 1; i < Brawl.ItemCapacity; i++) { Assert.That(a.Drop(a.Player.X, a.Player.Z + (40 * Lab.Px)), Is.True); }
            Assert.That(a.Drop(a.Player.X, a.Player.Z), Is.False, "置き場が満杯なら落とさない");
        }

        [Test]
        public void StageFoodFlagIsParsedAndBounded()
        {
            var stage = StageData.Parse("start 96\nsection 0\nwave grunt right 40 1 food\nwave boss right 40 1\n");
            Assert.That(stage.Sections[0].Waves[0].Food, Is.True);
            Assert.That(stage.Sections[0].Waves[1].Food, Is.False);
            Assert.Throws<FormatException>(() => StageData.Parse("start 96\nsection 0\nwave grunt right 40 1 apple\n"));
            var many = "start 96\nsection 0\n";
            for (int i = 0; i <= Brawl.ItemCapacity; i++) { many += "wave grunt right 40 1 food\n"; }
            many += "wave boss right 40 1\n";
            Assert.Throws<FormatException>(() => StageData.Parse(many).Check(Lab.Table(), new BrawlConfig()), "置き場より多い food");
            int foods = 0;
            foreach (var section in Lab.Stage().Sections)
            {
                foreach (var wave in section.Waves)
                {
                    if (wave.Food) { foods++; }
                }
            }

            Assert.That(foods, Is.InRange(1, Brawl.ItemCapacity), "本番の面にも回復アイテムがある");
        }

        [Test]
        public void CameraScrollsSmoothlyAfterTheSectionIsCleared()
        {
            var config = new BrawlConfig();
            var text = "start 96\nsection 0\nwave grunt right 40 1\nsection 600\nwave boss right 40 30\n";
            var brawl = new Brawl(Lab.Table(), config, StageData.Parse(text), 3) { AiEnabled = false };
            Lab.Idle(brawl, 2);
            var p = brawl.Player;
            brawl.Place(p, brawl.PlayerMaxX - (20 * Lab.Px), p.Z, 1);
            var foe = brawl.At(1);
            brawl.Place(foe, p.X - (18 * Lab.Px), p.Z, 1);
            brawl.Place(p, p.X, p.Z, -1);
            brawl.SetHealth(foe, 1);
            Lab.Tap(brawl, Lab.Attack);
            Assert.That(Lab.Until(brawl, b => !b.Locked, 400), Is.True);
            int jumpTarget = p.X - (config.ViewWidth / 2);
            Assert.That(jumpTarget - brawl.CameraX, Is.GreaterThan(config.ScrollSpeed * 10), "プレイヤーは画面の中央より十分右にいる");

            int camera = brawl.CameraX;
            int ticks = 0;
            while (brawl.SectionIndex == 0 && ticks++ < 2000)
            {
                brawl.Step(ticks < 60 ? Pad.Neutral : _right);
                int delta = brawl.CameraX - camera;
                Assert.That(delta, Is.InRange(0, config.ScrollSpeed), $"tick {ticks}: 1 Tick に ScrollSpeed までしか進まない");
                camera = brawl.CameraX;
            }

            Assert.That(brawl.SectionIndex, Is.EqualTo(1), "次の区画で止まる");
            Assert.That(brawl.CameraX, Is.EqualTo(brawl.Stage.Sections[1].CameraX));
        }

        [Test]
        public void ScrollSpeedIsValidated()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BrawlConfig { ScrollSpeed = 0 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new BrawlConfig { HealAmount = 0 }.Validate());
        }
    }
}
