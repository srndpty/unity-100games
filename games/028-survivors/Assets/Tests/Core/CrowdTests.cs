using System;
using System.Collections.Generic;
using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>題材固有：上限敵数で更新が安定。近傍探索・向きの決め直しの頻度・押し合い・出現・置き直し。</summary>
    public sealed class CrowdTests
    {
        /// <summary>上限いっぱいの敵を自機の周りに並べた試合（自機は倒れない・武器は全部最大）。</summary>
        private static TideRun Crowded(out TideConfig config, int seed = 1)
        {
            config = new TideConfig { PlayerHealth = TideConfig.MaxHealth };
            foreach (var stage in config.Stages)
            {
                stage.TargetAlive = config.MaxEnemies;
                stage.IntervalTicks = 1;
            }
            var run = new TideRun(config, seed);
            for (var w = 0; w < config.Weapons.Length; w++) { run.SetWeaponLevel(w, config.Weapons[w].MaxLevel); }
            var random = new SeededRandom(seed);
            while (run.NormalCount < config.MaxEnemies)
            {
                var angle = random.Next(Trig.FullTurn);
                var distance = random.Range(3 * T, 18 * T);
                var at = new Vec2i(Trig.Cos(angle) * distance / Trig.One, Trig.Sin(angle) * distance / Trig.One);
                run.Place(random.Range(Mite, Husk), at);
            }

            return run;
        }

        private static void ChooseAll(TideRun run)
        {
            while (run.IsChoosing) { run.Choose(0); }
        }

        [Test]
        public void AtTheEnemyCap_EveryTickStaysWithinBounds()
        {
            var run = Crowded(out var config);
            var random = new SeededRandom(2);
            var limit = (long)config.DespawnDistance * config.DespawnDistance;
            var peak = 0;
            for (var tick = 0; tick < 3000; tick++)
            {
                ChooseAll(run);
                var before = new Dictionary<int, Vec2i>();
                for (var i = 0; i < run.Enemies.Count; i++) { before[run.Enemies.Id(i)] = run.Enemies.Position(i); }
                run.Step(new PlayerCommand(random.Range(-1, 1), random.Range(-1, 1)));

                Assert.That(run.NormalCount, Is.LessThanOrEqualTo(config.MaxEnemies));
                Assert.That(run.Projectiles.Count, Is.LessThanOrEqualTo(config.MaxProjectiles));
                Assert.That(run.Gems.Count, Is.LessThanOrEqualTo(config.MaxGems));
                peak = Math.Max(peak, run.NormalCount);
                for (var i = 0; i < run.Enemies.Count; i++)
                {
                    var position = run.Enemies.Position(i);
                    Assert.That(Vec2i.DistanceSquared(position, run.Player), Is.LessThanOrEqualTo(limit), "置き直しで遠くに溜まらない");
                    if (!before.TryGetValue(run.Enemies.Id(i), out var previous)) { continue; }
                    var moved = Math.Sqrt(Vec2i.DistanceSquared(previous, position));
                    var allowed = config.Enemies[run.Enemies.Def(i)].Speed + config.SeparationPush + 2;
                    if (moved > allowed)
                    {
                        // 置き直した敵だけが大きく動く（出現の輪の上にいる）。
                        var fromPlayer = Math.Sqrt(Vec2i.DistanceSquared(position, run.Player));
                        Assert.That(fromPlayer, Is.InRange(config.SpawnInner - 2, config.SpawnOuter + 2), $"押し合いで発散しない（{moved:F0} 動いた）");
                    }
                }
            }

            Assert.That(peak, Is.EqualTo(config.MaxEnemies));
            Assert.That(run.Kills, Is.GreaterThan(500), "上限に張り付いても倒して湧く");
        }

        [Test]
        public void SteeringIsStaggered_EachChaserReSteersExactlyOncePerInterval()
        {
            var run = Crowded(out var config);
            var interval = config.SteerIntervalTicks;
            var updatesPerTick = new List<int>();
            for (var tick = 0; tick < interval * 20; tick++)
            {
                ChooseAll(run);
                run.Step(PlayerCommand.Idle);
                var updated = 0;
                for (var i = 0; i < run.Enemies.Count; i++)
                {
                    if (config.Enemies[run.Enemies.Def(i)].Behavior == EnemyBehavior.Swarm) { continue; }
                    var since = run.Elapsed - run.Enemies.LastSteerTick(i);
                    Assert.That(since, Is.InRange(0, interval - 1), "どの敵も間隔の中で必ず 1 回決め直す");
                    if (since == 0 && TideRun.IsSteerPhase(run.Enemies.Id(i), run.Elapsed, interval)) { updated++; }
                }

                updatesPerTick.Add(updated);
            }

            // 1 Tick に決め直すのは全体のおよそ 1/間隔（番号の位相でならす）。
            var chasers = 0;
            for (var i = 0; i < run.Enemies.Count; i++)
            {
                if (config.Enemies[run.Enemies.Def(i)].Behavior != EnemyBehavior.Swarm) { chasers++; }
            }

            Assert.That(updatesPerTick.FindAll(u => u > (chasers / interval * 2) + 8), Is.Empty);
        }

        [Test]
        public void SteerPhase_ComesOncePerInterval_ForAnyIdAndTick()
        {
            for (var id = 1; id < 50; id++)
            {
                for (var interval = 1; interval <= 8; interval++)
                {
                    for (var start = 0; start < 20; start++)
                    {
                        var count = 0;
                        for (var t = start; t < start + interval; t++)
                        {
                            if (TideRun.IsSteerPhase(id, t, interval)) { count++; }
                        }

                        Assert.That(count, Is.EqualTo(1));
                    }
                }
            }
        }

        [Test]
        public void StepDoesNotAllocate_AtTheEnemyCap()
        {
            var run = Crowded(out _);
            for (var i = 0; i < 200; i++)
            {
                ChooseAll(run);
                run.Step(new PlayerCommand(1, 0));
            }

            // Mono の GC.GetAllocatedBytesForCurrentThread は常に 0 を返すので、これが確かめられるのは .NET だけ（027 の知見）。
            long allocated = 0;
            for (var i = 0; i < 300; i++)
            {
                ChooseAll(run);
                var before = GC.GetAllocatedBytesForCurrentThread();
                run.Step(new PlayerCommand(i % 2 == 0 ? 1 : 0, 1));
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void Grid_CollectsEveryPointInsideTheRect_EvenPointsOutsideTheGrid()
        {
            var random = new SeededRandom(17);
            const int Count = 300;
            var xs = new int[Count];
            var ys = new int[Count];
            var grid = new SpatialGrid(10, 2 * T, Count);
            var buffer = new int[Count];
            for (var round = 0; round < 200; round++)
            {
                var center = At(random.Range(-50 * T, 50 * T), random.Range(-50 * T, 50 * T));
                for (var i = 0; i < Count; i++)
                {
                    // 4 割はグリッドの外（端のマスへ寄せられる）。
                    var spread = i % 5 < 2 ? 40 * T : 10 * T;
                    xs[i] = center.X + random.Range(-spread, spread);
                    ys[i] = center.Y + random.Range(-spread, spread);
                }

                grid.Build(center, Count, xs, ys);
                var minX = center.X + random.Range(-30 * T, 30 * T);
                var minY = center.Y + random.Range(-30 * T, 30 * T);
                var maxX = minX + random.Range(0, 20 * T);
                var maxY = minY + random.Range(0, 20 * T);
                var found = grid.Collect(minX, minY, maxX, maxY, buffer);
                var collected = new HashSet<int>();
                for (var k = 0; k < found; k++) { Assert.That(collected.Add(buffer[k]), Is.True, "同じ点を二度返さない"); }
                for (var i = 0; i < Count; i++)
                {
                    var inside = xs[i] >= minX && xs[i] <= maxX && ys[i] >= minY && ys[i] <= maxY;
                    if (inside) { Assert.That(collected.Contains(i), Is.True, $"点 {i} ({xs[i]}, {ys[i]})"); }
                }
            }
        }

        [Test]
        public void NearestTarget_MatchesBruteForce()
        {
            // Bolt Lv5 が狙う「射程の中の近い順の 3 体」を、全探索の近い順（同じ距離は番号の順）と照合する。
            for (var seed = 1; seed <= 30; seed++)
            {
                var config = Quiet();
                Sturdy(config, Gnat);
                config.Weapons = new[] { config.Weapons[Bolt] };
                var run = new TideRun(config, seed);
                run.SetWeaponLevel(0, 5);
                var stats = config.Weapons[0].At(5);
                var random = new SeededRandom(seed);
                for (var i = 0; i < 80; i++) { run.Place(Gnat, At(random.Range(-14 * T, 14 * T), random.Range(-14 * T, 14 * T))); }

                // 武器は押し合いの後の位置で狙う。的（速度 0 の Gnat）は弾の後には動かないので、Step の後の位置で照合する。
                run.Step(PlayerCommand.Idle);
                var order = new List<(long Distance, int Id, Vec2i Position)>();
                for (var i = 0; i < run.Enemies.Count; i++)
                {
                    var d = Vec2i.DistanceSquared(run.Enemies.Position(i), run.Player);
                    if (d < (long)stats.Range * stats.Range) { order.Add((d, run.Enemies.Id(i), run.Enemies.Position(i))); }
                }

                order.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Id.CompareTo(b.Id));
                Assert.That(run.Projectiles.Count, Is.EqualTo(order.Count == 0 ? 0 : stats.Count));
                for (var k = 0; k < run.Projectiles.Count; k++)
                {
                    var target = order[k % order.Count].Position;
                    var expected = IntMath.Scale(target - run.Player, stats.Speed);
                    Assert.That(run.Projectiles.Velocity(k), Is.EqualTo(expected), $"シード {seed} の {k} 発目");
                }
            }
        }

        [Test]
        public void Spawns_AppearOutsideTheView_AndNeverExceedTheStageTarget()
        {
            var config = new TideConfig { PlayerHealth = TideConfig.MaxHealth };
            var run = new TideRun(config, 4);
            run.SetWeaponLevel(Bolt, 0);
            var known = new HashSet<int>();
            for (var tick = 0; tick < 4000; tick++)
            {
                run.Step(new PlayerCommand(tick / 300 % 2 == 0 ? 1 : -1, 0));
                var stage = config.StageAt(run.Elapsed - 1);
                Assert.That(run.NormalCount, Is.LessThanOrEqualTo(Math.Min(stage.TargetAlive, config.MaxEnemies)));
                var view = run.View;
                for (var i = 0; i < run.Enemies.Count; i++)
                {
                    if (!known.Add(run.Enemies.Id(i))) { continue; }
                    var r = config.Enemies[run.Enemies.Def(i)].Radius;
                    Assert.That(view.Overlaps(run.Enemies.Position(i), r), Is.False, "出現した Tick に視野へ入っていない");
                }
            }

            Assert.That(run.Spawned, Is.GreaterThanOrEqualTo(60), "区間の目標まで出る");
        }

        [Test]
        public void SpawnBudget_IsCapped_SoAFreedSlotFillsOneAtATime()
        {
            var config = Quiet(c =>
            {
                foreach (var stage in c.Stages)
                {
                    stage.TargetAlive = 10;
                    stage.IntervalTicks = 5;
                }

                c.PlayerHealth = TideConfig.MaxHealth;
                c.Enemies[Mite].Speed = 60;
                c.Enemies[Mite].Health = 1;
            });
            var run = Run(config);
            Wait(run, 60);
            Assert.That(run.NormalCount, Is.EqualTo(10));

            // 上限に張り付いた間に予算が貯まり続けても、空いた後は 1 回分（間隔 1 つ）ずつしか出ない：
            // 空いた直後の 1 体を除き、出現と出現の間は間隔以上あく。
            run.SetWeaponLevel(Pulse, config.Weapons[Pulse].MaxLevel);
            var spawnTicks = new List<int>();
            for (var tick = 0; tick < 3000; tick++)
            {
                ChooseAll(run);
                var count = run.Spawned;
                run.Step(PlayerCommand.Idle);
                Assert.That(run.Spawned - count, Is.LessThanOrEqualTo(1));
                if (run.Spawned > count) { spawnTicks.Add(run.Elapsed); }
            }

            Assert.That(run.Kills, Is.GreaterThan(10));
            Assert.That(spawnTicks.Count, Is.GreaterThan(10));
            for (var k = 1; k < spawnTicks.Count; k++) { Assert.That(spawnTicks[k] - spawnTicks[k - 1], Is.GreaterThanOrEqualTo(5)); }
        }

        [Test]
        public void FarEnemies_AreRelocatedAhead_KeepingHealthAndCount()
        {
            var config = Quiet();
            Sturdy(config, Gnat);
            var run = Run(config);
            var index = run.Place(Gnat, At(-config.DespawnDistance - 10, 0));
            var id = run.Enemies.Id(index);
            run.Place(Mite, At(0, 5 * T));
            run.Step(new PlayerCommand(1, 0));

            Assert.That(run.Enemies.Count, Is.EqualTo(2));
            var moved = IndexOf(run, id);
            Assert.That(run.Enemies.Health(moved), Is.EqualTo(config.Enemies[Gnat].Health));
            var offset = run.Enemies.Position(moved) - run.Player;
            Assert.That(offset.X, Is.GreaterThan(0), "進む向き（右）の側へ置き直す");
            Assert.That(Math.Sqrt(offset.LengthSquared), Is.InRange(config.SpawnInner - 2, config.SpawnOuter + 2));
            Assert.That(run.Enemies.Velocity(moved).X, Is.LessThan(0), "Swarm は新しい位置から自機へ向け直す");
        }

        [Test]
        public void Separation_PushesOverlappingEnemiesApart_IndependentOfOrder()
        {
            var config = Quiet();
            var run = Run(config);
            run.Place(Mite, At(10 * T, 0));
            run.Place(Mite, At(10 * T, 0));
            run.Place(Mite, At(10 * T + 20, 0));
            var before = run.Hash();
            var clone = run.Clone();
            Wait(run, 30);
            Wait(clone, 30);
            Assert.That(run.Hash(), Is.EqualTo(clone.Hash()));
            Assert.That(before, Is.Not.EqualTo(run.Hash()));
            for (var i = 0; i < run.Enemies.Count; i++)
            {
                for (var j = i + 1; j < run.Enemies.Count; j++)
                {
                    Assert.That(run.Enemies.Position(i), Is.Not.EqualTo(run.Enemies.Position(j)), "同じ点に重なった敵も離れる");
                }
            }
        }
    }
}
