using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core;

namespace Game.App
{
    /// <summary>表示上の戦う者 1 人。</summary>
    public sealed class Seat
    {
        private readonly int[] _status = new int[Enum.GetValues(typeof(StatusKind)).Length];

        internal Seat(int id, EnemyDef? def, int hp, int maxHp)
        {
            Id = id;
            Def = def;
            Hp = hp;
            MaxHp = maxHp;
        }

        public int Id { get; }

        /// <summary>敵のデータ（プレイヤーは null）。</summary>
        public EnemyDef? Def { get; }

        public int Hp { get; internal set; }

        public int MaxHp { get; }

        public int Block { get; internal set; }

        /// <summary>公開中の行動（なければ null）。</summary>
        public MoveDef? Intent { get; internal set; }

        public bool Alive => Hp > 0;

        public int Status(StatusKind kind) => _status[(int)kind];

        internal void AddStatus(StatusKind kind, int amount) => _status[(int)kind] += amount;
    }

    /// <summary>
    /// 画面に描く卓。戦闘の始めの状態から出来事（<see cref="BattleEvent"/>）を 1 つずつ当てはめて作る。
    /// Core は状態を先に最後まで変えるので、演出の途中はこちらを描く。全ての出来事を当てはめると Core の状態と一致する（テストで照合）。
    /// </summary>
    public sealed class TableModel
    {
        private readonly Dictionary<int, CardRef> _cards = new();
        private readonly List<Seat> _seats = new();
        private readonly List<CardRef> _draw = new();
        private readonly List<CardRef> _hand = new();
        private readonly List<CardRef> _discard = new();
        private readonly List<CardRef> _exhaust = new();
        private readonly GameData _data;

        public TableModel(Battle battle, GameData data)
        {
            if (battle == null) { throw new ArgumentNullException(nameof(battle)); }
            _data = data ?? throw new ArgumentNullException(nameof(data));
            foreach (var c in battle.Deck)
            {
                _cards.Add(c.Id, c);
                _draw.Add(c);
            }

            _seats.Add(new Seat(0, null, battle.StartHp, battle.Player.MaxHp));
            var id = 1;
            foreach (var foe in battle.Encounter.Foes) { _seats.Add(new Seat(id++, foe, foe.Hp, foe.Hp)); }
            Seats = new ReadOnlyCollection<Seat>(_seats);
            Hand = new ReadOnlyCollection<CardRef>(_hand);
            Discard = new ReadOnlyCollection<CardRef>(_discard);
            Exhaust = new ReadOnlyCollection<CardRef>(_exhaust);
        }

        /// <summary>0 がプレイヤー、1 から敵（出た順）。</summary>
        public IReadOnlyList<Seat> Seats { get; }

        public Seat Player => _seats[0];

        public IReadOnlyList<CardRef> Hand { get; }

        public IReadOnlyList<CardRef> Discard { get; }

        public IReadOnlyList<CardRef> Exhaust { get; }

        /// <summary>山札の枚数（順番は出さない）。</summary>
        public int DrawCount => _draw.Count;

        /// <summary>山札の中身（表示用に ID 順）。</summary>
        public IEnumerable<CardRef> DrawSorted()
        {
            var copy = new List<CardRef>(_draw);
            copy.Sort((a, b) => a.Id.CompareTo(b.Id));
            return copy;
        }

        public CardRef? Resolving { get; private set; }

        public int Energy { get; private set; }

        public int Turn { get; private set; }

        public BattlePhase Phase { get; private set; }

        public Seat? SeatById(int id)
        {
            foreach (var s in _seats)
            {
                if (s.Id == id) { return s; }
            }

            return null;
        }

        public void Apply(BattleEvent e)
        {
            switch (e.Kind)
            {
                case EventKind.TurnStarted:
                    Turn = e.Amount;
                    break;
                case EventKind.CardPlayed:
                    Resolving = Take(_hand, e.Detail);
                    Energy -= e.Amount;
                    break;
                case EventKind.Damaged:
                    var hit = Seat(e.Target);
                    hit.Block -= e.Detail;
                    hit.Hp -= e.Amount;
                    break;
                case EventKind.PoisonTick:
                    var sick = Seat(e.Target);
                    sick.Hp -= e.Amount;
                    sick.AddStatus(StatusKind.Poison, -1);
                    break;
                case EventKind.BlockGained:
                    Seat(e.Target).Block += e.Amount;
                    break;
                case EventKind.BlockLost:
                    Seat(e.Target).Block -= e.Amount;
                    break;
                case EventKind.StatusApplied:
                    Seat(e.Target).AddStatus((StatusKind)e.Detail, e.Amount);
                    break;
                case EventKind.EnergyGained:
                    Energy += e.Amount;
                    break;
                case EventKind.IntentSet:
                    var enemy = Seat(e.Source);
                    enemy.Intent = enemy.Def!.Moves[e.Detail];
                    break;
                case EventKind.Drawn:
                    _hand.Add(Take(_draw, e.Detail));
                    break;
                case EventKind.Shuffled:
                    _draw.AddRange(_discard);
                    _discard.Clear();
                    break;
                case EventKind.Discarded:
                    _discard.Add(TakeFromPlay(e.Detail));
                    break;
                case EventKind.Exhausted:
                    _exhaust.Add(TakeFromPlay(e.Detail));
                    break;
                case EventKind.Summoned:
                    var def = _data.Enemies[e.Detail];
                    _seats.Add(new Seat(e.Target, def, def.Hp, def.Hp));
                    break;
                case EventKind.Victory:
                    Phase = BattlePhase.Won;
                    break;
                case EventKind.Defeat:
                    Phase = BattlePhase.Lost;
                    break;
                case EventKind.EnemyActed:
                case EventKind.HandFull:
                case EventKind.Fizzled:
                case EventKind.Died:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e), e.Kind, "未定義の出来事です。");
            }
        }

        public CardRef Card(int id) => _cards[id];

        private Seat Seat(int id) => SeatById(id) ?? throw new InvalidOperationException($"番号 {id} の席がありません。");

        private CardRef TakeFromPlay(int id)
        {
            if (Resolving != null && Resolving.Id == id)
            {
                var card = Resolving;
                Resolving = null;
                return card;
            }

            return Take(_hand, id);
        }

        private static CardRef Take(List<CardRef> pile, int id)
        {
            var index = pile.FindIndex(c => c.Id == id);
            if (index < 0) { throw new InvalidOperationException($"カード {id} がその山にありません。"); }
            var card = pile[index];
            pile.RemoveAt(index);
            return card;
        }
    }
}
