using System;

namespace Game.Core
{
    /// <summary>出来事ごとの演出の長さ（Tick、60 Tick = 1 秒）。規則には効かない。遊んで決める値なので設定アセットから渡す（Unity のシリアライズのため Core に置く）。</summary>
    [Serializable]
    public sealed class PlaybackTiming
    {
        public int TurnStarted = 24;
        public int CardPlayed = 10;
        public int EnemyActed = 18;
        public int Damaged = 14;
        public int PoisonTick = 14;
        public int BlockGained = 8;
        public int BlockLost = 4;
        public int StatusApplied = 8;
        public int Drawn = 4;
        public int Shuffled = 14;
        public int Discarded = 2;
        public int Exhausted = 10;
        public int Fizzled = 8;
        public int Died = 18;
        public int Summoned = 18;
        public int Ending = 36;

        /// <summary>全体の速さ（百分率）。200 なら長さが半分。</summary>
        public int SpeedPercent = 100;

        public void Validate()
        {
            foreach (var v in new[] { TurnStarted, CardPlayed, EnemyActed, Damaged, PoisonTick, BlockGained, BlockLost, StatusApplied, Drawn, Shuffled, Discarded, Exhausted, Fizzled, Died, Summoned, Ending })
            {
                if (v < 0 || v > 600) { throw new ArgumentOutOfRangeException(nameof(PlaybackTiming), $"演出の長さは 0〜600 Tick（{v}）"); }
            }

            if (SpeedPercent < 25 || SpeedPercent > 1000) { throw new ArgumentOutOfRangeException(nameof(SpeedPercent), $"速さは 25〜1000%（{SpeedPercent}）"); }
        }

        public PlaybackTiming Copy() => (PlaybackTiming)MemberwiseClone();

        /// <summary>出来事の演出の長さ。0 なら待たずに次へ（ターンの終わりの減少など）。</summary>
        public int TicksOf(BattleEvent e)
        {
            var raw = e.Kind switch
            {
                EventKind.TurnStarted => TurnStarted,
                EventKind.CardPlayed => CardPlayed,
                EventKind.EnemyActed => EnemyActed,
                EventKind.Damaged => Damaged,
                EventKind.PoisonTick => PoisonTick,
                EventKind.BlockGained => BlockGained,
                EventKind.BlockLost => BlockLost,
                EventKind.StatusApplied => e.Amount > 0 ? StatusApplied : 0,
                EventKind.Drawn => Drawn,
                EventKind.Shuffled => Shuffled,
                EventKind.Discarded => Discarded,
                EventKind.Exhausted => Exhausted,
                EventKind.Fizzled => Fizzled,
                EventKind.Died => Died,
                EventKind.Summoned => Summoned,
                EventKind.Victory or EventKind.Defeat => Ending,
                _ => 0,
            };
            return raw == 0 ? 0 : Math.Max(1, raw * 100 / SpeedPercent);
        }
    }
}
