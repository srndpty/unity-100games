using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Screen = Game.App.Screen;

namespace Game.Unity
{
    internal sealed class RhythmHud
    {
        private static readonly Color Ink = new(.035f, .066f, .11f);
        private static readonly Color Pulse = new(.21f, .89f, .83f);
        private static readonly Color Spark = new(1f, .66f, .37f);
        private readonly VisualElement _selection;
        private readonly VisualElement _play;
        private readonly VisualElement _paused;
        private readonly VisualElement _result;
        private readonly Label _offset;
        private readonly Label _song;
        private readonly Label _judgement;
        private readonly Label _score;
        private readonly Label _summary;
        private readonly Button[] _songs;
        private readonly VisualElement[] _lanes;
        private readonly List<VisualElement> _notes = new();
        private Scorecard _shownScore;

        public RhythmHud(VisualElement root, Font font, IReadOnlyList<Arrangement> tracks, Action<int> choose,
            Action<int> offset, Action submit, Action pause, Action select)
        {
            root.Clear();
            root.style.unityFont = font;
            root.style.flexGrow = 1;
            root.style.backgroundColor = Ink;
            var shell = new VisualElement();
            shell.AddToClassList("shell");
            root.Add(shell);

            _selection = Section(shell);
            Text(_selection, "リズムゲーム", "title");
            Text(_selection, "曲を選んで Enter で開始。F と J でノーツを打つ。", "instruction");
            _songs = new Button[tracks.Count];
            for (var i = 0; i < tracks.Count; i++)
            {
                var index = i;
                _songs[i] = Button(_selection, $"{tracks[i].Song.Name}   /   {tracks[i].Tempo} BPM", () => choose(index));
                _songs[i].AddToClassList("track");
            }

            var adjustment = Row(_selection);
            Button(adjustment, "− 5 ms", () => offset(-5));
            _offset = Text(adjustment, "", "offset");
            Button(adjustment, "+ 5 ms", () => offset(5));
            Text(_selection, "打鍵が遅く判定されるときは補正値を上げる。", "instruction");
            Button(_selection, "演奏開始", submit);

            _play = Section(shell);
            var top = Row(_play);
            _song = Text(top, "", "title");
            _song.style.flexGrow = 1;
            Button(top, "一時停止  Esc", pause);
            _score = Text(_play, "", "score");
            _lanes = new[] { Lane(_play, "F", Pulse), Lane(_play, "J", Spark) };
            _judgement = Text(_play, "", "judgement");

            _paused = Section(shell);
            Text(_paused, "一時停止", "title");
            Button(_paused, "再開  Esc", pause);
            Button(_paused, "曲選択へ  T", select);

            _result = Section(shell);
            Text(_result, "演奏結果", "title");
            _summary = Text(_result, "", "summary");
            Button(_result, "もう一度  Enter", submit);
            Button(_result, "曲選択へ  T", select);
        }

        public void Render(RhythmSession session, int track, int offset, float leadSeconds)
        {
            Show(_selection, session.State == Screen.Select);
            Show(_play, session.State == Screen.Play);
            Show(_paused, session.State == Screen.Paused);
            Show(_result, session.State == Screen.Result);
            for (var i = 0; i < _songs.Length; i++)
            {
                _songs[i].EnableInClassList("selected", i == track);
            }

            _offset.text = $"入力補正   {offset:+0;-0;0} ms";
            var score = session.Score;
            if (score == null) { return; }
            if (session.State == Screen.Result)
            {
                _summary.text = $"{score.Song.Name}\n{score.Points:N0} 点\n良 {score.Good}    可 {score.Fair}    ミス {score.Miss}\n最大コンボ {score.BestCombo}";
                return;
            }

            if (session.State != Screen.Play) { return; }
            if (_shownScore != score) { BuildNotes(score); }
            _song.text = score.Song.Name;
            _score.text = $"{score.Points:N0} 点    /    {score.Combo} コンボ";
            _judgement.text = session.Position < 0 ? "まもなく開始" : score.Last switch
            {
                Grade.Good => "良",
                Grade.Fair => "可",
                Grade.Miss => "ミス",
                _ => "",
            };
            var lead = leadSeconds * Song.SampleRate;
            for (var i = 0; i < _notes.Count; i++)
            {
                var distance = score.Song.Notes[i].Sample - session.Position;
                var visible = score.GradeAt(i) == Grade.None && distance >= -lead / 8 && distance <= lead;
                Show(_notes[i], visible);
                if (visible) { _notes[i].style.left = Length.Percent(12 + 84 * distance / lead); }
            }
        }

        private void BuildNotes(Scorecard score)
        {
            foreach (var existing in _notes) { existing.RemoveFromHierarchy(); }
            _notes.Clear();
            _shownScore = score;
            foreach (var note in score.Song.Notes)
            {
                var pulse = note.Strike == Strike.Pulse;
                var marker = new VisualElement { pickingMode = PickingMode.Ignore };
                marker.AddToClassList(pulse ? "pulse" : "spark");
                marker.style.backgroundColor = pulse ? Pulse : Spark;
                _lanes[pulse ? 0 : 1].Add(marker);
                _notes.Add(marker);
            }
        }

        private static VisualElement Lane(VisualElement parent, string key, Color color)
        {
            var lane = new VisualElement();
            lane.AddToClassList("lane");
            parent.Add(lane);
            var line = new VisualElement();
            line.AddToClassList("hit-line");
            line.style.backgroundColor = color;
            lane.Add(line);
            Text(lane, key, "lane-key").style.color = color;
            return lane;
        }

        private static VisualElement Section(VisualElement parent)
        {
            var section = new VisualElement();
            section.AddToClassList("section");
            parent.Add(section);
            return section;
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            parent.Add(row);
            return row;
        }

        private static Label Text(VisualElement parent, string text, string style)
        {
            var label = new Label(text);
            label.AddToClassList(style);
            parent.Add(label);
            return label;
        }

        private static Button Button(VisualElement parent, string text, Action action)
        {
            var button = new Button(action) { text = text, focusable = false };
            parent.Add(button);
            return button;
        }

        private static void Show(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
