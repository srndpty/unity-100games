using System;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    public enum PilotStyle
    {
        Cpu,
        Human,
    }

    /// <summary>
    /// 距離と相手の状態から、歩き・ガード・対空・弱の確定反撃・必殺・投げを選ぶ。
    /// 出すのは人と同じ方向とボタンで、反応の遅れと乱数は試合のシードから取る。
    /// </summary>
    public sealed class CpuPilot
    {
        public const int CpuReaction = 8;

        public const int HumanReaction = 16;

        private const int Approach = 0;
        private const int Light = 1;
        private const int Heavy = 2;
        private const int Guard = 3;
        private const int Retreat = 4;
        private const int Throw = 5;
        private const int Special = 6;

        private readonly int _index;
        private readonly PilotStyle _style;
        private readonly int _reaction;
        private readonly int _holdTicks;
        private SeededRandom _random;
        private int _wait;
        private int _hold;
        private int _intent = Approach;
        private int _motion = -1;
        private Pad _held = Pad.Neutral;

        public CpuPilot(int index, int seed, PilotStyle style = PilotStyle.Cpu)
        {
            if ((uint)index > 1) { throw new ArgumentOutOfRangeException(nameof(index)); }
            _index = index;
            _style = style;
            _reaction = style == PilotStyle.Human ? HumanReaction : CpuReaction;
            _holdTicks = style == PilotStyle.Human ? 6 : 0;
            _random = new SeededRandom(Mix(seed));
        }

        public PilotStyle Style => _style;

        public void Reset(int seed)
        {
            _random = new SeededRandom(Mix(seed));
            _wait = 0;
            _hold = 0;
            _intent = Approach;
            _motion = -1;
            _held = Pad.Neutral;
        }

        public Pad PadFor(Duel duel)
        {
            var self = duel.At(_index);
            var foe = duel.At(1 - _index);
            if (_wait <= 0)
            {
                _intent = Pick(self, foe);
                _wait = _reaction;
            }
            else
            {
                _wait--;
            }

            if (!Ready(self))
            {
                _motion = -1;
                return Pad.Neutral;
            }

            if (_motion >= 0) { return Motion(self); }
            if (_hold > 0)
            {
                _hold--;
                return _held;
            }

            _held = Emit(self);
            _hold = _holdTicks;
            return _held;
        }

        private int Pick(Fighter self, Fighter foe)
        {
            int gap = Math.Abs(self.X - foe.X);
            bool air = foe.Y > 0 || foe.State == FighterState.Air || foe.State == FighterState.JumpPre;
            if (air) { return gap < (48 * 256) ? Light : Approach; }
            if (foe.State == FighterState.Attack && gap < (46 * 256))
            {
                int startup = foe.Move == null ? 0 : foe.Move.Startup;
                if (_style == PilotStyle.Human && foe.Frame >= startup + (foe.Move == null ? 0 : foe.Move.Active))
                {
                    return gap < (40 * 256) ? Light : Approach;
                }

                return Guard;
            }

            if (foe.State == FighterState.HitStun || foe.State == FighterState.BlockStun || foe.State == FighterState.Knockdown)
            {
                return gap < (40 * 256) ? Light : Approach;
            }

            if (gap > (36 * 256)) { return Approach; }
            if (_style == PilotStyle.Human)
            {
                if (gap < (28 * 256)) { return Retreat; }
                return _random.Next(2) == 0 ? Light : Guard;
            }

            int roll = _random.Next(10);
            if (gap < (18 * 256) && roll < 2) { return Throw; }
            if (roll < 6) { return Light; }
            if (roll < 8) { return Heavy; }
            if (roll == 8) { return Special; }
            return Retreat;
        }

        private Pad Emit(Fighter self)
        {
            switch (_intent)
            {
                case Approach:
                    return Direction(self, true);
                case Light:
                    return new Pad(5, true, false, false);
                case Heavy:
                    return new Pad(5, false, true, false);
                case Guard:
                case Retreat:
                    return Direction(self, false);
                case Throw:
                    return new Pad(5, true, true, false);
                case Special:
                    _intent = Approach;
                    _motion = 0;
                    return Motion(self);
                default:
                    return Pad.Neutral;
            }
        }

        private Pad Motion(Fighter self)
        {
            bool right = self.Facing > 0;
            int step = _motion;
            _motion++;
            if (_motion > 2) { _motion = -1; }
            if (step <= 0) { return new Pad(2, false, false, false); }
            if (step == 1) { return new Pad(right ? 3 : 1, false, false, false); }
            return new Pad(right ? 6 : 4, true, false, false);
        }

        private static Pad Direction(Fighter self, bool forward)
        {
            bool right = self.Facing > 0;
            int dir = forward ? (right ? 6 : 4) : (right ? 4 : 6);
            return new Pad(dir, false, false, false);
        }

        private static bool Ready(Fighter fighter)
        {
            switch (fighter.State)
            {
                case FighterState.Idle:
                case FighterState.Walk:
                case FighterState.Crouch:
                    return true;
                default:
                    return false;
            }
        }

        private int Mix(int seed)
        {
            unchecked
            {
                return seed + (_index * 7919) + ((int)_style * 104729);
            }
        }
    }
}
