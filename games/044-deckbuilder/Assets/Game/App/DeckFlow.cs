using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        HowTo,

        /// <summary>ルートの地図と、今の節の遭遇の選択（ボスの節は挑むだけ）。</summary>
        Route,
        Battle,
        Reward,
        Rest,
        Result,
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core の操作へ変える窓口。
    /// <para>対象選択（<see cref="Targeting"/>）はここが持ち、Core には確定した <c>Play(カード, 対象)</c> だけを渡す。</para>
    /// <para>戦闘の演出（<see cref="Playback"/>）の再生中は戦闘の入力を受け付けず、選択も消す（015）。</para>
    /// <para>シード：試合ごとに <c>firstSeed</c> から 1 ずつ進める（再戦は必ず新しいシード、010）。</para>
    /// </summary>
    public sealed class DeckFlow
    {
        /// <summary>1 秒の Tick 数。</summary>
        public const int TickRate = 60;

        /// <summary>1 フレームで進める Tick の上限（処理落ちのとき演出を飛ばしすぎない）。</summary>
        public const int MaxTicksPerFrame = 8;

        /// <summary>案内の文を出しておく Tick。</summary>
        public const int NoticeTicks = 90;

        private readonly GameData _data;
        private readonly DeckConfig _config;
        private readonly PlaybackTiming _timing;
        private readonly Queue<Intent> _pending = new();
        private int _nextSeed;
        private double _tickDebt;

        public DeckFlow(GameData data, DeckConfig config, PlaybackTiming timing, int firstSeed)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (timing == null) { throw new ArgumentNullException(nameof(timing)); }
            var errors = DeckDataValidator.Validate(data, config);
            if (errors.Count > 0) { throw new ArgumentException("設定かデータが不正です：" + string.Join(" / ", errors), nameof(config)); }
            timing.Validate();
            _config = config.Copy();
            _timing = timing.Copy();
            _nextSeed = firstSeed;
        }

        public GameData Data => _data;

        /// <summary>試合に渡す設定の複製（書き換えても Flow と試合には効かない）。</summary>
        public DeckConfig Config => _config.Copy();

        public AppScreen Screen { get; private set; }

        /// <summary>一時停止中（戦闘・ルート・報酬・休憩の上に重ねる）。演出も止まる。</summary>
        public bool Paused { get; private set; }

        /// <summary>今の挑戦。タイトルでは null。</summary>
        public Run? Run { get; private set; }

        /// <summary>今の戦闘の演出（戦闘がなければ null）。</summary>
        public EventPlayback? Playback { get; private set; }

        public Targeting Targeting { get; } = new();

        /// <summary>始めた挑戦の数（新しい挑戦の始まりを描画が知るため）。</summary>
        public int RunNumber { get; private set; }

        /// <summary>始めた戦闘の数（新しい戦闘の始まりを描画が知るため）。</summary>
        public int BattleNumber { get; private set; }

        public int Seed { get; private set; }

        /// <summary>経過した Tick（案内の表示時間などに使う）。</summary>
        public long Tick { get; private set; }

        /// <summary>案内の文（エネルギーが足りない など）。空なら出さない。</summary>
        public string Notice => Tick < _noticeUntil ? _notice : string.Empty;

        /// <summary>報酬の画面で除去の候補に選んだカード（なければ null）。</summary>
        public int? RemovalCandidate { get; private set; }

        /// <summary>終了が求められた（Unity 層がアプリを閉じる）。</summary>
        public bool QuitRequested { get; private set; }

        /// <summary>戦闘の入力を受け付けるか（自分の番・演出が終わっている・一時停止でない）。</summary>
        public bool AcceptsBattleInput =>
            Screen == AppScreen.Battle && !Paused && Run?.Battle != null && !Run.Battle.IsOver && Playback != null && !Playback.Busy;

        private string _notice = string.Empty;
        private long _noticeUntil;

        /// <summary>意図を列に積む（届いた順に <see cref="Advance"/> で処理する）。</summary>
        public void Submit(Intent intent) => _pending.Enqueue(intent);

        /// <summary>
        /// 積んだ意図を順に処理し、経過時間ぶん Tick を進める（1 回の呼び出しで最大 <see cref="MaxTicksPerFrame"/>）。
        /// 上限を超えた遅れは追いつかずに捨てる（処理落ちの後は演出の時間を飛ばす）。
        /// </summary>
        public void Advance(double seconds)
        {
            while (_pending.Count > 0) { Apply(_pending.Dequeue()); }
            if (!double.IsFinite(seconds) || seconds < 0) { seconds = 0; }
            _tickDebt = Math.Min(_tickDebt + (seconds * TickRate), MaxTicksPerFrame);
            var ticks = (int)Math.Floor(_tickDebt);
            _tickDebt -= ticks;
            for (int i = 0; i < ticks; i++) { Step(); }
        }

        /// <summary>1 Tick 進める（撮影・テストは直接呼ぶ）。一時停止の間は時計（Tick・通知の期限・演出）を止める。</summary>
        public void Step()
        {
            if (Paused) { return; }
            Tick++;
            Playback?.Step();
            SyncAfterBattle();
        }

        /// <summary>演出を最後まで当てはめる（テスト・操縦役の早送り）。</summary>
        public void FinishPlayback()
        {
            Playback?.Finish();
            SyncAfterBattle();
        }

        public void Apply(Intent intent)
        {
            if (Paused)
            {
                ApplyPaused(intent);
                return;
            }

            switch (Screen)
            {
                case AppScreen.Title:
                    if (intent.Kind == IntentKind.Confirm) { Begin(); }
                    else if (intent.Kind == IntentKind.HowTo) { Screen = AppScreen.HowTo; }
                    else if (intent.Kind == IntentKind.Quit) { QuitRequested = true; }
                    break;
                case AppScreen.HowTo:
                    if (intent.Kind is IntentKind.Confirm or IntentKind.Cancel) { Screen = AppScreen.Title; }
                    break;
                case AppScreen.Route:
                    RouteIntent(intent);
                    break;
                case AppScreen.Battle:
                    BattleIntent(intent);
                    break;
                case AppScreen.Reward:
                    RewardIntent(intent);
                    break;
                case AppScreen.Rest:
                    RestIntent(intent);
                    break;
                case AppScreen.Result:
                    if (intent.Kind is IntentKind.Confirm or IntentKind.ToTitle) { ToTitle(); }
                    break;
            }
        }

        private void ApplyPaused(Intent intent)
        {
            if (intent.Kind is IntentKind.Cancel or IntentKind.Confirm) { Paused = false; }
            else if (intent.Kind == IntentKind.ToTitle) { ToTitle(); }
        }

        private void Begin()
        {
            Seed = _nextSeed++;
            RunNumber++;
            Run = new Run(_data, _config, Seed);
            Playback = null;
            Targeting.Reset();
            RemovalCandidate = null;
            _noticeUntil = 0;
            Screen = AppScreen.Route;
        }

        private void ToTitle()
        {
            Paused = false;
            Run = null;
            Playback = null;
            Targeting.Reset();
            RemovalCandidate = null;
            _noticeUntil = 0;
            _pending.Clear();
            Screen = AppScreen.Title;
        }

        private void RouteIntent(Intent intent)
        {
            var run = Run!;
            switch (intent.Kind)
            {
                case IntentKind.Cancel:
                    Paused = true;
                    return;
                case IntentKind.Confirm when run.Options.Count == 1:
                    StartBattle(0);
                    return;
                case IntentKind.Number:
                case IntentKind.Option:
                    StartBattle(intent.Value);
                    return;
            }
        }

        private void StartBattle(int option)
        {
            var run = Run!;
            if (!run.ChooseEncounter(option).Accepted) { return; }
            BattleNumber++;
            Playback = new EventPlayback(run.Battle!, _data, _timing);
            Targeting.Reset();
            Screen = AppScreen.Battle;
        }

        private void BattleIntent(Intent intent)
        {
            if (intent.Kind == IntentKind.Cancel && !Targeting.HasCard)
            {
                Paused = true;
                return;
            }

            if (!AcceptsBattleInput)
            {
                // 演出中の操作は捨て、途中の選択も持ち越さない（015）。
                Targeting.Clear();
                return;
            }

            var battle = Run!.Battle!;
            switch (intent.Kind)
            {
                case IntentKind.Number:
                    var hand = Playback!.Table.Hand;
                    if (intent.Value >= 0 && intent.Value < hand.Count) { SelectCard(hand[intent.Value].Id); }
                    break;
                case IntentKind.SelectCard:
                    SelectCard(intent.Value);
                    break;
                case IntentKind.ClickEnemy:
                    var enemy = battle.EnemyById(intent.Value);
                    if (enemy == null || !enemy.Alive) { break; }
                    Targeting.Cursor = enemy.Id;
                    if (Targeting.HasCard) { PlaySelected(); }
                    break;
                case IntentKind.CycleTarget:
                    if (Targeting.HasCard) { Targeting.Cursor = NextAlive(Targeting.Cursor, intent.Value >= 0 ? 1 : -1); }
                    break;
                case IntentKind.Confirm:
                    if (Targeting.HasCard) { PlaySelected(); }
                    break;
                case IntentKind.Cancel:
                    Targeting.Clear();
                    break;
                case IntentKind.EndTurn:
                    Targeting.Clear();
                    Run.EndTurn();
                    break;
            }
        }

        private void SelectCard(int cardId)
        {
            var battle = Run!.Battle!;
            if (Targeting.CardId == cardId)
            {
                Targeting.Clear();
                return;
            }

            CardRef? card = null;
            foreach (var c in battle.Hand)
            {
                if (c.Id == cardId) { card = c; }
            }

            if (card == null) { return; }
            if (!battle.Affordable(card))
            {
                Targeting.Clear();
                Say("エネルギーが足りない");
                return;
            }

            if (!card.Def.NeedsTarget)
            {
                Targeting.Clear();
                Run.Play(card.Id, null);
                return;
            }

            Targeting.CardId = card.Id;
            var cursor = Targeting.Cursor;
            var current = cursor == null ? null : battle.EnemyById(cursor.Value);
            if (current == null || !current.Alive) { Targeting.Cursor = NextAlive(null, 1); }
        }

        private void PlaySelected()
        {
            var cardId = Targeting.CardId!.Value;
            var target = Targeting.Cursor;
            Targeting.Clear();
            Targeting.Cursor = target;
            var result = Run!.Play(cardId, target);
            if (!result.Accepted && result.Reason == RejectReason.TargetInvalid) { Say("対象を選んでください"); }
        }

        /// <summary>生きている敵を番号順に巡る。</summary>
        private int? NextAlive(int? from, int direction)
        {
            var enemies = Run!.Battle!.Enemies;
            var alive = new List<int>();
            foreach (var e in enemies)
            {
                if (e.Alive) { alive.Add(e.Id); }
            }

            if (alive.Count == 0) { return null; }
            var index = from == null ? -1 : alive.IndexOf(from.Value);
            if (index < 0) { return direction > 0 ? alive[0] : alive[alive.Count - 1]; }
            return alive[(index + direction + alive.Count) % alive.Count];
        }

        private void RewardIntent(Intent intent)
        {
            var run = Run!;
            switch (intent.Kind)
            {
                case IntentKind.Cancel:
                    if (RemovalCandidate != null) { RemovalCandidate = null; }
                    else { Paused = true; }
                    break;
                case IntentKind.Number:
                case IntentKind.Option:
                    if (run.RewardTaken) { Say("カードはもう選んだ"); }
                    else { run.TakeReward(intent.Value); }
                    break;
                case IntentKind.PickRemoval:
                    if (!run.CanRemove)
                    {
                        Say(run.RemovalUsed ? "除去はもう使った" : "これ以上は減らせない");
                        break;
                    }

                    RemovalCandidate = RemovalCandidate == intent.Value ? null : intent.Value;
                    break;
                case IntentKind.ConfirmRemoval:
                    if (RemovalCandidate != null && run.RemoveCard(RemovalCandidate.Value).Accepted) { RemovalCandidate = null; }
                    break;
                case IntentKind.Leave:
                case IntentKind.Confirm:
                    RemovalCandidate = null;
                    run.LeaveReward();
                    Screen = ScreenOf(run.Stage);
                    break;
            }
        }

        private void RestIntent(Intent intent)
        {
            var run = Run!;
            switch (intent.Kind)
            {
                case IntentKind.Cancel:
                    Paused = true;
                    break;
                case IntentKind.Number:
                case IntentKind.Option:
                    if (intent.Value is 0 or 1 && run.Rest(intent.Value == 0 ? RestChoice.Heal : RestChoice.MaxHp).Accepted)
                    {
                        Screen = ScreenOf(run.Stage);
                    }

                    break;
            }
        }

        /// <summary>戦闘が終わり、演出も終わったら次の画面へ。</summary>
        private void SyncAfterBattle()
        {
            if (Screen != AppScreen.Battle || Run == null || Playback == null) { return; }
            if (!Run.Battle!.IsOver || Playback.Busy) { return; }
            Targeting.Clear();
            Screen = ScreenOf(Run.Stage);
        }

        private static AppScreen ScreenOf(RunStage stage) => stage switch
        {
            RunStage.ChooseEncounter => AppScreen.Route,
            RunStage.Battle => AppScreen.Battle,
            RunStage.Reward => AppScreen.Reward,
            RunStage.Rest => AppScreen.Rest,
            _ => AppScreen.Result,
        };

        private void Say(string text)
        {
            _notice = text;
            _noticeUntil = Tick + NoticeTicks;
        }
    }

    /// <summary>戦闘の対象選択の途中の状態。Core は知らない。</summary>
    public sealed class Targeting
    {
        /// <summary>選んだカード（対象を選ぶカードだけ）。</summary>
        public int? CardId { get; internal set; }

        /// <summary>対象のカーソル（敵の番号）。カードを選んでいなくても最後に選んだ敵を覚える。</summary>
        public int? Cursor { get; internal set; }

        public bool HasCard => CardId != null;

        internal void Clear()
        {
            CardId = null;
        }

        /// <summary>戦闘・挑戦が変わったら、カーソルも捨てる（前の戦闘の敵の番号を持ち越さない）。</summary>
        internal void Reset()
        {
            CardId = null;
            Cursor = null;
        }
    }
}
