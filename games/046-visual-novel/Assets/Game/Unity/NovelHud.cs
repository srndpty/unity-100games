#nullable enable
using System;
using Game.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// UI Toolkit（<see cref="PanelRenderer"/>）の HUD：タイトル・操作説明・テキスト枠・選択肢・一時停止・結果。
    /// 要素は作るときに 1 度だけ組み、毎フレーム文字と表示の有無だけを変える。
    /// 選択の印（▶）は固定幅の列に分け、選び直しで項目名が動かないようにする（032）。
    /// クリックは UI のイベントを使わず、押した位置をパネルの座標で各項目の矩形と照合する（<see cref="MenuIndexAt"/>・<see cref="OptionIndexAt"/>）。
    /// </summary>
    public sealed class NovelHud
    {
        public const int MaxOptions = 3;

        private static readonly Color WindowBg = new(0.05f, 0.07f, 0.12f, 0.86f);
        private static readonly Color Border = new(0.62f, 0.70f, 0.82f, 1f);
        private static readonly Color Light = new(0.96f, 0.95f, 0.92f, 1f);
        private static readonly Color ReadText = new(0.68f, 0.80f, 0.94f, 1f);
        private static readonly Color Muted = new(0.62f, 0.64f, 0.68f, 1f);
        private static readonly Color Accent = new(1f, 0.84f, 0.46f, 1f);
        private static readonly Color Danger = new(1f, 0.52f, 0.46f, 1f);
        private static readonly Color Chosen = new(0.24f, 0.30f, 0.44f, 0.94f);

        private readonly VisualElement _title;
        private readonly Row[] _titleRows = new Row[4];
        private readonly Label _titleDetail;
        private readonly Label _titleEndings;
        private readonly VisualElement _howTo;
        private readonly VisualElement _window;
        private readonly VisualElement _namePlate;
        private readonly Label _name;
        private readonly Label _text;
        private readonly Label _next;
        private readonly VisualElement _choices;
        private readonly VisualElement[] _optionBoxes = new VisualElement[MaxOptions];
        private readonly Row[] _optionRows = new Row[MaxOptions];
        private readonly Label _skip;
        private readonly Label _hint;
        private readonly Label _notice;
        private readonly VisualElement _pause;
        private readonly Row[] _pauseRows = new Row[2];
        private readonly VisualElement _result;
        private readonly Label _resultTitle;
        private readonly Label[] _resultRows = new Label[3];

        public NovelHud(VisualElement root, Font? font)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            root.Clear();
            Fill(root);
            root.pickingMode = PickingMode.Ignore;
            if (font != null)
            {
                root.style.unityFont = font;
                root.style.unityFontDefinition = FontDefinition.FromFont(font);
            }

            // タイトル
            _title = new VisualElement { name = "title" };
            Fill(_title);
            _title.style.alignItems = Align.Center;
            _title.style.paddingTop = 170;
            root.Add(_title);
            var logo = Text("title-main", 92, Accent);
            logo.text = "LANTERN WAKE";
            logo.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.Add(logo);
            var sub = Text("title-sub", 40, Light);
            sub.text = "灯守りの夜";
            sub.style.marginBottom = 48;
            _title.Add(sub);
            var menu = Window("title-menu");
            menu.style.width = 480;
            _title.Add(menu);
            for (int i = 0; i < _titleRows.Length; i++)
            {
                _titleRows[i] = new Row("title-row-" + i, 34);
                menu.Add(_titleRows[i].Root);
            }

            _titleDetail = Text("title-detail", 24, Danger);
            _titleDetail.style.marginTop = 16;
            _titleDetail.style.maxWidth = 1200;
            _titleDetail.style.whiteSpace = WhiteSpace.Normal;
            _title.Add(_titleDetail);
            _titleEndings = Text("title-endings", 26, Muted);
            _titleEndings.style.marginTop = 20;
            _title.Add(_titleEndings);

            // 操作説明
            _howTo = Window("howto");
            _howTo.style.position = Position.Absolute;
            _howTo.style.left = 300;
            _howTo.style.right = 300;
            _howTo.style.top = 140;
            root.Add(_howTo);
            var howTitle = Text("howto-title", 42, Accent);
            howTitle.text = "操作説明";
            howTitle.style.marginBottom = 16;
            _howTo.Add(howTitle);
            var how = Text("howto-body", 28, Light);
            how.style.whiteSpace = WhiteSpace.Normal;
            how.text =
                "クリック / Enter / Space / ↓：文を送る（表示の途中なら全文を出す）\n" +
                "↑↓ と Enter、またはクリック：選択肢を選ぶ\n" +
                "Ctrl（押している間）/ S（切り替え）：既読の文をスキップ。未読の文と選択肢で止まる\n" +
                "F5：クイックセーブ　F9：クイックロード（タイトルの「つづきから」も同じ）\n" +
                "H：テキスト枠を隠す　Esc：一時停止（タイトルへ戻る）\n\n" +
                "嵐の夜、灯台の灯が消えた。あなたの選んだ 3 つの行動で、夜の終わり方が変わる。\n" +
                "結末は 3 つ。読んだ文は青く表示され、次からスキップできる。\n\n" +
                "クリックか Enter でタイトルへ";
            _howTo.Add(how);

            // テキスト枠
            _window = Window("window");
            _window.style.position = Position.Absolute;
            _window.style.left = 150;
            _window.style.right = 150;
            _window.style.bottom = 36;
            _window.style.height = 250;
            _window.style.paddingLeft = 40;
            _window.style.paddingRight = 40;
            _window.style.paddingTop = 34;
            root.Add(_window);
            _namePlate = Window("name-plate");
            _namePlate.style.position = Position.Absolute;
            _namePlate.style.left = 24;
            _namePlate.style.top = -38;
            _namePlate.style.paddingTop = 4;
            _namePlate.style.paddingBottom = 4;
            _namePlate.style.paddingLeft = 22;
            _namePlate.style.paddingRight = 22;
            _window.Add(_namePlate);
            _name = Text("name", 30, Accent);
            _namePlate.Add(_name);
            _text = Text("text", 36, Light);
            _text.style.whiteSpace = WhiteSpace.Normal;
            _window.Add(_text);
            _next = Text("next", 28, Accent);
            _next.text = "▼";
            _next.style.position = Position.Absolute;
            _next.style.right = 28;
            _next.style.bottom = 14;
            _window.Add(_next);

            // 選択肢
            _choices = new VisualElement { name = "choices" };
            _choices.style.position = Position.Absolute;
            _choices.style.left = 560;
            _choices.style.right = 560;
            _choices.style.top = 250;
            root.Add(_choices);
            for (int i = 0; i < MaxOptions; i++)
            {
                _optionBoxes[i] = Window("option-" + i);
                _optionBoxes[i].style.marginBottom = 22;
                _optionRows[i] = new Row("option-row-" + i, 36);
                _optionBoxes[i].Add(_optionRows[i].Root);
                _choices.Add(_optionBoxes[i]);
            }

            // 上の帯：スキップ中・操作の案内・案内
            _skip = Text("skip", 30, Accent);
            _skip.text = "SKIP ▶▶";
            _skip.style.position = Position.Absolute;
            _skip.style.right = 40;
            _skip.style.top = 24;
            root.Add(_skip);
            _hint = Text("hint", 22, Muted);
            _hint.text = "Enter / クリック 送る　Ctrl スキップ　F5 保存　F9 読込　H 枠を隠す　Esc メニュー";
            _hint.style.position = Position.Absolute;
            _hint.style.left = 40;
            _hint.style.top = 22;
            _hint.style.backgroundColor = new Color(0.05f, 0.07f, 0.12f, 0.6f);
            _hint.style.paddingLeft = 12;
            _hint.style.paddingRight = 12;
            _hint.style.paddingTop = 4;
            _hint.style.paddingBottom = 4;
            root.Add(_hint);
            _notice = Text("notice", 28, Light);
            _notice.style.position = Position.Absolute;
            _notice.style.top = 80;
            _notice.style.alignSelf = Align.Center;
            _notice.style.backgroundColor = WindowBg;
            _notice.style.paddingLeft = 24;
            _notice.style.paddingRight = 24;
            _notice.style.paddingTop = 8;
            _notice.style.paddingBottom = 8;
            root.Add(_notice);

            // 一時停止
            _pause = new VisualElement { name = "pause" };
            Fill(_pause);
            _pause.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _pause.style.alignItems = Align.Center;
            _pause.style.justifyContent = Justify.Center;
            root.Add(_pause);
            var pauseBox = Window("pause-menu");
            pauseBox.style.width = 480;
            _pause.Add(pauseBox);
            var pauseTitle = Text("pause-title", 40, Accent);
            pauseTitle.text = "一時停止";
            pauseTitle.style.marginBottom = 12;
            pauseBox.Add(pauseTitle);
            for (int i = 0; i < _pauseRows.Length; i++)
            {
                _pauseRows[i] = new Row("pause-row-" + i, 34);
                pauseBox.Add(_pauseRows[i].Root);
            }

            // 結果
            _result = new VisualElement { name = "result" };
            Fill(_result);
            _result.style.alignItems = Align.Center;
            _result.style.justifyContent = Justify.Center;
            root.Add(_result);
            var resultBox = Window("result-box");
            resultBox.style.width = 760;
            resultBox.style.alignItems = Align.Center;
            _result.Add(resultBox);
            _resultTitle = Text("result-title", 60, Accent);
            _resultTitle.style.marginBottom = 24;
            resultBox.Add(_resultTitle);
            var heading = Text("result-heading", 28, Muted);
            heading.text = "たどり着いた結末";
            heading.style.marginBottom = 8;
            resultBox.Add(heading);
            for (int i = 0; i < _resultRows.Length; i++)
            {
                _resultRows[i] = Text("result-row-" + i, 34, Light);
                resultBox.Add(_resultRows[i]);
            }

            var back = Text("result-back", 26, Muted);
            back.text = "クリックか Enter でタイトルへ";
            back.style.marginTop = 24;
            resultBox.Add(back);
        }

        public VisualElement Root { get; }

        public void Render(NovelFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var screen = flow.Screen;
            Show(_title, screen == FlowScreen.Title);
            Show(_howTo, screen == FlowScreen.HowTo);
            Show(_pause, screen == FlowScreen.Paused);
            Show(_result, screen == FlowScreen.Result);
            var reading = screen is FlowScreen.Reading or FlowScreen.Paused;
            var hidden = flow.WindowHidden;
            Show(_window, reading && !hidden && !flow.IsChoice);
            Show(_choices, reading && !hidden && flow.IsChoice);
            Show(_skip, flow.SkipActive);
            Show(_hint, reading && !hidden);
            _notice.text = flow.Notice;
            Show(_notice, flow.Notice.Length > 0);

            if (screen == FlowScreen.Title)
            {
                var items = flow.TitleItems;
                for (int i = 0; i < _titleRows.Length; i++)
                {
                    _titleRows[i].Set(items[i].Label, flow.MenuCursor == i, items[i].Enabled);
                }

                // 「つづきから」を選べない理由のうち、壊れた・版が違うなどの案内だけを出す（ないだけなら出さない）。
                var cont = items[NovelFlow.TitleContinue];
                _titleDetail.text = !cont.Enabled && cont.Detail != "クイックセーブがありません" ? cont.Detail : "";
                Show(_titleDetail, _titleDetail.text.Length > 0);
                _titleEndings.text = $"たどり着いた結末　{flow.EndingsReached} / {flow.Endings.Count}";
            }

            if (screen == FlowScreen.Paused)
            {
                for (int i = 0; i < _pauseRows.Length; i++) { _pauseRows[i].Set(flow.PauseItems[i].Label, flow.MenuCursor == i, true); }
            }

            if (reading)
            {
                var speaker = flow.SpeakerName;
                Show(_namePlate, speaker.Length > 0);
                _name.text = speaker;
                var c = flow.SpeakerColor;
                _name.style.color = speaker.Length > 0 ? new Color(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f, 1f) : Accent;
                _text.text = flow.VisibleText;
                _text.style.color = flow.IsCurrentRead ? ReadText : Light;
                Show(_next, flow.IsRevealed && (flow.Ticks / 30) % 2 == 0);
                var options = flow.Options;
                for (int i = 0; i < MaxOptions; i++)
                {
                    var has = i < options.Count;
                    Show(_optionBoxes[i], has);
                    if (!has) { continue; }
                    _optionRows[i].Set(options[i], flow.ChoiceCursor == i, true);
                    _optionBoxes[i].style.backgroundColor = flow.ChoiceCursor == i ? Chosen : WindowBg;
                }
            }

            if (screen == FlowScreen.Result)
            {
                _resultTitle.text = $"― {flow.EndingName} ―";
                var endings = flow.Endings;
                for (int i = 0; i < _resultRows.Length; i++)
                {
                    var has = i < endings.Count;
                    Show(_resultRows[i], has);
                    if (!has) { continue; }
                    var e = endings[i];
                    _resultRows[i].text = (e.Current ? "▶ " : "　 ") + (e.Reached ? e.Name : "？？？");
                    _resultRows[i].style.color = e.Current ? Accent : e.Reached ? Light : Muted;
                }
            }
        }

        /// <summary>パネルの座標にあるメニュー（タイトル・一時停止）の項目の番号。なければ -1。</summary>
        public int MenuIndexAt(FlowScreen screen, Vector2 panelPosition)
        {
            var rows = screen == FlowScreen.Title ? _titleRows : screen == FlowScreen.Paused ? _pauseRows : Array.Empty<Row>();
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Root.worldBound.Contains(panelPosition)) { return i; }
            }

            return -1;
        }

        /// <summary>パネルの座標にある選択肢の番号。なければ -1。</summary>
        public int OptionIndexAt(Vector2 panelPosition)
        {
            for (int i = 0; i < MaxOptions; i++)
            {
                if (_optionBoxes[i].resolvedStyle.display != DisplayStyle.None && _optionBoxes[i].worldBound.Contains(panelPosition)) { return i; }
            }

            return -1;
        }

        private static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0;
            e.style.right = 0;
            e.style.top = 0;
            e.style.bottom = 0;
        }

        private static void Show(VisualElement e, bool visible) => e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        private static Label Text(string name, int size, Color color)
        {
            var l = new Label { name = name, enableRichText = false };
            l.style.fontSize = size;
            l.style.color = color;
            l.style.marginBottom = 0;
            l.style.marginTop = 0;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private static VisualElement Window(string name)
        {
            var w = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            w.style.backgroundColor = WindowBg;
            w.style.borderTopWidth = 2;
            w.style.borderBottomWidth = 2;
            w.style.borderLeftWidth = 2;
            w.style.borderRightWidth = 2;
            w.style.borderTopColor = Border;
            w.style.borderBottomColor = Border;
            w.style.borderLeftColor = Border;
            w.style.borderRightColor = Border;
            w.style.borderTopLeftRadius = 10;
            w.style.borderTopRightRadius = 10;
            w.style.borderBottomLeftRadius = 10;
            w.style.borderBottomRightRadius = 10;
            w.style.paddingLeft = 24;
            w.style.paddingRight = 24;
            w.style.paddingTop = 14;
            w.style.paddingBottom = 14;
            return w;
        }

        /// <summary>メニューの 1 行：選択の印・項目名・右の印の列を固定幅に分ける（032）。</summary>
        private sealed class Row
        {
            public Row(string name, int size)
            {
                Root = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
                Root.style.flexDirection = FlexDirection.Row;
                Root.style.alignItems = Align.Center;
                Root.style.minHeight = size + 14;
                Cursor = Text(name + "-cursor", size, Accent);
                Cursor.style.width = 48;
                Root.Add(Cursor);
                Label = Text(name + "-label", size, Light);
                Label.style.flexGrow = 1;
                Label.style.whiteSpace = WhiteSpace.Normal;
                Root.Add(Label);
                var right = Text(name + "-right", size, Accent);
                right.style.width = 48;
                Root.Add(right);
            }

            public VisualElement Root { get; }
            public Label Cursor { get; }
            public Label Label { get; }

            public void Set(string label, bool selected, bool enabled)
            {
                Label.text = label;
                Cursor.text = selected ? "▶" : "";
                Label.style.color = !enabled ? Muted : selected ? Accent : Light;
            }
        }
    }
}
