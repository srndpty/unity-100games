using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>衝突の始まり 1 回：2 つの物体（部材の番号、部材でなければ -1）と法線撃力の合計（N·s）。</summary>
    public readonly struct Impact
    {
        public Impact(int a, int b, float impulse)
        {
            A = a;
            B = b;
            Impulse = impulse;
        }

        public int A { get; }

        public int B { get; }

        public float Impulse { get; }
    }

    /// <summary>爆発が部材に届いた：部材の番号と爆心からの距離（m）。</summary>
    public readonly struct BlastHit
    {
        public BlastHit(int piece, float distance)
        {
            Piece = piece;
            Distance = distance;
        }

        public int Piece { get; }

        public float Distance { get; }
    }

    /// <summary>
    /// 物理を 1 Tick 進めた結果（Unity が書き、<see cref="SiegeRun.EndTick"/> が読む）。割り当てを避けるため使い回す。
    /// </summary>
    public sealed class TickReport
    {
        public List<Impact> Impacts { get; } = new();

        public List<BlastHit> Blasts { get; } = new();

        /// <summary>Physics2D が力の上限で外した接合の番号。</summary>
        public List<int> BrokenJoints { get; } = new();

        /// <summary>消滅線の外へ出た部材の番号。</summary>
        public List<int> FellOut { get; } = new();

        /// <summary>飛んでいる弾がこの Tick に何かに触れたか。</summary>
        public bool ShotTouched { get; set; }

        /// <summary>飛んでいる弾が盤の外へ出て消えたか。</summary>
        public bool ShotGone { get; set; }

        /// <summary>動く物（壊れていない部材と弾）の最大の速さ（m/s）。</summary>
        public float MaxSpeed { get; set; }

        /// <summary>動く物の最大の角速度の大きさ（度/s）。</summary>
        public float MaxAngular { get; set; }

        public void Clear()
        {
            Impacts.Clear();
            Blasts.Clear();
            BrokenJoints.Clear();
            FellOut.Clear();
            ShotTouched = false;
            ShotGone = false;
            MaxSpeed = 0f;
            MaxAngular = 0f;
        }
    }

    /// <summary>Tick の始めに Unity へ渡す命令。</summary>
    public readonly struct TickCommands : IEquatable<TickCommands>
    {
        public TickCommands(V2? launch, ShotKind kind, bool detonate, bool removeShot)
        {
            Launch = launch;
            Kind = kind;
            Detonate = detonate;
            RemoveShot = removeShot;
        }

        /// <summary>この初速で <see cref="Kind"/> の弾を発射台から撃つ（なければ null）。</summary>
        public V2? Launch { get; }

        public ShotKind Kind { get; }

        /// <summary>飛んでいる爆発の弾を起爆する（弾は消え、周りの部材に撃力を与えて <see cref="TickReport.Blasts"/> に書く）。</summary>
        public bool Detonate { get; }

        /// <summary>飛んでいる弾を消す（寿命・静止）。</summary>
        public bool RemoveShot { get; }

        public bool IsEmpty => Launch == null && !Detonate && !RemoveShot;

        public bool Equals(TickCommands other) => Launch == other.Launch && Kind == other.Kind && Detonate == other.Detonate && RemoveShot == other.RemoveShot;

        public override bool Equals(object? obj) => obj is TickCommands other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Launch, Kind, Detonate, RemoveShot);

        public static bool operator ==(TickCommands left, TickCommands right) => left.Equals(right);

        public static bool operator !=(TickCommands left, TickCommands right) => !left.Equals(right);
    }
}
