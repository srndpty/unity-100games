using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>CPU の操縦役。プレイヤーと同じ操作だけを一定の間隔で発行する。</summary>
    public sealed class CpuController
    {
        private readonly SporeConfig _config;
        private readonly SeededRandom _random;
        private readonly int _actionIntervalTicks;
        private readonly int _mistakePercent;
        private IReadOnlyList<GameCommand> _plan = Array.AsReadOnly(Array.Empty<GameCommand>());
        private int _planIndex;
        private int _plannedPair = -1;
        private int _wait;

        public CpuController(SporeConfig config, int seed, int actionIntervalTicks, int mistakePercent)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (actionIntervalTicks <= 0) { throw new ArgumentOutOfRangeException(nameof(actionIntervalTicks), actionIntervalTicks, "1 以上を指定してください。"); }
            if (mistakePercent < 0 || mistakePercent > 100) { throw new ArgumentOutOfRangeException(nameof(mistakePercent), mistakePercent, "0〜100 の範囲で指定してください。"); }
            _config = config.Copy();
            _random = new SeededRandom(seed);
            _actionIntervalTicks = actionIntervalTicks;
            _mistakePercent = mistakePercent;
        }

        /// <summary>1 Tick ぶん考え、間隔が来ていれば次の操作を発行する。</summary>
        public void Tick(PlayerField field)
        {
            if (field == null) { throw new ArgumentNullException(nameof(field)); }
            if (field.Phase != FieldPhase.Falling) { return; }
            if (_plannedPair != field.PairsSpawned)
            {
                _plannedPair = field.PairsSpawned;
                _plan = PairPlanner.Plan(_config, field, _random, _mistakePercent);
                _planIndex = 0;
                _wait = _actionIntervalTicks;
            }

            _wait--;
            if (_wait > 0 || _planIndex >= _plan.Count) { return; }
            _wait = _actionIntervalTicks;
            field.Apply(_plan[_planIndex]);
            _planIndex++;
        }
    }
}
