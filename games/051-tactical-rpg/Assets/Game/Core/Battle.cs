using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum Side { Ally, Enemy }
    public enum Ground { Plain, Woods, Rock }
    public enum Result { Playing, Won, Lost }
    public enum Strike { Rejected, Miss, Hit }

    [Serializable]
    public sealed class Stats
    {
        public int Hp;
        public int Move;
        public int Power;
        public int Accuracy;
        public int Range;

        public Stats(int hp, int move, int power, int accuracy, int range) =>
            (Hp, Move, Power, Accuracy, Range) = (hp, move, power, accuracy, range);
    }

    public sealed class Unit
    {
        internal Unit(int cell, Stats stats)
        {
            if (stats.Hp is < 1 or > 99 || stats.Move is < 1 or > 12 || stats.Power is < 1 or > 99 ||
                stats.Accuracy is < 1 or > 100 || stats.Range is < 1 or > 2)
            {
                throw new ArgumentException("ユニット設定が範囲外です。", nameof(stats));
            }

            Cell = cell;
            (Hp, Move, Power, Accuracy, Range) = (stats.Hp, stats.Move, stats.Power, stats.Accuracy, stats.Range);
        }

        public int Cell { get; internal set; }
        public int Hp { get; internal set; }
        public int Move { get; }
        public int Power { get; }
        public int Accuracy { get; }
        public int Range { get; }
        public bool Moved { get; internal set; }
        public bool Done { get; internal set; }
    }

    public sealed class Battle
    {
        public const int Width = 8;
        public const int CellCount = 64;
        public const int AllyCount = 4;
        public const int Commander = 4;

        private static readonly int[] _positions = { 49, 50, 57, 58, 14, 13, 22, 20, 29 };
        private readonly Unit[] _units;
        private uint _random;

        public Battle(uint seed, IReadOnlyList<Stats> stats)
        {
            if (stats.Count != _positions.Length) { throw new ArgumentException("9体分の設定が必要です。", nameof(stats)); }
            _random = seed == 0 ? 1U : seed;
            _units = new Unit[_positions.Length];
            for (var i = 0; i < _units.Length; i++) { _units[i] = new Unit(_positions[i], stats[i]); }
            Units = Array.AsReadOnly(_units);
        }

        public IReadOnlyList<Unit> Units { get; }
        public Side Turn { get; private set; }
        public Result Result { get; private set; }
        public int Round { get; private set; } = 1;

        public static Stats[] Defaults() => new Stats[]
        {
            new(12, 4, 5, 90, 1), new(15, 3, 4, 90, 1), new(10, 5, 4, 95, 1), new(9, 4, 4, 85, 2),
            new(13, 2, 4, 85, 1), new(8, 3, 3, 80, 1), new(8, 3, 3, 80, 1),
            new(7, 3, 3, 80, 2), new(8, 3, 3, 80, 1),
        };

        public static Side Team(int id) => id < AllyCount ? Side.Ally : Side.Enemy;
        public static bool ValidCell(int cell) => cell is >= 0 and < CellCount;
        public static int Distance(int a, int b) => Math.Abs(a % Width - b % Width) + Math.Abs(a / Width - b / Width);

        public static Ground Terrain(int cell) => cell switch
        {
            26 or 27 or 28 => Ground.Rock,
            25 or 30 or 36 or 37 => Ground.Woods,
            _ => Ground.Plain,
        };

        public int Occupant(int cell)
        {
            for (var id = 0; id < _units.Length; id++)
            {
                if (_units[id].Hp > 0 && _units[id].Cell == cell) { return id; }
            }

            return -1;
        }

        public bool CanAct(int id, Side actor) => Result == Result.Playing && actor == Turn &&
            id >= 0 && id < _units.Length && Team(id) == actor && _units[id].Hp > 0 && !_units[id].Done;

        public Routes? Movement(int id, Side actor)
        {
            if (!CanAct(id, actor) || _units[id].Moved) { return null; }
            return new Routes(this, id, _units[id].Cell, _units[id].Move);
        }

        public bool Move(int id, int cell, Side actor)
        {
            var routes = Movement(id, actor);
            if (routes == null || !routes.Reaches(cell) || cell == _units[id].Cell) { return false; }
            _units[id].Cell = cell;
            _units[id].Moved = true;
            return true;
        }

        public bool CanAttack(int id, int target, Side actor) => CanAct(id, actor) &&
            target >= 0 && target < _units.Length && Team(id) != Team(target) &&
            _units[target].Hp > 0 && Distance(_units[id].Cell, _units[target].Cell) <= _units[id].Range;

        public Strike Attack(int id, int target, Side actor)
        {
            if (!CanAttack(id, target, actor)) { return Strike.Rejected; }
            var attacker = _units[id];
            attacker.Done = true;
            // xorshift32。上位24ビットを0〜99に写し、再現性をUnityと.NETで揃える。
            _random ^= _random << 13;
            _random ^= _random >> 17;
            _random ^= _random << 5;
            if (((_random >> 8) * 100U >> 24) >= attacker.Accuracy) { return Strike.Miss; }
            var defender = _units[target];
            defender.Hp = Math.Max(0, defender.Hp - attacker.Power);
            if (_units[Commander].Hp == 0) { Result = Result.Won; }
            else if (!AlliesAlive()) { Result = Result.Lost; }
            return Strike.Hit;
        }

        public bool EndTurn(Side actor)
        {
            if (Result != Result.Playing || Turn != actor) { return false; }
            Turn = Turn == Side.Ally ? Side.Enemy : Side.Ally;
            if (Turn == Side.Ally) { Round++; }
            for (var i = 0; i < _units.Length; i++)
            {
                if (Team(i) != Turn) { continue; }
                _units[i].Moved = false;
                _units[i].Done = false;
            }

            return true;
        }

        private bool AlliesAlive()
        {
            for (var i = 0; i < AllyCount; i++)
            {
                if (_units[i].Hp > 0) { return true; }
            }

            return false;
        }
    }
}
