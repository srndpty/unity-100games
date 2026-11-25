using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>奥行きの判定と描画順。</summary>
    public sealed class DepthTests
    {
        [Test]
        public void BoxesTouchingAtAnEdgeDoNotOverlap()
        {
            var a = new DepthBox(0, 10, 0, 10, 0, 5);
            Assert.That(a.Overlaps(new DepthBox(10, 20, 0, 10, 0, 5)), Is.False, "横の端が接するだけ");
            Assert.That(a.Overlaps(new DepthBox(9, 20, 0, 10, 0, 5)), Is.True);
            Assert.That(a.Overlaps(new DepthBox(0, 10, 10, 20, 0, 5)), Is.False, "縦の端が接するだけ");
            Assert.That(a.Overlaps(new DepthBox(0, 10, 0, 10, 10, 5)), Is.False, "奥行きの差が半幅の和ちょうど");
            Assert.That(a.Overlaps(new DepthBox(0, 10, 0, 10, 9, 5)), Is.True);
            Assert.That(a.Overlaps(new DepthBox(0, 10, 0, 10, -10, 5)), Is.False, "手前側も同じ");
            Assert.That(a.Overlaps(new DepthBox(0, 10, 0, 10, -9, 5)), Is.True);
        }

        [Test]
        public void PlacedBoxMirrorsWithFacing()
        {
            var shape = new BoxShape(4, 2, 10, 6, 3);
            var right = DepthBox.Place(100, 0, 50, 1, shape);
            var left = DepthBox.Place(100, 0, 50, -1, shape);
            Assert.That((right.MinX, right.MaxX), Is.EqualTo((104, 114)));
            Assert.That((left.MinX, left.MaxX), Is.EqualTo((86, 96)));
            Assert.That((right.MinY, right.MaxY, right.Z, right.Depth), Is.EqualTo((2, 8, 50, 3)));
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void JabMissesAnEnemyOnADifferentDepth(int side)
        {
            var table = Lab.Table();
            var jab = table[ActorKind.Player].Find("jab1");
            int limit = jab.Box.Depth + table[ActorKind.Grunt].Body.Depth;

            var exact = Lab.Arena();
            var far = Lab.Foe(exact, ActorKind.Grunt, 16, side * limit);
            Lab.Tap(exact, Lab.Attack);
            Lab.Idle(exact, 10);
            Assert.That(far.Health, Is.EqualTo(far.Def!.Health), "奥行きの差が半幅の和ちょうどなら当たらない");
            Assert.That(exact.HitCount, Is.Zero);

            var inside = Lab.Arena();
            var near = Lab.Foe(inside, ActorKind.Grunt, 16, side * (limit - 1));
            Lab.Tap(inside, Lab.Attack);
            Lab.Idle(inside, 10);
            Assert.That(near.Health, Is.EqualTo(near.Def!.Health - jab.Damage), "1 単位内側なら当たる");
        }

        [Test]
        public void JumpKickKeepsTheTakeoffDepth()
        {
            var table = Lab.Table();
            var kick = table[ActorKind.Player].Find("jumpkick");
            int limit = kick.Box.Depth + table[ActorKind.Grunt].Body.Depth;
            foreach (var (dz, hits) in new[] { (limit, false), (limit - 1, true) })
            {
                var brawl = Lab.Arena();
                var foe = Lab.Foe(brawl, ActorKind.Grunt, 40, dz);
                int z = brawl.Player.Z;
                brawl.Step(new Pad(1, 0, false, true, false));
                Lab.Run(brawl, new Pad(1, 0, false, false, false), 6);
                brawl.Step(new Pad(1, 0, true, false, false));
                bool hit = Lab.Until(brawl, b => b.HitCount > 0 || b.Player.State == ActorState.Land, 120, new Pad(1, 1, false, false, false));
                Assert.That(brawl.Player.Z, Is.EqualTo(z), "空中では奥行きが変わらない");
                Assert.That(brawl.HitCount > 0, Is.EqualTo(hits), $"dz={dz}");
                Assert.That(hit, Is.True);
                Assert.That(foe.Active, Is.True);
            }
        }

        [Test]
        public void SpecialHitsBothSidesOnlyInsideItsDepth()
        {
            var table = Lab.Table();
            var special = table[ActorKind.Player].Find("special");
            int limit = special.Box.Depth + table[ActorKind.Grunt].Body.Depth;
            var brawl = Lab.Arena();
            var front = Lab.Foe(brawl, ActorKind.Grunt, 20, limit - 1);
            var back = Lab.Foe(brawl, ActorKind.Grunt, -20, -(limit - 1));
            var outside = Lab.Foe(brawl, ActorKind.Grunt, 0, limit + 2);
            Lab.Tap(brawl, Lab.Special);
            Lab.Idle(brawl, 20);
            Assert.That(front.State, Is.EqualTo(ActorState.Knockdown));
            Assert.That(back.State, Is.EqualTo(ActorState.Knockdown));
            Assert.That(outside.Health, Is.EqualTo(outside.Def!.Health));
            Assert.That(brawl.HitCount, Is.EqualTo(2), "当たった 2 体に 1 回ずつ");
        }

        [Test]
        public void PushOnlyActsInsideThePushDepth()
        {
            var table = Lab.Table();
            int limit = table[ActorKind.Player].PushDepth + table[ActorKind.Grunt].PushDepth;
            var apart = Lab.Arena();
            var a = Lab.Foe(apart, ActorKind.Grunt, 0, limit);
            int x = a.X;
            apart.Step(Pad.Neutral);
            Assert.That(a.X, Is.EqualTo(x), "奥行きが離れていれば重なっても押さない");

            var close = Lab.Arena();
            var b = Lab.Foe(close, ActorKind.Grunt, 0, limit - 1);
            close.Step(Pad.Neutral);
            int reach = (table[ActorKind.Player].PushWidth + table[ActorKind.Grunt].PushWidth) / 2;
            Assert.That(System.Math.Abs(b.X - close.Player.X), Is.GreaterThanOrEqualTo(reach));
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        public void GrabNeedsTheEnemyWithinGrabDepth(int extra, bool grabs)
        {
            var config = new BrawlConfig();
            var brawl = Lab.Arena(config);
            Lab.Foe(brawl, ActorKind.Grunt, 22, config.GrabDepth + extra);
            Lab.Run(brawl, new Pad(1, 0, false, false, false), 10);
            Assert.That(brawl.Player.State == ActorState.Grab, Is.EqualTo(grabs));
        }

        [Test]
        public void DrawOrderIsBackToFrontThenBySerial()
        {
            var brawl = Lab.Arena();
            var p = brawl.Player;
            var back = Lab.Foe(brawl, ActorKind.Grunt, 60, 4000);
            var same = Lab.Foe(brawl, ActorKind.Grunt, 90, 0);
            var front = Lab.Foe(brawl, ActorKind.Grunt, 120, -4000);
            var order = new int[brawl.SlotCount];
            int count = Depth.DrawOrder(brawl, order);
            Assert.That(count, Is.EqualTo(4));
            Assert.That(new[] { order[0], order[1], order[2], order[3] }, Is.EqualTo(new[] { back.Slot, p.Slot, same.Slot, front.Slot }), "同じ z は出現の早い順");

            brawl.Step(Lab.Jump);
            Lab.Idle(brawl, 8);
            Assert.That(p.Y, Is.GreaterThan(0));
            Depth.DrawOrder(brawl, order);
            Assert.That(order[1], Is.EqualTo(p.Slot), "跳んでも順は変わらない");
        }

        [Test]
        public void DrawOrderSwapsWhenTheWalkerCrossesDepth()
        {
            var brawl = Lab.Arena();
            var p = brawl.Player;
            var foe = Lab.Foe(brawl, ActorKind.Grunt, 80, 600);
            var order = new int[brawl.SlotCount];
            Depth.DrawOrder(brawl, order);
            Assert.That(order[0], Is.EqualTo(foe.Slot));
            int ticks = 0;
            while (p.Z <= foe.Z && ticks++ < 10)
            {
                brawl.Step(new Pad(0, 1, false, false, false));
                Depth.DrawOrder(brawl, order);
                Assert.That(order[0], Is.EqualTo(p.Z > foe.Z ? p.Slot : foe.Slot), $"z={p.Z}");
            }

            Assert.That(p.Z, Is.GreaterThan(foe.Z));
        }

        [Test]
        public void ProjectionRaisesWithDepthAndHeight()
        {
            var config = new BrawlConfig();
            Assert.That(Depth.ScreenY(0, 0, config), Is.Zero);
            Assert.That(Depth.ScreenY(0, 1000, config), Is.EqualTo(1000 * config.DepthNum / config.DepthDen));
            Assert.That(Depth.ScreenY(300, 1000, config), Is.EqualTo(Depth.ScreenY(0, 1000, config) + 300));
            Assert.That(Depth.ShadowY(1000, config), Is.EqualTo(Depth.ScreenY(0, 1000, config)));
        }
    }
}
