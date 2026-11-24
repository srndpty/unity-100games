using System;

namespace Game.Core
{
    /// <summary>部材の素材。<see cref="Lantern"/> は標的（全部壊すと勝ち）。</summary>
    public enum Material
    {
        Wood,
        Stone,
        Ice,
        Lantern,
    }

    /// <summary>弾の種類。</summary>
    public enum ShotKind
    {
        /// <summary>基準の弾。</summary>
        Normal,

        /// <summary>重く少し大きい弾。初速は同じ。</summary>
        Heavy,

        /// <summary>飛行中のクリック、または最初に触れてから一定 Tick で起爆する弾。</summary>
        Bomb,
    }

    /// <summary>盤の大きさ（m）。原点は地面の左端、y は上向き。</summary>
    public static class Field
    {
        public const float Width = 32f;

        public const float Height = 18f;

        /// <summary>これより下へ落ちた部材は壊れたとみなす。</summary>
        public const float KillY = -3f;

        /// <summary>左右の外へこれだけ出た部材は壊れたとみなす。</summary>
        public const float KillMargin = 4f;

        /// <summary>部材の中心が消滅線の外か。</summary>
        public static bool IsOut(V2 center) =>
            !center.IsFinite || center.Y < KillY || center.X < -KillMargin || center.X > Width + KillMargin;
    }

    /// <summary>素材ごとの値。</summary>
    [Serializable]
    public sealed class MaterialSpec
    {
        public int Health;

        /// <summary>壊したときの得点。</summary>
        public int Score;

        /// <summary>密度（Physics2D の <c>density</c>）。</summary>
        public float Density;

        /// <summary>これ以下の撃力（N·s）はダメージにしない。</summary>
        public float Threshold;

        /// <summary>閾値を超えた撃力 1 あたりのダメージ。</summary>
        public float DamagePerImpulse;

        public MaterialSpec(int health, int score, float density, float threshold, float damagePerImpulse)
        {
            Health = health;
            Score = score;
            Density = density;
            Threshold = threshold;
            DamagePerImpulse = damagePerImpulse;
        }

        public MaterialSpec Copy() => (MaterialSpec)MemberwiseClone();
    }

    /// <summary>弾の種類ごとの値。</summary>
    [Serializable]
    public sealed class ShotSpec
    {
        public float Radius;

        public float Density;

        public ShotSpec(float radius, float density)
        {
            Radius = radius;
            Density = density;
        }

        public ShotSpec Copy() => (ShotSpec)MemberwiseClone();
    }

    /// <summary>
    /// 射出・ダメージ・停止の設定。Unity のシリアライズのため公開フィールドで書く。使う前に <see cref="Validate"/> で検証する。
    /// 時間は Tick（1/60 秒）、長さは m、速さは m/s、角速度は度/s。
    /// </summary>
    [Serializable]
    public sealed class SiegeConfig
    {
        /// <summary>重力の加速度（m/s²、下向きは負）。</summary>
        public float Gravity = -9.81f;

        /// <summary>引いた長さの上限。これ以上引いても初速は <see cref="MaxSpeed"/> のまま。</summary>
        public float MaxPull = 2.4f;

        /// <summary>これより短い引きで離したら取り消し（弾を消費しない）。</summary>
        public float MinPull = 0.35f;

        /// <summary><see cref="MaxPull"/> まで引いたときの初速。</summary>
        public float MaxSpeed = 21f;

        /// <summary>発射台からこの距離以内を押したら構える。</summary>
        public float GrabRadius = 1.2f;

        /// <summary>弾道プレビューで見せる Tick 数。</summary>
        public int PreviewTicks = 48;

        /// <summary>弾道プレビューの点の間隔（Tick）。</summary>
        public int PreviewStride = 3;

        public MaterialSpec Wood = new(100, 500, 1f, 2f, 9f);

        public MaterialSpec Stone = new(260, 800, 2.4f, 4f, 9f);

        public MaterialSpec Ice = new(50, 300, 0.8f, 1f, 12f);

        public MaterialSpec Lantern = new(30, 3000, 0.6f, 0.6f, 15f);

        public ShotSpec Normal = new(0.32f, 6f);

        public ShotSpec Heavy = new(0.42f, 14f);

        public ShotSpec Bomb = new(0.36f, 5f);

        /// <summary>
        /// 弾の回転の減衰（Physics2D の <c>angularDamping</c>）。地面を転がる弾を止める。飛ぶ間の位置には効かない（弾道プレビューと一致したまま）。
        /// </summary>
        public float ShotAngularDamping = 3f;

        /// <summary>爆発の届く半径（m）。</summary>
        public float BlastRadius = 2.8f;

        /// <summary>爆心でのダメージ。距離に比例して減り、半径で 0。</summary>
        public int BlastDamage = 180;

        /// <summary>爆心での外向きの撃力（N·s）。Unity 側が距離で減らして与える。</summary>
        public float BlastImpulse = 16f;

        /// <summary>爆発の弾が最初に触れてから自動で起爆するまでの Tick。</summary>
        public int FuseTicks = 90;

        /// <summary>これより遅ければ止まっているとみなす速さ。</summary>
        public float RestSpeed = 0.12f;

        /// <summary>これより遅ければ止まっているとみなす角速度。</summary>
        public float RestAngular = 10f;

        /// <summary>止まっている状態がこれだけ続いたら静止。</summary>
        public int RestTicks = 30;

        /// <summary>撃ってからこれだけ経ったら、揺れが続いていても静止とみなす。</summary>
        public int SettleTimeoutTicks = 720;

        /// <summary>撃ってからこれだけ経った弾は消す。</summary>
        public int ShotLifeTicks = 480;

        /// <summary>面を始めてからこの Tick まではダメージを数えない（積み上げの落ち着き）。</summary>
        public int GraceTicks = 60;

        /// <summary>勝ったときに残った弾 1 発あたりの得点。</summary>
        public int UnusedShotBonus = 2000;

        public MaterialSpec Spec(Material material) => material switch
        {
            Material.Wood => Wood,
            Material.Stone => Stone,
            Material.Ice => Ice,
            Material.Lantern => Lantern,
            _ => throw new ArgumentOutOfRangeException(nameof(material), material, "未定義の素材です。"),
        };

        public ShotSpec Spec(ShotKind kind) => kind switch
        {
            ShotKind.Normal => Normal,
            ShotKind.Heavy => Heavy,
            ShotKind.Bomb => Bomb,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の弾です。"),
        };

        /// <summary>中の素材・弾の値まで複製する。</summary>
        public SiegeConfig Copy()
        {
            var copy = (SiegeConfig)MemberwiseClone();
            copy.Wood = Wood?.Copy()!;
            copy.Stone = Stone?.Copy()!;
            copy.Ice = Ice?.Copy()!;
            copy.Lantern = Lantern?.Copy()!;
            copy.Normal = Normal?.Copy()!;
            copy.Heavy = Heavy?.Copy()!;
            copy.Bomb = Bomb?.Copy()!;
            return copy;
        }

        /// <summary>範囲外の値があれば例外。</summary>
        public void Validate()
        {
            Require(Gravity, -40f, -1f, nameof(Gravity));
            Require(MaxPull, 0.5f, 6f, nameof(MaxPull));
            Require(MinPull, 0.05f, 6f, nameof(MinPull));
            if (MinPull >= MaxPull) { throw new ArgumentOutOfRangeException(nameof(MinPull), MinPull, "MinPull は MaxPull より小さくしてください。"); }
            Require(MaxSpeed, 1f, 60f, nameof(MaxSpeed));
            Require(GrabRadius, 0.2f, 4f, nameof(GrabRadius));
            Require(PreviewTicks, 1, 600, nameof(PreviewTicks));
            Require(PreviewStride, 1, 60, nameof(PreviewStride));
            foreach (Material material in Enum.GetValues(typeof(Material)))
            {
                var spec = Spec(material) ?? throw new ArgumentNullException(material.ToString());
                var name = material.ToString();
                Require(spec.Health, 1, 100000, name + ".Health");
                Require(spec.Score, 0, 1000000, name + ".Score");
                Require(spec.Density, 0.05f, 100f, name + ".Density");
                Require(spec.Threshold, 0f, 1000f, name + ".Threshold");
                Require(spec.DamagePerImpulse, 0f, 1000f, name + ".DamagePerImpulse");
            }

            foreach (ShotKind kind in Enum.GetValues(typeof(ShotKind)))
            {
                var spec = Spec(kind) ?? throw new ArgumentNullException(kind.ToString());
                Require(spec.Radius, 0.1f, 1f, kind + ".Radius");
                Require(spec.Density, 0.05f, 100f, kind + ".Density");
            }

            Require(ShotAngularDamping, 0f, 100f, nameof(ShotAngularDamping));
            Require(BlastRadius, 0.5f, 8f, nameof(BlastRadius));
            Require(BlastDamage, 0, 100000, nameof(BlastDamage));
            Require(BlastImpulse, 0f, 1000f, nameof(BlastImpulse));
            Require(FuseTicks, 1, 600, nameof(FuseTicks));
            Require(RestSpeed, 0.001f, 5f, nameof(RestSpeed));
            Require(RestAngular, 0.01f, 360f, nameof(RestAngular));
            Require(RestTicks, 1, 600, nameof(RestTicks));
            Require(SettleTimeoutTicks, RestTicks, 36000, nameof(SettleTimeoutTicks));
            Require(ShotLifeTicks, 1, SettleTimeoutTicks, nameof(ShotLifeTicks));
            Require(GraceTicks, 0, 600, nameof(GraceTicks));
            Require(UnusedShotBonus, 0, 1000000, nameof(UnusedShotBonus));
        }

        private static void Require(float value, float min, float max, string name)
        {
            if (!float.IsFinite(value) || value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} の有限の値にしてください。");
            }
        }

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} にしてください。");
            }
        }
    }
}
