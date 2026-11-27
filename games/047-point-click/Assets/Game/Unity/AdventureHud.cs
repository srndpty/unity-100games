#nullable enable
using System;
using Game.App;
using Game.Core.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// UI Toolkit（<see cref="PanelRenderer"/>）の HUD：下の帯（命令文・動詞 4・持ち物 8 枠）・台詞・会話の選択肢・
    /// タイトル・操作説明・一時停止・結果。要素は作るときに 1 度だけ組み、毎フレーム文字と表示の有無だけを変える。
    /// クリックは UI のイベントを使わず、押した位置をパネルの座標で各要素の矩形と照合する（046）。
    /// </summary>
    public sealed class AdventureHud
    {
        public const int Slots = 8;
        /// <summary>話題の枠の数：同時に並ぶ話題の上限（Core の検証と同じ定数）と「さようなら」。</summary>
        public const int MaxTopics = BookLimits.MaxTopicChoices + 1;

        /// <summary>下の帯の高さ（パネルの座標、部屋の座標でも同じ 200）。</summary>
        public const int BarHeight = 200;

        private static readonly Color WindowBg = new(0.05f, 0.07f, 0.12f, 0.88f);
        private static readonly Color BarBg = new(0.07f, 0.06f, 0.08f, 1f);
        private static readonly Color Border = new(0.55f, 0.62f, 0.74f, 1f);
        private static readonly Color Light = new(0.96f, 0.95f, 0.92f, 1f);
        private static readonly Color Muted = new(0.60f, 0.62f, 0.66f, 1f);
        private static readonly Color Accent = new(1f, 0.84f, 0.46f, 1f);
        private static readonly Color Npc = new(0.62f, 0.86f, 1f, 1f);
        private static readonly Color Chosen = new(0.30f, 0.26f, 0.14f, 1f);
        private static readonly Color SlotBg = new(0.13f, 0.12f, 0.15f, 1f);

        private readonly Func<string, Sprite?> _icon;
        private readonly VisualElement _title;
        private readonly Row[] _titleRows = new Row[3];
        private readonly VisualElement _howTo;
        private readonly VisualElement _bar;
        private readonly Label _sentence;
        private readonly Label _roomName;
        private readonly VisualElement[] _verbs = new VisualElement[4];
        private readonly Label[] _verbLabels = new Label[4];
        private readonly VisualElement[] _slots = new VisualElement[Slots];
        private readonly VisualElement[] _slotIcons = new VisualElement[Slots];
        private readonly VisualElement _line;
        private readonly Label _lineName;
        private readonly Label _lineText;
        private readonly VisualElement _topics;
        private readonly VisualElement[] _topicBoxes = new VisualElement[MaxTopics];
        private readonly Row[] _topicRows = new Row[MaxTopics];
        private readonly VisualElement _pause;
        private readonly Row[] _pauseRows = new Row[2];
        private readonly VisualElement _result;
        private readonly Label _resultStats;

        public AdventureHud(VisualElement root, Font? font, Func<string, Sprite?> icon)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            _icon = icon ?? throw new ArgumentNullException(nameof(icon));
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
            _title.style.paddingTop = 150;
            _title.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            root.Add(_title);
            var logo = Text("title-main", 96, Accent);
            logo.text = "TIDEWATCH";
            logo.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.Add(logo);
            var sub = Text("title-sub", 40, Light);
            sub.text = "嵐の灯台守小屋";
            sub.style.marginBottom = 56;
            _title.Add(sub);
            var menu = Window("title-menu");
            menu.style.width = 480;
            _title.Add(menu);
            for (int i = 0; i < _titleRows.Length; i++)
            {
                _titleRows[i] = new Row("title-row-" + i, 34);
                menu.Add(_titleRows[i].Root);
            }

            // 操作説明
            _howTo = Window("howto");
            _howTo.style.position = Position.Absolute;
            _howTo.style.left = 260;
            _howTo.style.right = 260;
            _howTo.style.top = 110;
            root.Add(_howTo);
            var howTitle = Text("howto-title", 42, Accent);
            howTitle.text = "操作説明";
            howTitle.style.marginBottom = 16;
            _howTo.Add(howTitle);
            var how = Text("howto-body", 28, Light);
            how.style.whiteSpace = WhiteSpace.Normal;
            how.text =
                "嵐の夜、灯台の灯が消えた。灯をともせば、船頭が小舟で町へ送ってくれる。\n\n" +
                "下の帯で動詞（見る・取る・使う・話す、キーの 1〜4）を選び、部屋の物をクリックする。\n" +
                "主人公はその物の前まで歩いてから行動する。動詞を選ばずにクリックすると、そこへ歩く（出口なら移動）。\n" +
                "持ち物をクリックすると「使う ＜持ち物＞ →」になる。続けて部屋の物か別の持ち物をクリックして使う。\n" +
                "右クリックで動詞と持ち物の選択を外す。台詞はクリックか Enter で送る。\n" +
                "人物と話すと話題が並ぶ。クリックか ↑↓ と Enter で選ぶ。Esc で一時停止。\n\n" +
                "謎は 5 つ。拾った物や開けた扉は、部屋を移っても元に戻らない。\n\n" +
                "クリックか Enter でタイトルへ";
            _howTo.Add(how);

            // 部屋の名前
            _roomName = Text("room-name", 26, Light);
            _roomName.style.position = Position.Absolute;
            _roomName.style.left = 28;
            _roomName.style.top = 20;
            _roomName.style.backgroundColor = new Color(0.05f, 0.07f, 0.12f, 0.6f);
            _roomName.style.paddingLeft = 14;
            _roomName.style.paddingRight = 14;
            _roomName.style.paddingTop = 4;
            _roomName.style.paddingBottom = 4;
            root.Add(_roomName);

            // 下の帯
            _bar = new VisualElement { name = "bar", pickingMode = PickingMode.Ignore };
            _bar.style.position = Position.Absolute;
            _bar.style.left = 0;
            _bar.style.right = 0;
            _bar.style.bottom = 0;
            _bar.style.height = BarHeight;
            _bar.style.backgroundColor = BarBg;
            _bar.style.borderTopWidth = 3;
            _bar.style.borderTopColor = Border;
            root.Add(_bar);
            _sentence = Text("sentence", 32, Accent);
            _sentence.style.position = Position.Absolute;
            _sentence.style.left = 40;
            _sentence.style.top = 10;
            _bar.Add(_sentence);
            for (int i = 0; i < _verbs.Length; i++)
            {
                var v = new VisualElement { name = "verb-" + i, pickingMode = PickingMode.Ignore };
                v.style.position = Position.Absolute;
                v.style.left = 40 + (i % 2) * 215;
                v.style.top = 60 + (i / 2) * 68;
                v.style.width = 200;
                v.style.height = 58;
                v.style.alignItems = Align.Center;
                v.style.justifyContent = Justify.Center;
                Outline(v, 2, Border, 8);
                _verbLabels[i] = Text("verb-label-" + i, 30, Light);
                _verbLabels[i].text = $"{i + 1} {AdventureFlow.VerbName(AdventureFlow.Verbs[i])}";
                v.Add(_verbLabels[i]);
                _verbs[i] = v;
                _bar.Add(v);
            }

            for (int i = 0; i < Slots; i++)
            {
                var s = new VisualElement { name = "slot-" + i, pickingMode = PickingMode.Ignore };
                s.style.position = Position.Absolute;
                s.style.left = 520 + i * 132;
                s.style.top = 62;
                s.style.width = 120;
                s.style.height = 120;
                s.style.backgroundColor = SlotBg;
                Outline(s, 2, Border, 8);
                var slotIcon = new VisualElement { name = "slot-icon-" + i, pickingMode = PickingMode.Ignore };
                slotIcon.style.position = Position.Absolute;
                slotIcon.style.left = 12;
                slotIcon.style.top = 12;
                slotIcon.style.width = 96;
                slotIcon.style.height = 96;
                slotIcon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                s.Add(slotIcon);
                _slotIcons[i] = slotIcon;
                _slots[i] = s;
                _bar.Add(s);
            }

            var hint = Text("hint", 22, Muted);
            hint.text = "1〜4 動詞\n右クリック 取り消し\nEsc 一時停止";
            hint.style.position = Position.Absolute;
            hint.style.right = 40;
            hint.style.top = 66;
            hint.style.unityTextAlign = TextAnchor.UpperRight;
            _bar.Add(hint);

            // 台詞
            _line = Window("line");
            _line.style.position = Position.Absolute;
            _line.style.left = 260;
            _line.style.right = 260;
            _line.style.top = 80;
            _line.style.minHeight = 120;
            _line.style.paddingTop = 30;
            root.Add(_line);
            _lineName = Text("line-name", 28, Npc);
            _lineName.style.position = Position.Absolute;
            _lineName.style.left = 24;
            _lineName.style.top = 2;
            _line.Add(_lineName);
            _lineText = Text("line-text", 38, Light);
            _lineText.style.whiteSpace = WhiteSpace.Normal;
            _line.Add(_lineText);
            var lineNext = Text("line-next", 22, Muted);
            lineNext.text = "クリックで送る";
            lineNext.style.position = Position.Absolute;
            lineNext.style.right = 20;
            lineNext.style.bottom = 8;
            _line.Add(lineNext);

            // 会話の選択肢
            _topics = Window("topics");
            _topics.style.position = Position.Absolute;
            _topics.style.left = 300;
            _topics.style.right = 300;
            _topics.style.top = 80;
            root.Add(_topics);
            for (int i = 0; i < MaxTopics; i++)
            {
                _topicBoxes[i] = new VisualElement { name = "topic-" + i, pickingMode = PickingMode.Ignore };
                _topicRows[i] = new Row("topic-row-" + i, 32);
                _topicBoxes[i].Add(_topicRows[i].Root);
                _topics.Add(_topicBoxes[i]);
            }

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
            _result.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            root.Add(_result);
            var resultBox = Window("result-box");
            resultBox.style.width = 900;
            resultBox.style.alignItems = Align.Center;
            _result.Add(resultBox);
            var resultTitle = Text("result-title", 60, Accent);
            resultTitle.text = "脱出";
            resultTitle.style.marginBottom = 16;
            resultBox.Add(resultTitle);
            var story = Text("result-story", 30, Light);
            story.style.whiteSpace = WhiteSpace.Normal;
            story.style.unityTextAlign = TextAnchor.MiddleCenter;
            story.text = "灯台の光を頼りに、船頭の小舟は岩場を抜けた。\n夜明け前、あなたは町の港に降り立った。";
            story.style.marginBottom = 20;
            resultBox.Add(story);
            _resultStats = Text("result-stats", 30, Light);
            _resultStats.style.unityTextAlign = TextAnchor.MiddleCenter;
            resultBox.Add(_resultStats);
            var back = Text("result-back", 26, Muted);
            back.text = "クリックか Enter でタイトルへ";
            back.style.marginTop = 24;
            resultBox.Add(back);
        }

        public VisualElement Root { get; }

        public void Render(AdventureFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var screen = flow.Screen;
            var book = flow.Book;
            Show(_title, screen == FlowScreen.Title);
            Show(_howTo, screen == FlowScreen.HowTo);
            Show(_pause, screen == FlowScreen.Paused);
            Show(_result, screen == FlowScreen.Result);
            var playing = screen is FlowScreen.Playing or FlowScreen.Paused;
            Show(_bar, playing);
            Show(_roomName, playing);

            if (screen == FlowScreen.Title)
            {
                for (int i = 0; i < _titleRows.Length; i++) { _titleRows[i].Set(flow.MenuItems[i], flow.MenuCursor == i); }
            }

            if (screen == FlowScreen.Paused)
            {
                for (int i = 0; i < _pauseRows.Length; i++) { _pauseRows[i].Set(flow.MenuItems[i], flow.MenuCursor == i); }
            }

            var line = playing ? flow.CurrentLine : null;
            Show(_line, line != null);
            if (line != null)
            {
                _lineName.text = line.Speaker;
                Show(_lineName, line.Speaker.Length > 0);
                _lineText.text = line.Text;
                _lineText.style.color = line.SpeakerHotspot >= 0 ? Npc : line.IsDefault ? Muted : Light;
            }

            var topics = playing && flow.ShowingTopics ? flow.TopicOptions : Array.Empty<string>();
            Show(_topics, topics.Count > 0);
            for (int i = 0; i < MaxTopics; i++)
            {
                var has = i < topics.Count;
                Show(_topicBoxes[i], has);
                if (!has) { continue; }
                _topicRows[i].Set(topics[i], flow.TopicCursor == i);
                _topicBoxes[i].style.backgroundColor = flow.TopicCursor == i ? Chosen : Color.clear;
            }

            if (screen == FlowScreen.Result) { _resultStats.text = Stats(flow); }
            if (!playing) { return; }
            _roomName.text = book.Rooms[flow.Room].Name;
            _sentence.text = flow.Sentence;
            for (int i = 0; i < _verbs.Length; i++)
            {
                var selected = AdventureFlow.Verbs[i] == flow.SelectedVerb;
                _verbs[i].style.backgroundColor = selected ? Chosen : SlotBg;
                _verbLabels[i].style.color = selected ? Accent : Light;
            }

            var inventory = flow.Inventory;
            for (int i = 0; i < Slots; i++)
            {
                var item = i < inventory.Count ? inventory[i] : -1;
                var sprite = item >= 0 ? _icon(book.Items[item].Id) : null;
                _slotIcons[i].style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground(StyleKeyword.None);
                _slots[i].style.backgroundColor = item >= 0 && item == flow.SelectedTool ? Chosen : SlotBg;
                var border = item >= 0 && item == flow.SelectedTool ? Accent : Border;
                _slots[i].style.borderTopColor = border;
                _slots[i].style.borderBottomColor = border;
                _slots[i].style.borderLeftColor = border;
                _slots[i].style.borderRightColor = border;
            }
        }

        /// <summary>結果画面の数字（解いた謎・行為の数・時間）。</summary>
        public static string Stats(AdventureFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var seconds = (int)Math.Round(flow.ElapsedSeconds);
            return $"解いた謎　{flow.SolvedCount} / {flow.Book.Puzzles.Count}\n行動　{flow.ActionCount} 回　　時間　{seconds / 60} 分 {seconds % 60:00} 秒";
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

        public int VerbIndexAt(Vector2 panelPosition) => IndexAt(_verbs, panelPosition);

        public int SlotIndexAt(Vector2 panelPosition) => IndexAt(_slots, panelPosition);

        public int TopicIndexAt(Vector2 panelPosition) => IndexAt(_topicBoxes, panelPosition);

        /// <summary>下の帯の上か（帯の上のクリックは部屋に通さない）。</summary>
        public bool IsOverBar(Vector2 panelPosition) => Visible(_bar) && _bar.worldBound.Contains(panelPosition);

        /// <summary>要素の中心（パネルの座標）。テストで要素の上をクリックするのに使う。</summary>
        public static Vector2 CenterOf(VisualElement e) => e?.worldBound.center ?? throw new ArgumentNullException(nameof(e));

        public VisualElement Verb(int i) => _verbs[i];

        public VisualElement Slot(int i) => _slots[i];

        public VisualElement Topic(int i) => _topicBoxes[i];

        public VisualElement MenuRow(FlowScreen screen, int i) => (screen == FlowScreen.Paused ? _pauseRows : _titleRows)[i].Root;

        public Label LineText => _lineText;

        public Label Sentence => _sentence;

        public bool IsSlotIconShown(int i) => _slotIcons[i].resolvedStyle.backgroundImage.sprite != null;

        private static int IndexAt(VisualElement[] elements, Vector2 p)
        {
            for (int i = 0; i < elements.Length; i++)
            {
                if (Visible(elements[i]) && elements[i].worldBound.Contains(p)) { return i; }
            }

            return -1;
        }

        private static bool Visible(VisualElement e)
        {
            for (var v = e; v != null; v = v.parent)
            {
                if (v.resolvedStyle.display == DisplayStyle.None) { return false; }
            }

            return true;
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

        private static void Outline(VisualElement w, float width, Color color, float radius)
        {
            w.style.borderTopWidth = width;
            w.style.borderBottomWidth = width;
            w.style.borderLeftWidth = width;
            w.style.borderRightWidth = width;
            w.style.borderTopColor = color;
            w.style.borderBottomColor = color;
            w.style.borderLeftColor = color;
            w.style.borderRightColor = color;
            w.style.borderTopLeftRadius = radius;
            w.style.borderTopRightRadius = radius;
            w.style.borderBottomLeftRadius = radius;
            w.style.borderBottomRightRadius = radius;
        }

        private static VisualElement Window(string name)
        {
            var w = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            w.style.backgroundColor = WindowBg;
            Outline(w, 2, Border, 10);
            w.style.paddingLeft = 24;
            w.style.paddingRight = 24;
            w.style.paddingTop = 14;
            w.style.paddingBottom = 14;
            return w;
        }

        /// <summary>メニューの 1 行：選択の印と項目名を固定幅の列に分ける（032）。</summary>
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
            }

            public VisualElement Root { get; }
            public Label Cursor { get; }
            public Label Label { get; }

            public void Set(string label, bool selected)
            {
                Label.text = label;
                Cursor.text = selected ? "▶" : "";
                Label.style.color = selected ? Accent : Light;
            }
        }
    }
}
