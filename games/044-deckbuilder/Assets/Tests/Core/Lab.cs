using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Game.Core.Tests
{
    /// <summary>テストの道具。データは作品の Assets/Game/Data から読む。</summary>
    internal static class Lab
    {
        private static GameData? _data;

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

        public static GameData Data => _data ??= GameData.Load(DataText("Cards.txt"), DataText("Enemies.txt"), DataText("Encounters.txt"), new DeckConfig());

        public static CardDef Card(string id) => Data.Card(id);

        /// <summary>テスト用の遭遇（敵の ID を並べる）。</summary>
        public static EncounterDef Encounter(params string[] foes) =>
            new(0, "test", "test", EncounterTier.Easy, foes.Select(f => Data.Enemy(f)).ToArray());

        /// <summary>カードの ID の列から実体（ID は 1 から）を作る。</summary>
        public static CardRef[] Deck(params string[] ids)
        {
            var deck = new CardRef[ids.Length];
            for (int i = 0; i < ids.Length; i++) { deck[i] = new CardRef(i + 1, Card(ids[i])); }
            return deck;
        }

        /// <summary>初期デッキ（ID は 1 から）。</summary>
        public static CardRef[] Starter() => Deck(Data.StarterDeck.Select(c => c.Id).ToArray());

        /// <summary>データの全 20 種を 1 枚ずつ。</summary>
        public static CardRef[] AllCards() => Deck(Data.Cards.Select(c => c.Id).ToArray());

        public static Battle NewBattle(CardRef[] deck, EncounterDef encounter, int seed = 1, DeckConfig? config = null, int hp = 70) =>
            new(Data, config ?? new DeckConfig(), deck, hp, 70, encounter, seed, 0);

        /// <summary>
        /// 手札・山札（先頭が一番上）・捨て札を空白区切りのカードの ID で決めた戦闘。実体の ID は手札・山札・捨て札の順に 1 から振る。
        /// </summary>
        public static Battle Arranged(EncounterDef encounter, string hand, string draw = "", string discard = "", int energy = 3, int seed = 1)
        {
            var h = Split(hand);
            var d = Split(draw);
            var x = Split(discard);
            var deck = Deck(h.Concat(d).Concat(x).ToArray());
            var battle = NewBattle(deck, encounter, seed);
            battle.Arrange(deck.Take(h.Length), deck.Skip(h.Length).Take(d.Length).Reverse(), deck.Skip(h.Length + d.Length), energy);
            return battle;
        }

        /// <summary>同じカードの ID を空白区切りで count 個。</summary>
        public static string Many(string id, int count) => string.Join(" ", Enumerable.Repeat(id, count));

        /// <summary>実体の ID を空白区切りで（照合用）。</summary>
        public static string Ids(IEnumerable<CardRef> cards) => string.Join(" ", cards.Select(c => c.Id));

        private static string[] Split(string ids) => ids.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

        /// <summary>手札で名前が id の最初の実体。</summary>
        public static CardRef InHand(Battle battle, string id) => battle.Hand.First(c => c.Def.Id == id);

        /// <summary>出来事の列を「種類:対象:量」の短い文字列にする（照合用）。</summary>
        public static string Trace(IEnumerable<BattleEvent> events) =>
            string.Join(" ", events.Select(e => e.Kind switch
            {
                EventKind.CardPlayed => $"Play:{e.Detail}",
                EventKind.Damaged => $"Dmg:{e.Target}:{e.Amount}+{e.Detail}",
                EventKind.StatusApplied => $"St:{e.Target}:{(StatusKind)e.Detail}{e.Amount}",
                EventKind.BlockGained => $"Blk:{e.Target}:{e.Amount}",
                EventKind.Drawn => $"Draw:{e.Detail}",
                EventKind.Discarded => $"Disc:{e.Detail}",
                EventKind.Exhausted => $"Exh:{e.Detail}",
                EventKind.Shuffled => $"Shuf:{e.Amount}",
                EventKind.Died => $"Died:{e.Target}",
                EventKind.Fizzled => $"Fizz:{(EffectKind)e.Detail}",
                EventKind.PoisonTick => $"Psn:{e.Target}:{e.Amount}",
                EventKind.EnergyGained => $"En:{e.Amount}",
                _ => e.Kind.ToString(),
            }));

        /// <summary>index 以降の出来事。</summary>
        public static string TraceFrom(Battle battle, int index) => Trace(battle.Events.Skip(index));

        /// <summary>全ての実体がちょうど 1 つの領域にあるか。</summary>
        public static void AssertZones(Battle battle, int deckSize)
        {
            var ids = new List<int>();
            ids.AddRange(battle.DrawPile.Select(c => c.Id));
            ids.AddRange(battle.Hand.Select(c => c.Id));
            ids.AddRange(battle.DiscardPile.Select(c => c.Id));
            ids.AddRange(battle.ExhaustPile.Select(c => c.Id));
            if (battle.Resolving != null) { ids.Add(battle.Resolving.Id); }
            NUnit.Framework.Assert.That(ids.Count, NUnit.Framework.Is.EqualTo(deckSize), "実体の数");
            NUnit.Framework.Assert.That(ids.Distinct().Count(), NUnit.Framework.Is.EqualTo(deckSize), "重複");
        }
    }
}
