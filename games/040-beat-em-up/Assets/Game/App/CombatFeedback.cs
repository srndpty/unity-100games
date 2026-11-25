using System;
using Game.Core;

namespace Game.App
{
    [Flags]
    public enum CombatCue
    {
        None = 0,
        Swing = 1,
        Hit = 2,
        Hurt = 4,
        Down = 8,
        Defeat = 16,
        Grab = 32,
        Special = 64,
        Death = 128,
        Go = 256,
        Heal = 512,
    }

    /// <summary>前回の描画から起きた出来事を効果音へ変換する。出来事の累計の差で取り出すので、描画の間に何 Tick 進んでも取りこぼさない。</summary>
    public sealed class CombatFeedback
    {
        private Brawl? _seen;
        private int _attacks;
        private int _hits;
        private int _hurts;
        private int _downs;
        private int _defeats;
        private int _grabs;
        private int _specials;
        private int _deaths;
        private int _heals;
        private bool _locked;

        public CombatCue Observe(Brawl? brawl)
        {
            if (!ReferenceEquals(_seen, brawl))
            {
                _seen = brawl;
                _attacks = brawl?.AttacksStarted ?? 0;
                _hits = brawl?.HitCount ?? 0;
                _hurts = brawl?.PlayerHurtCount ?? 0;
                _downs = brawl?.Knockdowns ?? 0;
                _defeats = brawl?.Defeats ?? 0;
                _grabs = brawl?.GrabCount ?? 0;
                _specials = brawl?.SpecialCount ?? 0;
                _deaths = brawl?.Deaths ?? 0;
                _heals = brawl?.HealCount ?? 0;
                _locked = brawl?.Locked ?? true;
                return CombatCue.None;
            }

            if (brawl == null) { return CombatCue.None; }
            var cue = CombatCue.None;
            if (brawl.AttacksStarted > _attacks) { cue |= CombatCue.Swing; }
            if (brawl.HitCount - brawl.PlayerHurtCount > _hits - _hurts) { cue |= CombatCue.Hit; }
            if (brawl.PlayerHurtCount > _hurts) { cue |= CombatCue.Hurt; }
            if (brawl.Knockdowns > _downs) { cue |= CombatCue.Down; }
            if (brawl.Defeats > _defeats) { cue |= CombatCue.Defeat; }
            if (brawl.GrabCount > _grabs) { cue |= CombatCue.Grab; }
            if (brawl.SpecialCount > _specials) { cue |= CombatCue.Special; }
            if (brawl.Deaths > _deaths) { cue |= CombatCue.Death; }
            if (_locked && !brawl.Locked) { cue |= CombatCue.Go; }
            if (brawl.HealCount > _heals) { cue |= CombatCue.Heal; }
            _attacks = brawl.AttacksStarted;
            _hits = brawl.HitCount;
            _hurts = brawl.PlayerHurtCount;
            _downs = brawl.Knockdowns;
            _defeats = brawl.Defeats;
            _grabs = brawl.GrabCount;
            _specials = brawl.SpecialCount;
            _deaths = brawl.Deaths;
            _heals = brawl.HealCount;
            _locked = brawl.Locked;
            return cue;
        }
    }
}
