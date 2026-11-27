#nullable enable
using System;
using System.Globalization;
using Game.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// UI Toolkit（<see cref="PanelRenderer"/>）の HUD：タイトル・操作説明・日付・能力値・行動の選択・結果・会話の枠・選択肢・詳細・一時停止・結果画面。
    /// 要素は作るときに 1 度だけ組み、毎フレーム文字と表示の有無だけを変える。
    /// 選択の印（▶）は固定幅の列に分け、選び直しで項目名が動かないようにする（032）。
    /// クリックは UI のイベントを使わず、押した位置をパネルの座標で各項目の矩形と照合する（<see cref="MenuIndexAt"/>）。
    /// </summary>
    public sealed class SummerHud
    {
        public const int MaxActions = 12;
        public const int MaxOptions = 3;
        public const int MaxStats = 16;

        private static readonly Color WindowBg = new(0.06f, 0.08f, 0.16f, 0.86f);
        private static readonly Color Border = new(0.60f, 0.72f, 0.92f, 1f);
        private static readonly Color Light = new(0.96f, 0.96f, 0.94f, 1f);
        private static readonly Color Muted = new(0.64f, 0.67f, 0.74f, 1f);
        private static readonly Color Accent = new(1f, 0.85f, 0.45f, 1f);
        private static readonly Color Danger = new(1f, 0.55f, 0.50f, 1f);
        private static readonly Color HeartColor = new(1f, 0.52f, 0.62f, 1f);
        private static readonly Color Chosen = new(0.24f, 0.32f, 0.52f, 0.94f);
        private static readonly Color BarBack = new(0.18f, 0.20f, 0.28f, 1f);
        private static readonly Color BarFill = new(0.50f, 0.78f, 1f, 1f);
        private static readonly Color CostFill = new(1f, 0.62f, 0.40f, 1f);

        private readonly VisualElement _title;
        private readonly Row[] _titleRows = new Row[4];
        private readonly Label _titleDetail;
        private readonly VisualElement _howTo;
        private readonly VisualElement _date;
        private readonly Label _day;
        private readonly Label _weekday;
        private readonly VisualElement _stats;
        private readonly StatRow[] _statRows = new StatRow[MaxStats];
        private readonly VisualElement _actions;
        private readonly Label _actionsTitle;
        private readonly Row[] _actionRows = new Row[MaxActions];
        private readonly VisualElement _report;
        private readonly Label _reportTitle;
        private readonly Label _reportBody;
        private readonly VisualElement _window;
        private readonly VisualElement _namePlate;
        private readonly Label _name;
        private readonly Label _text;
        private readonly Label _next;
        private readonly VisualElement _choices;
        private readonly VisualElement[] _optionBoxes = new VisualElement[MaxOptions];
        private readonly Row[] _optionRows = new Row[MaxOptions];
        private readonly VisualElement _detail;
        private readonly Label _detailBody;
        private readonly Label _howToHints;
        private readonly Label _detailHints;
        private readonly Label _hint;
        private readonly Label _notice;
        private readonly VisualElement _pause;
        private readonly Row[] _pauseRows = new Row[2];
        private readonly VisualElement _result;
        private readonly Label _resultTitle;
        private readonly Label _resultBody;

        public SummerHud(VisualElement root, Font? font)
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
            _title.style.paddingTop = 150;
            root.Add(_title);
            var logo = Text("title-main", 88, Accent);
            logo.text = "夏、流星群の夜まで";
            logo.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.Add(logo);
            var sub = Text("title-sub", 34, Light);
            sub.text = "天文部の 30 日";
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

            _titleDetail = Text("title-detail", 26, Muted);
            _titleDetail.style.marginTop = 16;
            _titleDetail.style.maxWidth = 1200;
            _titleDetail.style.whiteSpace = WhiteSpace.Normal;
            _titleDetail.style.backgroundColor = WindowBg;
            _titleDetail.style.paddingLeft = 16;
            _titleDetail.style.paddingRight = 16;
            _title.Add(_titleDetail);

            // 操作説明
            _howTo = Window("howto");
            _howTo.style.position = Position.Absolute;
            _howTo.style.left = 260;
            _howTo.style.right = 260;
            _howTo.style.top = 90;
            root.Add(_howTo);
            var howTitle = Text("howto-title", 42, Accent);
            howTitle.text = "操作説明";
            howTitle.style.marginBottom = 16;
            _howTo.Add(howTitle);
            var how = Text("howto-body", 28, Light);
            how.style.whiteSpace = WhiteSpace.Normal;
            how.text =
                "↑↓ と Enter、またはクリック：行動・選択肢を選ぶ\n" +
                "Enter / Space / クリック：会話を送る（表示の途中なら全文を出す）\n" +
                "Tab：能力値の詳細と結末の目安　Esc：一時停止（タイトルへ戻る）\n\n" +
                "夏休みは 30 日。毎朝 1 つ行動を選ぶと、能力値と疲労が動き、その日の出来事が起きる。\n" +
                "天文部の先輩・星野澄と、図書室の同級生・本田栞。30 日目の夜、流星群の下で夏の終わり方が決まる。\n" +
                "毎朝自動でセーブされ、タイトルの「つづきから」で再開できる。";
            _howTo.Add(how);
            _howToHints = Hints("howto-hints");
            _howTo.Add(_howToHints);
            var howBack = Text("howto-back", 26, Muted);
            howBack.text = "クリックか Enter でタイトルへ";
            howBack.style.marginTop = 16;
            _howTo.Add(howBack);

            // 日付
            _date = Window("date");
            _date.style.position = Position.Absolute;
            _date.style.left = 40;
            _date.style.top = 28;
            _date.style.flexDirection = FlexDirection.Row;
            _date.style.alignItems = Align.FlexEnd;
            root.Add(_date);
            _day = Text("day", 44, Accent);
            _date.Add(_day);
            _weekday = Text("weekday", 30, Light);
            _weekday.style.marginLeft = 16;
            _date.Add(_weekday);

            // 能力値
            _stats = Window("stats");
            _stats.style.position = Position.Absolute;
            _stats.style.left = 40;
            _stats.style.top = 140;
            _stats.style.width = 470;
            root.Add(_stats);
            for (int i = 0; i < MaxStats; i++)
            {
                _statRows[i] = new StatRow("stat-" + i);
                _stats.Add(_statRows[i].Root);
            }

            // 行動の選択
            _actions = Window("actions");
            _actions.style.position = Position.Absolute;
            _actions.style.right = 60;
            _actions.style.top = 140;
            _actions.style.width = 600;
            root.Add(_actions);
            _actionsTitle = Text("actions-title", 30, Accent);
            _actionsTitle.text = "今日はなにをしよう？";
            _actionsTitle.style.marginBottom = 8;
            _actions.Add(_actionsTitle);
            for (int i = 0; i < MaxActions; i++)
            {
                _actionRows[i] = new Row("action-row-" + i, 32, detail: true);
                _actions.Add(_actionRows[i].Root);
            }

            // 結果
            _report = Window("report");
            _report.style.position = Position.Absolute;
            _report.style.right = 60;
            _report.style.top = 200;
            _report.style.width = 600;
            root.Add(_report);
            _reportTitle = Text("report-title", 40, Accent);
            _reportTitle.style.marginBottom = 12;
            _report.Add(_reportTitle);
            _reportBody = Text("report-body", 32, Light);
            _reportBody.style.whiteSpace = WhiteSpace.Normal;
            _report.Add(_reportBody);
            var reportNext = Text("report-next", 24, Muted);
            reportNext.text = "クリックか Enter で次へ";
            reportNext.style.marginTop = 16;
            _report.Add(reportNext);

            // 会話の枠
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
            _choices = new VisualElement { name = "choices", pickingMode = PickingMode.Ignore };
            _choices.style.position = Position.Absolute;
            // 立ち絵（中央）の顔にかからないよう右に寄せる。
            _choices.style.left = 1180;
            _choices.style.right = 80;
            _choices.style.top = 300;
            root.Add(_choices);
            for (int i = 0; i < MaxOptions; i++)
            {
                _optionBoxes[i] = Window("option-" + i);
                _optionBoxes[i].style.marginBottom = 22;
                _optionRows[i] = new Row("option-row-" + i, 36);
                _optionBoxes[i].Add(_optionRows[i].Root);
                _choices.Add(_optionBoxes[i]);
            }

            // 詳細（Tab）
            _detail = Window("detail");
            _detail.style.position = Position.Absolute;
            _detail.style.left = 400;
            _detail.style.right = 400;
            _detail.style.top = 140;
            root.Add(_detail);
            var detailTitle = Text("detail-title", 36, Accent);
            detailTitle.text = "能力値の詳細";
            detailTitle.style.marginBottom = 12;
            _detail.Add(detailTitle);
            _detailBody = Text("detail-body", 28, Light);
            _detailBody.style.whiteSpace = WhiteSpace.Normal;
            _detail.Add(_detailBody);
            _detailHints = Hints("detail-hints");
            _detail.Add(_detailHints);
            var detailBack = Text("detail-back", 24, Muted);
            detailBack.text = "Tab / Esc で閉じる";
            detailBack.style.marginTop = 16;
            _detail.Add(detailBack);

            // 操作の案内・案内
            _hint = Text("hint", 22, Muted);
            _hint.style.position = Position.Absolute;
            _hint.style.right = 40;
            _hint.style.top = 34;
            _hint.style.backgroundColor = new Color(0.05f, 0.07f, 0.12f, 0.6f);
            _hint.style.paddingLeft = 12;
            _hint.style.paddingRight = 12;
            _hint.style.paddingTop = 4;
            _hint.style.paddingBottom = 4;
            root.Add(_hint);
            _notice = Text("notice", 28, Light);
            _notice.style.position = Position.Absolute;
            _notice.style.top = 90;
            _notice.style.alignSelf = Align.Center;
            _notice.style.backgroundColor = WindowBg;
            _notice.style.paddingLeft = 24;
            _notice.style.paddingRight = 24;
            _notice.style.paddingTop = 8;
            _notice.style.paddingBottom = 8;
            root.Add(_notice);

            // 一時停止
            _pause = new VisualElement { name = "pause", pickingMode = PickingMode.Ignore };
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

            // 結果画面
            _result = new VisualElement { name = "result", pickingMode = PickingMode.Ignore };
            Fill(_result);
            _result.style.alignItems = Align.Center;
            _result.style.justifyContent = Justify.Center;
            root.Add(_result);
            var resultBox = Window("result-box");
            resultBox.style.width = 900;
            resultBox.style.alignItems = Align.Center;
            _result.Add(resultBox);
            _resultTitle = Text("result-title", 56, Accent);
            _resultTitle.style.marginBottom = 20;
            resultBox.Add(_resultTitle);
            _resultBody = Text("result-body", 30, Light);
            _resultBody.style.whiteSpace = WhiteSpace.Normal;
            _resultBody.style.unityTextAlign = TextAnchor.MiddleCenter;
            resultBox.Add(_resultBody);
            var back = Text("result-back", 26, Muted);
            back.text = "クリックか Enter でタイトルへ";
            back.style.marginTop = 24;
            resultBox.Add(back);
        }

        public VisualElement Root { get; }

        public void Render(SummerFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var screen = flow.Screen;
            var playing = flow.InSummer;
            var view = flow.View;
            Show(_title, screen == FlowScreen.Title);
            Show(_howTo, screen == FlowScreen.HowTo);
            if (screen == FlowScreen.HowTo) { _howToHints.text = HintText(flow); }
            Show(_pause, screen == FlowScreen.Paused);
            Show(_result, screen == FlowScreen.Result);
            Show(_date, playing);
            Show(_stats, playing && view is PlayView.Morning or PlayView.Report);
            Show(_actions, playing && view == PlayView.Morning);
            Show(_report, playing && view == PlayView.Report);
            Show(_window, playing && flow.InText && !flow.IsChoice);
            Show(_choices, playing && flow.IsChoice);
            Show(_detail, playing && flow.DetailOpen);
            Show(_hint, playing);
            _notice.text = flow.Notice;
            Show(_notice, flow.Notice.Length > 0);

            if (screen == FlowScreen.Title)
            {
                var items = flow.TitleItems;
                for (int i = 0; i < _titleRows.Length; i++) { _titleRows[i].Set(items[i].Label, "", flow.MenuCursor == i, items[i].Enabled); }

                // 「つづきから」の何日目か、選べない理由（壊れた・版が違う）を出す。ないだけなら出さない。
                var cont = items[SummerFlow.TitleContinue];
                _titleDetail.text = cont.Enabled ? "つづきから：" + cont.Detail : cont.Detail != "セーブがありません" ? cont.Detail : "";
                _titleDetail.style.color = cont.Enabled ? Muted : Danger;
                Show(_titleDetail, _titleDetail.text.Length > 0);
            }

            if (screen == FlowScreen.Paused)
            {
                for (int i = 0; i < _pauseRows.Length; i++) { _pauseRows[i].Set(flow.PauseItems[i].Label, "", flow.MenuCursor == i, true); }
            }

            if (playing)
            {
                _day.text = $"{flow.Day} 日目 / {flow.Days}";
                _weekday.text = flow.WeekdayName + "曜日";
                _hint.text = view == PlayView.Morning ? "↑↓ Enter / クリック 選ぶ　Tab 詳細　Esc メニュー" : "Enter / クリック 次へ　Tab 詳細　Esc メニュー";
                RenderStats(flow);
            }

            if (playing && view == PlayView.Morning)
            {
                var actions = flow.Actions;
                for (int i = 0; i < MaxActions; i++)
                {
                    var has = i < actions.Count;
                    Show(_actionRows[i].Root, has);
                    if (has) { _actionRows[i].Set(actions[i].Label, actions[i].Detail, flow.MenuCursor == i, actions[i].Enabled); }
                }
            }

            if (playing && view == PlayView.Report)
            {
                _reportTitle.text = flow.ReportTitle;
                _reportBody.text = flow.ReportChanges.Count > 0 ? string.Join("\n", flow.ReportChanges) : "変化なし";
            }

            if (playing && flow.InText)
            {
                var speaker = flow.SpeakerName;
                Show(_namePlate, speaker.Length > 0);
                _name.text = speaker;
                var c = flow.SpeakerColor;
                _name.style.color = speaker.Length > 0 ? new Color(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f, 1f) : Accent;
                _text.text = flow.VisibleText;
                Show(_next, flow.IsRevealed && (flow.Ticks / 30) % 2 == 0);
                var options = flow.Options;
                for (int i = 0; i < MaxOptions; i++)
                {
                    var has = i < options.Count;
                    Show(_optionBoxes[i], has);
                    if (!has) { continue; }
                    _optionRows[i].Set(options[i], "", flow.MenuCursor == i, true);
                    _optionBoxes[i].style.backgroundColor = flow.MenuCursor == i ? Chosen : WindowBg;
                }
            }

            if (playing && flow.DetailOpen)
            {
                var lines = new System.Text.StringBuilder();
                foreach (var s in flow.Stats)
                {
                    lines.Append(s.Name).Append('　').Append(s.Value.ToString(CultureInfo.InvariantCulture)).Append(" / ").Append(s.Max.ToString(CultureInfo.InvariantCulture));
                    if (s.IsHeart) { lines.Append('　').Append(Hearts(s.Hearts)); }
                    lines.Append('\n');
                }

                _detailBody.text = lines.ToString().TrimEnd('\n');
                _detailHints.text = HintText(flow);
            }

            if (screen == FlowScreen.Result)
            {
                _resultTitle.text = $"― {flow.EndingName} ―";
                var sb = new System.Text.StringBuilder();
                sb.Append("30 日間の夏休み　遊んだ時間 ").Append(Clock(flow.PlaySeconds)).Append("\n\n");
                var stats = new System.Collections.Generic.List<string>();
                var hearts = new System.Collections.Generic.List<string>();
                foreach (var s in flow.Stats)
                {
                    if (s.IsHeart) { hearts.Add(s.Name + " " + Hearts(s.Hearts)); }
                    else { stats.Add(s.Name + " " + s.Value.ToString(CultureInfo.InvariantCulture)); }
                }

                sb.Append(string.Join("　", stats)).Append('\n').Append(string.Join("　", hearts));
                _resultBody.text = sb.ToString();
            }
        }

        /// <summary>パネルの座標にあるメニュー（タイトル・一時停止・朝の行動・選択肢）の項目の番号。なければ -1。</summary>
        public int MenuIndexAt(SummerFlow flow, Vector2 panelPosition)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }

            // 詳細（モーダル）を開いている間は、背後の項目に当てない。
            if (flow.InSummer && flow.DetailOpen) { return -1; }
            switch (flow.Screen)
            {
                case FlowScreen.Title:
                    return IndexAt(_titleRows, panelPosition);
                case FlowScreen.Paused:
                    return IndexAt(_pauseRows, panelPosition);
                case FlowScreen.Playing when flow.View == PlayView.Morning:
                    return IndexAt(_actionRows, panelPosition);
                case FlowScreen.Playing when flow.IsChoice:
                    for (int i = 0; i < MaxOptions; i++)
                    {
                        if (_optionBoxes[i].resolvedStyle.display != DisplayStyle.None && _optionBoxes[i].worldBound.Contains(panelPosition)) { return i; }
                    }

                    return -1;
                default:
                    return -1;
            }
        }

        /// <summary>項目の矩形（パネルの座標、テストがクリックする位置を決める）。</summary>
        public Rect ItemBound(SummerFlow flow, int index)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            return flow.Screen switch
            {
                FlowScreen.Title => _titleRows[index].Root.worldBound,
                FlowScreen.Paused => _pauseRows[index].Root.worldBound,
                FlowScreen.Playing when flow.View == PlayView.Morning => _actionRows[index].Root.worldBound,
                FlowScreen.Playing when flow.IsChoice => _optionBoxes[index].worldBound,
                _ => throw new InvalidOperationException($"項目のない画面 {flow.Screen}"),
            };
        }

        private void RenderStats(SummerFlow flow)
        {
            var stats = flow.Stats;
            for (int i = 0; i < MaxStats; i++)
            {
                var has = i < stats.Count;
                Show(_statRows[i].Root, has);
                if (has) { _statRows[i].Set(stats[i], stats[i].Id == "fatigue"); }
            }
        }

        private static int IndexAt(Row[] rows, Vector2 p)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Root.resolvedStyle.display != DisplayStyle.None && rows[i].Root.worldBound.Contains(p)) { return i; }
            }

            return -1;
        }

        /// <summary>結末の目安（台本の @hint）。</summary>
        private static string HintText(SummerFlow flow) => flow.Book.Hints.Count == 0 ? "" : "結末の目安\n・" + string.Join("\n・", flow.Book.Hints);

        private static Label Hints(string name)
        {
            var l = Text(name, 26, Accent);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = 20;
            return l;
        }

        private static string Hearts(int n) => new string('♥', n) + new string('♡', 5 - n);

        private static string Clock(double seconds)
        {
            var s = (int)Math.Floor(seconds);
            return $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}";
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

        /// <summary>メニューの 1 行：選択の印・項目名・付記の列を固定幅に分ける（032）。</summary>
        private sealed class Row
        {
            private readonly Label? _detail;

            public Row(string name, int size, bool detail = false)
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
                if (detail)
                {
                    _detail = Text(name + "-detail", 22, Muted);
                    _detail.style.width = 260;
                    _detail.style.unityTextAlign = TextAnchor.MiddleRight;
                    Root.Add(_detail);
                }
                else
                {
                    var right = Text(name + "-right", size, Accent);
                    right.style.width = 48;
                    Root.Add(right);
                }
            }

            public VisualElement Root { get; }
            public Label Cursor { get; }
            public Label Label { get; }

            public void Set(string label, string detail, bool selected, bool enabled)
            {
                Label.text = label;
                Cursor.text = selected ? "▶" : "";
                Label.style.color = !enabled ? Muted : selected ? Accent : Light;
                if (_detail != null) { _detail.text = detail; }
            }
        }

        /// <summary>能力値の 1 行：名前・棒（好感度はハート）・値。</summary>
        private sealed class StatRow
        {
            private readonly Label _name;
            private readonly VisualElement _bar;
            private readonly VisualElement _fill;
            private readonly Label _hearts;
            private readonly Label _value;

            public StatRow(string name)
            {
                Root = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
                Root.style.flexDirection = FlexDirection.Row;
                Root.style.alignItems = Align.Center;
                Root.style.minHeight = 42;
                _name = Text(name + "-name", 28, Light);
                _name.style.width = 130;
                Root.Add(_name);
                _bar = new VisualElement { name = name + "-bar", pickingMode = PickingMode.Ignore };
                _bar.style.width = 200;
                _bar.style.height = 16;
                _bar.style.backgroundColor = BarBack;
                Root.Add(_bar);
                _fill = new VisualElement { name = name + "-fill", pickingMode = PickingMode.Ignore };
                _fill.style.height = 16;
                _bar.Add(_fill);
                _hearts = Text(name + "-hearts", 28, HeartColor);
                _hearts.style.width = 200;
                Root.Add(_hearts);
                _value = Text(name + "-value", 26, Muted);
                _value.style.width = 90;
                _value.style.unityTextAlign = TextAnchor.MiddleRight;
                Root.Add(_value);
            }

            public VisualElement Root { get; }

            public void Set(StatLine s, bool cost)
            {
                _name.text = s.Name;
                Show(_bar, !s.IsHeart);
                Show(_hearts, s.IsHeart);
                _hearts.text = Hearts(s.Hearts);
                _fill.style.width = Length.Percent(Mathf.Clamp01(s.Fill) * 100f);
                _fill.style.backgroundColor = cost ? CostFill : BarFill;
                _value.text = s.Value.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
