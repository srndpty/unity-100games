using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>敵の動き。</summary>
    public sealed class EnemyTests
    {
        private readonly PlatformerConfig _config = new();

        private Enemy Spawn(EnemyKind kind, int column, int row)
        {
            var size = Enemy.SizeOf(kind);
            return new Enemy(_config, kind, Level.PlaceOnTile(column, row, size.Width, size.Height));
        }

        private static Box PlayerAt(int column) => Level.PlaceOnTile(column, 1, Player.Width, Player.Height);

        [Test]
        public void WaitsUntilPlayerIsWithinActivationDistance()
        {
            var map = Flat(60).Map;
            var enemy = Spawn(EnemyKind.Walker, 30, 1);
            var start = enemy.Body;
            enemy.Step(map, PlayerAt(30 - _config.ActivationTiles - 1), 1);
            Assert.That(enemy.State, Is.EqualTo(EnemyState.Waiting));
            Assert.That(enemy.Body, Is.EqualTo(start));

            enemy.Step(map, PlayerAt(30 - _config.ActivationTiles), 2);
            Assert.That(enemy.State, Is.EqualTo(EnemyState.Active));
            Assert.That(enemy.Body.X, Is.EqualTo(start.X - _config.WalkerSpeed));

            enemy.Step(map, PlayerAt(0), 3);
            Assert.That(enemy.State, Is.EqualTo(EnemyState.Active), "一度動き出したら止まらない");
        }

        [Test]
        public void Walker_TurnsAtWall()
        {
            var map = Map(
                "G.....",
                "#S.w..",
                "######").Map;
            var enemy = Spawn(EnemyKind.Walker, 3, 1);
            for (var i = 0; i < 100; i++)
            {
                enemy.Step(map, PlayerAt(3), i);
            }

            Assert.That(enemy.Facing, Is.EqualTo(1), "左の壁で向きを変えた");
            Assert.That(enemy.Body.Left, Is.GreaterThan(T));
        }

        [Test]
        public void Walker_WalksOffLedgeAndFallsIntoPit()
        {
            var map = Map(
                "G.....",
                "S..w..",
                "#.####").Map;
            var enemy = Spawn(EnemyKind.Walker, 3, 1);
            for (var i = 0; i < 300 && enemy.IsAlive; i++)
            {
                enemy.Step(map, PlayerAt(3), i);
            }

            Assert.That(enemy.State, Is.EqualTo(EnemyState.Fell));
        }

        [Test]
        public void Thorn_TurnsAtLedge()
        {
            var map = Map(
                "G.....",
                "S..t..",
                "#.###.").Map;
            var enemy = Spawn(EnemyKind.Thorn, 3, 1);
            var minLeft = int.MaxValue;
            var maxRight = int.MinValue;
            for (var i = 0; i < 600; i++)
            {
                enemy.Step(map, PlayerAt(3), i);
                minLeft = Math.Min(minLeft, enemy.Body.Left);
                maxRight = Math.Max(maxRight, enemy.Body.Right);
            }

            Assert.That(enemy.IsAlive, Is.True);
            // 次の 1 歩で前の端が足場の外に出るなら、その手前で引き返す。
            Assert.That(minLeft, Is.InRange(2 * T, (2 * T) + _config.ThornSpeed - 1), "左の崖の手前で引き返す");
            Assert.That(maxRight, Is.InRange((5 * T) - _config.ThornSpeed + 1, 5 * T), "右の崖の手前で引き返す");
        }

        [Test]
        public void Hopper_HopsAtIntervalInPlace()
        {
            var map = Flat(10).Map;
            var enemy = Spawn(EnemyKind.Hopper, 5, 1);
            var x = enemy.Body.X;
            var firstHop = -1;
            for (var i = 1; i <= _config.HopperInterval + 1 && firstHop < 0; i++)
            {
                enemy.Step(map, PlayerAt(5), i);
                if (enemy.Body.Bottom > T) { firstHop = i; }
            }

            Assert.That(firstHop, Is.EqualTo(_config.HopperInterval));
            Assert.That(enemy.Body.X, Is.EqualTo(x));
        }

        [Test]
        public void Stomp_EndsAndCannotRepeat()
        {
            var enemy = Spawn(EnemyKind.Walker, 5, 1);
            enemy.Stomp(7);
            Assert.That(enemy.State, Is.EqualTo(EnemyState.Stomped));
            Assert.That(enemy.EndedTick, Is.EqualTo(7));
            Assert.Throws<InvalidOperationException>(() => enemy.Stomp(8));
        }

        [Test]
        public void StompedEnemy_NoLongerMoves()
        {
            var map = Flat(10).Map;
            var enemy = Spawn(EnemyKind.Walker, 5, 1);
            enemy.Stomp(1);
            var body = enemy.Body;
            enemy.Step(map, PlayerAt(5), 2);
            Assert.That(enemy.Body, Is.EqualTo(body));
        }

        [Test]
        public void ThornIsNotStompable()
        {
            Assert.That(Spawn(EnemyKind.Thorn, 1, 1).Stompable, Is.False);
            Assert.That(Spawn(EnemyKind.Walker, 1, 1).Stompable, Is.True);
            Assert.That(Spawn(EnemyKind.Hopper, 1, 1).Stompable, Is.True);
        }

        [Test]
        public void RejectsUndefinedKind()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Enemy(_config, (EnemyKind)99, new Box(0, 0, 1, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Enemy.SizeOf((EnemyKind)99));
        }
    }
}
