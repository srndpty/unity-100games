using System;
using Game.Core;

namespace Game.App
{
    /// <summary>撮影・検証用。短い未来の上昇/待機を探索し、最初の入力だけを実行する。</summary>
    public static class Autopilot
    {
        private const int BeamWidth = 24;
        private const int Horizon = 90;
        private struct Candidate
        {
            public float Y;
            public float Velocity;
            public float Cost;
            public bool FirstFlap;
        }
        public static FlappyCommand Next(FlappyState state, FlappyConfig config)
        {
            if (state == null) { throw new ArgumentNullException(nameof(state)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var beam = new Candidate[BeamWidth]; var choices = new Candidate[BeamWidth * 2];
            beam[0] = new Candidate { Y = state.BirdY, Velocity = state.Velocity }; var count = 1;
            var move = state.Speed / FlappyConfig.TickRate;
            for (var tick = 0; tick < Horizon; tick += 3)
            {
                var generated = 0;
                for (var i = 0; i < count; i++)
                {
                    for (var action = 0; action < 2; action++)
                    {
                        var candidate = beam[i]; if (tick == 0) { candidate.FirstFlap = action == 1; }
                        if (action == 1) { candidate.Velocity = config.FlapSpeed; candidate.Cost += .03f; }
                        var alive = true;
                        for (var step = 0; step < 3 && alive; step++)
                        {
                            candidate.Velocity = Math.Max(-config.TerminalSpeed, candidate.Velocity + config.Gravity / FlappyConfig.TickRate);
                            var dy = candidate.Velocity / FlappyConfig.TickRate;
                            if (Math.Abs(candidate.Y + dy) + config.BirdRadius >= FlappyConfig.Height / 2) { alive = false; break; }
                            var nearest = float.PositiveInfinity; var target = 0f;
                            for (var p = 0; p < state.PipeCount; p++)
                            {
                                var pipe = state.PipeAt(p); var x = pipe.X - move * (tick + step);
                                var left = x - config.PipeWidth / 2; var right = x + config.PipeWidth / 2;
                                if (right >= FlappyConfig.BirdX - config.BirdRadius && x < nearest) { nearest = x; target = pipe.Center; }
                                if (right < FlappyConfig.BirdX - config.BirdRadius || left > FlappyConfig.BirdX + config.BirdRadius + move) { continue; }
                                if (Sweep.CircleRectangle(FlappyConfig.BirdX, candidate.Y, move, dy, config.BirdRadius, left, right, -7, pipe.Center - pipe.Gap / 2) <= 1
                                    || Sweep.CircleRectangle(FlappyConfig.BirdX, candidate.Y, move, dy, config.BirdRadius, left, right, pipe.Center + pipe.Gap / 2, 7) <= 1)
                                { alive = false; break; }
                            }
                            candidate.Y += dy;
                            var error = candidate.Y - target;
                            candidate.Cost += error * error * .02f + candidate.Velocity * candidate.Velocity * .0004f;
                        }
                        if (alive) { choices[generated++] = candidate; }
                    }
                }
                if (generated == 0) { return new FlappyCommand(beam[0].FirstFlap); }
                Array.Sort(choices, 0, generated, CandidateComparer.Instance);
                count = 0;
                for (var i = 0; i < generated && count < BeamWidth; i++)
                {
                    var duplicate = false;
                    for (var j = 0; j < count; j++)
                    {
                        if (Math.Abs(beam[j].Y - choices[i].Y) < .08f && Math.Abs(beam[j].Velocity - choices[i].Velocity) < .3f) { duplicate = true; break; }
                    }
                    if (!duplicate) { beam[count++] = choices[i]; }
                }
            }
            return new FlappyCommand(beam[0].FirstFlap);
        }
        private sealed class CandidateComparer : System.Collections.Generic.IComparer<Candidate>
        {
            public static readonly CandidateComparer Instance = new();
            public int Compare(Candidate x, Candidate y) => x.Cost.CompareTo(y.Cost);
        }
    }
}
