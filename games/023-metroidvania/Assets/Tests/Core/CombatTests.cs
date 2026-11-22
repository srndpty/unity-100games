using System.Linq;
using NUnit.Framework;
using TileMotion;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>自機の弾・歩く敵・ひびの壁・ボス。</summary>
    public sealed class CombatTests
    {
        /// <summary>幅 20 の部屋。自機は列 2、ひびの壁は列 10（行 1〜2）、その奥に回復の実。</summary>
        private static World CrackedRoom() => Worlds(Map(0, 0,
            "####################",
            "#..................#",
            "#..................#",
            "#.........%........#",
            "#.S.......%...+....#",
            "####################"));

        /// <summary>幅 20 の部屋。自機は列 2、歩く敵は列 12。</summary>
        private static World WalkerRoom() => Worlds(Map(0, 0,
            "####################",
            "#..................#",
            "#..................#",
            "#..................#",
            "#.S.........w......#",
            "####################"));

        /// <summary>幅 30・高さ 8 のボスの場。自機は列 2、ボスは列 22。</summary>
        private static World Arena() => Worlds(Map(0, 0,
            "##############################",
            "#............................#",
            "#............................#",
            "#............................#",
            "#............................#",
            "#............................#",
            "#.S...................M......#",
            "##############################"));

        /// <summary>ショットのキーを押し直しながら進める（押したままでは連射しない）。</summary>
        private static ExpeditionEvents Fire(Expedition expedition, int horizontal, int ticks)
        {
            var events = ExpeditionEvents.None;
            for (var i = 0; i < ticks; i++)
            {
                expedition.Tick(In(horizontal, shoot: i % 2 == 0));
                events |= expedition.TakeEvents();
            }

            return events;
        }

        [Test]
        public void Shot_FiresOnPress_CapsOnScreen_AndExpires()
        {
            var config = Config();
            var e = At(CrackedRoom(), 0, new Cell(2, 1), config: config);
            e.Tick(Idle);
            e.Tick(Shoot);
            Assert.That(e.Run.Shots, Has.Count.EqualTo(1));
            for (var i = 0; i < 40; i++) { e.Tick(Shoot); }
            Assert.That(e.Run.Shots, Has.Count.EqualTo(0), "押したままでは撃たない（最初の弾は壁で消えた）");

            // 左向きに撃つと、壁（列 0）まで遠くないので射程より先に消える。上限は同時 2 発。
            e.Tick(Left);
            var most = 0;
            for (var i = 0; i < 60; i++)
            {
                e.Tick(In(1, shoot: i % 2 == 0));
                most = System.Math.Max(most, e.Run.Shots.Count);
            }

            Assert.That(most, Is.EqualTo(config.MaxShots));
        }

        [Test]
        public void Shot_RangeLimitsTravel()
        {
            var config = Config();
            config.ShotRangeTicks = 3;
            var e = At(WalkerRoom(), 0, new Cell(2, 1), config: config);
            e.Tick(Idle);
            e.Tick(Shoot);
            Assert.That(e.Run.Shots, Has.Count.EqualTo(1));
            e.Tick(Idle);
            e.Tick(Idle);
            Assert.That(e.Run.Shots, Has.Count.EqualTo(0), "3 Tick で消える");
            Assert.That(e.Run.Walkers[0].Health, Is.EqualTo(config.WalkerHealth), "射程の外の敵には当たらない");
        }

        [Test]
        public void CrackedWall_StaysWithoutBreakShot()
        {
            var e = At(CrackedRoom(), 0, new Cell(2, 1));
            var events = Fire(e, 0, 200);
            Assert.That(events & ExpeditionEvents.WallBroken, Is.EqualTo(ExpeditionEvents.None));
            Assert.That(e.Run.Terrain.IsSolid(10, 1), Is.True);
            Assert.That(e.State.BrokenCount, Is.Zero);

            // 歩いて押しても通れない。
            Hold(e, Right, 200);
            Assert.That(e.Player.Body.Right, Is.EqualTo(10 * T));
            Assert.That(e.State.IsTaken(0), Is.False);
        }

        [Test]
        public void CrackedWall_BreakShotBreaksOnlyTheTouchedTile_AndOpensThePath()
        {
            var e = At(CrackedRoom(), 0, new Cell(2, 1), Ability.BreakShot);
            e.Tick(Idle);
            e.Tick(Shoot);
            var events = Hold(e, Idle, 30);
            Assert.That(events & ExpeditionEvents.WallBroken, Is.EqualTo(ExpeditionEvents.WallBroken));
            Assert.That(e.Run.Terrain.IsSolid(10, 1), Is.False, "弾の高さの 1 タイルが壊れる");
            Assert.That(e.Run.Terrain.IsSolid(10, 2), Is.True, "上のタイルは残る");
            Assert.That(e.State.IsBroken(0, 10, 1, e.Run.Room.Map), Is.True, "壊れたことは世界の状態に残る");
            Hold(e, Right, 120);
            Assert.That(e.State.IsTaken(0), Is.True, "奥の実を取れる");
        }

        [Test]
        public void BreakShot_IsDecidedWhenFired()
        {
            // 能力のない弾は、飛んでいる間に能力を取っても壁を壊さない（撃った時点で決まる）。
            var shot = new Shot(new Box(0, 0, Shot.Width, Shot.Height), 1, 10, breaking: false);
            Assert.That(shot.Breaking, Is.False);
        }

        [Test]
        public void Walker_TakesTwoHits_ThenIsDefeated()
        {
            var config = Config();
            var e = At(WalkerRoom(), 0, new Cell(2, 1), config: config);
            e.Tick(Idle);
            var events = ExpeditionEvents.None;
            for (var i = 0; i < 80 && e.Run.Walkers[0].IsAlive; i++)
            {
                e.Tick(In(0, shoot: i % 12 == 0));
                events |= e.TakeEvents();
            }

            Assert.That(e.Run.Walkers[0].IsAlive, Is.False);
            Assert.That(events & (ExpeditionEvents.EnemyHit | ExpeditionEvents.EnemyDefeated), Is.EqualTo(ExpeditionEvents.EnemyHit | ExpeditionEvents.EnemyDefeated));
            Assert.That(e.Player.Health, Is.EqualTo(config.MaxHealth), "近づく前に倒した");
        }

        [Test]
        public void Walker_ContactHurtsAndPushesAway()
        {
            var config = Config();
            var e = At(WalkerRoom(), 0, new Cell(2, 1), config: config);
            var events = ExpeditionEvents.None;
            for (var i = 0; i < 300 && (events & ExpeditionEvents.Hurt) == 0; i++)
            {
                e.Tick(Right);
                events |= e.TakeEvents();
            }

            Assert.That(events & ExpeditionEvents.Hurt, Is.EqualTo(ExpeditionEvents.Hurt));
            Assert.That(e.Player.Health, Is.EqualTo(config.MaxHealth - config.ContactDamage));
            Assert.That(e.Player.VelocityX, Is.LessThan(0), "敵と反対（左）へ押し戻す");
            Assert.That(e.Player.InvincibleRemaining, Is.GreaterThan(0));
        }

        [Test]
        public void Walker_TurnsAtWallsAndLedges()
        {
            var config = Config();
            // 宙に浮いた幅 3 の足場（行 1 の列 2〜4）。
            var terrain = new Terrain(Map(0, 0, "#######", "#.....#", "#.....#", "#.###.#", "#######").Map, new bool[7 * 5]);
            var walker = new Walker(Room.PlaceOnTile(new Cell(3, 2), Walker.Width, Walker.Height), 1);
            var facings = new System.Collections.Generic.HashSet<int>();
            for (var i = 0; i < 300; i++)
            {
                walker.Step(terrain, config);
                facings.Add(walker.Facing);
                Assert.That(walker.Body.Bottom, Is.EqualTo(2 * T), "段から落ちない");
            }

            Assert.That(facings, Is.EquivalentTo(new[] { -1, 1 }));
        }

        [Test]
        public void Boss_CyclesIdleChargeStunFire()
        {
            var config = Config();
            var room = Arena().Rooms[0];
            var terrain = new Terrain(room.Map, new bool[room.Map.Width * room.Map.Height]);
            var boss = new Boss(Room.PlaceOnTile(room.Boss!.Value, Boss.Width, Boss.Height), config.BossHealth, config.BossIdleTicks);
            var bullets = new System.Collections.Generic.List<Bullet>();
            var left = new Box(3 * T, T, Player.Width, Player.Height);
            Assert.That(boss.Phase, Is.EqualTo(BossPhase.Idle));
            for (var i = 0; i < config.BossIdleTicks - 1; i++) { boss.Step(terrain, config, left, bullets); }
            Assert.That(boss.Phase, Is.EqualTo(BossPhase.Idle), "構えは BossIdleTicks Tick");
            boss.Step(terrain, config, left, bullets);
            Assert.That(boss.Phase, Is.EqualTo(BossPhase.Charge));
            Assert.That(boss.Facing, Is.EqualTo(-1), "自機のほう（左）へ突進する");

            var ticks = 0;
            while (boss.Phase == BossPhase.Charge && ticks++ < 300) { boss.Step(terrain, config, left, bullets); }
            Assert.That(boss.Phase, Is.EqualTo(BossPhase.Stunned));
            Assert.That(boss.Body.Left, Is.EqualTo(T), "左の壁で止まる");

            // 気絶が終わる時点で自機が右にいれば、右へ撃つ。
            var right = new Box(20 * T, T, Player.Width, Player.Height);
            for (var i = 0; i < config.BossStunTicks; i++) { boss.Step(terrain, config, right, bullets); }
            Assert.That(boss.Phase, Is.EqualTo(BossPhase.Idle));
            Assert.That(boss.PhaseLeft, Is.EqualTo(config.BossIdleTicks));
            Assert.That(bullets, Has.Count.EqualTo(3), "気絶の後に 3 方向の弾");
            Assert.That(bullets.All(b => b.VelocityX > 0), Is.True);
            Assert.That(bullets.Select(b => b.VelocityY).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void Boss_DefeatedByShots_WinsAndStopsTheGame()
        {
            var config = Config();
            config.BossHealth = 3;
            config.BossIdleTicks = 200;
            var e = At(Arena(), 0, new Cell(14, 1), config: config);
            e.Tick(Idle);
            var events = ExpeditionEvents.None;
            for (var i = 0; i < 150 && e.Status == ExpeditionStatus.Playing; i++)
            {
                e.Tick(In(0, shoot: i % 12 == 0));
                events |= e.TakeEvents();
            }

            Assert.That(e.Status, Is.EqualTo(ExpeditionStatus.Won));
            Assert.That(events & (ExpeditionEvents.BossHit | ExpeditionEvents.Won), Is.EqualTo(ExpeditionEvents.BossHit | ExpeditionEvents.Won));
            var hash = e.Hash();
            Assert.That(e.Tick(In(1, true, true)), Is.False, "勝った後の Tick は受け付けない");
            Assert.That(e.Continue(), Is.False);
            Assert.That(e.Hash(), Is.EqualTo(hash), "状態は変わらない");
        }

        [Test]
        public void BossBullet_HurtsOnce_AndDisappears()
        {
            var config = Config();
            config.BossIdleTicks = 1;
            config.BossStunTicks = 1;
            var e = At(Arena(), 0, new Cell(2, 1), config: config);
            var hurt = 0;
            for (var i = 0; i < 400 && hurt == 0; i++)
            {
                e.Tick(Idle);
                if ((e.TakeEvents() & ExpeditionEvents.Hurt) != 0) { hurt++; }
            }

            Assert.That(hurt, Is.EqualTo(1));
            Assert.That(e.Player.Health, Is.LessThan(config.MaxHealth));
        }

        [Test]
        public void Sweep_FindsCrossingsThatEndPositionsMiss()
        {
            // 弾が右へ、敵が左へ動いてすれ違う。終わりの位置では重ならないが、途中で触れている。
            var shotStart = new Box(0, 0, 4, 4);
            var shotEnd = new Box(100, 0, 4, 4);
            var enemyStart = new Box(60, 0, 10, 10);
            var enemyEnd = new Box(20, 0, 10, 10);
            Assert.That(shotEnd.Overlaps(enemyEnd), Is.False);
            var touch = Sweep.First(shotStart, shotEnd, enemyStart, enemyEnd);
            Assert.That(touch.Hit, Is.True);
            Assert.That(touch.Numerator * 140, Is.EqualTo(56 * touch.Denominator), "相対速度 140 で距離 56 を詰めた時刻");
            Assert.That(Sweep.First(shotStart, shotEnd, new Box(60, 5, 10, 10), new Box(20, 5, 10, 10)).Hit, Is.False, "縦にずれていれば触れない");
            Assert.That(Sweep.First(new Box(0, 0, 4, 4), new Box(0, 0, 4, 4), new Box(4, 0, 4, 4), new Box(4, 0, 4, 4)).Hit, Is.False, "辺が接するだけ");
            Assert.That(Sweep.First(new Box(0, 0, 4, 4), new Box(0, 0, 4, 4), new Box(2, 0, 4, 4), new Box(2, 0, 4, 4)).Numerator, Is.Zero, "始めから重なっていれば時刻 0");
        }
    }
}
