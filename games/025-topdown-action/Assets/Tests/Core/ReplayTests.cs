using System.Collections.Generic;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>再現性：同じシードと入力の列なら同じ状態。部屋の乱数は入室ごとの系列で、他の部屋の使い方に左右されない。</summary>
    public sealed class ReplayTests
    {
        private static List<HeroInput> RandomInputs(int seed, int count)
        {
            var random = new SeededRandom(seed);
            var inputs = new List<HeroInput>();
            for (var i = 0; i < count; i++)
            {
                inputs.Add(new HeroInput(random.Range(-1, 1), random.Range(-1, 1), random.Next(4) == 0, random.Next(30) == 0));
            }

            return inputs;
        }

        private static uint Play(int seed, IReadOnlyList<HeroInput> inputs, int startRoom = Rooms.Entrance)
        {
            var world = Real();
            var dungeon = At(world, startRoom, world.Rooms[startRoom].Start ?? new Cell(10, 5), seed: seed);
            foreach (var input in inputs)
            {
                if (dungeon.Status == DungeonStatus.Down) { dungeon.Continue(); }
                dungeon.Tick(input);
            }

            return dungeon.Hash();
        }

        [Test]
        public void SameSeedAndInputs_GiveTheSameHash()
        {
            var inputs = RandomInputs(42, 3000);
            Assert.That(Play(5, inputs, Rooms.West), Is.EqualTo(Play(5, inputs, Rooms.West)));
            Assert.That(Play(5, inputs), Is.EqualTo(Play(5, inputs)));
        }

        [Test]
        public void DifferentSeeds_MoveTheSlimesDifferently()
        {
            var inputs = new HeroInput[200];
            var world = Real();
            var a = At(world, Rooms.West, new Cell(10, 5), seed: 1);
            var b = At(world, Rooms.West, new Cell(10, 5), seed: 2);
            foreach (var input in inputs)
            {
                a.Tick(input);
                b.Tick(input);
            }

            var same = true;
            for (var i = 0; i < a.Run.Enemies.Count; i++) { same &= a.Run.Enemies[i].Body == b.Run.Enemies[i].Body; }
            Assert.That(same, Is.False);
        }

        [Test]
        public void RoomSeed_DependsOnSeedRoomAndEntry()
        {
            var seeds = new HashSet<int>();
            for (var room = 0; room < 6; room++)
            {
                for (var entry = 1; entry <= 5; entry++) { seeds.Add(Dungeon.RoomSeed(9, room, entry)); }
            }

            Assert.That(seeds.Count, Is.EqualTo(30));
            Assert.That(Dungeon.RoomSeed(9, 2, 3), Is.EqualTo(Dungeon.RoomSeed(9, 2, 3)));
            Assert.That(Dungeon.RoomSeed(9, 2, 3), Is.Not.EqualTo(Dungeon.RoomSeed(10, 2, 3)));
        }

        [Test]
        public void SlimesInTheNextRoom_DoNotDependOnTimeSpentInThePreviousRoom()
        {
            var world = Worlds(
                Map(0, 0, "#######", "#..s..D", "#.S...D", "#.s...D", "#######"),
                Map(1, 0, "#########", "D....s..#", "D.......#", "D.....s.#", "#########"));
            var config = new VaultConfig { ContactDamage = 1 };
            var quick = At(world, 0, new Cell(2, 2), config, seed: 3);
            var slow = At(world, 0, new Cell(2, 2), config, seed: 3);
            Hold(slow, Idle, 150);
            Assert.That(Until(quick, Right, d => d.RoomIndex == 1, 800), Is.True);
            Assert.That(Until(slow, Right, d => d.RoomIndex == 1, 800), Is.True);
            Assert.That(quick.RoomEntries, Is.EqualTo(slow.RoomEntries));
            Hold(quick, Idle, config.TransitionTicks + 120);
            Hold(slow, Idle, config.TransitionTicks + 120);
            for (var i = 0; i < quick.Run.Enemies.Count; i++)
            {
                Assert.That(slow.Run.Enemies[i].Body, Is.EqualTo(quick.Run.Enemies[i].Body), $"スライム {i}");
            }
        }

        [Test]
        public void Clone_ProgressesIdentically_AndIndependently()
        {
            var inputs = RandomInputs(7, 600);
            var world = Real();
            var dungeon = At(world, Rooms.East, new Cell(5, 5), seed: 11);
            for (var i = 0; i < 300; i++) { dungeon.Tick(inputs[i]); }
            var clone = dungeon.Clone();
            Assert.That(clone.Hash(), Is.EqualTo(dungeon.Hash()));
            for (var i = 300; i < 600; i++)
            {
                dungeon.Tick(inputs[i]);
                clone.Tick(inputs[i]);
            }

            Assert.That(clone.Hash(), Is.EqualTo(dungeon.Hash()));
            if (dungeon.Status == DungeonStatus.Down)
            {
                dungeon.Continue();
                clone.Continue();
            }

            Assert.That(clone.Tick(new HeroInput(1, 1, true, false)), Is.True);
            Assert.That(clone.Hash(), Is.Not.EqualTo(dungeon.Hash()));
        }
    }
}
