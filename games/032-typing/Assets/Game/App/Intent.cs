using System;
using Game.Core;

namespace Game.App
{
    public enum IntentKind
    {
        /// <summary>文字キー（<see cref="RomajiTable.Keys"/>）。プレイ中は打鍵、一時停止中の <c>t</c> はタイトルへ。</summary>
        Key,

        /// <summary>Backspace。プレイ中は狙いの解除。</summary>
        Backspace,

        /// <summary>↑。タイトルで難度を上へ。</summary>
        Up,

        /// <summary>↓。タイトルで難度を下へ。</summary>
        Down,

        /// <summary>Enter。タイトルで開始、一時停止中は再開、結果では同じ難度で再挑戦。</summary>
        Submit,

        /// <summary>Esc。プレイ中は一時停止、一時停止中は再開、結果ではタイトルへ。</summary>
        Cancel,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。打鍵と画面の操作を 1 本の列に並べ、起きた順に処理する。
    /// <see cref="Time"/> は入力が起きた時刻（秒）。この時刻でどの Tick の前に効くかを決める（030 と同じ）。
    /// </summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        private Intent(IntentKind kind, char key, double time)
        {
            if (!double.IsFinite(time)) { throw new ArgumentOutOfRangeException(nameof(time), time, "時刻は有限の値にしてください。"); }
            Kind = kind;
            Key = key;
            Time = time;
        }

        public IntentKind Kind { get; }

        /// <summary><see cref="IntentKind.Key"/> の文字。それ以外は <c>'\0'</c>。</summary>
        public char Key { get; }

        public double Time { get; }

        /// <summary>文字キー。受け付けない文字は例外（大文字は小文字にする）。</summary>
        public static Intent Type(char key, double time)
        {
            var lower = char.ToLowerInvariant(key);
            if (!RomajiTable.IsKey(lower)) { throw new ArgumentOutOfRangeException(nameof(key), key, "受け付けない文字です。"); }
            return new Intent(IntentKind.Key, lower, time);
        }

        public static Intent Of(IntentKind kind, double time)
        {
            if (kind == IntentKind.Key) { throw new ArgumentException("文字キーは Type で作ってください。", nameof(kind)); }
            if (!Enum.IsDefined(typeof(IntentKind), kind)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。"); }
            return new Intent(kind, '\0', time);
        }

        public bool Equals(Intent other) => Kind == other.Kind && Key == other.Key && Time.Equals(other.Time);

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Key, Time);

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);
    }
}
