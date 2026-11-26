using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TileMotion;

namespace Game.Core
{
    public enum SaveErrorKind
    {
        /// <summary>空・見出しがない。</summary>
        NotASave = 1,

        /// <summary>途中で切れている（行が足りない・順番が違う）。</summary>
        Truncated = 2,

        /// <summary>チェックサムが合わない（書き換え・破損）。</summary>
        Corrupted = 3,

        /// <summary>版が違う（移行はしない）。</summary>
        VersionMismatch = 4,

        /// <summary>データ（モンスター・規則・地図）が保存したときと違う。</summary>
        DataMismatch = 5,

        /// <summary>値が範囲の外・知らない ID・規則に合わない（手持ちの数など）。</summary>
        BadValue = 6,

        /// <summary>回復所の前でしか保存できない。</summary>
        NotAtCenter = 7,
    }

    public sealed class SaveError
    {
        internal SaveError(SaveErrorKind kind, string detail)
        {
            Kind = kind;
            Detail = detail;
        }

        public SaveErrorKind Kind { get; }

        public string Detail { get; }

        /// <summary>タイトルに出す案内。</summary>
        public string Message => Kind switch
        {
            SaveErrorKind.NotASave => "セーブデータではありません",
            SaveErrorKind.Truncated => "セーブデータが途中で切れています",
            SaveErrorKind.Corrupted => "セーブデータが壊れています",
            SaveErrorKind.VersionMismatch => "セーブデータの版が違います",
            SaveErrorKind.DataMismatch => "ゲームのデータが保存したときと違います",
            SaveErrorKind.NotAtCenter => "回復所でしか記録できません",
            _ => "セーブデータの値が正しくありません",
        };

        public override string ToString() => $"{Kind}: {Detail}";
    }

    /// <summary>
    /// 冒険の 1 枠のセーブ。行指向のテキスト（<c>key=value</c>、決まった順）で、最後の行に本文のチェックサムを置く。
    /// 読み込みは切れた・壊れた・版違い・データ違い・値域外を理由付きで拒否し、世界を作らない（移行はしない）。
    /// </summary>
    public static class SaveCodec
    {
        public const string Header = "THORNWILD-SAVE";
        public const int Version = 1;

        private static readonly string[] _keys =
        {
            "version", "fingerprint", "seed", "battles", "ticks", "wins", "losses", "map", "x", "y", "facing", "steps", "grace",
            "encounter_rng", "trainers", "seen", "caught", "balls", "potions",
        };

        public static bool TryEncode(World world, out string text, out SaveError? error)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            if (!world.AtCenter)
            {
                text = string.Empty;
                error = new SaveError(SaveErrorKind.NotAtCenter, "回復所の前ではありません");
                return false;
            }

            text = Encode(world);
            error = null;
            return true;
        }

        /// <summary>場所を確かめずに書く（テストで負例を作る）。</summary>
        internal static string Encode(World world)
        {
            var e = world.Explorer;
            var b = new StringBuilder();
            b.Append(Header).Append('\n');
            Line(b, "version", Version);
            Line(b, "fingerprint", world.Fingerprint);
            Line(b, "seed", world.Seed);
            Line(b, "battles", world.BattleCount);
            Line(b, "ticks", world.PlayTicks);
            Line(b, "wins", world.Wins);
            Line(b, "losses", world.Losses);
            b.Append("map=").Append(e.MapId).Append('\n');
            Line(b, "x", e.X);
            Line(b, "y", e.Y);
            Line(b, "facing", (int)e.Facing);
            Line(b, "steps", e.TotalSteps);
            Line(b, "grace", e.StepsSinceEncounter);
            Line(b, "encounter_rng", e.EncounterRng.State);
            Line(b, "trainers", world.TrainersDefeated);
            Line(b, "seen", world.Seen);
            Line(b, "caught", world.Caught);
            Line(b, "balls", world.Balls);
            Line(b, "potions", world.Potions);
            Line(b, "party", world.Roster.Party.Count);
            foreach (var m in world.Roster.Party) { b.Append("mon=").Append(Mon(m)).Append('\n'); }
            Line(b, "box", world.Roster.Box.Count);
            foreach (var m in world.Roster.Box) { b.Append("mon=").Append(Mon(m)).Append('\n'); }
            var body = b.ToString();
            return body + "checksum=" + Checksum(body).ToString("x8", CultureInfo.InvariantCulture) + "\n";
        }

        public static bool TryDecode(MonsterData data, string? text, out World? world, out SaveError? error)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            world = null;
            try
            {
                world = Decode(data, text ?? string.Empty);
                error = null;
                return true;
            }
            catch (SaveException ex)
            {
                error = ex.Error;
                return false;
            }
        }

        private static World Decode(MonsterData data, string text)
        {
            text = text.Replace("\r", string.Empty, StringComparison.Ordinal);
            if (!text.StartsWith(Header + "\n", StringComparison.Ordinal)) { throw Fail(SaveErrorKind.NotASave, "見出しがありません"); }
            var lines = text.Split('\n');
            var last = lines.Length - 1;
            if (lines[last].Length == 0) { last--; }
            if (last < 1 || !lines[last].StartsWith("checksum=", StringComparison.Ordinal)) { throw Fail(SaveErrorKind.Truncated, "チェックサムの行がありません"); }
            var body = string.Join("\n", lines, 0, last) + "\n";
            var sum = lines[last].Substring("checksum=".Length);
            if (!string.Equals(sum, Checksum(body).ToString("x8", CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw Fail(SaveErrorKind.Corrupted, "チェックサムが合いません");
            }

            var reader = new Reader(lines, 1, last);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in _keys) { values[key] = reader.Next(key); }

            var version = Int(values, "version", int.MinValue, int.MaxValue);
            if (version != Version) { throw Fail(SaveErrorKind.VersionMismatch, $"版 {version}"); }
            var fingerprint = UInt(values, "fingerprint");
            if (fingerprint != Fingerprint.Of(data)) { throw Fail(SaveErrorKind.DataMismatch, "データの指紋が違います"); }

            var rules = data.Rules;
            var seed = Int(values, "seed", int.MinValue, int.MaxValue);
            var battles = Int(values, "battles", 0, int.MaxValue);
            var ticks = Long(values, "ticks", 0, long.MaxValue);
            var wins = Int(values, "wins", 0, int.MaxValue);
            var losses = Int(values, "losses", 0, int.MaxValue);
            var mapId = values["map"];
            if (!data.TryGetMap(mapId, out _)) { throw Fail(SaveErrorKind.BadValue, $"地図 {mapId} がありません"); }
            var x = Int(values, "x", 0, 999);
            var y = Int(values, "y", 0, 999);
            var facing = Int(values, "facing", 0, 3);
            if (!string.Equals(mapId, rules.CenterMapId, StringComparison.Ordinal) || x != rules.CenterX || y != rules.CenterY || facing != (int)Direction.Up)
            {
                throw Fail(SaveErrorKind.BadValue, "回復所の前ではありません");
            }

            var steps = Int(values, "steps", 0, int.MaxValue);
            var grace = Int(values, "grace", 0, int.MaxValue);
            var rng = UInt(values, "encounter_rng");
            if (rng == 0) { throw Fail(SaveErrorKind.BadValue, "乱数の状態が 0 です"); }
            var allTrainers = (1 << data.Trainers.Count) - 1;
            var trainers = Int(values, "trainers", 0, allTrainers - 1);
            var allSpecies = (1 << data.Species.Count) - 1;
            var seen = Int(values, "seen", 0, allSpecies);
            var caught = Int(values, "caught", 1, allSpecies);
            if ((caught & ~seen) != 0) { throw Fail(SaveErrorKind.BadValue, "見ていない種族を捕まえています"); }
            var balls = Int(values, "balls", 0, 99);
            var potions = Int(values, "potions", 0, 99);

            var partyCount = IntValue("party", reader.Next("party"), 1, rules.PartyMax);
            var party = new List<Monster>();
            for (int i = 0; i < partyCount; i++) { party.Add(ParseMon(data, reader.Next("mon"))); }
            var boxCount = IntValue("box", reader.Next("box"), 0, rules.BoxMax);
            var box = new List<Monster>();
            for (int i = 0; i < boxCount; i++) { box.Add(ParseMon(data, reader.Next("mon"))); }
            reader.End();
            if (!party.Exists(m => !m.IsFainted)) { throw Fail(SaveErrorKind.BadValue, "手持ちに戦える個体がいません"); }
            foreach (var m in party)
            {
                if ((caught & (1 << m.Species.Index)) == 0) { throw Fail(SaveErrorKind.BadValue, $"{m.Species.Id} を捕まえた記録がありません"); }
            }

            foreach (var m in box)
            {
                if ((caught & (1 << m.Species.Index)) == 0) { throw Fail(SaveErrorKind.BadValue, $"{m.Species.Id} を捕まえた記録がありません"); }
            }

            var explorer = Explorer.Restore(mapId, x, y, (Direction)facing, grace, steps, rng);
            return World.Restore(data, seed, party, box, explorer, balls, potions, trainers, seen, caught, battles, ticks, wins, losses);
        }

        private static string Mon(Monster m)
        {
            var b = new StringBuilder();
            b.Append(m.Species.Id).Append(',').Append(m.Level).Append(',').Append(m.Exp).Append(',').Append(m.Hp).Append(',');
            for (int i = 0; i < m.Moves.Count; i++)
            {
                if (i > 0) { b.Append(';'); }
                b.Append(m.Moves[i].Move.Id).Append(':').Append(m.Moves[i].Pp);
            }

            return b.ToString();
        }

        private static Monster ParseMon(MonsterData data, string text)
        {
            var p = text.Split(',');
            if (p.Length != 5) { throw Fail(SaveErrorKind.BadValue, $"個体の項目の数が違います（{text}）"); }
            if (!data.TryGetSpecies(p[0], out var species)) { throw Fail(SaveErrorKind.BadValue, $"種族 {p[0]} がありません"); }
            var rules = data.Rules;
            var level = IntValue("level", p[1], 1, rules.LevelCap);
            var minExp = Formula.ExpForLevel(rules, level);
            var maxExp = level == rules.LevelCap ? minExp : Formula.ExpForLevel(rules, level + 1) - 1;
            var exp = IntValue("exp", p[2], minExp, maxExp);
            var hp = IntValue("hp", p[3], 0, Formula.MaxHp(species.BaseHp, level));
            var slots = new List<MoveSlot>();
            var moves = p[4].Split(';');
            if (moves.Length < 1 || moves.Length > MonsterDataValidator.MaxMoves) { throw Fail(SaveErrorKind.BadValue, "技の数が 1〜4 ではありません"); }
            foreach (var mv in moves)
            {
                var q = mv.Split(':');
                if (q.Length != 2 || !data.TryGetMove(q[0], out var move) || ReferenceEquals(move, rules.Struggle))
                {
                    throw Fail(SaveErrorKind.BadValue, $"技 {mv} は使えません");
                }

                if (slots.Exists(s => ReferenceEquals(s.Move, move))) { throw Fail(SaveErrorKind.BadValue, $"技 {q[0]} が重複しています"); }
                slots.Add(new MoveSlot(move, IntValue("pp", q[1], 0, move.Pp)));
            }

            return Monster.Restore(species, level, exp, hp, slots);
        }

        internal static uint Checksum(string body)
        {
            var h = StateHash.Start();
            foreach (var ch in body) { h.Mix(ch); }
            return h.Value;
        }

        private static void Line(StringBuilder b, string key, long value) =>
            b.Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');

        private static int Int(Dictionary<string, string> v, string key, int min, int max) => IntValue(key, v[key], min, max);

        private static int IntValue(string key, string s, int min, int max)
        {
            if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
            {
                throw Fail(SaveErrorKind.BadValue, $"{key} の値 {s} は {min}〜{max} ではありません");
            }

            return n;
        }

        private static long Long(Dictionary<string, string> v, string key, long min, long max)
        {
            if (!long.TryParse(v[key], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
            {
                throw Fail(SaveErrorKind.BadValue, $"{key} の値 {v[key]} が正しくありません");
            }

            return n;
        }

        private static uint UInt(Dictionary<string, string> v, string key)
        {
            if (!uint.TryParse(v[key], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                throw Fail(SaveErrorKind.BadValue, $"{key} の値 {v[key]} が正しくありません");
            }

            return n;
        }

        private static SaveException Fail(SaveErrorKind kind, string detail) => new(new SaveError(kind, detail));

        /// <summary>決まった順の行を読む。足りなければ途中で切れたとみなす。</summary>
        private sealed class Reader
        {
            private readonly string[] _lines;
            private readonly int _end;
            private int _next;

            public Reader(string[] lines, int start, int end)
            {
                _lines = lines;
                _next = start;
                _end = end;
            }

            public string Next(string key)
            {
                if (_next >= _end) { throw Fail(SaveErrorKind.Truncated, $"{key} の行がありません"); }
                var line = _lines[_next++];
                var prefix = key + "=";
                if (!line.StartsWith(prefix, StringComparison.Ordinal)) { throw Fail(SaveErrorKind.Truncated, $"{key} の行がありません（{line}）"); }
                return line.Substring(prefix.Length);
            }

            public void End()
            {
                if (_next != _end) { throw Fail(SaveErrorKind.BadValue, "余分な行があります"); }
            }
        }

        private sealed class SaveException : Exception
        {
            public SaveException(SaveError error)
                : base(error.ToString())
            {
                Error = error;
            }

            public SaveError Error { get; }
        }
    }
}
