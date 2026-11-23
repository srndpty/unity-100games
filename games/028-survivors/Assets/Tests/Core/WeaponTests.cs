using System;
using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 題材固有：強化後の武器値反映。武器 1 つだけの設定でレベルアップの 3 択から強化し、次の発動のダメージ・数・範囲・間隔が
    /// 定義の表（<see cref="WeaponDef.Levels"/>）の値どおりかを、止まった敵（速度 0 の Swarm）の体力の減りで確かめる。
    /// </summary>
    public sealed class WeaponTests
    {
        private static readonly int[] _levels = { 1, 2, 3, 4, 5 };

        /// <summary>武器 <paramref name="weapon"/> だけを持つ設定。止まった的として Gnat を丈夫にする。</summary>
        private static TideConfig Only(int weapon)
        {
            var config = Quiet();
            config.Weapons = new[] { config.Weapons[weapon] };
            config.StartingWeapon = 0;
            Sturdy(config, Gnat);
            return config;
        }

        /// <summary>レベルアップの 3 択の「強化」を選んで、武器 0 をレベル <paramref name="level"/> まで上げる。</summary>
        private static void UpgradeTo(TideRun run, int level)
        {
            while (run.WeaponLevelOf(0) < level)
            {
                run.PlaceGem(run.Player, run.ExperienceToNext);
                run.Step(PlayerCommand.Idle);
                Assert.That(run.IsChoosing, Is.True);
                var picked = -1;
                for (var i = 0; i < run.ChoiceCount; i++)
                {
                    if (run.ChoiceAt(i).Kind == ChoiceKind.Upgrade) { picked = i; }
                }

                Assert.That(picked, Is.GreaterThanOrEqualTo(0), "最大でない武器は強化の候補に出る");
                var before = run.WeaponLevelOf(0);
                run.Choose(picked);
                Assert.That(run.WeaponLevelOf(0), Is.EqualTo(before + 1));
            }
        }

        /// <summary><paramref name="flag"/> が起きる Tick まで進める（起きなければ失敗）。</summary>
        private static void StepUntil(TideRun run, RunEvents flag, int limit = 400)
        {
            run.TakeEvents();
            for (var i = 0; i < limit; i++)
            {
                run.Step(PlayerCommand.Idle);
                if ((run.TakeEvents() & flag) != 0) { return; }
            }

            Assert.Fail($"{flag} が {limit} Tick の間に起きませんでした。");
        }

        private static int Target(TideRun run, Vec2i at) => run.Enemies.Id(run.Place(Gnat, at));

        [Test]
        public void UpgradedStats_EqualTheTable_ForEveryWeaponAndLevel([Values(Bolt, Orbit, Pulse, Lash, Arc)] int weapon)
        {
            var config = Only(weapon);
            var run = new TideRun(config, 1);
            foreach (var level in _levels)
            {
                UpgradeTo(run, level);
                var expected = config.Weapons[0].At(level);
                var actual = run.WeaponStatsOf(0)!;
                Assert.That(
                    new[] { actual.Damage, actual.IntervalTicks, actual.Count, actual.Range, actual.Size, actual.Speed, actual.Pierce, actual.LifeTicks },
                    Is.EqualTo(new[] { expected.Damage, expected.IntervalTicks, expected.Count, expected.Range, expected.Size, expected.Speed, expected.Pierce, expected.LifeTicks }));
                Assert.That(run.WeaponCooldownOf(0), Is.LessThanOrEqualTo(expected.IntervalTicks), "強化の後は新しい間隔より長く待たない");
            }
        }

        [Test]
        public void Bolt_FiresCountShots_EachDealingTheLevelDamage_AtTheLevelInterval([ValueSource(nameof(_levels))] int level)
        {
            var config = Only(Bolt);
            var run = new TideRun(config, 1);
            UpgradeTo(run, level);
            var stats = config.Weapons[0].At(level);
            var id = Target(run, run.Player + At(4 * T, 0));
            var start = HealthOf(run, id);

            StepUntil(run, RunEvents.BoltFired);
            Assert.That(run.Projectiles.Count, Is.EqualTo(stats.Count));
            var firedAt = run.Elapsed;
            Wait(run, Math.Min(stats.LifeTicks, stats.IntervalTicks - 1));
            Assert.That(start - HealthOf(run, id), Is.EqualTo(stats.Count * stats.Damage), "敵が 1 体なら全弾が同じ敵へ、1 発 1 回");

            // 次の発動はちょうど間隔の Tick 後（間隔より寿命が長ければ前の弾を待たない）。
            var next = new TideRun(config, 1);
            UpgradeTo(next, level);
            Target(next, next.Player + At(4 * T, 0));
            StepUntil(next, RunEvents.BoltFired);
            var first = next.Elapsed;
            StepUntil(next, RunEvents.BoltFired);
            Assert.That(next.Elapsed - first, Is.EqualTo(stats.IntervalTicks));
            Assert.That(firedAt, Is.GreaterThan(0));
        }

        [Test]
        public void Bolt_PierceLimitsHowManyEnemiesInALineAreHit([ValueSource(nameof(_levels))] int level)
        {
            var config = Only(Bolt);
            var run = new TideRun(config, 1);
            UpgradeTo(run, level);
            var stats = config.Weapons[0].At(level);
            var ids = new int[5];
            for (var k = 0; k < ids.Length; k++) { ids[k] = Target(run, run.Player + At((3 + k) * T / 2 * 2, 0)); }

            StepUntil(run, RunEvents.BoltFired);
            Wait(run, stats.LifeTicks);
            var hit = 0;
            foreach (var id in ids)
            {
                var lost = config.Enemies[Gnat].Health - HealthOf(run, id);
                if (lost > 0) { hit++; }
            }

            // Count 発は近い順の敵（ここでは並びの手前から）を狙い、各弾は Pierce 体まで貫く。全弾が一直線に飛ぶので、手前から Pierce 体が当たる。
            Assert.That(hit, Is.EqualTo(Math.Min(ids.Length, stats.Pierce)));
        }

        [Test]
        public void Pulse_HitsEveryEnemyWithinTheLevelRange([ValueSource(nameof(_levels))] int level)
        {
            var config = Only(Pulse);
            var run = new TideRun(config, 1);
            UpgradeTo(run, level);
            var stats = config.Weapons[0].At(level);
            var r = config.Enemies[Gnat].Radius;
            var inside = Target(run, run.Player + At(stats.Range + r - 1, 0));
            var outside = Target(run, run.Player + At(-(stats.Range + r), 0));
            var diagonal = Target(run, run.Player + At(0, stats.Range + r - 1));

            StepUntil(run, RunEvents.Pulsed);
            Assert.That(config.Enemies[Gnat].Health - HealthOf(run, inside), Is.EqualTo(stats.Damage));
            Assert.That(config.Enemies[Gnat].Health - HealthOf(run, diagonal), Is.EqualTo(stats.Damage));
            Assert.That(HealthOf(run, outside), Is.EqualTo(config.Enemies[Gnat].Health), "ちょうど接するだけでは当たらない");
            var first = run.Elapsed;
            StepUntil(run, RunEvents.Pulsed);
            Assert.That(run.Elapsed - first, Is.EqualTo(stats.IntervalTicks));
        }

        [Test]
        public void Lash_HitsTheFacingSide_AndBothSidesWithCountTwo([ValueSource(nameof(_levels))] int level)
        {
            var config = Only(Lash);
            var run = new TideRun(config, 1);
            UpgradeTo(run, level);
            var stats = config.Weapons[0].At(level);
            var r = config.Enemies[Gnat].Radius;
            var front = Target(run, run.Player + At(stats.Range + r - 1, 0));
            var beyond = Target(run, run.Player + At(stats.Range + r, 0));
            var above = Target(run, run.Player + At(stats.Range / 2, stats.Size + r));
            var behind = Target(run, run.Player + At(-stats.Range / 2, 0));

            StepUntil(run, RunEvents.Lashed);
            var full = config.Enemies[Gnat].Health;
            Assert.That(full - HealthOf(run, front), Is.EqualTo(stats.Damage));
            Assert.That(HealthOf(run, beyond), Is.EqualTo(full));
            Assert.That(HealthOf(run, above), Is.EqualTo(full));
            Assert.That(full - HealthOf(run, behind), Is.EqualTo(stats.Count == 2 ? stats.Damage : 0));
            Assert.That(run.Effects.LashCount, Is.EqualTo(stats.Count));
        }

        [Test]
        public void Arc_ChainsToCountEnemiesWithinTheStepDistance([ValueSource(nameof(_levels))] int level)
        {
            var config = Only(Arc);
            var run = new TideRun(config, 1);
            UpgradeTo(run, level);
            var stats = config.Weapons[0].At(level);
            var ids = new int[TideConfig.MaxCount];
            for (var k = 0; k < ids.Length; k++) { ids[k] = Target(run, run.Player + At(T + (k * (stats.Size - 1)), 0)); }
            var far = Target(run, run.Player + At(T + ((ids.Length - 1) * (stats.Size - 1)) + stats.Size, 0));

            StepUntil(run, RunEvents.Arced);
            var full = config.Enemies[Gnat].Health;
            for (var k = 0; k < ids.Length; k++)
            {
                Assert.That(full - HealthOf(run, ids[k]), Is.EqualTo(k < stats.Count ? stats.Damage : 0), $"{k} 体目");
            }

            Assert.That(HealthOf(run, far), Is.EqualTo(full));
            Assert.That(run.Effects.ArcCount, Is.EqualTo(stats.Count + 1));
        }

        [Test]
        public void Orbit_BladesCircleAtTheLevelRange_AndRehitWaitsTheInterval([ValueSource(nameof(_levels))] int level)
        {
            var config = Only(Orbit);
            var run = new TideRun(config, 1);
            UpgradeTo(run, level);
            var stats = config.Weapons[0].At(level);
            for (var b = 0; b < stats.Count; b++)
            {
                var distance = Math.Sqrt((run.OrbitBlade(0, b) - run.Player).LengthSquared);
                Assert.That(distance, Is.EqualTo(stats.Range).Within(2.0));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => run.OrbitBlade(0, stats.Count));
            var id = Target(run, run.Player + At(0, stats.Range));
            var full = config.Enemies[Gnat].Health;
            var hits = new System.Collections.Generic.List<int>();
            var last = full;
            for (var t = 0; t < 600; t++)
            {
                run.Step(PlayerCommand.Idle);
                var now = HealthOf(run, id);
                if (now == last) { continue; }
                Assert.That(last - now, Is.EqualTo(stats.Damage));
                hits.Add(run.Elapsed);
                last = now;
            }

            Assert.That(hits.Count, Is.GreaterThan(2));
            for (var k = 1; k < hits.Count; k++) { Assert.That(hits[k] - hits[k - 1], Is.GreaterThanOrEqualTo(stats.IntervalTicks)); }
        }

        [Test]
        public void MaxLevelWeapons_AreNotOffered_AndAllMaxedOffersOnlyHeal()
        {
            var config = Quiet();
            var run = new TideRun(config, 3);
            run.SetWeaponLevel(Bolt, config.Weapons[Bolt].MaxLevel);
            for (var round = 0; round < 10; round++)
            {
                run.PlaceGem(run.Player, run.ExperienceToNext);
                run.Step(PlayerCommand.Idle);
                for (var i = 0; i < run.ChoiceCount; i++) { Assert.That(run.ChoiceAt(i).Weapon, Is.Not.EqualTo(Bolt)); }
                Assert.That(run.ChoiceCount, Is.EqualTo(TideRun.ChoiceSlots));
                run.Choose(0);
            }

            for (var w = 0; w < config.Weapons.Length; w++) { run.SetWeaponLevel(w, config.Weapons[w].MaxLevel); }
            run.PlaceGem(run.Player, run.ExperienceToNext);
            run.Step(PlayerCommand.Idle);
            Assert.That(run.ChoiceCount, Is.EqualTo(1));
            Assert.That(run.ChoiceAt(0).Kind, Is.EqualTo(ChoiceKind.Heal));
        }

        [Test]
        public void Choices_AreDistinct_AndDependOnTheSeed()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (var seed = 1; seed <= 20; seed++)
            {
                var run = new TideRun(Quiet(), seed);
                run.PlaceGem(run.Player, run.ExperienceToNext);
                run.Step(PlayerCommand.Idle);
                var text = "";
                for (var i = 0; i < run.ChoiceCount; i++)
                {
                    for (var j = 0; j < i; j++) { Assert.That(run.ChoiceAt(i), Is.Not.EqualTo(run.ChoiceAt(j))); }
                    text += run.ChoiceAt(i) + ";";
                }

                seen.Add(text);
            }

            Assert.That(seen.Count, Is.GreaterThan(3), "シードで候補が変わる");
        }

        [Test]
        public void Heal_RestoresUpToMaximum()
        {
            var config = Quiet();
            config.Weapons = new[] { config.Weapons[Bolt] };
            var run = new TideRun(config, 1);
            run.SetWeaponLevel(0, config.Weapons[0].MaxLevel);
            config.Enemies[Gnat].Health = 10000;
            run = new TideRun(config, 1);
            run.SetWeaponLevel(0, config.Weapons[0].MaxLevel);
            run.Place(Gnat, run.Player + At(100, 0));
            Wait(run, 1);
            var hurt = run.Health;
            Assert.That(hurt, Is.LessThan(config.PlayerHealth));
            run.PlaceGem(run.Player, run.ExperienceToNext);
            run.Step(PlayerCommand.Idle);
            run.Choose(0);
            Assert.That(run.Health, Is.EqualTo(Math.Min(config.PlayerHealth, hurt + config.HealAmount)));
        }
    }
}
