using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>部材 1 つ：素材と、軸に沿った箱（中心と幅・高さ、m）。</summary>
    public readonly struct PieceDef : IEquatable<PieceDef>
    {
        public PieceDef(Material material, V2 center, V2 size)
        {
            Material = material;
            Center = center;
            Size = size;
        }

        public Material Material { get; }

        public V2 Center { get; }

        public V2 Size { get; }

        public float Left => Center.X - Size.X / 2;

        public float Right => Center.X + Size.X / 2;

        public float Bottom => Center.Y - Size.Y / 2;

        public float Top => Center.Y + Size.Y / 2;

        public bool IsTarget => Material == Material.Lantern;

        public bool Equals(PieceDef other) => Material == other.Material && Center == other.Center && Size == other.Size;

        public override bool Equals(object? obj) => obj is PieceDef other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Material, Center, Size);

        public static bool operator ==(PieceDef left, PieceDef right) => left.Equals(right);

        public static bool operator !=(PieceDef left, PieceDef right) => !left.Equals(right);
    }

    /// <summary>2 つの部材をつなぐ接合（Physics2D の <c>FixedJoint2D</c>）。<see cref="BreakForce"/> を超える力で外れる。</summary>
    public readonly struct JointDef : IEquatable<JointDef>
    {
        public JointDef(int a, int b, float breakForce)
        {
            A = a;
            B = b;
            BreakForce = breakForce;
        }

        public int A { get; }

        public int B { get; }

        public float BreakForce { get; }

        public bool Equals(JointDef other) => A == other.A && B == other.B && BreakForce.Equals(other.BreakForce);

        public override bool Equals(object? obj) => obj is JointDef other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(A, B, BreakForce);

        public static bool operator ==(JointDef left, JointDef right) => left.Equals(right);

        public static bool operator !=(JointDef left, JointDef right) => !left.Equals(right);
    }

    /// <summary>1 面のデータ。作った後は変わらない。</summary>
    public sealed class Stage
    {
        /// <summary>接合する 2 つの部材の間に許す隙間（m）。</summary>
        public const float JointGap = 0.02f;

        /// <summary>部材どうしの重なりの許容（m）。接しているだけは重なりではない。</summary>
        public const float OverlapTolerance = 0.001f;

        public const int MaxShots = 5;

        public const float MinSize = 0.1f;

        public const float MaxSize = 8f;

        public Stage(string name, string hint, V2 launcher, IList<ShotKind> shots, IList<PieceDef> pieces, IList<JointDef> joints)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Hint = hint ?? throw new ArgumentNullException(nameof(hint));
            Launcher = launcher;
            Shots = Array.AsReadOnly(new List<ShotKind>(shots ?? throw new ArgumentNullException(nameof(shots))).ToArray());
            Pieces = Array.AsReadOnly(new List<PieceDef>(pieces ?? throw new ArgumentNullException(nameof(pieces))).ToArray());
            Joints = Array.AsReadOnly(new List<JointDef>(joints ?? throw new ArgumentNullException(nameof(joints))).ToArray());
            var targets = 0;
            foreach (var piece in Pieces)
            {
                if (piece.IsTarget) { targets++; }
            }

            TargetCount = targets;
        }

        public string Name { get; }

        public string Hint { get; }

        /// <summary>弾を構える位置。</summary>
        public V2 Launcher { get; }

        /// <summary>使う順の弾。</summary>
        public ReadOnlyCollection<ShotKind> Shots { get; }

        public ReadOnlyCollection<PieceDef> Pieces { get; }

        public ReadOnlyCollection<JointDef> Joints { get; }

        public int TargetCount { get; }

        /// <summary>設定に照らして正しいか。誤りは理由付きの例外。</summary>
        public void Validate(SiegeConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (Name.Length == 0) { throw new FormatException("面の名前がありません。"); }
            if (Shots.Count < 1 || Shots.Count > MaxShots) { throw new FormatException($"弾は 1〜{MaxShots} 発にしてください（{Shots.Count} 発）。"); }
            foreach (var shot in Shots)
            {
                if (!Enum.IsDefined(typeof(ShotKind), shot)) { throw new FormatException($"未定義の弾です：{shot}"); }
            }

            if (TargetCount < 1) { throw new FormatException("標的（lantern）がありません。"); }
            if (!Launcher.IsFinite || Launcher.X < 0 || Launcher.X > Field.Width || Launcher.Y < 0 || Launcher.Y > Field.Height)
            {
                throw new FormatException($"発射台 {Launcher} が盤の外です。");
            }

            // 引いている弾が部材に触れないよう、発射台の周り（掴む範囲 + 引きの上限）に部材を置かない。
            var clear = config.GrabRadius + config.MaxPull;
            for (var i = 0; i < Pieces.Count; i++)
            {
                var piece = Pieces[i];
                if (!Enum.IsDefined(typeof(Material), piece.Material)) { throw new FormatException($"部材 {i}：未定義の素材です。"); }
                if (!piece.Center.IsFinite || !piece.Size.IsFinite) { throw new FormatException($"部材 {i}：値が有限ではありません。"); }
                if (piece.Size.X < MinSize || piece.Size.X > MaxSize || piece.Size.Y < MinSize || piece.Size.Y > MaxSize)
                {
                    throw new FormatException($"部材 {i}：大きさは {MinSize}〜{MaxSize} m にしてください。");
                }

                if (piece.Left < 0 || piece.Right > Field.Width || piece.Bottom < -OverlapTolerance || piece.Top > Field.Height)
                {
                    throw new FormatException($"部材 {i}：盤の外にはみ出しています（地面より下を含む）。");
                }

                if (DistanceToBox(Launcher, piece) < clear) { throw new FormatException($"部材 {i}：発射台から {clear} m 以内にあります。"); }
                for (var j = 0; j < i; j++)
                {
                    if (Overlap(Pieces[j], piece)) { throw new FormatException($"部材 {j} と {i} が重なっています。"); }
                }
            }

            var pairs = new HashSet<long>();
            for (var k = 0; k < Joints.Count; k++)
            {
                var joint = Joints[k];
                if (joint.A < 0 || joint.A >= Pieces.Count || joint.B < 0 || joint.B >= Pieces.Count) { throw new FormatException($"接合 {k}：部材の番号が範囲外です。"); }
                if (joint.A == joint.B) { throw new FormatException($"接合 {k}：同じ部材どうしです。"); }
                if (!float.IsFinite(joint.BreakForce) || joint.BreakForce <= 0) { throw new FormatException($"接合 {k}：外れる力は正の有限の値にしてください。"); }
                var low = Math.Min(joint.A, joint.B);
                var high = Math.Max(joint.A, joint.B);
                if (!pairs.Add(((long)low << 32) | (uint)high)) { throw new FormatException($"接合 {k}：同じ組の接合が 2 つあります。"); }
                if (Gap(Pieces[joint.A], Pieces[joint.B]) > JointGap) { throw new FormatException($"接合 {k}：部材 {joint.A} と {joint.B} が接していません。"); }
            }
        }

        /// <summary>2 つの箱が（許容を超えて）重なっているか。</summary>
        public static bool Overlap(PieceDef a, PieceDef b) =>
            Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > OverlapTolerance
            && Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom) > OverlapTolerance;

        /// <summary>2 つの箱の隙間（重なっていれば 0）。</summary>
        public static float Gap(PieceDef a, PieceDef b)
        {
            var dx = Math.Max(0f, Math.Max(a.Left, b.Left) - Math.Min(a.Right, b.Right));
            var dy = Math.Max(0f, Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Top, b.Top));
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private static float DistanceToBox(V2 point, PieceDef box)
        {
            var dx = Math.Max(0f, Math.Max(box.Left - point.X, point.X - box.Right));
            var dy = Math.Max(0f, Math.Max(box.Bottom - point.Y, point.Y - box.Top));
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}
