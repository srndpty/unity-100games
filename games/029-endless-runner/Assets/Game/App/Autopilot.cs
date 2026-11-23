using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 自動操縦。区間（助走・チャンク 1 つ）に入るたびに、Core の探索（<see cref="Planner.Plan"/>）で
    /// 区間の出口を止まった状態で越える操作の列を求め、それを再生する。探索は本物の 1 Tick と掃引を使うので、
    /// 列どおりに動けば触れない。チャンクの検証が通っていれば、どの区間の入口からも列が見つかる。
    /// 試合が変わったら計画を捨てる（023 の知見）。
    /// </summary>
    public sealed class Autopilot : IPilot
    {
        private readonly List<PlanObstacle> _obstacles = new();
        private RunnerRun? _run;
        private Command[]? _plan;
        private int _cursor;

        /// <summary>道が見つからなかった回数（検証が正しければ 0）。</summary>
        public int Failures { get; private set; }

        public Command CommandFor(RunnerRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (!ReferenceEquals(run, _run))
            {
                _run = run;
                _plan = null;
            }

            if (_plan == null || _cursor >= _plan.Length)
            {
                _plan = Replan(run);
                _cursor = 0;
                if (_plan == null)
                {
                    Failures++;
                    return Command.None;
                }
            }

            return _cursor < _plan.Length ? _plan[_cursor++] : Command.None;
        }

        private Command[]? Replan(RunnerRun run)
        {
            var config = run.Config;
            var segment = run.Chunks.SegmentAt(run.Z);
            _obstacles.Clear();
            for (var i = 0; i < run.Chunks.Count; i++)
            {
                var start = run.Chunks.StartAt(i);
                var chunk = run.Chunks.ChunkAt(i);
                if (start > segment.End + config.RowLength || start + chunk.Length(config) < segment.Start - config.RowLength) { continue; }
                var offset = (int)(start - segment.Start);
                foreach (var obstacle in chunk.Obstacles)
                {
                    var box = obstacle.Box(config);
                    _obstacles.Add(new PlanObstacle(obstacle.Kind, obstacle.Lane, box.Z + offset, box.Depth));
                }
            }

            var body = run.Body;
            return run.Rules.Planner.Plan(body.Lane, body.Vertical, (int)(run.Z - segment.Start), (int)(segment.End - segment.Start), segment.Speed, _obstacles);
        }
    }
}
