using System;
using TileMotion;

namespace Game.Core
{
    public enum EnemyKind
    {
        /// <summary>自機へ向きを変えながら追う。</summary>
        Chaser,

        /// <summary>まっすぐ進み、外周で跳ね返る。</summary>
        Bouncer,

        /// <summary>止まって狙い（予告）、自機の方向へ突進する。</summary>
        Lancer,
    }

    /// <summary>敵 1 種の性能。</summary>
    [Serializable]
    public sealed class EnemyStats
    {
        public int Radius;

        /// <summary>1 Tick の移動量（Lancer は突進していない間の歩く速さ）。</summary>
        public int Speed;

        public int Health;

        public int Score;

        public EnemyStats Copy() => (EnemyStats)MemberwiseClone();
    }

    /// <summary>出現の区間 1 つ（<see cref="SiegeConfig.StageTicks"/> の長さ）。</summary>
    [Serializable]
    public sealed class SpawnStage
    {
        /// <summary>何 Tick ごとに 1 体出すか。</summary>
        public int IntervalTicks;

        public int ChaserWeight;

        public int BouncerWeight;

        public int LancerWeight;

        public int WeightOf(EnemyKind kind) => kind switch
        {
            EnemyKind.Chaser => ChaserWeight,
            EnemyKind.Bouncer => BouncerWeight,
            EnemyKind.Lancer => LancerWeight,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の敵です。"),
        };

        public SpawnStage Copy() => (SpawnStage)MemberwiseClone();
    }

    /// <summary>
    /// 試合の設定。位置・速さは 1 セルを <see cref="Units.Tile"/> 等分した整数、時間は Tick 数で持つ
    /// （浮動小数点を使わないので Mono と .NET で同じ結果になる）。
    /// </summary>
    [Serializable]
    public sealed class SiegeConfig
    {
        /// <summary>1 Tick の移動量の上限（弾・敵・自機の共通）。掃引の計算が桁あふれしない範囲に抑える。</summary>
        public const int MaxSpeed = Units.Tile;

        public const int MaxRadius = 2 * Units.Tile;

        public const int MaxArenaCells = 128;

        public const int MaxStatusTicks = 3600;

        public const int MaxDurationTicks = 60 * 60 * Units.TickRate;

        /// <summary>生存できる敵の数の上限（予告中を含む）の最大。描画の負荷もこの数で計測する。</summary>
        public const int MaxAliveLimit = 64;

        /// <summary>同時に存在できる弾の数の最大。</summary>
        public const int MaxBulletsLimit = 32;

        public const int MaxLives = 9;

        public const int MaxHealth = 99;

        public const int MaxScore = 100000;

        public const int MaxWeight = 100;

        public const int MaxStages = 30;

        /// <summary>出現位置を探す回数の最大（見つからなければその Tick は見送る）。</summary>
        public const int MaxSpawnAttempts = 32;

        /// <summary>アリーナの幅・高さ（セル）。</summary>
        public int ArenaWidthCells = 48;

        public int ArenaHeightCells = 27;

        /// <summary>画面に映る範囲の幅・高さ（セル）。カメラもこの大きさにする。</summary>
        public int ViewWidthCells = 32;

        public int ViewHeightCells = 18;

        /// <summary>試合の長さ（Tick）。生き残れば勝ち。既定は 1 分。</summary>
        public int DurationTicks = 60 * Units.TickRate;

        public int Lives = 3;

        /// <summary>被弾した Tick を含めて何 Tick の間、無敵か。</summary>
        public int InvincibleTicks = 120;

        public int ShipRadius = 72;

        /// <summary>自機の速さ。斜めは各軸 181/256 倍。</summary>
        public int ShipSpeed = 28;

        /// <summary>撃ってから次を撃てるまでの Tick 数（押し続けるとこの間隔で連射）。</summary>
        public int FireIntervalTicks = 6;

        public int BulletSpeed = 96;

        public int BulletRadius = 32;

        /// <summary>撃った Tick を含めて何 Tick 飛ぶか。</summary>
        public int BulletLifeTicks = 50;

        public EnemyStats Chaser = new() { Radius = 96, Speed = 14, Health = 1, Score = 10 };

        public EnemyStats Bouncer = new() { Radius = 104, Speed = 20, Health = 1, Score = 20 };

        public EnemyStats Lancer = new() { Radius = 120, Speed = 6, Health = 3, Score = 50 };

        /// <summary>Lancer が歩く Tick 数（その後に狙う）。</summary>
        public int LancerRestTicks = 60;

        /// <summary>Lancer が止まって狙う Tick 数（向きは狙い始めに決める）。</summary>
        public int LancerAimTicks = 30;

        public int LancerDashTicks = 24;

        public int LancerDashSpeed = 64;

        /// <summary>出現の予告を出す Tick 数。予告中は動かず、当たらない。</summary>
        public int SpawnWarnTicks = 45;

        /// <summary>自機からこの距離未満には出さない。</summary>
        public int SafeRadius = 5 * Units.Tile;

        public int SpawnAttempts = 8;

        /// <summary>生存できる敵の数の上限（予告中を含む）。</summary>
        public int MaxAlive = 36;

        /// <summary>画面外にこの Tick 数続けて居た敵は、得点なしで消える。</summary>
        public int OffscreenLimitTicks = 180;

        /// <summary>出現の区間 1 つの長さ（Tick）。既定は 10 秒。</summary>
        public int StageTicks = 10 * Units.TickRate;

        /// <summary>区間ごとの出現の間隔と種類の重み。既定は間隔を 10 秒ごとに縮め、最後の区間で最初の 3 倍の頻度にする。</summary>
        public SpawnStage[] Stages =
        {
            new() { IntervalTicks = 75, ChaserWeight = 3, BouncerWeight = 1, LancerWeight = 0 },
            new() { IntervalTicks = 62, ChaserWeight = 3, BouncerWeight = 2, LancerWeight = 1 },
            new() { IntervalTicks = 51, ChaserWeight = 3, BouncerWeight = 2, LancerWeight = 1 },
            new() { IntervalTicks = 42, ChaserWeight = 2, BouncerWeight = 2, LancerWeight = 2 },
            new() { IntervalTicks = 33, ChaserWeight = 2, BouncerWeight = 2, LancerWeight = 2 },
            new() { IntervalTicks = 25, ChaserWeight = 2, BouncerWeight = 2, LancerWeight = 2 },
        };

        /// <summary>勝ったときに残機 1 つにつき足す点。</summary>
        public int LifeBonus = 500;

        public int ArenaWidth => ArenaWidthCells * Units.Tile;

        public int ArenaHeight => ArenaHeightCells * Units.Tile;

        public int ViewWidth => ViewWidthCells * Units.Tile;

        public int ViewHeight => ViewHeightCells * Units.Tile;

        /// <summary>同時に存在しうる弾の数（寿命 ÷ 連射の間隔の切り上げ）。<see cref="Validate"/> で <see cref="MaxBulletsLimit"/> 以下を保証する。</summary>
        public int BulletCapacity => (BulletLifeTicks + FireIntervalTicks - 1) / FireIntervalTicks;

        public EnemyStats StatsOf(EnemyKind kind) => kind switch
        {
            EnemyKind.Chaser => Chaser,
            EnemyKind.Bouncer => Bouncer,
            EnemyKind.Lancer => Lancer,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の敵です。"),
        };

        /// <summary>経過 Tick に対応する出現の区間（最後の区間より後は最後の区間）。</summary>
        public SpawnStage StageAt(int elapsedTicks)
        {
            var index = elapsedTicks / StageTicks;
            return Stages[index < Stages.Length ? index : Stages.Length - 1];
        }

        /// <summary>斜めの移動量。各軸を 1/√2 倍（181/256）にした整数。正の速さは最低 1 にする（切り捨てで 0 にしない）。</summary>
        public static int DiagonalOf(int speed) => speed <= 0 ? 0 : Math.Max(1, speed * 181 / 256);

        /// <summary>範囲を確かめる。範囲外なら例外（fail-closed）。</summary>
        public void Validate()
        {
            Require(ArenaWidthCells, 1, MaxArenaCells, nameof(ArenaWidthCells));
            Require(ArenaHeightCells, 1, MaxArenaCells, nameof(ArenaHeightCells));
            Require(ViewWidthCells, 1, ArenaWidthCells, nameof(ViewWidthCells));
            Require(ViewHeightCells, 1, ArenaHeightCells, nameof(ViewHeightCells));
            Require(DurationTicks, 1, MaxDurationTicks, nameof(DurationTicks));
            Require(Lives, 1, MaxLives, nameof(Lives));
            Require(InvincibleTicks, 1, MaxStatusTicks, nameof(InvincibleTicks));
            // 自機はアリーナの外周で止める（中心を [半径, 幅 - 半径] に制限する）。上下限が逆転しないよう、直径はアリーナの短いほうの辺以下にする。
            Require(ShipRadius, 1, Math.Min(MaxRadius, Math.Min(ArenaWidth, ArenaHeight) / 2), nameof(ShipRadius));
            Require(ShipSpeed, 1, MaxSpeed, nameof(ShipSpeed));
            Require(FireIntervalTicks, 1, MaxStatusTicks, nameof(FireIntervalTicks));
            Require(BulletSpeed, 1, MaxSpeed, nameof(BulletSpeed));
            Require(BulletRadius, 1, MaxRadius, nameof(BulletRadius));
            Require(BulletLifeTicks, 1, FireIntervalTicks * MaxBulletsLimit, nameof(BulletLifeTicks));
            RequireStats(Chaser, nameof(Chaser));
            RequireStats(Bouncer, nameof(Bouncer));
            RequireStats(Lancer, nameof(Lancer));
            Require(LancerRestTicks, 1, MaxStatusTicks, nameof(LancerRestTicks));
            Require(LancerAimTicks, 1, MaxStatusTicks, nameof(LancerAimTicks));
            Require(LancerDashTicks, 1, MaxStatusTicks, nameof(LancerDashTicks));
            Require(LancerDashSpeed, 1, MaxSpeed, nameof(LancerDashSpeed));
            Require(SpawnWarnTicks, 1, MaxStatusTicks, nameof(SpawnWarnTicks));
            Require(SpawnAttempts, 1, MaxSpawnAttempts, nameof(SpawnAttempts));
            Require(MaxAlive, 1, MaxAliveLimit, nameof(MaxAlive));
            Require(OffscreenLimitTicks, 1, MaxStatusTicks, nameof(OffscreenLimitTicks));
            Require(StageTicks, 1, MaxDurationTicks, nameof(StageTicks));
            Require(LifeBonus, 0, MaxScore, nameof(LifeBonus));

            // 出現位置は視野の内側（いちばん大きい敵が収まる範囲）から選ぶ。自機が視野のどこに居ても安全距離の外に候補が残るよう、
            // 安全距離は視野の短いほうの半分から敵の大きさを引いた長さ未満にする。
            var largest = Math.Max(Chaser.Radius, Math.Max(Bouncer.Radius, Lancer.Radius));
            var shortHalf = Math.Min(ViewWidth, ViewHeight) / 2;
            Require(SafeRadius, 0, shortHalf - largest - 1, nameof(SafeRadius));

            if (Stages == null || Stages.Length < 1 || Stages.Length > MaxStages)
            {
                throw new ArgumentOutOfRangeException(nameof(Stages), Stages?.Length, $"1〜{MaxStages} 個の区間を指定してください。");
            }

            if ((long)Stages.Length * StageTicks < DurationTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(Stages), Stages.Length, "出現の区間が試合の長さを覆っていません。");
            }

            for (var i = 0; i < Stages.Length; i++)
            {
                var stage = Stages[i] ?? throw new ArgumentOutOfRangeException(nameof(Stages), i, "区間が空です。");
                var name = $"{nameof(Stages)}[{i}]";
                Require(stage.IntervalTicks, 1, MaxDurationTicks, name + "." + nameof(SpawnStage.IntervalTicks));
                Require(stage.ChaserWeight, 0, MaxWeight, name + "." + nameof(SpawnStage.ChaserWeight));
                Require(stage.BouncerWeight, 0, MaxWeight, name + "." + nameof(SpawnStage.BouncerWeight));
                Require(stage.LancerWeight, 0, MaxWeight, name + "." + nameof(SpawnStage.LancerWeight));
                if (stage.ChaserWeight + stage.BouncerWeight + stage.LancerWeight == 0)
                {
                    throw new ArgumentOutOfRangeException(name, "重みの合計が 0 です。");
                }
            }
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public SiegeConfig Copy()
        {
            var copy = (SiegeConfig)MemberwiseClone();
            copy.Chaser = Chaser?.Copy()!;
            copy.Bouncer = Bouncer?.Copy()!;
            copy.Lancer = Lancer?.Copy()!;
            copy.Stages = Stages == null ? null! : Array.ConvertAll(Stages, stage => stage?.Copy()!);
            return copy;
        }

        private static void RequireStats(EnemyStats stats, string name)
        {
            if (stats == null) { throw new ArgumentOutOfRangeException(name, "敵の性能が空です。"); }
            Require(stats.Radius, 1, MaxRadius, name + "." + nameof(EnemyStats.Radius));
            Require(stats.Speed, 1, MaxSpeed, name + "." + nameof(EnemyStats.Speed));
            Require(stats.Health, 1, MaxHealth, name + "." + nameof(EnemyStats.Health));
            Require(stats.Score, 0, MaxScore, name + "." + nameof(EnemyStats.Score));
        }

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
