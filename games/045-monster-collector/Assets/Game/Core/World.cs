using System;
using System.Collections.Generic;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>冒険の場面。戦闘中は <see cref="World.Battle"/> が進み、終わったら <see cref="World.FinishBattle"/> で書き戻す。</summary>
    public enum WorldMode
    {
        /// <summary>最初の 1 匹を選ぶ。</summary>
        StarterPick = 1,

        Field = 2,
        Battle = 3,

        /// <summary>戦闘で覚えられなかった技を、入れ替えるか覚えないか選ぶ。</summary>
        LearnMove = 4,

        /// <summary>3 人のトレーナーに勝った。</summary>
        Cleared = 5,
    }

    public enum InteractKind
    {
        Nothing = 0,

        /// <summary>トレーナーとの戦闘を始めた。</summary>
        Trainer = 1,

        /// <summary>回復所で全員を全快させ、道具を補充した（セーブ・ボックスが使える）。</summary>
        Center = 2,
    }

    /// <summary>戦闘を書き戻した結果（演出の文に使う）。</summary>
    public sealed class BattleAftermath
    {
        internal BattleAftermath(BattleOutcome outcome, TrainerDef? trainer, Monster? captured, RosterPlace place, bool cleared)
        {
            Outcome = outcome;
            Trainer = trainer;
            Captured = captured;
            Place = place;
            Cleared = cleared;
        }

        public BattleOutcome Outcome { get; }

        public TrainerDef? Trainer { get; }

        public Monster? Captured { get; }

        /// <summary>捕まえた個体の行き先。</summary>
        public RosterPlace Place { get; }

        /// <summary>この勝利で 3 人目のトレーナーに勝った。</summary>
        public bool Cleared { get; }
    }

    /// <summary>
    /// 1 回の冒険の状態：手持ちとボックス・図鑑・道具・倒したトレーナー・フィールドの位置・戦闘・遊んだ Tick。
    /// 1 試合 = 1 シード。戦闘の乱数は (シード, 戦闘番号) から派生する。
    /// </summary>
    public sealed class World
    {
        private readonly List<PendingLearn> _pendingLearns = new();

        private World(MonsterData data, int seed, Roster roster, Explorer explorer)
        {
            Data = data;
            Seed = seed;
            Roster = roster;
            Explorer = explorer;
            Fingerprint = Core.Fingerprint.Of(data);
            PendingLearns = _pendingLearns.AsReadOnly();
        }

        public MonsterData Data { get; }

        public int Seed { get; }

        public uint Fingerprint { get; }

        public WorldMode Mode { get; private set; }

        public Roster Roster { get; }

        public Explorer Explorer { get; private set; }

        public Battle? Battle { get; private set; }

        public int Balls { get; private set; }

        public int Potions { get; private set; }

        /// <summary>倒したトレーナーのビット（番号 = <see cref="TrainerDef.Index"/>）。</summary>
        public int TrainersDefeated { get; private set; }

        /// <summary>見た種族のビット。</summary>
        public int Seen { get; private set; }

        /// <summary>捕まえた（手に入れた）種族のビット。</summary>
        public int Caught { get; private set; }

        /// <summary>これまでの戦闘の数（戦闘の乱数の番号）。</summary>
        public int BattleCount { get; private set; }

        public long PlayTicks { get; private set; }

        public int Wins { get; private set; }

        public int Losses { get; private set; }

        public IReadOnlyList<PendingLearn> PendingLearns { get; }

        public static World New(MonsterData data, int seed)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            var w = new World(data, seed, new Roster(data.Rules.PartyMax, data.Rules.BoxMax), Explorer.Start(data, seed))
            {
                Mode = WorldMode.StarterPick,
                Balls = data.Rules.Balls,
                Potions = data.Rules.Potions,
            };
            return w;
        }

        public World Clone()
        {
            var c = new World(Data, Seed, Roster.Clone(), Explorer.Clone())
            {
                Mode = Mode,
                Battle = Battle?.Clone(),
                Balls = Balls,
                Potions = Potions,
                TrainersDefeated = TrainersDefeated,
                Seen = Seen,
                Caught = Caught,
                BattleCount = BattleCount,
                PlayTicks = PlayTicks,
                Wins = Wins,
                Losses = Losses,
            };
            c._pendingLearns.AddRange(_pendingLearns);
            return c;
        }

        public int CaughtCount => BitCount(Caught);

        public int SeenCount => BitCount(Seen);

        public bool IsDefeated(TrainerDef trainer) => (TrainersDefeated & (1 << trainer.Index)) != 0;

        /// <summary>回復所のカウンターの前に立ち、カウンターを向いている（セーブ・ボックスが使える）。</summary>
        public bool AtCenter =>
            Mode == WorldMode.Field &&
            !Explorer.IsMoving &&
            string.Equals(Explorer.MapId, Data.Rules.CenterMapId, StringComparison.Ordinal) &&
            Explorer.X == Data.Rules.CenterX &&
            Explorer.Y == Data.Rules.CenterY &&
            Explorer.Facing == Direction.Up;

        public void AddPlayTicks(long ticks)
        {
            if (ticks > 0 && Mode != WorldMode.Cleared) { PlayTicks += ticks; }
        }

        /// <summary>最初の 1 匹を選ぶ（属性ごとの 3 匹の番号）。</summary>
        public bool ChooseStarter(int index)
        {
            if (Mode != WorldMode.StarterPick || index < 0 || index >= Data.Rules.Starters.Count) { return false; }
            var species = Data.Rules.Starters[index];
            Roster.Receive(Monster.Create(species, Data.Rules.StarterLevel, Data.Rules));
            Seen |= 1 << species.Index;
            Caught |= 1 << species.Index;
            Mode = WorldMode.Field;
            return true;
        }

        /// <summary>フィールドを 1 Tick 進める。草むらで遭遇したら戦闘を始める。</summary>
        public FieldStep FieldTick(Direction? held)
        {
            if (Mode != WorldMode.Field) { return FieldStep.None; }
            var step = Explorer.Tick(Data, TrainersDefeated, held);
            if (step == FieldStep.Encounter)
            {
                var wild = Monster.Create(Explorer.EncounteredSpecies!, Explorer.EncounteredLevel, Data.Rules);
                StartBattle(Battle.Wild(Data, Roster.Party, wild, Balls, Potions, Roster.HasRoom, NextBattleRng()), wild.Species);
            }

            return step;
        }

        /// <summary>向いているマスに話しかける：トレーナーなら戦闘、回復所のカウンターなら全快と補充。</summary>
        public InteractKind Interact(out TrainerDef? trainer)
        {
            trainer = null;
            if (Mode != WorldMode.Field || Explorer.IsMoving) { return InteractKind.Nothing; }
            Explorer.FrontTile(out var fx, out var fy);
            var t = Data.TrainerAt(Explorer.MapId, fx, fy);
            if (t != null && !IsDefeated(t))
            {
                trainer = t;
                var battle = Battle.AgainstTrainer(Data, Roster.Party, t, Balls, Potions, NextBattleRng());
                StartBattle(battle, null);
                foreach (var m in battle.Foes) { Seen |= 1 << m.Species.Index; }
                return InteractKind.Trainer;
            }

            if (AtCenter)
            {
                Roster.RestoreAll();
                Balls = Math.Max(Balls, Data.Rules.Balls);
                Potions = Math.Max(Potions, Data.Rules.Potions);
                return InteractKind.Center;
            }

            return InteractKind.Nothing;
        }

        /// <summary>終わった戦闘を書き戻す。全滅なら回復所の前へ戻って全快、3 人目のトレーナーに勝てばクリア。</summary>
        public BattleAftermath FinishBattle()
        {
            if (Mode != WorldMode.Battle || Battle == null || !Battle.IsOver) { throw new InvalidOperationException("終わった戦闘がありません。"); }
            var b = Battle;
            Roster.ReplaceParty(b.Party);
            Balls = b.Balls;
            Potions = b.Potions;
            var place = RosterPlace.Rejected;
            Monster? captured = null;
            var cleared = false;
            switch (b.Outcome)
            {
                case BattleOutcome.Captured:
                    captured = b.Captured!.Clone();
                    place = Roster.Receive(captured);
                    Caught |= 1 << captured.Species.Index;
                    break;
                case BattleOutcome.Won:
                    Wins++;
                    if (b.Trainer != null)
                    {
                        TrainersDefeated |= 1 << b.Trainer.Index;
                        cleared = TrainersDefeated == (1 << Data.Trainers.Count) - 1;
                    }

                    break;
                case BattleOutcome.Lost:
                    Losses++;
                    Roster.RestoreAll();
                    Explorer.WarpTo(Data.Rules.CenterMapId, Data.Rules.CenterX, Data.Rules.CenterY, Direction.Up);
                    break;
            }

            _pendingLearns.Clear();
            if (b.Outcome != BattleOutcome.Lost) { _pendingLearns.AddRange(b.Pending); }
            Battle = null;
            Explorer.ClearBufferedInput();
            Mode = cleared ? WorldMode.Cleared : _pendingLearns.Count > 0 ? WorldMode.LearnMove : WorldMode.Field;
            if (cleared) { _pendingLearns.Clear(); }
            return new BattleAftermath(b.Outcome, b.Trainer, captured, place, cleared);
        }

        /// <summary>覚えられなかった技の先頭を片づける：forgetSlot の技を忘れて覚えるか、-1 なら覚えない。</summary>
        public bool ResolveLearn(int forgetSlot)
        {
            if (Mode != WorldMode.LearnMove || _pendingLearns.Count == 0) { return false; }
            var p = _pendingLearns[0];
            var m = Roster.Party[p.Member];
            if (forgetSlot < -1 || forgetSlot >= m.Moves.Count) { return false; }
            if (forgetSlot >= 0 && !m.Knows(p.Move)) { m.ReplaceMove(forgetSlot, p.Move); }
            _pendingLearns.RemoveAt(0);
            if (_pendingLearns.Count == 0) { Mode = WorldMode.Field; }
            return true;
        }

        public RosterError Deposit(int partyIndex) => AtCenter ? Roster.Deposit(partyIndex) : RosterError.NoSuchSlot;

        public RosterError Withdraw(int boxIndex) => AtCenter ? Roster.Withdraw(boxIndex) : RosterError.NoSuchSlot;

        public RosterError SwapParty(int a, int b) => Mode == WorldMode.Field ? Roster.SwapParty(a, b) : RosterError.NoSuchSlot;

        public uint Hash()
        {
            var h = StateHash.Start();
            h.Mix(Fingerprint);
            h.Mix(Seed);
            h.Mix((int)Mode);
            Roster.MixHash(ref h);
            Explorer.MixHash(Data, ref h);
            h.Mix(Battle != null);
            Battle?.MixHash(ref h);
            h.Mix(Balls);
            h.Mix(Potions);
            h.Mix(TrainersDefeated);
            h.Mix(Seen);
            h.Mix(Caught);
            h.Mix(BattleCount);
            h.Mix(PlayTicks);
            h.Mix(Wins);
            h.Mix(Losses);
            h.Mix(_pendingLearns.Count);
            foreach (var p in _pendingLearns)
            {
                h.Mix(p.Member);
                h.Mix(p.Move.Index);
            }

            return h.Value;
        }

        // --- セーブからの復元とテスト用 ---

        internal static World Restore(
            MonsterData data,
            int seed,
            IEnumerable<Monster> party,
            IEnumerable<Monster> box,
            Explorer explorer,
            int balls,
            int potions,
            int trainers,
            int seen,
            int caught,
            int battleCount,
            long playTicks,
            int wins,
            int losses)
        {
            var roster = new Roster(data.Rules.PartyMax, data.Rules.BoxMax);
            roster.LoadUnchecked(party, box);
            return new World(data, seed, roster, explorer)
            {
                Mode = WorldMode.Field,
                Balls = balls,
                Potions = potions,
                TrainersDefeated = trainers,
                Seen = seen,
                Caught = caught,
                BattleCount = battleCount,
                PlayTicks = playTicks,
                Wins = wins,
                Losses = losses,
            };
        }

        internal void SetItemsForTest(int balls, int potions)
        {
            Balls = balls;
            Potions = potions;
        }

        internal void DefeatTrainerForTest(int index) => TrainersDefeated |= 1 << index;

        internal void WarpForTest(string mapId, int x, int y, Direction facing) => Explorer.WarpTo(mapId, x, y, facing);

        /// <summary>テスト用：野生の戦闘を直接始める。</summary>
        internal void StartWildForTest(Monster wild) =>
            StartBattle(Battle.Wild(Data, Roster.Party, wild, Balls, Potions, Roster.HasRoom, NextBattleRng()), wild.Species);

        private RandomSeries NextBattleRng()
        {
            BattleCount++;
            return RandomSeries.Derive(Seed, BattleCount, (int)RngPurpose.Battle);
        }

        private void StartBattle(Battle battle, SpeciesDef? wild)
        {
            Battle = battle;
            Mode = WorldMode.Battle;
            if (wild != null) { Seen |= 1 << wild.Index; }
        }

        private static int BitCount(int bits)
        {
            var n = 0;
            for (var b = bits; b != 0; b &= b - 1) { n++; }
            return n;
        }
    }
}
