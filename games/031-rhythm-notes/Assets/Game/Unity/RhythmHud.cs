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
        private const float HitLinePercent = 12;
        private const float TravelPercent = 84;
        private static readonly Color Ink = new(.035f, .066f, .11f);
        private static readonly Color Pulse = new(.21f, .89f, .83f);
        private static readonly Color Spark = new(1f, .66f, .37f);
        private static readonly Color FastColor = new(.45f, .72f, 1f);
        private static readonly Color SlowColor = new(1f, .45f, .42f);
        private const int FullComboPopSamples = 280 * Song.SamplesPerMillisecond;
        private const int FullComboFlashSamples = 520 * Song.SamplesPerMillisecond;
        private readonly VisualElement _selection;
        private readonly VisualElement _play;
        private readonly VisualElement _calibration;
        private readonly VisualElement _paused;
        private readonly VisualElement _result;
        private readonly Label _offset;
        private readonly Label _song;
        private readonly Label _judgement;
        private readonly Label _timing;
        private readonly VisualElement _fullComboLayer;
        private readonly VisualElement _fullComboFlash;
        private readonly Label _fullCombo;
        private readonly Label _score;
        private readonly Label _taps;
        private readonly Label _summary;
        private readonly Button[] _songs;
        private readonly VisualElement _barLayer;
        private readonly VisualElement[] _lanes;
        private readonly VisualElement[] _laneBursts;
        private readonly List<VisualElement> _notes = new();
        private readonly List<VisualElement> _bars = new();
        private readonly List<NoteBurst> _bursts = new();
        private Grade[] _seen = Array.Empty<Grade>();
        private Scorecard _shownScore;
        private long? _fullComboBorn;

        public RhythmHud(VisualElement root, Font font, IReadOnlyList<Arrangement> tracks, Action<int> choose,
            Action<int> offset, Action submit, Action calibrate, Action pause, Action select)
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
            Button(adjustment, "− 5 ms", () => offset(-Calibration.StepMs));
            _offset = Text(adjustment, "", "offset");
            Button(adjustment, "+ 5 ms", () => offset(Calibration.StepMs));
            Button(adjustment, "補正テスト  C", calibrate);
            Text(_selection, "音が遅れて聞こえるときは補正値を上げる（ノーツと判定が遅れる）。補正テストで自動で測れる。", "instruction");
            Button(_selection, "演奏開始  Enter", submit);

            _play = Section(shell);
            var top = Row(_play);
            _song = Text(top, "", "title");
            _song.style.flexGrow = 1;
            Button(top, "一時停止  Esc", pause);
            _score = Text(_play, "", "score");
            var field = new VisualElement();
            field.AddToClassList("field");
            _play.Add(field);
            _barLayer = Layer(field);
            var pulseLane = Lane(field, "F", Pulse);
            var sparkLane = Lane(field, "J", Spark);
            _lanes = new[] { pulseLane.Notes, sparkLane.Notes };
            _laneBursts = new[] { pulseLane.Bursts, sparkLane.Bursts };
            _fullComboLayer = Layer(field);
            _fullComboLayer.AddToClassList("banner-layer");
            _fullComboFlash = new VisualElement { pickingMode = PickingMode.Ignore };
            _fullComboFlash.AddToClassList("full-combo-flash");
            _fullComboLayer.Add(_fullComboFlash);
            _fullCombo = Text(_fullComboLayer, "FULL COMBO", "full-combo");
            _fullCombo.pickingMode = PickingMode.Ignore;
            var judgementRow = Row(_play);
            _judgement = Text(judgementRow, "", "judgement");
            _timing = Text(judgementRow, "", "timing");

            _calibration = Section(shell);
            var calibrationTop = Row(_calibration);
            Text(calibrationTop, "補正テスト", "title").style.flexGrow = 1;
            Button(calibrationTop, "一時停止  Esc", pause);
            Text(_calibration, "画面を見ずに、クリック音に合わせて F か J を押す。", "instruction");
            Text(_calibration, "最初の1小節はカウント。続く8小節、4分音符ごとに1回押す。", "instruction");
            _taps = Text(_calibration, "", "score");

            _paused = Section(shell);
            Text(_paused, "一時停止", "title");
            Button(_paused, "再開  Esc", pause);
            Button(_paused, "曲選択へ  T", select);

            _result = Section(shell);
            Text(_result, "結果", "title");
            _summary = Text(_result, "", "summary");
            Button(_result, "もう一度  Enter", submit);
            Button(_result, "曲選択へ  T", select);
        }

        public int VisibleBursts => _bursts.Count;

        public void Render(RhythmSession session, int track, float leadSeconds)
        {
            var playing = session.State == Screen.Play;
            Show(_selection, session.State == Screen.Select);
            Show(_play, playing && !session.Calibrating);
            Show(_calibration, playing && session.Calibrating);
            Show(_paused, session.State == Screen.Paused);
            Show(_result, session.State == Screen.Result);
            for (var i = 0; i < _songs.Length; i++)
            {
                _songs[i].EnableInClassList("selected", i == track);
            }

            _offset.text = $"音ずれ補正   {session.Offset:+0;-0;0} ms";
            var score = session.Score;
            if (score == null) { return; }
            if (session.State == Screen.Result)
            {
                _summary.text = session.Calibrating ? CalibrationSummary(session) : ScoreSummary(score);
                return;
            }

            if (!playing) { return; }
            if (session.Calibrating)
            {
                var total = score.Song.Notes.Count;
                _taps.text = session.Position < 0 ? "まもなく開始" : $"打鍵 {session.TapCount} 回    /    目安 {total} 回";
                return;
            }

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
            _timing.text = score.LastTiming switch
            {
                Timing.Fast => "FAST",
                Timing.Slow => "SLOW",
                _ => "",
            };
            _timing.style.color = score.LastTiming == Timing.Fast ? FastColor : SlowColor;
            var lead = leadSeconds * Song.SampleRate;
            for (var i = 0; i < _notes.Count; i++)
            {
                var note = score.Song.Notes[i];
                var grade = score.GradeAt(i);
                if (_seen[i] == Grade.None && grade is Grade.Good or Grade.Fair)
                {
                    _bursts.Add(new NoteBurst(_laneBursts[note.Strike == Strike.Pulse ? 0 : 1], grade == Grade.Good,
                        note.Strike == Strike.Pulse ? Pulse : Spark, session.Position));
                }

                _seen[i] = grade;
                Place(_notes[i], grade == Grade.None, note.Sample - session.Position, lead);
            }

            for (var i = 0; i < _bars.Count; i++)
            {
                Place(_bars[i], true, score.Song.Bars[i] - session.Position, lead);
            }

            for (var i = _bursts.Count - 1; i >= 0; i--)
            {
                if (!_bursts[i].Render(session.Position))
                {
                    _bursts[i].Remove();
                    _bursts.RemoveAt(i);
                }
            }

            if (score.FullCombo && _fullComboBorn == null) { _fullComboBorn = session.Position; }
            RenderFullCombo(session.Position);
        }

        // 最後のノーツでフルコンボが決まった瞬間から、文字が縮みながら現れ、背後の帯が光って消える。
        // 経過は曲内サンプルで数える（一時停止で止まり、撮影でも同じ絵になる）。
        private void RenderFullCombo(long position)
        {
            Show(_fullComboLayer, _fullComboBorn != null);
            if (_fullComboBorn is not { } born) { return; }

            var age = Math.Max(0, position - born);
            var pop = Mathf.Clamp01((float)age / FullComboPopSamples);
            var ease = 1 - (1 - pop) * (1 - pop) * (1 - pop);
            var beat = pop < 1 ? 0 : .04f * Mathf.Sin((age - FullComboPopSamples) * 2 * Mathf.PI / (Song.SampleRate / 2f));
            var scale = 2.2f - 1.2f * ease + beat;
            _fullCombo.style.scale = new Scale(new Vector2(scale, scale));
            _fullCombo.style.opacity = ease;
            var flash = Mathf.Clamp01((float)age / FullComboFlashSamples);
            _fullComboFlash.style.opacity = .9f * (1 - flash);
            _fullComboFlash.style.scale = new Scale(new Vector2(1, .2f + .8f * Mathf.Sqrt(flash)));
        }

        private static void Place(VisualElement element, bool alive, float distance, float lead)
        {
            var visible = alive && distance >= -lead / 8 && distance <= lead;
            Show(element, visible);
            if (visible) { element.style.left = Length.Percent(HitLinePercent + TravelPercent * distance / lead); }
        }

        private static string ScoreSummary(Scorecard score) =>
            $"{score.Song.Name}{(score.FullCombo ? "    FULL COMBO" : "")}\n{score.Points:N0} 点\n" +
            $"良 {score.Good}    可 {score.Fair}（FAST {score.Fast} / SLOW {score.Slow}）    ミス {score.Miss}\n最大コンボ {score.BestCombo}";

        private static string CalibrationSummary(RhythmSession session) => session.Measured is { } measured
            ? $"補正テスト\n打鍵 {session.TapCount} 回から測ったずれ {measured:+0;-0;0} ms\n音ずれ補正を {session.Offset:+0;-0;0} ms に設定した"
            : $"補正テスト\n打鍵 {session.TapCount} 回では拍に合う打鍵が足りず、測れなかった\n音ずれ補正は {session.Offset:+0;-0;0} ms のまま";

        private void BuildNotes(Scorecard score)
        {
            foreach (var existing in _notes) { existing.RemoveFromHierarchy(); }
            foreach (var existing in _bars) { existing.RemoveFromHierarchy(); }
            foreach (var burst in _bursts) { burst.Remove(); }
            _notes.Clear();
            _bars.Clear();
            _bursts.Clear();
            _shownScore = score;
            _fullComboBorn = null;
            _seen = new Grade[score.Song.Notes.Count];
            foreach (var note in score.Song.Notes)
            {
                var pulse = note.Strike == Strike.Pulse;
                var marker = new VisualElement { pickingMode = PickingMode.Ignore };
                marker.AddToClassList(pulse ? "pulse" : "spark");
                marker.style.backgroundColor = pulse ? Pulse : Spark;
                _lanes[pulse ? 0 : 1].Add(marker);
                _notes.Add(marker);
            }

            foreach (var _ in score.Song.Bars)
            {
                var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.AddToClassList("bar-line");
                _barLayer.Add(bar);
                _bars.Add(bar);
            }
        }

        private static VisualElement Layer(VisualElement parent)
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList("layer");
            parent.Add(layer);
            return layer;
        }

        // レーンの中に、ノーツの層と破壊演出の層をこの順に重ねる。演出はレーンの縦中央に置き、レイアウトの計算を待たない。
        private static (VisualElement Notes, VisualElement Bursts) Lane(VisualElement parent, string key, Color color)
        {
            var lane = new VisualElement();
            lane.AddToClassList("lane");
            parent.Add(lane);
            var line = new VisualElement();
            line.AddToClassList("hit-line");
            line.style.backgroundColor = color;
            lane.Add(line);
            Text(lane, key, "lane-key").style.color = color;
            return (Layer(lane), Layer(lane));
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

        // ノーツを打った位置に出す破壊演出。良は閃光・輪・8つの破片、可は小さく淡い輪と4つの破片。
        // 経過は曲内サンプルで数えるので、一時停止で止まり、撮影でも同じ絵になる。
        private sealed class NoteBurst
        {
            private readonly VisualElement _root;
            private readonly VisualElement _flash;
            private readonly VisualElement _ring;
            private readonly VisualElement[] _shards;
            private readonly long _born;
            private readonly bool _good;
            private readonly int _duration;

            public NoteBurst(VisualElement layer, bool good, Color color, long born)
            {
                _good = good;
                _born = born;
                _duration = (good ? 320 : 240) * Song.SamplesPerMillisecond;
                _root = new VisualElement { pickingMode = PickingMode.Ignore };
                _root.AddToClassList("burst");
                _root.style.left = Length.Percent(HitLinePercent);
                _root.style.top = Length.Percent(50);
                layer.Add(_root);
                var bright = Color.Lerp(color, Color.white, .55f);
                if (good) { _flash = Piece(64, Color.Lerp(color, Color.white, .8f), 0); }
                _ring = Piece(good ? 68 : 54, Color.clear, good ? 5 : 3);
                var ringColor = good ? bright : new Color(color.r, color.g, color.b, .7f);
                _ring.style.borderTopColor = ringColor;
                _ring.style.borderBottomColor = ringColor;
                _ring.style.borderLeftColor = ringColor;
                _ring.style.borderRightColor = ringColor;
                _shards = new VisualElement[good ? 8 : 4];
                for (var i = 0; i < _shards.Length; i++)
                {
                    _shards[i] = Piece(good ? 11 : 8, good ? (i % 2 == 0 ? Color.white : bright) : color * .85f, -1);
                }
            }

            public bool Render(long position)
            {
                var t = (float)(position - _born) / _duration;
                if (t >= 1) { return false; }

                t = Mathf.Max(0, t);
                var ease = 1 - (1 - t) * (1 - t) * (1 - t);
                if (_flash != null)
                {
                    Resize(_flash, .6f + ease);
                    _flash.style.opacity = 1 - Mathf.Sqrt(t);
                }

                Resize(_ring, _good ? .8f + 1.6f * ease : .8f + .9f * ease);
                _ring.style.opacity = 1 - t;
                var reach = _good ? 22 + 72 * ease : 14 + 34 * ease;
                for (var i = 0; i < _shards.Length; i++)
                {
                    var angle = (i + .5f) * 360f / _shards.Length;
                    var radians = angle * Mathf.Deg2Rad;
                    var shard = _shards[i];
                    shard.style.translate = new Translate(Mathf.Cos(radians) * reach, Mathf.Sin(radians) * reach);
                    shard.style.rotate = new Rotate(new Angle(angle + 220 * t));
                    shard.style.opacity = 1 - t;
                }

                return true;
            }

            public void Remove() => _root.RemoveFromHierarchy();

            private static void Resize(VisualElement element, float scale) =>
                element.style.scale = new Scale(new Vector2(scale, scale));

            // border が負なら角の丸くない破片、0 なら塗りつぶしの円、正ならその太さの輪。
            private VisualElement Piece(float size, Color fill, float border)
            {
                var piece = new VisualElement { pickingMode = PickingMode.Ignore };
                piece.style.position = Position.Absolute;
                piece.style.left = -size / 2;
                piece.style.top = -size / 2;
                piece.style.width = size;
                piece.style.height = size;
                piece.style.backgroundColor = fill;
                var radius = border < 0 ? 2 : size / 2;
                piece.style.borderTopLeftRadius = radius;
                piece.style.borderTopRightRadius = radius;
                piece.style.borderBottomLeftRadius = radius;
                piece.style.borderBottomRightRadius = radius;
                if (border > 0)
                {
                    piece.style.borderTopWidth = border;
                    piece.style.borderBottomWidth = border;
                    piece.style.borderLeftWidth = border;
                    piece.style.borderRightWidth = border;
                }

                _root.Add(piece);
                return piece;
            }
        }
    }
}
