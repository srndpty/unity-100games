using System;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>同じ Tick に複数の弾が敵へ届くときは、弾の登録順ではなく触れた時刻の順に解決する（レビュー P2-2）。</summary>
    public sealed class BulletOrderTests
    {
        private const int Y = 13 * T;

        /// <summary>1 Tick ごとに撃て、弾速 100、自機・弾・Bouncer の半径 1 の設定。</summary>
        private static SiegeRun Run1() => Run(Quiet(c =>
        {
            c.FireIntervalTicks = 1;
            c.BulletLifeTicks = 30;
            c.BulletSpeed = 100;
            c.ShipRadius = 1;
            c.BulletRadius = 1;
            c.Bouncer.Radius = 1;
        }));

        /// <summary>
        /// Tick 1：x=6144 から右へ古い弾 A（6144→6244）。Tick 2：x=<paramref name="rightX"/> から左へ新しい弾 B を撃つ（A は 6244→6344）。
        /// Tick 3 は撃たない（A は右へ、B は左へさらに 100 進む）。
        /// </summary>
        private static (Bullet A, Bullet B) FireFromBothSides(SiegeRun run, int rightX, Action? beforeThirdTick = null)
        {
            run.PlaceShip(At(6144, Y));
            run.Step(Move(0, 0, At(9000, Y), fire: true));
            var a = run.Bullets.Single();
            run.PlaceShip(At(rightX, Y));
            run.Step(Move(0, 0, At(1000, Y), fire: true));
            var b = run.Bullets.Single(bullet => bullet != a);
            Assert.That(a.Position, Is.EqualTo(At(6344, Y)));
            beforeThirdTick?.Invoke();
            run.Step(Move(0, 0, At(1000, Y)));
            return (a, b);
        }

        [Test]
        public void NewerBulletThatTouchesFirst_TakesTheEnemy()
        {
            var run = Run1();
            var enemy = run.Place(EnemyKind.Bouncer, At(6390, Y));

            // Tick 3：A は 6344→6444 で時刻 0.44 に、B は 6400→6300 で時刻 0.08 に触れる。
            var (a, b) = FireFromBothSides(run, 6500);

            Assert.That(run.Enemies, Has.No.Member(enemy));
            Assert.That(run.Bullets, Has.No.Member(b), "先に触れた新しい弾 B が当たって消える");
            Assert.That(run.Bullets, Has.Member(a), "古い弾 A は倒れた敵を素通りして残る");
            Assert.That(a.Position, Is.EqualTo(At(6444, Y)));
        }

        [Test]
        public void SameTime_OlderBulletWins()
        {
            var run = Run1();
            var enemy = run.Place(EnemyKind.Bouncer, At(6394, Y));

            // Tick 3：A は 6344→6444、B は 6444→6344。どちらも時刻 0.48 に触れる。
            var (a, b) = FireFromBothSides(run, 6544);

            Assert.That(run.Enemies, Has.No.Member(enemy));
            Assert.That(run.Bullets, Has.No.Member(a), "同じ時刻なら古い弾が当たる");
            Assert.That(run.Bullets, Has.Member(b));
        }

        [Test]
        public void BulletWhoseTargetFell_HitsTheNextEnemyOnItsPath()
        {
            var run = Run1();
            var first = run.Place(EnemyKind.Bouncer, At(6390, Y));
            Enemy? behind = null;

            // A の通り道で倒れた敵の奥（6430、時刻 0.84）に、Tick 3 の前に敵を置く。B はそこから遠ざかるので触れない。
            var (a, b) = FireFromBothSides(run, 6500, () => behind = run.Place(EnemyKind.Bouncer, At(6430, Y)));

            Assert.That(run.Enemies, Has.No.Member(first));
            Assert.That(run.Enemies, Has.No.Member(behind), "A は次の候補を探し直して奥の敵に当たる");
            Assert.That(run.Bullets, Has.No.Member(a));
            Assert.That(run.Bullets, Has.No.Member(b));
            Assert.That(run.Kills, Is.EqualTo(2));
        }

        [Test]
        public void EnemyThatSurvives_TakesEveryBulletThatTouchesItInTheTick()
        {
            var run = Run1();
            var lancer = run.Place(EnemyKind.Lancer, At(6390, Y));

            // Lancer は半径 120。Tick 2 に A（6244→6344、時刻 0.25）と B（6600→6500、時刻 0.89）の両方が触れる。
            run.PlaceShip(At(6144, Y));
            run.Step(Move(0, 0, At(9000, Y), fire: true));
            run.PlaceShip(At(6600, Y));
            run.Step(Move(0, 0, At(1000, Y), fire: true));

            Assert.That(run.Bullets, Is.Empty, "どちらの弾も倒れていない Lancer に当たって消える");
            Assert.That(lancer.Health, Is.EqualTo(1));
            Assert.That(run.Enemies, Has.Member(lancer));
        }

        /// <summary>検証を通る設定では、自機をどこへ置いても・動かしてもアリーナの中に収まる（レビュー P2-1）。</summary>
        [Test]
        public void ShipStaysInsideArena_ForRandomValidConfigs()
        {
            var random = new SeededRandom(5);
            var checkedConfigs = 0;
            for (var trial = 0; trial < 500; trial++)
            {
                var config = Quiet(c =>
                {
                    c.ArenaWidthCells = random.Range(1, 6);
                    c.ArenaHeightCells = random.Range(1, 6);
                    c.ViewWidthCells = 1;
                    c.ViewHeightCells = 1;
                    c.ShipRadius = random.Range(1, SiegeConfig.MaxRadius);
                    c.ShipSpeed = random.Range(1, SiegeConfig.MaxSpeed);
                    c.SafeRadius = 0;
                    c.Chaser.Radius = c.Bouncer.Radius = c.Lancer.Radius = 1;
                });
                try { config.Validate(); }
                catch (ArgumentOutOfRangeException) { continue; }

                checkedConfigs++;
                var run = Run(config);
                foreach (var point in new[] { At(-10000, -10000), At(100000, 100000), At(0, 100000) })
                {
                    run.PlaceShip(point);
                    AssertInside(run, config);
                }

                for (var i = 0; i < 20; i++)
                {
                    run.Step(Move((i % 3) - 1, (i / 3 % 3) - 1, run.Ship.Position + At(T, 0)));
                    AssertInside(run, config);
                }
            }

            Assert.That(checkedConfigs, Is.GreaterThan(50), "検証を通る設定を十分に試した");
        }

        [Test]
        public void ShipRadiusLargerThanHalfArena_IsRejected()
        {
            // レビューの例：1×1 セル（256 単位）のアリーナに半径 512 の自機。
            var config = Quiet(c =>
            {
                c.ArenaWidthCells = 1;
                c.ArenaHeightCells = 1;
                c.ViewWidthCells = 1;
                c.ViewHeightCells = 1;
                c.ShipRadius = 512;
                c.SafeRadius = 0;
                c.Chaser.Radius = c.Bouncer.Radius = c.Lancer.Radius = 1;
            });
            Assert.That(() => config.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
            config.ShipRadius = 129;
            Assert.That(() => config.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
            config.ShipRadius = 128;
            Assert.That(() => config.Validate(), Throws.Nothing, "直径がアリーナの短い辺ちょうどまでは受け付ける");

            var wide = Quiet(c =>
            {
                c.ArenaHeightCells = 18;
                c.ShipRadius = (18 * T / 2) + 1;
            });
            Assert.That(() => wide.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>(), "短いほうの辺（高さ）で決まる");
        }

        private static void AssertInside(SiegeRun run, SiegeConfig config)
        {
            var p = run.Ship.Position;
            var r = config.ShipRadius;
            Assert.That(p.X - r, Is.GreaterThanOrEqualTo(0));
            Assert.That(p.Y - r, Is.GreaterThanOrEqualTo(0));
            Assert.That(p.X + r, Is.LessThanOrEqualTo(config.ArenaWidth));
            Assert.That(p.Y + r, Is.LessThanOrEqualTo(config.ArenaHeight));
        }
    }
}
