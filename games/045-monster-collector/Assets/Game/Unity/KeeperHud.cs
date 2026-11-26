#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// UI Toolkit（<see cref="PanelRenderer"/>）の HUD。要素は作るときに 1 度だけ組み、毎フレーム文字と表示の有無だけを変える。
    /// 選択の印（▶）は固定幅の列に分け、選び直しで項目名が動かないようにする（032）。
    /// </summary>
    public sealed class KeeperHud
    {
        public const int MenuRows = 12;

        private static readonly Color WindowBg = new(0.06f, 0.09f, 0.08f, 0.93f);
        private static readonly Color Border = new(0.78f, 0.86f, 0.52f, 1f);
        private static readonly Color Light = new(0.95f, 0.95f, 0.90f, 1f);
        private static readonly Color Muted = new(0.55f, 0.58f, 0.55f, 1f);
        private static readonly Color Accent = new(1f, 0.85f, 0.42f, 1f);
        private static readonly Color Danger = new(1f, 0.46f, 0.42f, 1f);

        private readonly VisualElement _title;
        private readonly Row[] _titleRows = new Row[3];
        private readonly Label _titleNotice;
        private readonly Label _titleRecords;
        private readonly VisualElement _howTo;
        private readonly VisualElement _fieldTop;
        private readonly Label _fieldPlace;
        private readonly Label _fieldParty;
        private readonly Label _fieldItems;
        private readonly Label _guide;
        private readonly VisualElement _guideBox;
        private readonly VisualElement _dialog;
        private readonly Label _dialogText;
        private readonly VisualElement _menu;
        private readonly Label _menuHeading;
        private readonly Row[] _menuRows = new Row[MenuRows];
        private readonly Label _menuNotice;
        private readonly VisualElement _foeBox;
        private readonly Label _foeName;
        private readonly VisualElement _foeBar;
        private readonly Label _foeStatus;
        private readonly VisualElement _playerBox;
        private readonly Label _playerName;
        private readonly VisualElement _playerBar;
        private readonly Label _playerHp;
        private readonly VisualElement _playerExp;
        private readonly Label _playerStatus;
        private readonly VisualElement _battleMessage;
        private readonly Label _battleText;
        private readonly VisualElement _battleMenu;
        private readonly Label _battleHeading;
        private readonly Row[] _battleRows = new Row[6];
        private readonly Label _battleNotice;
        private readonly VisualElement _result;
        private readonly Label _resultBody;
        private readonly VisualElement _pause;
        private readonly Label _autoplay;

        public KeeperHud(VisualElement root, Font? font)
        {
            if (root == null) { throw new ArgumentNullException(nameof(root)); }
            Root = root;
            root.Clear();
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;
            if (font != null)
            {
                root.style.unityFont = font;
                root.style.unityFontDefinition = FontDefinition.FromFont(font);
            }

            // タイトル
            _title = Panel("title", root);
            _title.style.left = 560;
            _title.style.right = 560;
            _title.style.top = 180;
            _title.style.alignItems = Align.Center;
            var big = Text("title-main", 64, Accent);
            big.text = "THORNWILD KEEPERS";
            big.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.Add(big);
            var sub = Text("title-sub", 28, Light);
            sub.text = "茨野の守り手 ― つかまえて、育てて、3 人に勝て";
            sub.style.marginBottom = 28;
            _title.Add(sub);
            for (int i = 0; i < _titleRows.Length; i++)
            {
                _titleRows[i] = new Row("title-row-" + i, 34, false);
                _title.Add(_titleRows[i].Root);
            }

            _titleNotice = Text("title-notice", 22, Danger);
            _titleNotice.style.marginTop = 16;
            _titleNotice.style.whiteSpace = WhiteSpace.Normal;
            _title.Add(_titleNotice);
            _titleRecords = Text("title-records", 22, Muted);
            _titleRecords.style.marginTop = 8;
            _title.Add(_titleRecords);

            // あそびかた
            _howTo = Panel("howto", root);
            _howTo.style.left = 300;
            _howTo.style.right = 300;
            _howTo.style.top = 120;
            var howTitle = Text("howto-title", 40, Accent);
            howTitle.text = "あそびかた";
            _howTo.Add(howTitle);
            var how = Text("howto-body", 26, Light);
            how.style.whiteSpace = WhiteSpace.Normal;
            how.text =
                "矢印キー / WASD：歩く・えらぶ　　Z / Enter / Space：決定・話す・文を送る　　X / Esc / Backspace：もどる・メニュー　　P：一時停止\n\n" +
                "・最初に 3 匹から相棒を選ぶ。草むらを歩くと野生のモンスターが出る。\n" +
                "・弱らせたり眠らせたりして「どうぐ → 捕獲玉」で捕まえる。手持ちは 6 匹まで、それ以上はボックスへ。\n" +
                "・属性は 灯 → 苔 → 潮 → 灯 の三すくみ。得意な属性の技は 2 倍、苦手は半分。\n" +
                "・状態異常は 毒（ターンの終わりに削れる）と 眠り（動けない）。決まったターンで治る。\n" +
                "・村の回復所のカウンターで全快・道具の補充・記録（セーブ）・ボックス。\n" +
                "・道をふさぐトレーナー 3 人に勝てばクリア。負けても回復所からやり直せる。\n\n" +
                "決定キーでタイトルへもどる";
            _howTo.Add(how);

            // フィールド
            _fieldTop = new VisualElement { name = "field-top" };
            _fieldTop.style.position = Position.Absolute;
            _fieldTop.style.left = 24;
            _fieldTop.style.right = 24;
            _fieldTop.style.top = 20;
            _fieldTop.style.flexDirection = FlexDirection.Row;
            _fieldTop.style.justifyContent = Justify.SpaceBetween;
            root.Add(_fieldTop);
            var left = Window("field-left");
            _fieldPlace = Text("field-place", 26, Accent);
            _fieldParty = Text("field-party", 22, Light);
            left.Add(_fieldPlace);
            left.Add(_fieldParty);
            _fieldTop.Add(left);
            var right = Window("field-right");
            _fieldItems = Text("field-items", 22, Light);
            right.Add(_fieldItems);
            _fieldTop.Add(right);
            var guideBox = Panel("guide-box", root);
            guideBox.style.left = 24;
            guideBox.style.bottom = 16;
            guideBox.style.paddingTop = 6;
            guideBox.style.paddingBottom = 6;
            _guide = Text("guide", 22, Light);
            guideBox.Add(_guide);
            _guideBox = guideBox;

            // 会話
            _dialog = Panel("dialog", root);
            _dialog.style.left = 220;
            _dialog.style.right = 220;
            _dialog.style.bottom = 40;
            _dialog.style.minHeight = 150;
            _dialogText = Text("dialog-text", 30, Light);
            _dialogText.style.whiteSpace = WhiteSpace.Normal;
            _dialog.Add(_dialogText);

            // メニュー
            _menu = Panel("menu", root);
            _menu.style.left = 520;
            _menu.style.right = 520;
            _menu.style.top = 120;
            _menuHeading = Text("menu-heading", 26, Accent);
            _menuHeading.style.whiteSpace = WhiteSpace.Normal;
            _menuHeading.style.marginBottom = 10;
            _menu.Add(_menuHeading);
            for (int i = 0; i < _menuRows.Length; i++)
            {
                _menuRows[i] = new Row("menu-row-" + i, 26, true);
                _menu.Add(_menuRows[i].Root);
            }

            _menuNotice = Text("menu-notice", 22, Danger);
            _menu.Add(_menuNotice);

            // 戦闘
            _foeBox = Panel("foe-box", root);
            _foeBox.style.left = 80;
            _foeBox.style.top = 70;
            _foeBox.style.width = 560;
            _foeName = Text("foe-name", 28, Light);
            _foeBox.Add(_foeName);
            _foeBar = HpBar(_foeBox, "foe-bar");
            _foeStatus = Text("foe-status", 22, Accent);
            _foeBox.Add(_foeStatus);

            _playerBox = Panel("player-box", root);
            _playerBox.style.right = 80;
            _playerBox.style.bottom = 390;
            _playerBox.style.width = 560;
            _playerName = Text("player-name", 28, Light);
            _playerBox.Add(_playerName);
            _playerBar = HpBar(_playerBox, "player-bar");
            _playerHp = Text("player-hp", 24, Light);
            _playerBox.Add(_playerHp);
            _playerExp = ExpBar(_playerBox, "player-exp");
            _playerStatus = Text("player-status", 22, Accent);
            _playerBox.Add(_playerStatus);

            _battleMessage = Panel("battle-message", root);
            _battleMessage.style.left = 40;
            _battleMessage.style.right = 40;
            _battleMessage.style.bottom = 30;
            _battleMessage.style.height = 200;
            _battleText = Text("battle-text", 32, Light);
            _battleText.style.whiteSpace = WhiteSpace.Normal;
            _battleMessage.Add(_battleText);

            _battleMenu = Panel("battle-menu", root);
            _battleMenu.style.right = 60;
            _battleMenu.style.bottom = 50;
            _battleMenu.style.width = 720;
            _battleHeading = Text("battle-heading", 24, Accent);
            _battleMenu.Add(_battleHeading);
            for (int i = 0; i < _battleRows.Length; i++)
            {
                _battleRows[i] = new Row("battle-row-" + i, 26, true);
                _battleMenu.Add(_battleRows[i].Root);
            }

            _battleNotice = Text("battle-notice", 22, Danger);
            _battleMenu.Add(_battleNotice);

            // 結果
            _result = Panel("result", root);
            _result.style.left = 520;
            _result.style.right = 520;
            _result.style.top = 200;
            var resultTitle = Text("result-title", 44, Accent);
            resultTitle.text = "茨守りに なった！";
            _result.Add(resultTitle);
            _resultBody = Text("result-body", 28, Light);
            _resultBody.style.whiteSpace = WhiteSpace.Normal;
            _result.Add(_resultBody);

            // 一時停止
            _pause = Panel("pause", root);
            _pause.style.left = 700;
            _pause.style.right = 700;
            _pause.style.top = 420;
            _pause.style.alignItems = Align.Center;
            var pauseText = Text("pause-text", 34, Accent);
            pauseText.text = "一時停止中";
            _pause.Add(pauseText);
            var pauseHint = Text("pause-hint", 22, Light);
            pauseHint.text = "P か 決定キーで つづける";
            _pause.Add(pauseHint);

            // 眺める自動プレイの表示（タイトルでは切り替えの案内）。
            _autoplay = Text("autoplay", 22, Accent);
            _autoplay.style.position = Position.Absolute;
            _autoplay.style.right = 28;
            _autoplay.style.bottom = 18;
            _autoplay.style.backgroundColor = WindowBg;
            _autoplay.style.paddingLeft = 12;
            _autoplay.style.paddingRight = 12;
            _autoplay.style.paddingTop = 4;
            _autoplay.style.paddingBottom = 4;
            root.Add(_autoplay);
        }

        public VisualElement Root { get; }

        /// <summary>今の Flow の状態を描く。</summary>
        public void Render(KeeperFlow flow, MonsterData data, float autoplaySpeed = 0f)
        {
            _autoplay.text = autoplaySpeed > 0f ? $"自動プレイ中 ×{autoplaySpeed:0.#}（F9 で止める）" : "F9：自動プレイを眺める";
            Show(_autoplay, autoplaySpeed > 0f || flow.Screen == FlowScreen.Title);
            var screen = flow.Screen;
            var world = flow.World;
            Show(_title, screen == FlowScreen.Title);
            Show(_howTo, screen == FlowScreen.HowTo);
            Show(_result, screen == FlowScreen.Result);
            Show(_pause, flow.IsPaused);
            var inField = world != null && (screen == FlowScreen.Field || screen == FlowScreen.FieldMenu || screen == FlowScreen.Party || screen == FlowScreen.Dex ||
                screen == FlowScreen.CenterMenu || screen == FlowScreen.Storage || screen == FlowScreen.LearnMove || screen == FlowScreen.StarterPick);
            Show(_fieldTop, inField && screen != FlowScreen.StarterPick);
            Show(_guideBox, screen == FlowScreen.Field && !flow.InDialogue);
            var battle = screen == FlowScreen.Battle && flow.Presenter != null;
            Show(_foeBox, battle && flow.Presenter!.Foe.Visible);
            Show(_playerBox, battle && flow.Presenter!.Player.Visible);
            Show(_battleMessage, battle);
            Show(_battleMenu, battle && flow.BattleMenu != BattleMenu.Playing);
            var menuScreen = screen == FlowScreen.FieldMenu || screen == FlowScreen.Party || screen == FlowScreen.Dex || screen == FlowScreen.CenterMenu ||
                screen == FlowScreen.Storage || screen == FlowScreen.LearnMove || screen == FlowScreen.StarterPick;
            Show(_menu, menuScreen && !flow.InDialogue);
            Show(_dialog, flow.InDialogue && screen != FlowScreen.Battle);

            switch (screen)
            {
                case FlowScreen.Title:
                    RenderRows(_titleRows, flow, false);
                    _titleNotice.text = flow.TitleNotice;
                    _titleRecords.text = flow.Records.Clears == 0
                        ? "記録：まだ クリアしていない"
                        : $"記録：クリア {flow.Records.Clears} 回　最短 {flow.FormatTime(flow.Records.BestTicks)}　最多 {flow.Records.BestCaught} 種";
                    break;
                case FlowScreen.Result:
                    var w = world!;
                    _resultBody.text =
                        $"遊んだ時間　{flow.FormatTime(flow.ResultTicks)}\n捕まえた種類　{w.CaughtCount} / {data.Species.Count}\n戦闘　{w.BattleCount} 回（負け {w.Losses} 回）\n手持ち　{PartyLine(w)}\n\n" +
                        $"最短記録 {flow.FormatTime(flow.Records.BestTicks)}　最多 {flow.Records.BestCaught} 種　クリア {flow.Records.Clears} 回\n" +
                        (flow.ResultNotice.Length > 0 ? flow.ResultNotice + "\n" : string.Empty) + "\n決定キーで タイトルへ";
                    break;
            }

            if (inField && world != null)
            {
                _fieldPlace.text = data.Map(world.Explorer.MapId).Name + (world.AtCenter ? "　回復所" : string.Empty);
                var lead = world.Roster.Party.Count > 0 ? world.Roster.Party[0] : null;
                _fieldParty.text = lead == null ? string.Empty : $"先頭 {lead.Name} Lv{lead.Level}　HP {lead.Hp}/{lead.MaxHp}";
                var defeated = 0;
                foreach (var t in data.Trainers)
                {
                    if (world.IsDefeated(t)) { defeated++; }
                }

                _fieldItems.text = $"てもち {world.Roster.Party.Count}/{world.Roster.PartyMax}　ボックス {world.Roster.Box.Count}　ずかん {world.CaughtCount}/{data.Species.Count}\n捕獲玉 {world.Balls}　傷薬 {world.Potions}　トレーナー {defeated}/{data.Trainers.Count}　{flow.FormatTime(world.PlayTicks)}";
                _guide.text = "矢印：歩く　Z：話す・調べる　X：メニュー　P：一時停止";
            }

            if (menuScreen)
            {
                _menuHeading.text = flow.Heading;
                RenderRows(_menuRows, flow, true);
                _menuNotice.text = flow.Notice;
            }

            if (flow.InDialogue) { _dialogText.text = flow.Dialogue; }

            if (battle)
            {
                var p = flow.Presenter!;
                _foeName.text = $"{(p.Kind == BattleKind.Wild ? "野生の " : string.Empty)}{p.Foe.Name}　Lv{p.Foe.Level}";
                SetBar(_foeBar, p.Foe.ShownHp, p.Foe.MaxHp);
                _foeStatus.text = p.Foe.Status;
                _playerName.text = $"{p.Player.Name}　Lv{p.Player.Level}";
                SetBar(_playerBar, p.Player.ShownHp, p.Player.MaxHp);
                _playerHp.text = $"HP {p.Player.ShownHp} / {p.Player.MaxHp}";
                _playerExp.style.width = Length.Percent(p.Player.ExpPermille / 10f);
                _playerStatus.text = p.Player.Status;
                _battleText.text = flow.InDialogue ? flow.Dialogue : p.Text;
                if (flow.BattleMenu != BattleMenu.Playing)
                {
                    _battleHeading.text = flow.Heading;
                    RenderRows(_battleRows, flow, true);
                    _battleNotice.text = flow.Notice;
                }
            }
        }

        private static string PartyLine(World w)
        {
            var parts = new List<string>();
            foreach (var m in w.Roster.Party) { parts.Add($"{m.Name} Lv{m.Level}"); }
            return string.Join("、", parts);
        }

        /// <summary>項目を描く。行より多ければカーソルが見える範囲を出す。</summary>
        private static void RenderRows(Row[] rows, KeeperFlow flow, bool detail)
        {
            var items = flow.Items;
            var first = Math.Max(0, Math.Min(flow.Cursor - rows.Length / 2, items.Count - rows.Length));
            for (int r = 0; r < rows.Length; r++)
            {
                var i = first + r;
                var row = rows[r];
                if (i >= items.Count)
                {
                    row.Root.style.display = DisplayStyle.None;
                    continue;
                }

                row.Root.style.display = DisplayStyle.Flex;
                var item = items[i];
                var selected = i == flow.Cursor;
                var marked = flow.Screen == FlowScreen.Party && i == flow.SwapFrom;
                row.Cursor.text = selected ? "▶" : marked ? "◆" : string.Empty;
                row.Label.text = item.Label;
                row.Label.style.color = item.Enabled ? (selected ? Accent : Light) : Muted;
                if (detail) { row.Detail.text = item.Detail; }
            }
        }

        private static void SetBar(VisualElement bar, int hp, int max)
        {
            var ratio = max <= 0 ? 0f : Mathf.Clamp01(hp / (float)max);
            bar.style.width = Length.Percent(ratio * 100f);
            bar.style.backgroundColor = ratio > 0.5f ? new Color(0.36f, 0.86f, 0.44f) : ratio > 0.2f ? new Color(0.96f, 0.80f, 0.26f) : new Color(0.94f, 0.32f, 0.30f);
        }

        private static void Show(VisualElement e, bool visible) => e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>次のレベルまでの経験値の細いメーター（青）。</summary>
        private static VisualElement ExpBar(VisualElement parent, string name)
        {
            var row = new VisualElement { name = name + "-row" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var label = Text(name + "-label", 18, Muted);
            label.text = "EXP";
            label.style.width = 48;
            row.Add(label);
            var back = new VisualElement { name = name + "-back" };
            back.style.flexGrow = 1;
            back.style.height = 8;
            back.style.backgroundColor = new Color(0.16f, 0.18f, 0.16f, 1f);
            var fill = new VisualElement { name = name };
            fill.style.height = 8;
            fill.style.backgroundColor = new Color(0.36f, 0.62f, 0.98f, 1f);
            back.Add(fill);
            row.Add(back);
            parent.Add(row);
            return fill;
        }

        private static VisualElement HpBar(VisualElement parent, string name)
        {
            var back = new VisualElement { name = name + "-back" };
            back.style.height = 16;
            back.style.marginTop = 6;
            back.style.marginBottom = 6;
            back.style.backgroundColor = new Color(0.16f, 0.18f, 0.16f, 1f);
            var fill = new VisualElement { name = name };
            fill.style.height = 16;
            back.Add(fill);
            parent.Add(back);
            return fill;
        }

        private static VisualElement Panel(string name, VisualElement root)
        {
            var p = Window(name);
            p.style.position = Position.Absolute;
            root.Add(p);
            return p;
        }

        private static VisualElement Window(string name)
        {
            var w = new VisualElement { name = name };
            w.style.backgroundColor = WindowBg;
            w.style.borderTopWidth = 3;
            w.style.borderBottomWidth = 3;
            w.style.borderLeftWidth = 3;
            w.style.borderRightWidth = 3;
            w.style.borderTopColor = Border;
            w.style.borderBottomColor = Border;
            w.style.borderLeftColor = Border;
            w.style.borderRightColor = Border;
            w.style.borderTopLeftRadius = 10;
            w.style.borderTopRightRadius = 10;
            w.style.borderBottomLeftRadius = 10;
            w.style.borderBottomRightRadius = 10;
            w.style.paddingLeft = 22;
            w.style.paddingRight = 22;
            w.style.paddingTop = 14;
            w.style.paddingBottom = 14;
            return w;
        }

        private static Label Text(string name, int size, Color color)
        {
            var l = new Label { name = name };
            l.style.fontSize = size;
            l.style.color = color;
            return l;
        }

        private sealed class Row
        {
            public Row(string name, int size, bool detail)
            {
                Root = new VisualElement { name = name };
                Root.style.flexDirection = FlexDirection.Row;
                Root.style.alignItems = Align.Center;
                Root.style.minHeight = size + 10;
                Cursor = Text(name + "-cursor", size, Accent);
                Cursor.style.width = 36;
                Root.Add(Cursor);
                Label = Text(name + "-label", size, Light);
                Label.style.flexGrow = 1;
                Root.Add(Label);
                Detail = Text(name + "-detail", size - 4, Muted);
                Detail.style.display = detail ? DisplayStyle.Flex : DisplayStyle.None;
                Root.Add(Detail);
            }

            public VisualElement Root { get; }

            public Label Cursor { get; }

            public Label Label { get; }

            public Label Detail { get; }
        }
    }
}
