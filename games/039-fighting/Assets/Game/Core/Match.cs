using System;

namespace Game.Core
{
    public enum MatchPhase
    {
        Intro,
        Fight,
        RoundEnd,
        MatchEnd,
    }

    public enum MatchResult
    {
        None,
        Left,
        Right,
        Draw,
    }

    /// <summary>ラウンドの待ちと、2 本先取。引き分けは両者に 1 本。同時に 2 本なら延長し、上限で打ち切る。</summary>
    public sealed class Match
    {
        private readonly MoveTable _table;
        private readonly DuelConfig _config;
        private readonly int _left;
        private readonly int _right;
        private bool _decided;
        private MatchResult _pending;

        public Match(MoveTable table, DuelConfig config, int leftCharacter, int rightCharacter)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _table = table;
            _config = config.Validate();
            _left = leftCharacter;
            _right = rightCharacter;
            Wins = new int[2];
            BeginRound();
        }

        public MatchPhase Phase { get; private set; }

        public MatchResult Result { get; private set; }

        public int Round { get; private set; }

        public int Clock { get; private set; }

        public int PhaseLeft { get; private set; }

        public int[] Wins { get; }

        public Duel Duel { get; private set; } = null!;

        public bool IsOver => Phase == MatchPhase.MatchEnd;

        public void Step(Pad left, Pad right)
        {
            if (Phase == MatchPhase.MatchEnd) { return; }
            if (Phase == MatchPhase.Intro)
            {
                PhaseLeft--;
                if (PhaseLeft <= 0) { Phase = MatchPhase.Fight; }
                return;
            }

            if (Phase == MatchPhase.RoundEnd)
            {
                PhaseLeft--;
                if (PhaseLeft > 0) { return; }
                if (_decided)
                {
                    Phase = MatchPhase.MatchEnd;
                    Result = _pending;
                    return;
                }

                BeginRound();
                return;
            }

            Duel.Step(left, right);
            Clock--;
            if (AwardKo()) { return; }
            if (Clock <= 0) { AwardTime(); }
        }

        private void BeginRound()
        {
            Round++;
            Duel = new Duel(_table, _config, _left, _right);
            Clock = _config.RoundTicks;
            Phase = MatchPhase.Intro;
            PhaseLeft = _config.IntroTicks;
        }

        private bool AwardKo()
        {
            bool left = Duel.At(0).Health <= 0;
            bool right = Duel.At(1).Health <= 0;
            if (!left && !right) { return false; }
            if (left && right) { Award(-1); }
            else { Award(left ? 1 : 0); }
            return true;
        }

        private void AwardTime()
        {
            long left = (long)Duel.At(0).Health * Duel.At(1).MaxHealth;
            long right = (long)Duel.At(1).Health * Duel.At(0).MaxHealth;
            if (left > right) { Award(0); }
            else if (right > left) { Award(1); }
            else { Award(-1); }
        }

        private void Award(int winner)
        {
            if (winner < 0)
            {
                Wins[0]++;
                Wins[1]++;
            }
            else
            {
                Wins[winner]++;
            }

            Phase = MatchPhase.RoundEnd;
            PhaseLeft = _config.RoundEndTicks;
            bool leftWon = Wins[0] >= 2;
            bool rightWon = Wins[1] >= 2;
            if (leftWon && rightWon && Wins[0] == Wins[1])
            {
                if (Round >= _config.MaxRounds)
                {
                    _decided = true;
                    _pending = MatchResult.Draw;
                }

                return;
            }

            if (leftWon || rightWon)
            {
                _decided = true;
                _pending = Wins[0] > Wins[1] ? MatchResult.Left : MatchResult.Right;
            }
        }
    }
}
