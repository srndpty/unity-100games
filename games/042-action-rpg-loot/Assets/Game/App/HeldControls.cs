using System;
using System.Collections.Generic;

namespace Game.App
{
    /// <summary>
    /// 押している操作。左右（上下）を両方押しているときは後から押したほうを使う。
    /// 攻撃は「押したまま」と「次の Tick までに押した」を分けて持つ（同じ Tick の中で押して離しても 1 回振る。040）。
    /// 薙ぎ払い・回復薬・拾うは押下の印だけを持ち、次の Tick で 1 回だけ効く（押したままでも続けて効かない）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<int> _horizontal = new();
        private readonly List<int> _vertical = new();
        private bool _attackHeld;
        private bool _attackLatched;
        private bool _skill;
        private bool _potion;
        private bool _pick;

        public int Horizontal => _horizontal.Count == 0 ? 0 : _horizontal[_horizontal.Count - 1];

        public int Vertical => _vertical.Count == 0 ? 0 : _vertical[_vertical.Count - 1];

        public void Press(Control control)
        {
            switch (control)
            {
                case Control.Attack:
                    _attackHeld = true;
                    _attackLatched = true;
                    return;
                case Control.Skill: _skill = true; return;
                case Control.Potion: _potion = true; return;
                case Control.Pick: _pick = true; return;
            }

            var (list, value) = Axis(control);
            list.Remove(value);
            list.Add(value);
        }

        public void Release(Control control)
        {
            switch (control)
            {
                case Control.Attack: _attackHeld = false; return;
                case Control.Skill:
                case Control.Potion:
                case Control.Pick:
                    return;
            }

            var (list, value) = Axis(control);
            list.Remove(value);
        }

        /// <summary>この Tick の押下を取り出す。押下の印はここで消える。</summary>
        public (bool Attack, bool Skill, bool Potion, bool Pick) Take()
        {
            var result = (_attackHeld || _attackLatched, _skill, _potion, _pick);
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
            _attackHeld = false;
            _attackLatched = false;
            _skill = false;
            _potion = false;
            _pick = false;
        }

        private (List<int> List, int Value) Axis(Control control) => control switch
        {
            Control.Left => (_horizontal, -1),
            Control.Right => (_horizontal, 1),
            Control.Down => (_vertical, -1),
            Control.Up => (_vertical, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。"),
        };
    }
}
