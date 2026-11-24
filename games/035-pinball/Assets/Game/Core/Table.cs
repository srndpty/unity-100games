using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum ColliderKind
    {
        /// <summary>壁・ガイド・ポスト（反射だけ）。</summary>
        Wall,

        /// <summary>一方通行の扉。a→b の左側の法線と逆向きに動く球だけを止める。始めから重なっている球は止めない。</summary>
        Gate,

        /// <summary>バンパー（反射＋外向きの弾き、得点）。</summary>
        Bumper,

        /// <summary>ドロップターゲット（起きている間だけ当たる。触れると倒れて得点）。</summary>
        Target,
    }

    /// <summary>静止した衝突物：線分 a-b に半径を持たせたカプセル（a == b なら円）。</summary>
    public readonly struct Collider
    {
        public Collider(ColliderKind kind, Vec2i a, Vec2i b, int radius, int number)
        {
            Kind = kind;
            A = a;
            B = b;
            Radius = radius;
            Number = number;
        }

        public ColliderKind Kind { get; }

        public Vec2i A { get; }

        public Vec2i B { get; }

        public int Radius { get; }

        /// <summary>種類の中での番号（バンパー・ターゲットの 0〜2）。</summary>
        public int Number { get; }
    }

    public enum FlipperSide
    {
        Left,
        Right,
    }

    /// <summary>フリッパーの形と動く範囲。角度は <c>Trig</c> の単位で、休み（離した）角度から上（押した）角度まで回る。</summary>
    public readonly struct FlipperSpec
    {
        public FlipperSpec(FlipperSide side, Vec2i pivot, int length, int radius, int restAngle, int upAngle)
        {
            Side = side;
            Pivot = pivot;
            Length = length;
            Radius = radius;
            RestAngle = restAngle;
            UpAngle = upAngle;
        }

        public FlipperSide Side { get; }

        public Vec2i Pivot { get; }

        public int Length { get; }

        public int Radius { get; }

        public int RestAngle { get; }

        public int UpAngle { get; }

        /// <summary>上がる向き（+1：反時計回り、−1：時計回り）。</summary>
        public int UpDirection => UpAngle > RestAngle ? 1 : -1;

        /// <summary>角度 <paramref name="angle"/> のときの先端の中心。</summary>
        public Vec2i TipAt(int angle) =>
            Pivot + new Vec2i(IntMath.RoundDiv((long)Length * Trig.Cos(angle), Trig.One), IntMath.RoundDiv((long)Length * Trig.Sin(angle), Trig.One));
    }

    /// <summary>
    /// 台（<c>Assets/Game/Data/Table.txt</c>）。座標はマス単位の小数（1/1024 マスへ四捨五入）、角度は度。
    /// <code>
    /// wall x0 y0 x1 y1 半径          壁（a == b ならポスト）
    /// gate x0 y0 x1 y1 半径          一方通行の扉（a→b の左側へ抜ける向きだけ通す）
    /// bumper x y 半径
    /// target x0 y0 x1 y1 半径
    /// flipper L|R 軸x 軸y 長さ 半径 休みの角度 上の角度
    /// lane x0 x1 置き場y             打ち出しレーン（球は ((x0+x1)/2, 置き場y) に置く）
    /// drain y                        球の中心がこれより下へ出たら失う
    /// </code>
    /// <c>#</c> から行末と空行は無視する。
    /// </summary>
    public sealed class Table
    {
        public const int BumperCount = 3;

        public const int TargetCount = 3;

        /// <summary>フリッパーの長さの上限（8 マス）。</summary>
        public const int MaxFlipperLength = 8 * Board.Unit;

        /// <summary>2π を 2^16 倍した値（角速度から先端の速さを求める）。</summary>
        public const long TwoPiQ16 = 411775;

        private static readonly char[] _separators = { ' ', '\t' };

        private Table(IList<Collider> colliders, FlipperSpec left, FlipperSpec right, int laneX0, int laneX1, int laneRestY, int drainY)
        {
            Colliders = new ReadOnlyCollection<Collider>(colliders);
            Left = left;
            Right = right;
            LaneX0 = laneX0;
            LaneX1 = laneX1;
            LaneRestY = laneRestY;
            DrainY = drainY;
        }

        public ReadOnlyCollection<Collider> Colliders { get; }

        public FlipperSpec Left { get; }

        public FlipperSpec Right { get; }

        public int LaneX0 { get; }

        public int LaneX1 { get; }

        public int LaneRestY { get; }

        public int DrainY { get; }

        /// <summary>レーンで球を置く位置。</summary>
        public Vec2i LaneRest => new((LaneX0 + LaneX1) / 2, LaneRestY);

        public FlipperSpec Flipper(FlipperSide side) => side == FlipperSide.Left ? Left : Right;

        /// <summary>書式を読み、台を検証する。書式の誤りは行番号付き、値の誤りは理由付きの例外。</summary>
        public static Table Parse(string text, PinballConfig config)
        {
            var table = ParseOnly(text);
            table.Validate(config);
            return table;
        }

        /// <summary>書式だけを読む（値の妥当性は <see cref="Validate"/>）。</summary>
        public static Table ParseOnly(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var colliders = new List<Collider>();
            FlipperSpec? left = null;
            FlipperSpec? right = null;
            int[]? lane = null;
            int? drain = null;
            int bumpers = 0;
            int targets = 0;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var comment = line.IndexOf('#');
                if (comment >= 0) { line = line.Substring(0, comment); }
                var words = line.Split(_separators, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) { continue; }
                try
                {
                    switch (words[0])
                    {
                        case "wall":
                        case "gate":
                        case "target":
                            {
                                Count(words, 6);
                                var kind = words[0] == "wall" ? ColliderKind.Wall : words[0] == "gate" ? ColliderKind.Gate : ColliderKind.Target;
                                var number = kind == ColliderKind.Target ? targets++ : 0;
                                colliders.Add(new Collider(kind, Point(words[1], words[2]), Point(words[3], words[4]), Length(words[5]), number));
                                break;
                            }

                        case "bumper":
                            {
                                Count(words, 4);
                                var center = Point(words[1], words[2]);
                                colliders.Add(new Collider(ColliderKind.Bumper, center, center, Length(words[3]), bumpers++));
                                break;
                            }

                        case "flipper":
                            {
                                Count(words, 8);
                                var side = words[1] == "L" ? FlipperSide.Left : words[1] == "R" ? FlipperSide.Right : throw new FormatException("フリッパーの向きは L か R にしてください。");
                                var spec = new FlipperSpec(side, Point(words[2], words[3]), Length(words[4]), Length(words[5]), Angle(words[6]), Angle(words[7]));
                                if (side == FlipperSide.Left)
                                {
                                    if (left != null) { throw new FormatException("左のフリッパーが 2 つあります。"); }
                                    left = spec;
                                }
                                else
                                {
                                    if (right != null) { throw new FormatException("右のフリッパーが 2 つあります。"); }
                                    right = spec;
                                }

                                break;
                            }

                        case "lane":
                            Count(words, 4);
                            if (lane != null) { throw new FormatException("lane が 2 つあります。"); }
                            lane = new[] { Length(words[1]), Length(words[2]), Length(words[3]) };
                            break;
                        case "drain":
                            Count(words, 2);
                            if (drain != null) { throw new FormatException("drain が 2 つあります。"); }
                            drain = Length(words[1]);
                            break;
                        default:
                            throw new FormatException($"未知の行「{words[0]}」です。");
                    }
                }
                catch (Exception error) when (error is FormatException || error is OverflowException)
                {
                    throw new FormatException($"{i + 1} 行目：{error.Message}", error);
                }
            }

            if (left == null || right == null) { throw new FormatException("フリッパーは左右 1 つずつ置いてください。"); }
            if (lane == null) { throw new FormatException("lane がありません。"); }
            if (drain == null) { throw new FormatException("drain がありません。"); }
            return new Table(colliders, left.Value, right.Value, lane[0], lane[1], lane[2], drain.Value);
        }

        /// <summary>台が設定に照らして正しいか。誤りは理由付きの例外。</summary>
        public void Validate(PinballConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();

            var bumpers = 0;
            var targets = 0;
            foreach (var collider in Colliders)
            {
                if (collider.Kind == ColliderKind.Bumper) { bumpers++; }
                if (collider.Kind == ColliderKind.Target) { targets++; }
                if (!Board.Contains(collider.A) || !Board.Contains(collider.B)) { throw new ArgumentException($"衝突物 {collider.A}-{collider.B} が台の外にあります。"); }
                if (collider.Radius <= 0 || collider.Radius > 4 * Board.Unit) { throw new ArgumentException($"衝突物 {collider.A}-{collider.B} の半径は 0 より大きく 4 マス以下にしてください。"); }
                if ((collider.B - collider.A).LengthSquared > (long)Board.MaxSegmentLength * Board.MaxSegmentLength)
                {
                    throw new ArgumentException($"線分 {collider.A}-{collider.B} が 16 マスより長いので分けてください。");
                }

                if ((collider.Kind == ColliderKind.Gate || collider.Kind == ColliderKind.Target) && collider.A == collider.B)
                {
                    throw new ArgumentException($"{collider.Kind} は長さを持つ線分にしてください。");
                }
            }

            if (bumpers != BumperCount) { throw new ArgumentException($"バンパーは {BumperCount} つにしてください（{bumpers} つ）。"); }
            if (targets != TargetCount) { throw new ArgumentException($"ターゲットは {TargetCount} つにしてください（{targets} つ）。"); }

            if (LaneX1 - LaneX0 <= 2 * config.BallRadius) { throw new ArgumentException("レーンの幅は球の直径より広くしてください。"); }
            if (!Board.Contains(LaneRest) || !Board.Contains(new Vec2i(LaneX0, LaneRestY)) || !Board.Contains(new Vec2i(LaneX1, LaneRestY)))
            {
                throw new ArgumentException("レーンが台の外にあります。");
            }

            if (DrainY < 0 || DrainY >= LaneRestY) { throw new ArgumentException("drain は 0 以上、レーンの置き場より下にしてください。"); }
            foreach (var collider in Colliders)
            {
                if (Capsule.Inside(LaneRest, collider.A, collider.B, collider.Radius + config.BallRadius))
                {
                    throw new ArgumentException($"レーンの置き場が衝突物 {collider.A}-{collider.B} と重なっています。");
                }
            }

            ValidateFlipper(Left, FlipperSide.Left, config);
            ValidateFlipper(Right, FlipperSide.Right, config);
            ValidateFlippersApart();
        }

        /// <summary>フリッパーどうしを重ならないか調べる角度の刻み（1 度）。</summary>
        public const int FlipperPairStep = Trig.FullTurn / 360;

        /// <summary>
        /// 左右のフリッパーが、回る範囲のどの組み合わせでも重ならないか（フリッパーどうしの衝突は Core にないので、重なる台は成り立たない）。
        /// 角度を <see cref="FlipperPairStep"/> 刻みで組み合わせ、刻みの間に先端が動く弧の長さを両方ぶん余裕に足して判定する
        /// （刻みの間で触れる配置も拒否する側に倒す）。
        /// </summary>
        private void ValidateFlippersApart()
        {
            var margin = Arc(Left.Length) + Arc(Right.Length) + 1;
            var reach = (int)Math.Min(int.MaxValue, Left.Radius + Right.Radius + margin);
            foreach (var left in Angles(Left))
            {
                var leftTip = Left.TipAt(left);
                foreach (var right in Angles(Right))
                {
                    if (Capsule.SegmentsWithin(Left.Pivot, leftTip, Right.Pivot, Right.TipAt(right), reach))
                    {
                        throw new ArgumentException("左右のフリッパーどうしが回る範囲で重なります（フリッパーどうしは当たらないので、離して置いてください）。");
                    }
                }
            }

            static long Arc(int length) => (((long)length * FlipperPairStep * TwoPiQ16) >> 32) + 1;
        }

        /// <summary>休みの角度から上の角度まで、<see cref="FlipperPairStep"/> 刻み（最後は上の角度ちょうど）。</summary>
        private static System.Collections.Generic.IEnumerable<int> Angles(FlipperSpec flipper)
        {
            var step = flipper.UpDirection * FlipperPairStep;
            for (var angle = flipper.RestAngle; ; angle += step)
            {
                var done = flipper.UpDirection > 0 ? angle >= flipper.UpAngle : angle <= flipper.UpAngle;
                yield return done ? flipper.UpAngle : angle;
                if (done) { yield break; }
            }
        }

        /// <summary>
        /// 1 区間に先端が動く距離が「球の半径 + フリッパーの半径」より小さいか（すり抜けない条件）。
        /// 先端の弧の長さ = 角速度 × 長さ × 2π / 65536 で、弦はこれ以下。
        /// </summary>
        public static bool TipStepIsSafe(int angularSpeed, int length, int flipperRadius, int ballRadius) =>
            (long)angularSpeed * length * TwoPiQ16 < (long)(ballRadius + flipperRadius) << 32;

        /// <summary>全体の指紋（状態のハッシュに含める）。</summary>
        public void MixInto(ref StateHash hash)
        {
            foreach (var collider in Colliders)
            {
                hash.Mix((int)collider.Kind);
                hash.Mix(collider.A.X);
                hash.Mix(collider.A.Y);
                hash.Mix(collider.B.X);
                hash.Mix(collider.B.Y);
                hash.Mix(collider.Radius);
            }

            foreach (var flipper in new[] { Left, Right })
            {
                hash.Mix(flipper.Pivot.X);
                hash.Mix(flipper.Pivot.Y);
                hash.Mix(flipper.Length);
                hash.Mix(flipper.Radius);
                hash.Mix(flipper.RestAngle);
                hash.Mix(flipper.UpAngle);
            }

            hash.Mix(LaneX0);
            hash.Mix(LaneX1);
            hash.Mix(LaneRestY);
            hash.Mix(DrainY);
        }

        private void ValidateFlipper(FlipperSpec flipper, FlipperSide side, PinballConfig config)
        {
            if (flipper.Side != side) { throw new ArgumentException($"{side} のフリッパーの向きが違います。"); }
            if (!Board.Contains(flipper.Pivot)) { throw new ArgumentException($"{side} のフリッパーの軸が台の外にあります。"); }
            if (flipper.Length <= 0 || flipper.Length > MaxFlipperLength) { throw new ArgumentException($"{side} のフリッパーの長さは 0 より大きく 8 マス以下にしてください。"); }
            if (flipper.Radius <= 0 || flipper.Radius > Board.Unit) { throw new ArgumentException($"{side} のフリッパーの半径は 0 より大きく 1 マス以下にしてください。"); }
            var sweep = Math.Abs(flipper.UpAngle - flipper.RestAngle);
            if (sweep == 0 || sweep > Trig.FullTurn / 4) { throw new ArgumentException($"{side} のフリッパーの回る角度は 0 より大きく 90 度以下にしてください。"); }
            if ((side == FlipperSide.Left) != (flipper.UpDirection > 0)) { throw new ArgumentException($"{side} のフリッパーは内側へ向けて上がるようにしてください。"); }

            if (!TipStepIsSafe(config.FlipMaxSpeed, flipper.Length, flipper.Radius, config.BallRadius) ||
                !TipStepIsSafe(config.ReturnMaxSpeed, flipper.Length, flipper.Radius, config.BallRadius))
            {
                throw new ArgumentException($"{side} のフリッパーの先端が 1 区間に「球の半径 + フリッパーの半径」以上動くので、球がすり抜けます。角速度の上限を下げてください。");
            }

            // 回る範囲（角度の表の刻みごと）で静止した衝突物に触れない。
            var step = flipper.UpDirection * (1 << Trig.IndexShift);
            for (var angle = flipper.RestAngle; ; angle += step)
            {
                var done = flipper.UpDirection > 0 ? angle >= flipper.UpAngle : angle <= flipper.UpAngle;
                if (done) { angle = flipper.UpAngle; }
                var tip = flipper.TipAt(angle);
                if (!Board.Contains(tip)) { throw new ArgumentException($"{side} のフリッパーの先端が台の外に出ます。"); }
                foreach (var collider in Colliders)
                {
                    if (Capsule.SegmentsWithin(flipper.Pivot, tip, collider.A, collider.B, flipper.Radius + collider.Radius))
                    {
                        throw new ArgumentException($"{side} のフリッパーの回る範囲に衝突物 {collider.A}-{collider.B} があります。");
                    }
                }

                if (done) { break; }
            }
        }

        private static void Count(string[] words, int count)
        {
            if (words.Length != count) { throw new FormatException($"「{words[0]}」の値は {count - 1} 個にしてください（{words.Length - 1} 個）。"); }
        }

        private static Vec2i Point(string x, string y) => new(Length(x), Length(y));

        /// <summary>マスの小数を単位へ（四捨五入、decimal で計算するので環境に依らない）。</summary>
        private static int Length(string word) => decimal.ToInt32(decimal.Round(Number(word) * Board.Unit, MidpointRounding.AwayFromZero));

        /// <summary>度を <c>Trig</c> の単位へ。</summary>
        private static int Angle(string word) => decimal.ToInt32(decimal.Round(Number(word) * Trig.FullTurn / 360m, MidpointRounding.AwayFromZero));

        private static decimal Number(string word)
        {
            if (!decimal.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) { throw new FormatException($"数ではありません：{word}"); }
            if (Math.Abs(value) > 100000m) { throw new FormatException($"値が大きすぎます：{word}"); }
            return value;
        }
    }
}
