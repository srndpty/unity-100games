using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>攻撃の属性。物理は防御、火・冷気は耐性で減る。</summary>
    public enum Element
    {
        Physical,
        Fire,
        Cold,
    }

    /// <summary>敵の 1 種類の値（1 階の値。深い階では <see cref="DelveConfig.FloorLifePercent"/> などで増える）。</summary>
    [Serializable]
    public sealed class FoeStats
    {
        public int Life = 40;
        public int Damage = 10;
        public Element Element = Element.Physical;

        /// <summary>歩く速さ（単位/Tick）。</summary>
        public int Speed = 12;

        /// <summary>体の円の半径（単位）。</summary>
        public int Radius = 96;

        /// <summary>自機に気づく距離（単位）。</summary>
        public int Aggro = 6 * Units.Tile;

        /// <summary>近接は届く距離（体どうしの隙間）、射手は撃つ距離、突進は突っ込み始める距離（中心どうし）。</summary>
        public int Range = 64;

        /// <summary>射手が離れようとする距離（中心どうし）。射手以外は使わない。</summary>
        public int Keep;

        /// <summary>構えの Tick。</summary>
        public int Windup = 24;

        /// <summary>攻撃の後の待ち Tick。</summary>
        public int Cooldown = 45;

        /// <summary>飛び道具の速さ（単位/Tick）と半径。</summary>
        public int ProjectileSpeed;
        public int ProjectileRadius;

        /// <summary>突進の速さ（単位/Tick）と長さ（Tick）。</summary>
        public int ChargeSpeed;
        public int ChargeTicks;

        public FoeStats Copy() => (FoeStats)MemberwiseClone();
    }

    /// <summary>
    /// 試合の設定。Unity の ScriptableObject からそのまま渡すので公開フィールドで書く。<see cref="Validate"/> を通ったものだけを使う。
    /// 遊んで決める値（敵の強さ・薬・攻撃の間隔など）はここにまとめる。戦利品の表は <see cref="LootData"/>。
    /// </summary>
    [Serializable]
    public sealed class DelveConfig
    {
        /// <summary>移動速度 100% のときの歩く速さ（単位/Tick）。</summary>
        public int HeroSpeed = 24;

        /// <summary>自機の体の円の半径と、壁との当たりの箱の 1 辺（単位）。</summary>
        public int HeroRadius = 96;
        public int HeroBox = 192;

        /// <summary>攻撃速度 100% の攻撃の間隔（Tick）と、振り始めから当たるまでの Tick。</summary>
        public int AttackInterval = 30;
        public int AttackWindup = 6;

        /// <summary>基本攻撃の当たりの円：自機の中心から照準の向きへ Reach の点を中心に、半径 AttackRadius。</summary>
        public int AttackReach = 224;
        public int AttackRadius = 192;

        /// <summary>薙ぎ払い：待ち時間・当たるまで・半径（自機の中心から）・威力の倍率（%）。</summary>
        public int SkillCooldown = 240;
        public int SkillWindup = 10;
        public int SkillRadius = 480;
        public int SkillPercent = 150;

        public int InvulnTicks = 30;
        public int DeathTicks = 90;

        /// <summary>回復薬：最大HP に対する回復量（%）・続けて使えない Tick・持てる数・始めの数。</summary>
        public int PotionHealPercent = 40;
        public int PotionCooldown = 60;
        public int MaxPotions = 5;
        public int StartPotions = 2;

        /// <summary>E で拾える距離・名前札のクリックで拾える距離・回復薬を触って拾う距離（中心どうし）。</summary>
        public int PickupRange = 384;
        public int ClickPickupRange = 768;
        public int PotionTouchRange = 160;

        public int InventorySize = 20;

        /// <summary>1 つの階の地面に置ける品の上限。超えた分は消える（品の総数を有限に保つ）。</summary>
        public int GroundCap = 48;

        /// <summary>飛び道具の同時の上限。満杯のときは撃たない。</summary>
        public int ProjectileCap = 64;

        /// <summary>始めに装備している武器（基礎品の ID、普通）。</summary>
        public string StartWeapon = "shortsword";

        /// <summary>階ごとの敵の体力・威力の倍率（%）。番人には掛けない。</summary>
        public int[] FloorLifePercent = { 100, 160, 230 };
        public int[] FloorDamagePercent = { 100, 135, 170 };

        /// <summary>種類ごとの敵の値（<see cref="FoeKind"/> の順）。</summary>
        public FoeStats[] Foes =
        {
            new() { Life = 40, Damage = 10, Speed = 12, Radius = 96, Aggro = 6 * Units.Tile, Range = 64, Windup = 24, Cooldown = 45 },
            new()
            {
                Life = 28, Damage = 9, Element = Element.Cold, Speed = 10, Radius = 80, Aggro = 7 * Units.Tile, Range = 5 * Units.Tile,
                Keep = 3 * Units.Tile, Windup = 20, Cooldown = 80, ProjectileSpeed = 40, ProjectileRadius = 48,
            },
            new()
            {
                Life = 60, Damage = 16, Speed = 10, Radius = 112, Aggro = 7 * Units.Tile, Range = 5 * Units.Tile, Windup = 36, Cooldown = 70,
                ChargeSpeed = 56, ChargeTicks = 28,
            },
            new()
            {
                Life = 440, Damage = 12, Speed = 12, Radius = 192, Aggro = 9 * Units.Tile, Range = 96, Windup = 30, Cooldown = 70,
                ProjectileSpeed = 36, ProjectileRadius = 64, ChargeSpeed = 60, ChargeTicks = 30,
            },
        };

        /// <summary>番人の火の扇：弾の数・弾どうしの角度（<see cref="Trig.FullTurn"/> 単位）・間隔（HP が半分を超える間／以下）・威力。</summary>
        public int BossFanCount = 5;
        public int BossFanSpread = Trig.FullTurn / 24;
        public int BossFanInterval = 160;
        public int BossFanIntervalEnraged = 110;
        public int BossFanDamage = 8;

        /// <summary>番人が HP 半分以下で突進する間隔（Tick）。</summary>
        public int BossChargeInterval = 240;

        /// <summary>番人を倒してから結果へ移るまでの Tick（落とした品が見える）。</summary>
        public int VictoryDelay = 120;

        public DelveConfig Copy()
        {
            var copy = (DelveConfig)MemberwiseClone();
            copy.FloorLifePercent = (int[])FloorLifePercent.Clone();
            copy.FloorDamagePercent = (int[])FloorDamagePercent.Clone();
            copy.Foes = new FoeStats[Foes.Length];
            for (int i = 0; i < Foes.Length; i++) { copy.Foes[i] = Foes[i].Copy(); }
            return copy;
        }

        public FoeStats Foe(FoeKind kind) => Foes[(int)kind];

        /// <summary>範囲の検証。理由付きで <see cref="ArgumentException"/> を投げる。</summary>
        public void Validate()
        {
            Range(nameof(HeroSpeed), HeroSpeed, 1, 128);
            Range(nameof(HeroRadius), HeroRadius, 16, Units.Tile / 2);
            Range(nameof(HeroBox), HeroBox, 32, Units.Tile - 16);
            Range(nameof(AttackInterval), AttackInterval, 4, 600);
            Range(nameof(AttackWindup), AttackWindup, 0, 120);
            Range(nameof(AttackReach), AttackReach, 0, 4 * Units.Tile);
            Range(nameof(AttackRadius), AttackRadius, 16, 4 * Units.Tile);
            Range(nameof(SkillCooldown), SkillCooldown, 1, 3600);
            Range(nameof(SkillWindup), SkillWindup, 0, 120);
            Range(nameof(SkillRadius), SkillRadius, 32, 8 * Units.Tile);
            Range(nameof(SkillPercent), SkillPercent, 1, 1000);
            Range(nameof(InvulnTicks), InvulnTicks, 0, 600);
            Range(nameof(DeathTicks), DeathTicks, 1, 600);
            Range(nameof(PotionHealPercent), PotionHealPercent, 1, 100);
            Range(nameof(PotionCooldown), PotionCooldown, 0, 600);
            Range(nameof(MaxPotions), MaxPotions, 0, 99);
            Range(nameof(StartPotions), StartPotions, 0, MaxPotions);
            Range(nameof(PickupRange), PickupRange, 32, 8 * Units.Tile);
            Range(nameof(ClickPickupRange), ClickPickupRange, PickupRange, 16 * Units.Tile);
            Range(nameof(PotionTouchRange), PotionTouchRange, 16, 4 * Units.Tile);
            Range(nameof(InventorySize), InventorySize, 1, 60);
            Range(nameof(GroundCap), GroundCap, 1, 256);
            Range(nameof(ProjectileCap), ProjectileCap, 1, 512);
            Range(nameof(BossFanCount), BossFanCount, 1, 32);
            Range(nameof(BossFanSpread), BossFanSpread, 0, Trig.FullTurn / BossFanCount);
            Range(nameof(BossFanInterval), BossFanInterval, 1, 3600);
            Range(nameof(BossFanIntervalEnraged), BossFanIntervalEnraged, 1, 3600);
            Range(nameof(BossFanDamage), BossFanDamage, 0, 10000);
            Range(nameof(BossChargeInterval), BossChargeInterval, 1, 3600);
            Range(nameof(VictoryDelay), VictoryDelay, 0, 3600);
            if (string.IsNullOrEmpty(StartWeapon)) { throw new ArgumentException("StartWeapon を指定してください。"); }
            Percents(nameof(FloorLifePercent), FloorLifePercent);
            Percents(nameof(FloorDamagePercent), FloorDamagePercent);
            if (Foes == null || Foes.Length != FoeInfo.Count) { throw new ArgumentException($"Foes は {FoeInfo.Count} 種類です。"); }
            for (int i = 0; i < Foes.Length; i++)
            {
                var f = Foes[i] ?? throw new ArgumentException($"Foes[{i}] がありません。");
                var kind = (FoeKind)i;
                string p = $"Foes[{kind}].";
                Range(p + nameof(f.Life), f.Life, 1, 1000000);
                Range(p + nameof(f.Damage), f.Damage, 0, 10000);
                Range(p + nameof(f.Speed), f.Speed, 0, 128);
                Range(p + nameof(f.Radius), f.Radius, 16, Units.Tile);
                Range(p + nameof(f.Aggro), f.Aggro, Units.Tile, 32 * Units.Tile);
                Range(p + nameof(f.Range), f.Range, 0, 16 * Units.Tile);
                Range(p + nameof(f.Windup), f.Windup, 1, 600);
                Range(p + nameof(f.Cooldown), f.Cooldown, 1, 600);
                bool shoots = kind == FoeKind.Archer || kind == FoeKind.Boss;
                bool charges = kind == FoeKind.Charger || kind == FoeKind.Boss;
                if (shoots)
                {
                    // 1 Tick の移動を半径の和より小さく保つ（掃引の判定は厳密だが、壁の判定は終わりの位置で行う）。
                    Range(p + nameof(f.ProjectileSpeed), f.ProjectileSpeed, 1, Units.Tile / 2);
                    Range(p + nameof(f.ProjectileRadius), f.ProjectileRadius, 8, Units.Tile);
                }

                if (charges)
                {
                    Range(p + nameof(f.ChargeSpeed), f.ChargeSpeed, 1, 128);
                    Range(p + nameof(f.ChargeTicks), f.ChargeTicks, 1, 600);
                }

                if (kind == FoeKind.Archer) { Range(p + nameof(f.Keep), f.Keep, 0, f.Range); }
            }

            // 1 体の敵の 1 Tick の移動は自機の当たりの箱より小さくする（すり抜けない）。
            foreach (var f in Foes)
            {
                if (Math.Max(f.Speed, f.ChargeSpeed) >= HeroBox) { throw new ArgumentException("敵の 1 Tick の移動は自機の箱より小さくしてください。"); }
            }
        }

        private static void Percents(string name, int[] values)
        {
            if (values == null || values.Length != LootData.MaxFloor) { throw new ArgumentException($"{name} は {LootData.MaxFloor} 階ぶん必要です。"); }
            for (int i = 0; i < values.Length; i++) { Range($"{name}[{i}]", values[i], 1, 10000); }
        }

        private static void Range(string name, int value, int min, int max)
        {
            if (value < min || value > max) { throw new ArgumentException($"{name} は {min}〜{max} にしてください（{value}）。"); }
        }

        /// <summary>設定の指紋。状態のハッシュに混ぜる（040）。</summary>
        public uint Fingerprint()
        {
            var hash = StateHash.Start();
            foreach (var v in new[]
            {
                HeroSpeed, HeroRadius, HeroBox, AttackInterval, AttackWindup, AttackReach, AttackRadius, SkillCooldown, SkillWindup,
                SkillRadius, SkillPercent, InvulnTicks, DeathTicks, PotionHealPercent, PotionCooldown, MaxPotions, StartPotions,
                PickupRange, ClickPickupRange, PotionTouchRange, InventorySize, GroundCap, ProjectileCap, BossFanCount, BossFanSpread,
                BossFanInterval, BossFanIntervalEnraged, BossFanDamage, BossChargeInterval, VictoryDelay,
            })
            {
                hash.Mix(v);
            }

            foreach (var v in FloorLifePercent) { hash.Mix(v); }
            foreach (var v in FloorDamagePercent) { hash.Mix(v); }
            foreach (var f in Foes)
            {
                foreach (var v in new[]
                {
                    f.Life, f.Damage, (int)f.Element, f.Speed, f.Radius, f.Aggro, f.Range, f.Keep, f.Windup, f.Cooldown,
                    f.ProjectileSpeed, f.ProjectileRadius, f.ChargeSpeed, f.ChargeTicks,
                })
                {
                    hash.Mix(v);
                }
            }

            hash.Mix(StartWeapon.Length);
            foreach (var ch in StartWeapon) { hash.Mix(ch); }
            return hash.Value;
        }
    }
}
