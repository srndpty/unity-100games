using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// パワーアップの残り時間（Tick）。効果ごとに独立していて、同時に有効になれる。
    /// <para>
    /// 有効期間：Tick T に <see cref="Activate"/> したら、T+1 〜 T+D（D = 効果時間）の D 回の Tick で有効。
    /// 有効中に取り直したら残りを D に戻す（加算しない）。<see cref="Advance"/> は各 Tick の終わりに呼び、
    /// その Tick に発動した効果は減らさない。
    /// </para>
    /// </summary>
    public sealed class EffectTimers
    {
        private static readonly EffectKind[] _allEffects = { EffectKind.Pierce, EffectKind.Expand };

        private readonly BreakoutConfig _config;
        private readonly int[] _remaining = new int[_allEffects.Length];
        private readonly bool[] _activatedThisTick = new bool[_allEffects.Length];

        public EffectTimers(BreakoutConfig config)
        {
            _config = config;
        }

        public bool IsActive(EffectKind effect) => _remaining[(int)effect] > 0;

        /// <summary>残り Tick 数。無効なら 0。</summary>
        public int Remaining(EffectKind effect) => _remaining[(int)effect];

        public void Activate(EffectKind effect)
        {
            _remaining[(int)effect] = _config.DurationTicks(effect);
            _activatedThisTick[(int)effect] = true;
        }

        /// <summary>1 Tick 経過させ、時間切れになった効果を <paramref name="expired"/> に足す。</summary>
        public void Advance(ICollection<EffectKind> expired)
        {
            foreach (var effect in _allEffects)
            {
                var i = (int)effect;
                if (_activatedThisTick[i])
                {
                    _activatedThisTick[i] = false;
                    continue;
                }

                if (_remaining[i] > 0)
                {
                    _remaining[i]--;
                    if (_remaining[i] == 0)
                    {
                        expired.Add(effect);
                    }
                }
            }
        }

        /// <summary>すべての効果を即座に解除する（ミス・面クリア）。</summary>
        public void Clear()
        {
            for (var i = 0; i < _remaining.Length; i++)
            {
                _remaining[i] = 0;
                _activatedThisTick[i] = false;
            }
        }
    }
}
