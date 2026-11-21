using System;

namespace Game.Core
{
    /// <summary>
    /// 試合の設定。敵の位置・速さ・半径は 1 セルを <see cref="SubCell"/> 等分した整数で持つ
    /// （浮動小数点を使わないので、Mono と .NET で同じ結果になる）。
    /// </summary>
    [Serializable]
    public sealed class QixConfig
    {
        public const int TickRate = 60;
        public const int SubCell = 256;

        public const int MinStepTicks = 1;
        public const int MaxStepTicks = 30;
        public const int MinEnemyRadius = SubCell / 4;
        public const int MaxEnemySpeed = SubCell;
        public const int MaxTurnTicks = 60 * TickRate;
        public const int MaxLives = 9;
        public const int MinTargetPercent = 1;
        public const int MaxTargetPercent = 99;

        /// <summary>盤の幅（外周の陸を含むセル数）。</summary>
        public int Width = 80;

        /// <summary>盤の高さ（外周の陸を含むセル数）。</summary>
        public int Height = 56;

        /// <summary>自機が 1 セル進むのにかかる Tick 数。</summary>
        public int PlayerStepTicks = 2;

        /// <summary>敵の半径（1/<see cref="SubCell"/> セル単位）。</summary>
        public int EnemyRadius = 384;

        /// <summary>敵の 1 Tick あたりの移動量（1/<see cref="SubCell"/> セル単位）。</summary>
        public int EnemySpeed = 40;

        /// <summary>敵が向きを変えるまでの Tick 数の下限。</summary>
        public int EnemyTurnMinTicks = 40;

        /// <summary>敵が向きを変えるまでの Tick 数の上限。</summary>
        public int EnemyTurnMaxTicks = 150;

        public int Lives = 3;

        /// <summary>勝利に必要な占有率（%）。</summary>
        public int TargetPercent = 75;

        public void Validate()
        {
            Require(Width, CellGrid.MinSize, CellGrid.MaxSize, nameof(Width));
            Require(Height, CellGrid.MinSize, CellGrid.MaxSize, nameof(Height));
            Require(PlayerStepTicks, MinStepTicks, MaxStepTicks, nameof(PlayerStepTicks));

            // 敵は海の中で陸に重ならずに置けなければならない。
            var interior = (Math.Min(Width, Height) - 2) * SubCell;
            Require(EnemyRadius, MinEnemyRadius, (interior - 1) / 2, nameof(EnemyRadius));
            Require(EnemySpeed, 1, MaxEnemySpeed, nameof(EnemySpeed));
            Require(EnemyTurnMinTicks, 1, MaxTurnTicks, nameof(EnemyTurnMinTicks));
            Require(EnemyTurnMaxTicks, EnemyTurnMinTicks, MaxTurnTicks, nameof(EnemyTurnMaxTicks));
            Require(Lives, 1, MaxLives, nameof(Lives));
            Require(TargetPercent, MinTargetPercent, MaxTargetPercent, nameof(TargetPercent));
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public QixConfig Copy() => (QixConfig)MemberwiseClone();

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。");
            }
        }
    }
}
