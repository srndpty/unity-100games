using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;

namespace Game.App
{
    public enum TableScreen { Title, Help, Table }
    public enum Intent { Start, Help, Title, Resume, Move }

    public static class Cpu
    {
        public static Move Choose(Match match, int seat, IReadOnlyList<Move> legal)
        {
            var hand = match.Hand(seat);
            var seatWind = 27 + ((seat - match.Dealer + 4) % 4);
            foreach (var kind in new[] { MoveKind.Tsumo, MoveKind.Ron })
            {
                foreach (var move in legal)
                {
                    if (move.Kind == kind) { return move; }
                }
            }

            var riichi = RecommendDiscard(match, seat, legal, MoveKind.Riichi);
            if (riichi.HasValue) { return new Move(MoveKind.Riichi, riichi.Value); }

            foreach (var move in legal)
            {
                if (move.Kind == MoveKind.ClosedKan) { return move; }
            }

            foreach (var move in legal.OrderByDescending(m => m.Kind == MoveKind.OpenKan))
            {
                if (move.Kind is (MoveKind.Pon or MoveKind.OpenKan) &&
                    (move.Value == 27 || move.Value == seatWind || move.Value >= 31))
                {
                    return move;
                }
            }

            var counts = Tiles.Counts(hand.Select(Tiles.Type));
            return legal.OrderBy(m => m.Kind == MoveKind.Discard ? KeepValue(Tiles.Type(m.Value), counts) : 100)
                .ThenBy(m => m.Value).First();
        }

        public static int? RecommendDiscard(Match match, int seat, IReadOnlyList<Move> legal, MoveKind kind)
        {
            var options = legal.Where(move => move.Kind == kind).ToArray();
            if (options.Length == 0) { return null; }

            var hand = match.Hand(seat);
            var counts = Tiles.Counts(hand.Select(Tiles.Type));
            var visible = kind == MoveKind.Riichi ? Tiles.Counts(hand.Concat(match.DoraIndicators)
                .Concat(match.Seats.SelectMany(p => p.Discards.Where(d => !d.Called).Select(d => d.Id)))
                .Concat(match.Seats.SelectMany(p => p.Calls.SelectMany(m => m.Ids)))
                .Distinct().Select(Tiles.Type)) : counts;
            var melds = match.Seats[seat].Calls.Select(m => m.Group).ToArray();
            return options.OrderByDescending(move => kind == MoveKind.Riichi
                    ? Scoring.Waits(hand.Where(id => id != move.Value).Select(Tiles.Type), melds).Sum(t => 4 - visible[t]) : 0)
                .ThenBy(move => KeepValue(Tiles.Type(move.Value), counts))
                .ThenBy(move => move.Value)
                .First().Value;
        }

        private static int KeepValue(int tile, int[] counts)
        {
            var value = (counts[tile] - 1) * 12;
            if (Tiles.Honor(tile)) { return value; }

            var rank = tile % 9;
            for (var other = Math.Max(0, rank - 2); other <= Math.Min(8, rank + 2); other++)
            {
                if (other == rank) { continue; }

                value += counts[tile - rank + other] * (Math.Abs(other - rank) == 1 ? 4 : 2);
            }

            return value + (Tiles.Outside(tile) ? 1 : 2);
        }
    }

    public sealed class TableFlow
    {
        private readonly Queue<(Intent Intent, int Revision, Move Move, long Generation)> _queue = new();
        private readonly Func<int, IReadOnlyList<int>>? _wall;
        private readonly float _delay;
        private int _seed;
        private float _elapsed;
        private bool _focused = true;
        private bool _blockFrame;

        public TableFlow(int seed, float delay, Func<int, IReadOnlyList<int>>? wall = null)
            => (_seed, _delay, _wall) = (seed, delay, wall);

        public TableScreen Screen { get; private set; }
        public Match? Match { get; private set; }
        public bool Paused { get; private set; }
        public long Generation { get; private set; }

        public void Push(Intent intent, int revision = -1, Move move = default, long generation = -1)
        {
            if (_focused && !_blockFrame) { _queue.Enqueue((intent, revision, move, generation)); }
        }

        public void Focus(bool focused)
        {
            _focused = focused;
            _blockFrame = true;
            _queue.Clear();
            if (!focused && Screen == TableScreen.Table) { Paused = true; }
        }

        public void Step(float seconds)
        {
            _blockFrame = false;
            while (_queue.Count > 0)
            {
                var entry = _queue.Dequeue();
                if (entry.Intent == Intent.Resume && _focused && Paused)
                {
                    Paused = false;
                    ChangeGeneration();
                    continue;
                }
                if (!_focused || Paused) { continue; }

                switch (entry.Intent)
                {
                    case Intent.Start when Screen != TableScreen.Table || Match?.Phase == Phase.MatchResult:
                        Match = new Match(_seed, _wall);
                        _seed = unchecked(_seed + 1);
                        Screen = TableScreen.Table;
                        ChangeGeneration();
                        _elapsed = 0;
                        break;
                    case Intent.Help when Screen == TableScreen.Title:
                        Screen = TableScreen.Help;
                        ChangeGeneration();
                        break;
                    case Intent.Title:
                        Screen = TableScreen.Title;
                        Match = null;
                        ChangeGeneration();
                        break;
                    case Intent.Move when entry.Generation == Generation && Match?.Apply(0, entry.Revision, entry.Move) == true:
                        _elapsed = 0;
                        break;
                }
            }

            if (!_focused || Paused || Match == null || Match.LegalActions(0).Count > 0) { return; }

            _elapsed += seconds;
            if (_elapsed < _delay) { return; }

            _elapsed = 0;
            PlayCpu(Match, 1);
        }

        private void ChangeGeneration()
        {
            Generation++;
            _queue.Clear();
        }

        public static bool PlayCpu(Match match, int firstSeat)
        {
            for (var seat = firstSeat; seat < 4; seat++)
            {
                var legal = match.LegalActions(seat);
                if (legal.Count == 0) { continue; }

                var move = legal[0].Kind == MoveKind.Next ? legal[0] : Cpu.Choose(match, seat, legal);
                return match.Apply(seat, match.Revision, move);
            }

            return false;
        }
    }
}
