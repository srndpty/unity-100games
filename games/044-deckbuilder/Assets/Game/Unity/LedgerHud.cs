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
    /// HUD（UI Toolkit）。手札・敵の意図と体力・対象の矢印・山の枚数・各画面のパネルを、App の卓（<see cref="TableModel"/>）と Flow から描く。
    /// クリックは押下（PointerDown）の時点で意図にして列へ積む（画面を移るクリックの離しで次の画面のボタンを押さない、033）。
    /// 作り直しは見た目を決める値の組が変わったときだけ（032）。
    /// </summary>
    public sealed class LedgerHud
    {
        private readonly VisualElement _root;
        private readonly DeckConfig _rules;
        private readonly Action<Intent> _queue;
        private readonly Sprite[] _cardIcons;
        private readonly Sprite[] _intentIcons;
        private readonly Label _route;
        private readonly Label _hp;
        private readonly Label _deck;
        private readonly VisualElement _battle;
        private readonly VisualElement _seats;
        private readonly VisualElement _energy;
        private readonly Label _energyText;
        private readonly Label _drawPile;
        private readonly Label _discardPile;
        private readonly Label _exhaustPile;
        private readonly VisualElement _endTurn;
        private readonly VisualElement _hand;
        private readonly VisualElement _resolving;
        private readonly Label _banner;
        private readonly Label _notice;
        private readonly VisualElement _panelLayer;
        private readonly VisualElement _panel;
        private readonly ArrowElement _arrow;
        private readonly SeatView _playerView;
        private readonly SeatView[] _slotViews = new SeatView[LedgerStage.SlotCount];
        private readonly Seat[] _slotSeats = new Seat[LedgerStage.SlotCount];
        private readonly Dictionary<int, VisualElement> _cards = new();
        private long _handKey = long.MinValue;
        private long _panelKey = long.MinValue;
        private int _resolvingId = -1;

        public LedgerHud(VisualElement root, DeckConfig rules, Sprite[] cardIcons, Sprite[] intentIcons, Action<Intent> queue)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _cardIcons = cardIcons;
            _intentIcons = intentIcons;
            _queue = queue;
            _route = root.Q<Label>("route");
            _hp = root.Q<Label>("hp");
            _deck = root.Q<Label>("deck");
            _battle = root.Q("battle");
            _seats = root.Q("seats");
            _energy = root.Q("energy");
            _energyText = root.Q<Label>("energy-text");
            _drawPile = root.Q<Label>("draw-pile");
            _discardPile = root.Q<Label>("discard-pile");
            _exhaustPile = root.Q<Label>("exhaust-pile");
            _endTurn = root.Q("end-turn");
            _hand = root.Q("hand");
            _resolving = root.Q("resolving");
            _banner = root.Q<Label>("banner");
            _notice = root.Q<Label>("notice");
            _panelLayer = root.Q("panel-layer");
            _panel = root.Q("panel");
            _arrow = new ArrowElement();
            root.Q("arrow-layer").Add(_arrow);
            OnPress(_endTurn, new Intent(IntentKind.EndTurn));
            _playerView = new SeatView(_seats, 0, null, this);
            for (int i = 0; i < _slotViews.Length; i++) { _slotViews[i] = new SeatView(_seats, i, intent => _queue(intent), this); }
        }

        public VisualElement Root => _root;

        /// <summary>手札のカードの要素（テスト用）。なければ null。</summary>
        public VisualElement CardElement(int cardId) => _cards.TryGetValue(cardId, out var e) ? e : null;

        /// <summary>立ち位置 slot の敵のクリック範囲（テスト用）。</summary>
        public VisualElement EnemyHit(int slot) => _slotViews[slot].Hit;

        /// <summary>パネルの中の名前付きの要素（テスト用）。</summary>
        public VisualElement PanelElement(string name) => _panel.Q(name);

        public VisualElement Panel => _panel;

        public ArrowElement Arrow => _arrow;

        public Label Banner => _banner;

        public Label EnergyText => _energyText;

        public string SeatText(int slot) => _slotViews[slot].Describe();

        public void Render(DeckFlow flow, LedgerStage stage, Camera camera)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var run = flow.Run;
            _route.text = run == null ? string.Empty : RouteText(run);
            _hp.text = run == null ? string.Empty : $"HP {(flow.Playback != null && flow.Screen == AppScreen.Battle ? flow.Playback.Table.Player.Hp : run.Hp)}/{run.MaxHp}";
            _deck.text = run == null ? string.Empty : $"デッキ {run.Deck.Count} 枚";
            var notice = flow.Notice;
            _notice.text = notice;
            _notice.style.display = notice.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            var inBattle = flow.Screen == AppScreen.Battle && flow.Playback != null;
            _battle.style.display = inBattle ? DisplayStyle.Flex : DisplayStyle.None;
            _seats.style.display = inBattle ? DisplayStyle.Flex : DisplayStyle.None;
            if (inBattle) { RenderBattle(flow, stage, camera); }
            else
            {
                _arrow.Visible = false;
                _handKey = long.MinValue;
            }

            RenderPanel(flow);
        }

        private void RenderBattle(DeckFlow flow, LedgerStage stage, Camera camera)
        {
            var playback = flow.Playback;
            var table = playback.Table;
            var config = _rules;
            var accepting = flow.AcceptsBattleInput;
            _energyText.text = $"{table.Energy}/{config.Energy}";
            _energy.EnableInClassList("energy-empty", table.Energy == 0);
            _drawPile.text = $"山札 {table.DrawCount}";
            _discardPile.text = $"捨て札 {table.Discard.Count}";
            _exhaustPile.text = $"廃棄 {table.Exhaust.Count}";
            _endTurn.EnableInClassList("btn-disabled", !accepting);

            var selected = flow.Targeting.CardId ?? -1;
            var key = (((((long)flow.BattleNumber * 1009) + playback.Cursor) * 1009) + selected + 1) * 4;
            key += (accepting ? 2 : 0) + (flow.Paused ? 1 : 0);
            if (key != _handKey)
            {
                _handKey = key;
                RebuildHand(table, selected, accepting);
            }

            var resolving = table.Resolving?.Id ?? -1;
            if (resolving != _resolvingId)
            {
                _resolvingId = resolving;
                _resolving.Clear();
                if (table.Resolving != null)
                {
                    var big = CardView(table.Resolving.Def, null);
                    big.AddToClassList("card-big");
                    big.pickingMode = PickingMode.Ignore;
                    _resolving.Add(big);
                }
            }

            _banner.text = BannerText(playback);
            LedgerStage.Assign(table, _slotSeats);
            var target = flow.Targeting.HasCard ? flow.Targeting.Cursor : null;
            _playerView.Render(table.Player, stage.Player, camera, playback, config, table, false);
            for (int i = 0; i < _slotViews.Length; i++)
            {
                var seat = _slotSeats[i];
                var renderer = stage.Slot(i);
                _slotViews[i].Render(renderer.enabled ? seat : null, renderer, camera, playback, config, table, seat != null && target == seat.Id);
            }

            RenderArrow(flow, selected);
        }

        private void RebuildHand(TableModel table, int selected, bool accepting)
        {
            _hand.Clear();
            _cards.Clear();
            for (int i = 0; i < table.Hand.Count; i++)
            {
                var card = table.Hand[i];
                var view = CardView(card.Def, new Intent(IntentKind.SelectCard, card.Id));
                var keyLabel = new Label(((i + 1) % 10).ToString(CultureInfo.InvariantCulture));
                keyLabel.AddToClassList("card-key");
                keyLabel.pickingMode = PickingMode.Ignore;
                view.Add(keyLabel);
                view.EnableInClassList("card-selected", card.Id == selected);
                view.EnableInClassList("card-unaffordable", card.Def.Cost > table.Energy || !accepting);
                _hand.Add(view);
                _cards[card.Id] = view;
            }
        }

        private void RenderArrow(DeckFlow flow, int selected)
        {
            _arrow.Visible = false;
            if (selected < 0 || !_cards.TryGetValue(selected, out var card) || flow.Targeting.Cursor == null) { return; }
            foreach (var view in _slotViews)
            {
                if (view.SeatId != flow.Targeting.Cursor || !view.Shown) { continue; }
                var from = card.worldBound;
                var to = view.Hit.worldBound;
                if (float.IsNaN(from.x) || float.IsNaN(to.x)) { return; }
                _arrow.Set(_arrow.WorldToLocal(new Vector2(from.center.x, from.yMin)), _arrow.WorldToLocal(new Vector2(to.center.x, to.center.y)));
                return;
            }
        }

        private static string BannerText(EventPlayback playback)
        {
            if (playback.Current == null) { return string.Empty; }
            var e = playback.Current.Value;
            return e.Kind switch
            {
                EventKind.TurnStarted => $"ターン {e.Amount}",
                EventKind.Victory => "勝利",
                EventKind.Defeat => "敗北",
                _ => string.Empty,
            };
        }

        private static string RouteText(Run run)
        {
            var names = new[] { "戦 1", "休憩", "戦 2", "ボス" };
            var sb = new StringBuilder();
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) { sb.Append("  ›  "); }
                sb.Append(i == run.Node && !run.IsOver ? $"［{names[i]}］" : names[i]);
            }

            return sb.ToString();
        }

        /// <summary>カード 1 枚の見た目。intent があれば押下でそれを積む。</summary>
        public VisualElement CardView(CardDef def, Intent? intent)
        {
            if (def == null) { throw new ArgumentNullException(nameof(def)); }
            var card = new VisualElement();
            card.AddToClassList("card");
            card.AddToClassList(def.Type switch { CardType.Attack => "card-attack", CardType.Skill => "card-skill", _ => "card-power" });
            card.Add(Text(def.Name, "card-name"));
            var icon = new VisualElement();
            icon.AddToClassList("card-icon");
            icon.pickingMode = PickingMode.Ignore;
            if (def.Index < _cardIcons.Length && _cardIcons[def.Index] != null) { icon.style.backgroundImage = new StyleBackground(_cardIcons[def.Index]); }
            card.Add(icon);
            card.Add(Text($"{CardText.TypeName(def.Type)}・{CardText.RarityName(def.Rarity)}", "card-type"));
            card.Add(Text(CardText.Describe(def), "card-text"));
            card.Add(Text(def.Cost.ToString(CultureInfo.InvariantCulture), "card-cost"));
            if (intent != null) { OnPress(card, intent.Value); }
            else { card.pickingMode = PickingMode.Ignore; }
            return card;
        }

        private void RenderPanel(DeckFlow flow)
        {
            var run = flow.Run;
            var screen = flow.Screen;
            var overlay = flow.Paused || screen != AppScreen.Battle;
            _panel.style.display = overlay ? DisplayStyle.Flex : DisplayStyle.None;
            _panelLayer.EnableInClassList("panel-shade", overlay && screen != AppScreen.Route && screen != AppScreen.Reward && screen != AppScreen.Rest);
            if (!overlay)
            {
                _panelKey = long.MinValue;
                return;
            }

            long key = ((int)screen * 2) + (flow.Paused ? 1 : 0);
            if (run != null)
            {
                key = (key * 1009) + flow.RunNumber;
                key = (key * 31) + run.Node;
                key = (key * 1009) + run.Deck.Count;
                key = (key * 4) + (run.RewardTaken ? 2 : 0) + (run.RemovalUsed ? 1 : 0);
                key = (key * 1009) + (flow.RemovalCandidate ?? -1) + 1;
            }

            if (key == _panelKey) { return; }
            _panelKey = key;
            _panel.Clear();
            if (flow.Paused)
            {
                BuildPause();
                return;
            }

            switch (screen)
            {
                case AppScreen.Title: BuildTitle(); break;
                case AppScreen.HowTo: BuildHowTo(_rules); break;
                case AppScreen.Route: BuildRoute(run); break;
                case AppScreen.Reward: BuildReward(flow, run); break;
                case AppScreen.Rest: BuildRest(run); break;
                case AppScreen.Result: BuildResult(run); break;
            }
        }

        private void BuildTitle()
        {
            _panel.Add(Text("CINDER LEDGER", "headline"));
            _panel.Add(Text("灯の書記は、帳面に綴ったカードで灰炉の主へ挑む。\n分かれ道を選んで 3 つの戦いを越え、勝つたびにデッキを鍛えよう。", "message"));
            var row = Row();
            row.Add(Button("start", "はじめる / Enter", new Intent(IntentKind.Confirm)));
            row.Add(Button("howto", "遊び方 / H", new Intent(IntentKind.HowTo)));
            row.Add(Button("quit", "終了", new Intent(IntentKind.Quit)));
            _panel.Add(row);
        }

        private void BuildHowTo(DeckConfig config)
        {
            _panel.Add(Text("遊び方", "headline"));
            _panel.Add(Text(
                $"・毎ターン エネルギー {config.Energy}・カードを {config.DrawPerTurn} 枚引く。カードの左上の数字がコスト。払えないカードは使えない。\n" +
                "・攻撃のカードはクリックしてから敵をクリック（キーなら 1〜0 で選び、←→/Tab で敵を選んで Enter）。対象のないカードは選んだ時点で使う。\n" +
                "・効果は書いた順に解決する。狙った敵が途中で倒れたら、その敵への残りの効果は出ない。\n" +
                "・敵の頭上の印は次の行動（剣＝攻撃の量、盾＝防御、矢印＝強化、しずく＝妨害、星＝仲間を呼ぶ）。ブロックは攻撃を先に受け、自分の番の初めに消える。\n" +
                "・脱力：与える攻撃 75%。弱体：受ける攻撃 150%。毒：番の初めに量だけ HP を失う。廃棄のカードはその戦闘から外れる。持続のカードは効果が残る。\n" +
                "・E でターン終了。手札は全て捨て札へ。山札が尽きたら捨て札を混ぜ直す。\n" +
                "・ルート：戦 1（2 択）→ 報酬 → 休憩 → 戦 2（2 択）→ 報酬 → ボス。手強い方は報酬の希少度が上がる。報酬はカード 3 択と、デッキから 1 枚の除去。\n" +
                "・HP は戦闘をまたいで持ち越す。0 になったら終わり。右クリック/Esc で選択の取り消し、何も選んでいなければ一時停止。",
                "howto"));
            var row = Row();
            row.Add(Button("back", "戻る / Esc", new Intent(IntentKind.Cancel)));
            _panel.Add(row);
        }

        private void BuildRoute(Run run)
        {
            _panel.Add(Text(run.Node == Run.BossNode ? "灰炉の主" : $"分かれ道 — 戦 {(run.Node == Run.BattleOneNode ? 1 : 2)}", "headline"));
            _panel.Add(Map(run));
            var row = Row();
            for (int i = 0; i < run.Options.Count; i++)
            {
                var option = run.Options[i];
                var choice = new VisualElement { name = $"option-{i}" };
                choice.AddToClassList("choice");
                choice.EnableInClassList("choice-hard", option.Hard);
                var tier = option.Encounter.Tier == EncounterTier.Boss ? "ボス" : option.Hard ? "手強い（報酬の希少度が上がる）" : "通常";
                choice.Add(Text($"{i + 1}. {option.Encounter.Name}", "choice-title"));
                choice.Add(Text(tier, "choice-text"));
                var foes = new List<string>();
                foreach (var f in option.Encounter.Foes) { foes.Add($"{f.Name}（HP {f.Hp}）"); }
                choice.Add(Text(string.Join("・", foes), "choice-text"));
                OnPress(choice, new Intent(IntentKind.Option, i));
                row.Add(choice);
            }

            _panel.Add(row);
            _panel.Add(Text(run.Options.Count == 1 ? "クリックか Enter で挑む" : "クリックか 1・2 で選ぶ", "message"));
        }

        private void BuildReward(DeckFlow flow, Run run)
        {
            _panel.Add(Text("報酬", "headline"));
            _panel.Add(Map(run));
            _panel.Add(Text(run.RewardTaken ? "カードを加えた。" : "カードを 1 枚選んでデッキに加える（クリックか 1〜3、取らずに進んでもよい）", "message"));
            var row = Row();
            for (int i = 0; i < run.RewardOptions.Count; i++)
            {
                var view = CardView(run.RewardOptions[i], run.RewardTaken ? null : new Intent(IntentKind.Option, i));
                view.name = $"reward-{i}";
                view.EnableInClassList("taken", run.RewardTaken);
                row.Add(view);
            }

            _panel.Add(row);
            _panel.Add(Text(run.RemovalUsed ? "デッキから 1 枚除去した。" : "デッキから 1 枚除去（任意）：カードを選んで「除去する」", "section"));
            var chips = new VisualElement();
            chips.AddToClassList("chips");
            foreach (var card in run.Deck)
            {
                var chip = Text($"{card.Def.Name}（{card.Def.Cost}）", "chip");
                chip.name = $"chip-{card.Id}";
                chip.pickingMode = PickingMode.Position;
                chip.EnableInClassList("chip-selected", flow.RemovalCandidate == card.Id);
                if (run.CanRemove) { OnPress(chip, new Intent(IntentKind.PickRemoval, card.Id)); }
                else { chip.AddToClassList("taken"); }
                chips.Add(chip);
            }

            _panel.Add(chips);
            var buttons = Row();
            var remove = Button("remove", "除去する", new Intent(IntentKind.ConfirmRemoval));
            remove.EnableInClassList("btn-disabled", flow.RemovalCandidate == null);
            buttons.Add(remove);
            buttons.Add(Button("leave", "先へ進む / Enter", new Intent(IntentKind.Leave)));
            _panel.Add(buttons);
        }

        private void BuildRest(Run run)
        {
            _panel.Add(Text("休憩", "headline"));
            _panel.Add(Map(run));
            _panel.Add(Text($"HP {run.Hp}/{run.MaxHp}。どちらか 1 つ（クリックか 1・2）", "message"));
            var row = Row();
            var heal = Choice("rest-0", "1. 休む", $"HP を {Math.Min(run.RestHeal, run.MaxHp - run.Hp)} 回復（最大 HP の {_rules.RestHealPercent}%）", new Intent(IntentKind.Option, 0));
            var train = Choice("rest-1", "2. 鍛える", $"最大 HP +{_rules.RestMaxHpGain}（同じだけ回復）", new Intent(IntentKind.Option, 1));
            row.Add(heal);
            row.Add(train);
            _panel.Add(row);
        }

        private void BuildResult(Run run)
        {
            var won = run.Stage == RunStage.Victory;
            _panel.Add(Text(won ? "灰炉を鎮めた" : "灯が消えた", "headline"));
            var reached = won ? "ボスを倒した" : $"{(run.Node == Run.BossNode ? "ボス" : $"戦 {(run.Node == Run.BattleOneNode ? 1 : 2)}")}で倒れた";
            _panel.Add(Text(
                $"{reached}\n勝った戦闘 {run.BattlesWon}　ターン {run.TotalTurns}　使ったカード {run.CardsPlayed} 枚　受けたダメージ {run.DamageTaken}\n最後のデッキ {run.Deck.Count} 枚　HP {run.Hp}/{run.MaxHp}",
                "message"));
            var row = Row();
            row.Add(Button("to-title", "タイトルへ / Enter", new Intent(IntentKind.Confirm)));
            _panel.Add(row);
        }

        private void BuildPause()
        {
            _panel.Add(Text("一時停止", "headline"));
            var row = Row();
            row.Add(Button("resume", "再開 / Esc", new Intent(IntentKind.Cancel)));
            row.Add(Button("pause-title", "タイトルへ / T", new Intent(IntentKind.ToTitle)));
            _panel.Add(row);
        }

        private static VisualElement Map(Run run)
        {
            var map = new VisualElement();
            map.AddToClassList("map");
            var names = new[] { "戦 1", "休憩", "戦 2", "ボス" };
            for (int i = 0; i < names.Length; i++)
            {
                var label = Text(names[i], "node");
                label.EnableInClassList("node-done", i < run.Node);
                label.EnableInClassList("node-current", i == run.Node);
                map.Add(label);
            }

            return map;
        }

        private VisualElement Choice(string name, string title, string text, Intent intent)
        {
            var choice = new VisualElement { name = name };
            choice.AddToClassList("choice");
            choice.Add(Text(title, "choice-title"));
            choice.Add(Text(text, "choice-text"));
            OnPress(choice, intent);
            return choice;
        }

        private VisualElement Button(string name, string text, Intent intent)
        {
            var button = new VisualElement { name = name };
            button.AddToClassList("btn");
            button.Add(Text(text, "btn-text"));
            OnPress(button, intent);
            return button;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            return row;
        }

        internal static Label Text(string text, string cls)
        {
            var label = new Label(text);
            label.AddToClassList(cls);
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        /// <summary>左ボタンの押下で意図を積む。</summary>
        internal void OnPress(VisualElement element, Intent intent)
        {
            element.pickingMode = PickingMode.Position;
            element.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) { return; }
                _queue(intent);
                e.StopPropagation();
            });
        }

        internal Sprite IntentSprite(IntentIcon icon) => (int)icon < _intentIcons.Length ? _intentIcons[(int)icon] : null;

        /// <summary>1 人ぶんの重ね表示：頭上の意図、足元の名前・HP・ブロック・状態、被弾の数字、敵のクリック範囲。</summary>
        private sealed class SeatView
        {
            private readonly LedgerHud _hud;
            private readonly VisualElement _intent;
            private readonly VisualElement _intentIcon;
            private readonly Label _intentText;
            private readonly Label _intentName;
            private readonly VisualElement _info;
            private readonly Label _name;
            private readonly VisualElement _hpFill;
            private readonly Label _hpText;
            private readonly Label _block;
            private readonly Label _status;
            private readonly Label _float;

            public SeatView(VisualElement layer, int slot, Action<Intent> queue, LedgerHud hud)
            {
                _hud = hud;
                Hit = new VisualElement { name = queue == null ? "player-hit" : $"enemy-hit-{slot}" };
                Hit.AddToClassList("seat-hit");
                Hit.pickingMode = queue == null ? PickingMode.Ignore : PickingMode.Position;
                if (queue != null)
                {
                    Hit.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button != 0 || SeatId < 0) { return; }
                        queue(new Intent(IntentKind.ClickEnemy, SeatId));
                        e.StopPropagation();
                    });
                }

                layer.Add(Hit);
                _intent = new VisualElement();
                _intent.AddToClassList("seat");
                _intent.AddToClassList("seat-intent");
                _intent.pickingMode = PickingMode.Ignore;
                _intentIcon = new VisualElement();
                _intentIcon.AddToClassList("intent-icon");
                _intentIcon.pickingMode = PickingMode.Ignore;
                _intentText = Text(string.Empty, "intent-text");
                _intentName = Text(string.Empty, "intent-name");
                _intent.Add(_intentIcon);
                _intent.Add(_intentText);
                _intent.Add(_intentName);
                layer.Add(_intent);

                _info = new VisualElement();
                _info.AddToClassList("seat-info");
                _info.pickingMode = PickingMode.Ignore;
                _name = Text(string.Empty, "seat-name");
                var bar = new VisualElement();
                bar.AddToClassList("hp-bar");
                bar.pickingMode = PickingMode.Ignore;
                _hpFill = new VisualElement();
                _hpFill.AddToClassList("hp-fill");
                _hpFill.pickingMode = PickingMode.Ignore;
                _hpText = Text(string.Empty, "hp-text");
                bar.Add(_hpFill);
                bar.Add(_hpText);
                _block = Text(string.Empty, "block-text");
                _status = Text(string.Empty, "status-text");
                _info.Add(_name);
                _info.Add(bar);
                _info.Add(_block);
                _info.Add(_status);
                layer.Add(_info);
                _float = Text(string.Empty, "float");
                layer.Add(_float);
            }

            public VisualElement Hit { get; }

            public int SeatId { get; private set; } = -1;

            public bool Shown { get; private set; }

            public string Describe() => Shown ? $"{_name.text} {_hpText.text} {_block.text} {_status.text} {_intentText.text}" : string.Empty;

            public void Render(Seat seat, SpriteRenderer renderer, Camera camera, EventPlayback playback, DeckConfig config, TableModel table, bool targeted)
            {
                Shown = seat != null && renderer != null && renderer.enabled && Hit.panel != null;
                var display = Shown ? DisplayStyle.Flex : DisplayStyle.None;
                Hit.style.display = display;
                _info.style.display = display;
                _intent.style.display = display;
                _float.style.display = display;
                SeatId = Shown ? seat.Id : -1;
                if (!Shown) { return; }

                var bounds = renderer.bounds;
                var panel = Hit.panel;
                var topLeft = RuntimePanelUtils.CameraTransformWorldToPanel(panel, new Vector3(bounds.min.x, bounds.max.y, 0), camera);
                var bottomRight = RuntimePanelUtils.CameraTransformWorldToPanel(panel, new Vector3(bounds.max.x, bounds.min.y, 0), camera);
                var centerX = (topLeft.x + bottomRight.x) / 2;
                Hit.style.left = topLeft.x;
                Hit.style.top = topLeft.y;
                Hit.style.width = bottomRight.x - topLeft.x;
                Hit.style.height = bottomRight.y - topLeft.y;
                Hit.EnableInClassList("seat-hit-target", targeted);
                _info.style.left = centerX - 130;
                _info.style.top = bottomRight.y + 4;
                _intent.style.left = centerX - 130;
                _intent.style.top = topLeft.y - 46;

                _name.text = seat.Def == null ? "灯の書記" : seat.Def.Name;
                _hpText.text = $"{Math.Max(0, seat.Hp)}/{seat.MaxHp}";
                _hpFill.style.width = Length.Percent(100f * Mathf.Clamp01(seat.Hp / (float)Math.Max(1, seat.MaxHp)));
                _block.text = seat.Block > 0 ? $"ブロック {seat.Block}" : string.Empty;
                _status.text = StatusText(seat);

                if (seat.Def != null && seat.Intent != null && seat.Alive)
                {
                    var summary = IntentSummary.Of(seat, table, config);
                    _intentIcon.style.backgroundImage = new StyleBackground(_hud.IntentSprite(summary.Icon));
                    _intentText.text = summary.Text;
                    _intentName.text = seat.Intent.Name;
                    _intent.style.visibility = Visibility.Visible;
                }
                else
                {
                    _intent.style.visibility = Visibility.Hidden;
                    _intentText.text = string.Empty;
                }

                RenderFloat(seat, playback, centerX, topLeft.y);
            }

            private void RenderFloat(Seat seat, EventPlayback playback, float centerX, float top)
            {
                _float.text = string.Empty;
                var current = playback.Current;
                if (current == null || playback.Age > 40) { return; }
                var e = current.Value;
                string text = null;
                string cls = "float-damage";
                if (e.Target == seat.Id)
                {
                    switch (e.Kind)
                    {
                        case EventKind.Damaged:
                            text = e.Amount > 0 ? $"-{e.Amount}" : "防いだ";
                            cls = e.Amount > 0 ? "float-damage" : "float-block";
                            break;
                        case EventKind.PoisonTick:
                            text = $"毒 -{e.Amount}";
                            cls = "float-poison";
                            break;
                        case EventKind.BlockGained:
                            text = $"+{e.Amount} ブロック";
                            cls = "float-block";
                            break;
                        case EventKind.StatusApplied when e.Amount > 0:
                            text = $"{CardText.StatusName((StatusKind)e.Detail)} +{e.Amount}";
                            cls = "float-status";
                            break;
                    }
                }
                else if (e.Kind == EventKind.Fizzled && e.Source == seat.Id)
                {
                    text = "不発";
                    cls = "float-status";
                }

                if (text == null) { return; }
                _float.text = text;
                _float.RemoveFromClassList("float-damage");
                _float.RemoveFromClassList("float-block");
                _float.RemoveFromClassList("float-status");
                _float.RemoveFromClassList("float-poison");
                _float.AddToClassList(cls);
                _float.style.left = centerX - 130;
                _float.style.top = top - 90 - playback.Age;
            }

            private static string StatusText(Seat seat)
            {
                var sb = new StringBuilder();
                foreach (var kind in _kinds)
                {
                    var v = seat.Status(kind);
                    if (v <= 0) { continue; }
                    if (sb.Length > 0) { sb.Append('　'); }
                    sb.Append(CardText.StatusName(kind)).Append(' ').Append(v);
                }

                return sb.ToString();
            }

            private static readonly StatusKind[] _kinds =
            {
                StatusKind.Strength, StatusKind.Weak, StatusKind.Vulnerable, StatusKind.Poison, StatusKind.Bulwark, StatusKind.Miasma,
            };
        }
    }

    /// <summary>選んだカードから対象の敵へ引く矢印（Painter2D）。</summary>
    public sealed class ArrowElement : VisualElement
    {
        private Vector2 _from;
        private Vector2 _to;

        public ArrowElement()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += Draw;
        }

        public bool Visible
        {
            get => style.display != DisplayStyle.None;
            set => style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public Vector2 From => _from;

        public Vector2 To => _to;

        public void Set(Vector2 from, Vector2 to)
        {
            Visible = true;
            if (from == _from && to == _to) { return; }
            _from = from;
            _to = to;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            var control = new Vector2((_from.x + _to.x) / 2, Mathf.Min(_from.y, _to.y) - 160);
            p.strokeColor = new Color(1f, .82f, .35f, .95f);
            p.lineWidth = 8;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(_from);
            p.QuadraticCurveTo(control, _to);
            p.Stroke();
            var dir = (_to - control).normalized;
            var side = new Vector2(-dir.y, dir.x);
            p.fillColor = new Color(1f, .82f, .35f, .95f);
            p.BeginPath();
            p.MoveTo(_to + (dir * 6));
            p.LineTo(_to - (dir * 26) + (side * 16));
            p.LineTo(_to - (dir * 26) - (side * 16));
            p.ClosePath();
            p.Fill();
        }
    }
}
