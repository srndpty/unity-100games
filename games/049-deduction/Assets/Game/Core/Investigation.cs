using System;

namespace Game.Core
{
    public enum Outcome { Playing, Won, Lost }
    public enum ActionResult { Rejected, Acquired, Incorrect, Correct, Won, Lost }

    public sealed class Investigation
    {
        private readonly bool[] _unlocked;
        private readonly bool[] _owned;
        private readonly bool[] _questioned;
        private readonly Match?[] _proved;
        private int _stage;

        public Investigation(CaseFile file, int attempts)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            if (attempts <= 0) { throw new ArgumentOutOfRangeException(nameof(attempts)); }
            Attempts = attempts;
            _unlocked = new bool[file.Testimony.Count];
            _questioned = new bool[file.Testimony.Count];
            _proved = new Match?[file.Testimony.Count];
            _owned = new bool[file.Evidence.Count];
            _unlocked[0] = _unlocked[1] = _owned[0] = _owned[1] = true;
        }

        public CaseFile File { get; }
        public int Attempts { get; private set; }
        public Outcome Outcome { get; private set; }
        public int Target => Outcome == Outcome.Playing ? File.Matches[_stage].TestimonyId : -1;
        public string Finding { get; private set; } = string.Empty;

        public Match? ProofOf(int statement) => statement >= 0 && statement < _proved.Length ? _proved[statement] : null;

        public bool IsUnlocked(int id) => Contains(_unlocked, id);
        public bool Owns(int id) => Contains(_owned, id);
        public bool CanQuestion(int id) => Outcome == Outcome.Playing && IsUnlocked(id) && !_questioned[id];

        public ActionResult Question(int id)
        {
            if (!CanQuestion(id)) { return ActionResult.Rejected; }
            _questioned[id] = true;
            foreach (var reward in File.Testimony[id].Rewards) { _owned[reward] = true; }
            return ActionResult.Acquired;
        }

        public ActionResult Present(int statement, int evidence)
        {
            if (Target < 0 || statement != Target || !Owns(evidence)) { return ActionResult.Rejected; }
            var match = File.Matches[_stage];
            if (match.EvidenceId != evidence)
            {
                Attempts--;
                if (Attempts > 0) { return ActionResult.Incorrect; }
                Outcome = Outcome.Lost;
                return ActionResult.Lost;
            }

            _proved[statement] = match;
            Finding = match.Finding;
            foreach (var id in match.Unlock) { _unlocked[id] = true; }
            if (match.Next < 0)
            {
                Outcome = Outcome.Won;
                return ActionResult.Won;
            }

            _stage = match.Next;
            return ActionResult.Correct;
        }

        private static bool Contains(bool[] flags, int id) => id >= 0 && id < flags.Length && flags[id];
    }
}
