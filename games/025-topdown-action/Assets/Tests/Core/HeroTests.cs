using NUnit.Framework;
using TileMotion;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>自機の移動・向き・剣・ロールと、剣の当たりの箱。</summary>
    public sealed class HeroTests
    {
        private static Hero Fresh(VaultConfig? config = null, Terrain? terrain = null)
        {
            var hero = new Hero(config ?? Config(), new Cell(6, 4).Centered(Hero.Size));

            // 開始前から押していたことになっているキーを離す。
            hero.Step(terrain ?? Open(), Idle);
            hero.EndTick();
            return hero;
        }

        [Test]
        public void SwordHitbox_IsInFrontOfTheBody_ForEachFacing()
        {
            var config = Config();
            var body = new Box(1000, 2000, Hero.Size, Hero.Size);
            var reach = config.SwordReach;
            var width = config.SwordWidth;
            var cx = body.CenterX - (width / 2);
            var cy = Boxes.CenterY(body) - (width / 2);
            Assert.That(SwordHitbox.For(body, Facing.Right, config), Is.EqualTo(new Box(body.Right, cy, reach, width)));
            Assert.That(SwordHitbox.For(body, Facing.Left, config), Is.EqualTo(new Box(body.Left - reach, cy, reach, width)));
            Assert.That(SwordHitbox.For(body, Facing.Up, config), Is.EqualTo(new Box(cx, body.Top, width, reach)));
            Assert.That(SwordHitbox.For(body, Facing.Down, config), Is.EqualTo(new Box(cx, body.Bottom - reach, width, reach)));
        }

        [Test]
        public void SwordHitbox_ReachBoundary()
        {
            var config = Config();
            var body = new Box(1000, 2000, Hero.Size, Hero.Size);
            var sword = SwordHitbox.For(body, Facing.Right, config);
            var size = Enemy.SizeOf(EnemyKind.Slime);
            Assert.That(sword.Overlaps(new Box(body.Right + config.SwordReach, body.Y, size, size)), Is.False, "届く距離ちょうど先は当たらない（辺が接するだけ）");
            Assert.That(sword.Overlaps(new Box(body.Right + config.SwordReach - 1, body.Y, size, size)), Is.True, "1 単位内側なら当たる");
        }

        [Test]
        public void Sword_IsActiveOnlyBetweenActiveTicks()
        {
            var config = Config();
            var terrain = Open();
            var hero = Fresh(config, terrain);
            Assert.That(hero.Step(terrain, Attack) & HeroActions.AttackReady, Is.EqualTo(HeroActions.AttackReady));
            hero.StartSwing();
            for (var tick = 1; tick <= config.SwingTicks; tick++)
            {
                Assert.That(hero.SwingTick, Is.EqualTo(tick));
                var active = tick >= config.SwingActiveFrom && tick <= config.SwingActiveTo;
                Assert.That(hero.Sword.HasValue, Is.EqualTo(active), $"振りの {tick} Tick 目");
                hero.Step(terrain, Attack);
            }

            Assert.That(hero.IsSwinging, Is.False);
            Assert.That(hero.Sword.HasValue, Is.False);
        }

        [Test]
        public void HoldingAttack_DoesNotSwingAgain_UntilReleased()
        {
            var terrain = Open();
            var hero = Fresh(null, terrain);
            hero.Step(terrain, Attack);
            hero.StartSwing();
            for (var i = 0; i < 40; i++) { Assert.That(hero.Step(terrain, Attack) & HeroActions.AttackReady, Is.EqualTo(HeroActions.None)); }
            hero.Step(terrain, Idle);
            Assert.That(hero.Step(terrain, Attack) & HeroActions.AttackReady, Is.EqualTo(HeroActions.AttackReady));
        }

        [Test]
        public void NewHero_IgnoresKeysHeldBeforeItStarted()
        {
            var terrain = Open();
            var hero = new Hero(Config(), new Cell(6, 4).Centered(Hero.Size));
            Assert.That(hero.Step(terrain, In(attack: true, roll: true)), Is.EqualTo(HeroActions.None));
        }

        [Test]
        public void SwingSlowsWalking_AndLocksFacing()
        {
            var config = Config();
            var terrain = Open(30, 8);
            var hero = Fresh(config, terrain);
            hero.Step(terrain, Right);
            hero.Step(terrain, In(x: 1, attack: true));
            hero.StartSwing();
            var x = hero.Body.X;
            hero.Step(terrain, In(x: -1, attack: true));
            Assert.That(hero.Body.X, Is.EqualTo(x - config.SwingMoveSpeed));
            Assert.That(hero.Facing, Is.EqualTo(Facing.Right), "振っている間は向きを変えない");
        }

        [Test]
        public void Diagonal_UsesScaledSpeed_AndKeepsTheFacingAxisThatIsStillPressed()
        {
            var config = Config();
            var terrain = Open(30, 20);
            var hero = Fresh(config, terrain);
            hero.Step(terrain, Up);
            Assert.That(hero.Facing, Is.EqualTo(Facing.Up));
            var start = hero.Body;
            hero.Step(terrain, In(x: 1, y: 1));
            var diagonal = VaultConfig.DiagonalOf(config.HeroSpeed);
            Assert.That(hero.Body, Is.EqualTo(start.Offset(diagonal, diagonal)));
            Assert.That(hero.Facing, Is.EqualTo(Facing.Up), "上を押したままなら上を向いたまま");
            hero.Step(terrain, In(x: 1, y: -1));
            Assert.That(hero.Facing, Is.EqualTo(Facing.Right), "今の向きの成分を押していなければ横を優先");
        }

        [Test]
        public void DiagonalOf_NeverRoundsAPositiveSpeedToZero()
        {
            Assert.That(VaultConfig.DiagonalOf(1), Is.EqualTo(1));
            Assert.That(VaultConfig.DiagonalOf(0), Is.EqualTo(0));
            Assert.That(VaultConfig.DiagonalOf(256), Is.EqualTo(181));
        }

        [Test]
        public void Roll_UsesFacing_WhenNoDirectionIsPressed_AndCannotSwing()
        {
            var config = Config();
            var terrain = Open(30, 20);
            var hero = Fresh(config, terrain);
            var start = hero.Body;
            Assert.That(hero.Step(terrain, Roll) & HeroActions.Rolled, Is.EqualTo(HeroActions.Rolled));
            Assert.That(hero.Body, Is.EqualTo(start.Offset(0, config.RollSpeed)), "上を向いていれば上へ");
            Assert.That(hero.Step(terrain, In(attack: true, roll: true)) & HeroActions.AttackReady, Is.EqualTo(HeroActions.None), "転がっている間は振れない");
        }

        [Test]
        public void Roll_CooldownCountsFromTheTickItEnds()
        {
            var config = Config();
            var terrain = Open(60, 20);
            var lastBlocked = 1 + config.RollTicks + config.RollCooldownTicks - 1;

            Assert.That(RollsAt(lastBlocked), Is.False, "終わった Tick を含めて RollCooldownTicks Tick は転がれない");
            Assert.That(RollsAt(lastBlocked + 1), Is.True);

            bool RollsAt(int tick)
            {
                var hero = new Hero(config, new Cell(2, 10).Centered(Hero.Size));
                hero.Step(terrain, Idle);
                Assert.That(hero.Step(terrain, In(x: 1, roll: true)) & HeroActions.Rolled, Is.EqualTo(HeroActions.Rolled));
                for (var t = 2; t < tick; t++) { hero.Step(terrain, Idle); }
                return (hero.Step(terrain, In(roll: true)) & HeroActions.Rolled) != 0;
            }
        }

        [Test]
        public void Walls_StopTheHero()
        {
            var tiles = new TileKind[5 * 3];
            tiles[(1 * 5) + 3] = TileKind.Wall;
            var terrain = new Terrain(new TileMap(5, 3, tiles), new bool[4]);
            var hero = new Hero(Config(), new Cell(1, 1).Centered(Hero.Size));
            for (var i = 0; i < 30; i++) { hero.Step(terrain, Right); }
            Assert.That(hero.Body.Right, Is.EqualTo(3 * Units.Tile));
        }

        [Test]
        public void InputOutOfRange_IsRejected()
        {
            Assert.That(() => new HeroInput(2, 0, false, false), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => new HeroInput(0, -2, false, false), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
