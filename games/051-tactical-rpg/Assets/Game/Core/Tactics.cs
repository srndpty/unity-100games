using System;

namespace Game.Core
{
    public readonly struct Order
    {
        public Order(int cell, int target) => (Cell, Target) = (cell, target);
        public int Cell { get; }
        public int Target { get; }
    }

    public enum AttackPriority { Damage, Defeat, Objective }

    public static class Tactics
    {
        public static Order Choose(Battle battle, int id, AttackPriority priority = AttackPriority.Objective)
        {
            if (!battle.CanAct(id, battle.Turn)) { return new Order(-1, -1); }
            var unit = battle.Units[id];
            var routes = battle.Movement(id, battle.Turn);
            var best = new Order(unit.Cell, -1);
            var bestScore = (-1, -1, -1, int.MinValue);
            var bestApproach = (int.MaxValue, int.MaxValue);
            Routes? approach = null;
            for (var cell = 0; cell < Battle.CellCount; cell++)
            {
                if (cell != unit.Cell && (routes == null || !routes.Reaches(cell))) { continue; }
                var threat = 0;
                for (var other = 0; other < battle.Units.Count; other++)
                {
                    if (IsOpponent(battle, other) && Threatens(battle.Units[other], cell)) { threat += 100; }
                }

                for (var other = 0; other < battle.Units.Count; other++)
                {
                    var opponent = battle.Units[other];
                    if (!IsOpponent(battle, other) || Battle.Distance(cell, opponent.Cell) > unit.Range) { continue; }
                    var defeat = unit.Power >= opponent.Hp ? unit.Accuracy : 0;
                    var victory = priority == AttackPriority.Objective && other == Battle.Commander ? defeat : 0;
                    var remainingThreat = threat - (Threatens(opponent, cell) ? defeat : 0);
                    var score = (victory, priority == AttackPriority.Damage ? 0 : defeat,
                        Math.Min(unit.Power, opponent.Hp) * unit.Accuracy, -remainingThreat);
                    if (score.CompareTo(bestScore) <= 0) { continue; }
                    bestScore = score;
                    best = new Order(cell, other);
                }

                if (best.Target >= 0) { continue; }
                approach ??= Approach(battle, id);
                var position = (approach.Cost(cell), threat);
                if (position.CompareTo(bestApproach) >= 0) { continue; }
                bestApproach = position;
                best = new Order(cell, -1);
            }

            return best;
        }

        public static (Order Order, Strike Strike, int Damage) Act(Battle battle, int id)
        {
            var order = Choose(battle, id);
            if (order.Cell < 0) { return (order, Strike.Rejected, 0); }
            if (battle.Units[id].Cell != order.Cell) { battle.Move(id, order.Cell, battle.Turn); }
            if (order.Target < 0) { return (order, Strike.Rejected, 0); }
            var target = battle.Units[order.Target];
            var before = target.Hp;
            var strike = battle.Attack(id, order.Target, battle.Turn);
            return (order, strike, before - target.Hp);
        }

        private static bool Threatens(Unit unit, int cell) => Battle.Distance(cell, unit.Cell) <= unit.Range;

        private static bool IsOpponent(Battle battle, int id) => Battle.Team(id) != battle.Turn && battle.Units[id].Hp > 0;

        public static Routes Approach(Battle battle, int id)
        {
            return new Routes(battle, id, cell =>
            {
                for (var other = 0; other < battle.Units.Count; other++)
                {
                    if (IsOpponent(battle, other) &&
                        Battle.Distance(cell, battle.Units[other].Cell) <= battle.Units[id].Range) { return true; }
                }

                return false;
            }, int.MaxValue, reverse: true);
        }
    }
}
