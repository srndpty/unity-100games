using System;

namespace Game.Core
{
    /// <summary>ルールの設定。時間はすべて 60Hz の Tick 数で持ち、実行環境による丸めの差をなくす。</summary>
    [Serializable]
    public sealed class Match3Config
    {
        public const int TickRate = 60;

        private const int MaxTicks = 600;
        private const int MaxMoves = 999;
        private const int MaxTargetScore = 10_000_000;
        private const int MaxScorePerPiece = 10_000;
        private const int MaxAttempts = 100_000;

        /// <summary>結晶の色数（3〜6）。</summary>
        public int Colors = 5;

        /// <summary>手数。成立した入れ替えで1減る。</summary>
        public int Moves = 20;

        /// <summary>目標スコア。到達したらクリア。</summary>
        public int TargetScore = 2000;

        /// <summary>結晶1個あたりの得点。</summary>
        public int ScorePerPiece = 10;

        /// <summary>入れ替え（と不成立の戻し）の演出に使う Tick 数。</summary>
        public int SwapTicks = 10;

        /// <summary>並びが消える演出の Tick 数。</summary>
        public int ClearTicks = 18;

        /// <summary>1段落ちるのに使う Tick 数。</summary>
        public int FallTicksPerRow = 4;

        /// <summary>手詰まりで盤面を作り直す演出の Tick 数。</summary>
        public int ReshuffleTicks = 40;

        /// <summary>合法手のある盤面を作るまでの作り直しの上限。</summary>
        public int MaxGenerationAttempts = 1000;

        public void Validate()
        {
            RequireRange(Colors, 3, Pieces.MaxColors, nameof(Colors));
            RequireRange(Moves, 1, MaxMoves, nameof(Moves));
            RequireRange(TargetScore, 1, MaxTargetScore, nameof(TargetScore));
            RequireRange(ScorePerPiece, 1, MaxScorePerPiece, nameof(ScorePerPiece));
            RequireRange(SwapTicks, 0, MaxTicks, nameof(SwapTicks));
            RequireRange(ClearTicks, 0, MaxTicks, nameof(ClearTicks));
            RequireRange(FallTicksPerRow, 0, MaxTicks, nameof(FallTicksPerRow));
            RequireRange(ReshuffleTicks, 0, MaxTicks, nameof(ReshuffleTicks));
            RequireRange(MaxGenerationAttempts, 1, MaxAttempts, nameof(MaxGenerationAttempts));
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public Match3Config Copy() => (Match3Config)MemberwiseClone();

        private static void RequireRange(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
