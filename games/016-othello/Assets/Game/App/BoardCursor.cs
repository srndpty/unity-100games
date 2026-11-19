using System;
using Game.Core;

namespace Game.App
{
    /// <summary>キー操作のカーソル。盤の外へは出ない。キーで動かしている間だけ表示する。</summary>
    public sealed class BoardCursor
    {
        /// <summary>試合の開始時の位置（d4、盤の中央）。</summary>
        public static readonly Square Home = new(3, 3);

        public Square Position { get; private set; } = Home;

        /// <summary>キーで動かした後で、表示する。マウスで打つと隠れる。</summary>
        public bool IsActive { get; private set; }

        public void Move(int dx, int dy)
        {
            Position = new Square(Clamp(Position.X + Math.Sign(dx)), Clamp(Position.Y + Math.Sign(dy)));
            IsActive = true;
        }

        /// <summary>マウスで選んだマスへ移す。カーソルは隠す。</summary>
        public void MoveTo(Square square)
        {
            if (!square.IsInside) { throw new ArgumentOutOfRangeException(nameof(square), square, "盤外のマスです。"); }
            Position = square;
            IsActive = false;
        }

        public void Reset()
        {
            Position = Home;
            IsActive = false;
        }

        /// <summary>
        /// 盤の座標（左上の角が 0、1マスが 1、右と下が正）からマスを求める。盤外なら null。
        /// 範囲の判定を整数へ丸める前に行い、-0.5 のような値を 0 列目にしない。
        /// </summary>
        public static Square? SquareAt(double column, double row)
        {
            if (!double.IsFinite(column) || !double.IsFinite(row)) { return null; }
            if (column < 0 || row < 0 || column >= Square.Size || row >= Square.Size) { return null; }
            return new Square((int)column, (int)row);
        }

        private static int Clamp(int value) => Math.Min(Math.Max(value, 0), Square.Size - 1);
    }
}
