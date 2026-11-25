#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.App;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// UI Toolkit (<see cref="PanelRenderer"/>) 上に JRPG ウィンドウ群を構築・更新する描画クラス。
    /// 選択カーソル列は固定幅で分離し、カーソル移動で項目ラベルの座標が揺れないようにする。
    /// </summary>
    public sealed class EmberlockHudView
    {
        private const int MaxMenuRows = 12;

        private static readonly Color WindowBg = new(0.07f, 0.10f, 0.18f, 0.94f);
        private static readonly Color BorderGold = new(0.86f, 0.72f, 0.38f, 1f);
        private static readonly Color TextLight = new(0.95f, 0.94f, 0.90f, 1f);
        private static readonly Color TextMuted = new(0.52f, 0.55f, 0.62f, 1f);
        private static readonly Color TextAccent = new(1.00f, 0.86f, 0.42f, 1f);
        private static readonly Color TextDanger = new(1.00f, 0.46f, 0.44f, 1f);
        private static readonly Color TextHeal = new(0.48f, 0.96f, 0.66f, 1f);

        private readonly VisualElement _topBar;
        private readonly Label[] _partyCards = new Label[3];
        private readonly Label _locationBadge;

        // タイトル画面
        private readonly VisualElement _titleScreen;
        private readonly Label _corruptNoticeBox;
        private readonly MenuRow[] _titleRows = new MenuRow[4];

        // 遊び方・エンディング・リザルト用フルパネル
        private readonly VisualElement _storyModal;
        private readonly Label _storyTitle;
        private readonly Label _storyBody;
        private readonly Label _storyFooter;

        // フィールド下部ガイド
        private readonly VisualElement _fieldFooter;
        private readonly Label _fieldBannerLabel;
        private readonly Label _fieldGuideLabel;

        // 会話ウィンドウ
        private readonly VisualElement _dialogueBox;
        private readonly Label _dialogueSpeaker;
        private readonly Label _dialogueBody;

        // キャンプ・施設共通 2 ペインウィンドウ
        private readonly VisualElement _menuContainer;
        private readonly Label _menuHeader;
        private readonly MenuRow[] _leftRows = new MenuRow[8];
        private readonly VisualElement _rightPane;
        private readonly Label _rightSummary;
        private readonly MenuRow[] _rightRows = new MenuRow[MaxMenuRows];
        private readonly Label _menuFeedbackLabel;

        // 戦闘 UI
        private readonly VisualElement _battleLayer;
        private readonly VisualElement _enemyRosterBox;
        private readonly Label _enemyRosterLabel;
        private readonly Label _battlePopupLabel;
        private readonly VisualElement _battleCommandBox;
        private readonly Label _battleCommandTitle;
        private readonly MenuRow[] _battleCmdRows = new MenuRow[6];
        private readonly VisualElement _battleSubBox;
        private readonly MenuRow[] _battleSubRows = new MenuRow[8];
        private readonly VisualElement _battleMsgBox;
        private readonly Label _battleMsgLabel;
        private readonly Label _battleFeedbackLabel;

        // 一時停止モーダル
        private readonly VisualElement _pauseModal;
        private readonly MenuRow[] _pauseRows = new MenuRow[2];

        public EmberlockHudView(VisualElement root, Font? font)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            root.Clear();
            if (font != null)
            {
                root.style.unityFont = font;
                root.style.unityFontDefinition = FontDefinition.FromFont(font);
            }

            // 1. 上部ステータスバー
            _topBar = MakeWindow("top-bar");
            _topBar.style.position = Position.Absolute;
            _topBar.style.left = 28;
            _topBar.style.right = 28;
            _topBar.style.top = 18;
            _topBar.style.flexDirection = FlexDirection.Row;
            _topBar.style.justifyContent = Justify.SpaceBetween;
            _topBar.style.alignItems = Align.Center;
            _topBar.style.paddingTop = 10;
            _topBar.style.paddingBottom = 10;

            var partyRow = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            for (var i = 0; i < _partyCards.Length; i++)
            {
                var card = MakeLabel($"party-card-{i}", 21, TextLight);
                card.style.minWidth = 390;
                card.style.marginRight = 18;
                _partyCards[i] = card;
                partyRow.Add(card);
            }

            _topBar.Add(partyRow);
            _locationBadge = MakeLabel("location-badge", 22, TextAccent);
            _locationBadge.style.unityTextAlign = TextAnchor.MiddleRight;
            _topBar.Add(_locationBadge);
            root.Add(_topBar);

            // 2. タイトル画面
            _titleScreen = new VisualElement
            {
                name = "title-screen",
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    right = 0,
                    top = 0,
                    bottom = 0,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    backgroundColor = new Color(0.03f, 0.04f, 0.08f, 0.62f),
                },
            };

            var crestBox = MakeWindow("title-crest");
            crestBox.style.width = 920;
            crestBox.style.alignItems = Align.Center;
            crestBox.style.paddingTop = 28;
            crestBox.style.paddingBottom = 28;
            crestBox.style.marginBottom = 24;
            var mainTitle = MakeLabel("title-logo", 58, TextAccent);
            mainTitle.text = "EMBERLOCK HOLLOW";
            mainTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            mainTitle.style.marginBottom = 8;
            crestBox.Add(mainTitle);
            var subTitle = MakeLabel("title-sub", 24, TextLight);
            subTitle.text = "― 焔鎖の坑道と灯守りの町 ―";
            crestBox.Add(subTitle);
            _titleScreen.Add(crestBox);

            _corruptNoticeBox = MakeLabel("corrupt-save-notice", 21, TextDanger);
            _corruptNoticeBox.style.backgroundColor = new Color(0.24f, 0.06f, 0.08f, 0.95f);
            _corruptNoticeBox.style.borderTopWidth = 2;
            _corruptNoticeBox.style.borderBottomWidth = 2;
            _corruptNoticeBox.style.borderLeftWidth = 2;
            _corruptNoticeBox.style.borderRightWidth = 2;
            SetBorderColor(_corruptNoticeBox, TextDanger);
            _corruptNoticeBox.style.paddingLeft = 20;
            _corruptNoticeBox.style.paddingRight = 20;
            _corruptNoticeBox.style.paddingTop = 10;
            _corruptNoticeBox.style.paddingBottom = 10;
            _corruptNoticeBox.style.marginBottom = 18;
            _titleScreen.Add(_corruptNoticeBox);

            var titleMenuBox = MakeWindow("title-menu");
            titleMenuBox.style.width = 460;
            titleMenuBox.style.paddingTop = 18;
            titleMenuBox.style.paddingBottom = 18;
            for (var i = 0; i < _titleRows.Length; i++)
            {
                _titleRows[i] = new MenuRow($"title-row-{i}", 28, showDetail: false);
                titleMenuBox.Add(_titleRows[i].Root);
            }

            _titleScreen.Add(titleMenuBox);

            var titleKeyGuide = MakeLabel("title-guide", 20, TextLight);
            titleKeyGuide.text = "WASD / 矢印: 移動・選択    Enter / Space / Z: 決定    Esc / Backspace / X: キャンセル    Tab / C: キャンプ";
            titleKeyGuide.style.marginTop = 28;
            _titleScreen.Add(titleKeyGuide);
            root.Add(_titleScreen);

            // 3. フィールド下部バー
            _fieldFooter = MakeWindow("field-footer");
            _fieldFooter.style.position = Position.Absolute;
            _fieldFooter.style.left = 28;
            _fieldFooter.style.right = 28;
            _fieldFooter.style.bottom = 18;
            _fieldFooter.style.flexDirection = FlexDirection.Row;
            _fieldFooter.style.justifyContent = Justify.SpaceBetween;
            _fieldBannerLabel = MakeLabel("field-banner", 22, TextLight);
            _fieldGuideLabel = MakeLabel("field-guide", 20, TextAccent);
            _fieldGuideLabel.text = "[Enter/Z] 調べる・話す   [Tab/C/X] キャンプ   [P] 一時停止";
            _fieldFooter.Add(_fieldBannerLabel);
            _fieldFooter.Add(_fieldGuideLabel);
            root.Add(_fieldFooter);

            // 4. 会話ボックス
            _dialogueBox = MakeWindow("dialogue-box");
            _dialogueBox.style.position = Position.Absolute;
            _dialogueBox.style.left = 140;
            _dialogueBox.style.right = 140;
            _dialogueBox.style.bottom = 36;
            _dialogueBox.style.minHeight = 170;
            _dialogueSpeaker = MakeLabel("dialogue-speaker", 24, TextAccent);
            _dialogueSpeaker.style.unityFontStyleAndWeight = FontStyle.Bold;
            _dialogueSpeaker.style.marginBottom = 8;
            _dialogueBody = MakeLabel("dialogue-body", 25, TextLight);
            _dialogueBody.style.whiteSpace = WhiteSpace.Normal;
            _dialogueBox.Add(_dialogueSpeaker);
            _dialogueBox.Add(_dialogueBody);
            root.Add(_dialogueBox);

            // 5. キャンプ・店・宿屋・教会共通 2 ペインウィンドウ
            _menuContainer = MakeWindow("menu-container");
            _menuContainer.style.position = Position.Absolute;
            _menuContainer.style.left = 80;
            _menuContainer.style.right = 80;
            _menuContainer.style.top = 112;
            _menuContainer.style.bottom = 36;
            _menuHeader = MakeLabel("menu-header", 26, TextAccent);
            _menuHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            _menuHeader.style.marginBottom = 12;
            _menuContainer.Add(_menuHeader);

            var panes = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            var leftPane = MakeInnerPanel(340);
            leftPane.style.marginRight = 18;
            for (var i = 0; i < _leftRows.Length; i++)
            {
                _leftRows[i] = new MenuRow($"left-row-{i}", 24, showDetail: false);
                leftPane.Add(_leftRows[i].Root);
            }

            _rightPane = MakeInnerPanel(0);
            _rightPane.style.flexGrow = 1;
            _rightSummary = MakeLabel("right-summary", 22, TextLight);
            _rightSummary.style.whiteSpace = WhiteSpace.Normal;
            _rightSummary.style.marginBottom = 10;
            _rightPane.Add(_rightSummary);
            for (var i = 0; i < _rightRows.Length; i++)
            {
                _rightRows[i] = new MenuRow($"right-row-{i}", 22, showDetail: true);
                _rightPane.Add(_rightRows[i].Root);
            }

            panes.Add(leftPane);
            panes.Add(_rightPane);
            _menuContainer.Add(panes);

            _menuFeedbackLabel = MakeLabel("menu-feedback", 22, TextAccent);
            _menuFeedbackLabel.style.marginTop = 10;
            _menuContainer.Add(_menuFeedbackLabel);
            root.Add(_menuContainer);

            // 6. 戦闘 UI
            _battleLayer = new VisualElement
            {
                name = "battle-layer",
                style = { position = Position.Absolute, left = 0, right = 0, top = 0, bottom = 0 },
            };

            _enemyRosterBox = MakeWindow("enemy-roster");
            _enemyRosterBox.style.position = Position.Absolute;
            _enemyRosterBox.style.left = 28;
            _enemyRosterBox.style.top = 108;
            _enemyRosterBox.style.minWidth = 380;
            _enemyRosterLabel = MakeLabel("enemy-roster-label", 22, TextLight);
            _enemyRosterLabel.style.whiteSpace = WhiteSpace.Normal;
            _enemyRosterBox.Add(_enemyRosterLabel);
            _battleLayer.Add(_enemyRosterBox);

            _battlePopupLabel = MakeLabel("battle-popup", 38, TextAccent);
            _battlePopupLabel.style.position = Position.Absolute;
            _battlePopupLabel.style.left = 0;
            _battlePopupLabel.style.right = 0;
            _battlePopupLabel.style.top = 200;
            _battlePopupLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _battlePopupLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _battleLayer.Add(_battlePopupLabel);

            _battleCommandBox = MakeWindow("battle-command-box");
            _battleCommandBox.style.position = Position.Absolute;
            _battleCommandBox.style.left = 28;
            _battleCommandBox.style.bottom = 24;
            _battleCommandBox.style.width = 330;
            _battleCommandBox.style.minHeight = 270;
            _battleCommandTitle = MakeLabel("battle-cmd-title", 23, TextAccent);
            _battleCommandTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _battleCommandTitle.style.marginBottom = 8;
            _battleCommandBox.Add(_battleCommandTitle);
            for (var i = 0; i < _battleCmdRows.Length; i++)
            {
                _battleCmdRows[i] = new MenuRow($"battle-cmd-{i}", 24, showDetail: false);
                _battleCommandBox.Add(_battleCmdRows[i].Root);
            }

            _battleLayer.Add(_battleCommandBox);

            _battleSubBox = MakeWindow("battle-sub-box");
            _battleSubBox.style.position = Position.Absolute;
            _battleSubBox.style.left = 374;
            _battleSubBox.style.right = 28;
            _battleSubBox.style.bottom = 148;
            _battleSubBox.style.minHeight = 190;
            for (var i = 0; i < _battleSubRows.Length; i++)
            {
                _battleSubRows[i] = new MenuRow($"battle-sub-{i}", 22, showDetail: true);
                _battleSubBox.Add(_battleSubRows[i].Root);
            }

            _battleLayer.Add(_battleSubBox);

            _battleMsgBox = MakeWindow("battle-msg-box");
            _battleMsgBox.style.position = Position.Absolute;
            _battleMsgBox.style.left = 374;
            _battleMsgBox.style.right = 28;
            _battleMsgBox.style.bottom = 24;
            _battleMsgBox.style.minHeight = 112;
            _battleMsgLabel = MakeLabel("battle-msg", 25, TextLight);
            _battleMsgLabel.style.whiteSpace = WhiteSpace.Normal;
            _battleFeedbackLabel = MakeLabel("battle-feedback", 22, TextDanger);
            _battleFeedbackLabel.style.marginTop = 6;
            _battleMsgBox.Add(_battleMsgLabel);
            _battleMsgBox.Add(_battleFeedbackLabel);
            _battleLayer.Add(_battleMsgBox);
            root.Add(_battleLayer);

            // 7. 遊び方・エンディング・リザルト用フルモーダル
            _storyModal = MakeWindow("story-modal");
            _storyModal.style.position = Position.Absolute;
            _storyModal.style.left = 140;
            _storyModal.style.right = 140;
            _storyModal.style.top = 90;
            _storyModal.style.bottom = 90;
            _storyModal.style.justifyContent = Justify.SpaceBetween;
            _storyModal.style.paddingLeft = 36;
            _storyModal.style.paddingRight = 36;
            _storyModal.style.paddingTop = 30;
            _storyModal.style.paddingBottom = 30;
            _storyTitle = MakeLabel("story-title", 38, TextAccent);
            _storyTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _storyBody = MakeLabel("story-body", 24, TextLight);
            _storyBody.style.whiteSpace = WhiteSpace.Normal;
            _storyFooter = MakeLabel("story-footer", 22, TextAccent);
            _storyFooter.style.unityTextAlign = TextAnchor.MiddleRight;
            _storyModal.Add(_storyTitle);
            _storyModal.Add(_storyBody);
            _storyModal.Add(_storyFooter);
            root.Add(_storyModal);

            // 8. 一時停止モーダル
            _pauseModal = MakeWindow("pause-modal");
            _pauseModal.style.position = Position.Absolute;
            _pauseModal.style.left = 680;
            _pauseModal.style.right = 680;
            _pauseModal.style.top = 380;
            _pauseModal.style.paddingTop = 24;
            _pauseModal.style.paddingBottom = 24;
            var pauseTitle = MakeLabel("pause-title", 30, TextAccent);
            pauseTitle.text = "PAUSED ― 一時停止";
            pauseTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            pauseTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            pauseTitle.style.marginBottom = 16;
            _pauseModal.Add(pauseTitle);
            for (var i = 0; i < _pauseRows.Length; i++)
            {
                _pauseRows[i] = new MenuRow($"pause-row-{i}", 26, showDetail: false);
                _pauseModal.Add(_pauseRows[i].Root);
            }

            root.Add(_pauseModal);
        }

        public void Render(QuestFlow flow)
        {
            if (flow == null)
            {
                return;
            }

            var screen = flow.Screen;
            var world = flow.World;

            var showTopBar = world != null &&
                             screen != QuestScreen.Title &&
                             screen != QuestScreen.HowTo &&
                             screen != QuestScreen.Ending &&
                             screen != QuestScreen.Result;
            SetVisible(_topBar, showTopBar);
            if (showTopBar && world != null)
            {
                RenderTopBar(flow, world);
            }

            SetVisible(_titleScreen, screen == QuestScreen.Title);
            if (screen == QuestScreen.Title)
            {
                RenderTitle(flow);
            }

            SetVisible(_fieldFooter, screen == QuestScreen.Field);
            if (screen == QuestScreen.Field)
            {
                _fieldBannerLabel.text = flow.FieldBanner;
            }

            SetVisible(_dialogueBox, screen == QuestScreen.Dialogue);
            if (screen == QuestScreen.Dialogue)
            {
                SetVisible(_dialogueSpeaker, flow.DialogueSpeaker.Length > 0);
                _dialogueSpeaker.text = flow.DialogueSpeaker;
                _dialogueBody.text = flow.DialogueMessage + "\n\n[Enter / Z] 次へ";
            }

            var isMenuScreen = screen == QuestScreen.CampMenu ||
                               screen == QuestScreen.Shop ||
                               screen == QuestScreen.Inn ||
                               screen == QuestScreen.Church;
            SetVisible(_menuContainer, isMenuScreen);
            if (isMenuScreen && world != null)
            {
                RenderFacilityOrCamp(flow, world);
            }

            SetVisible(_battleLayer, screen == QuestScreen.Battle);
            if (screen == QuestScreen.Battle && flow.ActiveBattle != null)
            {
                RenderBattle(flow.ActiveBattle);
            }

            var isStoryScreen = screen == QuestScreen.HowTo || screen == QuestScreen.Ending || screen == QuestScreen.Result;
            SetVisible(_storyModal, isStoryScreen);
            if (isStoryScreen)
            {
                RenderStoryModal(flow);
            }

            SetVisible(_pauseModal, flow.IsPaused);
            if (flow.IsPaused)
            {
                var labels = QuestFlow.PauseMenuLabels;
                for (var i = 0; i < _pauseRows.Length; i++)
                {
                    _pauseRows[i].Bind(i < labels.Count, i == flow.PauseCursor, i < labels.Count ? labels[i] : string.Empty, string.Empty, enabled: true);
                }
            }
        }

        private void RenderTopBar(QuestFlow flow, World world)
        {
            if (flow.Screen == QuestScreen.Battle && flow.ActiveBattle != null)
            {
                var snap = flow.ActiveBattle.DisplaySnapshot;
                for (var i = 0; i < _partyCards.Length && i < snap.Allies.Count; i++)
                {
                    var a = snap.Allies[i];
                    var tag = !a.IsAlive ? " [戦闘不能]" : (a.IsDefending ? " [防]" : string.Empty);
                    _partyCards[i].text = $"{a.Name}({a.ClassName}) Lv{a.Level}{tag}  HP {a.Hp}/{a.MaxHp}  MP {a.Mp}/{a.MaxMp}";
                    _partyCards[i].style.color = !a.IsAlive ? TextDanger : TextLight;
                }
            }
            else
            {
                for (var i = 0; i < _partyCards.Length && i < world.Party.Count; i++)
                {
                    var m = world.Party[i];
                    var tag = !m.IsAlive ? " [戦闘不能]" : string.Empty;
                    _partyCards[i].text = $"{m.Name}({m.Class.Name}) Lv{m.Level}{tag}  HP {m.Hp}/{m.MaxHp}  MP {m.Mp}/{m.MaxMp}";
                    _partyCards[i].style.color = !m.IsAlive ? TextDanger : TextLight;
                }
            }

            _locationBadge.text = $"{world.CurrentMap.Name}   |   {world.Gold} G";
        }

        private void RenderTitle(QuestFlow flow)
        {
            SetVisible(_corruptNoticeBox, flow.CorruptSaveNotice.Length > 0);
            _corruptNoticeBox.text = flow.CorruptSaveNotice;

            var opts = flow.TitleOptions;
            for (var i = 0; i < _titleRows.Length; i++)
            {
                if (i < opts.Count)
                {
                    _titleRows[i].Bind(true, i == flow.TitleCursor, QuestFlow.TitleOptionLabel(opts[i]), string.Empty, enabled: true);
                }
                else
                {
                    _titleRows[i].Bind(false, false, string.Empty, string.Empty, enabled: false);
                }
            }
        }

        private void RenderFacilityOrCamp(QuestFlow flow, World world)
        {
            ClearRows(_leftRows);
            ClearRows(_rightRows);
            _rightSummary.text = string.Empty;
            _menuFeedbackLabel.text = flow.MenuFeedback;

            switch (flow.Screen)
            {
                case QuestScreen.CampMenu:
                    RenderCampMenu(flow, world);
                    break;
                case QuestScreen.Shop:
                    RenderShopMenu(flow, world);
                    break;
                case QuestScreen.Inn:
                    RenderInnMenu(flow, world);
                    break;
                case QuestScreen.Church:
                    RenderChurchMenu(flow, world);
                    break;
            }
        }

        private void RenderCampMenu(QuestFlow flow, World world)
        {
            _menuHeader.text = "キャンプメニュー";
            var labels = QuestFlow.CampMenuLabels;
            for (var i = 0; i < labels.Count && i < _leftRows.Length; i++)
            {
                _leftRows[i].Bind(true, flow.CampMode == CampSubMode.Root && flow.CampRootCursor == i, labels[i], string.Empty, enabled: true);
            }

            switch (flow.CampMode)
            {
                case CampSubMode.Root:
                case CampSubMode.Status:
                    _rightSummary.text = BuildPartyStatusSummary(world);
                    break;

                case CampSubMode.SpellCaster:
                    _rightSummary.text = "誰の呪文を使いますか？";
                    BindPartyTargetRows(world, flow.CampMemberCursor, requireDead: false, allowDeadSelection: false);
                    break;

                case CampSubMode.SpellSelect:
                    _rightSummary.text = $"{world.Party[flow.CampMemberCursor].Name}の習得呪文（MP {world.Party[flow.CampMemberCursor].Mp}/{world.Party[flow.CampMemberCursor].MaxMp}）";
                    BindSelectableOptions(flow.GetCampSpellOptions(), flow.CampSubCursor);
                    break;

                case CampSubMode.SpellTarget:
                    _rightSummary.text = "誰に使いますか？";
                    BindPartyTargetRows(world, flow.CampTargetCursor, requireDead: false, allowDeadSelection: true);
                    break;

                case CampSubMode.ItemSelect:
                    _rightSummary.text = "使う道具を選んでください";
                    BindSelectableOptions(flow.GetCampItemOptions(), flow.CampSubCursor);
                    break;

                case CampSubMode.ItemTarget:
                    _rightSummary.text = "誰に使いますか？";
                    BindPartyTargetRows(world, flow.CampTargetCursor, requireDead: false, allowDeadSelection: false);
                    break;

                case CampSubMode.EquipMember:
                    _rightSummary.text = "装備を変える仲間を選んでください";
                    BindPartyTargetRows(world, flow.CampMemberCursor, requireDead: false, allowDeadSelection: true);
                    break;

                case CampSubMode.EquipSlot:
                    {
                        var m = world.Party[flow.CampMemberCursor];
                        var wName = m.WeaponId.Length > 0 ? flow.Data.ProductName(m.WeaponId) : "なし";
                        var aName = m.ArmorId.Length > 0 ? flow.Data.ProductName(m.ArmorId) : "なし";
                        _rightSummary.text = $"{m.Name}   攻 {m.Atk} / 守 {m.Def} / 速 {m.Agi}";
                        _rightRows[0].Bind(true, flow.SelectedEquipSlot == EquipSlot.Weapon, $"武器: {wName}", "武器を変更", enabled: true);
                        _rightRows[1].Bind(true, flow.SelectedEquipSlot == EquipSlot.Armor, $"防具: {aName}", "防具を変更", enabled: true);
                        break;
                    }

                case CampSubMode.EquipCandidate:
                    _rightSummary.text = $"{world.Party[flow.CampMemberCursor].Name}の装備候補";
                    BindSelectableOptions(flow.GetEquipCandidateOptions(), flow.CampSubCursor);
                    break;
            }
        }

        private void RenderShopMenu(QuestFlow flow, World world)
        {
            var shopName = flow.ActiveShop != null ? flow.ActiveShop.Name : "よろず屋";
            _menuHeader.text = $"{shopName}   （所持金: {world.Gold} G）";
            var labels = QuestFlow.ShopMenuLabels;
            for (var i = 0; i < labels.Count && i < _leftRows.Length; i++)
            {
                _leftRows[i].Bind(true, flow.ShopMode == ShopSubMode.Root && flow.ShopRootCursor == i, labels[i], string.Empty, enabled: true);
            }

            switch (flow.ShopMode)
            {
                case ShopSubMode.Root:
                    _rightSummary.text = "売買する項目を選んでください。\n装備品を買うとその場で装備して旧装備を袋に戻せます。\n装備中の品もそのまま半額で売却できます。";
                    break;

                case ShopSubMode.BuySelect:
                    _rightSummary.text = "購入する品物を選んでください";
                    BindSelectableOptions(flow.GetShopBuyOptions(), flow.ShopSubCursor);
                    break;

                case ShopSubMode.BuyEquipTarget:
                    _rightSummary.text = "購入後の扱いを選んでください";
                    BindSelectableOptions(flow.GetShopEquipTargetOptions(), flow.ShopTargetCursor);
                    break;

                case ShopSubMode.SellSelect:
                    {
                        _rightSummary.text = "売却する品を選んでください（買値の半額）";
                        var sellList = flow.GetShopSellCandidates();
                        BindWindowedSellList(sellList, flow.ShopSubCursor);
                        break;
                    }
            }
        }

        private void RenderInnMenu(QuestFlow flow, World world)
        {
            var cost = flow.Data.Rules.InnCost;
            _menuHeader.text = $"灯守りの宿屋   （一晩 {cost} G / 所持金 {world.Gold} G）";
            var labels = QuestFlow.InnMenuLabels;
            for (var i = 0; i < labels.Count && i < _leftRows.Length; i++)
            {
                _leftRows[i].Bind(true, flow.InnCursor == i, labels[i], string.Empty, enabled: i != 0 || world.Gold >= cost);
            }

            _rightSummary.text = $"一晩 {cost} G で生存している仲間全員の HP と MP を全快します。\n（戦闘不能の仲間は教会で蘇生できます）";
        }

        private void RenderChurchMenu(QuestFlow flow, World world)
        {
            _menuHeader.text = $"灯の教会   （所持金 {world.Gold} G）";
            var labels = QuestFlow.ChurchMenuLabels;
            for (var i = 0; i < labels.Count && i < _leftRows.Length; i++)
            {
                _leftRows[i].Bind(true, flow.ChurchMode == ChurchSubMode.Root && flow.ChurchRootCursor == i, labels[i], string.Empty, enabled: true);
            }

            if (flow.ChurchMode == ChurchSubMode.Root)
            {
                _rightSummary.text = "セーブはこの教会でのみ行えます。\n倒れた仲間がいるときは『蘇生する』で息を吹き返せます。";
                return;
            }

            var fallen = flow.GetFallenMemberIndices();
            _rightSummary.text = "蘇生する仲間を選んでください";
            for (var i = 0; i < fallen.Count && i < _rightRows.Length; i++)
            {
                var m = world.Party[fallen[i]];
                var cost = flow.Data.Rules.ReviveCostPerLevel * m.Level;
                _rightRows[i].Bind(true, i == flow.ChurchTargetCursor, m.Name, $"Lv{m.Level}  寄付 {cost}G", world.Gold >= cost);
            }
        }

        private void RenderBattle(BattlePresenter presenter)
        {
            var snap = presenter.DisplaySnapshot;
            var sb = new StringBuilder(128);
            for (var i = 0; i < snap.Enemies.Count; i++)
            {
                var e = snap.Enemies[i];
                if (!e.IsAlive)
                {
                    continue;
                }

                var pointer = (presenter.Step == BattleInputStep.EnemyTarget && presenter.TargetCursor == i) ? "▶ " : "   ";
                sb.Append(pointer);
                sb.Append(e.DisplayName);
                sb.Append("   HP ");
                sb.Append(e.Hp.ToString(CultureInfo.InvariantCulture));
                sb.Append('/');
                sb.Append(e.MaxHp.ToString(CultureInfo.InvariantCulture));
                sb.Append('\n');
            }

            _enemyRosterLabel.text = sb.Length > 0 ? sb.ToString().TrimEnd() : "魔物をすべて打ち払った！";

            // ダメージ・回復ポップアップ
            var showPopup = (presenter.HitFlashTicks > 0 || presenter.ScreenShakeTicks > 0) && presenter.LastPopupAmount > 0;
            SetVisible(_battlePopupLabel, showPopup);
            if (showPopup)
            {
                _battlePopupLabel.text = presenter.LastPopupIsHeal
                    ? $"+{presenter.LastPopupAmount}"
                    : $"-{presenter.LastPopupAmount}";
                _battlePopupLabel.style.color = presenter.LastPopupIsHeal ? TextHeal : TextAccent;
            }

            var inInput = presenter.Step != BattleInputStep.Animating && presenter.Step != BattleInputStep.Finished;
            SetVisible(_battleCommandBox, inInput);
            if (inInput)
            {
                var actor = snap.Allies[presenter.CurrentActorIndex];
                _battleCommandTitle.text = $"{actor.Name} の行動";
                var rootLabels = BattlePresenter.RootCommandLabels;
                for (var i = 0; i < _battleCmdRows.Length; i++)
                {
                    if (i < rootLabels.Count)
                    {
                        var enabled = !(i == 4 && presenter.Battle.IsBossBattle);
                        _battleCmdRows[i].Bind(
                            true,
                            presenter.Step == BattleInputStep.RootCommand && presenter.RootCursor == i,
                            rootLabels[i],
                            string.Empty,
                            enabled);
                    }
                    else
                    {
                        _battleCmdRows[i].Bind(false, false, string.Empty, string.Empty, false);
                    }
                }
            }

            var showSub = presenter.Step == BattleInputStep.SpellSelect ||
                          presenter.Step == BattleInputStep.ItemSelect ||
                          presenter.Step == BattleInputStep.EnemyTarget ||
                          presenter.Step == BattleInputStep.AllyTarget;
            SetVisible(_battleSubBox, showSub);
            if (showSub)
            {
                ClearRows(_battleSubRows);
                switch (presenter.Step)
                {
                    case BattleInputStep.SpellSelect:
                        BindBattleSubOptions(presenter.GetCurrentSpellOptions(), presenter.SubCursor);
                        break;

                    case BattleInputStep.ItemSelect:
                        BindBattleSubOptions(presenter.GetCurrentItemOptions(), presenter.SubCursor);
                        break;

                    case BattleInputStep.EnemyTarget:
                        for (var i = 0; i < snap.Enemies.Count && i < _battleSubRows.Length; i++)
                        {
                            var e = snap.Enemies[i];
                            _battleSubRows[i].Bind(e.IsAlive, presenter.TargetCursor == i, e.DisplayName, $"HP {e.Hp}/{e.MaxHp}", e.IsAlive);
                        }

                        break;

                    case BattleInputStep.AllyTarget:
                        for (var i = 0; i < snap.Allies.Count && i < _battleSubRows.Length; i++)
                        {
                            var a = snap.Allies[i];
                            var status = a.IsAlive ? $"HP {a.Hp}/{a.MaxHp}  MP {a.Mp}/{a.MaxMp}" : "[戦闘不能]";
                            _battleSubRows[i].Bind(true, presenter.TargetCursor == i, a.Name, status, enabled: true);
                        }

                        break;
                }
            }

            var promptSuffix = presenter.Step == BattleInputStep.Finished ? "   [Enter / Z: 次へ]" : string.Empty;
            _battleMsgLabel.text = presenter.BannerMessage + promptSuffix;
            _battleFeedbackLabel.text = presenter.FeedbackMessage;
            SetVisible(_battleFeedbackLabel, presenter.FeedbackMessage.Length > 0);
        }

        private void RenderStoryModal(QuestFlow flow)
        {
            switch (flow.Screen)
            {
                case QuestScreen.HowTo:
                    _storyTitle.text = "あそびかた ― EMBERLOCK HOLLOW";
                    _storyBody.text =
                        "【目的】\n" +
                        "灯守りの町の北にある『焔鎖の坑道』（全3階層）を探索し、\n" +
                        "最奥の祭壇に巣食う『焔鎖の魔将ヴァルグ』を討ち払えばクリアです。\n\n" +
                        "【操作】\n" +
                        "・移動 / カーソル選択 : WASD または 矢印キー\n" +
                        "・決定 / 調べる / 早送り : Enter / Space / Z\n" +
                        "・キャンセル / 戻る : Esc / Backspace / X\n" +
                        "・キャンプメニュー開閉 : Tab / C / X（移動中）\n" +
                        "・一時停止 : P / F1\n\n" +
                        "【冒険の心得】\n" +
                        "・魔将ヴァルグは炎を纏っており、氷の呪文（フロスト / グレイシア）が弱点です。\n" +
                        "・セーブは町の『灯の教会』でのみ行えます。全滅しても所持金が半分になるだけで、\n" +
                        "  レベル・装備・宝箱の鍵はそのまま教会から再開できます。";
                    _storyFooter.text = "[Enter / Esc] タイトルへ戻る";
                    break;

                case QuestScreen.Ending:
                    _storyTitle.text = "ENDING ― 灯は再びともされた";
                    _storyBody.text =
                        "焔鎖の魔将ヴァルグは砕け散り、坑道を覆っていた黒煙が晴れていく。\n\n" +
                        "アレン、リナ、セオの 3 人が地上へ戻ると、\n" +
                        "『灯守りの町』の広場には温かな灯火と歓声が満ちていた。\n\n" +
                        "こうして長い灰の夜は終わり、町に穏やかな朝が戻った――。";
                    _storyFooter.text = "[Enter / Z] 冒険の記録を見る";
                    break;

                case QuestScreen.Result:
                    {
                        _storyTitle.text = "ADVENTURE RECORD ― 冒険の記録";
                        var sb = new StringBuilder(256);
                        if (flow.World != null)
                        {
                            var w = flow.World;
                            var totalSec = (int)(w.PlayTicks / Math.Max(1, flow.Data.Rules.TickRate));
                            var mins = totalSec / 60;
                            var secs = totalSec % 60;
                            sb.AppendLine($"クリア時間（ゲーム内） : {mins:D2}:{secs:D2} （{w.PlayTicks} Ticks）");
                            sb.AppendLine($"総歩数 : {w.Explorer.TotalSteps} 歩     戦闘回数 : {w.BattleCount} 回     所持金 : {w.Gold} G");
                            sb.AppendLine();
                            for (var i = 0; i < w.Party.Count; i++)
                            {
                                var m = w.Party[i];
                                var wep = m.WeaponId.Length > 0 ? flow.Data.ProductName(m.WeaponId) : "なし";
                                var arm = m.ArmorId.Length > 0 ? flow.Data.ProductName(m.ArmorId) : "なし";
                                sb.AppendLine($"・{m.Name}（{m.Class.Name}）  Lv.{m.Level}   HP {m.Hp}/{m.MaxHp}   MP {m.Mp}/{m.MaxMp}   装備: {wep} / {arm}");
                            }
                        }

                        _storyBody.text = sb.ToString();
                        _storyFooter.text = "[Enter / Z] タイトルへ戻る";
                        break;
                    }
            }
        }

        private static string BuildPartyStatusSummary(World world)
        {
            var sb = new StringBuilder(256);
            var maxLv = world.Data.ExpTable.MaxLevel;
            for (var i = 0; i < world.Party.Count; i++)
            {
                var m = world.Party[i];
                var nextExp = m.Level < maxLv
                    ? (world.Data.ExpTable.ExpForLevel(m.Level + 1) - m.Exp).ToString(CultureInfo.InvariantCulture)
                    : "-";
                var wep = m.WeaponId.Length > 0 ? world.Data.ProductName(m.WeaponId) : "なし";
                var arm = m.ArmorId.Length > 0 ? world.Data.ProductName(m.ArmorId) : "なし";
                sb.AppendLine($"■ {m.Name}（{m.Class.Name}）  Lv.{m.Level}   EXP {m.Exp}（次まで {nextExp}）");
                sb.AppendLine($"   HP {m.Hp}/{m.MaxHp}   MP {m.Mp}/{m.MaxMp}   攻 {m.Atk} / 守 {m.Def} / 速 {m.Agi}   武器:{wep}  防具:{arm}");
                if (i + 1 < world.Party.Count)
                {
                    sb.AppendLine();
                }
            }

            var hasKey = world.Flags.Get("flag_iron_key");
            sb.AppendLine();
            sb.Append($"貴重品 : {(hasKey ? "『鉄格子の鍵』" : "なし")}");
            return sb.ToString();
        }

        private void BindPartyTargetRows(World world, int selectedCursor, bool requireDead, bool allowDeadSelection)
        {
            for (var i = 0; i < world.Party.Count && i < _rightRows.Length; i++)
            {
                var m = world.Party[i];
                var ok = allowDeadSelection || (requireDead ? !m.IsAlive : m.IsAlive);
                var detail = m.IsAlive ? $"Lv{m.Level}  HP {m.Hp}/{m.MaxHp}  MP {m.Mp}/{m.MaxMp}" : $"Lv{m.Level}  [戦闘不能]";
                _rightRows[i].Bind(true, i == selectedCursor, $"{m.Name}（{m.Class.Name}）", detail, ok);
            }
        }

        private void BindSelectableOptions(IReadOnlyList<SelectableOption> opts, int cursor)
        {
            var start = ComputeWindowStart(opts.Count, cursor, _rightRows.Length);
            for (var i = 0; i < _rightRows.Length; i++)
            {
                var idx = start + i;
                if (idx < opts.Count)
                {
                    var o = opts[idx];
                    _rightRows[i].Bind(true, idx == cursor, o.Label, o.Detail, o.Enabled);
                }
                else
                {
                    _rightRows[i].Bind(false, false, string.Empty, string.Empty, false);
                }
            }
        }

        private void BindWindowedSellList(IReadOnlyList<SellCandidate> list, int cursor)
        {
            var start = ComputeWindowStart(list.Count, cursor, _rightRows.Length);
            for (var i = 0; i < _rightRows.Length; i++)
            {
                var idx = start + i;
                if (idx < list.Count)
                {
                    var c = list[idx];
                    _rightRows[i].Bind(true, idx == cursor, c.Label, $"{c.SellPrice} G", enabled: true);
                }
                else
                {
                    _rightRows[i].Bind(false, false, string.Empty, string.Empty, false);
                }
            }
        }

        private void BindBattleSubOptions(IReadOnlyList<SelectableOption> opts, int cursor)
        {
            var start = ComputeWindowStart(opts.Count, cursor, _battleSubRows.Length);
            for (var i = 0; i < _battleSubRows.Length; i++)
            {
                var idx = start + i;
                if (idx < opts.Count)
                {
                    var o = opts[idx];
                    _battleSubRows[i].Bind(true, idx == cursor, o.Label, o.Detail, o.Enabled);
                }
                else
                {
                    _battleSubRows[i].Bind(false, false, string.Empty, string.Empty, false);
                }
            }
        }

        private static int ComputeWindowStart(int total, int cursor, int capacity)
        {
            if (total <= capacity)
            {
                return 0;
            }

            var half = capacity / 2;
            return Math.Clamp(cursor - half, 0, total - capacity);
        }

        private static void ClearRows(MenuRow[] rows)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i].Bind(false, false, string.Empty, string.Empty, false);
            }
        }

        private static void SetVisible(VisualElement el, bool visible)
        {
            el.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static VisualElement MakeWindow(string name)
        {
            var box = new VisualElement
            {
                name = name,
                style =
                {
                    backgroundColor = WindowBg,
                    borderTopWidth = 3,
                    borderBottomWidth = 3,
                    borderLeftWidth = 3,
                    borderRightWidth = 3,
                    borderTopLeftRadius = 6,
                    borderTopRightRadius = 6,
                    borderBottomLeftRadius = 6,
                    borderBottomRightRadius = 6,
                    paddingLeft = 20,
                    paddingRight = 20,
                    paddingTop = 14,
                    paddingBottom = 14,
                },
            };
            SetBorderColor(box, BorderGold);
            return box;
        }

        private static VisualElement MakeInnerPanel(float width)
        {
            var panel = new VisualElement
            {
                style =
                {
                    backgroundColor = new Color(0.04f, 0.06f, 0.12f, 0.7f),
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    paddingLeft = 14,
                    paddingRight = 14,
                    paddingTop = 10,
                    paddingBottom = 10,
                },
            };
            if (width > 0f)
            {
                panel.style.width = width;
            }

            SetBorderColor(panel, new Color(0.55f, 0.48f, 0.28f, 0.8f));
            return panel;
        }

        private static void SetBorderColor(VisualElement el, Color color)
        {
            el.style.borderTopColor = color;
            el.style.borderBottomColor = color;
            el.style.borderLeftColor = color;
            el.style.borderRightColor = color;
        }

        private static Label MakeLabel(string name, int fontSize, Color color)
        {
            return new Label
            {
                name = name,
                style =
                {
                    fontSize = fontSize,
                    color = color,
                    marginTop = 0,
                    marginBottom = 0,
                    paddingTop = 0,
                    paddingBottom = 0,
                },
            };
        }

        private sealed class MenuRow
        {
            public readonly VisualElement Root;
            public readonly Label Cursor;
            public readonly Label Text;
            public readonly Label Detail;

            public MenuRow(string name, int fontSize, bool showDetail)
            {
                Root = new VisualElement
                {
                    name = name,
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.Center,
                        minHeight = fontSize + 10,
                        marginBottom = 3,
                    },
                };

                // 固定幅カーソル列（選択の有無で右側のラベル座標が動かない）
                Cursor = MakeLabel(name + "-cursor", fontSize, TextAccent);
                Cursor.style.width = 32;
                Cursor.style.unityTextAlign = TextAnchor.MiddleLeft;
                Root.Add(Cursor);

                Text = MakeLabel(name + "-label", fontSize, TextLight);
                Text.style.minWidth = 220;
                Root.Add(Text);

                Detail = MakeLabel(name + "-detail", Math.Max(18, fontSize - 2), TextLight);
                Detail.style.marginLeft = 20;
                Detail.style.flexGrow = 1;
                Detail.style.display = showDetail ? DisplayStyle.Flex : DisplayStyle.None;
                Root.Add(Detail);
            }

            public void Bind(bool visible, bool selected, string label, string detail, bool enabled)
            {
                Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible)
                {
                    return;
                }

                Cursor.text = selected ? "▶" : string.Empty;
                Text.text = label;
                Detail.text = detail;

                var c = !enabled ? TextMuted : (selected ? TextAccent : TextLight);
                Text.style.color = c;
                Detail.style.color = enabled ? TextLight : TextMuted;
            }
        }
    }
}
