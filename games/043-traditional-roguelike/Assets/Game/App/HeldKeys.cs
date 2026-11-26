using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 押しているキー。物理キーごとに持つ（同じ役割に複数のキーを割り当てても、一時停止で空にした後に別のキーを押せば効く。035・037・042）。
    /// 方向は、テンキーを押していれば最後に押したテンキーの方向。なければ矢印・WASD の左右と上下をそれぞれ後から押した方で合わせる。
    /// Shift（斜め）を押している間は、左右と上下の両方を押しているときだけ斜めの方向になる。
    /// </summary>
    public sealed class HeldKeys
    {
        private readonly int[] _bits = new int[Enum.GetValues(typeof(Control)).Length];
        private readonly List<(Control Control, int Key)> _order = new();

        public bool IsHeld(Control control) => _bits[(int)control] != 0;

        /// <summary>方向のキー（矢印・WASD・テンキー）をどれか押しているか。</summary>
        public bool AnyDirectionKey => IsHeld(Control.Left) || IsHeld(Control.Right) || IsHeld(Control.Up) || IsHeld(Control.Down) || IsHeld(Control.Pad);

        public static bool IsDirectional(Control control) => control <= Control.Pad;

        public void Press(Control control, int key)
        {
            _bits[(int)control] |= Bit(key);
            if (IsDirectional(control))
            {
                _order.Remove((control, key));
                _order.Add((control, key));
            }
        }

        public void Release(Control control, int key)
        {
            _bits[(int)control] &= ~Bit(key);
            if (IsDirectional(control)) { _order.Remove((control, key)); }
        }

        public void Clear()
        {
            Array.Clear(_bits, 0, _bits.Length);
            _order.Clear();
        }

        /// <summary>今の押し方の方向。なければ null。</summary>
        public Direction? Direction()
        {
            for (int i = _order.Count - 1; i >= 0; i--)
            {
                if (_order[i].Control == Control.Pad) { return (Direction)_order[i].Key; }
            }

            int h = 0;
            int v = 0;
            for (int i = _order.Count - 1; i >= 0 && (h == 0 || v == 0); i--)
            {
                switch (_order[i].Control)
                {
                    case Control.Left when h == 0: h = -1; break;
                    case Control.Right when h == 0: h = 1; break;
                    case Control.Up when v == 0: v = -1; break;
                    case Control.Down when v == 0: v = 1; break;
                }
            }

            if (IsHeld(Control.Diagonal) && (h == 0 || v == 0)) { return null; }
            return (h, v) switch
            {
                (0, -1) => Core.Direction.North,
                (1, -1) => Core.Direction.NorthEast,
                (1, 0) => Core.Direction.East,
                (1, 1) => Core.Direction.SouthEast,
                (0, 1) => Core.Direction.South,
                (-1, 1) => Core.Direction.SouthWest,
                (-1, 0) => Core.Direction.West,
                (-1, -1) => Core.Direction.NorthWest,
                _ => null,
            };
        }

        private static int Bit(int key)
        {
            if (key < 0 || key > Intent.MaxKey) { throw new ArgumentOutOfRangeException(nameof(key), key, "物理キーの番号は 0〜7 です。"); }
            return 1 << key;
        }
    }
}
