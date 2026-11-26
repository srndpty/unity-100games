using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>ターンの解決。行動順・技の効果列・命中・ダメージ・状態異常・ターンの終わり・倒れた後。</summary>
    public sealed partial class Battle
    {
        /// <summary>トレーナーが最善手でなく無作為の技を選ぶ確率（%）。</summary>
        public const int TrainerRandomPercent = 20;

        private void Start()
        {
            Turn = 1;
            Phase = BattlePhase.ChooseCommand;
            Active = _party.FindIndex(m => !m.IsFainted);
            FoeActive = 0;
            var started = Emit(BattleEventKind.Started, false, 0, Kind == BattleKind.Trainer ? Trainer!.Name : FoeMonster.Name);
            started.Level = FoeMonster.Level;
            EmitSentOut(false, FoeActive);
            EmitSentOut(true, Active);
            _participated[Active] = true;
        }

        private void ResolveTurn(BattleCommand command)
        {
            var foeSlot = ChooseFoeMove();
            var playerFirst = PlayerActsFirst(command, foeSlot);
            if (playerFirst)
            {
                PlayerAct(command);
                if (!IsOver && !FoeMonster.IsFainted && !PlayerMonster.IsFainted) { FoeAct(foeSlot); }
            }
            else
            {
                FoeAct(foeSlot);
                if (!IsOver && !PlayerMonster.IsFainted && !FoeMonster.IsFainted) { PlayerAct(command); }
            }

            if (IsOver) { return; }
            EndOfTurn();
        }

        /// <summary>交代・道具・逃走は技より先。技どうしは優先度 → 素早さ（段階込み）→ 同じなら戦闘の乱数で 1 回の抽選。</summary>
        private bool PlayerActsFirst(BattleCommand command, int foeSlot)
        {
            if (command.Kind != CommandKind.Fight) { return true; }
            var mine = MoveFor(PlayerMonster, command.Index);
            var theirs = MoveFor(FoeMonster, foeSlot);
            if (mine.Priority != theirs.Priority) { return mine.Priority > theirs.Priority; }
            var mySpeed = Formula.ApplyStage(PlayerMonster.Speed, _playerStages[(int)StatKind.Speed]);
            var foeSpeed = Formula.ApplyStage(FoeMonster.Speed, _foeStages[(int)StatKind.Speed]);
            if (mySpeed != foeSpeed) { return mySpeed > foeSpeed; }
            return _rng.Next(2) == 0;
        }

        private MoveDef MoveFor(Monster m, int slot)
        {
            if (slot < 0 || !m.HasUsableMove()) { return _data.Rules.Struggle!; }
            return m.Moves[slot].Move;
        }

        private void PlayerAct(BattleCommand command)
        {
            switch (command.Kind)
            {
                case CommandKind.Fight:
                    UseMove(true, PlayerMonster.HasUsableMove() ? command.Index : -1);
                    break;
                case CommandKind.Switch:
                    var old = Emit(BattleEventKind.Withdrew, true, Active, PlayerMonster.Name);
                    old.HpAfter = PlayerMonster.Hp;
                    SwitchIn(command.Index);
                    break;
                case CommandKind.Potion:
                    UsePotion(command.Index);
                    break;
                case CommandKind.Ball:
                    ThrowBall();
                    break;
                case CommandKind.Run:
                    TryRun();
                    break;
            }
        }

        private void FoeAct(int slot) => UseMove(false, slot);

        /// <summary>相手の技の枠を決める（PP が尽きていれば -1 = わるあがき）。野生は無作為、トレーナーは一定の確率で無作為・それ以外は最善手。</summary>
        private int ChooseFoeMove()
        {
            var foe = FoeMonster;
            var usable = new List<int>();
            for (int i = 0; i < foe.Moves.Count; i++)
            {
                if (foe.Moves[i].Pp > 0) { usable.Add(i); }
            }

            if (usable.Count == 0) { return -1; }
            if (Kind == BattleKind.Wild) { return usable[_rng.Next(usable.Count)]; }
            if (_rng.Next(100) < TrainerRandomPercent) { return usable[_rng.Next(usable.Count)]; }
            return TrainerAi.Best(_data, foe, PlayerMonster, _foeStages, _playerStages, usable);
        }

        private void UseMove(bool player, int slot)
        {
            var actor = player ? PlayerMonster : FoeMonster;
            var actorSlot = player ? Active : FoeActive;
            if (actor.Status != null && actor.Status.BlocksAction)
            {
                var asleep = Emit(BattleEventKind.Asleep, player, actorSlot, actor.Name);
                asleep.Status = actor.Status;
                return;
            }

            var move = MoveFor(actor, slot);
            if (slot >= 0 && actor.HasUsableMove()) { actor.Moves[slot].Pp--; }
            var used = Emit(BattleEventKind.UsedMove, player, actorSlot, actor.Name);
            used.Move = move;

            // 命中は Next(100) < 命中。命中 100 の技でも 1 回引き、技によって乱数の使用回数を変えない。
            if (_rng.Next(100) >= move.Accuracy)
            {
                Emit(BattleEventKind.Missed, player, actorSlot, actor.Name).Move = move;
                return;
            }

            foreach (var effect in move.Effects)
            {
                if (IsOver) { return; }
                ApplyEffect(player, move, effect);
            }
        }

        private void ApplyEffect(bool player, MoveDef move, EffectDef effect)
        {
            var actor = player ? PlayerMonster : FoeMonster;
            var target = player ? FoeMonster : PlayerMonster;
            var targetSlot = player ? FoeActive : Active;
            var actorStages = player ? _playerStages : _foeStages;
            var targetStages = player ? _foeStages : _playerStages;
            switch (effect.Kind)
            {
                case EffectKind.Damage:
                    {
                        if (target.IsFainted || actor.IsFainted) { return; }
                        var eff = _data.Effectiveness(move.Type, target.Species.Type);
                        var stab = move.Type != null && ReferenceEquals(move.Type, actor.Species.Type);
                        var attack = Formula.ApplyStage(actor.Attack, actorStages[(int)StatKind.Attack]);
                        var defense = Formula.ApplyStage(target.Defense, targetStages[(int)StatKind.Defense]);
                        var random = _rng.Range(Formula.RandomMinPercent, Formula.RandomMaxPercent);
                        var dmg = Formula.Damage(actor.Level, move.Power, attack, Math.Max(1, defense), stab, eff, random);
                        dmg = Math.Min(dmg, target.Hp);
                        target.Hp -= dmg;
                        var ev = Emit(BattleEventKind.Damaged, !player, targetSlot, target.Name);
                        ev.Amount = dmg;
                        ev.HpAfter = target.Hp;
                        ev.MaxHp = target.MaxHp;
                        ev.Effectiveness = eff;
                        ev.Move = move;
                        if (target.IsFainted) { OnFainted(!player, targetSlot); }
                        return;
                    }

                case EffectKind.Inflict:
                    {
                        if (target.IsFainted || actor.IsFainted) { return; }
                        var status = effect.Status!;
                        var hit = _rng.Next(100) < effect.Percent;
                        if (!hit) { return; }
                        if (target.Status != null)
                        {
                            if (move.Power == 0)
                            {
                                var failed = Emit(BattleEventKind.StatusFailed, !player, targetSlot, target.Name);
                                failed.Status = target.Status;
                            }

                            return;
                        }

                        // 持続は付与した時点で決め、「今のターン + 持続」のターンの開始で解除する。
                        var turns = _rng.Range(status.MinTurns, status.MaxTurns);
                        target.SetStatus(status, Turn + turns);
                        var ev = Emit(BattleEventKind.StatusInflicted, !player, targetSlot, target.Name);
                        ev.Status = status;
                        ev.Amount = turns;
                        ev.Turn = Turn + turns;
                        return;
                    }

                case EffectKind.Heal:
                    {
                        if (actor.IsFainted) { return; }
                        var amount = Math.Max(1, actor.MaxHp * effect.Percent / 100);
                        amount = Math.Min(amount, actor.MaxHp - actor.Hp);
                        if (amount <= 0) { return; }
                        actor.Hp += amount;
                        var ev = Emit(BattleEventKind.Healed, player, player ? Active : FoeActive, actor.Name);
                        ev.Amount = amount;
                        ev.HpAfter = actor.Hp;
                        ev.MaxHp = actor.MaxHp;
                        return;
                    }

                case EffectKind.Stat:
                    {
                        var self = effect.Target == EffectTarget.Self;
                        var who = self ? actor : target;
                        if (who.IsFainted) { return; }
                        var stages = self ? actorStages : targetStages;
                        var side = self ? player : !player;
                        var slot = side ? Active : FoeActive;
                        var old = stages[(int)effect.Stat];
                        var now = Math.Max(-MonsterDataValidator.MaxStage, Math.Min(MonsterDataValidator.MaxStage, old + effect.Stages));
                        var ev = Emit(now == old ? BattleEventKind.StatUnchanged : BattleEventKind.StatChanged, side, slot, who.Name);
                        ev.Stat = effect.Stat;
                        ev.Amount = now - old;
                        stages[(int)effect.Stat] = now;
                        return;
                    }
            }
        }

        private void UsePotion(int member)
        {
            var m = _party[member];
            var amount = Math.Min(_data.Rules.PotionHeal, m.MaxHp - m.Hp);
            Potions--;
            m.Hp += amount;
            var ev = Emit(BattleEventKind.PotionUsed, true, member, m.Name);
            ev.Amount = amount;
            ev.HpAfter = m.Hp;
            ev.MaxHp = m.MaxHp;
        }

        private void ThrowBall()
        {
            Balls--;
            var foe = FoeMonster;
            var value = Formula.CaptureValue(foe.Species.CatchRate, foe.Hp, foe.MaxHp, foe.Status?.CatchPercent ?? 100);
            var roll = _rng.Next(256);
            var success = value >= Formula.CertainRoll || roll < value;
            var shakes = success ? 3 : Math.Min(2, value * 3 / (roll + 1));
            var thrown = Emit(BattleEventKind.BallThrown, false, FoeActive, foe.Name);
            thrown.Amount = shakes;
            if (!success)
            {
                Emit(BattleEventKind.BrokeFree, false, FoeActive, foe.Name);
                return;
            }

            foe.ClearStatus();
            Captured = foe;
            Emit(BattleEventKind.Captured, false, FoeActive, foe.Name).Level = foe.Level;
            End(BattleOutcome.Captured);
        }

        private void TryRun()
        {
            var odds = Formula.RunOdds(
                Formula.ApplyStage(PlayerMonster.Speed, _playerStages[(int)StatKind.Speed]),
                Formula.ApplyStage(FoeMonster.Speed, _foeStages[(int)StatKind.Speed]),
                RunAttempts);
            var roll = _rng.Next(256);
            RunAttempts++;
            if (odds >= 256 || roll < odds)
            {
                Emit(BattleEventKind.RanAway, true, Active, PlayerMonster.Name);
                End(BattleOutcome.Ran);
                return;
            }

            Emit(BattleEventKind.RunFailed, true, Active, PlayerMonster.Name);
        }

        private void SwitchIn(int member)
        {
            Active = member;
            Array.Clear(_playerStages, 0, _playerStages.Length);
            _participated[member] = true;
            EmitSentOut(true, member);
        }

        /// <summary>ターンの終わり：毒のダメージ（味方 → 相手）、相手の次の個体、味方の交代先の選択、次のターンの始まり。</summary>
        private void EndOfTurn()
        {
            StatusTick(true);
            if (IsOver) { return; }
            StatusTick(false);
            if (IsOver) { return; }

            if (FoeMonster.IsFainted)
            {
                var next = _foes.FindIndex(m => !m.IsFainted);
                FoeActive = next;
                Array.Clear(_foeStages, 0, _foeStages.Length);
                Array.Clear(_participated, 0, _participated.Length);
                if (!PlayerMonster.IsFainted) { _participated[Active] = true; }
                EmitSentOut(false, next);
            }

            if (PlayerMonster.IsFainted)
            {
                Phase = BattlePhase.ForcedSwitch;
                return;
            }

            BeginTurn();
        }

        private void StatusTick(bool player)
        {
            var m = player ? PlayerMonster : FoeMonster;
            if (m.IsFainted || m.Status == null || m.Status.DotDivisor == 0) { return; }
            var slot = player ? Active : FoeActive;
            var dmg = Math.Min(m.Hp, Math.Max(1, m.MaxHp / m.Status.DotDivisor));
            m.Hp -= dmg;
            var ev = Emit(BattleEventKind.StatusDamage, player, slot, m.Name);
            ev.Status = m.Status;
            ev.Amount = dmg;
            ev.HpAfter = m.Hp;
            ev.MaxHp = m.MaxHp;
            if (m.IsFainted) { OnFainted(player, slot); }
        }

        /// <summary>次のターンを始め、解除予定のターンに達した状態異常を治す（場にいない手持ちも同じ）。</summary>
        private void BeginTurn()
        {
            Turn++;
            CureDue(_party, true);
            CureDue(_foes, false);
        }

        private void CureDue(List<Monster> list, bool player)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m.Status == null || m.IsFainted || Turn < m.StatusEndsTurn) { continue; }
                var ev = Emit(BattleEventKind.StatusCured, player, i, m.Name);
                ev.Status = m.Status;
                ev.Turn = Turn;
                m.ClearStatus();
            }
        }

        private void OnFainted(bool player, int slot)
        {
            var list = player ? _party : _foes;
            Emit(BattleEventKind.Fainted, player, slot, list[slot].Name);
            if (!player) { AwardExp(_foes[slot]); }

            // 自分の残りがいなければ敗北（相手も全滅していても）。
            if (!_party.Exists(m => !m.IsFainted))
            {
                End(BattleOutcome.Lost);
                return;
            }

            if (!_foes.Exists(m => !m.IsFainted)) { End(BattleOutcome.Won); }
        }

        /// <summary>倒した相手の経験値を、その相手と戦った（場に出た）生存者で等分する（端数は切り捨て、最低 1）。</summary>
        private void AwardExp(Monster foe)
        {
            var receivers = new List<int>();
            for (int i = 0; i < _party.Count; i++)
            {
                if (_participated[i] && !_party[i].IsFainted) { receivers.Add(i); }
            }

            if (receivers.Count == 0) { return; }
            var total = Formula.ExpGain(_data.Rules, foe.Species, foe.Level, Kind == BattleKind.Trainer);
            var share = Math.Max(1, total / receivers.Count);
            foreach (var i in receivers)
            {
                var m = _party[i];
                var gained = Emit(BattleEventKind.ExpGained, true, i, m.Name);
                gained.Amount = share;
                foreach (var level in m.GainExp(_data.Rules, share))
                {
                    var up = Emit(BattleEventKind.LeveledUp, true, i, m.Name);
                    up.Level = level;
                    up.HpAfter = m.Hp;
                    up.MaxHp = m.MaxHp;
                    LearnAt(i, m, level);
                }
            }
        }

        private void LearnAt(int member, Monster m, int level)
        {
            foreach (var l in m.Species.Learnset)
            {
                if (l.Level != level || m.Knows(l.Move)) { continue; }
                if (m.Moves.Count < MonsterDataValidator.MaxMoves)
                {
                    m.AddMove(l.Move);
                    Emit(BattleEventKind.LearnedMove, true, member, m.Name).Move = l.Move;
                }
                else
                {
                    _pending.Add(new PendingLearn(member, l.Move));
                    Emit(BattleEventKind.WantsToLearn, true, member, m.Name).Move = l.Move;
                }
            }
        }

        private void End(BattleOutcome outcome)
        {
            Phase = BattlePhase.Ended;
            Outcome = outcome;
            if (outcome == BattleOutcome.Won) { Emit(BattleEventKind.Won, true, Active, Trainer?.Name ?? FoeMonster.Name); }
            if (outcome == BattleOutcome.Lost) { Emit(BattleEventKind.Lost, true, Active, PlayerMonster.Name); }

            // 戦闘が終わると状態異常は治る（持続はこの戦闘のターン番号で数えるため）。
            foreach (var m in _party) { m.ClearStatus(); }
        }

        private void EmitSentOut(bool player, int slot)
        {
            var m = player ? _party[slot] : _foes[slot];
            var ev = Emit(BattleEventKind.SentOut, player, slot, m.Name);
            ev.Species = m.Species;
            ev.Level = m.Level;
            ev.HpAfter = m.Hp;
            ev.MaxHp = m.MaxHp;
        }

        private BattleEvent Emit(BattleEventKind kind, bool player, int slot, string name)
        {
            var ev = new BattleEvent(kind, player, slot, name) { Turn = Turn };
            _log.Add(ev);
            _current?.Add(ev);
            return ev;
        }
    }
}
