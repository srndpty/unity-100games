#nullable enable
using System;
using System.Globalization;
using Game.App;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// UI Toolkit（<see cref="PanelRenderer"/>）の HUD：タイトル・遊び方・右の板（手番・アゲハマ・CPU の考え中・通知・パスと一時停止のボタン）・
    /// 盤の座標・一時停止・死石の確定・結果。要素は作るときに 1 度だけ組み、毎フレーム文字と表示の有無だけを変える。選択の印（▶）は固定幅の列に分ける（032）。
    /// クリックは UI のイベントを使わず、押した位置をパネルの座標で各項目の矩形と照合する（<see cref="ClickIndexAt"/>、046）。
    /// </summary>
    public sealed class SekiteiHud
    {
        public const string Title = "石庭";
        public const string Subtitle = "SEKITEI ― 九路の碁";

        private const string ColumnLetters = "ABCDEFGHJ";

        private static readonly Color WindowBg = new(0.10f, 0.08f, 0.06f, 0.88f);
        private static readonly Color Border = new(0.86f, 0.74f, 0.50f, 1f);
        private static readonly Color Light = new(0.97f, 0.95f, 0.90f, 1f);
        private static readonly Color Muted = new(0.75f, 0.70f, 0.62f, 1f);
        private static readonly Color Accent = new(1f, 0.82f, 0.42f, 1f);
        private static readonly Color Alert = new(1f, 0.55f, 0.45f, 1f);
        private static readonly Color Ink = new(0.24f, 0.15f, 0.06f, 1f);

        private readonly VisualElement _title;
        private readonly Row[] _titleRows = new Row[GoFlow.TitleCount];
        private readonly VisualElement _howTo;
        private readonly VisualElement _side;
        private readonly Label _turn;
        private readonly Label _players;
        private readonly Label _counts;
        private readonly Label _thinking;
        private readonly Label _notice;
        private readonly VisualElement _playButtons;
        private readonly Button _pass;
        private readonly Button _pauseButton;
        private readonly VisualElement _scoring;
        private readonly Label _scoreText;
        private readonly Button _confirm;
        private readonly Label _hint;
        private readonly Label[] _coords = new Label[Points.Size * 2];
        private readonly VisualElement _pause;
        private readonly Row[] _pauseRows = new Row[GoFlow.PauseCount];
        private readonly VisualElement _result;
        private readonly Label _outcome;
        private readonly Label _detail;
        private readonly Row[] _resultRows = new Row[GoFlow.ResultCount];

        public SekiteiHud(VisualElement root, Font? font)
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

            // 盤の座標（列は下、行は左）。
            for (var i = 0; i < _coords.Length; i++)
            {
                _coords[i] = Text("coord-" + i, 24, Ink);
                _coords[i].style.position = Position.Absolute;
                _coords[i].style.width = 40;
                _coords[i].style.unityTextAlign = TextAnchor.MiddleCenter;
                _coords[i].text = i < Points.Size ? ColumnLetters[i].ToString() : (i - Points.Size + 1).ToString(CultureInfo.InvariantCulture);
                root.Add(_coords[i]);
            }

            // 右の板
            _side = Window("side");
            _side.style.position = Position.Absolute;
            _side.style.right = 48;
            _side.style.top = 48;
            _side.style.width = 640;
            root.Add(_side);
            _turn = Text("turn", 40, Accent);
            _turn.style.marginBottom = 8;
            _side.Add(_turn);
            _players = Text("players", 26, Light);
            _players.style.marginBottom = 8;
            _side.Add(_players);
            _counts = Text("counts", 26, Light);
            _counts.style.marginBottom = 8;
            _side.Add(_counts);
            _thinking = Text("thinking", 26, Accent);
            _thinking.style.minHeight = 36;
            _side.Add(_thinking);
            _notice = Text("notice", 28, Alert);
            _notice.style.minHeight = 40;
            _notice.style.whiteSpace = WhiteSpace.Normal;
            _side.Add(_notice);
            _playButtons = new VisualElement { name = "play-buttons", pickingMode = PickingMode.Ignore };
            _playButtons.style.flexDirection = FlexDirection.Row;
            _playButtons.style.marginTop = 12;
            _side.Add(_playButtons);
            _pass = new Button("pass", "パス（P）");
            _pass.Root.style.marginRight = 16;
            _playButtons.Add(_pass.Root);
            _pauseButton = new Button("pause-button", "一時停止（Esc）");
            _playButtons.Add(_pauseButton.Root);

            _scoring = new VisualElement { name = "scoring", pickingMode = PickingMode.Ignore };
            _side.Add(_scoring);
            var scoringTitle = Text("scoring-title", 34, Accent);
            scoringTitle.text = "死石の確定";
            scoringTitle.style.marginTop = 8;
            _scoring.Add(scoringTitle);
            var scoringHelp = Text("scoring-help", 22, Muted);
            scoringHelp.style.whiteSpace = WhiteSpace.Normal;
            scoringHelp.text = "薄い石は死石と推定した石。連をクリック（または矢印＋Enter）で生死を切り替える。小さな四角は地。";
            scoringHelp.style.marginBottom = 8;
            _scoring.Add(scoringHelp);
            _scoreText = Text("score-text", 28, Light);
            _scoreText.style.marginBottom = 12;
            _scoring.Add(_scoreText);
            _confirm = new Button("confirm", "確定（Esc）");
            _scoring.Add(_confirm.Root);

            _hint = Text("hint", 22, Muted);
            _hint.style.marginTop = 16;
            _hint.style.whiteSpace = WhiteSpace.Normal;
            _side.Add(_hint);

            // タイトル
            // 盤の右の列に置く（盤の線と重ねない）。
            _title = new VisualElement { name = "title", pickingMode = PickingMode.Ignore };
            _title.style.position = Position.Absolute;
            _title.style.top = 0;
            _title.style.bottom = 0;
            _title.style.right = 40;
            _title.style.width = 760;
            _title.style.alignItems = Align.Center;
            _title.style.justifyContent = Justify.Center;
            root.Add(_title);
            var logo = Text("title-main", 120, Accent);
            logo.text = Title;
            logo.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.Add(logo);
            var sub = Text("title-sub", 34, Light);
            sub.text = Subtitle;
            sub.style.marginBottom = 32;
            _title.Add(sub);
            var menu = Window("title-menu");
            menu.style.width = 680;
            _title.Add(menu);
            for (var i = 0; i < _titleRows.Length; i++)
            {
                _titleRows[i] = new Row("title-row-" + i, 32);
                menu.Add(_titleRows[i].Root);
            }

            var titleHint = Text("title-hint", 22, Muted);
            titleHint.text = "↑↓ 選ぶ　Enter 決定　強さと石は ←→ でも切り替え　クリックでも選べる";
            titleHint.style.marginTop = 16;
            titleHint.style.backgroundColor = WindowBg;
            _title.Add(titleHint);

            // 遊び方
            _howTo = Window("howto");
            _howTo.style.position = Position.Absolute;
            _howTo.style.left = 260;
            _howTo.style.right = 260;
            _howTo.style.top = 90;
            root.Add(_howTo);
            var howTitle = Text("howto-title", 42, Accent);
            howTitle.text = "遊び方";
            howTitle.style.marginBottom = 12;
            _howTo.Add(howTitle);
            var how = Text("howto-body", 26, Light);
            how.style.whiteSpace = WhiteSpace.Normal;
            how.text =
                "9 路盤で CPU と打つ囲碁。黒が先手。交点をクリック（または矢印で選んで Enter）して石を置く。\n" +
                "・呼吸点（上下左右の空点）がなくなった相手の石は取れる。\n" +
                "・自殺手（取れずに自分の呼吸点がなくなる手）は打てない。\n" +
                "・コウ：取られた直後の 1 子の取り返しはできない。過去と同じ盤面に戻る手も打てない（同型反復）。\n" +
                "・P でパス。両者が続けてパスすると終局する。Esc の一時停止から投了もできる。\n" +
                "・終局したら死石を確かめて確定する。中国式（石＋地の面積）で数え、白にコミ 7 目。\n\n" +
                "クリックか Enter でタイトルへ";
            _howTo.Add(how);

            // 一時停止
            _pause = new VisualElement { name = "pause", pickingMode = PickingMode.Ignore };
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
            for (var i = 0; i < _pauseRows.Length; i++)
            {
                _pauseRows[i] = new Row("pause-row-" + i, 34);
                pauseBox.Add(_pauseRows[i].Root);
            }

            // 結果
            _result = new VisualElement { name = "result", pickingMode = PickingMode.Ignore };
            Fill(_result);
            _result.style.alignItems = Align.FlexEnd;
            _result.style.justifyContent = Justify.Center;
            _result.style.paddingRight = 48;
            root.Add(_result);
            var resultBox = Window("result-box");
            resultBox.style.width = 640;
            resultBox.style.alignItems = Align.Center;
            _result.Add(resultBox);
            _outcome = Text("outcome", 80, Accent);
            resultBox.Add(_outcome);
            _detail = Text("detail", 28, Light);
            _detail.style.unityTextAlign = TextAnchor.MiddleCenter;
            _detail.style.whiteSpace = WhiteSpace.Normal;
            _detail.style.marginBottom = 16;
            resultBox.Add(_detail);
            for (var i = 0; i < _resultRows.Length; i++)
            {
                _resultRows[i] = new Row("result-row-" + i, 32);
                _resultRows[i].Root.style.width = 480;
                resultBox.Add(_resultRows[i].Root);
            }
        }

        public VisualElement Root { get; }

        public static string Label(Stone stone) => stone == Stone.Black ? "黒" : "白";

        public static string MarginText(ScoreResult score) => score.Margin > 0 ? $"黒の {score.Margin} 目勝ち" : score.Margin < 0 ? $"白の {-score.Margin} 目勝ち" : "持碁（引き分け）";

        public void Render(GoFlow flow, Camera camera)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var screen = flow.Screen;
            var game = flow.Game;
            var inGame = game != null && screen is FlowScreen.Playing or FlowScreen.Paused or FlowScreen.Scoring;
            Show(_title, screen == FlowScreen.Title);
            Show(_howTo, screen == FlowScreen.HowTo);
            Show(_pause, screen == FlowScreen.Paused);
            Show(_result, screen == FlowScreen.Result);
            Show(_side, inGame);
            Show(_playButtons, screen is FlowScreen.Playing or FlowScreen.Paused);
            Show(_scoring, screen == FlowScreen.Scoring);
            RenderCoords(camera, screen != FlowScreen.HowTo);

            if (screen == FlowScreen.Title)
            {
                _titleRows[GoFlow.TitleStart].Set("対局を始める", flow.MenuCursor == GoFlow.TitleStart, false);
                _titleRows[GoFlow.TitleLevel].Set($"CPU の強さ：{GoFlow.LevelLabel(flow.Level)}", flow.MenuCursor == GoFlow.TitleLevel, true);
                _titleRows[GoFlow.TitleSide].Set($"あなたの石：{(flow.PlayerSide == Stone.Black ? "黒（先手）" : "白（後手）")}", flow.MenuCursor == GoFlow.TitleSide, true);
                _titleRows[GoFlow.TitleHowTo].Set("遊び方", flow.MenuCursor == GoFlow.TitleHowTo, false);
                _titleRows[GoFlow.TitleQuit].Set("終了", flow.MenuCursor == GoFlow.TitleQuit, false);
            }

            if (screen == FlowScreen.Paused)
            {
                _pauseRows[GoFlow.PauseResume].Set("再開", flow.MenuCursor == GoFlow.PauseResume, false);
                _pauseRows[GoFlow.PauseResign].Set("投了する", flow.MenuCursor == GoFlow.PauseResign, false);
                _pauseRows[GoFlow.PauseTitle].Set("タイトルへ", flow.MenuCursor == GoFlow.PauseTitle, false);
            }

            if (inGame) { RenderSide(flow, game!); }
            if (screen == FlowScreen.Result && game != null) { RenderResult(flow, game); }
        }

        /// <summary>パネルの座標にあるクリックの対象（メニューの項目・HUD のボタン）の番号。なければ -1（盤は Unity 側で判定する）。</summary>
        public int ClickIndexAt(GoFlow flow, Vector2 panelPosition)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            switch (flow.Screen)
            {
                case FlowScreen.Title:
                    return RowAt(_titleRows, panelPosition);
                case FlowScreen.Paused:
                    return RowAt(_pauseRows, panelPosition);
                case FlowScreen.Result:
                    return RowAt(_resultRows, panelPosition);
                case FlowScreen.HowTo:
                    return 0;
                case FlowScreen.Playing:
                    if (_pass.Root.worldBound.Contains(panelPosition)) { return GoFlow.ClickPass; }
                    if (_pauseButton.Root.worldBound.Contains(panelPosition)) { return GoFlow.ClickPause; }
                    return -1;
                case FlowScreen.Scoring:
                    return _confirm.Root.worldBound.Contains(panelPosition) ? GoFlow.ClickConfirmScore : -1;
                default:
                    return -1;
            }
        }

        /// <summary>パネルの座標が右の板・メニューの窓の上か（盤のクリックとして扱わない）。</summary>
        public bool IsOverPanel(Vector2 panelPosition) => _side.resolvedStyle.display == DisplayStyle.Flex && _side.worldBound.Contains(panelPosition);

        private void RenderSide(GoFlow flow, GoGame game)
        {
            var player = flow.MatchPlayerSide;
            var cpu = flow.CpuSide;
            var toMove = game.ToMove;
            if (flow.Screen == FlowScreen.Scoring)
            {
                _turn.text = "終局";
            }
            else
            {
                _turn.text = $"{(toMove == Stone.Black ? "●" : "○")} {Label(toMove)}の番" + (toMove == player ? "（あなた）" : "（CPU）");
            }

            _players.text = $"あなた：{Label(player)}　CPU：{Label(cpu)}（{GoFlow.LevelLabel(flow.MatchLevel)}）\nコミ {game.Komi} 目・中国式（面積）";
            var last = game.LastMove;
            var lastText = last.HasValue ? $"{Label(toMove.Opponent())} {Points.Name(last.Value)}" : "—";
            _counts.text = $"アゲハマ　黒 {game.Captures(Stone.Black)}　白 {game.Captures(Stone.White)}\n手数 {game.MoveCount}　直前 {lastText}";

            var search = flow.Cpu?.Search;
            if (flow.IsCpuThinking && search != null)
            {
                var win = search.Done > 0 ? $"　見込み {search.BestWinPermille / 10} %" : "";
                _thinking.text = $"CPU 考え中… {search.Done} / {search.Target}{win}";
            }
            else
            {
                _thinking.text = flow.IsCpuThinking ? "CPU 考え中…" : "";
            }

            _notice.text = flow.Notice ?? "";
            _pass.SetEnabled(flow.IsPlayerTurn);

            if (flow.Screen == FlowScreen.Scoring)
            {
                var s = flow.ScorePreview;
                _scoreText.text =
                    $"黒　石 {s.BlackStones} ＋ 地 {s.BlackTerritory} ＝ {s.BlackArea}\n" +
                    $"白　石 {s.WhiteStones} ＋ 地 {s.WhiteTerritory} ＋ コミ {s.Komi} ＝ {s.WhiteArea + s.Komi}\n" +
                    $"→ {MarginText(s)}";
                _hint.text = "クリック：連の生死を切り替え　Esc／確定：計点する";
            }
            else
            {
                _hint.text = "クリック／矢印＋Enter：打つ　P：パス　Esc：一時停止（投了）";
            }
        }

        private void RenderResult(GoFlow flow, GoGame game)
        {
            _outcome.text = flow.Outcome switch
            {
                Outcome.Win => "勝ち！",
                Outcome.Loss => "負け",
                _ => "引き分け",
            };
            string detail;
            if (game.End == GameEnd.Resignation)
            {
                var who = game.Resigned == flow.MatchPlayerSide ? "あなた" : "CPU";
                detail = $"{Label(game.Resigned)}（{who}）の投了　{game.MoveCount} 手";
            }
            else
            {
                var s = flow.FinalScore!.Value;
                var reason = game.End == GameEnd.MoveLimit ? "手数の上限で終局" : "両者パスで終局";
                detail =
                    $"{reason}　{game.MoveCount} 手\n" +
                    $"黒 {s.BlackArea}（石 {s.BlackStones}・地 {s.BlackTerritory}）\n" +
                    $"白 {s.WhiteArea}（石 {s.WhiteStones}・地 {s.WhiteTerritory}）＋ コミ {s.Komi}\n" +
                    MarginText(s);
            }

            _detail.text = detail + $"\nあなた：{Label(flow.MatchPlayerSide)}　CPU：{GoFlow.LevelLabel(flow.MatchLevel)}";
            _resultRows[GoFlow.ResultAgain].Set("もう一度（同じ設定）", flow.MenuCursor == GoFlow.ResultAgain, false);
            _resultRows[GoFlow.ResultTitle].Set("タイトルへ", flow.MenuCursor == GoFlow.ResultTitle, false);
        }

        private void RenderCoords(Camera camera, bool show)
        {
            var panel = Root.panel;
            for (var i = 0; i < _coords.Length; i++)
            {
                Show(_coords[i], show && panel != null);
                if (!show || panel == null) { continue; }
                var world = i < Points.Size
                    ? BoardProjection.World(Points.At(i, 0)) + new Vector3(0f, -0.72f, 0f)
                    : BoardProjection.World(Points.At(0, i - Points.Size)) + new Vector3(-0.72f, 0f, 0f);
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, camera);
                _coords[i].style.left = p.x - 20;
                _coords[i].style.top = p.y - 16;
            }
        }

        private static int RowAt(Row[] rows, Vector2 panelPosition)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i].Root.worldBound.Contains(panelPosition)) { return i; }
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

        /// <summary>メニューの 1 行：選択の印・項目名・右の印（←→ で切り替えられる項目）の列を固定幅に分ける（032）。</summary>
        private sealed class Row
        {
            private readonly Label _cursor;
            private readonly Label _label;
            private readonly Label _right;

            public Row(string name, int size)
            {
                Root = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
                Root.style.flexDirection = FlexDirection.Row;
                Root.style.alignItems = Align.Center;
                Root.style.minHeight = size + 14;
                _cursor = Text(name + "-cursor", size, Accent);
                _cursor.style.width = 48;
                Root.Add(_cursor);
                _label = Text(name + "-label", size, Light);
                _label.style.flexGrow = 1;
                Root.Add(_label);
                _right = Text(name + "-right", size, Accent);
                _right.style.width = 48;
                Root.Add(_right);
            }

            public VisualElement Root { get; }

            public void Set(string label, bool selected, bool cycles)
            {
                _label.text = label;
                _cursor.text = selected ? "▶" : "";
                _label.style.color = selected ? Accent : Light;
                _right.text = selected && cycles ? "⇆" : "";
            }
        }

        /// <summary>押せる枠（見た目だけ。押下は <see cref="ClickIndexAt"/> で照合する）。</summary>
        private sealed class Button
        {
            private readonly Label _label;

            public Button(string name, string text)
            {
                Root = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
                Root.style.borderTopWidth = 2;
                Root.style.borderBottomWidth = 2;
                Root.style.borderLeftWidth = 2;
                Root.style.borderRightWidth = 2;
                Root.style.borderTopColor = Border;
                Root.style.borderBottomColor = Border;
                Root.style.borderLeftColor = Border;
                Root.style.borderRightColor = Border;
                Root.style.borderTopLeftRadius = 8;
                Root.style.borderTopRightRadius = 8;
                Root.style.borderBottomLeftRadius = 8;
                Root.style.borderBottomRightRadius = 8;
                Root.style.paddingLeft = 18;
                Root.style.paddingRight = 18;
                Root.style.paddingTop = 6;
                Root.style.paddingBottom = 6;
                Root.style.backgroundColor = new Color(0.30f, 0.22f, 0.12f, 1f);
                _label = Text(name + "-label", 28, Light);
                _label.text = text;
                Root.Add(_label);
            }

            public VisualElement Root { get; }

            public void SetEnabled(bool enabled)
            {
                Root.style.opacity = enabled ? 1f : 0.45f;
            }
        }
    }
}
