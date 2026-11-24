using System;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    public enum IntentKind
    {
        /// <summary>マウスの左ボタンを押した（<see cref="Intent.Point"/> は盤の座標）。プレイ中はなぞりの始まり、他の画面では決定。</summary>
        PointerDown,

        /// <summary>押したままマウスが動いた。プレイ中は前の点からのなぞりで切る。</summary>
        PointerMove,

        /// <summary>左ボタンを離した。なぞりを終える。</summary>
        PointerUp,

        /// <summary>Enter・Space。決定・再開・次の問。</summary>
        Submit,

        /// <summary>Esc。プレイ中は一時停止、一時停止中は再開、他の画面では戻る。</summary>
        Cancel,

        /// <summary>R。今の問をやり直す。</summary>
        Retry,

        /// <summary>Backspace。一時停止・結果から問題選択へ戻る。</summary>
        Back,

        /// <summary>← → ↑ ↓。問題選択のカーソル（<see cref="Intent.Index"/> が移動量）。</summary>
        Move,

        /// <summary>問題選択の札をクリックした（<see cref="Intent.Index"/> が問の番号）。</summary>
        Choose,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。マウスとキーを 1 本の列に並べ、起きた順に処理する（012）。
    /// <see cref="Time"/> は入力が起きた時刻（秒）で、どの Tick の境目で効くかを決める（022）。
    /// </summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        private Intent(IntentKind kind, Vec2i point, int index, double time)
        {
            if (!double.IsFinite(time)) { throw new ArgumentOutOfRangeException(nameof(time), time, "時刻は有限の値にしてください。"); }
            if (!Enum.IsDefined(typeof(IntentKind), kind)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。"); }
            Kind = kind;
            Point = point;
            Index = index;
            Time = time;
        }

        public IntentKind Kind { get; }

        /// <summary>マウスの位置（盤の座標）。</summary>
        public Vec2i Point { get; }

        /// <summary><see cref="IntentKind.Move"/> の移動量・<see cref="IntentKind.Choose"/> の問の番号。</summary>
        public int Index { get; }

        public double Time { get; }

        /// <summary>マウスの操作。座標は <see cref="Board.AcceptsSwipePoint"/> の範囲に寄せる（画面の外のマウスでも例外にしない）。</summary>
        public static Intent Mouse(IntentKind kind, Vec2i point, double time)
        {
            if (kind != IntentKind.PointerDown && kind != IntentKind.PointerMove && kind != IntentKind.PointerUp)
            {
                throw new ArgumentException("マウスの操作ではありません。", nameof(kind));
            }

            var clamped = new Vec2i(IntMath.Clamp(point.X, -Board.Width, 2 * Board.Width), IntMath.Clamp(point.Y, -Board.Height, 2 * Board.Height));
            return new Intent(kind, clamped, 0, time);
        }

        public static Intent Move(int delta, double time) => new(IntentKind.Move, Vec2i.Zero, delta, time);

        public static Intent Choose(int index, double time) => new(IntentKind.Choose, Vec2i.Zero, index, time);

        public static Intent Of(IntentKind kind, double time)
        {
            if (kind == IntentKind.PointerDown || kind == IntentKind.PointerMove || kind == IntentKind.PointerUp || kind == IntentKind.Move || kind == IntentKind.Choose)
            {
                throw new ArgumentException("位置・番号の要る操作は専用の作り方を使ってください。", nameof(kind));
            }

            return new Intent(kind, Vec2i.Zero, 0, time);
        }

        public bool Equals(Intent other) => Kind == other.Kind && Point == other.Point && Index == other.Index && Time.Equals(other.Time);

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Point, Index, Time);

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);
    }
}
