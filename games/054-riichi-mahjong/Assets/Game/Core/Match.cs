using System;
using System.Collections.Generic;
using System.Linq;
using IntGeometry;

namespace Game.Core
{
    public enum Phase { Drawn, DiscardReaction, KanReaction, AfterCall, HandResult, MatchResult }
    public enum MoveKind { Discard, Riichi, Tsumo, Pass, Ron, Chi, Pon, OpenKan, ClosedKan, AddedKan, Next }

    public readonly struct Move : IEquatable<Move>
    {
        public Move(MoveKind kind, int value = -1) => (Kind, Value) = (kind, value);
        public MoveKind Kind { get; }
        public int Value { get; }
        public bool Equals(Move other) => Kind == other.Kind && Value == other.Value;
        public override bool Equals(object? obj) => obj is Move other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 137) + Value;
        public static bool operator ==(Move left, Move right) => left.Equals(right);
        public static bool operator !=(Move left, Move right) => !left.Equals(right);
    }

    public sealed class Discard
    {
        internal Discard(int id, bool riichi) => (Id, Riichi) = (id, riichi);
        public int Id { get; }
        /// <summary>この打牌で宣言したことを示す。成立状態は Seat.Riichi が示す。</summary>
        public bool Riichi { get; }
        public bool Called { get; internal set; }
    }

    public sealed class Meld
    {
        internal Meld(Group group, IEnumerable<int> ids, int from, int calledId)
            => (Group, Ids, From, CalledId) = (group, Array.AsReadOnly(ids.ToArray()), from, calledId);
        public Group Group { get; }
        public IReadOnlyList<int> Ids { get; }
        public int From { get; }
        public int CalledId { get; }
    }

    public sealed class Seat
    {
        internal readonly List<int> Hand = new();
        internal readonly List<Discard> River = new();
        internal readonly List<Meld> Melds = new();
        internal bool TemporaryFuriten { get; set; }
        internal bool RiichiFuriten { get; set; }
        internal bool Ippatsu { get; set; }
        internal bool DoubleRiichi { get; set; }
        public int Points { get; internal set; } = 25000;
        public bool Riichi { get; internal set; }
        public int HandCount => Hand.Count;
        public IReadOnlyList<Discard> Discards { get; }
        public IReadOnlyList<Meld> Calls { get; }

        internal Seat() => (Discards, Calls) = (River.AsReadOnly(), Melds.AsReadOnly());

        internal void Reset()
        {
            Hand.Clear();
            River.Clear();
            Melds.Clear();
            TemporaryFuriten = RiichiFuriten = Ippatsu = DoubleRiichi = Riichi = false;
        }
    }

    public sealed class Win
    {
        internal Win(int seat, int tile, HandScore score, IEnumerable<int> hand)
            => (Seat, Tile, Score, Hand) = (seat, tile, score, Array.AsReadOnly(hand.ToArray()));
        public int Seat { get; }
        public int Tile { get; }
        public HandScore Score { get; }
        public IReadOnlyList<int> Hand { get; }
    }

    public sealed class Settlement
    {
        internal Settlement(Win[] wins, bool[] ready, int[] differences, int source, int[] ura)
            => (Wins, Ready, Differences, Source, Ura) =
                (Array.AsReadOnly(wins), Array.AsReadOnly(ready), Array.AsReadOnly(differences), source, Array.AsReadOnly(ura));
        public IReadOnlyList<Win> Wins { get; }
        public IReadOnlyList<bool> Ready { get; }
        public IReadOnlyList<int> Differences { get; }
        public int Source { get; }
        public IReadOnlyList<int> Ura { get; }
    }

    public sealed class Match
    {
        private const int SeatsCount = 4;
        private const int DeadSize = 14;
        private readonly Seat[] _seats = { new(), new(), new(), new() };
        private readonly Move?[] _answers = new Move?[SeatsCount];
        private readonly IReadOnlyList<Move>[] _responses = new IReadOnlyList<Move>[SeatsCount];
        private readonly HashSet<int> _forbidden = new();
        private readonly int _seed;
        private readonly Func<int, IReadOnlyList<int>>? _wallForHand;
        private readonly int[] _wall = new int[Tiles.Total];
        private int _next;
        private int _end;
        private int _kans;
        private IReadOnlyList<Move> _turnActions = Array.Empty<Move>();
        private int _turnActionRevision = -1;
        private int _draw = -1;
        private int _target = -1;
        private int _added;
        private bool _opening;
        private bool _rinshan;
        private bool _lastDiscard;
        private bool _endAfterResult;

        public Match(int seed, Func<int, IReadOnlyList<int>>? wallForHand = null)
        {
            _seed = seed;
            _wallForHand = wallForHand;
            Seats = Array.AsReadOnly(_seats);
            StartHand();
        }

        public IReadOnlyList<Seat> Seats { get; }
        public Phase Phase { get; private set; }
        public int Revision { get; private set; }
        public int Current { get; private set; }
        public int Dealer { get; private set; }
        public int Round { get; private set; } = 1;
        public int Serial { get; private set; }
        public int Honba { get; private set; }
        public int Sticks { get; private set; }
        public int Remaining => _end - _next;
        public int Drawn => _draw;
        public int Target => _target;
        public Settlement? Result { get; private set; }
        public IReadOnlyList<int> Ranking => Array.AsReadOnly(Enumerable.Range(0, SeatsCount).OrderByDescending(s => _seats[s].Points).ToArray());
        public IReadOnlyList<int> DoraIndicators => Array.AsReadOnly(Indicators(false));
        public IReadOnlyList<int> Hand(int seat) => _seats[seat].Hand.AsReadOnly();

        public IReadOnlyList<Move> LegalActions(int seat)
        {
            if (seat is < 0 or >= SeatsCount) { return Array.Empty<Move>(); }

            if (Phase is Phase.DiscardReaction or Phase.KanReaction)
            {
                return _answers[seat].HasValue ? Array.Empty<Move>() : _responses[seat];
            }

            if (Phase == Phase.HandResult && seat == 0) { return new[] { new Move(MoveKind.Next) }; }
            if (seat != Current || Phase is not (Phase.Drawn or Phase.AfterCall)) { return Array.Empty<Move>(); }

            if (_turnActionRevision != Revision)
            {
                _turnActions = TurnActions(seat);
                _turnActionRevision = Revision;
            }

            return _turnActions;
        }

        private System.Collections.ObjectModel.ReadOnlyCollection<Move> TurnActions(int seat)
        {
            var player = _seats[seat];
            var moves = new List<Move>();
            if (Phase == Phase.Drawn && Evaluate(seat, _draw, true) != null) { moves.Add(new Move(MoveKind.Tsumo)); }

            var groups = Groups(player);
            foreach (var id in player.Hand)
            {
                if ((player.Riichi && id != _draw) || _forbidden.Contains(Tiles.Type(id))) { continue; }

                moves.Add(new Move(MoveKind.Discard, id));
                if (Phase == Phase.Drawn && !player.Riichi && player.Points >= 1000 && Remaining >= 4 &&
                    groups.All(g => !g.Open) && Scoring.Waits(Without(player, id), groups).Count > 0)
                {
                    moves.Add(new Move(MoveKind.Riichi, id));
                }
            }

            if (Phase == Phase.Drawn && CanKan)
            {
                foreach (var group in player.Hand.GroupBy(Tiles.Type))
                {
                    if (group.Count() == 4 && (!player.Riichi || (Tiles.Type(_draw) == group.Key &&
                        Scoring.CanLockKan(Without(player, _draw), group.Key, groups))))
                    {
                        moves.Add(new Move(MoveKind.ClosedKan, group.Key));
                    }

                    if (!player.Riichi && groups.Any(g => g.Kind == MeldKind.Pon && g.Tile == group.Key))
                    {
                        moves.Add(new Move(MoveKind.AddedKan, group.Key));
                    }
                }
            }

            return moves.AsReadOnly();
        }

        public bool Apply(int seat, int revision, Move move)
        {
            if (revision != Revision || !LegalActions(seat).Contains(move)) { return false; }

            if (Phase is Phase.DiscardReaction or Phase.KanReaction)
            {
                _answers[seat] = move;
                if (_answers.All(a => a.HasValue))
                {
                    Revision++;
                    ResolveResponses();
                }

                return true;
            }

            Revision++;
            switch (move.Kind)
            {
                case MoveKind.Discard:
                case MoveKind.Riichi:
                    DiscardTile(move.Value, move.Kind == MoveKind.Riichi);
                    break;
                case MoveKind.Tsumo:
                    SettleWins(new[] { Current }, _draw, true);
                    break;
                case MoveKind.ClosedKan:
                    OwnKan(move.Value, MeldKind.ClosedKan);
                    break;
                case MoveKind.AddedKan:
                    _added = move.Value;
                    _target = _seats[Current].Hand.First(id => Tiles.Type(id) == move.Value);
                    OpenResponses(true);
                    break;
                case MoveKind.Next:
                    Advance();
                    break;
            }

            return true;
        }

        public IReadOnlyList<int> PhysicalTiles()
        {
            var result = new List<int>(Tiles.Total);
            result.AddRange(_wall.Skip(_next).Take(_end - _next));
            result.AddRange(_wall.Skip(_end).Take(Tiles.Total - _end - _kans));
            foreach (var seat in _seats)
            {
                result.AddRange(seat.Hand);
                result.AddRange(seat.Melds.SelectMany(m => m.Ids));
                result.AddRange(seat.River.Where(d => !d.Called).Select(d => d.Id));
            }

            return result.AsReadOnly();
        }

        private bool CanKan => _kans < 4 && Remaining > 0;
        private static Group[] Groups(Seat s) => s.Melds.Select(m => m.Group).ToArray();
        private static IEnumerable<int> Without(Seat s, int id) => s.Hand.Where(t => t != id).Select(Tiles.Type);
        private static int Clockwise(int from, int to) => (to - from + SeatsCount) % SeatsCount;
        private int[] Indicators(bool ura) => Enumerable.Range(0, _kans + 1)
            .Select(i => _wall[122 + i * 2 + (ura ? 1 : 0)]).ToArray();

        private void StartHand()
        {
            Serial++;
            foreach (var seat in _seats) { seat.Reset(); }

            Result = null;
            _next = 0;
            _end = Tiles.Total - DeadSize;
            _kans = 0;
            _target = -1;
            _opening = true;
            _lastDiscard = false;
            var custom = _wallForHand?.Invoke(Serial);
            if (custom != null)
            {
                if (custom.Count != Tiles.Total || custom.Distinct().Count() != Tiles.Total ||
                    custom.Any(id => id is < 0 or >= Tiles.Total))
                {
                    throw new ArgumentException("牌山には136枚の一意な牌IDが必要です。");
                }

                for (var i = 0; i < _wall.Length; i++) { _wall[i] = custom[i]; }
            }
            else
            {
                var random = new SeededRandom(unchecked(_seed * 397 ^ Serial));
                for (var i = 0; i < _wall.Length; i++) { _wall[i] = i; }

                for (var i = _wall.Length - 1; i > 0; i--)
                {
                    var other = random.Next(i + 1);
                    (_wall[i], _wall[other]) = (_wall[other], _wall[i]);
                }
            }

            for (var tile = 0; tile < 13; tile++)
            {
                for (var turn = 0; turn < SeatsCount; turn++)
                {
                    _seats[(Dealer + turn) % SeatsCount].Hand.Add(_wall[_next++]);
                }
            }

            Draw(Dealer, false);
        }

        private void Draw(int seat, bool kan)
        {
            Current = seat;
            _target = -1;
            _rinshan = kan;
            _forbidden.Clear();
            Phase = Phase.Drawn;
            _seats[seat].TemporaryFuriten = false;
            _draw = kan ? _wall[Tiles.Total - _kans] : _wall[_next++];
            _seats[seat].Hand.Add(_draw);
        }

        private void DiscardTile(int id, bool declare)
        {
            var s = _seats[Current];
            s.Hand.Remove(id);
            s.River.Add(new Discard(id, declare));
            s.Ippatsu = false;
            _target = id;
            _lastDiscard = Remaining == 0 && !_rinshan;
            _draw = -1;
            _forbidden.Clear();
            OpenResponses(false);
        }

        private void OpenResponses(bool kan)
        {
            Phase = kan ? Phase.KanReaction : Phase.DiscardReaction;
            for (var seat = 0; seat < SeatsCount; seat++)
            {
                _responses[seat] = seat == Current ? Array.Empty<Move>() : BuildResponses(seat, kan);
                _answers[seat] = _responses[seat].Count == 0 ? new Move(MoveKind.Pass) : null;
            }

            if (_answers.All(a => a.HasValue)) { ResolveResponses(); }
        }

        private System.Collections.ObjectModel.ReadOnlyCollection<Move> BuildResponses(int seat, bool kan)
        {
            var moves = new List<Move>();
            var player = _seats[seat];
            var tile = Tiles.Type(_target);
            if (!player.TemporaryFuriten && !player.RiichiFuriten && Evaluate(seat, _target, false) != null)
            {
                var waits = Scoring.Waits(player.Hand.Select(Tiles.Type), Groups(player));
                if (!player.River.Any(d => waits.Contains(Tiles.Type(d.Id)))) { moves.Add(new Move(MoveKind.Ron)); }
            }

            if (!kan && Remaining > 0 && !player.Riichi)
            {
                var counts = Tiles.Counts(player.Hand.Select(Tiles.Type));
                if (counts[tile] >= 2 && player.Hand.Any(id => Tiles.Type(id) != tile))
                {
                    moves.Add(new Move(MoveKind.Pon, tile));
                }

                if (counts[tile] == 3 && CanKan) { moves.Add(new Move(MoveKind.OpenKan, tile)); }

                if (Clockwise(Current, seat) == 1 && !Tiles.Honor(tile))
                {
                    for (var start = Math.Max(0, tile - 2); start <= tile; start++)
                    {
                        if (!Tiles.CanSequence(start) || start / 9 != tile / 9) { continue; }

                        var needed = Enumerable.Range(start, 3).Where(t => t != tile).ToArray();
                        var forbidden = SwapTiles(start, tile);
                        if (needed.All(t => counts[t] > 0) &&
                            player.Hand.GroupBy(Tiles.Type).Any(g => !forbidden.Contains(g.Key)
                                && g.Count() > (needed.Contains(g.Key) ? 1 : 0)))
                        {
                            moves.Add(new Move(MoveKind.Chi, start));
                        }
                    }
                }
            }

            if (moves.Count > 0) { moves.Add(new Move(MoveKind.Pass)); }

            return moves.AsReadOnly();
        }

        private void ResolveResponses()
        {
            var order = Enumerable.Range(1, SeatsCount - 1).Select(offset => (Current + offset) % SeatsCount).ToArray();
            var winners = order.Where(s => _answers[s]?.Kind == MoveKind.Ron).ToArray();
            foreach (var seat in order)
            {
                if (_answers[seat]?.Kind == MoveKind.Ron || !_responses[seat].Contains(new Move(MoveKind.Ron))) { continue; }

                if (_seats[seat].Riichi) { _seats[seat].RiichiFuriten = true; }
                else { _seats[seat].TemporaryFuriten = true; }
            }

            if (winners.Length > 0)
            {
                SettleWins(winners, _target, false);
                return;
            }

            if (Phase == Phase.KanReaction)
            {
                OwnKan(_added, MeldKind.AddedKan);
                return;
            }

            var player = _seats[Current];
            if (player.River[player.River.Count - 1].Riichi)
            {
                player.DoubleRiichi = _opening && player.River.Count == 1;
                player.Riichi = true;
                player.Ippatsu = true;
                player.Points -= 1000;
                Sticks++;
            }

            foreach (var seat in order.OrderBy(s => _answers[s]?.Kind == MoveKind.Chi))
            {
                var move = _answers[seat].GetValueOrDefault();
                if (move.Kind is not (MoveKind.Chi or MoveKind.Pon or MoveKind.OpenKan)) { continue; }

                Call(seat, move);
                return;
            }

            if (Remaining == 0) { SettleDraw(); }
            else { Draw((Current + 1) % SeatsCount, false); }
        }

        private static int[] SwapTiles(int start, int called)
        {
            var extra = called == start && start % 9 < 6 ? start + 3
                : called == start + 2 && start % 9 > 0 ? start - 1 : called;
            return new[] { called, extra };
        }

        private void Call(int seat, Move move)
        {
            var player = _seats[seat];
            var tile = Tiles.Type(_target);
            var kind = move.Kind switch
            {
                MoveKind.Chi => MeldKind.Chi,
                MoveKind.Pon => MeldKind.Pon,
                _ => MeldKind.OpenKan,
            };
            var ids = new List<int> { _target };
            var types = kind == MeldKind.Chi ? Enumerable.Range(move.Value, 3).Where(t => t != tile)
                : Enumerable.Repeat(tile, kind == MeldKind.Pon ? 2 : 3);
            foreach (var type in types)
            {
                var id = player.Hand.First(t => Tiles.Type(t) == type);
                player.Hand.Remove(id);
                ids.Add(id);
            }

            var source = _seats[Current].River;
            source[source.Count - 1].Called = true;
            player.Melds.Add(new Meld(new Group(move.Value, kind), ids, Current, _target));
            InterruptOpening();
            Current = seat;
            _draw = -1;
            if (kind == MeldKind.OpenKan) { DrawRinshan(); }
            else
            {
                _forbidden.UnionWith(kind == MeldKind.Chi ? SwapTiles(move.Value, tile) : new[] { tile });
                Phase = Phase.AfterCall;
            }
        }

        private void OwnKan(int tile, MeldKind kind)
        {
            var player = _seats[Current];
            var ids = player.Hand.Where(t => Tiles.Type(t) == tile).ToArray();
            foreach (var id in ids) { player.Hand.Remove(id); }

            if (kind == MeldKind.ClosedKan)
            {
                player.Melds.Add(new Meld(new Group(tile, kind), ids, Current, -1));
            }
            else
            {
                var index = player.Melds.FindIndex(m => m.Group.Kind == MeldKind.Pon && m.Group.Tile == tile);
                var pon = player.Melds[index];
                player.Melds[index] = new Meld(new Group(tile, kind), pon.Ids.Concat(ids), pon.From, pon.CalledId);
            }

            InterruptOpening();
            DrawRinshan();
        }

        private void InterruptOpening()
        {
            _opening = false;
            foreach (var seat in _seats) { seat.Ippatsu = false; }
        }

        private void DrawRinshan()
        {
            _kans++;
            _end--;
            Draw(Current, true);
        }

        private HandScore? Evaluate(int seat, int id, bool tsumo)
        {
            var p = _seats[seat];
            var flags = tsumo ? WinContext.Tsumo : WinContext.None;
            if (p.Riichi) { flags |= p.DoubleRiichi ? WinContext.DoubleRiichi : WinContext.Riichi; }
            if (p.Ippatsu) { flags |= WinContext.Ippatsu; }
            if (tsumo && _rinshan) { flags |= WinContext.Rinshan; }
            if (tsumo && !_rinshan && Remaining == 0) { flags |= WinContext.LastDraw; }
            if (!tsumo && Phase == Phase.DiscardReaction && _lastDiscard) { flags |= WinContext.LastDiscard; }
            if (!tsumo && Phase == Phase.KanReaction) { flags |= WinContext.RobbingKan; }

            var types = p.Hand.Select(Tiles.Type);
            if (!tsumo) { types = types.Append(Tiles.Type(id)); }

            var indicators = Indicators(false).AsEnumerable();
            if (p.Riichi) { indicators = indicators.Concat(Indicators(true)); }

            return Scoring.Evaluate(types, Groups(p), Tiles.Type(id), 27 + Clockwise(Dealer, seat), flags,
                indicators.Select(Tiles.Type));
        }

        private void SettleWins(int[] winners, int tile, bool tsumo)
        {
            var delta = new int[SeatsCount];
            var wins = new List<Win>();
            foreach (var seat in winners)
            {
                var score = Evaluate(seat, tile, tsumo) ?? throw new InvalidOperationException("和了の再計算に失敗しました。");
                var hand = tsumo ? _seats[seat].Hand : _seats[seat].Hand.Append(tile);
                wins.Add(new Win(seat, tile, score, hand));
                if (tsumo)
                {
                    for (var payer = 0; payer < SeatsCount; payer++)
                    {
                        if (payer == seat) { continue; }

                        Transfer(delta, payer, seat, score.Tsumo(seat == Dealer, payer == Dealer) + Honba * 100);
                    }
                }
                else { Transfer(delta, Current, seat, score.Ron(seat == Dealer) + Honba * 300); }
            }

            delta[winners[0]] += Sticks * 1000;
            Sticks = 0;
            Finish(wins.ToArray(), new bool[SeatsCount], delta, winners.Contains(Dealer));
        }

        private static void Transfer(int[] delta, int from, int to, int points)
        {
            delta[from] -= points;
            delta[to] += points;
        }

        private void SettleDraw()
        {
            var ready = _seats.Select(p => Scoring.Waits(p.Hand.Select(Tiles.Type), Groups(p)).Count > 0).ToArray();
            var count = ready.Count(value => value);
            var delta = new int[SeatsCount];
            if (count is > 0 and < SeatsCount)
            {
                for (var s = 0; s < SeatsCount; s++) { delta[s] = ready[s] ? 3000 / count : -3000 / (SeatsCount - count); }
            }

            Finish(Array.Empty<Win>(), ready, delta, ready[Dealer]);
        }

        private bool _repeat;

        private void Finish(Win[] wins, bool[] ready, int[] delta, bool repeat)
        {
            _repeat = repeat;
            for (var s = 0; s < SeatsCount; s++) { _seats[s].Points += delta[s]; }

            _endAfterResult = (Round == 4 && !repeat) || _seats.Any(p => p.Points < 0);
            if (_endAfterResult && Sticks > 0)
            {
                var first = Ranking[0];
                delta[first] += Sticks * 1000;
                _seats[first].Points += Sticks * 1000;
                Sticks = 0;
            }

            Result = new Settlement(wins, ready, delta, Current, wins.Any(w => _seats[w.Seat].Riichi) ? Indicators(true) : Array.Empty<int>());
            Phase = Phase.HandResult;
        }

        private void Advance()
        {
            if (_endAfterResult)
            {
                Phase = Phase.MatchResult;
                return;
            }

            Honba = _repeat || Result!.Wins.Count == 0 ? Honba + 1 : 0;
            if (!_repeat)
            {
                Round++;
                Dealer = (Dealer + 1) % SeatsCount;
            }

            StartHand();
        }
    }
}
