using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum Screen { Title, Help, Case, Result }
    public enum Operation { Start, Help, Title, SelectTestimony, SelectEvidence, Question, Present }

    public sealed class CaseFlow
    {
        private readonly CaseFile _file;
        private readonly int _attempts;
        private readonly Queue<(Operation Kind, int Id, int Version)> _pending = new();
        private bool _focused = true;

        public CaseFlow(CaseFile file, int attempts)
        {
            _file = file;
            _attempts = attempts;
            Case = new Investigation(file, attempts);
        }

        public int ViewVersion { get; private set; }
        public Screen Screen { get; private set; }
        public Investigation Case { get; private set; }
        public int Testimony { get; private set; }
        public int Evidence { get; private set; }
        public ActionResult LastAction { get; private set; }
        public string Notice { get; private set; } = string.Empty;
        public bool CanPresent => Case.Target >= 0 && Testimony == Case.Target && Case.Owns(Evidence);

        public void Focus(bool focused)
        {
            _focused = focused;
            if (!focused) { _pending.Clear(); }
        }

        public void Enqueue(Operation operation, int id = -1, int? viewVersion = null)
        {
            if (_focused) { _pending.Enqueue((operation, id, viewVersion ?? ViewVersion)); }
        }

        public bool Flush()
        {
            var changed = false;
            while (_pending.TryDequeue(out var action))
            {
                if (action.Version != ViewVersion || !Apply(action.Kind, action.Id)) { continue; }

                // Unity は受理した操作の後に画面を描き直す。古い表示上の入力を次の状態に持ち越さない。
                ViewVersion++;
                changed = true;
            }

            return changed;
        }

        private bool Apply(Operation operation, int id)
        {
            switch (operation)
            {
                case Operation.Title when Screen != Screen.Title:
                    Screen = Screen.Title;
                    return true;
                case Operation.Help when Screen == Screen.Title:
                    Screen = Screen.Help;
                    return true;
                case Operation.Start when Screen is Screen.Title or Screen.Help or Screen.Result:
                    Case = new Investigation(_file, _attempts);
                    (Testimony, Evidence) = (0, 0);
                    Notice = "証言を選び、尋問して記録を集めましょう。";
                    LastAction = ActionResult.Rejected;
                    Screen = Screen.Case;
                    return true;
            }

            if (Screen != Screen.Case) { return false; }
            switch (operation)
            {
                case Operation.SelectTestimony when Case.IsUnlocked(id):
                    Testimony = id;
                    return true;
                case Operation.SelectEvidence when Case.Owns(id):
                    Evidence = id;
                    return true;
                case Operation.Question when Case.Question(Testimony) == ActionResult.Acquired:
                    LastAction = ActionResult.Acquired;
                    Notice = "事件資料に証拠を追加しました。";
                    return true;
                case Operation.Present when CanPresent:
                    LastAction = Case.Present(Testimony, Evidence);
                    Notice = LastAction is ActionResult.Incorrect or ActionResult.Lost
                        ? "その証拠では証言の矛盾を証明できません。"
                        : Case.Finding;
                    if (Case.Outcome != Outcome.Playing) { Screen = Screen.Result; }
                    return true;
                default:
                    return false;
            }
        }
    }
}
