using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>他家にも見える席情報。伏せ牌は枚数だけを持つ。</summary>
    public sealed class PublicSeat
    {
        private readonly Seat _seat;

        internal PublicSeat(Seat seat) => _seat = seat;

        public int Points => _seat.Points;
        public bool Riichi => _seat.Riichi;
        public int HandCount => _seat.HandCount;
        public IReadOnlyList<Discard> Discards => _seat.Discards;
        public IReadOnlyList<Meld> Calls => _seat.Calls;
    }

    /// <summary>一席に結び付いた読み取り窓口。別の席への切り替えは対局の制御側が行う。</summary>
    public sealed class SeatView
    {
        private readonly Match _match;

        internal SeatView(Match match, int seat, IReadOnlyList<PublicSeat> seats)
        {
            _match = match;
            Seat = seat;
            Seats = seats;
            Hand = match.Hand(seat);
        }

        public int Seat { get; }
        public IReadOnlyList<PublicSeat> Seats { get; }
        public IReadOnlyList<int> Hand { get; }
        public int Drawn => Current == Seat && Phase == Phase.Drawn ? _match.Drawn : -1;
        public IReadOnlyList<Move> LegalActions => _match.LegalActions(Seat);
        public Phase Phase => _match.Phase;
        public int Revision => _match.Revision;
        public int Current => _match.Current;
        public int Dealer => _match.Dealer;
        public int Round => _match.Round;
        public int Serial => _match.Serial;
        public int Honba => _match.Honba;
        public int Sticks => _match.Sticks;
        public int Remaining => _match.Remaining;
        public int Target => _match.Target;
        public Settlement? Result => _match.Result;
        public IReadOnlyList<int> Ranking => _match.Ranking;
        public IReadOnlyList<int> DoraIndicators => _match.DoraIndicators;
    }
}
