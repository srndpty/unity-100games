using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// コースの地面を 1 つのメッシュにする。三角形の順に重ねて描く：外の芝、ランオフの芝、ガード、ターマック、縁石、白線、スタートライン。
    /// 位置は全て Core の <see cref="Track"/> の点から <see cref="ThistleLayout"/> の式で作る（ターマックの端は Left/Right、ガードは GuardLeft/GuardRight）。
    /// シーン生成が保存したメッシュと、この関数の結果を <see cref="ThistleStage.Mismatch"/> が照合する。
    /// UV の x は路面の粒の強さ（<c>Thistle/Ground</c> シェーダー）。
    /// </summary>
    public static class ThistleTrackMesh
    {
        /// <summary>ガードの外の帯の幅（m）。</summary>
        public const float BarrierWidth = 0.7f;

        /// <summary>縁石の幅（m、ターマックの端から外へ）。</summary>
        public const float CurbWidth = 0.9f;

        /// <summary>この角度（Trig）より曲がる点の前後に縁石を置く。</summary>
        public const int CurbTurn = 500;

        private const float FieldMargin = 60f;
        private const float LineWidth = 0.22f;
        private const float LineInset = 0.35f;
        private const int CheckerColumns = 14;
        private const float CheckerRow = 0.45f;

        public static Mesh Build(Track track)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            Write(track, vertices, colors, uvs, triangles);
            var mesh = new Mesh { name = "Circuit" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static void Write(Track track, List<Vector3> vertices, List<Color> colors, List<Vector2> uvs, List<int> triangles)
        {
            if (track == null) { throw new ArgumentNullException(nameof(track)); }
            var builder = new Builder(vertices, colors, uvs, triangles);
            var count = track.StationCount;

            Bounds(track, out var min, out var max);
            builder.Quad(
                new Vector3(min.x - FieldMargin, min.y - FieldMargin), new Vector3(max.x + FieldMargin, min.y - FieldMargin),
                new Vector3(max.x + FieldMargin, max.y + FieldMargin), new Vector3(min.x - FieldMargin, max.y + FieldMargin),
                ThistlePalette.Field, 0.55f);

            for (var i = 0; i < count; i++)
            {
                var n = (i + 1) % count;
                builder.Quad(Side(track, i, 1, track.HalfWidth(i)), Side(track, n, 1, track.HalfWidth(n)), Side(track, n, 1, Guard(track, n)), Side(track, i, 1, Guard(track, i)), ThistlePalette.Runoff, 0.5f);
                builder.Quad(Side(track, i, -1, Guard(track, i)), Side(track, n, -1, Guard(track, n)), Side(track, n, -1, track.HalfWidth(n)), Side(track, i, -1, track.HalfWidth(i)), ThistlePalette.Runoff, 0.5f);
            }

            for (var i = 0; i < count; i++)
            {
                var n = (i + 1) % count;
                var color = (i % 2) == 0 ? ThistlePalette.BarrierA : ThistlePalette.BarrierB;
                foreach (var side in new[] { 1, -1 })
                {
                    var inner = Guard(track, i);
                    var innerNext = Guard(track, n);
                    var outer = inner + (BarrierWidth * Units.MillimetersPerMeter);
                    var outerNext = innerNext + (BarrierWidth * Units.MillimetersPerMeter);
                    builder.Quad(Side(track, i, side, inner), Side(track, n, side, innerNext), Side(track, n, side, outerNext), Side(track, i, side, outer), color, 0.15f);
                }
            }

            for (var i = 0; i < count; i++)
            {
                var n = (i + 1) % count;
                builder.Quad(Side(track, i, -1, track.HalfWidth(i)), Side(track, n, -1, track.HalfWidth(n)), Side(track, n, 1, track.HalfWidth(n)), Side(track, i, 1, track.HalfWidth(i)), ThistlePalette.Asphalt, 0.35f);
            }

            for (var i = 0; i < count; i++)
            {
                var n = (i + 1) % count;
                var curb = HasCurb(track, i);
                foreach (var side in new[] { 1, -1 })
                {
                    if (curb)
                    {
                        const int Blocks = 4;
                        for (var b = 0; b < Blocks; b++)
                        {
                            var from = b / (float)Blocks;
                            var to = (b + 1) / (float)Blocks;
                            var color = (b % 2) == 0 ? ThistlePalette.CurbRed : ThistlePalette.CurbWhite;
                            var a0 = Along(track, i, n, side, track.HalfWidth(i), track.HalfWidth(n), from);
                            var a1 = Along(track, i, n, side, track.HalfWidth(i), track.HalfWidth(n), to);
                            var b0 = Along(track, i, n, side, track.HalfWidth(i) + Curb(), track.HalfWidth(n) + Curb(), from);
                            var b1 = Along(track, i, n, side, track.HalfWidth(i) + Curb(), track.HalfWidth(n) + Curb(), to);
                            builder.Quad(a0, a1, b1, b0, color, 0.1f);
                        }
                    }
                    else
                    {
                        var inner = track.HalfWidth(i) - (int)((LineInset + LineWidth) * Units.MillimetersPerMeter);
                        var innerNext = track.HalfWidth(n) - (int)((LineInset + LineWidth) * Units.MillimetersPerMeter);
                        var outer = track.HalfWidth(i) - (int)(LineInset * Units.MillimetersPerMeter);
                        var outerNext = track.HalfWidth(n) - (int)(LineInset * Units.MillimetersPerMeter);
                        builder.Quad(Side(track, i, side, inner), Side(track, n, side, innerNext), Side(track, n, side, outerNext), Side(track, i, side, outer), ThistlePalette.Line, 0.05f);
                    }
                }
            }

            StartLine(track, builder);
        }

        /// <summary>点 <paramref name="station"/> の前後どちらかの区間がこの角度以上曲がっていれば、その区間に縁石を置く。</summary>
        public static bool HasCurb(Track track, int station)
        {
            var count = track.StationCount;
            var here = Turn(track, station);
            var next = Turn(track, (station + 1) % count);
            return Math.Abs(here) >= CurbTurn || Math.Abs(next) >= CurbTurn;
        }

        /// <summary>コースのガードまで含む範囲（m）。</summary>
        public static void Bounds(Track track, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < track.StationCount; i++)
            {
                foreach (var point in new[] { track.GuardLeft(i), track.GuardRight(i) })
                {
                    var at = ThistleLayout.At(point);
                    min = Vector2.Min(min, at);
                    max = Vector2.Max(max, at);
                }
            }
        }

        private static int Curb() => (int)(CurbWidth * Units.MillimetersPerMeter);

        private static int Guard(Track track, int station) => track.HalfWidth(station) + track.Runoff;

        private static int Turn(Track track, int station)
        {
            var count = track.StationCount;
            var before = track.Direction((station + count - 1) % count);
            var after = track.Direction(station);
            var delta = Trig.Normalize(after - before);
            return delta > Trig.FullTurn / 2 ? delta - Trig.FullTurn : delta;
        }

        /// <summary>点の中心から、左（+1）か右（-1）の端の点の向きへ <paramref name="millimeters"/> 離れた点。端の点は Track が二等分線の向きに置いたもの。</summary>
        private static Vector3 Side(Track track, int station, int side, float millimeters)
        {
            var center = ThistleLayout.At(track.StationPosition(station));
            var edge = ThistleLayout.At(side > 0 ? track.Left(station) : track.Right(station));
            return center + ((edge - center) * (millimeters / track.HalfWidth(station)));
        }

        private static Vector3 Along(Track track, int station, int next, int side, float distance, float distanceNext, float t) =>
            Vector3.Lerp(Side(track, station, side, distance), Side(track, next, side, distanceNext), t);

        private static void StartLine(Track track, Builder builder)
        {
            var station = track.CheckpointStation(0);
            var left = ThistleLayout.At(track.Left(station));
            var right = ThistleLayout.At(track.Right(station));
            var heading = track.Direction(station);
            var forward = new Vector3(Trig.Cos(heading), Trig.Sin(heading), 0f) / Trig.One;
            for (var row = 0; row < 2; row++)
            {
                for (var column = 0; column < CheckerColumns; column++)
                {
                    var a = Vector3.Lerp(right, left, column / (float)CheckerColumns) + (forward * (row * CheckerRow));
                    var b = Vector3.Lerp(right, left, (column + 1) / (float)CheckerColumns) + (forward * (row * CheckerRow));
                    var color = ((row + column) % 2) == 0 ? ThistlePalette.Checker : ThistlePalette.CurbWhite;
                    builder.Quad(a, b, b + (forward * CheckerRow), a + (forward * CheckerRow), color, 0.05f);
                }
            }
        }

        /// <summary>頂点と三角形を足す。色は線形色空間なら線形へ直す（メッシュの頂点色は変換されない）。</summary>
        private sealed class Builder
        {
            private readonly List<Vector3> _vertices;
            private readonly List<Color> _colors;
            private readonly List<Vector2> _uvs;
            private readonly List<int> _triangles;
            private readonly bool _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            public Builder(List<Vector3> vertices, List<Color> colors, List<Vector2> uvs, List<int> triangles)
            {
                _vertices = vertices;
                _colors = colors;
                _uvs = uvs;
                _triangles = triangles;
            }

            /// <summary>4 点を順に結んだ四角形（a→b→c→d）。</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, float grain)
            {
                var start = _vertices.Count;
                var shown = _linear ? color.linear : color;
                foreach (var point in new[] { a, b, c, d })
                {
                    _vertices.Add(point);
                    _colors.Add(shown);
                    _uvs.Add(new Vector2(grain, 0f));
                }

                _triangles.Add(start);
                _triangles.Add(start + 1);
                _triangles.Add(start + 2);
                _triangles.Add(start);
                _triangles.Add(start + 2);
                _triangles.Add(start + 3);
            }
        }
    }
}
