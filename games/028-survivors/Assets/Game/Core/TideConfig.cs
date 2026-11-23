using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 試合の設定。位置・速さは 1 セルを <see cref="Units.Tile"/> 等分した整数、時間は Tick 数で持つ
    /// （浮動小数点を使わないので Mono と .NET で同じ結果になる）。敵・武器・出現の表はデータ（Unity 層では定義アセット）から写す。
    /// </summary>
    [Serializable]
    public sealed class TideConfig
    {
        private const int T = Units.Tile;

        /// <summary>1 Tick の移動量の上限（自機・敵・弾・宝石の共通）。</summary>
        public const int MaxSpeed = T;

        public const int MaxRadius = 4 * T;

        public const int MaxStatusTicks = 3600;

        public const int MaxDurationTicks = 30 * 60 * Units.TickRate;

        /// <summary>同時に生きている敵の数の上限（<see cref="MaxEnemies"/>）の最大。</summary>
        public const int MaxEnemiesLimit = 1000;

        public const int MaxProjectilesLimit = 512;

        public const int MaxGemsLimit = 2048;

        public const int MaxHealth = 100000;

        public const int MaxDamage = 10000;

        public const int MaxExperience = 100000;

        public const int MaxWeight = 100;

        public const int MaxStages = 60;

        public const int MaxDefinitions = 16;

        public const int MaxLevels = 16;

        /// <summary>弾・刃・連鎖の数、貫通数の最大。</summary>
        public const int MaxCount = 16;

        /// <summary>視野の幅・高さ（セル）。カメラもこの大きさにする。</summary>
        public int ViewWidthCells = 32;

        public int ViewHeightCells = 18;

        /// <summary>ボスが出るまでの Tick。既定は 5 分。</summary>
        public int DurationTicks = 5 * 60 * Units.TickRate;

        /// <summary>ボスが出てから倒すまでの猶予（Tick）。過ぎたら時間切れで負け。</summary>
        public int BossTimeLimitTicks = 2 * 60 * Units.TickRate;

        public int PlayerHealth = 100;

        public int PlayerRadius = 80;

        /// <summary>自機の速さ。斜めは各軸 181/256 倍。</summary>
        public int PlayerSpeed = 22;

        /// <summary>被弾した Tick を含めて何 Tick の間、無敵か。</summary>
        public int InvincibleTicks = 30;

        /// <summary>「体力回復」を選んだときに戻す量。</summary>
        public int HealAmount = 30;

        /// <summary>宝石を吸い寄せ始める距離（自機の中心から宝石の中心まで）。</summary>
        public int PickupRadius = 640;

        public int GemRadius = 40;

        public int GemSpeed = 40;

        /// <summary>落ちている宝石の数の上限。満杯のときの宝石は、近い宝石に値を足す（合計は変わらない）。</summary>
        public int MaxGems = 256;

        /// <summary>同時に生きている敵の数の上限（ボスは別に 1 体）。</summary>
        public int MaxEnemies = 400;

        public int MaxProjectiles = 128;

        /// <summary>敵の向きを決め直す間隔（Tick）。敵の番号で位相をずらし、毎 Tick 一部だけ決め直す。</summary>
        public int SteerIntervalTicks = 6;

        /// <summary>敵どうしの押し合いで 1 体が見る近傍の数の上限。</summary>
        public int SeparationNeighbors = 6;

        /// <summary>押し合いで 1 体が 1 Tick に動かされる量の上限。</summary>
        public int SeparationPush = 12;

        /// <summary>近傍探索のグリッドの 1 マスの幅。</summary>
        public int GridCellSize = 2 * T;

        /// <summary>出現する輪の内側・外側の半径（自機から）。内側は視野の外。</summary>
        public int SpawnInner = 20 * T;

        public int SpawnOuter = 24 * T;

        /// <summary>自機からこの距離より離れた敵は、自機の進む向きの側の出現の輪へ置き直す（体力はそのまま）。</summary>
        public int DespawnDistance = 32 * T;

        /// <summary>出現の区間 1 つの長さ（Tick）。既定は 30 秒。</summary>
        public int StageTicks = 30 * Units.TickRate;

        /// <summary>レベル n → n+1 に必要な経験値（表より上のレベルは最後の値を使う）。</summary>
        public int[] ExperienceTable = { 5, 8, 11, 14, 18, 22, 26, 30, 35, 40, 45, 50, 56, 62, 68, 75, 82, 90, 98, 106, 115, 124, 133, 142 };

        /// <summary>最初に持っている武器（<see cref="Weapons"/> の番号）。</summary>
        public int StartingWeapon;

        public EnemyDef[] Enemies =
        {
            new() { Id = "mite", Behavior = EnemyBehavior.Chase, Radius = 72, Speed = 10, Health = 3, ContactDamage = 5, Experience = 1 },
            new() { Id = "gnat", Behavior = EnemyBehavior.Swarm, Radius = 56, Speed = 22, Health = 2, ContactDamage = 4, Experience = 1 },
            new() { Id = "husk", Behavior = EnemyBehavior.Chase, Radius = 112, Speed = 7, Health = 16, ContactDamage = 10, Experience = 3 },
            new() { Id = "warden", Behavior = EnemyBehavior.Boss, Radius = 320, Speed = 9, Health = 1500, ContactDamage = 25, Experience = 0 },
        };

        public WeaponDef[] Weapons =
        {
            new()
            {
                Id = "bolt", Kind = WeaponKind.Bolt, Levels = new WeaponLevel[]
                {
                    new() { Damage = 4, IntervalTicks = 50, Count = 1, Range = 10 * T, Size = 40, Speed = 72, Pierce = 1, LifeTicks = 50 },
                    new() { Damage = 5, IntervalTicks = 45, Count = 1, Range = 10 * T, Size = 40, Speed = 72, Pierce = 2, LifeTicks = 50 },
                    new() { Damage = 6, IntervalTicks = 40, Count = 2, Range = 10 * T, Size = 44, Speed = 76, Pierce = 2, LifeTicks = 50 },
                    new() { Damage = 7, IntervalTicks = 35, Count = 2, Range = 11 * T, Size = 44, Speed = 80, Pierce = 3, LifeTicks = 50 },
                    new() { Damage = 8, IntervalTicks = 30, Count = 3, Range = 12 * T, Size = 48, Speed = 84, Pierce = 3, LifeTicks = 50 },
                },
            },
            new()
            {
                Id = "orbit", Kind = WeaponKind.Orbit, Levels = new WeaponLevel[]
                {
                    new() { Damage = 3, IntervalTicks = 20, Count = 2, Range = 410, Size = 60, Speed = 546 },
                    new() { Damage = 4, IntervalTicks = 20, Count = 3, Range = 430, Size = 64, Speed = 546 },
                    new() { Damage = 5, IntervalTicks = 18, Count = 3, Range = 460, Size = 70, Speed = 600 },
                    new() { Damage = 6, IntervalTicks = 16, Count = 4, Range = 490, Size = 76, Speed = 655 },
                    new() { Damage = 7, IntervalTicks = 15, Count = 5, Range = 512, Size = 80, Speed = 700 },
                },
            },
            new()
            {
                Id = "pulse", Kind = WeaponKind.Pulse, Levels = new WeaponLevel[]
                {
                    new() { Damage = 3, IntervalTicks = 90, Range = 512 },
                    new() { Damage = 4, IntervalTicks = 80, Range = 580 },
                    new() { Damage = 5, IntervalTicks = 70, Range = 650 },
                    new() { Damage = 6, IntervalTicks = 60, Range = 730 },
                    new() { Damage = 8, IntervalTicks = 50, Range = 820 },
                },
            },
            new()
            {
                Id = "lash", Kind = WeaponKind.Lash, Levels = new WeaponLevel[]
                {
                    new() { Damage = 8, IntervalTicks = 70, Count = 1, Range = 3 * T, Size = 128 },
                    new() { Damage = 10, IntervalTicks = 65, Count = 1, Range = 3 * T + 128, Size = 144 },
                    new() { Damage = 12, IntervalTicks = 60, Count = 2, Range = 4 * T, Size = 160 },
                    new() { Damage = 14, IntervalTicks = 52, Count = 2, Range = 4 * T + 64, Size = 176 },
                    new() { Damage = 16, IntervalTicks = 45, Count = 2, Range = 4 * T + 128, Size = 200 },
                },
            },
            new()
            {
                Id = "arc", Kind = WeaponKind.Arc, Levels = new WeaponLevel[]
                {
                    new() { Damage = 6, IntervalTicks = 100, Count = 2, Range = 7 * T, Size = 3 * T },
                    new() { Damage = 7, IntervalTicks = 90, Count = 3, Range = 7 * T, Size = 3 * T },
                    new() { Damage = 8, IntervalTicks = 80, Count = 4, Range = 8 * T, Size = 3 * T + 128 },
                    new() { Damage = 10, IntervalTicks = 70, Count = 5, Range = 8 * T, Size = 3 * T + 128 },
                    new() { Damage = 12, IntervalTicks = 60, Count = 6, Range = 9 * T, Size = 4 * T },
                },
            },
        };

        /// <summary>出現の区間（30 秒ずつ）。生存数の目標と間隔を縮めて密度を上げる。重みは mite・gnat・husk・warden の順。</summary>
        public SpawnStage[] Stages =
        {
            new() { TargetAlive = 20, IntervalTicks = 30, Weights = new[] { 1, 0, 0, 0 } },
            new() { TargetAlive = 40, IntervalTicks = 20, Weights = new[] { 3, 1, 0, 0 } },
            new() { TargetAlive = 60, IntervalTicks = 15, Weights = new[] { 3, 1, 0, 0 } },
            new() { TargetAlive = 90, IntervalTicks = 12, Weights = new[] { 3, 2, 1, 0 } },
            new() { TargetAlive = 120, IntervalTicks = 10, Weights = new[] { 3, 2, 1, 0 } },
            new() { TargetAlive = 160, IntervalTicks = 8, Weights = new[] { 3, 3, 1, 0 } },
            new() { TargetAlive = 200, IntervalTicks = 6, Weights = new[] { 3, 3, 2, 0 } },
            new() { TargetAlive = 250, IntervalTicks = 5, Weights = new[] { 3, 3, 2, 0 } },
            new() { TargetAlive = 300, IntervalTicks = 4, Weights = new[] { 2, 3, 2, 0 } },
            new() { TargetAlive = 350, IntervalTicks = 3, Weights = new[] { 2, 3, 3, 0 } },
        };

        public int ViewWidth => ViewWidthCells * T;

        public int ViewHeight => ViewHeightCells * T;

        /// <summary>試合の最も長い Tick 数（ボスが出るまで + ボスの猶予）。</summary>
        public int MaxRunTicks => DurationTicks + BossTimeLimitTicks;

        /// <summary>近傍探索のグリッドが覆う範囲の半分の幅（自機から）。置き直しの距離に、1 Tick の移動ぶんの余裕を足す。</summary>
        public int GridHalfExtent => DespawnDistance + (2 * MaxSpeed) + MaxRadius;

        /// <summary>近傍探索のグリッドの 1 辺のマス数。</summary>
        public int GridCells => ((2 * GridHalfExtent) + GridCellSize - 1) / GridCellSize;

        /// <summary>ボスの定義の番号（検証で 1 つだけと保証する）。</summary>
        public int BossIndex => Array.FindIndex(Enemies, enemy => enemy.Behavior == EnemyBehavior.Boss);

        /// <summary>いちばん大きい敵の半径。</summary>
        public int LargestEnemyRadius
        {
            get
            {
                var largest = 0;
                foreach (var enemy in Enemies) { largest = Math.Max(largest, enemy.Radius); }
                return largest;
            }
        }

        /// <summary>経過 Tick に対応する出現の区間（最後の区間より後は最後の区間）。</summary>
        public SpawnStage StageAt(int elapsedTicks)
        {
            var index = elapsedTicks / StageTicks;
            return Stages[index < Stages.Length ? index : Stages.Length - 1];
        }

        /// <summary>レベル <paramref name="level"/> から次へ上がるのに必要な経験値。</summary>
        public int ExperienceToNext(int level) => ExperienceTable[Math.Min(level, ExperienceTable.Length) - 1];

        /// <summary>斜めの移動量。各軸を 1/√2 倍（181/256）にした整数。正の速さは最低 1 にする（切り捨てで 0 にしない）。</summary>
        public static int DiagonalOf(int speed) => speed <= 0 ? 0 : Math.Max(1, speed * 181 / 256);

        /// <summary>
        /// 1 つの武器の定義で同時に存在しうる弾の数（Bolt のみ。レベルごとの「数 × 寿命 ÷ 間隔の切り上げ」の最大）。
        /// </summary>
        public static int ProjectileCapacityOf(WeaponDef weapon)
        {
            if (weapon.Kind != WeaponKind.Bolt) { return 0; }
            var most = 0;
            foreach (var level in weapon.Levels)
            {
                most = Math.Max(most, level.Count * ((level.LifeTicks + level.IntervalTicks - 1) / level.IntervalTicks));
            }

            return most;
        }

        /// <summary>範囲と組み合わせを確かめる。範囲外なら例外（fail-closed）。</summary>
        public void Validate()
        {
            Require(ViewWidthCells, 1, 256, nameof(ViewWidthCells));
            Require(ViewHeightCells, 1, 256, nameof(ViewHeightCells));
            Require(DurationTicks, 1, MaxDurationTicks, nameof(DurationTicks));
            Require(BossTimeLimitTicks, 1, MaxDurationTicks, nameof(BossTimeLimitTicks));
            Require(PlayerHealth, 1, MaxHealth, nameof(PlayerHealth));
            Require(PlayerRadius, 1, MaxRadius, nameof(PlayerRadius));
            Require(PlayerSpeed, 1, MaxSpeed, nameof(PlayerSpeed));
            Require(InvincibleTicks, 1, MaxStatusTicks, nameof(InvincibleTicks));
            Require(HealAmount, 1, MaxHealth, nameof(HealAmount));
            Require(PickupRadius, 1, 16 * T, nameof(PickupRadius));
            Require(GemRadius, 1, MaxRadius, nameof(GemRadius));
            Require(GemSpeed, 1, MaxSpeed, nameof(GemSpeed));
            Require(MaxGems, 1, MaxGemsLimit, nameof(MaxGems));
            Require(MaxEnemies, 1, MaxEnemiesLimit, nameof(MaxEnemies));
            Require(MaxProjectiles, 1, MaxProjectilesLimit, nameof(MaxProjectiles));
            Require(SteerIntervalTicks, 1, 60, nameof(SteerIntervalTicks));
            Require(SeparationNeighbors, 0, MaxCount, nameof(SeparationNeighbors));
            Require(SeparationPush, 0, MaxSpeed, nameof(SeparationPush));
            Require(GridCellSize, T / 4, 16 * T, nameof(GridCellSize));
            Require(StageTicks, 1, MaxDurationTicks, nameof(StageTicks));
            ValidateEnemies();

            // 出現の輪の内側は視野の外（視野の半分の対角線 + いちばん大きい敵の半径より遠い）にする。
            var halfDiagonal = IntMath.SqrtCeiling(((long)ViewWidth * ViewWidth) + ((long)ViewHeight * ViewHeight)) / 2;
            Require(SpawnInner, (int)Math.Min(int.MaxValue, halfDiagonal + LargestEnemyRadius + 8), 256 * T, nameof(SpawnInner));
            Require(SpawnOuter, SpawnInner, 256 * T, nameof(SpawnOuter));

            // 置き直した敵がすぐにまた置き直されないよう、置き直しの距離は輪の外側に 1 Tick の移動の 2 倍以上の余裕を持たせる。
            Require(DespawnDistance, SpawnOuter + (2 * MaxSpeed), 256 * T, nameof(DespawnDistance));
            if (GridCells > 512) { throw new ArgumentOutOfRangeException(nameof(GridCellSize), GridCellSize, "グリッドのマスが多すぎます。マスを大きくしてください。"); }

            // 無限の平面でも座標が int に収まる：最も長い試合を最も速く動いても、置き直しの範囲を足して int の 1/4 未満。
            if ((long)MaxRunTicks * PlayerSpeed + GridHalfExtent >= int.MaxValue / 4)
            {
                throw new ArgumentOutOfRangeException(nameof(DurationTicks), DurationTicks, "試合が長すぎて座標が桁あふれします。");
            }

            ValidateWeapons();
            ValidateStages();

            if (ExperienceTable == null || ExperienceTable.Length < 1 || ExperienceTable.Length > 200)
            {
                throw new ArgumentOutOfRangeException(nameof(ExperienceTable), ExperienceTable?.Length, "1〜200 個の値を指定してください。");
            }

            for (var i = 0; i < ExperienceTable.Length; i++) { Require(ExperienceTable[i], 1, MaxExperience, $"{nameof(ExperienceTable)}[{i}]"); }
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public TideConfig Copy()
        {
            var copy = (TideConfig)MemberwiseClone();
            copy.ExperienceTable = ExperienceTable == null ? null! : (int[])ExperienceTable.Clone();
            copy.Enemies = Enemies == null ? null! : Array.ConvertAll(Enemies, enemy => enemy?.Copy()!);
            copy.Weapons = Weapons == null ? null! : Array.ConvertAll(Weapons, weapon => weapon?.Copy()!);
            copy.Stages = Stages == null ? null! : Array.ConvertAll(Stages, stage => stage?.Copy()!);
            return copy;
        }

        private void ValidateEnemies()
        {
            RequireLength(Enemies?.Length, 2, MaxDefinitions, nameof(Enemies));
            var bosses = 0;
            for (var i = 0; i < Enemies!.Length; i++)
            {
                var enemy = Enemies[i] ?? throw new ArgumentOutOfRangeException(nameof(Enemies), i, "敵の定義が空です。");
                var name = $"{nameof(Enemies)}[{i}]";
                if (!Enum.IsDefined(typeof(EnemyBehavior), enemy.Behavior)) { throw new ArgumentOutOfRangeException(name, enemy.Behavior, "未定義の振る舞いです。"); }
                if (string.IsNullOrEmpty(enemy.Id)) { throw new ArgumentOutOfRangeException(name, "名前が空です。"); }
                Require(enemy.Radius, 1, MaxRadius, name + "." + nameof(EnemyDef.Radius));
                Require(enemy.Speed, 1, MaxSpeed, name + "." + nameof(EnemyDef.Speed));
                Require(enemy.Health, 1, MaxHealth, name + "." + nameof(EnemyDef.Health));
                Require(enemy.ContactDamage, 0, MaxHealth, name + "." + nameof(EnemyDef.ContactDamage));
                Require(enemy.Experience, 0, MaxExperience, name + "." + nameof(EnemyDef.Experience));
                if (enemy.Behavior == EnemyBehavior.Boss) { bosses++; }
            }

            if (bosses != 1) { throw new ArgumentOutOfRangeException(nameof(Enemies), bosses, "ボスの定義はちょうど 1 つにしてください。"); }
        }

        private void ValidateWeapons()
        {
            RequireLength(Weapons?.Length, 1, MaxDefinitions, nameof(Weapons));
            Require(StartingWeapon, 0, Weapons!.Length - 1, nameof(StartingWeapon));
            var projectiles = 0;
            for (var i = 0; i < Weapons.Length; i++)
            {
                var weapon = Weapons[i] ?? throw new ArgumentOutOfRangeException(nameof(Weapons), i, "武器の定義が空です。");
                var name = $"{nameof(Weapons)}[{i}]";
                if (!Enum.IsDefined(typeof(WeaponKind), weapon.Kind)) { throw new ArgumentOutOfRangeException(name, weapon.Kind, "未定義の武器です。"); }
                if (string.IsNullOrEmpty(weapon.Id)) { throw new ArgumentOutOfRangeException(name, "名前が空です。"); }
                RequireLength(weapon.Levels?.Length, 1, MaxLevels, name + "." + nameof(WeaponDef.Levels));
                for (var l = 0; l < weapon.Levels!.Length; l++)
                {
                    ValidateLevel(weapon.Kind, weapon.Levels[l] ?? throw new ArgumentOutOfRangeException(name, l, "レベルの値が空です。"), $"{name}.{nameof(WeaponDef.Levels)}[{l}]");
                }

                projectiles += ProjectileCapacityOf(weapon);
            }

            // 弾の数は、検証を通った設定では上限を超えない（超えたら壊れているので、実行時は例外にする）。
            if (projectiles > MaxProjectiles)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxProjectiles), MaxProjectiles, $"武器の弾が同時に {projectiles} 発存在しえます。上限を増やすか、寿命・間隔・数を見直してください。");
            }
        }

        private static void ValidateLevel(WeaponKind kind, WeaponLevel level, string name)
        {
            // 使う値は正の範囲、使わない値も負や巨大な値は拒否する。
            Require(level.Damage, 1, MaxDamage, name + "." + nameof(WeaponLevel.Damage));
            Require(level.IntervalTicks, 1, MaxStatusTicks, name + "." + nameof(WeaponLevel.IntervalTicks));
            var uses = kind switch
            {
                WeaponKind.Bolt => (Count: true, Range: true, Size: true, Speed: true, Pierce: true, Life: true),
                WeaponKind.Orbit => (Count: true, Range: true, Size: true, Speed: true, Pierce: false, Life: false),
                WeaponKind.Pulse => (Count: false, Range: true, Size: false, Speed: false, Pierce: false, Life: false),
                WeaponKind.Lash => (Count: true, Range: true, Size: true, Speed: false, Pierce: false, Life: false),
                _ => (Count: true, Range: true, Size: true, Speed: false, Pierce: false, Life: false),
            };
            Require(level.Count, uses.Count ? 1 : 0, kind == WeaponKind.Lash ? 2 : MaxCount, name + "." + nameof(WeaponLevel.Count));
            Require(level.Range, uses.Range ? 1 : 0, 16 * T, name + "." + nameof(WeaponLevel.Range));
            Require(level.Size, uses.Size ? 1 : 0, kind == WeaponKind.Arc ? 16 * T : MaxRadius, name + "." + nameof(WeaponLevel.Size));
            Require(level.Speed, uses.Speed ? 1 : 0, kind == WeaponKind.Orbit ? Trig.FullTurn / 16 : MaxSpeed, name + "." + nameof(WeaponLevel.Speed));
            Require(level.Pierce, uses.Pierce ? 1 : 0, MaxCount, name + "." + nameof(WeaponLevel.Pierce));
            Require(level.LifeTicks, uses.Life ? 1 : 0, MaxStatusTicks, name + "." + nameof(WeaponLevel.LifeTicks));
        }

        private void ValidateStages()
        {
            RequireLength(Stages?.Length, 1, MaxStages, nameof(Stages));
            if ((long)Stages!.Length * StageTicks < DurationTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(Stages), Stages.Length, "出現の区間がボスまでの時間を覆っていません。");
            }

            var boss = BossIndex;
            for (var i = 0; i < Stages.Length; i++)
            {
                var stage = Stages[i] ?? throw new ArgumentOutOfRangeException(nameof(Stages), i, "区間が空です。");
                var name = $"{nameof(Stages)}[{i}]";
                Require(stage.TargetAlive, 0, MaxEnemies, name + "." + nameof(SpawnStage.TargetAlive));
                Require(stage.IntervalTicks, 1, MaxDurationTicks, name + "." + nameof(SpawnStage.IntervalTicks));
                if (stage.Weights == null || stage.Weights.Length != Enemies.Length)
                {
                    throw new ArgumentOutOfRangeException(name + "." + nameof(SpawnStage.Weights), stage.Weights?.Length, $"敵の定義と同じ {Enemies.Length} 個の重みを指定してください。");
                }

                var total = 0;
                for (var k = 0; k < stage.Weights.Length; k++)
                {
                    Require(stage.Weights[k], 0, k == boss ? 0 : MaxWeight, $"{name}.{nameof(SpawnStage.Weights)}[{k}]");
                    total += stage.Weights[k];
                }

                if (total == 0) { throw new ArgumentOutOfRangeException(name, "重みの合計が 0 です。"); }
            }
        }

        private static void RequireLength(int? length, int min, int max, string name)
        {
            if (length == null || length < min || length > max) { throw new ArgumentOutOfRangeException(name, length, $"{min}〜{max} 個を指定してください。"); }
        }

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
