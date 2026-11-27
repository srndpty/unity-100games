using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class BattleTests
    {
        [Test]
        public void MovementMatchesIndependentRepeatedRelaxation()
        {
            var state = new Battle(1, Battle.Defaults());
            for (var id = 0; id < Battle.AllyCount; id++)
            {
                var cost = new int[Battle.CellCount];
                Array.Fill(cost, 1000);
                cost[state.Units[id].Cell] = 0;
                for (var sweep = 0; sweep < Battle.CellCount; sweep++)
                {
                    for (var cell = 0; cell < cost.Length; cell++)
                    {
                        if (state.Occupant(cell) >= 0 || Battle.Terrain(cell) == Ground.Rock) { continue; }
                        for (var from = 0; from < cost.Length; from++)
                        {
                            var x = Math.Abs(cell % 8 - from % 8);
                            var y = Math.Abs(cell / 8 - from / 8);
                            if (x + y == 1)
                            {
                                cost[cell] = Math.Min(cost[cell], cost[from] + (Battle.Terrain(cell) == Ground.Woods ? 2 : 1));
                            }
                        }
                    }
                }

                var actual = state.Movement(id, Side.Ally)!;
                for (var cell = 0; cell < cost.Length; cell++)
                {
                    var expected = cost[cell] <= state.Units[id].Move ? cost[cell] : int.MaxValue;
                    Assert.That(actual.Cost(cell), Is.EqualTo(expected), $"ユニット{id}・セル{cell}");
                }
            }
        }

        [Test]
        public void RejectsWrongTurnBlockedCellsAndRepeatedMoves()
        {
            var state = new Battle(1, Battle.Defaults());
            Assert.That(state.Move(4, 6, Side.Enemy), Is.False);
            Assert.That(state.Attack(0, 4, Side.Ally), Is.EqualTo(Strike.Rejected));
            Assert.That(state.Move(0, 26, Side.Ally), Is.False);
            Assert.That(state.Move(0, 50, Side.Ally), Is.False);
            Assert.That(state.Move(0, -1, Side.Ally), Is.False);
            Assert.That(state.Movement(0, Side.Ally)!.Cost(32), Is.EqualTo(3));
            Assert.That(state.Move(0, 32, Side.Ally), Is.True);
            Assert.That(state.Move(0, 33, Side.Ally), Is.False);
            Assert.That(state.EndTurn(Side.Ally), Is.True);
            Assert.That(state.EndTurn(Side.Ally), Is.False);
            Assert.That(state.Move(1, 42, Side.Ally), Is.False);
        }

        [Test]
        public void EnemyCanReachAnAttackAndChoosesDeterministically()
        {
            var state = new Battle(1, Battle.Defaults());
            Assert.That(state.Move(0, 24, Side.Ally), Is.True);
            Assert.That(state.EndTurn(Side.Ally), Is.True);
            var order = Tactics.Choose(state, 7);
            var again = Tactics.Choose(state, 7);
            Assert.That(order.Cell, Is.EqualTo(17));
            Assert.That(order.Target, Is.Zero);
            Assert.That(order.Cell, Is.EqualTo(again.Cell));
            Assert.That(order.Target, Is.EqualTo(again.Target));
            Assert.That(state.Movement(7, Side.Enemy)!.Reaches(order.Cell), Is.True);
        }

        [TestCase(1, 100, Strike.Hit)]
        [TestCase(1, 1, Strike.Hit)]
        [TestCase(100000, 1, Strike.Miss)]
        public void AttackingAfterMovingConsumesTheAction(int seed, int accuracy, Strike expected)
        {
            var stats = Battle.Defaults();
            stats[0].Accuracy = accuracy;
            stats[0].Power = 99;
            var state = new Battle((uint)seed, stats);
            state.Move(0, 24, Side.Ally);
            state.EndTurn(Side.Ally);
            Assert.That(state.Move(7, 17, Side.Enemy), Is.True);
            state.EndTurn(Side.Enemy);
            Assert.That(state.Attack(0, 7, Side.Ally), Is.EqualTo(Strike.Rejected));
            Assert.That(state.Attack(0, 1, Side.Ally), Is.EqualTo(Strike.Rejected));
            Assert.That(state.Move(0, 16, Side.Ally), Is.True);
            Assert.That(state.Attack(0, 7, Side.Ally), Is.EqualTo(expected));
            Assert.That(state.Units[7].Hp, Is.EqualTo(expected == Strike.Hit ? 0 : 7));
            Assert.That(state.Units[0].Done, Is.True);
            Assert.That(state.Attack(0, 7, Side.Ally), Is.EqualTo(Strike.Rejected));
            Assert.That(state.Move(0, 8, Side.Ally), Is.False);
            state.EndTurn(Side.Ally);
            state.EndTurn(Side.Enemy);
            Assert.That(state.Units[0].Done || state.Units[0].Moved, Is.False);
            if (expected == Strike.Hit)
            {
                Assert.That(state.Attack(0, 7, Side.Ally), Is.EqualTo(Strike.Rejected));
                Assert.That(state.Movement(0, Side.Ally)!.Cost(17), Is.EqualTo(1));
            }
        }

        [Test]
        public void ReverseCostsMatchRepeatedRelaxationAfterMovement()
        {
            var state = new Battle(1, Battle.Defaults());
            state.Move(0, 24, Side.Ally);
            state.EndTurn(Side.Ally);
            state.Move(7, 17, Side.Enemy);
            const int moving = 7;
            var cost = new int[Battle.CellCount];
            for (var cell = 0; cell < cost.Length; cell++)
            {
                cost[cell] = 1000;
                if (Blocked(cell)) { continue; }
                for (var ally = 0; ally < Battle.AllyCount; ally++)
                {
                    if (Battle.Distance(cell, state.Units[ally].Cell) <= state.Units[moving].Range) { cost[cell] = 0; }
                }
            }

            for (var sweep = 0; sweep < cost.Length; sweep++)
            {
                for (var from = 0; from < cost.Length; from++)
                {
                    if (Blocked(from)) { continue; }
                    for (var to = 0; to < cost.Length; to++)
                    {
                        if (Blocked(to) || Battle.Distance(from, to) != 1) { continue; }
                        cost[from] = Math.Min(cost[from], cost[to] + (Battle.Terrain(to) == Ground.Woods ? 2 : 1));
                    }
                }
            }

            var actual = Tactics.Approach(state, moving);
            for (var cell = 0; cell < cost.Length; cell++)
            {
                Assert.That(actual.Cost(cell), Is.EqualTo(cost[cell] == 1000 ? int.MaxValue : cost[cell]), $"セル{cell}");
            }

            bool Blocked(int cell) => Battle.Terrain(cell) == Ground.Rock ||
                (state.Occupant(cell) >= 0 && state.Occupant(cell) != moving);
        }


        [Test]
        public void CommanderDefeatEndsBattleImmediately()
        {
            var stats = Battle.Defaults();
            stats[0].Move = 12;
            stats[0].Power = 99;
            stats[0].Accuracy = 100;
            var state = new Battle(1, stats);
            Assert.That(state.Move(0, 6, Side.Ally), Is.True);
            Assert.That(state.Attack(0, Battle.Commander, Side.Ally), Is.EqualTo(Strike.Hit));
            Assert.That(state.Result, Is.EqualTo(Result.Won));
            Assert.That(state.EndTurn(Side.Ally), Is.False);
            Assert.That(state.Move(1, 42, Side.Ally), Is.False);
            Assert.That(state.Attack(0, 5, Side.Ally), Is.EqualTo(Strike.Rejected));
        }

    }
}
