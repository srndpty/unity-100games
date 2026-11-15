using System;

namespace Game.Core
{
    public enum GameEventKind
    {
        /// <summary>ボールを発射した。</summary>
        Launched,
        WallHit,
        PaddleHit,

        /// <summary>硬いブロックに当たったが壊れなかった。Value はブロック番号。</summary>
        BrickHit,

        /// <summary>ブロックが壊れた。Value はブロック番号。</summary>
        BrickDestroyed,

        /// <summary>カプセルが落ち始めた。Value は <see cref="EffectKind"/>。</summary>
        CapsuleDropped,

        /// <summary>効果が始まった（取り直しを含む）。Value は <see cref="EffectKind"/>。</summary>
        EffectStarted,

        /// <summary>効果が時間切れで終わった。Value は <see cref="EffectKind"/>。ミス・面クリアでの一括解除では出さない。</summary>
        EffectExpired,

        /// <summary>ボールを落とした。Value は残りの残機。</summary>
        LifeLost,

        /// <summary>面をクリアした。Value は面の番号（0 始まり）。</summary>
        StageCleared,

        /// <summary>面が始まった。Value は面の番号（0 始まり）。</summary>
        StageStarted,
        GameWon,
        GameOver,
    }

    /// <summary>1 Tick の間に起きた出来事。演出（音・表示）の起点になる。</summary>
    public readonly struct GameEvent : IEquatable<GameEvent>
    {
        public GameEvent(GameEventKind kind, int value = 0)
        {
            Kind = kind;
            Value = value;
        }

        public GameEventKind Kind { get; }

        public int Value { get; }

        public static bool operator ==(GameEvent a, GameEvent b) => a.Equals(b);

        public static bool operator !=(GameEvent a, GameEvent b) => !a.Equals(b);

        public bool Equals(GameEvent other) => Kind == other.Kind && Value == other.Value;

        public override bool Equals(object? obj) => obj is GameEvent other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Value);

        public override string ToString() => $"{Kind}({Value})";
    }
}
