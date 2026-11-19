using System;

namespace Game.Core
{
    /// <summary>CPU の強さ。強さごとの探索深さは <see cref="OthelloConfig"/> で決める。</summary>
    public enum CpuLevel
    {
        Weak,
        Normal,
        Strong,
    }

    /// <summary>ルールと進行の設定。時間はすべて 60Hz の Tick 数で持つ。</summary>
    [Serializable]
    public sealed class OthelloConfig
    {
        public const int TickRate = 60;

        /// <summary>探索深さの上限。同期探索で1手を短時間に決められる範囲にとどめる。</summary>
        public const int MaxDepth = 5;

        private const int MaxTicks = 600;

        /// <summary>石を置いて返す演出の Tick 数。</summary>
        public int FlipTicks = 18;

        /// <summary>パスを知らせる間の Tick 数。</summary>
        public int PassTicks = 60;

        /// <summary>CPU が手を決めてから打つまでの間の Tick 数。</summary>
        public int CpuThinkTicks = 30;

        /// <summary>「弱い」の探索深さ。</summary>
        public int WeakDepth = 1;

        /// <summary>「普通」の探索深さ。</summary>
        public int NormalDepth = 3;

        /// <summary>「強い」の探索深さ。</summary>
        public int StrongDepth = 5;

        public int DepthOf(CpuLevel level) => level switch
        {
            CpuLevel.Weak => WeakDepth,
            CpuLevel.Normal => NormalDepth,
            CpuLevel.Strong => StrongDepth,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "未定義の強さです。"),
        };

        public void Validate()
        {
            RequireRange(FlipTicks, 0, MaxTicks, nameof(FlipTicks));
            RequireRange(PassTicks, 0, MaxTicks, nameof(PassTicks));
            RequireRange(CpuThinkTicks, 0, MaxTicks, nameof(CpuThinkTicks));
            RequireRange(WeakDepth, 1, MaxDepth, nameof(WeakDepth));
            RequireRange(NormalDepth, 1, MaxDepth, nameof(NormalDepth));
            RequireRange(StrongDepth, 1, MaxDepth, nameof(StrongDepth));
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public OthelloConfig Copy() => (OthelloConfig)MemberwiseClone();

        private static void RequireRange(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
