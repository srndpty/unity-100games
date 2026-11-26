using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum BattlePhase
    {
        /// <summary>プレイヤーの番（操作を待っている）。敵の番は <see cref="Battle.EndTurn"/> の中で最後まで進む。</summary>
        PlayerTurn,
        Won,
        Lost,
    }

    /// <summary>
    /// 1 回の戦闘。カードを使うと、その効果を書いた順にコマンドとして解決キューへ積み、FIFO で最後まで解決する。
    /// 各コマンドは状態を変えて出来事（<see cref="BattleEvent"/>）を記録する。対象を選ぶのは App で、ここは確定した対象を受け取るだけ。
    /// カードはどの時点でも山札・手札・解決中・捨て札・廃棄のちょうど 1 つにある。
    /// </summary>
    public sealed partial class Battle
    {
        private static readonly StatusKind[] _decaying = { StatusKind.Weak, StatusKind.Vulnerable };
        private readonly GameData _data;
        private readonly DeckConfig _config;
        private readonly uint _fingerprint;
        private Combatant _player;
        private List<Enemy> _enemies;
        private List<CardRef> _draw;
        private List<CardRef> _hand;
        private List<CardRef> _discard;
        private List<CardRef> _exhaust;
        private List<BattleEvent> _events;
        private Queue<Command> _queue = new();
        private RandomSeries _shuffleRng;
        private RandomSeries _aiRng;
        private RandomSeries _effectRng;
        private int _nextEnemyId;

        /// <param name="config">検証済みの設定。戦闘の間は書き換えない前提で参照を持つ（<see cref="Run"/> が複製を渡す）。</param>
        /// <param name="node">ルートの節の番号。乱数の系列を節ごとに分ける。</param>
        public Battle(GameData data, DeckConfig config, IEnumerable<CardRef> deck, int hp, int maxHp, EncounterDef encounter, int seed, int node)
            : this(data, config, Fingerprint.Of(data, config), deck, hp, maxHp, encounter, seed, node)
        {
        }

        internal Battle(GameData data, DeckConfig config, uint fingerprint, IEnumerable<CardRef> deck, int hp, int maxHp, EncounterDef encounter, int seed, int node)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (deck == null) { throw new ArgumentNullException(nameof(deck)); }
            if (encounter == null) { throw new ArgumentNullException(nameof(encounter)); }
            if (maxHp < 1 || hp < 1 || hp > maxHp) { throw new ArgumentOutOfRangeException(nameof(hp)); }
            _fingerprint = fingerprint;
            _player = new Combatant(Combatant.PlayerId, hp, maxHp);
            _enemies = new List<Enemy>();
            _nextEnemyId = 1;
            foreach (var foe in encounter.Foes) { _enemies.Add(new Enemy(_nextEnemyId++, foe)); }
            _draw = new List<CardRef>(deck);
            Deck = Array.AsReadOnly(_draw.ToArray());
            StartHp = hp;
            var ids = new HashSet<int>();
            foreach (var c in _draw)
            {
                if (!ids.Add(c.Id)) { throw new ArgumentException($"カードの実体 ID {c.Id} が重複しています。", nameof(deck)); }
            }

            _hand = new List<CardRef>();
            _discard = new List<CardRef>();
            _exhaust = new List<CardRef>();
            _events = new List<BattleEvent>();
            _shuffleRng = RandomSeries.Derive(seed, node, (int)RngPurpose.Shuffle);
            _aiRng = RandomSeries.Derive(seed, node, (int)RngPurpose.EnemyAi);
            _effectRng = RandomSeries.Derive(seed, node, (int)RngPurpose.Effect);
            Encounter = encounter;
            ShuffleInPlace(_draw);
            MakeViews();
            StartPlayerTurn();
        }

        public EncounterDef Encounter { get; }

        /// <summary>戦闘に持ち込んだデッキ（渡された順）。表示の初期状態に使う。</summary>
        public IReadOnlyList<CardRef> Deck { get; }

        /// <summary>戦闘の始めの HP。</summary>
        public int StartHp { get; }

        public BattlePhase Phase { get; private set; }

        public bool IsOver => Phase != BattlePhase.PlayerTurn;

        /// <summary>今のターン（1 から）。</summary>
        public int Turn { get; private set; }

        public int Energy { get; private set; }

        public Combatant Player => _player;

        /// <summary>出た順（番号順）の敵。倒れた敵も HP 0 で残る。</summary>
        public IReadOnlyList<Enemy> Enemies { get; private set; } = null!;

        /// <summary>山札。末尾が一番上。画面には枚数だけを出す。</summary>
        public IReadOnlyList<CardRef> DrawPile { get; private set; } = null!;

        public IReadOnlyList<CardRef> Hand { get; private set; } = null!;

        public IReadOnlyList<CardRef> DiscardPile { get; private set; } = null!;

        public IReadOnlyList<CardRef> ExhaustPile { get; private set; } = null!;

        /// <summary>解決中のカード。操作の合間は常に null（解決は 1 回の呼び出しの中で終わる）。</summary>
        public CardRef? Resolving { get; private set; }

        /// <summary>この戦闘の出来事の全記録。</summary>
        public IReadOnlyList<BattleEvent> Events { get; private set; } = null!;

        public Enemy? EnemyById(int id)
        {
            foreach (var e in _enemies)
            {
                if (e.Id == id) { return e; }
            }

            return null;
        }

        /// <summary>手札のカードを、払えるかだけで見て使えるか（画面の暗転用）。</summary>
        public bool Affordable(CardRef card) => card != null && !IsOver && card.Def.Cost <= Energy;

        /// <summary>使えるかの検証だけを行う（状態を変えない）。</summary>
        public PlayResult CanPlay(int cardId, int? target) => Check(cardId, target, out _);

        /// <summary>
        /// カードを使う。拒否したときは状態・乱数・出来事を何も変えない。
        /// 受け付けたら、エネルギーを払い、カードを解決中へ移し、効果を書いた順にキューへ積んで最後まで解決し、捨て札か廃棄の山へ移す。
        /// </summary>
        public PlayResult Play(int cardId, int? target)
        {
            var check = Check(cardId, target, out var card);
            if (!check.Accepted) { return check; }
            Energy -= card!.Def.Cost;
            _hand.Remove(card);
            Resolving = card;
            Emit(EventKind.CardPlayed, Combatant.PlayerId, target ?? -1, card.Def.Cost, card.Id);
            foreach (var effect in card.Def.Effects) { Enqueue(new Command(effect, Combatant.PlayerId, target ?? -1)); }
            RunQueue();
            FinishResolving();
            return PlayResult.Ok;
        }

        /// <summary>ターンを終える。手札を捨て、敵が番号順に公開していた行動を実行し、次のプレイヤーの番を始める。</summary>
        public PlayResult EndTurn()
        {
            if (IsOver) { return PlayResult.Reject(RejectReason.BattleOver); }
            Emit(EventKind.TurnEnded, -1, -1, Turn, 0);
            while (_hand.Count > 0)
            {
                var card = _hand[0];
                _hand.RemoveAt(0);
                _discard.Add(card);
                Emit(EventKind.Discarded, -1, -1, 0, card.Id);
            }

            Decay(_player);
            var acting = _enemies.ToArray();
            foreach (var enemy in acting)
            {
                if (!enemy.Alive) { continue; }
                ResetBlock(enemy);
                PoisonTick(enemy);
                if (CheckEnd()) { return PlayResult.Ok; }
                if (!enemy.Alive) { continue; }
                var move = enemy.Intent;
                if (move != null)
                {
                    enemy.LastMove = move.Index;
                    Emit(EventKind.EnemyActed, enemy.Id, -1, 0, move.Index);
                    foreach (var effect in move.Effects) { Enqueue(new Command(effect, enemy.Id, -1)); }
                    RunQueue();
                    if (IsOver) { return PlayResult.Ok; }
                }

                Decay(enemy);
            }

            StartPlayerTurn();
            return PlayResult.Ok;
        }

        /// <summary>攻撃の量の見積もり（敵の意図の表示用）。実際の解決と同じ式。</summary>
        public int PreviewAttack(Combatant attacker, Combatant defender, int baseAmount) => DamageCalc.Attack(baseAmount, attacker, defender, _config);

        public Battle Clone()
        {
            var copy = (Battle)MemberwiseClone();
            copy._player = _player.Clone();
            copy._enemies = new List<Enemy>(_enemies.Count);
            foreach (var e in _enemies) { copy._enemies.Add((Enemy)e.Clone()); }
            copy._draw = new List<CardRef>(_draw);
            copy._hand = new List<CardRef>(_hand);
            copy._discard = new List<CardRef>(_discard);
            copy._exhaust = new List<CardRef>(_exhaust);
            copy._events = new List<BattleEvent>(_events);
            copy._queue = new Queue<Command>(_queue);
            copy._shuffleRng = _shuffleRng.Clone();
            copy._aiRng = _aiRng.Clone();
            copy._effectRng = _effectRng.Clone();
            copy.MakeViews();
            return copy;
        }

        /// <summary>以後の動きを決める状態全てのハッシュ（出来事の記録は含めない）。</summary>
        public uint Hash()
        {
            var h = StateHash.Start();
            h.Mix(_fingerprint);
            h.Mix(Encounter.Index);
            h.Mix((int)Phase);
            h.Mix(Turn);
            h.Mix(Energy);
            h.Mix(_nextEnemyId);
            _player.MixInto(ref h);
            h.Mix(_enemies.Count);
            foreach (var e in _enemies) { e.MixInto(ref h); }
            MixPile(ref h, _draw);
            MixPile(ref h, _hand);
            MixPile(ref h, _discard);
            MixPile(ref h, _exhaust);
            h.Mix(Resolving?.Id ?? -1);
            h.Mix(_shuffleRng.State);
            h.Mix(_aiRng.State);
            h.Mix(_effectRng.State);
            return h.Value;
        }

        /// <summary>テスト用：山を並べ直す（山札は末尾が一番上）。全ての実体がどこかちょうど 1 つにあること。</summary>
        internal void Arrange(IEnumerable<CardRef> hand, IEnumerable<CardRef> draw, IEnumerable<CardRef> discard, int energy)
        {
            var all = new HashSet<int>();
            foreach (var c in _draw) { all.Add(c.Id); }
            foreach (var c in _hand) { all.Add(c.Id); }
            foreach (var c in _discard) { all.Add(c.Id); }
            foreach (var c in _exhaust) { all.Add(c.Id); }
            _hand.Clear();
            _hand.AddRange(hand);
            _draw.Clear();
            _draw.AddRange(draw);
            _discard.Clear();
            _discard.AddRange(discard);
            _exhaust.Clear();
            Energy = energy;
            var placed = new HashSet<int>();
            foreach (var c in _hand) { placed.Add(c.Id); }
            foreach (var c in _draw) { placed.Add(c.Id); }
            foreach (var c in _discard) { placed.Add(c.Id); }
            if (!placed.SetEquals(all)) { throw new ArgumentException("並べ直しの前後でカードの実体が一致しません。"); }
        }

        private static void MixPile(ref StateHash h, List<CardRef> pile)
        {
            h.Mix(pile.Count);
            foreach (var c in pile)
            {
                h.Mix(c.Id);
                h.Mix(c.Def.Index);
            }
        }

        private void MakeViews()
        {
            Enemies = new ReadOnlyCollection<Enemy>(_enemies);
            DrawPile = new ReadOnlyCollection<CardRef>(_draw);
            Hand = new ReadOnlyCollection<CardRef>(_hand);
            DiscardPile = new ReadOnlyCollection<CardRef>(_discard);
            ExhaustPile = new ReadOnlyCollection<CardRef>(_exhaust);
            Events = new ReadOnlyCollection<BattleEvent>(_events);
        }

        private PlayResult Check(int cardId, int? target, out CardRef? card)
        {
            card = null;
            if (IsOver) { return PlayResult.Reject(RejectReason.BattleOver); }
            foreach (var c in _hand)
            {
                if (c.Id == cardId) { card = c; }
            }

            if (card == null) { return PlayResult.Reject(RejectReason.NotInHand); }
            if (card.Def.Cost > Energy) { return PlayResult.Reject(RejectReason.NotEnoughEnergy); }
            if (card.Def.NeedsTarget)
            {
                if (target == null) { return PlayResult.Reject(RejectReason.TargetRequired); }
                var enemy = EnemyById(target.Value);
                if (enemy == null || !enemy.Alive) { return PlayResult.Reject(RejectReason.TargetInvalid); }
            }
            else if (target != null)
            {
                return PlayResult.Reject(RejectReason.TargetNotAllowed);
            }

            return PlayResult.Ok;
        }

        private void StartPlayerTurn()
        {
            Turn++;
            Emit(EventKind.TurnStarted, -1, -1, Turn, 0);
            ResetBlock(_player);
            PoisonTick(_player);
            if (CheckEnd()) { return; }
            var bulwark = _player.Status(StatusKind.Bulwark);
            if (bulwark > 0) { Enqueue(new Command(new EffectDef(EffectKind.Block, bulwark, TargetMode.Self), Combatant.PlayerId, -1)); }
            var miasma = _player.Status(StatusKind.Miasma);
            if (miasma > 0)
            {
                Enqueue(new Command(new EffectDef(EffectKind.Status, miasma, TargetMode.AllEnemies, StatusKind.Poison), Combatant.PlayerId, -1));
            }

            RunQueue();
            if (IsOver) { return; }
            if (Energy != _config.Energy)
            {
                Emit(EventKind.EnergyGained, Combatant.PlayerId, -1, _config.Energy - Energy, 0);
                Energy = _config.Energy;
            }

            Enqueue(new Command(new EffectDef(EffectKind.Draw, _config.DrawPerTurn, TargetMode.None), Combatant.PlayerId, -1));
            RunQueue();
            if (IsOver) { return; }
            foreach (var enemy in _enemies)
            {
                if (!enemy.Alive) { continue; }
                enemy.Intent = ChooseMove(enemy);
                Emit(EventKind.IntentSet, enemy.Id, -1, 0, enemy.Intent.Index);
            }
        }

        private void ResetBlock(Combatant who)
        {
            if (who.Block == 0) { return; }
            Emit(EventKind.BlockLost, who.Id, who.Id, who.Block, 0);
            who.Block = 0;
        }

        /// <summary>持ち主のターンの終わり：脱力・弱体を 1 減らす。</summary>
        private void Decay(Combatant who)
        {
            foreach (var kind in _decaying)
            {
                if (who.Status(kind) <= 0) { continue; }
                who.SetStatus(kind, who.Status(kind) - 1);
                Emit(EventKind.StatusApplied, who.Id, who.Id, -1, (int)kind);
            }
        }

        private void FinishResolving()
        {
            var card = Resolving;
            if (card == null) { return; }
            Resolving = null;
            if (card.Def.GoesToExhaust)
            {
                _exhaust.Add(card);
                Emit(EventKind.Exhausted, -1, -1, 0, card.Id);
            }
            else
            {
                _discard.Add(card);
                Emit(EventKind.Discarded, -1, -1, 0, card.Id);
            }
        }

        private void Enqueue(Command command)
        {
            if (_queue.Count >= _config.MaxEffects + 2)
            {
                throw new InvalidOperationException("解決キューが上限を超えました（データの検証で防いでいるはずの状態）。");
            }

            _queue.Enqueue(command);
        }

        /// <summary>キューを空になるまで FIFO で解決する。戦闘が終わったら残りを捨てる。</summary>
        private void RunQueue()
        {
            while (_queue.Count > 0)
            {
                Execute(_queue.Dequeue());
                if (CheckEnd())
                {
                    _queue.Clear();
                    return;
                }
            }
        }

        private bool CheckEnd()
        {
            if (IsOver) { return true; }
            if (!_player.Alive)
            {
                Phase = BattlePhase.Lost;
                Emit(EventKind.Defeat, -1, -1, 0, 0);
                return true;
            }

            foreach (var e in _enemies)
            {
                if (e.Alive) { return false; }
            }

            Phase = BattlePhase.Won;
            Emit(EventKind.Victory, -1, -1, 0, 0);
            return true;
        }

        private void Emit(EventKind kind, int source, int target, int amount, int detail) => _events.Add(new BattleEvent(kind, source, target, amount, detail));

        private void ShuffleInPlace(List<CardRef> pile)
        {
            for (int i = pile.Count - 1; i > 0; i--)
            {
                int j = _shuffleRng.Next(i + 1);
                (pile[i], pile[j]) = (pile[j], pile[i]);
            }
        }
    }
}
