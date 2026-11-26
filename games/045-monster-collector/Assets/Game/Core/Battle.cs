using System;
using System.Collections.Generic;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum BattleKind
    {
        Wild = 1,
        Trainer = 2,
    }

    /// <summary>
    /// 戦闘のフェーズ。コマンドを選ぶ → 解決（<see cref="Battle.Submit"/> の中で一度に進む）→ 倒れた味方の交代先を選ぶ → 終了。
    /// 各フェーズで受け付けるコマンドだけを受け、他は拒否して何も変えない。
    /// </summary>
    public enum BattlePhase
    {
        ChooseCommand = 1,

        /// <summary>味方が倒れ、交代先を選ぶ（ターンを消費しない）。</summary>
        ForcedSwitch = 2,

        Ended = 3,
    }

    public enum BattleOutcome
    {
        None = 0,
        Won = 1,
        Lost = 2,
        Ran = 3,
        Captured = 4,
    }

    /// <summary>覚えられなかった技（戦闘の後に入れ替えるか選ぶ）。</summary>
    public sealed class PendingLearn
    {
        internal PendingLearn(int member, MoveDef move)
        {
            Member = member;
            Move = move;
        }

        /// <summary>手持ちの番号。</summary>
        public int Member { get; }

        public MoveDef Move { get; }
    }

    public sealed class BattleResult
    {
        private static readonly IReadOnlyList<BattleEvent> _none = Array.AsReadOnly(Array.Empty<BattleEvent>());

        private BattleResult(RejectReason reason, IReadOnlyList<BattleEvent> events)
        {
            Reason = reason;
            Events = events;
        }

        public bool Accepted => Reason == RejectReason.None;

        public RejectReason Reason { get; }

        /// <summary>このコマンドで起きた出来事（拒否なら空）。</summary>
        public IReadOnlyList<BattleEvent> Events { get; }

        internal static BattleResult Rejected(RejectReason reason) => new(reason, _none);

        internal static BattleResult Ok(List<BattleEvent> events) => new(RejectReason.None, events.AsReadOnly());
    }

    /// <summary>
    /// 1 回の戦闘。手持ちと道具は始めに複製して持ち、終わったら <see cref="Party"/> などを世界へ書き戻す。
    /// 乱数は戦闘ごとの系列（試合シードと戦闘番号から派生）だけを使う。
    /// </summary>
    public sealed partial class Battle
    {
        private readonly MonsterData _data;
        private readonly List<Monster> _party;
        private readonly List<Monster> _foes;
        private readonly bool[] _participated;
        private readonly int[] _playerStages;
        private readonly int[] _foeStages;
        private readonly List<BattleEvent> _log;
        private readonly List<PendingLearn> _pending;
        private RandomSeries _rng;
        private List<BattleEvent>? _current;

        private Battle(MonsterData data, BattleKind kind, TrainerDef? trainer, List<Monster> party, List<Monster> foes, int balls, int potions, bool hasRoom, RandomSeries rng)
        {
            _data = data;
            Kind = kind;
            Trainer = trainer;
            _party = party;
            _foes = foes;
            _participated = new bool[party.Count];
            _playerStages = new int[3];
            _foeStages = new int[3];
            _log = new List<BattleEvent>();
            _pending = new List<PendingLearn>();
            _rng = rng;
            Balls = balls;
            Potions = potions;
            HasRoom = hasRoom;
            Party = _party.AsReadOnly();
            Foes = _foes.AsReadOnly();
            Log = _log.AsReadOnly();
            Pending = _pending.AsReadOnly();
        }

        private Battle(Battle other)
        {
            _data = other._data;
            Kind = other.Kind;
            Trainer = other.Trainer;
            _party = new List<Monster>(other._party.Count);
            foreach (var m in other._party) { _party.Add(m.Clone()); }
            _foes = new List<Monster>(other._foes.Count);
            foreach (var m in other._foes) { _foes.Add(m.Clone()); }
            _participated = (bool[])other._participated.Clone();
            _playerStages = (int[])other._playerStages.Clone();
            _foeStages = (int[])other._foeStages.Clone();
            _log = new List<BattleEvent>(other._log);
            _pending = new List<PendingLearn>(other._pending);
            _rng = other._rng.Clone();
            Balls = other.Balls;
            Potions = other.Potions;
            HasRoom = other.HasRoom;
            Turn = other.Turn;
            Phase = other.Phase;
            Outcome = other.Outcome;
            Active = other.Active;
            FoeActive = other.FoeActive;
            RunAttempts = other.RunAttempts;
            Captured = other.Captured;
            Party = _party.AsReadOnly();
            Foes = _foes.AsReadOnly();
            Log = _log.AsReadOnly();
            Pending = _pending.AsReadOnly();
        }

        public BattleKind Kind { get; }

        public TrainerDef? Trainer { get; }

        /// <summary>戦闘中の手持ち（始めに複製したもの）。</summary>
        public IReadOnlyList<Monster> Party { get; }

        public IReadOnlyList<Monster> Foes { get; }

        public int Active { get; private set; }

        public int FoeActive { get; private set; }

        public Monster PlayerMonster => _party[Active];

        public Monster FoeMonster => _foes[FoeActive];

        public int Balls { get; private set; }

        public int Potions { get; private set; }

        /// <summary>捕まえた個体を置く場所があるか（手持ちかボックスに空き）。</summary>
        public bool HasRoom { get; }

        /// <summary>ターンの番号（1 から）。状態異常の解除予定はこの番号で持つ。</summary>
        public int Turn { get; private set; }

        public BattlePhase Phase { get; private set; }

        public BattleOutcome Outcome { get; private set; }

        public int RunAttempts { get; private set; }

        /// <summary>捕まえた個体（捕獲で終わったとき）。</summary>
        public Monster? Captured { get; private set; }

        /// <summary>これまでの出来事の全て。</summary>
        public IReadOnlyList<BattleEvent> Log { get; }

        public IReadOnlyList<PendingLearn> Pending { get; }

        public IReadOnlyList<int> PlayerStages => Array.AsReadOnly(_playerStages);

        public IReadOnlyList<int> FoeStages => Array.AsReadOnly(_foeStages);

        public uint RngState => _rng.State;

        public bool IsOver => Phase == BattlePhase.Ended;

        /// <summary>出ている味方の残りの手持ちに、元気な個体がいるか。</summary>
        public bool HasHealthyBench()
        {
            for (int i = 0; i < _party.Count; i++)
            {
                if (i != Active && !_party[i].IsFainted) { return true; }
            }

            return false;
        }

        public static Battle Wild(MonsterData data, IEnumerable<Monster> party, Monster wild, int balls, int potions, bool hasRoom, RandomSeries rng)
        {
            if (wild == null) { throw new ArgumentNullException(nameof(wild)); }
            var battle = Create(data, BattleKind.Wild, null, party, new List<Monster> { wild.Clone() }, balls, potions, hasRoom, rng);
            battle.Start();
            return battle;
        }

        public static Battle AgainstTrainer(MonsterData data, IEnumerable<Monster> party, TrainerDef trainer, int balls, int potions, RandomSeries rng)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            if (trainer == null) { throw new ArgumentNullException(nameof(trainer)); }
            var foes = new List<Monster>();
            foreach (var m in trainer.Party) { foes.Add(Monster.Create(m.Species, m.Level, data.Rules)); }
            var battle = Create(data, BattleKind.Trainer, trainer, party, foes, balls, potions, false, rng);
            battle.Start();
            return battle;
        }

        private static Battle Create(MonsterData data, BattleKind kind, TrainerDef? trainer, IEnumerable<Monster> party, List<Monster> foes, int balls, int potions, bool hasRoom, RandomSeries rng)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            if (party == null) { throw new ArgumentNullException(nameof(party)); }
            if (rng == null) { throw new ArgumentNullException(nameof(rng)); }
            var members = new List<Monster>();
            foreach (var m in party) { members.Add(m.Clone()); }
            if (!members.Exists(m => !m.IsFainted)) { throw new ArgumentException("戦える手持ちがいません。", nameof(party)); }
            if (foes.Count == 0) { throw new ArgumentException("相手がいません。", nameof(foes)); }
            return new Battle(data, kind, trainer, members, foes, balls, potions, hasRoom, rng);
        }

        public Battle Clone() => new(this);

        /// <summary>
        /// コマンドを 1 つ受け取り、そのターンを最後まで解決する。受け付けなければ理由を返し、状態・乱数・出来事を何も変えない。
        /// </summary>
        public BattleResult Submit(BattleCommand command)
        {
            var reason = Check(command);
            if (reason != RejectReason.None) { return BattleResult.Rejected(reason); }
            var events = new List<BattleEvent>();
            _current = events;
            try
            {
                if (Phase == BattlePhase.ForcedSwitch)
                {
                    SwitchIn(command.Index);
                    Phase = BattlePhase.ChooseCommand;
                    BeginTurn();
                }
                else
                {
                    ResolveTurn(command);
                }
            }
            finally
            {
                _current = null;
            }

            return BattleResult.Ok(events);
        }

        /// <summary>そのコマンドを今受け付けるか（受け付けないなら理由）。状態を変えない。</summary>
        public RejectReason Check(BattleCommand command)
        {
            if (Phase == BattlePhase.Ended) { return RejectReason.BattleOver; }
            if (Phase == BattlePhase.ForcedSwitch && command.Kind != CommandKind.Switch) { return RejectReason.MustSwitch; }
            switch (command.Kind)
            {
                case CommandKind.Fight:
                    var me = PlayerMonster;
                    if (command.Index < 0 || command.Index >= me.Moves.Count) { return RejectReason.NoSuchMove; }
                    if (me.HasUsableMove() && me.Moves[command.Index].Pp <= 0) { return RejectReason.NoPp; }
                    return RejectReason.None;
                case CommandKind.Switch:
                    if (command.Index < 0 || command.Index >= _party.Count) { return RejectReason.NoSuchMember; }
                    if (_party[command.Index].IsFainted) { return RejectReason.MemberFainted; }
                    if (command.Index == Active) { return RejectReason.AlreadyActive; }
                    return RejectReason.None;
                case CommandKind.Potion:
                    if (Potions <= 0) { return RejectReason.NoPotion; }
                    if (command.Index < 0 || command.Index >= _party.Count) { return RejectReason.NoSuchMember; }
                    if (_party[command.Index].IsFainted) { return RejectReason.MemberFainted; }
                    if (_party[command.Index].Hp >= _party[command.Index].MaxHp) { return RejectReason.FullHp; }
                    return RejectReason.None;
                case CommandKind.Ball:
                    if (Kind == BattleKind.Trainer) { return RejectReason.TrainerBattle; }
                    if (Balls <= 0) { return RejectReason.NoBall; }
                    if (!HasRoom) { return RejectReason.NoRoom; }
                    return RejectReason.None;
                case CommandKind.Run:
                    return Kind == BattleKind.Trainer ? RejectReason.TrainerBattle : RejectReason.None;
                default:
                    return RejectReason.NoSuchMove;
            }
        }

        public uint Hash()
        {
            var h = StateHash.Start();
            MixHash(ref h);
            return h.Value;
        }

        public void MixHash(ref StateHash h)
        {
            h.Mix((int)Kind);
            h.Mix(Trainer?.Index ?? -1);
            h.Mix(Turn);
            h.Mix((int)Phase);
            h.Mix((int)Outcome);
            h.Mix(Active);
            h.Mix(FoeActive);
            h.Mix(Balls);
            h.Mix(Potions);
            h.Mix(HasRoom);
            h.Mix(RunAttempts);
            h.Mix(_rng.State);
            foreach (var s in _playerStages) { h.Mix(s); }
            foreach (var s in _foeStages) { h.Mix(s); }
            foreach (var p in _participated) { h.Mix(p); }
            h.Mix(_party.Count);
            foreach (var m in _party) { m.MixHash(ref h); }
            h.Mix(_foes.Count);
            foreach (var m in _foes) { m.MixHash(ref h); }
            h.Mix(_pending.Count);
            foreach (var p in _pending)
            {
                h.Mix(p.Member);
                h.Mix(p.Move.Index);
            }

            h.Mix(Captured != null);
        }

        // --- テスト用の場面づくり ---

        /// <summary>テスト用：乱数の状態を差し替える（命中・捕獲の境界を決まった値で確かめる）。</summary>
        internal void SetRngStateForTest(uint state) => _rng = RandomSeries.FromState(state);
    }
}
