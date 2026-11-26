using System;
using System.Collections.Generic;

namespace Game.App
{
    /// <summary>
    /// 押している操作。物理キーごとに持つ（1 つの操作に複数のキーを割り当てても、一時停止で空にした後に別のキーを押せば効く。035・037）。
    /// 左右（上下）を両方押しているときは後から押したキーの向きを使う。
    /// 攻撃は「押したまま」と「次の Tick までに押した」を分けて持つ（同じ Tick の中で押して離しても 1 回振る。040）。
    /// 薙ぎ払い・回復薬・拾うは押下の印だけを持ち、次の Tick で 1 回だけ効く（押したままでも続けて効かない）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<(int Value, int Key)> _horizontal = new();
        private readonly List<(int Value, int Key)> _vertical = new();
        private int _attackKeys;
        private bool _attackLatched;
        private bool _skill;
        private bool _potion;
        private bool _pick;

        public int Horizontal => _horizontal.Count == 0 ? 0 : _horizontal[_horizontal.Count - 1].Value;

        public int Vertical => _vertical.Count == 0 ? 0 : _vertical[_vertical.Count - 1].Value;

        public void Press(Control control, int key = 0)
        {
            int bit = Bit(key);
            switch (control)
            {
                case Control.Attack:
                    _attackKeys |= bit;
                    _attackLatched = true;
                    return;
                case Control.Skill: _skill = true; return;
                case Control.Potion: _potion = true; return;
                case Control.Pick: _pick = true; return;
            }

            var (list, value) = Axis(control);
            list.Remove((value, key));
            list.Add((value, key));
        }

        public void Release(Control control, int key = 0)
        {
            int bit = Bit(key);
            switch (control)
            {
                case Control.Attack: _attackKeys &= ~bit; return;
                case Control.Skill:
                case Control.Potion:
                case Control.Pick:
                    return;
            }

            var (list, value) = Axis(control);
            list.Remove((value, key));
        }

        /// <summary>この Tick の押下を取り出す。押下の印はここで消える。</summary>
        public (bool Attack, bool Skill, bool Potion, bool Pick) Take()
        {
            var result = (_attackKeys != 0 || _attackLatched, _skill, _potion, _pick);
            _attackLatched = false;
            _skill = false;
            _potion = false;
            _pick = false;
            return result;
        }

        public void Clear()
        {
            _horizontal.Clear();
            _vertical.Clear();
            _attackKeys = 0;
            _attackLatched = false;
            _skill = false;
            _potion = false;
            _pick = false;
        }

        private static int Bit(int key)
        {
            if (key < 0 || key > Intent.MaxKey) { throw new ArgumentOutOfRangeException(nameof(key), key, "物理キーの番号は 0〜7 です。"); }
            return 1 << key;
        }

        private (List<(int Value, int Key)> List, int Value) Axis(Control control) => control switch
        {
            Control.Left => (_horizontal, -1),
            Control.Right => (_horizontal, 1),
            Control.Down => (_vertical, -1),
            Control.Up => (_vertical, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。"),
        };
    }
}
