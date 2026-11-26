using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using IntGeometry;

namespace Game.Core.Tests
{
    /// <summary>テストの道具。データは作品の Assets/Game/Data から読む。</summary>
    internal static class Lab
    {
        private static MonsterData? _data;

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static string ReadmeText([CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "README.md")));
        }

        public static MonsterData Data => _data ??= Load();

        public static MonsterData Load() => MonsterData.Load(
            DataText("Types.txt"),
            DataText("Statuses.txt"),
            DataText("Moves.txt"),
            DataText("Species.txt"),
            DataText("World.txt"));

        /// <summary>1 つのファイルの文字列を置き換えて読む（検証の負例）。</summary>
        public static MonsterData ParseWith(string file, string from, string to)
        {
            string Text(string name)
            {
                var t = DataText(name);
                if (name != file) { return t; }
                if (!t.Contains(from, StringComparison.Ordinal)) { throw new ArgumentException($"{file} に {from} がありません。"); }
                return t.Replace(from, to, StringComparison.Ordinal);
            }

            return MonsterData.Parse(Text("Types.txt"), Text("Statuses.txt"), Text("Moves.txt"), Text("Species.txt"), Text("World.txt"));
        }

        public static Monster Mon(string species, int level) => Monster.Create(Data.SpeciesOf(species), level, Data.Rules);

        /// <summary>技を指定した個体（PP は満タン）。</summary>
        public static Monster Mon(string species, int level, params string[] moves)
        {
            var m = Mon(species, level);
            return Monster.Restore(m.Species, m.Level, m.Exp, m.Hp, Slots(moves));
        }

        public static IEnumerable<MoveSlot> Slots(params string[] moves)
        {
            foreach (var id in moves)
            {
                var mv = Data.Move(id);
                yield return new MoveSlot(mv, mv.Pp);
            }
        }

        public static Battle Wild(Monster player, Monster wild, int seed = 1, int balls = 5, int potions = 3, bool hasRoom = true) =>
            Battle.Wild(Data, new[] { player }, wild, balls, potions, hasRoom, new RandomSeries(seed));

        public static Battle Wild(IEnumerable<Monster> party, Monster wild, int seed = 1, int balls = 5, int potions = 3, bool hasRoom = true) =>
            Battle.Wild(Data, party, wild, balls, potions, hasRoom, new RandomSeries(seed));

        public static Battle Trainer(IEnumerable<Monster> party, string trainer, int seed = 1) =>
            Battle.AgainstTrainer(Data, party, Data.Trainer(trainer), 5, 3, new RandomSeries(seed));

        /// <summary>条件を満たす乱数の状態を探す（命中・捕獲の境界を決まった値で確かめる）。</summary>
        public static uint StateWhere(Func<RandomSeries, bool> predicate)
        {
            for (uint s = 1; s < 2_000_000; s++)
            {
                if (predicate(RandomSeries.FromState(s))) { return s; }
            }

            throw new InvalidOperationException("条件に合う乱数の状態が見つかりません。");
        }

        public static int Count(IEnumerable<BattleEvent> events, BattleEventKind kind)
        {
            var n = 0;
            foreach (var e in events)
            {
                if (e.Kind == kind) { n++; }
            }

            return n;
        }

        public static BattleEvent? First(IEnumerable<BattleEvent> events, BattleEventKind kind, bool? player = null)
        {
            foreach (var e in events)
            {
                if (e.Kind == kind && (player == null || e.Player == player)) { return e; }
            }

            return null;
        }
    }
}
