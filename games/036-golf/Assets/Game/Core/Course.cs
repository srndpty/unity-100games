using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>木：平面上の円柱（中心・半径・高さ、µm）。</summary>
    public readonly struct Tree
    {
        public Tree(Vec2i center, int radius, int height)
        {
            Center = center;
            Radius = radius;
            Height = height;
        }

        public Vec2i Center { get; }

        public int Radius { get; }

        public int Height { get; }
    }

    /// <summary>
    /// 1 ホール。地面は 1 m のマスの格子（x：奥へ、y：横、原点は左下の角）。格子の外は OB。
    /// 傾き（‰、グリーンのマスだけ）は転がる球を低いほうへ加速する。
    /// </summary>
    public sealed class Hole
    {
        public const int MinPar = 3;
        public const int MaxPar = 5;
        public const int MinWidth = 40;
        public const int MaxWidth = 600;
        public const int MinHeight = 20;
        public const int MaxHeight = 200;

        /// <summary>傾きの大きさの上限（‰）。</summary>
        public const int MaxGrade = 80;

        private readonly Surface[] _cells;
        private readonly short[] _gradeX;
        private readonly short[] _gradeY;

        internal Hole(int number, int par, int width, int height, Surface[] cells, short[] gradeX, short[] gradeY, Vec2i tee, Vec2i cup, IList<Tree> trees)
        {
            Number = number;
            Par = par;
            Width = width;
            Height = height;
            _cells = cells;
            _gradeX = gradeX;
            _gradeY = gradeY;
            Tee = tee;
            Cup = cup;
            Trees = new ReadOnlyCollection<Tree>(trees);
            Fingerprint = ComputeFingerprint();
        }

        public int Number { get; }

        public int Par { get; }

        /// <summary>奥行き（m、マスの数）。</summary>
        public int Width { get; }

        /// <summary>横幅（m、マスの数）。</summary>
        public int Height { get; }

        public Vec2i Tee { get; }

        public Vec2i Cup { get; }

        public ReadOnlyCollection<Tree> Trees { get; }

        /// <summary>地面・傾き・木・ティー・カップを混ぜた値（ハッシュ用）。</summary>
        public uint Fingerprint { get; }

        /// <summary>マス (x, y) の地面。格子の外は OB。</summary>
        public Surface SurfaceOfCell(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? Surface.OutOfBounds : _cells[(y * Width) + x];

        /// <summary>位置の地面。</summary>
        public Surface SurfaceAt(Vec2i position) => SurfaceOfCell(Cell(position.X), Cell(position.Y));

        /// <summary>マス (x, y) の傾き（‰）。高いほうから低いほうへの向き。</summary>
        public Vec2i GradeOfCell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) { return Vec2i.Zero; }
            var index = (y * Width) + x;
            return new Vec2i(_gradeX[index], _gradeY[index]);
        }

        public Vec2i GradeAt(Vec2i position) => GradeOfCell(Cell(position.X), Cell(position.Y));

        /// <summary>µm の座標を含むマスの番号（負の値も床へ）。</summary>
        public static int Cell(int coordinate) => coordinate >= 0 ? coordinate / Units.PerMeter : ((coordinate + 1) / Units.PerMeter) - 1;

        private uint ComputeFingerprint()
        {
            var hash = StateHash.Start();
            hash.Mix(Number);
            hash.Mix(Par);
            hash.Mix(Width);
            hash.Mix(Height);
            for (var i = 0; i < _cells.Length; i++)
            {
                hash.Mix((int)_cells[i]);
                hash.Mix(_gradeX[i]);
                hash.Mix(_gradeY[i]);
            }

            hash.Mix(Tee.X);
            hash.Mix(Tee.Y);
            hash.Mix(Cup.X);
            hash.Mix(Cup.Y);
            foreach (var tree in Trees)
            {
                hash.Mix(tree.Center.X);
                hash.Mix(tree.Center.Y);
                hash.Mix(tree.Radius);
                hash.Mix(tree.Height);
            }

            return hash.Value;
        }
    }

    /// <summary>
    /// コース（<c>Assets/Game/Data/Course.txt</c>）。長さは m の小数。
    /// <code>
    /// hole 番号 par 打数 size 奥行き 横幅      ホールの始め（地面は全てラフで始まる）
    /// paint 種類 rect x0 y0 x1 y1             地面を塗る（種類：fairway・tee・green・bunker・water・ob・rough）
    /// paint 種類 ellipse cx cy rx ry
    /// paint 種類 capsule x0 y0 x1 y1 r        線分から r 以内
    /// tee x y
    /// cup x y
    /// tree x y 半径 高さ
    /// slope x0 y0 x1 y1 gx gy                 矩形の中のグリーンのマスの傾き（‰）
    /// </code>
    /// 塗りは書いた順に重ね、マスの中心が図形に入れば塗る。<c>#</c> から行末と空行は無視する。
    /// </summary>
    public sealed class Course
    {
        public const int HoleCount = 3;

        /// <summary>木の半径の範囲（µm）。</summary>
        public const int MinTreeRadius = 300_000;

        public const int MaxTreeRadius = Units.MaxCircleRadius;

        public const int MinTreeHeight = 1_000_000;

        public const int MaxTreeHeight = 30_000_000;

        private static readonly char[] _separators = { ' ', '\t' };

        private Course(IList<Hole> holes)
        {
            Holes = new ReadOnlyCollection<Hole>(holes);
            var hash = StateHash.Start();
            foreach (var hole in holes) { hash.Mix(hole.Fingerprint); }
            Fingerprint = hash.Value;
        }

        public ReadOnlyCollection<Hole> Holes { get; }

        public uint Fingerprint { get; }

        /// <summary>ちょうど <see cref="HoleCount"/> ホールのコースを読む。不正なら例外。</summary>
        public static Course Parse(string text, GolfConfig config)
        {
            var holes = ParseHoles(text, config);
            if (holes.Count != HoleCount) { throw new FormatException($"ホールは {HoleCount} つにしてください（{holes.Count} つ）。"); }
            for (var i = 0; i < holes.Count; i++)
            {
                if (holes[i].Number != i + 1) { throw new FormatException($"ホールの番号は 1 から順にしてください（{i + 1} 番目が {holes[i].Number}）。"); }
            }

            return new Course(holes);
        }

        /// <summary>ホールを数に依らず読む（テストの小さなホール用）。</summary>
        public static List<Hole> ParseHoles(string text, GolfConfig config)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            var holes = new List<Hole>();
            Draft? draft = null;
            var lineNumber = 0;
            foreach (var rawLine in text.Split('\n'))
            {
                lineNumber++;
                var comment = rawLine.IndexOf('#');
                var line = (comment >= 0 ? rawLine.Substring(0, comment) : rawLine).Trim();
                if (line.Length == 0) { continue; }
                var words = line.Split(_separators, StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    if (words[0] == "hole")
                    {
                        if (draft != null) { holes.Add(draft.Build(config)); }
                        draft = Draft.Begin(words);
                        continue;
                    }

                    if (draft == null) { throw new FormatException("hole の行より前に中身があります。"); }
                    draft.Read(words);
                }
                catch (FormatException error)
                {
                    throw new FormatException($"{lineNumber} 行目：{error.Message}", error);
                }
            }

            if (draft != null) { holes.Add(draft.Build(config)); }
            return holes;
        }

        /// <summary>m の小数を µm の整数へ（10 µm 未満の桁は書かない前提で、四捨五入）。</summary>
        internal static int Micro(string word)
        {
            if (!decimal.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var meters)) { throw new FormatException($"数ではありません：{word}"); }
            if (meters < -10_000m || meters > 10_000m) { throw new FormatException($"範囲外の長さです：{word}"); }
            return (int)Math.Round(meters * Units.PerMeter, MidpointRounding.AwayFromZero);
        }

        /// <summary>m の小数を dm（0.1 m）の整数へ（図形の内外の判定用）。</summary>
        internal static long Deci(string word) => (long)Math.Round(Micro(word) / 100_000m, MidpointRounding.AwayFromZero);

        internal static int Integer(string word)
        {
            if (!int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) { throw new FormatException($"整数ではありません：{word}"); }
            return value;
        }

        /// <summary>グリーン以外のマスに傾きが残っていたら拒否する（塗り直しで消したあとの不変条件）。</summary>
        internal static void RequireFlatOffTheGreen(Surface[] cells, short[] gradeX, short[] gradeY)
        {
            for (var i = 0; i < cells.Length; i++)
            {
                if (cells[i] != Surface.Green && (gradeX[i] != 0 || gradeY[i] != 0))
                {
                    throw new FormatException("グリーン以外のマスに傾きが残っています。");
                }
            }
        }

        private static Surface SurfaceNamed(string word) => word switch
        {
            "rough" => Surface.Rough,
            "fairway" => Surface.Fairway,
            "tee" => Surface.Tee,
            "green" => Surface.Green,
            "bunker" => Surface.Bunker,
            "water" => Surface.Water,
            "ob" => Surface.OutOfBounds,
            _ => throw new FormatException($"未定義の地面です：{word}"),
        };

        /// <summary>組み立て中のホール。</summary>
        private sealed class Draft
        {
            private readonly int _number;
            private readonly int _par;
            private readonly int _width;
            private readonly int _height;
            private readonly Surface[] _cells;
            private readonly short[] _gradeX;
            private readonly short[] _gradeY;
            private readonly List<Tree> _trees = new();
            private Vec2i? _tee;
            private Vec2i? _cup;

            private Draft(int number, int par, int width, int height)
            {
                _number = number;
                _par = par;
                _width = width;
                _height = height;
                _cells = new Surface[width * height];
                _gradeX = new short[width * height];
                _gradeY = new short[width * height];
            }

            public static Draft Begin(string[] words)
            {
                Expect(words, 7);
                if (words[2] != "par" || words[4] != "size") { throw new FormatException("hole 番号 par 打数 size 奥行き 横幅 の形にしてください。"); }
                var number = Integer(words[1]);
                var par = Integer(words[3]);
                var width = Integer(words[5]);
                var height = Integer(words[6]);
                if (number < 1 || number > 99) { throw new FormatException($"ホールの番号は 1〜99 にしてください：{number}"); }
                if (par < Hole.MinPar || par > Hole.MaxPar) { throw new FormatException($"par は {Hole.MinPar}〜{Hole.MaxPar} にしてください：{par}"); }
                if (width < Hole.MinWidth || width > Hole.MaxWidth) { throw new FormatException($"奥行きは {Hole.MinWidth}〜{Hole.MaxWidth} m にしてください：{width}"); }
                if (height < Hole.MinHeight || height > Hole.MaxHeight) { throw new FormatException($"横幅は {Hole.MinHeight}〜{Hole.MaxHeight} m にしてください：{height}"); }
                return new Draft(number, par, width, height);
            }

            public void Read(string[] words)
            {
                switch (words[0])
                {
                    case "paint":
                        Paint(words);
                        break;
                    case "tee":
                        Expect(words, 3);
                        if (_tee != null) { throw new FormatException("tee が 2 つあります。"); }
                        _tee = new Vec2i(Micro(words[1]), Micro(words[2]));
                        break;
                    case "cup":
                        Expect(words, 3);
                        if (_cup != null) { throw new FormatException("cup が 2 つあります。"); }
                        _cup = new Vec2i(Micro(words[1]), Micro(words[2]));
                        break;
                    case "tree":
                        Expect(words, 5);
                        var tree = new Tree(new Vec2i(Micro(words[1]), Micro(words[2])), Micro(words[3]), Micro(words[4]));
                        if (tree.Radius < MinTreeRadius || tree.Radius > MaxTreeRadius) { throw new FormatException($"木の半径は 0.3〜4 m にしてください：{words[3]}"); }
                        if (tree.Height < MinTreeHeight || tree.Height > MaxTreeHeight) { throw new FormatException($"木の高さは 1〜30 m にしてください：{words[4]}"); }
                        _trees.Add(tree);
                        break;
                    case "slope":
                        Slope(words);
                        break;
                    default:
                        throw new FormatException($"未定義の行です：{words[0]}");
                }
            }

            public Hole Build(GolfConfig config)
            {
                if (_tee == null) { throw new FormatException($"{_number} 番ホールに tee がありません。"); }
                if (_cup == null) { throw new FormatException($"{_number} 番ホールに cup がありません。"); }
                var tee = _tee.Value;
                var cup = _cup.Value;
                var hole = new Hole(_number, _par, _width, _height, _cells, _gradeX, _gradeY, tee, cup, _trees);
                if (hole.SurfaceAt(tee) != Surface.Tee) { throw new FormatException($"{_number} 番ホールの tee がティーのマスにありません。"); }
                if (hole.SurfaceAt(cup) != Surface.Green) { throw new FormatException($"{_number} 番ホールの cup がグリーンのマスにありません。"); }

                // カップの円がグリーンからはみ出さない（カップの周りの 8 方向もグリーン）。
                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        var edge = new Vec2i(cup.X + (dx * config.CupRadius), cup.Y + (dy * config.CupRadius));
                        if (hole.SurfaceAt(edge) != Surface.Green) { throw new FormatException($"{_number} 番ホールのカップがグリーンの端にかかっています。"); }
                    }
                }

                if (Vec2i.DistanceSquared(tee, cup) < 50L * Units.PerMeter * 50 * Units.PerMeter)
                {
                    throw new FormatException($"{_number} 番ホールの tee と cup は 50 m 以上離してください。");
                }

                if (_trees.Count > BallSim.MaxTrees)
                {
                    throw new FormatException($"{_number} 番ホールの木は {BallSim.MaxTrees} 本までです：{_trees.Count}");
                }

                Course.RequireFlatOffTheGreen(_cells, _gradeX, _gradeY);
                foreach (var tree in _trees)
                {
                    if (hole.SurfaceAt(tree.Center) == Surface.Green) { throw new FormatException($"{_number} 番ホールの木がグリーンの上にあります。"); }
                    if (hole.SurfaceAt(tree.Center) == Surface.OutOfBounds) { throw new FormatException($"{_number} 番ホールの木が OB の上にあります。"); }
                    if (Near(tree.Center, tee, tree.Radius + (3L * Units.PerMeter))) { throw new FormatException($"{_number} 番ホールの木が tee に近すぎます（3 m 以上離す）。"); }
                    if (Near(tree.Center, cup, tree.Radius + (3L * Units.PerMeter))) { throw new FormatException($"{_number} 番ホールの木が cup に近すぎます（3 m 以上離す）。"); }
                }

                return hole;
            }

            private static bool Near(Vec2i a, Vec2i b, long distance) => Vec2i.DistanceSquared(a, b) < distance * distance;

            private void Paint(string[] words)
            {
                if (words.Length < 3) { throw new FormatException("paint 種類 図形 … の形にしてください。"); }
                var surface = SurfaceNamed(words[1]);
                Func<long, long, bool> inside;
                switch (words[2])
                {
                    case "rect":
                        {
                            Expect(words, 7);
                            var x0 = Deci(words[3]);
                            var y0 = Deci(words[4]);
                            var x1 = Deci(words[5]);
                            var y1 = Deci(words[6]);
                            if (x1 < x0 || y1 < y0) { throw new FormatException("rect は x0 ≤ x1、y0 ≤ y1 にしてください。"); }
                            inside = (x, y) => x >= x0 && x <= x1 && y >= y0 && y <= y1;
                            break;
                        }

                    case "ellipse":
                        {
                            Expect(words, 7);
                            var cx = Deci(words[3]);
                            var cy = Deci(words[4]);
                            var rx = Deci(words[5]);
                            var ry = Deci(words[6]);
                            if (rx <= 0 || ry <= 0) { throw new FormatException("ellipse の半径は正にしてください。"); }
                            inside = (x, y) => ((x - cx) * (x - cx) * ry * ry) + ((y - cy) * (y - cy) * rx * rx) <= rx * rx * ry * ry;
                            break;
                        }

                    case "capsule":
                        {
                            Expect(words, 8);
                            var x0 = Deci(words[3]);
                            var y0 = Deci(words[4]);
                            var x1 = Deci(words[5]);
                            var y1 = Deci(words[6]);
                            var r = Deci(words[7]);
                            if (r <= 0) { throw new FormatException("capsule の半径は正にしてください。"); }
                            inside = (x, y) => SegmentDistanceSquaredAtMost(x, y, x0, y0, x1, y1, r * r);
                            break;
                        }

                    default:
                        throw new FormatException($"未定義の図形です：{words[2]}");
                }

                for (var y = 0; y < _height; y++)
                {
                    for (var x = 0; x < _width; x++)
                    {
                        // マスの中心（dm）。グリーン以外へ塗り直したら、そのマスの傾きは消す（slope は塗った時点のグリーンにだけ残る）。
                        if (!inside((x * 10L) + 5, (y * 10L) + 5)) { continue; }
                        var index = (y * _width) + x;
                        _cells[index] = surface;
                        if (surface != Surface.Green)
                        {
                            _gradeX[index] = 0;
                            _gradeY[index] = 0;
                        }
                    }
                }
            }

            private void Slope(string[] words)
            {
                Expect(words, 7);
                var x0 = Deci(words[1]);
                var y0 = Deci(words[2]);
                var x1 = Deci(words[3]);
                var y1 = Deci(words[4]);
                var gx = Integer(words[5]);
                var gy = Integer(words[6]);
                if (x1 < x0 || y1 < y0) { throw new FormatException("slope は x0 ≤ x1、y0 ≤ y1 にしてください。"); }
                if (((long)gx * gx) + ((long)gy * gy) > (long)Hole.MaxGrade * Hole.MaxGrade) { throw new FormatException($"傾きの大きさは {Hole.MaxGrade}‰ 以下にしてください。"); }
                var applied = 0;
                for (var y = 0; y < _height; y++)
                {
                    for (var x = 0; x < _width; x++)
                    {
                        var cx = (x * 10L) + 5;
                        var cy = (y * 10L) + 5;
                        var index = (y * _width) + x;
                        if (cx < x0 || cx > x1 || cy < y0 || cy > y1 || _cells[index] != Surface.Green) { continue; }
                        _gradeX[index] = (short)gx;
                        _gradeY[index] = (short)gy;
                        applied++;
                    }
                }

                if (applied == 0) { throw new FormatException("slope の矩形にグリーンのマスがありません（slope は塗りの後に書く）。"); }
            }

            private static bool SegmentDistanceSquaredAtMost(long x, long y, long x0, long y0, long x1, long y1, long limit)
            {
                var dx = x1 - x0;
                var dy = y1 - y0;
                var length = (dx * dx) + (dy * dy);
                var px = x - x0;
                var py = y - y0;
                var t = (px * dx) + (py * dy);
                if (length == 0 || t <= 0) { return (px * px) + (py * py) <= limit; }
                if (t >= length) { return ((x - x1) * (x - x1)) + ((y - y1) * (y - y1)) <= limit; }

                // 線分への垂線の長さの 2 乗 = (外積)² / 長さ²。
                var cross = (px * dy) - (py * dx);
                return (cross * cross) <= limit * length;
            }

            private static void Expect(string[] words, int count)
            {
                if (words.Length != count) { throw new FormatException($"{words[0]} の値の数が違います（{count - 1} 個）。"); }
            }
        }
    }
}
