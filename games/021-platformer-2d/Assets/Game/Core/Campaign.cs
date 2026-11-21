using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    public enum CampaignStatus
    {
        Playing,

        /// <summary>面をクリアし、次の面へ進むのを待っている。</summary>
        LevelCleared,

        /// <summary>ミスして、同じ面をやり直すのを待っている。</summary>
        Missed,
        Won,
        Lost,
    }

    /// <summary>
    /// 3 面を通した 1 試合。残機・スコアを持ち、面の挑戦（<see cref="LevelRun"/>）を作り直す。
    /// 面が終わると止まり、<see cref="Continue"/> で次の面（クリア）か同じ面のやり直し（ミス）へ進む。
    /// </summary>
    public sealed class Campaign
    {
        private readonly PlatformerConfig _config;
        private readonly Level[] _levels;

        public Campaign(PlatformerConfig config, IReadOnlyList<Level> levels)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (levels == null) { throw new ArgumentNullException(nameof(levels)); }
            if (levels.Count == 0) { throw new ArgumentException("面が 1 つもありません。", nameof(levels)); }
            config.Validate();
            _config = config.Copy();
            _levels = new Level[levels.Count];
            for (var i = 0; i < _levels.Length; i++)
            {
                _levels[i] = levels[i] ?? throw new ArgumentException($"{i} 番目の面が null です。", nameof(levels));
            }

            Levels = Array.AsReadOnly(_levels);
            Lives = _config.Lives;
            Run = new LevelRun(_config, _levels[0]);
        }

        public ReadOnlyCollection<Level> Levels { get; }

        public int LevelIndex { get; private set; }

        public LevelRun Run { get; private set; }

        public int Lives { get; private set; }

        public int Score { get; private set; }

        /// <summary>直前に面をクリアしたときの残り時間ボーナス。</summary>
        public int LastTimeBonus { get; private set; }

        public CampaignStatus Status { get; private set; }

        public bool IsOver => Status == CampaignStatus.Won || Status == CampaignStatus.Lost;

        /// <summary>1 Tick 進める。面が終わっている間・試合が終わった後は何もせず false。</summary>
        public bool Tick(PlayerInput input)
        {
            if (Status != CampaignStatus.Playing || !Run.Tick(input))
            {
                return false;
            }

            var events = Run.TakeEvents();
            AddScore((long)events.Stomps * _config.StompScore);
            Pending = Merge(Pending, events);
            if (Run.Status == RunStatus.Cleared)
            {
                LastTimeBonus = (int)Math.Min(int.MaxValue, (long)(Run.RemainingTicks / Units.TickRate) * _config.TimeBonusPerSecond);
                AddScore(LastTimeBonus);
                Status = LevelIndex == _levels.Length - 1 ? CampaignStatus.Won : CampaignStatus.LevelCleared;
            }
            else if (Run.Status == RunStatus.Missed)
            {
                Lives--;
                Status = Lives <= 0 ? CampaignStatus.Lost : CampaignStatus.Missed;
            }

            return true;
        }

        /// <summary>面の終わりから次へ進む。待っていないとき（多重の呼び出しを含む）は何もせず false。</summary>
        public bool Continue()
        {
            switch (Status)
            {
                case CampaignStatus.LevelCleared:
                    LevelIndex++;
                    break;
                case CampaignStatus.Missed:
                    break;
                default:
                    return false;
            }

            Run = new LevelRun(_config, _levels[LevelIndex]);
            Status = CampaignStatus.Playing;
            return true;
        }

        /// <summary>前回取り出してから起きた出来事。</summary>
        public RunEvents TakeEvents()
        {
            var events = Pending;
            Pending = default;
            return events;
        }

        private RunEvents Pending { get; set; }

        private static RunEvents Merge(RunEvents a, RunEvents b) => new(a.Kinds | b.Kinds, a.Stomps + b.Stomps);

        private void AddScore(long points)
        {
            Score = (int)Math.Min(int.MaxValue, Score + points);
        }
    }
}
