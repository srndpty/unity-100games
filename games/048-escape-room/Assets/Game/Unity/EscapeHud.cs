#nullable enable
using System;
using Game.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// UI Toolkit（<see cref="PanelRenderer"/>）の HUD：視点の名前・知らせ・ヒント・マウスの下の名前・持ち物の枠・操作の案内・
    /// タイトル・操作説明・一時停止・結果。要素は作るときに 1 度だけ組み、毎フレーム文字と表示の有無だけを変える。
    /// クリックは UI のイベントを使わず、押した位置をパネルの座標で各要素の矩形と照合する（046）。
    /// </summary>
    public sealed class EscapeHud
    {
        public const int Slots = 6;
        public const int BarHeight = 150;
        public const string GameTitle = "STARGAZER'S STUDY";

        private static readonly Color WindowBg = new(0.05f, 0.05f, 0.09f, 0.88f);
        private static readonly Color BarBg = new(0.06f, 0.05f, 0.05f, 0.82f);
        private static readonly Color Border = new(0.62f, 0.52f, 0.32f, 1f);
        private static readonly Color Light = new(0.96f, 0.94f, 0.88f, 1f);
        private static readonly Color Muted = new(0.66f, 0.64f, 0.6f, 1f);
        private static readonly Color Accent = new(1f, 0.82f, 0.42f, 1f);
        private static readonly Color HintColor = new(0.62f, 0.86f, 1f, 1f);
        private static readonly Color Chosen = new(0.34f, 0.26f, 0.1f, 1f);
        private static readonly Color SlotBg = new(0.13f, 0.11f, 0.1f, 1f);

        private readonly Func<int, Sprite?> _icon;
        private readonly VisualElement _title;
        private readonly Row[] _titleRows = new Row[3];
        private readonly VisualElement _howTo;
        private readonly Label _viewName;
        private readonly Label _clock;
        private readonly Label _message;
        private readonly VisualElement _hint;
        private readonly Label _hintLevel;
        private readonly Label _hintText;
        private readonly Label _hover;
        private readonly Label _guide;
        private readonly VisualElement _bar;
        private readonly VisualElement[] _slots = new VisualElement[Slots];
        private readonly VisualElement[] _slotIcons = new VisualElement[Slots];
        private readonly Label _itemName;
        private readonly VisualElement _pause;
        private readonly Row[] _pauseRows = new Row[3];
        private readonly VisualElement _result;
        private readonly Label _resultStats;

        public EscapeHud(VisualElement root, Font? font, Func<int, Sprite?> icon)
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

            // 視点の名前と経過時間
            _viewName = Badge("view-name", 30, Light);
            _viewName.style.left = 28;
            _viewName.style.top = 22;
            root.Add(_viewName);
            _clock = Badge("clock", 26, Muted);
            _clock.style.right = 28;
            _clock.style.top = 22;
            root.Add(_clock);

            // 知らせ
            _message = Badge("message", 34, Accent);
            _message.style.position = Position.Relative;
            Centered(root, "message-row", 90, -1).Add(_message);

            // ヒント
            _hint = Window("hint");
            _hint.style.position = Position.Absolute;
            _hint.style.right = 28;
            _hint.style.top = 80;
            _hint.style.width = 560;
            root.Add(_hint);
            _hintLevel = Text("hint-level", 24, HintColor);
            _hint.Add(_hintLevel);
            _hintText = Text("hint-text", 28, Light);
            _hintText.style.whiteSpace = WhiteSpace.Normal;
            _hint.Add(_hintText);

            // マウスの下の名前
            _hover = Badge("hover", 28, Light);
            _hover.style.position = Position.Relative;
            Centered(root, "hover-row", -1, BarHeight + 16).Add(_hover);

            // 下の帯：持ち物の枠と操作の案内
            _bar = new VisualElement { name = "bar", pickingMode = PickingMode.Ignore };
            _bar.style.position = Position.Absolute;
            _bar.style.left = 0;
            _bar.style.right = 0;
            _bar.style.bottom = 0;
            _bar.style.height = BarHeight;
            _bar.style.backgroundColor = BarBg;
            _bar.style.borderTopWidth = 2;
            _bar.style.borderTopColor = Border;
            root.Add(_bar);
            for (int i = 0; i < Slots; i++)
            {
                var s = new VisualElement { name = "slot-" + i, pickingMode = PickingMode.Ignore };
                s.style.position = Position.Absolute;
                s.style.left = 40 + (i * 124);
                s.style.top = 20;
                s.style.width = 110;
                s.style.height = 110;
                s.style.backgroundColor = SlotBg;
                Outline(s, 2, Border, 8);
                var slotIcon = new VisualElement { name = "slot-icon-" + i, pickingMode = PickingMode.Ignore };
                slotIcon.style.position = Position.Absolute;
                slotIcon.style.left = 11;
                slotIcon.style.top = 11;
                slotIcon.style.width = 88;
                slotIcon.style.height = 88;
                slotIcon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                s.Add(slotIcon);
                _slotIcons[i] = slotIcon;
                _slots[i] = s;
                _bar.Add(s);
            }

            _itemName = Text("item-name", 26, Accent);
            _itemName.style.position = Position.Absolute;
            _itemName.style.left = 40 + (Slots * 124) + 12;
            _itemName.style.top = 26;
            _itemName.style.width = 420;
            _itemName.style.whiteSpace = WhiteSpace.Normal;
            _bar.Add(_itemName);
            _guide = Text("guide", 22, Muted);
            _guide.style.position = Position.Absolute;
            _guide.style.right = 36;
            _guide.style.top = 22;
            _guide.style.unityTextAlign = TextAnchor.UpperRight;
            _bar.Add(_guide);

            // タイトル
            _title = new VisualElement { name = "title" };
            Fill(_title);
            _title.style.alignItems = Align.Center;
            _title.style.paddingTop = 150;
            _title.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            root.Add(_title);
            var logo = Text("title-main", 92, Accent);
            logo.text = GameTitle;
            logo.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.Add(logo);
            var sub = Text("title-sub", 38, Light);
            sub.text = "天文学者の書斎からの脱出";
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
            _howTo.style.left = 220;
            _howTo.style.right = 220;
            _howTo.style.top = 90;
            root.Add(_howTo);
            var howTitle = Text("howto-title", 42, Accent);
            howTitle.text = "操作説明";
            howTitle.style.marginBottom = 16;
            _howTo.Add(howTitle);
            var how = Text("howto-body", 27, Light);
            how.style.whiteSpace = WhiteSpace.Normal;
            how.text =
                "星を愛した老天文学者の書斎に閉じ込められた。5 つの仕掛けを解き、出口の鍵を見つけて扉を開けよう。\n\n" +
                "← → か A D で、4 面の壁へ向き直る。家具をクリックすると近くへ寄る。\n" +
                "近くでは、ダイヤル・リング・針のつまみ・宝石をクリックして動かす。落ちている物はクリックで拾う。\n" +
                "筒や本はクリックで手に取り、ドラッグで回して裏や底を見る。\n" +
                "持ち物の枠をクリックして選び、鍵穴やくぼみをクリックすると使う。選んだ枠をもう一度クリックすると手に取って見られる。\n" +
                "右クリックか Esc で一段戻る（壁の前の Esc は一時停止。一時停止の中の R でリセット）。\n" +
                "困ったら H でヒント（同じ一手には、方向 → 手がかり → 答え の順）。時間切れはない。\n\n" +
                "クリックか Enter でタイトルへ";
            _howTo.Add(how);

            // 一時停止
            _pause = new VisualElement { name = "pause" };
            Fill(_pause);
            _pause.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _pause.style.alignItems = Align.Center;
            _pause.style.justifyContent = Justify.Center;
            root.Add(_pause);
            var pauseBox = Window("pause-menu");
            pauseBox.style.width = 520;
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

            var pauseHint = Text("pause-hint", 22, Muted);
            pauseHint.text = "Esc つづける　R リセット（同じ部屋で最初から）";
            pauseHint.style.marginTop = 12;
            pauseBox.Add(pauseHint);

            // 結果
            _result = new VisualElement { name = "result" };
            Fill(_result);
            _result.style.alignItems = Align.Center;
            _result.style.justifyContent = Justify.Center;
            _result.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            root.Add(_result);
            var resultBox = Window("result-box");
            resultBox.style.width = 900;
            resultBox.style.alignItems = Align.Center;
            _result.Add(resultBox);
            var resultTitle = Text("result-title", 60, Accent);
            resultTitle.text = "脱出成功";
            resultTitle.style.marginBottom = 16;
            resultBox.Add(resultTitle);
            var story = Text("result-story", 30, Light);
            story.style.whiteSpace = WhiteSpace.Normal;
            story.style.unityTextAlign = TextAnchor.MiddleCenter;
            story.text = "扉の向こうには、夜明け前の星空が広がっていた。";
            story.style.marginBottom = 20;
            resultBox.Add(story);
            _resultStats = Text("result-stats", 32, Light);
            _resultStats.style.unityTextAlign = TextAnchor.MiddleCenter;
            resultBox.Add(_resultStats);
            var back = Text("result-back", 26, Muted);
            back.text = "クリックか Enter でタイトルへ";
            back.style.marginTop = 24;
            resultBox.Add(back);
        }

        public VisualElement Root { get; }

        /// <summary>マウスの下の部品の名前（<see cref="Render"/> の前に Unity 層が渡す）。</summary>
        public string HoverName { get; set; } = "";

        public void Render(EscapeFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var screen = flow.Screen;
            var def = flow.Def;
            Show(_title, screen == FlowScreen.Title);
            Show(_howTo, screen == FlowScreen.HowTo);
            Show(_pause, screen == FlowScreen.Paused);
            Show(_result, screen == FlowScreen.Result);
            var playing = screen is FlowScreen.Playing or FlowScreen.Paused;
            Show(_bar, playing);
            Show(_viewName, playing);
            Show(_clock, playing);

            if (screen == FlowScreen.Title)
            {
                for (int i = 0; i < _titleRows.Length; i++) { _titleRows[i].Set(flow.MenuItems[i], flow.MenuCursor == i); }
            }

            if (screen == FlowScreen.Paused)
            {
                for (int i = 0; i < _pauseRows.Length; i++) { _pauseRows[i].Set(flow.MenuItems[i], flow.MenuCursor == i); }
            }

            if (screen == FlowScreen.Result) { _resultStats.text = Stats(flow); }
            Show(_message, playing && flow.Message.Length > 0);
            Show(_hint, playing && flow.HintText.Length > 0);
            var hover = playing && flow.Held < 0 ? HoverName : "";
            Show(_hover, hover.Length > 0);
            if (!playing) { return; }

            _viewName.text = flow.Held >= 0 ? def.Examinables[flow.Held].Name + "（ドラッグで回す）" : def.Views[flow.View].Name;
            _clock.text = TimeText(flow.PlaySeconds);
            _message.text = flow.Message;
            _hintLevel.text = $"ヒント {flow.HintLevel + 1} / 3" + (flow.HintLevel == 0 ? "（方向）" : flow.HintLevel == 1 ? "（手がかり）" : "（答え）");
            _hintText.text = flow.HintText;
            _hover.text = hover;
            _guide.text = flow.Held >= 0
                ? "ドラッグ 回す\n右クリック・Esc 置く\nH ヒント"
                : flow.View == def.WallViews[def.Views[flow.View].Wall]
                    ? "← → 向き直る　クリック 寄る\nH ヒント\nEsc 一時停止"
                    : "クリック 操作・拾う・手に取る\n右クリック・Esc 戻る\nH ヒント";

            var inventory = flow.Inventory;
            for (int i = 0; i < Slots; i++)
            {
                var item = i < inventory.Count ? inventory[i] : -1;
                var sprite = item >= 0 ? _icon(item) : null;
                _slotIcons[i].style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground(StyleKeyword.None);
                var selected = item >= 0 && item == flow.SelectedItem;
                _slots[i].style.backgroundColor = selected ? Chosen : SlotBg;
                var border = selected ? Accent : Border;
                _slots[i].style.borderTopColor = border;
                _slots[i].style.borderBottomColor = border;
                _slots[i].style.borderLeftColor = border;
                _slots[i].style.borderRightColor = border;
            }

            _itemName.text = flow.SelectedItem >= 0
                ? def.Items[flow.SelectedItem].Name + " を使う →" + (def.Items[flow.SelectedItem].Examinable >= 0 ? "\n（もう一度クリックで手に取る）" : "")
                : "";
        }

        /// <summary>結果画面の数字（経過時間・操作の回数・ヒントの数）。</summary>
        public static string Stats(EscapeFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            return $"経過時間　{TimeText(flow.PlaySeconds)}\n操作　{flow.Actions} 回　　ヒント　{flow.HintsUsed} 回";
        }

        public static string TimeText(double seconds)
        {
            var s = (int)Math.Floor(seconds);
            return $"{s / 60} 分 {s % 60:00} 秒";
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

        public int SlotIndexAt(Vector2 panelPosition)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (Visible(_slots[i]) && _slots[i].worldBound.Contains(panelPosition)) { return i; }
            }

            return -1;
        }

        /// <summary>下の帯の上か（帯の上のクリックは部屋に通さない）。</summary>
        public bool IsOverBar(Vector2 panelPosition) => Visible(_bar) && _bar.worldBound.Contains(panelPosition);

        public VisualElement Slot(int i) => _slots[i];

        public VisualElement MenuRow(FlowScreen screen, int i) => (screen == FlowScreen.Paused ? _pauseRows : _titleRows)[i].Root;

        public Label ViewName => _viewName;

        public Label Message => _message;

        public Label HintTextLabel => _hintText;

        public Label ResultStats => _resultStats;

        public bool IsSlotIconShown(int i) => _slotIcons[i].resolvedStyle.backgroundImage.sprite != null;

        public static bool Visible(VisualElement e)
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

        /// <summary>横いっぱいの行（中身を中央に置く）。top か bottom の負の値は使わない。</summary>
        private static VisualElement Centered(VisualElement root, string name, float top, float bottom)
        {
            var row = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            row.style.position = Position.Absolute;
            row.style.left = 0;
            row.style.right = 0;
            if (top >= 0) { row.style.top = top; }
            if (bottom >= 0) { row.style.bottom = bottom; }
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;
            root.Add(row);
            return row;
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

        private static Label Badge(string name, int size, Color color)
        {
            var l = Text(name, size, color);
            l.style.position = Position.Absolute;
            l.style.backgroundColor = new Color(0.04f, 0.04f, 0.07f, 0.7f);
            l.style.paddingLeft = 16;
            l.style.paddingRight = 16;
            l.style.paddingTop = 4;
            l.style.paddingBottom = 4;
            l.style.borderTopLeftRadius = 6;
            l.style.borderTopRightRadius = 6;
            l.style.borderBottomLeftRadius = 6;
            l.style.borderBottomRightRadius = 6;
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
