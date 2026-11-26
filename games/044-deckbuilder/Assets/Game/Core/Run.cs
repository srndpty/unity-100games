using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    public enum RunStage
    {
        /// <summary>2 つの遭遇から選ぶ（戦 1・戦 2）。</summary>
        ChooseEncounter,
        Battle,

        /// <summary>報酬：カード 3 択から 1 枚（任意）と、デッキから 1 枚除去（任意）。</summary>
        Reward,

        /// <summary>休憩：回復か最大 HP の増加。</summary>
        Rest,
        Victory,
        Defeat,
    }

    public enum RestChoice
    {
        Heal,
        MaxHp,
    }

    /// <summary>ルートの 1 つの選択肢。</summary>
    public sealed class RouteOption
    {
        internal RouteOption(EncounterDef encounter, bool hard)
        {
            Encounter = encounter;
            Hard = hard;
        }

        public EncounterDef Encounter { get; }

        /// <summary>手強い方（報酬の希少度が上がる）。</summary>
        public bool Hard { get; }
    }

    /// <summary>
    /// 1 回の挑戦。ルートは 戦 1（2 択）→ 報酬 → 休憩 → 戦 2（2 択）→ 報酬 → 戦 3 ボス → 結果。
    /// 不正な操作は理由を返して何も変えない。
    /// </summary>
    public sealed class Run
    {
        /// <summary>ルートの節。0 = 戦 1、1 = 休憩、2 = 戦 2、3 = ボス。</summary>
        public const int BattleOneNode = 0;
        public const int RestNode = 1;
        public const int BattleTwoNode = 2;
        public const int BossNode = 3;

        private readonly GameData _data;
        private readonly DeckConfig _config;
        private readonly uint _fingerprint;
        private readonly RouteOption[][] _route;
        private readonly List<CardRef> _deck;
        private CardDef[] _rewards = Array.Empty<CardDef>();
        private readonly int[] _chosen = { -1, -1, -1, -1 };
        private int _nextCardId;
        private RunStage _stage;
        private int _hp;
        private bool _settled = true;
        private int _battlesWon;
        private int _totalTurns;
        private int _cardsPlayed;
        private int _damageTaken;

        public Run(GameData data, DeckConfig config, int seed)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var errors = DeckDataValidator.Validate(data, config);
            if (errors.Count > 0) { throw new ArgumentException("設定かデータが不正です：" + string.Join(" / ", errors), nameof(config)); }
            _config = config.Copy();
            _fingerprint = Fingerprint.Of(data, _config);
            Seed = seed;
            MaxHp = _config.PlayerMaxHp;
            _hp = MaxHp;
            _deck = new List<CardRef>();
            foreach (var def in data.StarterDeck) { _deck.Add(new CardRef(++_nextCardId, def)); }
            Deck = new ReadOnlyCollection<CardRef>(_deck);
            _route = MakeRoute();
            Route = Array.AsReadOnly(Array.ConvertAll(_route, r => (IReadOnlyList<RouteOption>)Array.AsReadOnly(r)));
            Node = BattleOneNode;
            _stage = RunStage.ChooseEncounter;
        }

        public int Seed { get; }

        /// <summary>今の段階。戦闘が終わっていれば（<see cref="Battle"/> を直接操作した場合も）ここで結果を反映する。</summary>
        public RunStage Stage
        {
            get
            {
                Settle();
                return _stage;
            }
        }

        public bool IsOver => Stage is RunStage.Victory or RunStage.Defeat;

        /// <summary>今の節（<see cref="BattleOneNode"/> など）。</summary>
        public int Node { get; private set; }

        public int Hp
        {
            get
            {
                Settle();
                return _hp;
            }
        }

        public int MaxHp { get; private set; }

        public IReadOnlyList<CardRef> Deck { get; }

        /// <summary>節ごとの選択肢（休憩の節は空、ボスの節は 1 つ）。始めから全て決まっている。</summary>
        public IReadOnlyList<IReadOnlyList<RouteOption>> Route { get; }


        /// <summary>今の戦闘（終わった後も次の戦闘まで残る）。</summary>
        public Battle? Battle { get; private set; }

        /// <summary>報酬のカードの候補。</summary>
        public IReadOnlyList<CardDef> RewardOptions
        {
            get
            {
                Settle();
                return Array.AsReadOnly(_rewards);
            }
        }

        /// <summary>この報酬でカードを取ったか。</summary>
        public bool RewardTaken { get; private set; }

        /// <summary>この報酬で除去したか。</summary>
        public bool RemovalUsed { get; private set; }

        /// <summary>除去できる状態か（報酬の画面・まだ使っていない・デッキが下限より多い）。</summary>
        public bool CanRemove => Stage == RunStage.Reward && !RemovalUsed && _deck.Count > _config.MinDeckSize;

        /// <summary>節で選んだ遭遇の番号。未選択は -1。</summary>
        public int ChosenAt(int node) => _chosen[node];

        public int BattlesWon
        {
            get
            {
                Settle();
                return _battlesWon;
            }
        }

        public int TotalTurns
        {
            get
            {
                Settle();
                return _totalTurns;
            }
        }

        public int CardsPlayed
        {
            get
            {
                Settle();
                return _cardsPlayed;
            }
        }

        public int DamageTaken
        {
            get
            {
                Settle();
                return _damageTaken;
            }
        }

        /// <summary>今の節の選択肢。</summary>
        public IReadOnlyList<RouteOption> Options => Route[Node];

        public PlayResult ChooseEncounter(int index)
        {
            if (Stage != RunStage.ChooseEncounter) { return PlayResult.Reject(RejectReason.WrongStage); }
            if (index < 0 || index >= _route[Node].Length) { return PlayResult.Reject(RejectReason.InvalidChoice); }
            _chosen[Node] = index;
            StartBattle(_route[Node][index].Encounter);
            return PlayResult.Ok;
        }

        public PlayResult Play(int cardId, int? target)
        {
            if (Stage != RunStage.Battle) { return PlayResult.Reject(RejectReason.WrongStage); }
            var result = Battle!.Play(cardId, target);
            Settle();
            return result;
        }

        public PlayResult EndTurn()
        {
            if (Stage != RunStage.Battle) { return PlayResult.Reject(RejectReason.WrongStage); }
            var result = Battle!.EndTurn();
            Settle();
            return result;
        }

        public PlayResult TakeReward(int index)
        {
            if (Stage != RunStage.Reward) { return PlayResult.Reject(RejectReason.WrongStage); }
            if (RewardTaken) { return PlayResult.Reject(RejectReason.AlreadyDone); }
            if (index < 0 || index >= _rewards.Length) { return PlayResult.Reject(RejectReason.InvalidChoice); }
            _deck.Add(new CardRef(++_nextCardId, _rewards[index]));
            RewardTaken = true;
            return PlayResult.Ok;
        }

        public PlayResult RemoveCard(int cardId)
        {
            if (Stage != RunStage.Reward) { return PlayResult.Reject(RejectReason.WrongStage); }
            if (RemovalUsed) { return PlayResult.Reject(RejectReason.AlreadyDone); }
            if (_deck.Count <= _config.MinDeckSize) { return PlayResult.Reject(RejectReason.DeckTooSmall); }
            var index = _deck.FindIndex(c => c.Id == cardId);
            if (index < 0) { return PlayResult.Reject(RejectReason.InvalidChoice); }
            _deck.RemoveAt(index);
            RemovalUsed = true;
            return PlayResult.Ok;
        }

        /// <summary>報酬を終えて先へ進む（取らなかったカードは見送り）。</summary>
        public PlayResult LeaveReward()
        {
            if (Stage != RunStage.Reward) { return PlayResult.Reject(RejectReason.WrongStage); }
            _rewards = Array.Empty<CardDef>();
            Node++;
            _stage = Node == RestNode ? RunStage.Rest : RunStage.ChooseEncounter;
            return PlayResult.Ok;
        }

        public PlayResult Rest(RestChoice choice)
        {
            if (Stage != RunStage.Rest) { return PlayResult.Reject(RejectReason.WrongStage); }
            switch (choice)
            {
                case RestChoice.Heal:
                    _hp = Math.Min(MaxHp, _hp + RestHeal);
                    break;
                case RestChoice.MaxHp:
                    MaxHp += _config.RestMaxHpGain;
                    _hp += _config.RestMaxHpGain;
                    break;
                default:
                    return PlayResult.Reject(RejectReason.InvalidChoice);
            }

            Node = BattleTwoNode;
            _stage = RunStage.ChooseEncounter;
            return PlayResult.Ok;
        }

        /// <summary>休憩の回復量（最大 HP の百分率、切り捨て）。</summary>
        public int RestHeal => (int)((long)MaxHp * _config.RestHealPercent / 100);

        public uint Hash()
        {
            var h = StateHash.Start();
            h.Mix(_fingerprint);
            h.Mix(Seed);
            Settle();
            h.Mix((int)_stage);
            h.Mix(Node);
            h.Mix(_hp);
            h.Mix(MaxHp);
            h.Mix(_nextCardId);
            h.Mix(_deck.Count);
            foreach (var c in _deck)
            {
                h.Mix(c.Id);
                h.Mix(c.Def.Index);
            }

            foreach (var c in _chosen) { h.Mix(c); }
            h.Mix(_rewards.Length);
            foreach (var r in _rewards) { h.Mix(r.Index); }
            h.Mix(RewardTaken);
            h.Mix(RemovalUsed);
            h.Mix(_battlesWon);
            h.Mix(_totalTurns);
            h.Mix(_cardsPlayed);
            h.Mix(_damageTaken);
            h.Mix(Battle?.Hash() ?? 0);
            return h.Value;
        }

        private RouteOption[][] MakeRoute()
        {
            var rng = GameRng.For(Seed, 0, RngPurpose.Route);
            var easy = Tier(EncounterTier.Easy);
            var normal = Tier(EncounterTier.Normal);
            var hard = Tier(EncounterTier.Hard);
            var boss = Tier(EncounterTier.Boss);
            var first = easy[rng.Next(easy.Count)];
            var firstHard = normal[rng.Next(normal.Count)];
            normal.Remove(firstHard);
            var second = normal[rng.Next(normal.Count)];
            var secondHard = hard[rng.Next(hard.Count)];
            return new[]
            {
                new[] { new RouteOption(first, false), new RouteOption(firstHard, true) },
                Array.Empty<RouteOption>(),
                new[] { new RouteOption(second, false), new RouteOption(secondHard, true) },
                new[] { new RouteOption(boss[rng.Next(boss.Count)], false) },
            };
        }

        private List<EncounterDef> Tier(EncounterTier tier)
        {
            var list = new List<EncounterDef>();
            foreach (var e in _data.Encounters)
            {
                if (e.Tier == tier) { list.Add(e); }
            }

            return list;
        }

        private void StartBattle(EncounterDef encounter)
        {
            Battle = new Battle(_data, _config, _fingerprint, _deck, _hp, MaxHp, encounter, Seed, Node);
            _stage = RunStage.Battle;
            _settled = false;
        }

        /// <summary>戦闘が終わっていたら、結果（HP・記録・次の段階）を一度だけ反映する。</summary>
        private void Settle()
        {
            if (_settled) { return; }
            var battle = Battle!;
            if (!battle.IsOver) { return; }
            _settled = true;
            _battlesWon += battle.Phase == BattlePhase.Won ? 1 : 0;
            _totalTurns += battle.Turn;
            foreach (var e in battle.Events)
            {
                if (e.Kind == EventKind.CardPlayed) { _cardsPlayed++; }
                if (e.Target == Combatant.PlayerId && e.Kind is EventKind.Damaged or EventKind.PoisonTick) { _damageTaken += e.Amount; }
            }

            _hp = battle.Player.Hp;
            if (battle.Phase == BattlePhase.Lost)
            {
                _stage = RunStage.Defeat;
                return;
            }

            if (Node == BossNode)
            {
                _stage = RunStage.Victory;
                return;
            }

            _stage = RunStage.Reward;
            RewardTaken = false;
            RemovalUsed = false;
            _rewards = RollRewards(_route[Node][_chosen[Node]].Hard);
        }

        /// <summary>報酬の候補：希少度の重みでカードを重複なく引く（カードごとの重み = 希少度の重み）。</summary>
        private CardDef[] RollRewards(bool hard)
        {
            var rng = GameRng.For(Seed, Node, RngPurpose.Reward);
            var weights = hard ? _config.HardRarityWeights : _config.NormalRarityWeights;
            var pool = new List<CardDef>();
            var w = new List<int>();
            foreach (var card in _data.Cards)
            {
                var slot = DeckDataValidator.RewardSlot(card.Rarity);
                if (slot < 0 || weights[slot] <= 0) { continue; }
                pool.Add(card);
                w.Add(weights[slot]);
            }

            var picked = new CardDef[_config.RewardChoices];
            for (int i = 0; i < picked.Length; i++)
            {
                var index = rng.Weighted(w.ToArray());
                picked[i] = pool[index];
                pool.RemoveAt(index);
                w.RemoveAt(index);
            }

            return picked;
        }
    }
}
