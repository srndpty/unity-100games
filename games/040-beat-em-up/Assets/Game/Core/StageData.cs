using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Core
{
    public enum SpawnSide
    {
        Left,
        Right,
    }

    /// <summary>波の 1 体。区画に入ってから Delay Tick 以後、空きがあれば出る。</summary>
    public readonly struct WaveEntry : IEquatable<WaveEntry>
    {
        public WaveEntry(ActorKind kind, SpawnSide side, int z, int delay, bool food = false)
        {
            Kind = kind;
            Side = side;
            Z = z;
            Delay = delay;
            Food = food;
        }

        /// <summary>倒れて消えると回復アイテムを落とす。</summary>
        public bool Food { get; }

        public ActorKind Kind { get; }

        public SpawnSide Side { get; }

        public int Z { get; }

        public int Delay { get; }

        public bool Equals(WaveEntry other) => Kind == other.Kind && Side == other.Side && Z == other.Z && Delay == other.Delay && Food == other.Food;

        public override bool Equals(object? obj) => obj is WaveEntry other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Side, Z, Delay, Food);

        public static bool operator ==(WaveEntry left, WaveEntry right) => left.Equals(right);

        public static bool operator !=(WaveEntry left, WaveEntry right) => !left.Equals(right);
    }

    /// <summary>画面を止めて戦う区画。CameraX は止まったときの画面の左端。</summary>
    public sealed class Section
    {
        private readonly WaveEntry[] _waves;

        internal Section(int cameraX, WaveEntry[] waves)
        {
            CameraX = cameraX;
            _waves = waves;
            Waves = Array.AsReadOnly(waves);
        }

        public int CameraX { get; }

        /// <summary>毎 Tick 参照するので、包みは作ったときに 1 回だけ作る。</summary>
        public IReadOnlyList<WaveEntry> Waves { get; }

        public bool HasBoss
        {
            get
            {
                for (int i = 0; i < _waves.Length; i++)
                {
                    if (_waves[i].Kind == ActorKind.Boss) { return true; }
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Stage.txt の読み込みと検証。区画は左から順に並び、最後の区画にだけボスが 1 体いる。
    /// 闘技場（<see cref="Arena"/>）は波の無い 1 区画で、テストと検証の場面に使う（終わらない）。
    /// </summary>
    public sealed class StageData
    {
        private readonly Section[] _sections;

        private StageData(int startX, Section[] sections, bool isArena)
        {
            StartX = startX;
            _sections = sections;
            Sections = Array.AsReadOnly(sections);
            IsArena = isArena;
        }

        /// <summary>最初の区画の画面の左端からのプレイヤーの位置。</summary>
        public int StartX { get; }

        public bool IsArena { get; }

        public IReadOnlyList<Section> Sections { get; }

        public int MaxWaveCount
        {
            get
            {
                int max = 0;
                for (int i = 0; i < _sections.Length; i++) { max = Math.Max(max, _sections[i].Waves.Count); }
                return Math.Max(1, max);
            }
        }

        public static StageData Arena(int startX = 96 * Px.One) =>
            new(startX, new[] { new Section(0, Array.Empty<WaveEntry>()) }, true);

        public static StageData Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var lines = text.Replace("\r", string.Empty).Split('\n');
            int startX = -1;
            var sections = new List<Section>();
            int cameraX = 0;
            List<WaveEntry>? waves = null;

            void Flush()
            {
                if (waves == null) { return; }
                sections.Add(new Section(cameraX, waves.ToArray()));
                waves = null;
            }

            for (int lineNo = 1; lineNo <= lines.Length; lineNo++)
            {
                var raw = lines[lineNo - 1];
                int comment = raw.IndexOf('#', StringComparison.Ordinal);
                if (comment >= 0) { raw = raw.Substring(0, comment); }
                raw = raw.Trim();
                if (raw.Length == 0) { continue; }
                var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0])
                {
                    case "start":
                        if (parts.Length != 2 || startX >= 0) { throw new FormatException($"{lineNo} 行目: start は 1 回、px で書きます。"); }
                        startX = Int(parts, 1, lineNo) * Px.One;
                        break;
                    case "section":
                        Flush();
                        if (parts.Length != 2) { throw new FormatException($"{lineNo} 行目: section には画面の左端（px）が必要です。"); }
                        cameraX = Int(parts, 1, lineNo) * Px.One;
                        waves = new List<WaveEntry>();
                        break;
                    case "wave":
                        if (waves == null) { throw new FormatException($"{lineNo} 行目: wave は section の中です。"); }
                        if (parts.Length != 5 && parts.Length != 6) { throw new FormatException($"{lineNo} 行目: wave は 種類 側 z(px) 遅れ(Tick) [food] です。"); }
                        if (parts.Length == 6 && parts[5] != "food") { throw new FormatException($"{lineNo} 行目: wave の最後は food だけ書けます。"); }
                        waves.Add(new WaveEntry(ParseKind(parts[1], lineNo), ParseSide(parts[2], lineNo), Int(parts, 3, lineNo) * Px.One, Int(parts, 4, lineNo), parts.Length == 6));
                        break;
                    default:
                        throw new FormatException($"{lineNo} 行目: 未知の項目 {parts[0]} です。");
                }
            }

            Flush();
            if (startX < 0) { throw new FormatException("start がありません。"); }
            if (sections.Count == 0) { throw new FormatException("区画がありません。"); }
            return new StageData(startX, sections.ToArray(), false);
        }

        /// <summary>設定と照らし合わせた検証。区画が帯・画面に収まるか、ボスの置き方、波の遅れの順。</summary>
        public void Check(MoveTable table, BrawlConfig config)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (StartX < config.EdgeMargin || StartX > config.ViewWidth - config.EdgeMargin)
            {
                throw new FormatException("start が画面の外です。");
            }

            if (IsArena) { return; }
            int bosses = 0;
            int foods = 0;
            for (int s = 0; s < _sections.Length; s++)
            {
                var section = _sections[s];
                if (section.Waves.Count == 0) { throw new FormatException($"区画 {s + 1} に敵がいません。"); }
                if (s > 0 && section.CameraX <= _sections[s - 1].CameraX) { throw new FormatException($"区画 {s + 1} は前の区画より右に置いてください。"); }
                if (section.CameraX < 0) { throw new FormatException($"区画 {s + 1} の位置が負です。"); }
                int lastDelay = 0;
                for (int w = 0; w < section.Waves.Count; w++)
                {
                    var wave = section.Waves[w];
                    if (wave.Z < config.ZMin || wave.Z > config.ZMax) { throw new FormatException($"区画 {s + 1} の {w + 1} 体目の z が帯の外です。"); }
                    if (wave.Delay < lastDelay) { throw new FormatException($"区画 {s + 1} の波は遅れの順に並べてください。"); }
                    if (wave.Kind == ActorKind.Player) { throw new FormatException("プレイヤーは波に置けません。"); }
                    if (wave.Kind == ActorKind.Boss) { bosses++; }
                    if (wave.Food) { foods++; }
                    lastDelay = wave.Delay;
                }

                if (section.HasBoss && s != _sections.Length - 1) { throw new FormatException("ボスは最後の区画にだけ置けます。"); }
            }

            if (bosses != 1) { throw new FormatException("ボスは 1 体にしてください。"); }
            if (foods > Brawl.ItemCapacity) { throw new FormatException($"回復アイテムを落とす敵は {Brawl.ItemCapacity} 体までです。"); }
        }

        private static int Int(string[] parts, int index, int lineNo)
        {
            if (index >= parts.Length || !int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"{lineNo} 行目: 整数が必要です。");
            }

            if (value < -100_000 || value > 100_000) { throw new FormatException($"{lineNo} 行目: 値が大きすぎます。"); }
            return value;
        }

        private static ActorKind ParseKind(string text, int lineNo)
        {
            switch (text)
            {
                case "grunt": return ActorKind.Grunt;
                case "brute": return ActorKind.Brute;
                case "kicker": return ActorKind.Kicker;
                case "boss": return ActorKind.Boss;
                default: throw new FormatException($"{lineNo} 行目: 未知の敵 {text} です。");
            }
        }

        private static SpawnSide ParseSide(string text, int lineNo)
        {
            switch (text)
            {
                case "left": return SpawnSide.Left;
                case "right": return SpawnSide.Right;
                default: throw new FormatException($"{lineNo} 行目: 側は left か right です。");
            }
        }
    }
}
