using System;

namespace Game.Core
{
    /// <summary>チームごとの集計。</summary>
    public sealed class TeamStats
    {
        public int Goals { get; internal set; }
        public int Shots { get; internal set; }
        public int Passes { get; internal set; }
        public int PassesCompleted { get; internal set; }
        public int Lobs { get; internal set; }
        public int Tackles { get; internal set; }

        /// <summary>球を持っていた Tick。</summary>
        public int PossessionTicks { get; internal set; }

        /// <summary>相手から球を取り返した回数（保持・タックル）。</summary>
        public int Recoveries { get; internal set; }

        /// <summary>取り返した位置の、自陣のゴールからの距離の和（x 方向、単位）。</summary>
        public long RecoveryDistanceSum { get; internal set; }

        /// <summary>取り返した位置の、自陣のゴールからの平均の距離（m の 1000 倍。回数 0 なら 0）。</summary>
        public long AverageRecoveryDistance => Recoveries == 0 ? 0 : RecoveryDistanceSum / Recoveries;

        internal TeamStats Clone() => (TeamStats)MemberwiseClone();
    }

    /// <summary>
    /// 試合の集計（結果画面と、方針の差を確かめるテストに使う）。各 Tick の <see cref="Match.Step"/> の後に <see cref="Observe"/> を呼ぶ。
    /// パスは「次に球を止めた選手が同じチーム」なら成功。取り返しは「最後に持っていたチームでないチームの選手が球を止めた・タックルで弾いた」。
    /// </summary>
    public sealed class MatchStats
    {
        private readonly TeamStats[] _teams = { new(), new() };
        private int _pendingPass = -1;
        private int _possession = -1;

        public TeamStats Of(Team team) => _teams[(int)team];

        public MatchStats Clone()
        {
            var c = new MatchStats { _pendingPass = _pendingPass, _possession = _possession };
            c._teams[0] = _teams[0].Clone();
            c._teams[1] = _teams[1].Clone();
            return c;
        }

        public void Observe(Match match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            foreach (var e in match.Events)
            {
                var t = _teams[(int)e.Team];
                switch (e.Kind)
                {
                    case MatchEventKind.Kick:
                        var action = (PlayerAction)e.Value;
                        if (action == PlayerAction.Shoot)
                        {
                            t.Shots++;
                            _pendingPass = -1;
                        }
                        else
                        {
                            t.Passes++;
                            if (action == PlayerAction.Lob) { t.Lobs++; }
                            _pendingPass = (int)e.Team;
                        }

                        break;
                    case MatchEventKind.Capture:
                        if (_pendingPass == (int)e.Team) { t.PassesCompleted++; }
                        _pendingPass = -1;
                        Recovered(match, e.Team);
                        break;
                    case MatchEventKind.Tackle:
                        t.Tackles++;
                        _pendingPass = -1;
                        Recovered(match, e.Team);
                        break;
                    case MatchEventKind.Goal:
                        t.Goals++;
                        _pendingPass = -1;
                        break;
                    case MatchEventKind.Out:
                    case MatchEventKind.SetPiece:
                        _pendingPass = -1;
                        _possession = (int)e.Team;
                        break;
                    default:
                        break;
                }
            }

            if (match.Owner >= 0 && match.Phase == MatchPhase.Play) { _teams[(int)match.Players[match.Owner].Team].PossessionTicks++; }
        }

        private void Recovered(Match match, Team team)
        {
            if (_possession >= 0 && _possession != (int)team)
            {
                var t = _teams[(int)team];
                t.Recoveries++;
                t.RecoveryDistanceSum += Math.Abs(match.Ball.Position.X - Pitch.OwnGoalX(team));
            }

            _possession = (int)team;
        }
    }
}
