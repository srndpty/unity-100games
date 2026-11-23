using System;

namespace Game.Core
{
    /// <summary>敵の振る舞い。振る舞いはコードで書き、数値は <see cref="EnemyDef"/> のデータで持つ。</summary>
    public enum EnemyBehavior
    {
        /// <summary>自機へ向かう（向きは <see cref="TideConfig.SteerIntervalTicks"/> ごとに決め直す）。</summary>
        Chase,

        /// <summary>出現したときの自機の向きへ直進し、通り過ぎる（向きを決め直さない）。</summary>
        Swarm,

        /// <summary>最後に出るボス。振る舞いは Chase と同じで、倒すと勝ち。置き直さない。</summary>
        Boss,
    }

    /// <summary>敵 1 種の定義（データ）。位置・速さ・半径は 1 セル = 256 単位の整数。</summary>
    [Serializable]
    public sealed class EnemyDef
    {
        /// <summary>データの名前（描画・定義アセットとの対応づけ）。</summary>
        public string Id = "";

        public EnemyBehavior Behavior;

        public int Radius;

        /// <summary>1 Tick の移動量。</summary>
        public int Speed;

        public int Health;

        /// <summary>自機に触れたときに与えるダメージ。</summary>
        public int ContactDamage;

        /// <summary>倒したときに落とす経験値。</summary>
        public int Experience;

        public EnemyDef Copy() => (EnemyDef)MemberwiseClone();
    }

    /// <summary>武器の種類。種類ごとの動きはコードで書き、強さは <see cref="WeaponDef.Levels"/> のデータで持つ。</summary>
    public enum WeaponKind
    {
        /// <summary>近い順に <see cref="WeaponLevel.Count"/> 体の敵へ弾を撃つ。弾は <see cref="WeaponLevel.Pierce"/> 体まで貫く。</summary>
        Bolt,

        /// <summary>自機の周り（半径 <see cref="WeaponLevel.Range"/>）を <see cref="WeaponLevel.Count"/> 枚の刃が回る。同じ敵へは <see cref="WeaponLevel.IntervalTicks"/> ごと。</summary>
        Orbit,

        /// <summary>自機を中心とする半径 <see cref="WeaponLevel.Range"/> の円の中の敵全体へ、一定の間隔でダメージ。</summary>
        Pulse,

        /// <summary>向いている左右へ、長さ <see cref="WeaponLevel.Range"/>・半分の高さ <see cref="WeaponLevel.Size"/> の矩形を斬る。Count 2 で反対側も。</summary>
        Lash,

        /// <summary>射程 <see cref="WeaponLevel.Range"/> の最も近い敵から、<see cref="WeaponLevel.Size"/> 以内の最も近い敵へ順に <see cref="WeaponLevel.Count"/> 体まで連鎖する。</summary>
        Arc,
    }

    /// <summary>
    /// 武器の 1 レベル分の値。この値をそのまま使う（倍率の計算をしない）ので、強化後の値はこの表と照合できる。
    /// 種類ごとに使う値は <see cref="WeaponKind"/> の説明のとおりで、使わない値は 0 でよい。
    /// </summary>
    [Serializable]
    public sealed class WeaponLevel
    {
        /// <summary>1 回当たったときのダメージ。</summary>
        public int Damage;

        /// <summary>発動の間隔（Tick）。Orbit では同じ敵へ再び当たるまでの Tick。</summary>
        public int IntervalTicks;

        /// <summary>弾の数・刃の数・斬る向きの数・連鎖する数。</summary>
        public int Count;

        /// <summary>狙う射程・刃の回る半径・範囲の半径・斬る長さ。</summary>
        public int Range;

        /// <summary>弾・刃の半径、斬る矩形の半分の高さ、連鎖の 1 段の距離。</summary>
        public int Size;

        /// <summary>弾の 1 Tick の移動量・刃の 1 Tick の回転（1 周 = <see cref="IntGeometry.Trig.FullTurn"/>）。</summary>
        public int Speed;

        /// <summary>弾 1 発が当たれる敵の数。</summary>
        public int Pierce;

        /// <summary>弾の寿命（撃った Tick を含む Tick 数）。</summary>
        public int LifeTicks;

        public WeaponLevel Copy() => (WeaponLevel)MemberwiseClone();
    }

    /// <summary>武器 1 種の定義（データ）。<see cref="Levels"/>[0] が Lv1。</summary>
    [Serializable]
    public sealed class WeaponDef
    {
        public string Id = "";

        public WeaponKind Kind;

        public WeaponLevel[] Levels = Array.Empty<WeaponLevel>();

        public int MaxLevel => Levels.Length;

        /// <summary>レベル <paramref name="level"/>（1 始まり）の値。</summary>
        public WeaponLevel At(int level)
        {
            if (level < 1 || level > Levels.Length) { throw new ArgumentOutOfRangeException(nameof(level), level, $"1〜{Levels.Length} で指定してください。"); }
            return Levels[level - 1];
        }

        public WeaponDef Copy()
        {
            var copy = (WeaponDef)MemberwiseClone();
            copy.Levels = Levels == null ? null! : Array.ConvertAll(Levels, level => level?.Copy()!);
            return copy;
        }
    }

    /// <summary>
    /// 出現の区間 1 つ（<see cref="TideConfig.StageTicks"/> の長さ）。生存数が <see cref="TargetAlive"/> を下回っている間、
    /// <see cref="IntervalTicks"/> ごとに 1 体を <see cref="Weights"/> の重みで選んで出す。
    /// </summary>
    [Serializable]
    public sealed class SpawnStage
    {
        public int TargetAlive;

        public int IntervalTicks;

        /// <summary><see cref="TideConfig.Enemies"/> と同じ並びの重み（ボスは 0）。</summary>
        public int[] Weights = Array.Empty<int>();

        public SpawnStage Copy()
        {
            var copy = (SpawnStage)MemberwiseClone();
            copy.Weights = Weights == null ? null! : (int[])Weights.Clone();
            return copy;
        }
    }
}
