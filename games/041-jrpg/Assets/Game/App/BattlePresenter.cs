using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core;

namespace Game.App
{
    public enum BattleInputStep
    {
        RootCommand = 0,
        SpellSelect = 1,
        ItemSelect = 2,
        EnemyTarget = 3,
        AllyTarget = 4,
        Animating = 5,
        Finished = 6,
    }

    public readonly struct SelectableOption
    {
        public SelectableOption(string id, string label, string detail, bool enabled)
        {
            Id = id ?? string.Empty;
            Label = label ?? string.Empty;
            Detail = detail ?? string.Empty;
            Enabled = enabled;
        }

        public string Id { get; }
        public string Label { get; }
        public string Detail { get; }
        public bool Enabled { get; }
    }

    /// <summary>
    /// 戦闘中のコマンド選択と、純粋解決器が返した出来事列の Tick 送り再生を担当する。
    /// 早送りは演出の残り Tick を縮めるだけで、Core の状態・乱数は一切変えない。
    /// </summary>
    public sealed class BattlePresenter
    {
        private static readonly string[] _rootLabels = { "たたかう", "じゅもん", "どうぐ", "ぼうぎょ", "にげる" };

        private readonly World _world;
        private readonly GameRng _battleRng;
        private readonly BattleCommand[] _pendingCommands;
        private readonly ReadOnlyCollection<BattleCommand> _pendingView;
        private readonly int _eventDurationTicks;

        private IReadOnlyList<BattleEvent> _queuedEvents = Array.Empty<BattleEvent>();
        private int _eventIndex;
        private int _eventTicksRemaining;
        private BattleCommandKind _stagingKind;
        private string _stagingActionId = string.Empty;
        private bool _targetRequiresDeadAlly;

        public BattlePresenter(World world, EnemyGroupInfo group, string openingMessage, int eventDurationTicks)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            Battle = new BattleState(world.Data, group ?? throw new ArgumentNullException(nameof(group)));
            _battleRng = world.CreateBattleRng();
            _pendingCommands = new BattleCommand[world.Party.Count];
            _pendingView = Array.AsReadOnly(_pendingCommands);
            _eventDurationTicks = Math.Max(1, eventDurationTicks);

            DisplaySnapshot = BattleSnapshot.Capture(Battle, world.Party, world.Inventory);
            BannerMessage = string.IsNullOrEmpty(openingMessage) ? "魔物が現れた！" : openingMessage;
            BeginCommandPhase();
        }

        public BattleState Battle { get; }
        public BattleSnapshot DisplaySnapshot { get; private set; }
        public BattleInputStep Step { get; private set; }
        public int CurrentActorIndex { get; private set; }
        public int RootCursor { get; private set; }
        public int SubCursor { get; private set; }
        public int TargetCursor { get; private set; }
        public string BannerMessage { get; private set; }
        public string FeedbackMessage { get; private set; } = string.Empty;
        public IReadOnlyList<BattleCommand> PendingCommands => _pendingView;

        // 演出用の直近イベント情報
        public BattleEvent? CurrentEvent { get; private set; }
        public int HitFlashTicks { get; private set; }
        public int ScreenShakeTicks { get; private set; }
        public int LastPopupAmount { get; private set; }
        public BattleSide LastPopupSide { get; private set; }
        public int LastPopupTargetIndex { get; private set; }
        public bool LastPopupIsHeal { get; private set; }

        public static IReadOnlyList<string> RootCommandLabels => _rootLabels;

        public IReadOnlyList<SelectableOption> GetCurrentSpellOptions()
        {
            var actor = _world.Party[CurrentActorIndex];
            var spells = actor.LearnedSpells();
            var list = new List<SelectableOption>(spells.Count);
            for (var i = 0; i < spells.Count; i++)
            {
                var sp = spells[i];
                var ok = BattleRules.CanCast(_world.Data, actor, sp.Id, inBattle: true, out _);
                if (ok && sp.Kind == SpellKind.Revive && !HasAnyDeadAlly())
                {
                    ok = false;
                }

                list.Add(new SelectableOption(sp.Id, sp.Name, $"MP {sp.MpCost}  {sp.Description}", ok));
            }

            return list.AsReadOnly();
        }

        public IReadOnlyList<SelectableOption> GetCurrentItemOptions()
        {
            var raw = _world.Inventory.UsableItems();
            var list = new List<SelectableOption>(raw.Count);
            for (var i = 0; i < raw.Count; i++)
            {
                var id = raw[i].ProductId;
                var remaining = raw[i].Count - CountReservedItems(id, CurrentActorIndex);
                if (remaining <= 0)
                {
                    continue;
                }

                var item = _world.Data.GetItem(id);
                list.Add(new SelectableOption(id, $"{item.Name} x{remaining}", item.Description, enabled: true));
            }

            return list.AsReadOnly();
        }

        public void MoveCursor(Direction dir, AudioCueCounter cues)
        {
            if (Step == BattleInputStep.Animating || Step == BattleInputStep.Finished)
            {
                return;
            }

            var delta = (dir == Direction.Down || dir == Direction.Right) ? 1 : -1;
            FeedbackMessage = string.Empty;

            switch (Step)
            {
                case BattleInputStep.RootCommand:
                    RootCursor = Wrap(RootCursor + delta, _rootLabels.Length);
                    cues.CursorMove++;
                    break;

                case BattleInputStep.SpellSelect:
                    {
                        var opts = GetCurrentSpellOptions();
                        if (opts.Count > 0)
                        {
                            SubCursor = Wrap(SubCursor + delta, opts.Count);
                            cues.CursorMove++;
                        }

                        break;
                    }

                case BattleInputStep.ItemSelect:
                    {
                        var opts = GetCurrentItemOptions();
                        if (opts.Count > 0)
                        {
                            SubCursor = Wrap(SubCursor + delta, opts.Count);
                            cues.CursorMove++;
                        }

                        break;
                    }

                case BattleInputStep.EnemyTarget:
                    TargetCursor = NextLivingEnemy(TargetCursor, delta);
                    cues.CursorMove++;
                    break;

                case BattleInputStep.AllyTarget:
                    TargetCursor = NextValidAlly(TargetCursor, delta, _targetRequiresDeadAlly);
                    cues.CursorMove++;
                    break;
            }
        }

        /// <summary>決定入力を処理する。戦闘が完全に終了して画面遷移すべきとき true を返す。</summary>
        public bool Confirm(AudioCueCounter cues)
        {
            FeedbackMessage = string.Empty;
            if (Step == BattleInputStep.Finished)
            {
                cues.Confirm++;
                return true;
            }

            if (Step == BattleInputStep.Animating)
            {
                // 演出の早送り: 現在のイベントの残り待ちを 0 にして次へ進める
                AdvanceEventStream(cues);
                return false;
            }

            switch (Step)
            {
                case BattleInputStep.RootCommand:
                    HandleRootConfirm(cues);
                    break;

                case BattleInputStep.SpellSelect:
                    HandleSpellConfirm(cues);
                    break;

                case BattleInputStep.ItemSelect:
                    HandleItemConfirm(cues);
                    break;

                case BattleInputStep.EnemyTarget:
                    {
                        var cmd = _stagingKind == BattleCommandKind.Attack
                            ? BattleCommand.Attack(TargetCursor)
                            : BattleCommand.CastSpell(_stagingActionId, TargetCursor);
                        CommitActorCommand(cmd, cues);
                        break;
                    }

                case BattleInputStep.AllyTarget:
                    {
                        var cmd = _stagingKind == BattleCommandKind.Spell
                            ? BattleCommand.CastSpell(_stagingActionId, TargetCursor)
                            : BattleCommand.UseItem(_stagingActionId, TargetCursor);
                        CommitActorCommand(cmd, cues);
                        break;
                    }
            }

            return false;
        }

        public void Cancel(AudioCueCounter cues)
        {
            FeedbackMessage = string.Empty;
            if (Step == BattleInputStep.Animating || Step == BattleInputStep.Finished)
            {
                return;
            }

            if (Step == BattleInputStep.SpellSelect || Step == BattleInputStep.ItemSelect)
            {
                Step = BattleInputStep.RootCommand;
                cues.Cancel++;
                return;
            }

            if (Step == BattleInputStep.EnemyTarget)
            {
                Step = _stagingKind == BattleCommandKind.Spell ? BattleInputStep.SpellSelect : BattleInputStep.RootCommand;
                cues.Cancel++;
                return;
            }

            if (Step == BattleInputStep.AllyTarget)
            {
                Step = _stagingKind == BattleCommandKind.Spell ? BattleInputStep.SpellSelect : BattleInputStep.ItemSelect;
                cues.Cancel++;
                return;
            }

            // RootCommand: 前の生存メンバーへ戻る
            for (var prev = CurrentActorIndex - 1; prev >= 0; prev--)
            {
                if (_world.Party[prev].IsAlive)
                {
                    CurrentActorIndex = prev;
                    _pendingCommands[prev] = BattleCommand.None;
                    RootCursor = 0;
                    BannerMessage = $"{_world.Party[prev].Name}はどうする？";
                    cues.Cancel++;
                    return;
                }
            }
        }

        public void Tick(AudioCueCounter cues)
        {
            if (HitFlashTicks > 0)
            {
                HitFlashTicks--;
            }

            if (ScreenShakeTicks > 0)
            {
                ScreenShakeTicks--;
            }

            if (Step != BattleInputStep.Animating)
            {
                return;
            }

            if (_eventTicksRemaining > 0)
            {
                _eventTicksRemaining--;
            }

            if (_eventTicksRemaining <= 0)
            {
                AdvanceEventStream(cues);
            }
        }

        private void HandleRootConfirm(AudioCueCounter cues)
        {
            switch (RootCursor)
            {
                case 0: // たたかう
                    _stagingKind = BattleCommandKind.Attack;
                    _stagingActionId = string.Empty;
                    TargetCursor = FirstLivingEnemy();
                    Step = BattleInputStep.EnemyTarget;
                    cues.Confirm++;
                    break;

                case 1: // じゅもん
                    {
                        var opts = GetCurrentSpellOptions();
                        if (opts.Count == 0)
                        {
                            FeedbackMessage = "覚えている呪文がない。";
                            cues.Reject++;
                            return;
                        }

                        SubCursor = Math.Clamp(SubCursor, 0, opts.Count - 1);
                        Step = BattleInputStep.SpellSelect;
                        cues.Confirm++;
                        break;
                    }

                case 2: // どうぐ
                    {
                        var opts = GetCurrentItemOptions();
                        if (opts.Count == 0)
                        {
                            FeedbackMessage = "使える道具を持っていない。";
                            cues.Reject++;
                            return;
                        }

                        SubCursor = Math.Clamp(SubCursor, 0, opts.Count - 1);
                        Step = BattleInputStep.ItemSelect;
                        cues.Confirm++;
                        break;
                    }

                case 3: // ぼうぎょ
                    CommitActorCommand(BattleCommand.Defend(), cues);
                    break;

                case 4: // にげる
                    if (Battle.IsBossBattle)
                    {
                        FeedbackMessage = CommandErrorText.Describe(CommandError.CannotFleeBoss);
                        cues.Reject++;
                        return;
                    }

                    for (var i = 0; i < _world.Party.Count; i++)
                    {
                        _pendingCommands[i] = _world.Party[i].IsAlive ? BattleCommand.Flee() : BattleCommand.None;
                    }

                    cues.Confirm++;
                    StartTurnResolution(cues);
                    break;
            }
        }

        private void HandleSpellConfirm(AudioCueCounter cues)
        {
            var opts = GetCurrentSpellOptions();
            if (opts.Count == 0)
            {
                Step = BattleInputStep.RootCommand;
                return;
            }

            SubCursor = Math.Clamp(SubCursor, 0, opts.Count - 1);
            var chosen = opts[SubCursor];
            var actor = _world.Party[CurrentActorIndex];
            if (!BattleRules.CanCast(_world.Data, actor, chosen.Id, inBattle: true, out var err))
            {
                FeedbackMessage = CommandErrorText.Describe(err);
                cues.Reject++;
                return;
            }

            var spell = _world.Data.GetSpell(chosen.Id);
            if (spell.Kind == SpellKind.Revive && !HasAnyDeadAlly())
            {
                FeedbackMessage = "倒れている仲間はいない。";
                cues.Reject++;
                return;
            }

            _stagingKind = BattleCommandKind.Spell;
            _stagingActionId = spell.Id;

            switch (spell.Kind)
            {
                case SpellKind.SingleAttack:
                case SpellKind.GroupAttack:
                    TargetCursor = FirstLivingEnemy();
                    Step = BattleInputStep.EnemyTarget;
                    cues.Confirm++;
                    break;

                case SpellKind.SingleHeal:
                    _targetRequiresDeadAlly = false;
                    TargetCursor = LowestHpRatioLivingAlly();
                    Step = BattleInputStep.AllyTarget;
                    cues.Confirm++;
                    break;

                case SpellKind.Revive:
                    _targetRequiresDeadAlly = true;
                    TargetCursor = FirstDeadAlly();
                    Step = BattleInputStep.AllyTarget;
                    cues.Confirm++;
                    break;

                case SpellKind.AllHeal:
                    CommitActorCommand(BattleCommand.CastSpell(spell.Id, 0), cues);
                    break;
            }
        }

        private void HandleItemConfirm(AudioCueCounter cues)
        {
            var opts = GetCurrentItemOptions();
            if (opts.Count == 0)
            {
                Step = BattleInputStep.RootCommand;
                return;
            }

            SubCursor = Math.Clamp(SubCursor, 0, opts.Count - 1);
            var chosen = opts[SubCursor];
            _stagingKind = BattleCommandKind.Item;
            _stagingActionId = chosen.Id;
            _targetRequiresDeadAlly = false;
            TargetCursor = LowestHpRatioLivingAlly();
            Step = BattleInputStep.AllyTarget;
            cues.Confirm++;
        }

        private void CommitActorCommand(BattleCommand cmd, AudioCueCounter cues)
        {
            if (!BattleRules.ValidateCommand(
                    _world.Data,
                    Battle,
                    _world.Party,
                    _world.Inventory,
                    CurrentActorIndex,
                    cmd,
                    _pendingCommands,
                    out var err))
            {
                FeedbackMessage = CommandErrorText.Describe(err);
                cues.Reject++;
                return;
            }

            _pendingCommands[CurrentActorIndex] = cmd;
            cues.Confirm++;

            for (var next = CurrentActorIndex + 1; next < _world.Party.Count; next++)
            {
                if (_world.Party[next].IsAlive)
                {
                    CurrentActorIndex = next;
                    Step = BattleInputStep.RootCommand;
                    RootCursor = 0;
                    SubCursor = 0;
                    BannerMessage = $"{_world.Party[next].Name}はどうする？";
                    return;
                }

                _pendingCommands[next] = BattleCommand.None;
            }

            StartTurnResolution(cues);
        }

        private void StartTurnResolution(AudioCueCounter cues)
        {
            DisplaySnapshot = BattleSnapshot.Capture(Battle, _world.Party, _world.Inventory);
            _queuedEvents = BattleResolver.ResolveTurn(
                _world.Data,
                Battle,
                _world.Party,
                _world.Inventory,
                _pendingCommands,
                _battleRng);
            _eventIndex = -1;
            Step = BattleInputStep.Animating;
            AdvanceEventStream(cues);
        }

        private void AdvanceEventStream(AudioCueCounter cues)
        {
            while (true)
            {
                _eventIndex++;
                if (_eventIndex >= _queuedEvents.Count)
                {
                    CurrentEvent = null;
                    DisplaySnapshot = BattleSnapshot.Capture(Battle, _world.Party, _world.Inventory);
                    if (Battle.Outcome == BattleOutcome.Ongoing)
                    {
                        BeginCommandPhase();
                    }
                    else
                    {
                        Step = BattleInputStep.Finished;
                    }

                    return;
                }

                var ev = _queuedEvents[_eventIndex];
                DisplaySnapshot.Apply(ev);
                ApplyEventCue(ev, cues);

                // メッセージを持たない内部消費イベント（MpSpent / ItemConsumed / ExpAwarded / TurnStarted）は待ち時間なしで次へ畳む
                if (ev.Kind == BattleEventKind.MpSpent ||
                    ev.Kind == BattleEventKind.ItemConsumed ||
                    ev.Kind == BattleEventKind.ExpAwarded ||
                    ev.Kind == BattleEventKind.TurnStarted)
                {
                    continue;
                }

                CurrentEvent = ev;
                if (ev.Message.Length > 0)
                {
                    BannerMessage = ev.Message;
                }

                _eventTicksRemaining = _eventDurationTicks;
                return;
            }
        }

        private void ApplyEventCue(BattleEvent ev, AudioCueCounter cues)
        {
            switch (ev.Kind)
            {
                case BattleEventKind.ActionDeclared:
                    if (ev.ActionId.Length > 0 && _world.Data.TryGetSpell(ev.ActionId, out _))
                    {
                        cues.Spell++;
                    }
                    else
                    {
                        cues.Attack++;
                    }

                    break;

                case BattleEventKind.DamageDealt:
                    cues.Damage++;
                    LastPopupSide = ev.TargetSide;
                    LastPopupTargetIndex = ev.TargetIndex;
                    LastPopupAmount = ev.Amount;
                    LastPopupIsHeal = false;
                    if (ev.TargetSide == BattleSide.Enemy)
                    {
                        HitFlashTicks = 10;
                    }
                    else if (ev.Amount > 0)
                    {
                        ScreenShakeTicks = 10;
                    }

                    break;

                case BattleEventKind.HpHealed:
                case BattleEventKind.MpRestored:
                case BattleEventKind.UnitRevived:
                    cues.Heal++;
                    LastPopupSide = ev.TargetSide;
                    LastPopupTargetIndex = ev.TargetIndex;
                    LastPopupAmount = ev.Amount;
                    LastPopupIsHeal = true;
                    break;

                case BattleEventKind.LevelUp:
                    cues.LevelUp++;
                    break;

                case BattleEventKind.Victory:
                    cues.Victory++;
                    break;

                case BattleEventKind.PartyWiped:
                    cues.Defeat++;
                    break;
            }
        }

        private void BeginCommandPhase()
        {
            for (var i = 0; i < _pendingCommands.Length; i++)
            {
                _pendingCommands[i] = BattleCommand.None;
            }

            CurrentActorIndex = 0;
            for (var i = 0; i < _world.Party.Count; i++)
            {
                if (_world.Party[i].IsAlive)
                {
                    CurrentActorIndex = i;
                    break;
                }
            }

            Step = BattleInputStep.RootCommand;
            RootCursor = 0;
            SubCursor = 0;
            TargetCursor = FirstLivingEnemy();
            BannerMessage = $"{_world.Party[CurrentActorIndex].Name}はどうする？";
            FeedbackMessage = string.Empty;
        }

        private int CountReservedItems(string itemId, int upToExclusive)
        {
            var count = 0;
            for (var i = 0; i < upToExclusive; i++)
            {
                if (_pendingCommands[i].Kind == BattleCommandKind.Item &&
                    string.Equals(_pendingCommands[i].ActionId, itemId, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private int FirstLivingEnemy()
        {
            for (var i = 0; i < Battle.Enemies.Count; i++)
            {
                if (Battle.Enemies[i].IsAlive)
                {
                    return i;
                }
            }

            return 0;
        }

        private int NextLivingEnemy(int current, int delta)
        {
            var n = Battle.Enemies.Count;
            for (var step = 1; step <= n; step++)
            {
                var idx = Wrap(current + (delta * step), n);
                if (Battle.Enemies[idx].IsAlive)
                {
                    return idx;
                }
            }

            return current;
        }

        private bool HasAnyDeadAlly()
        {
            for (var i = 0; i < _world.Party.Count; i++)
            {
                if (!_world.Party[i].IsAlive)
                {
                    return true;
                }
            }

            return false;
        }

        private int FirstDeadAlly()
        {
            for (var i = 0; i < _world.Party.Count; i++)
            {
                if (!_world.Party[i].IsAlive)
                {
                    return i;
                }
            }

            return 0;
        }

        private int LowestHpRatioLivingAlly()
        {
            var best = 0;
            var bestRatio = 2000;
            for (var i = 0; i < _world.Party.Count; i++)
            {
                var m = _world.Party[i];
                if (!m.IsAlive)
                {
                    continue;
                }

                var ratio = (m.Hp * 1000) / Math.Max(1, m.MaxHp);
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    best = i;
                }
            }

            return best;
        }

        private int NextValidAlly(int current, int delta, bool requireDead)
        {
            var n = _world.Party.Count;
            for (var step = 1; step <= n; step++)
            {
                var idx = Wrap(current + (delta * step), n);
                var alive = _world.Party[idx].IsAlive;
                if (requireDead ? !alive : alive)
                {
                    return idx;
                }
            }

            return current;
        }

        private static int Wrap(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            var r = index % count;
            return r < 0 ? r + count : r;
        }
    }
}
