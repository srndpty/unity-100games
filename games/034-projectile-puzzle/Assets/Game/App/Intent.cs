using System;
using Game.Core;

namespace Game.App
{
    public enum IntentKind
    {
        /// <summary>マウスの左ボタンを押した（<see cref="Intent.Point"/> はワールド座標）。発射台の近くなら構え、飛行中は起爆、他の画面では決定。</summary>
        PointerDown,

        /// <summary>マウスが動いた。構えている間は引きを変える。</summary>
        PointerMove,

        /// <summary>左ボタンを離した。構えていれば撃つ。</summary>
        PointerUp,

        /// <summary>右クリック。構えを取り消す。</summary>
        CancelAim,

        /// <summary>Enter・Space。決定・再開・次の面。飛行中は起爆。</summary>
        Submit,

        /// <summary>Esc。構えていれば取り消し、プレイ中は一時停止、一時停止中は再開、他の画面では戻る。</summary>
        Cancel,

        /// <summary>R。今の面をやり直す。</summary>
        Retry,

        /// <summary>Backspace。一時停止・結果から面選択へ戻る。</summary>
        Back,

        /// <summary>← →。面選択のカーソル（<see cref="Intent.Index"/> が移動量）。</summary>
        Move,

        /// <summary>面選択の札をクリックした（<see cref="Intent.Index"/> が面の番号）。</summary>
        Choose,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。マウスとキーを 1 本の列に並べ、起きた順に処理する（012）。
    /// <see cref="Time"/> は入力が起きた時刻（秒）で、どの Tick の境目で効くかを決める（022）。
    /// </summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        /// <summary>マウスの座標を寄せる範囲（盤の外に十分な余白）。</summary>
        public const float PointLimit = 100f;

        private Intent(IntentKind kind, V2 point, int index, double time)
        {
            if (!double.IsFinite(time)) { throw new ArgumentOutOfRangeException(nameof(time), time, "時刻は有限の値にしてください。"); }
            if (!Enum.IsDefined(typeof(IntentKind), kind)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。"); }
            Kind = kind;
            Point = point;
            Index = index;
            Time = time;
        }

        public IntentKind Kind { get; }

        /// <summary>マウスの位置（ワールド座標、m）。</summary>
        public V2 Point { get; }

        /// <summary><see cref="IntentKind.Move"/> の移動量・<see cref="IntentKind.Choose"/> の面の番号。</summary>
        public int Index { get; }

        public double Time { get; }

        /// <summary>マウスの操作。座標は ±<see cref="PointLimit"/> に寄せる。有限でなければ例外。</summary>
        public static Intent Mouse(IntentKind kind, V2 point, double time)
        {
            if (kind != IntentKind.PointerDown && kind != IntentKind.PointerMove && kind != IntentKind.PointerUp)
            {
                throw new ArgumentException("マウスの操作ではありません。", nameof(kind));
            }

            if (!point.IsFinite) { throw new ArgumentOutOfRangeException(nameof(point), point, "座標は有限の値にしてください。"); }
            var clamped = new V2(Math.Clamp(point.X, -PointLimit, PointLimit), Math.Clamp(point.Y, -PointLimit, PointLimit));
            return new Intent(kind, clamped, 0, time);
        }

        public static Intent Move(int delta, double time) => new(IntentKind.Move, V2.Zero, delta, time);

        public static Intent Choose(int index, double time) => new(IntentKind.Choose, V2.Zero, index, time);

        public static Intent Of(IntentKind kind, double time)
        {
            if (kind == IntentKind.PointerDown || kind == IntentKind.PointerMove || kind == IntentKind.PointerUp || kind == IntentKind.Move || kind == IntentKind.Choose)
            {
                throw new ArgumentException("位置・番号の要る操作は専用の作り方を使ってください。", nameof(kind));
            }

            return new Intent(kind, V2.Zero, 0, time);
        }

        public bool Equals(Intent other) => Kind == other.Kind && Point == other.Point && Index == other.Index && Time.Equals(other.Time);

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Point, Index, Time);

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);
    }
}
