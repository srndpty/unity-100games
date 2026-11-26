using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 戦闘の出来事を順に <see cref="TableModel"/> へ当てはめて再生する。Tick で進むので撮影でも同じ絵になる。
    /// 出来事は再生を始めた時点で卓に当てはめ、その長さだけ次を待つ。
    /// </summary>
    public sealed class EventPlayback
    {
        private readonly Battle _battle;
        private readonly PlaybackTiming _timing;
        private int _wait;

        public EventPlayback(Battle battle, GameData data, PlaybackTiming timing)
        {
            _battle = battle ?? throw new ArgumentNullException(nameof(battle));
            _timing = timing ?? throw new ArgumentNullException(nameof(timing));
            Table = new TableModel(battle, data);
        }

        public TableModel Table { get; }

        /// <summary>卓に当てはめた出来事の数（<see cref="Events"/> の先頭から）。</summary>
        public int Cursor { get; private set; }

        public IReadOnlyList<BattleEvent> Events => _battle.Events;

        /// <summary>今の演出の出来事（まだなければ null）。</summary>
        public BattleEvent? Current { get; private set; }

        /// <summary>今の出来事を当てはめてからの Tick。</summary>
        public int Age { get; private set; }

        /// <summary>まだ再生していない出来事があるか、演出の途中か。</summary>
        public bool Busy => _wait > 0 || Cursor < _battle.Events.Count;

        /// <summary>1 Tick 進める。</summary>
        public void Step()
        {
            Age++;
            if (_wait > 0)
            {
                _wait--;
                if (_wait > 0) { return; }
            }

            var events = _battle.Events;
            while (Cursor < events.Count)
            {
                var e = events[Cursor++];
                Table.Apply(e);
                Current = e;
                Age = 0;
                var ticks = _timing.TicksOf(e);
                if (ticks > 0)
                {
                    _wait = ticks;
                    return;
                }
            }
        }

        /// <summary>残りを全て当てはめる（演出を飛ばす）。</summary>
        public void Finish()
        {
            while (Cursor < _battle.Events.Count) { Table.Apply(_battle.Events[Cursor++]); }
            _wait = 0;
        }
    }
}
