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
        Guard = 4,
        Ko = 8,
    }

    /// <summary>前回の描画から起きた出来事を効果音へ変換する。同種の音は 1 回にまとめる。</summary>
    public sealed class CombatFeedback
    {
        private Duel? _seen;
        private int _attacks;
        private int _hits;
        private int _blocks;
        private int _knockouts;

        public CombatCue Observe(Duel? duel)
        {
            if (!ReferenceEquals(_seen, duel))
            {
                _seen = duel;
                _attacks = 0;
                _hits = 0;
                _blocks = 0;
                _knockouts = 0;
            }

            if (duel == null) { return CombatCue.None; }
            var cue = CombatCue.None;
            if (duel.AttacksStarted > _attacks) { cue |= CombatCue.Swing; }
            if (duel.Hits > _hits) { cue |= CombatCue.Hit; }
            if (duel.Blocks > _blocks) { cue |= CombatCue.Guard; }
            if (duel.Knockouts > _knockouts) { cue |= CombatCue.Ko; }
            _attacks = duel.AttacksStarted;
            _hits = duel.Hits;
            _blocks = duel.Blocks;
            _knockouts = duel.Knockouts;
            return cue;
        }
    }
}
