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
    /// UI Toolkit（<see cref="PanelRenderer"/>）の HUD：タイトル・操作説明・スコアと時計・再開の種類・ゴールと終了の帯・操作の案内・役割の名札・一時停止・結果。
    /// 要素は作るときに 1 度だけ組み、毎フレーム文字と表示の有無だけを変える。選択の印（▶）は固定幅の列に分ける（032）。
    /// クリックは UI のイベントを使わず、押した位置をパネルの座標で各項目の矩形と照合する（<see cref="MenuIndexAt"/>）。
    /// </summary>
    public sealed class SoccerHud
    {
        public const string Title = "MINI PITCH";
        public const string Subtitle = "3対3 サッカー";

        private static readonly Color WindowBg = new(0.05f, 0.09f, 0.08f, 0.86f);
        private static readonly Color Border = new(0.70f, 0.90f, 0.75f, 1f);
        private static readonly Color Light = new(0.97f, 0.97f, 0.95f, 1f);
        private static readonly Color Muted = new(0.70f, 0.75f, 0.72f, 1f);
        private static readonly Color Accent = new(1f, 0.88f, 0.40f, 1f);

        private readonly VisualElement _title;
        private readonly Row[] _titleRows = new Row[4];
        private readonly VisualElement _howTo;
        private readonly VisualElement _board;
        private readonly Label _score;
        private readonly Label _clock;
        private readonly Label _restart;
        private readonly Label _banner;
        private readonly Label _hint;
        private readonly Label _release;
        private readonly Label[] _roles = new Label[Match.PlayerCount];
        private readonly VisualElement _pause;
        private readonly Row[] _pauseRows = new Row[2];
        private readonly VisualElement _result;
        private readonly Label _outcome;
        private readonly Label _finalScore;
        private readonly Label _statsTable;

        public SoccerHud(VisualElement root, Font? font)
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
            _title = new VisualElement { name = "title", pickingMode = PickingMode.Ignore };
            Fill(_title);
            _title.style.alignItems = Align.Center;
            _title.style.paddingTop = 170;
            root.Add(_title);
            var logo = Text("title-main", 96, Accent);
            logo.text = Title;
            logo.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.Add(logo);
            var sub = Text("title-sub", 36, Light);
            sub.text = Subtitle;
            sub.style.marginBottom = 40;
            _title.Add(sub);
            var menu = Window("title-menu");
            menu.style.width = 640;
            _title.Add(menu);
            for (int i = 0; i < _titleRows.Length; i++)
            {
                _titleRows[i] = new Row("title-row-" + i, 32);
                menu.Add(_titleRows[i].Root);
            }

            var titleHint = Text("title-hint", 22, Muted);
            titleHint.text = "↑↓ 選ぶ　Enter 決定　相手の方針は ←→ でも切り替え";
            titleHint.style.marginTop = 16;
            titleHint.style.backgroundColor = WindowBg;
            _title.Add(titleHint);

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
                "WASD / 矢印：移動\n" +
                "J：パス（向いている方の味方へ）　守備ではタックル\n" +
                "K：シュート（W で左の隅、S で右の隅）\n" +
                "L：ループパス（相手の上を越える）\n" +
                "Space：操作する選手の切替　Esc：一時停止　F1：役割の表示\n\n" +
                "3 対 3・5 分。あなたは青のフィールドの 1 人を操作する。もう 1 人と GK は自動で動く。\n" +
                "切り替えた直後は、押していた移動キーをいったん離すと新しい選手が動く。\n\n" +
                "クリックか Enter でタイトルへ";
            _howTo.Add(how);

            // スコアと時計
            _board = Window("board");
            _board.style.position = Position.Absolute;
            _board.style.top = 16;
            _board.style.alignSelf = Align.Center;
            _board.style.flexDirection = FlexDirection.Row;
            _board.style.alignItems = Align.Center;
            _board.style.paddingTop = 6;
            _board.style.paddingBottom = 6;
            root.Add(_board);
            _score = Text("score", 40, Light);
            _board.Add(_score);
            _clock = Text("clock", 34, Accent);
            _clock.style.marginLeft = 32;
            _board.Add(_clock);
            _restart = Text("restart", 26, Accent);
            _restart.style.position = Position.Absolute;
            _restart.style.top = 92;
            _restart.style.alignSelf = Align.Center;
            _restart.style.backgroundColor = WindowBg;
            _restart.style.paddingLeft = 16;
            _restart.style.paddingRight = 16;
            root.Add(_restart);

            _banner = Text("banner", 110, Accent);
            _banner.style.position = Position.Absolute;
            _banner.style.top = 380;
            _banner.style.alignSelf = Align.Center;
            _banner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _banner.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            _banner.style.paddingLeft = 40;
            _banner.style.paddingRight = 40;
            root.Add(_banner);

            _hint = Text("hint", 22, Muted);
            _hint.text = "WASD 移動　J パス／タックル　K シュート　L ループ　Space 切替　Esc 一時停止　F1 役割";
            _hint.style.position = Position.Absolute;
            _hint.style.bottom = 12;
            _hint.style.alignSelf = Align.Center;
            _hint.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _hint.style.paddingLeft = 12;
            _hint.style.paddingRight = 12;
            root.Add(_hint);
            _release = Text("release", 26, Accent);
            _release.text = "移動キーを離すと、切り替えた選手を動かせます";
            _release.style.position = Position.Absolute;
            _release.style.bottom = 52;
            _release.style.alignSelf = Align.Center;
            _release.style.backgroundColor = WindowBg;
            _release.style.paddingLeft = 12;
            _release.style.paddingRight = 12;
            root.Add(_release);

            for (int i = 0; i < _roles.Length; i++)
            {
                _roles[i] = Text("role-" + i, 24, Light);
                _roles[i].style.position = Position.Absolute;
                _roles[i].style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
                _roles[i].style.paddingLeft = 6;
                _roles[i].style.paddingRight = 6;
                root.Add(_roles[i]);
            }

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

            // 結果
            _result = new VisualElement { name = "result", pickingMode = PickingMode.Ignore };
            Fill(_result);
            _result.style.alignItems = Align.Center;
            _result.style.justifyContent = Justify.Center;
            root.Add(_result);
            var resultBox = Window("result-box");
            resultBox.style.width = 820;
            resultBox.style.alignItems = Align.Center;
            _result.Add(resultBox);
            _outcome = Text("outcome", 72, Accent);
            resultBox.Add(_outcome);
            _finalScore = Text("final-score", 48, Light);
            _finalScore.style.marginBottom = 16;
            resultBox.Add(_finalScore);
            _statsTable = Text("stats-table", 28, Light);
            _statsTable.style.unityTextAlign = TextAnchor.MiddleCenter;
            resultBox.Add(_statsTable);
            var back = Text("result-back", 26, Muted);
            back.text = "クリックか Enter でタイトルへ";
            back.style.marginTop = 24;
            resultBox.Add(back);
        }

        public VisualElement Root { get; }

        public void Render(SoccerFlow flow, Camera camera, bool showRoles)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var screen = flow.Screen;
            var m = flow.Match;
            var inMatch = m != null && screen is FlowScreen.Playing or FlowScreen.Paused;
            Show(_title, screen == FlowScreen.Title);
            Show(_howTo, screen == FlowScreen.HowTo);
            Show(_pause, screen == FlowScreen.Paused);
            Show(_result, screen == FlowScreen.Result);
            Show(_board, inMatch);
            Show(_hint, inMatch && !flow.Autopilot);
            Show(_release, inMatch && flow.WaitingForRelease && !flow.Autopilot);

            if (screen == FlowScreen.Title)
            {
                var items = flow.TitleItems;
                for (int i = 0; i < _titleRows.Length; i++) { _titleRows[i].Set(items[i].Label, flow.MenuCursor == i, i == SoccerFlow.TitleTactics); }
            }

            if (screen == FlowScreen.Paused)
            {
                for (int i = 0; i < _pauseRows.Length; i++) { _pauseRows[i].Set(flow.PauseItems[i].Label, flow.MenuCursor == i, false); }
            }

            var restart = "";
            var banner = "";
            if (inMatch)
            {
                _score.text = $"青 {m!.Score(Team.Blue)} − {m.Score(Team.Red)} 赤";
                _clock.text = Clock(m.RemainingTicks);
                if (m.Phase is MatchPhase.Stopped or MatchPhase.SetPiece) { restart = RestartName(m.Restart) + "（" + (m.RestartTeam == Team.Blue ? "青" : "赤") + "）"; }
                if (m.Phase == MatchPhase.Goal) { banner = "ゴール！"; }
                if (m.Phase == MatchPhase.Ended) { banner = "試合終了"; }
            }

            _restart.text = restart;
            Show(_restart, restart.Length > 0);
            _banner.text = banner;
            Show(_banner, banner.Length > 0);
            RenderRoles(flow, camera, inMatch && showRoles);

            if (screen == FlowScreen.Result && m != null)
            {
                _outcome.text = flow.Outcome > 0 ? "勝ち！" : flow.Outcome < 0 ? "負け" : "引き分け";
                _finalScore.text = $"青 {m.Score(Team.Blue)} − {m.Score(Team.Red)} 赤　（相手：{flow.Opponent.Name}）";
                var b = flow.Stats.Of(Team.Blue);
                var r = flow.Stats.Of(Team.Red);
                var total = Math.Max(1, b.PossessionTicks + r.PossessionTicks);
                _statsTable.text =
                    $"シュート　{b.Shots} − {r.Shots}\n" +
                    $"パス成功　{b.PassesCompleted}/{b.Passes} − {r.PassesCompleted}/{r.Passes}\n" +
                    $"保持率　{b.PossessionTicks * 100 / total}% − {r.PossessionTicks * 100 / total}%\n" +
                    $"タックル　{b.Tackles} − {r.Tackles}\n" +
                    $"遊んだ時間　{Clock((int)(flow.PlaySeconds * Pitch.TickRate))}";
            }
        }

        /// <summary>パネルの座標にあるメニュー（タイトル・一時停止）の項目の番号。なければ -1。</summary>
        public int MenuIndexAt(SoccerFlow flow, Vector2 panelPosition)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var rows = flow.Screen == FlowScreen.Title ? _titleRows : flow.Screen == FlowScreen.Paused ? _pauseRows : Array.Empty<Row>();
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Root.worldBound.Contains(panelPosition)) { return i; }
            }

            return -1;
        }

        public static string RestartName(RestartKind kind) => kind switch
        {
            RestartKind.Kickoff => "キックオフ",
            RestartKind.ThrowIn => "スローイン",
            RestartKind.GoalKick => "ゴールキック",
            _ => "コーナーキック",
        };

        public static string RoleName(Role role) => role switch
        {
            Role.Support => "支援",
            Role.Runner => "抜け出し",
            Role.Press => "寄せ",
            Role.Cover => "カバー",
            Role.Chase => "回収",
            Role.Hold => "待機",
            _ => "",
        };

        private void RenderRoles(SoccerFlow flow, Camera camera, bool show)
        {
            var m = flow.Match;
            for (int i = 0; i < _roles.Length; i++)
            {
                var ai = Match.TeamOf(i) == Team.Blue ? flow.BlueAi : flow.RedAi;
                var name = show && m != null && ai != null ? RoleName(ai.RoleOf(i)) : "";
                if (show && m != null && i == flow.Controlled && !flow.Autopilot) { name = "あなた"; }
                _roles[i].text = name;
                Show(_roles[i], name.Length > 0);
                if (name.Length == 0 || Root.panel == null) { continue; }
                var world = PitchProjection.Ground(m!.Players[i].Position) + new Vector3(0f, 1.3f, 0f);
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(Root.panel, world, camera);
                _roles[i].style.left = p.x - 30;
                _roles[i].style.top = p.y - 20;
                _roles[i].style.color = SoccerStage.ColorOf(i);
            }
        }

        private static string Clock(int ticks)
        {
            var s = Math.Max(0, (ticks + Pitch.TickRate - 1) / Pitch.TickRate);
            return $"{s / 60}:{(s % 60).ToString("00", CultureInfo.InvariantCulture)}";
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
            private readonly Label _right;

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
                _right = Text(name + "-right", size, Accent);
                _right.style.width = 48;
                Root.Add(_right);
            }

            public VisualElement Root { get; }
            public Label Cursor { get; }
            public Label Label { get; }

            public void Set(string label, bool selected, bool cycles)
            {
                Label.text = label;
                Cursor.text = selected ? "▶" : "";
                Label.style.color = selected ? Accent : Light;
                _right.text = selected && cycles ? "⇆" : "";
            }
        }
    }
}
