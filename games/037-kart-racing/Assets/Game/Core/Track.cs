using System;
using System.Globalization;
using IntGeometry;

namespace Game.Core
{
    /// <summary>中心線上の位置（区間の番号と、その始点からの距離）。</summary>
    public readonly struct TrackPlace
    {
        public TrackPlace(int station, int along)
        {
            Station = station;
            Along = along;
        }

        public int Station { get; }

        public int Along { get; }
    }

    /// <summary>コースの壁の 1 本。内側の法線は長さ <c>Trig.One</c>。</summary>
    public readonly struct WallSeg
    {
        public WallSeg(Vec2i a, Vec2i b, Vec2i inward)
        {
            A = a;
            B = b;
            Inward = inward;
            MinX = Math.Min(a.X, b.X);
            MaxX = Math.Max(a.X, b.X);
            MinY = Math.Min(a.Y, b.Y);
            MaxY = Math.Max(a.Y, b.Y);
        }

        public Vec2i A { get; }

        public Vec2i B { get; }

        public Vec2i Inward { get; }

        public int MinX { get; }

        public int MaxX { get; }

        public int MinY { get; }

        public int MaxY { get; }
    }

    /// <summary>チェックポイントのゲート。A が左、B が右。<see cref="Ahead"/> は進行方向の側の符号。</summary>
    public readonly struct Gate
    {
        public Gate(Vec2i a, Vec2i b, int ahead, int station)
        {
            A = a;
            B = b;
            Ahead = ahead;
            Station = station;
        }

        public Vec2i A { get; }

        public Vec2i B { get; }

        public int Ahead { get; }

        public int Station { get; }
    }

    /// <summary>
    /// 閉じた中心線、ターマックの左右の端、その外の芝（ランオフ）の帯、帯の外側のガード壁、ゲート。点列から <see cref="Parse"/> が作る。
    /// 車はターマックの外も走れるが、ガード壁は越えられない。ゲートはガードからガードまで張るので、芝の上を通っても数える。
    /// 描画も当たりも、この点が返す左右の位置を使う。
    /// </summary>
    public sealed class Track
    {
        private readonly Vec2i[] _position;
        private readonly int[] _halfWidth;
        private readonly Vec2i[] _left;
        private readonly Vec2i[] _right;
        private readonly Vec2i[] _guardLeft;
        private readonly Vec2i[] _guardRight;
        private readonly int[] _length;
        private readonly int[] _checkpoints;

        private Track(Vec2i[] position, int[] halfWidth, int runoff, Vec2i[] left, Vec2i[] right, Vec2i[] guardLeft, Vec2i[] guardRight, int[] length, int[] checkpoints, WallSeg[] walls, Gate[] gates)
        {
            _position = position;
            _halfWidth = halfWidth;
            Runoff = runoff;
            _left = left;
            _right = right;
            _guardLeft = guardLeft;
            _guardRight = guardRight;
            _length = length;
            _checkpoints = checkpoints;
            Walls = walls;
            Gates = gates;
        }

        /// <summary>芝の幅の上限（mm）。</summary>
        public const int MaxRunoff = 20000;

        public int StationCount => _position.Length;

        public int CheckpointCount => _checkpoints.Length;

        /// <summary>ターマックの端からガード壁までの芝の幅（mm、全周で同じ）。</summary>
        public int Runoff { get; }

        public WallSeg[] Walls { get; }

        public Gate[] Gates { get; }

        public int TotalLength
        {
            get
            {
                var sum = 0;
                for (var i = 0; i < _length.Length; i++) { sum += _length[i]; }
                return sum;
            }
        }

        public Vec2i StationPosition(int station) => _position[station];

        public int HalfWidth(int station) => _halfWidth[station];

        public Vec2i Left(int station) => _left[station];

        public Vec2i Right(int station) => _right[station];

        /// <summary>左のガード壁の点（ターマックの端からさらに <see cref="Runoff"/> 外）。</summary>
        public Vec2i GuardLeft(int station) => _guardLeft[station];

        public Vec2i GuardRight(int station) => _guardRight[station];

        public int SegmentLength(int station) => _length[station];

        public int CheckpointStation(int gate) => _checkpoints[gate];

        public int Direction(int station)
        {
            var next = _position[(station + 1) % StationCount];
            var here = _position[station];
            return Trig.Atan2(next.Y - here.Y, next.X - here.X, 0);
        }

        /// <summary><paramref name="text"/> を読み、車の半径に対してコースが成り立つことを確かめる。</summary>
        public static Track Parse(string text, KartConfig config)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }

            var positions = new Vec2i[256];
            var widths = new int[256];
            var count = 0;
            var checkpointList = new int[32];
            var checkpointCount = 0;
            var runoff = -1;
            var lineNumber = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNumber++;
                var line = raw.Trim().TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') { continue; }
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts[0] == "station")
                {
                    if (parts.Length != 4) { throw Bad(lineNumber, "station は x y halfWidth です。"); }
                    if (count == positions.Length) { throw Bad(lineNumber, "点が多すぎます。"); }
                    positions[count] = new Vec2i(ParseInt(parts[1], lineNumber), ParseInt(parts[2], lineNumber));
                    widths[count] = ParseInt(parts[3], lineNumber);
                    count++;
                }
                else if (parts[0] == "checkpoint")
                {
                    if (parts.Length != 2) { throw Bad(lineNumber, "checkpoint は点の番号です。"); }
                    if (checkpointCount == checkpointList.Length) { throw Bad(lineNumber, "チェックポイントが多すぎます。"); }
                    checkpointList[checkpointCount++] = ParseInt(parts[1], lineNumber);
                }
                else if (parts[0] == "runoff")
                {
                    if (parts.Length != 2) { throw Bad(lineNumber, "runoff は芝の幅（mm）です。"); }
                    if (runoff >= 0) { throw Bad(lineNumber, "runoff は 1 行だけ書いてください。"); }
                    runoff = ParseInt(parts[1], lineNumber);
                    if (runoff < 0 || runoff > MaxRunoff) { throw Bad(lineNumber, $"runoff は 0〜{MaxRunoff} にしてください。"); }
                }
                else
                {
                    throw Bad(lineNumber, "未知の行です。");
                }
            }

            if (count < 4) { throw new FormatException("点は 4 個以上にしてください。"); }
            if (checkpointCount < 4) { throw new FormatException("チェックポイントは 4 個以上にしてください。"); }
            if (runoff < 0) { throw new FormatException("runoff（芝の幅）を書いてください。"); }

            var position = new Vec2i[count];
            var halfWidth = new int[count];
            Array.Copy(positions, position, count);
            Array.Copy(widths, halfWidth, count);
            var checkpoints = new int[checkpointCount];
            Array.Copy(checkpointList, checkpoints, checkpointCount);
            for (var i = 0; i < checkpointCount; i++)
            {
                if (i > 0 && checkpoints[i] <= checkpoints[i - 1])
                {
                    throw new FormatException("チェックポイントは中心線の順に並べてください。");
                }

                if (checkpoints[i] < 0 || checkpoints[i] >= count)
                {
                    throw new FormatException("チェックポイントの点の番号が範囲外です。");
                }
            }

            var left = new Vec2i[count];
            var right = new Vec2i[count];
            var guardLeft = new Vec2i[count];
            var guardRight = new Vec2i[count];
            var length = new int[count];
            for (var i = 0; i < count; i++)
            {
                if (halfWidth[i] < config.Radius * 2)
                {
                    throw new FormatException($"点 {i} の半幅が車の半径の 2 倍より小さいです。");
                }

                var previous = position[(i + count - 1) % count];
                var next = position[(i + 1) % count];
                var incoming = IntMath.Scale(position[i] - previous, 1000);
                var outgoing = IntMath.Scale(next - position[i], 1000);
                var bisector = new Vec2i(incoming.X + outgoing.X, incoming.Y + outgoing.Y);
                if (bisector.LengthSquared < 250000) { bisector = outgoing; }
                var leftDir = new Vec2i(-bisector.Y, bisector.X);
                left[i] = position[i] + IntMath.Scale(leftDir, halfWidth[i]);
                right[i] = position[i] + IntMath.Scale(-leftDir, halfWidth[i]);
                guardLeft[i] = position[i] + IntMath.Scale(leftDir, halfWidth[i] + runoff);
                guardRight[i] = position[i] + IntMath.Scale(-leftDir, halfWidth[i] + runoff);
                length[i] = (int)IntMath.Sqrt((next - position[i]).LengthSquared);
                if (length[i] < 500 || length[i] > Units.MaxSegment)
                {
                    throw new FormatException($"点 {i} からの区間の長さが 500〜{Units.MaxSegment} mm ではありません。");
                }
            }

            var walls = new WallSeg[count * 2];
            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                walls[i] = MakeWall(guardLeft[i], guardLeft[next], leftSide: true);
                walls[count + i] = MakeWall(guardRight[i], guardRight[next], leftSide: false);
                if (SegmentLength(walls[i]) > Units.MaxSegment || SegmentLength(walls[count + i]) > Units.MaxSegment)
                {
                    throw new FormatException($"点 {i} の壁が {Units.MaxSegment} mm を超えています。");
                }
            }

            EnsureClear(position, halfWidth);
            EnsureWallsDoNotCross(walls);
            var gates = new Gate[checkpointCount];
            for (var i = 0; i < checkpointCount; i++)
            {
                var station = checkpoints[i];
                var aheadPoint = position[station] + IntMath.Scale(new Vec2i(Trig.Cos(DirectionOf(position, station)), Trig.Sin(DirectionOf(position, station))), 1000);
                var a = guardLeft[station];
                var b = guardRight[station];
                var side = Geom.Sign(Geom.Cross(b - a, aheadPoint - a));
                if (side == 0) { throw new FormatException($"ゲート {i} の進行方向が決まりません。"); }
                var reach = halfWidth[station] + runoff;
                var span = (int)IntMath.Sqrt((b - a).LengthSquared);
                if (span < reach || span > reach * 3)
                {
                    throw new FormatException($"ゲート {i} がコースの幅をまたいでいません。");
                }

                gates[i] = new Gate(a, b, side, station);
            }

            return new Track(position, halfWidth, runoff, left, right, guardLeft, guardRight, length, checkpoints, walls, gates);
        }

        /// <summary>ヒントの近くだけを見て、点にいちばん近い中心線上の位置を返す。</summary>
        public TrackPlace Project(Vec2i point, int hint)
        {
            var bestStation = Geom.Mod(hint, StationCount);
            var bestDistance = long.MaxValue;
            var bestAlong = 0;
            for (var delta = -3; delta <= 4; delta++)
            {
                var station = Geom.Mod(hint + delta, StationCount);
                var along = Closest(point, station, out var distance);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestStation = station;
                    bestAlong = along;
                }
            }

            return new TrackPlace(bestStation, bestAlong);
        }

        /// <summary>
        /// 点がターマックの上か。ヒントの前後の区間の四角形（左右の端の点を結んだ、描画と同じ形）のどれかに入っていれば true。
        /// 四角形の辺の上はターマックとする。
        /// </summary>
        public bool OnTarmac(Vec2i point, int hint)
        {
            for (var delta = -2; delta <= 2; delta++)
            {
                var station = Geom.Mod(hint + delta, StationCount);
                var next = (station + 1) % StationCount;
                if (InsideQuad(point, _right[station], _right[next], _left[next], _left[station])) { return true; }
            }

            return false;
        }

        /// <summary>区間 <paramref name="station"/> の中心線から、左を正とした横の距離（mm）。</summary>
        public int LateralOf(Vec2i point, int station)
        {
            var a = _position[station];
            var b = _position[(station + 1) % StationCount];
            return (int)(Geom.Cross(b - a, point - a) / _length[station]);
        }

        /// <summary>中心線上の位置から左へ <paramref name="lateral"/> mm ずらした点（右は負）。</summary>
        public Vec2i Offset(TrackPlace place, int lateral)
        {
            var center = PointAt(place);
            if (lateral == 0) { return center; }
            var a = _position[place.Station];
            var b = _position[(place.Station + 1) % StationCount];
            var leftDir = new Vec2i(-(b.Y - a.Y), b.X - a.X);
            return center + IntMath.Scale(lateral > 0 ? leftDir : -leftDir, Math.Abs(lateral));
        }

        /// <summary>中心線に沿って <paramref name="distance"/> 先の位置。</summary>
        public TrackPlace Travel(TrackPlace place, int distance)
        {
            var station = place.Station;
            var along = place.Along;
            var left = distance;
            for (var guard = 0; left > 0 && guard < StationCount + 1; guard++)
            {
                var remain = _length[station] - along;
                if (left <= remain) { return new TrackPlace(station, along + left); }
                left -= remain;
                station = (station + 1) % StationCount;
                along = 0;
            }

            return new TrackPlace(station, along);
        }

        /// <summary>from から to へ中心線に沿って進んだ距離（mm）。逆走は負。半周を超える飛びは逆走とみなす。</summary>
        public int AdvanceOf(TrackPlace from, TrackPlace to)
        {
            var forward = ForwardDistance(from.Station, to.Station, to.Along) - from.Along;
            if (forward > (TotalLength / 2)) { forward -= TotalLength; }
            return forward;
        }

        public Vec2i PointAt(TrackPlace place)
        {
            var next = (place.Station + 1) % StationCount;
            var length = _length[place.Station];
            if (length <= 0) { return _position[place.Station]; }
            var along = place.Along;
            if (along < 0) { along = 0; }
            if (along > length) { along = length; }
            return new Vec2i(
                _position[place.Station].X + (int)(((long)(_position[next].X - _position[place.Station].X) * along) / length),
                _position[place.Station].Y + (int)(((long)(_position[next].Y - _position[place.Station].Y) * along) / length));
        }

        /// <summary>次のゲートまでの区間を、始点からどれだけ進んだか（0〜区間長）。始点より手前は 0。</summary>
        public int SectionProgress(TrackPlace place, int nextGate)
        {
            var start = _checkpoints[nextGate == 0 ? CheckpointCount - 1 : nextGate - 1];
            var end = _checkpoints[nextGate];
            var toEnd = ForwardSteps(start, end);
            var toPlace = ForwardSteps(start, place.Station);
            var length = ForwardDistance(start, end, 0);
            if (toPlace <= toEnd)
            {
                var along = ForwardDistance(start, place.Station, place.Along);
                return along > length ? length : along;
            }

            if (ForwardSteps(place.Station, start) <= Units.LocalStations + 2) { return 0; }
            return length;
        }

        /// <summary>ゲートを、手前から奥へ横切ったか。線の上は横切ったことにしない。</summary>
        public static bool ForwardCross(Gate gate, Vec2i from, Vec2i to)
        {
            var startSide = Geom.Sign(Geom.Cross(gate.B - gate.A, from - gate.A));
            var endSide = Geom.Sign(Geom.Cross(gate.B - gate.A, to - gate.A));
            if (startSide == 0 || endSide == 0 || startSide == endSide || endSide != gate.Ahead)
            {
                return false;
            }

            var gateA = Geom.Orient(from, to, gate.A);
            var gateB = Geom.Orient(from, to, gate.B);
            return gateA != 0 && gateB != 0 && gateA != gateB;
        }

        /// <summary>状態ハッシュ用に、点とゲートの番号を混ぜる。</summary>
        public void Mix(ref TileMotion.StateHash hash)
        {
            hash.Mix(StationCount);
            for (var i = 0; i < StationCount; i++)
            {
                hash.Mix(_position[i].X);
                hash.Mix(_position[i].Y);
                hash.Mix(_halfWidth[i]);
            }

            hash.Mix(Runoff);

            for (var i = 0; i < CheckpointCount; i++) { hash.Mix(_checkpoints[i]); }
        }

        private int Closest(Vec2i point, int station, out long distanceSquared)
        {
            var a = _position[station];
            var b = _position[(station + 1) % StationCount];
            long vx = b.X - a.X;
            long vy = b.Y - a.Y;
            var len2 = (vx * vx) + (vy * vy);
            long wx = point.X - a.X;
            long wy = point.Y - a.Y;
            if (len2 <= 0)
            {
                distanceSquared = (wx * wx) + (wy * wy);
                return 0;
            }

            var dot = (wx * vx) + (wy * vy);
            int along;
            long dx;
            long dy;
            if (dot <= 0)
            {
                along = 0;
                dx = wx;
                dy = wy;
            }
            else if (dot >= len2)
            {
                along = _length[station];
                dx = point.X - b.X;
                dy = point.Y - b.Y;
            }
            else
            {
                along = (int)(dot / IntMath.Sqrt(len2));
                var cross = (wx * vy) - (wy * vx);
                distanceSquared = (cross * cross) / len2;
                return along;
            }

            distanceSquared = (dx * dx) + (dy * dy);
            return along;
        }

        private static bool InsideQuad(Vec2i point, Vec2i a, Vec2i b, Vec2i c, Vec2i d) =>
            Geom.Orient(a, b, point) >= 0 && Geom.Orient(b, c, point) >= 0 && Geom.Orient(c, d, point) >= 0 && Geom.Orient(d, a, point) >= 0;

        private int ForwardSteps(int from, int to) => (to - from + StationCount) % StationCount;

        private int ForwardDistance(int from, int to, int extra)
        {
            var sum = extra;
            var steps = ForwardSteps(from, to);
            var station = from;
            for (var i = 0; i < steps; i++)
            {
                sum += _length[station];
                station = (station + 1) % StationCount;
            }

            return sum;
        }

        private static WallSeg MakeWall(Vec2i a, Vec2i b, bool leftSide)
        {
            var along = b - a;
            var inward = leftSide ? new Vec2i(along.Y, -along.X) : new Vec2i(-along.Y, along.X);
            return new WallSeg(a, b, IntMath.Scale(inward, Trig.One));
        }

        private static int SegmentLength(WallSeg wall) => (int)IntMath.Sqrt((wall.B - wall.A).LengthSquared);

        private static int DirectionOf(Vec2i[] position, int station)
        {
            var next = position[(station + 1) % position.Length];
            return Trig.Atan2(next.Y - position[station].Y, next.X - position[station].X, 0);
        }

        private static void EnsureClear(Vec2i[] position, int[] halfWidth)
        {
            var count = position.Length;
            for (var i = 0; i < count; i++)
            {
                var limit = halfWidth[i];
                for (var j = i + Units.LocalStations + 1; j < count; j++)
                {
                    var separation = Math.Min(Math.Abs(i - j), count - Math.Abs(i - j));
                    if (separation <= Units.LocalStations) { continue; }
                    var gap = SegmentGap(position[i], position[(i + 1) % count], position[j], position[(j + 1) % count]);
                    if (gap <= limit)
                    {
                        throw new FormatException($"点 {i} と {j} の区間が半幅以内まで近づいています。");
                    }
                }
            }
        }

        private static void EnsureWallsDoNotCross(WallSeg[] walls)
        {
            for (var i = 0; i < walls.Length; i++)
            {
                for (var j = i + 1; j < walls.Length; j++)
                {
                    if (SharesEnd(walls[i], walls[j])) { continue; }
                    if (ProperCross(walls[i].A, walls[i].B, walls[j].A, walls[j].B))
                    {
                        throw new FormatException("壁が交差しています。");
                    }
                }
            }
        }

        private static bool SharesEnd(WallSeg a, WallSeg b) => a.A == b.A || a.A == b.B || a.B == b.A || a.B == b.B;

        private static bool ProperCross(Vec2i a, Vec2i b, Vec2i c, Vec2i d)
        {
            var o1 = Geom.Orient(a, b, c);
            var o2 = Geom.Orient(a, b, d);
            var o3 = Geom.Orient(c, d, a);
            var o4 = Geom.Orient(c, d, b);
            return o1 != 0 && o2 != 0 && o3 != 0 && o4 != 0 && o1 != o2 && o3 != o4;
        }

        /// <summary>2 つの線分の距離（mm、切り捨て）。</summary>
        private static int SegmentGap(Vec2i a, Vec2i b, Vec2i c, Vec2i d)
        {
            var best = DistancePointSegment(a, c, d);
            best = Math.Min(best, DistancePointSegment(b, c, d));
            best = Math.Min(best, DistancePointSegment(c, a, b));
            best = Math.Min(best, DistancePointSegment(d, a, b));
            return best;
        }

        private static int DistancePointSegment(Vec2i point, Vec2i a, Vec2i b)
        {
            long vx = b.X - a.X;
            long vy = b.Y - a.Y;
            var len2 = (vx * vx) + (vy * vy);
            long wx = point.X - a.X;
            long wy = point.Y - a.Y;
            if (len2 <= 0) { return (int)IntMath.Sqrt((wx * wx) + (wy * wy)); }
            var dot = (wx * vx) + (wy * vy);
            if (dot <= 0) { return (int)IntMath.Sqrt((wx * wx) + (wy * wy)); }
            if (dot >= len2)
            {
                long ux = point.X - b.X;
                long uy = point.Y - b.Y;
                return (int)IntMath.Sqrt((ux * ux) + (uy * uy));
            }

            var cross = (wx * vy) - (wy * vx);
            return (int)IntMath.Sqrt((cross * cross) / len2);
        }

        private static int ParseInt(string text, int line)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw Bad(line, "整数ではありません。");
            }

            return value;
        }

        private static FormatException Bad(int line, string message) => new($"Track.txt の {line} 行目: {message}");
    }
}
