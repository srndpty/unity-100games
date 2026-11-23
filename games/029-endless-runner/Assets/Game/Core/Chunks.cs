using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    public enum ObstacleKind
    {
        /// <summary>全高の壁。行いっぱいの奥行き。レーン移動で避ける。</summary>
        Wall,

        /// <summary>低い障害。ジャンプで越える。</summary>
        Hurdle,

        /// <summary>頭上の梁。スライドでくぐる。</summary>
        Beam,
    }

    /// <summary>チャンクの中の障害物 1 つ（行とレーン）。</summary>
    public readonly struct Obstacle : IEquatable<Obstacle>
    {
        public Obstacle(ObstacleKind kind, int lane, int row)
        {
            Kind = kind;
            Lane = lane;
            Row = row;
        }

        public ObstacleKind Kind { get; }

        public int Lane { get; }

        /// <summary>チャンクの入口から数えた行（0 が入口）。</summary>
        public int Row { get; }

        /// <summary>チャンクの入口を z = 0 とした当たり箱。</summary>
        public Box3 Box(RunConfig config)
        {
            var x = config.LaneX(Lane) - (config.LaneWidth / 2);
            var rowZ = Row * config.RowLength;
            var thinZ = rowZ + ((config.RowLength - config.ObstacleDepth) / 2);
            return Kind switch
            {
                ObstacleKind.Wall => new Box3(x, 0, rowZ, config.LaneWidth, config.WallHeight, config.RowLength),
                ObstacleKind.Hurdle => new Box3(x, 0, thinZ, config.LaneWidth, config.HurdleHeight, config.ObstacleDepth),
                ObstacleKind.Beam => new Box3(x, config.BeamBottom, thinZ, config.LaneWidth, config.WallHeight - config.BeamBottom, config.ObstacleDepth),
                _ => throw new InvalidOperationException($"未定義の障害物です：{Kind}"),
            };
        }

        public bool Equals(Obstacle other) => Kind == other.Kind && Lane == other.Lane && Row == other.Row;

        public override bool Equals(object? obj) => obj is Obstacle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Lane, Row);

        public override string ToString() => $"{Kind}(lane {Lane}, row {Row})";

        public static bool operator ==(Obstacle left, Obstacle right) => left.Equals(right);

        public static bool operator !=(Obstacle left, Obstacle right) => !left.Equals(right);
    }

    /// <summary>
    /// 文字の表で書いたチャンク。1 行 = 奥行き 1 区画、3 文字 = 左・中・右のレーン。
    /// <b>上の行ほど奥</b>（自機の後ろから見た並び）。<c>.</c> 空・<c>W</c> Wall・<c>H</c> Hurdle・<c>B</c> Beam。
    /// 入口と出口の行は空にする（隣のチャンクの障害物と同じ Tick に触れないため）。
    /// </summary>
    [Serializable]
    public sealed class ChunkSpec
    {
        public string Name = "";

        /// <summary>難度（1 以上）。解禁された難度以下のチャンクだけが出る。</summary>
        public int Tier = 1;

        /// <summary>出やすさ（1 以上）。</summary>
        public int Weight = 1;

        public string[] Rows = Array.Empty<string>();

        public ChunkSpec Copy()
        {
            var copy = (ChunkSpec)MemberwiseClone();
            copy.Rows = (string[])Rows.Clone();
            return copy;
        }
    }

    /// <summary>読み込んで検査したチャンク。</summary>
    public sealed class ChunkDef
    {
        /// <summary>1 チャンクの行数の上限。</summary>
        public const int MaxRows = 64;

        private ChunkDef(int id, string name, int tier, int weight, int rowCount, Obstacle[] obstacles)
        {
            Id = id;
            Name = name;
            Tier = tier;
            Weight = weight;
            RowCount = rowCount;
            Obstacles = Array.AsReadOnly(obstacles);
        }

        /// <summary>ライブラリの中の番号。</summary>
        public int Id { get; }

        public string Name { get; }

        public int Tier { get; }

        public int Weight { get; }

        public int RowCount { get; }

        /// <summary>入口に近い行から順に並べた障害物。</summary>
        public ReadOnlyCollection<Obstacle> Obstacles { get; }

        public int Length(RunConfig config) => RowCount * config.RowLength;

        /// <summary>文字の表を読む。不正な表は例外。</summary>
        public static ChunkDef Parse(int id, ChunkSpec spec, int maxTier)
        {
            if (spec == null) { throw new ArgumentNullException(nameof(spec)); }
            var name = string.IsNullOrEmpty(spec.Name) ? $"#{id}" : spec.Name;
            if (spec.Tier < 1 || spec.Tier > maxTier) { throw new ArgumentException($"チャンク {name}：難度 {spec.Tier} は 1〜{maxTier} にしてください。", nameof(spec)); }
            if (spec.Weight < 1 || spec.Weight > 1000) { throw new ArgumentException($"チャンク {name}：重み {spec.Weight} は 1〜1000 にしてください。", nameof(spec)); }
            var rows = spec.Rows ?? Array.Empty<string>();
            if (rows.Length < 3 || rows.Length > MaxRows) { throw new ArgumentException($"チャンク {name}：行数 {rows.Length} は 3〜{MaxRows} にしてください。", nameof(spec)); }

            var obstacles = new List<Obstacle>();
            for (var row = 0; row < rows.Length; row++)
            {
                // 上の行ほど奥なので、入口からの行番号は下から数える。
                var text = rows[rows.Length - 1 - row];
                if (text == null || text.Length != RunConfig.Lanes) { throw new ArgumentException($"チャンク {name}：行「{text}」は {RunConfig.Lanes} 文字にしてください。", nameof(spec)); }
                for (var lane = 0; lane < RunConfig.Lanes; lane++)
                {
                    var c = text[lane];
                    if (c == '.') { continue; }
                    var kind = c switch
                    {
                        'W' => ObstacleKind.Wall,
                        'H' => ObstacleKind.Hurdle,
                        'B' => ObstacleKind.Beam,
                        _ => throw new ArgumentException($"チャンク {name}：未定義の文字「{c}」です（. W H B）。", nameof(spec)),
                    };
                    if (row == 0 || row == rows.Length - 1) { throw new ArgumentException($"チャンク {name}：入口と出口の行は空にしてください。", nameof(spec)); }
                    obstacles.Add(new Obstacle(kind, lane, row));
                }
            }

            return new ChunkDef(id, name, spec.Tier, spec.Weight, rows.Length, obstacles.ToArray());
        }
    }
}
