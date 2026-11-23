using System;

namespace Game.Core
{
    /// <summary>
    /// 試合の設定（弾幕の中身は台本 <see cref="BossScript"/> が持つ）。位置・速さは 1 px = <see cref="Field.Unit"/> の整数、時間は Tick 数
    /// （浮動小数点を使わないので Mono と .NET で同じ結果になる）。
    /// </summary>
    [Serializable]
    public sealed class VeilConfig
    {
        /// <summary>ボスの数（仕様書の In scope：ボス 2 体）。</summary>
        public const int BossCount = 2;

        /// <summary>ボス 1 体の弾幕の数（仕様書の In scope：各 3 弾幕）。</summary>
        public const int PatternsPerBoss = 3;

        public const int MaxLives = 9;

        public const int MaxBombs = 9;

        /// <summary>1 Tick の移動量の上限（自機・ショット）。掃引の計算が桁あふれしない範囲に抑える。</summary>
        public const int MaxMove = 32 * Field.Unit;

        public const int MaxRadius = 16 * Field.Unit;

        public const int MaxStatusTicks = 3600;

        public const int MaxDamage = 1000;

        public const int MaxScore = 1_000_000;

        /// <summary>自機のショットの左右のずれの上限。</summary>
        public const int MaxShotOffset = 64 * Field.Unit;

        /// <summary>1 回に撃つショットの数。</summary>
        public const int ShotsPerVolley = 3;

        /// <summary>
        /// 同時に存在しうるショットの数（<see cref="ShotCapacity"/>）の上限。ショットは 1 発ごとに描画物を持つので、
        /// 描画の負荷もこの数で計測する（AGENTS.md「設定の上限は描画側の負荷も含めて決める」）。既定の設定では 30。
        /// </summary>
        public const int MaxShots = 96;

        public int Lives = 3;

        /// <summary>ボムの数（試合の初め・被弾・再挑戦でこの数に戻す）。</summary>
        public int Bombs = 3;

        /// <summary>敵弾の置き場の容量。</summary>
        public int MaxBullets = 2048;

        /// <summary>自機の通常の速さ。斜めは各軸 181/256 倍。</summary>
        public int PlayerSpeed = 4 * Field.Unit;

        /// <summary>低速（Shift）の速さ。</summary>
        public int PlayerSlowSpeed = 7 * Field.Unit / 4;

        /// <summary>自機の当たり判定の半径。</summary>
        public int PlayerRadius = 5 * Field.Unit / 2;

        /// <summary>自機の中心が領域の端からこれより近づかない（絵が切れないように）。</summary>
        public int PlayerMargin = 8 * Field.Unit;

        /// <summary>自機の始めの位置（x は領域の中央）。</summary>
        public int PlayerStartY = 64 * Field.Unit;

        /// <summary>被弾した Tick を含めて何 Tick の間、無敵か。</summary>
        public int InvincibleTicks = 120;

        /// <summary>ボムを使った Tick を含めて何 Tick 続くか（その間は無敵で、敵弾を消し続け、ボスに毎 Tick ダメージ）。</summary>
        public int BombTicks = 120;

        public int BombDamagePerTick = 2;

        /// <summary>ショットを撃ってから次を撃てるまでの Tick 数。</summary>
        public int ShotIntervalTicks = 4;

        public int ShotSpeed = 12 * Field.Unit;

        public int ShotRadius = 4 * Field.Unit;

        public int ShotDamage = 1;

        /// <summary>1 回に撃つ 3 発の左右のずれ（通常）。</summary>
        public int ShotSpread = 10 * Field.Unit;

        /// <summary>低速中の左右のずれ（前に集まる）。</summary>
        public int ShotSlowSpread = 4 * Field.Unit;

        /// <summary>ボスの定位置（各ボスの登場と、弾幕の間の位置）。</summary>
        public int BossHomeX = 192 * Field.Unit;

        public int BossHomeY = 360 * Field.Unit;

        /// <summary>弾幕と弾幕の間（と試合の初め）の Tick 数。この間ボスは撃たず、ショットも効かない。</summary>
        public int IntervalTicks = 90;

        public int ShotHitScore = 10;

        /// <summary>被弾もボムもなく体力を削りきった弾幕のボーナス（弾幕の番号 1〜6 を掛ける）。</summary>
        public int PatternBonus = 3000;

        /// <summary>勝ったときに残機 1 つにつき足す点。</summary>
        public int LifeBonus = 10000;

        /// <summary>勝ったときにボム 1 つにつき足す点。</summary>
        public int BombBonus = 2000;

        public Vec2i PlayerStart => new(Field.Width / 2, PlayerStartY);

        public Vec2i BossHome => new(BossHomeX, BossHomeY);

        /// <summary>
        /// 同時に存在しうるショットの数の上限（どのボスにも当たらない最悪の場合）。ショットはいちばん低い位置（領域の下端から
        /// <see cref="PlayerMargin"/>）から撃っても、k 回動いた位置 y0 + k × 速さ が 領域の高さ + 半径 を超えた Tick の終わりに消えるので、
        /// Tick の終わりに残っているのは最大 k_max = floor((高さ + 半径 - y0) / 速さ) 回動いたものまで。その間に撃てる回数は
        /// floor(k_max / 間隔) + 1 で、1 回に <see cref="ShotsPerVolley"/> 発。<see cref="Validate"/> で <see cref="MaxShots"/> 以下を保証する。
        /// </summary>
        public long ShotCapacity
        {
            get
            {
                if (ShotSpeed <= 0 || ShotIntervalTicks <= 0) { return long.MaxValue; }
                var travel = (long)Field.Height + ShotRadius - PlayerMargin;
                var moves = Math.Max(0, travel / ShotSpeed);
                return ShotsPerVolley * ((moves / ShotIntervalTicks) + 1);
            }
        }

        public VeilConfig Copy() => (VeilConfig)MemberwiseClone();

        /// <summary>値の範囲を確かめる。範囲外なら例外（fail-closed）。</summary>
        public void Validate()
        {
            Range(nameof(Lives), Lives, 1, MaxLives);
            Range(nameof(Bombs), Bombs, 0, MaxBombs);
            Range(nameof(MaxBullets), MaxBullets, 1, BulletPool.MaxCapacity);
            Range(nameof(PlayerSpeed), PlayerSpeed, 1, MaxMove);
            Range(nameof(PlayerSlowSpeed), PlayerSlowSpeed, 1, PlayerSpeed);
            Range(nameof(PlayerRadius), PlayerRadius, 1, MaxRadius);
            Range(nameof(PlayerMargin), PlayerMargin, PlayerRadius, Field.Width / 4);
            Range(nameof(PlayerStartY), PlayerStartY, PlayerMargin, Field.Height - PlayerMargin);
            Range(nameof(InvincibleTicks), InvincibleTicks, 1, MaxStatusTicks);
            Range(nameof(BombTicks), BombTicks, 1, MaxStatusTicks);
            Range(nameof(BombDamagePerTick), BombDamagePerTick, 0, MaxDamage);
            Range(nameof(ShotIntervalTicks), ShotIntervalTicks, 1, MaxStatusTicks);
            Range(nameof(ShotSpeed), ShotSpeed, 1, MaxMove);
            Range(nameof(ShotRadius), ShotRadius, 1, MaxRadius);
            Range(nameof(ShotDamage), ShotDamage, 1, MaxDamage);
            Range(nameof(ShotSpread), ShotSpread, 0, MaxShotOffset);
            Range(nameof(ShotSlowSpread), ShotSlowSpread, 0, MaxShotOffset);
            if (ShotCapacity > MaxShots)
            {
                throw new ArgumentOutOfRangeException(nameof(ShotSpeed), ShotCapacity, $"同時に存在しうるショット {ShotCapacity} 発が上限 {MaxShots} を超えます。ShotSpeed を速くするか ShotIntervalTicks を長くしてください。");
            }
            Range(nameof(BossHomeX), BossHomeX, 0, Field.Width);
            Range(nameof(BossHomeY), BossHomeY, 0, Field.Height);
            Range(nameof(IntervalTicks), IntervalTicks, 1, MaxStatusTicks);
            Range(nameof(ShotHitScore), ShotHitScore, 0, MaxScore);
            Range(nameof(PatternBonus), PatternBonus, 0, MaxScore);
            Range(nameof(LifeBonus), LifeBonus, 0, MaxScore);
            Range(nameof(BombBonus), BombBonus, 0, MaxScore);
        }

        /// <summary>台本の組がこの作品の形（ボス 2 体・各 3 弾幕）かを確かめる。</summary>
        public static void ValidateBosses(System.Collections.Generic.IReadOnlyList<BossScript> bosses)
        {
            if (bosses == null) { throw new ArgumentNullException(nameof(bosses)); }
            if (bosses.Count != BossCount) { throw new ArgumentException($"ボスは {BossCount} 体にしてください（{bosses.Count} 体）。", nameof(bosses)); }
            for (var i = 0; i < bosses.Count; i++)
            {
                var boss = bosses[i] ?? throw new ArgumentException($"{i + 1} 体目のボスがありません。", nameof(bosses));
                if (boss.Patterns.Count != PatternsPerBoss)
                {
                    throw new ArgumentException($"{boss.Name} の弾幕は {PatternsPerBoss} つにしてください（{boss.Patterns.Count} つ）。", nameof(bosses));
                }
            }
        }

        private static void Range(string name, int value, int min, int max)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} にしてください。");
            }
        }
    }
}
