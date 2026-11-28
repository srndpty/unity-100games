using System;
using System.Linq;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    public sealed class MahjongGame : MonoBehaviour
    {
        [SerializeField] private PanelRenderer _panel = null!;
        [SerializeField] private Texture2D _tileAtlas = null!;
        private readonly Sprite[] _tileSprites = new Sprite[36];
        [SerializeField] private MahjongSettings _settings = null!;
        private static readonly Color Ink = new(0.09f, 0.19f, 0.16f);
        private static readonly Color Linen = new(0.97f, 0.92f, 0.80f);
        private static readonly Color Gold = new(0.95f, 0.74f, 0.40f);
        private static readonly Color Felt = new(0.13f, 0.29f, 0.24f);
        private static readonly Color Panel = new(0.07f, 0.17f, 0.14f);
        private static readonly Color TileBlue = new(0.10f, 0.24f, 0.45f);
        private Font? _font;
        private AudioClip? _click;
        private AudioSource _audio = null!;
        private TableFlow? _renderedFlow;
        private Match? _renderedMatch;
        private bool _renderedPause;
        private int _revision;
        private int _options;
        private TableScreen _screen;
        private MoveKind _mode;
        private VisualElement? _actions;

        public TableFlow Flow { get; private set; } = null!;
        public VisualElement? Root { get; private set; }

        private void Awake()
        {
            Flow = new TableFlow(EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount, _settings.CpuDelay);
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo", "MS Gothic" }, 32);
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _click = AudioClip.Create("牌の音", 1323, 1, 22050, false);
            var samples = new float[1323];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = Mathf.Sin(i * 0.215f) * Mathf.Exp(-i / 105f) * 0.31f;
            }

            _click.SetData(samples, 0);
            var width = _tileAtlas.width / 9;
            var height = _tileAtlas.height / 4;
            for (var id = 0; id < _tileSprites.Length; id++)
            {
                var rect = new Rect(id % 9 * width, (3 - id / 9) * height, width, height);
                _tileSprites[id] = Sprite.Create(_tileAtlas, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            }

            _panel.RegisterUIReloadCallback(OnReload);
        }

        private void OnReload(PanelRenderer panel, VisualElement root, int version)
        {
            Root = root.Q("hud");
            if (Root == null) { throw new InvalidOperationException("対局画面のルートがありません。"); }

            if (_font != null) { Root.style.unityFontDefinition = FontDefinition.FromFont(_font); }

            _renderedFlow = null;
            Render();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (Flow != null && !EvidenceCapture.IsRunning) { Flow.Focus(focused); }
        }

        private void OnDestroy()
        {
            if (_font != null) { Destroy(_font); }
            if (_click != null) { Destroy(_click); }
            foreach (var tile in _tileSprites) { if (tile != null) { Destroy(tile); } }
        }

        private void Update()
        {
            if (!EvidenceCapture.IsRunning)
            {
                Flow.Step(Time.unscaledDeltaTime);
                Render();
            }
        }

        public void UseDemonstration(TableFlow flow)
        {
            if (!EvidenceCapture.IsRunning && !Application.isBatchMode)
            {
                throw new InvalidOperationException("台本用の対局は検証中にだけ使用できます。");
            }

            Flow = flow;
            Render();
        }

        public void Render()
        {
            var root = Root;
            if (root == null) { return; }

            var match = Flow.Match;
            var revision = match?.Revision ?? -1;
            var options = match?.LegalActions(0).Count ?? 0;
            if (_renderedFlow == Flow && _renderedMatch == match && _screen == Flow.Screen &&
                _revision == revision && _options == options && _renderedPause == Flow.Paused) { return; }

            if (_renderedMatch == match && _revision != revision && _click != null && !AudioPolicy.ShouldMute)
            {
                _audio.pitch = match?.Phase == Phase.HandResult ? 0.8f : 1f;
                _audio.PlayOneShot(_click);
            }

            _renderedFlow = Flow;
            _renderedMatch = match;
            _screen = Flow.Screen;
            _revision = revision;
            _options = options;
            _renderedPause = Flow.Paused;
            _mode = MoveKind.Discard;
            root.Clear();
            root.style.backgroundColor = Ink;
            root.style.color = Linen;
            if (match == null) { Menu(root); }
            else if (match.Phase is Phase.HandResult or Phase.MatchResult) { Results(root, match); }
            else { Table(root, match); }

            if (Flow.Paused)
            {
                var overlay = Box(root, Panel);
                overlay.style.position = Position.Absolute;
                overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0;
                overlay.style.alignItems = Align.Center;
                overlay.style.justifyContent = Justify.Center;
                Label(overlay, "一時停止中", 38);
                Control(overlay, "resume", "対局を再開", () => Push(Intent.Resume));
            }
        }

        private void Menu(VisualElement root)
        {
            var banner = Box(root, Felt);
            banner.style.flexGrow = 1;
            banner.style.alignItems = Align.Center;
            banner.style.justifyContent = Justify.Center;
            if (Flow.Screen == TableScreen.Title)
            {
                Label(banner, "リーチ麻雀", 76);
                Label(banner, "四人打ち　東風戦", 24);
                var decoration = Row(banner);
                foreach (var tile in new[] { 0, 1, 2, 27, 31 }) { Tile(decoration, tile); }
                Control(banner, "start", "対局を始める", () => Push(Intent.Start));
                Control(banner, "help", "遊び方", () => Push(Intent.Help));
                return;
            }

            Label(banner, "遊び方", 46);
            Label(banner, "自分の番に手牌をクリックして打牌。ツモ牌は右端に表示します。", 22);
            Label(banner, "応答欄からチー・ポン・カン・ロンを選ぶか、見送ります。", 22);
            Label(banner, "リーチを選んだら、聴牌を維持できる打牌候補をクリックします。", 22);
            Label(banner, "和了には役が必要です。東4局の親が交代すると順位が決まります。", 22);
            Control(banner, "start", "対局を始める", () => Push(Intent.Start));
            Control(banner, "title", "タイトルへ", () => Push(Intent.Title));
        }

        private void Table(VisualElement root, Match match)
        {
            var body = Row(root);
            body.style.flexGrow = 1;
            body.style.paddingLeft = body.style.paddingRight = 16;
            body.style.paddingTop = body.style.paddingBottom = 13;
            var board = Box(body, Felt);
            board.style.flexGrow = 1;
            board.style.minWidth = 0;
            board.style.paddingLeft = board.style.paddingRight = 12;
            var cluster = Box(board, Felt);
            cluster.style.flexGrow = 1;
            cluster.style.justifyContent = Justify.Center;
            SeatPanel(cluster, match, 2);
            var middle = Row(cluster);
            middle.style.justifyContent = Justify.Center;
            middle.style.alignItems = Align.Center;
            SeatPanel(middle, match, 3);
            var center = Box(middle, Panel);
            center.style.width = 166;
            center.style.marginLeft = center.style.marginRight = 10;
            center.style.paddingLeft = center.style.paddingRight = 8;
            Label(center, $"東{match.Round}局　{match.Honba}本場", 23);
            Label(center, $"山 {match.Remaining}枚　供託 {match.Sticks}", 15);
            Label(center, "ドラ表示牌", 14);
            var dora = Row(center);
            dora.style.flexWrap = Wrap.Wrap;
            foreach (var id in match.DoraIndicators) { Tile(dora, Tiles.Type(id), true); }

            SeatPanel(middle, match, 1);
            SeatPanel(cluster, match, 0);
            var side = Box(body, Panel);
            side.style.width = 232;
            side.style.marginLeft = 12;
            side.style.paddingLeft = side.style.paddingRight = 10;
            side.style.paddingTop = 12;
            Label(side, "対局操作", 24);
            var status = match.Phase switch
            {
                Phase.KanReaction => $"{SeatName(match.Current)}の加槓",
                Phase.DiscardReaction => $"{SeatName(match.Current)}の打牌",
                _ => match.Current == 0 ? "あなたの番" : $"{SeatName(match.Current)}の番",
            };
            Label(side, status, 17);
            if (match.Target >= 0 && match.Phase is Phase.DiscardReaction or Phase.KanReaction)
            {
                var targetTile = Tile(Row(side), Tiles.Type(match.Target));
                targetTile.style.unityBackgroundImageTintColor = Gold;
                Pulse(targetTile);
            }

            _actions = Box(side, Panel);
            DrawActions(match);
            Control(side, "title", "対局を終了", () => Push(Intent.Title));
            if (match.LegalActions(0).Contains(new Move(MoveKind.Pass)))
            {
                Box(side, Panel).style.flexGrow = 1;
                Control(side, "pass-bottom", "見送り", BindMove(new Move(MoveKind.Pass)));
            }

            var tip = Label(board, string.Empty, 17);
            tip.name = "discard-tip";
            tip.style.height = 24;
            tip.style.unityTextAlign = TextAnchor.MiddleCenter;
            tip.style.color = Gold;
            DrawHand(board, match);
            for (var seat = 1; seat < 4; seat++) { DrawConcealedHand(board, match, seat); }
        }

        private void SeatPanel(VisualElement parent, Match match, int seat)
        {
            var player = match.Seats[seat];
            var container = Box(parent, match.Current == seat ? new Color(0.19f, 0.36f, 0.29f) : Felt);
            container.style.alignSelf = Align.Center;
            container.style.width = 178;
            container.style.marginTop = container.style.marginBottom = 4;
            container.style.paddingLeft = container.style.paddingRight = 5;
            var wind = "東南西北"[(seat - match.Dealer + 4) % 4];
            Label(container, $"{wind}　{SeatName(seat)}　{player.Points:N0}" + (player.Riichi ? " 立直" : ""), 17);
            if (seat != 0) { Label(container, $"手牌 {player.HandCount}枚", 13); }

            var river = Row(container);
            river.name = "river-" + seat;
            river.style.flexWrap = Wrap.Wrap;
            foreach (var discard in player.Discards)
            {
                var tile = Tile(river, Tiles.Type(discard.Id), true);
                if (discard.Riichi) { tile.style.unityBackgroundImageTintColor = Gold; }
                if (discard.Called) { tile.style.opacity = 0.46f; }
                if (discard.Id == match.Target && (match.Phase is Phase.DiscardReaction or Phase.AfterCall)) { Pulse(tile); }
            }

            var calls = Row(container);
            calls.style.flexWrap = Wrap.Wrap;
            foreach (var meld in player.Calls) { DrawMeld(calls, meld, seat); }
        }

        private void DrawMeld(VisualElement parent, Meld meld, int seat)
        {
            var group = Row(parent);
            group.style.marginRight = 4;
            group.style.alignItems = Align.FlexEnd;
            var source = (meld.From - seat + 4) % 4;
            var label = meld.Group.Kind switch
            {
                MeldKind.Chi => "チー",
                MeldKind.Pon => "ポン",
                MeldKind.OpenKan => "大明槓",
                MeldKind.AddedKan => "加槓",
                _ => "暗槓",
            };
            group.tooltip = source == 0 ? label : label + "・" + SeatName(meld.From);
            var ids = meld.Ids.Where(id => id != meld.CalledId).OrderBy(Tiles.Type).ToList();
            var stacked = meld.Group.Kind == MeldKind.AddedKan;
            if (stacked) { ids.RemoveAt(ids.Count - 1); }
            if (source != 0) { ids.Insert(source == 3 ? 0 : source == 2 ? 1 : ids.Count, meld.CalledId); }

            for (var index = 0; index < ids.Count; index++)
            {
                var id = ids[index];
                if (id == meld.CalledId)
                {
                    var stack = Box(group, Color.clear);
                    if (stacked) { Sideways(stack, id); }
                    Sideways(stack, id);
                    continue;
                }

                var visual = Tile(group, Tiles.Type(id), true);
                if (source == 0 && (index == 0 || index == ids.Count - 1))
                {
                    visual.style.backgroundImage = Background.FromSprite(_tileSprites[34]);
                    visual.tooltip = label;
                }
            }

            void Sideways(VisualElement stack, int id)
            {
                var tile = Tile(stack, Tiles.Type(id), true);
                tile.style.rotate = new Rotate(Angle.Degrees(90f));
                tile.style.marginLeft = tile.style.marginRight = 4;
                tile.style.marginTop = tile.style.marginBottom = -2;
                tile.style.unityBackgroundImageTintColor = Gold;
                tile.tooltip = group.tooltip;
            }
        }

        private void DrawActions(Match match)
        {
            var target = _actions!;
            target.Clear();
            var legal = match.LegalActions(0);
            if (_mode == MoveKind.Riichi)
            {
                Label(target, "リーチする打牌を選択", 16);
                Control(target, "cancel-riichi", "取消", () => SetMode(match, MoveKind.Discard));
                return;
            }

            if (legal.Any(m => m.Kind == MoveKind.Riichi))
            {
                Control(target, "riichi", "リーチ", () => SetMode(match, MoveKind.Riichi));
            }

            foreach (var move in legal.Where(m => m.Kind is not (MoveKind.Discard or MoveKind.Riichi)))
            {
                var label = move.Kind switch
                {
                    MoveKind.Tsumo => "ツモ",
                    MoveKind.Ron => "ロン",
                    MoveKind.Pass => "見送り",
                    MoveKind.Chi => "チー " + string.Join("・", Enumerable.Range(move.Value, 3).Select(TileName)),
                    MoveKind.Pon => "ポン",
                    MoveKind.OpenKan => "カン",
                    MoveKind.ClosedKan => "暗槓 " + TileName(move.Value),
                    MoveKind.AddedKan => "加槓 " + TileName(move.Value),
                    _ => move.Kind.ToString(),
                };
                Control(target, move.Kind + "-" + move.Value, label, BindMove(move));
            }
        }

        private void SetMode(Match match, MoveKind mode)
        {
            _mode = mode;
            DrawActions(match);
            var legal = match.LegalActions(0);
            UpdateRecommendation(match);
            foreach (var button in Root!.Query<Button>(className: "hand-tile").ToList())
            {
                SetPlayable(button, legal.Contains(new Move(mode, (int)button.userData)));
            }
        }

        private void DrawConcealedHand(VisualElement board, Match match, int seat)
        {
            var far = seat == 2;
            var rack = Row(board);
            rack.name = "cpu-rack-" + seat;
            rack.pickingMode = PickingMode.Ignore;
            rack.style.position = Position.Absolute;
            rack.style.justifyContent = Justify.Center;
            rack.style.alignItems = Align.Center;
            if (far)
            {
                rack.style.left = rack.style.right = 74;
                rack.style.top = 12;
            }
            else
            {
                rack.style.flexDirection = FlexDirection.Column;
                rack.style.top = 55;
                rack.style.bottom = 92;
                if (seat == 3) { rack.style.left = Length.Percent(7); }
                else { rack.style.right = Length.Percent(7); }
            }

            var player = match.Seats[seat];
            var drawn = match.Current == seat && match.Phase == Phase.Drawn;
            for (var index = 0; index < player.HandCount; index++)
            {
                var tile = new VisualElement { pickingMode = PickingMode.Ignore };
                tile.style.backgroundImage = Background.FromSprite(_tileSprites[34]);
                tile.style.width = 28;
                tile.style.height = 35;
                tile.style.flexShrink = 0;
                var separated = drawn && index == player.HandCount - 1;
                if (far)
                {
                    if (separated) { tile.style.marginLeft = 8; }
                }
                else
                {
                    tile.style.rotate = new Rotate(Angle.Degrees(seat == 3 ? 90f : -90f));
                    // Rotation affects drawing, so reserve the turned tile's short edge in the layout.
                    tile.style.marginTop = separated ? 4 : -4;
                    tile.style.marginBottom = -4;
                }

                rack.Add(tile);
            }
        }

        private void DrawHand(VisualElement board, Match match)
        {
            var row = Row(board);
            row.style.height = 73;
            row.style.justifyContent = Justify.Center;
            row.style.alignItems = Align.Center;
            var drawn = match.Current == 0 && match.Phase == Phase.Drawn ? match.Drawn : -1;
            System.Collections.Generic.IEnumerable<int> hand = match.Hand(0).Where(id => id != drawn).OrderBy(id => id);
            if (drawn >= 0) { hand = hand.Append(drawn); }

            var legal = match.LegalActions(0);
            foreach (var id in hand)
            {
                var t = Tiles.Type(id);
                var button = new Button(BindMove(() => new Move(_mode, id))) { tooltip = TileName(t), name = "tile-" + id, userData = id };
                button.AddToClassList("hand-tile");
                StyleTile(button, t, false);
                button.style.width = 49;
                button.style.height = 59;
                button.style.marginLeft = id == drawn ? 15 : 2;
                SetPlayable(button, legal.Contains(new Move(_mode, id)));
                row.Add(button);
            }

            UpdateRecommendation(match);
        }

        private void UpdateRecommendation(Match match)
        {
            var recommended = Cpu.RecommendDiscard(match, 0, match.LegalActions(0), _mode);
            Root!.Q<Label>("discard-tip").text = recommended.HasValue
                ? "打牌のおすすめ　" + TileName(Tiles.Type(recommended.Value)) : string.Empty;
            foreach (var button in Root.Query<Button>(className: "hand-tile").ToList())
            {
                var highlight = recommended == (int)button.userData;
                button.style.borderTopColor = button.style.borderBottomColor =
                    button.style.borderLeftColor = button.style.borderRightColor = Gold;
                button.style.borderTopWidth = button.style.borderBottomWidth =
                    button.style.borderLeftWidth = button.style.borderRightWidth = highlight ? 3 : 0;
            }
        }

        private void Results(VisualElement root, Match match)
        {
            var page = Box(root, Felt);
            page.style.flexGrow = 1;
            page.style.paddingTop = 22;
            page.style.paddingLeft = page.style.paddingRight = 38;
            var final = match.Phase == Phase.MatchResult;
            var headingLabel = Label(page, final ? "最終順位" : $"東{match.Round}局　{match.Honba}本場　{(match.Result!.Wins.Count == 0 ? "流局" : "和了")}", 34);
            Pulse(headingLabel);
            var result = match.Result!;
            if (!final)
            {
                var summary = Row(page);
                summary.style.flexGrow = 1;
                foreach (var win in result.Wins)
                {
                    var card = Box(summary, Panel);
                    card.style.flexGrow = 1;
                    card.style.flexBasis = 0;
                    card.style.minWidth = 0;
                    card.style.marginRight = 12;
                    card.style.paddingLeft = 10;
                    var heading = win.Score.Yakuman > 0 ? $"役満 ×{win.Score.Yakuman}" : $"{win.Score.Fu}符 {win.Score.Han}翻";
                    Label(card, $"{SeatName(win.Seat)}　{(result.Source == win.Seat ? "ツモ" : "ロン")}　{heading}", 22);
                    Label(card, string.Join("・", win.Score.Yaku), 17).style.whiteSpace = WhiteSpace.Normal;
                    Label(card, string.Join("・", win.Score.FuItems), 15).style.whiteSpace = WhiteSpace.Normal;
                    var tiles = Row(card);
                    tiles.style.flexWrap = Wrap.Wrap;
                    foreach (var id in win.Hand.Where(id => id != win.Tile).OrderBy(id => id)) { Tile(tiles, Tiles.Type(id), true); }

                    Tile(tiles, Tiles.Type(win.Tile), true).style.unityBackgroundImageTintColor = Gold;
                    foreach (var meld in match.Seats[win.Seat].Calls) { DrawMeld(card, meld, win.Seat); }
                }

                if (result.Ura.Count > 0)
                {
                    var ura = Row(page);
                    Label(ura, "裏ドラ表示牌", 15);
                    foreach (var id in result.Ura) { Tile(ura, Tiles.Type(id), true); }
                }
            }

            var order = final ? match.Ranking : new[] { 0, 1, 2, 3 };
            var points = Row(page);
            foreach (var seat in order)
            {
                var status = final ? $"{Array.IndexOf(order.ToArray(), seat) + 1}位"
                    : result.Wins.Count == 0 ? (result.Ready[seat] ? "聴牌" : "ノーテン") : string.Empty;
                var line = $"{status}　{SeatName(seat)}　{match.Seats[seat].Points:N0}点";
                if (!final) { line += $"　({result.Differences[seat]:+0;-0;0})"; }
                var label = Label(points, line, 17);
                label.style.flexGrow = 1;
            }

            if (final) { Control(page, "restart", "もう一度対局", () => Push(Intent.Start)); }
            else { Control(page, "next", "進む", BindMove(new Move(MoveKind.Next))); }

            Control(page, "title", "タイトルへ", () => Push(Intent.Title));
        }

        private Action BindMove(Move move) => BindMove(() => move);

        private Action BindMove(Func<Move> move)
        {
            var flow = Flow;
            var revision = _revision;
            var generation = flow.Generation;
            return () =>
            {
                if (!EvidenceCapture.IsRunning && Flow == flow)
                {
                    flow.Push(Intent.Move, revision, move(), generation);
                }
            };
        }

        private void Push(Intent intent)
        {
            if (!EvidenceCapture.IsRunning) { Flow.Push(intent); }
        }

        private static string SeatName(int seat) => seat == 0 ? "あなた" : $"CPU {seat}";
        private static string TileName(int type) => Tiles.Honor(type) ? "東南西北白發中"[type - 27].ToString()
            : (type % 9 + 1).ToString() + "萬筒索"[type / 9];

        private VisualElement Tile(VisualElement parent, int type, bool small = false)
        {
            var label = new VisualElement { tooltip = TileName(type) };
            parent.Add(label);
            StyleTile(label, type, small);
            return label;
        }

        private static void SetPlayable(Button button, bool enabled)
        {
            button.SetEnabled(enabled);
            button.style.opacity = 1f;
            button.style.unityBackgroundImageTintColor = enabled ? Color.white : new Color(0.79f, 0.82f, 0.76f);
        }

        private static void Pulse(VisualElement element)
        {
            if (EvidenceCapture.IsRunning || Application.isBatchMode) { return; }

            element.style.opacity = 0.66f;
            element.schedule.Execute(() => element.style.opacity = 1f).StartingIn(105);
        }

        private void StyleTile(VisualElement tile, int type, bool small)
        {
            tile.style.backgroundColor = Color.clear;
            tile.style.backgroundImage = Background.FromSprite(_tileSprites[type]);
            tile.style.color = type is 33 or < 9 ? new Color(0.52f, 0.16f, 0.13f)
                : type is >= 18 and < 27 or 32 ? Felt : TileBlue;
            tile.style.width = small ? 25 : 49;
            tile.style.height = small ? 31 : 61;
            tile.style.unityTextAlign = TextAnchor.MiddleCenter;
            tile.style.unityFontStyleAndWeight = FontStyle.Bold;
            tile.style.borderTopLeftRadius = tile.style.borderTopRightRadius = tile.style.borderBottomLeftRadius =
                tile.style.borderBottomRightRadius = small ? 3 : 6;
            tile.style.marginRight = 2;
            tile.style.marginBottom = 2;
            tile.style.paddingLeft = tile.style.paddingRight = 0;
        }

        private static VisualElement Box(VisualElement parent, Color color)
        {
            var box = new VisualElement();
            box.style.backgroundColor = color;
            parent.Add(box);
            return box;
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            parent.Add(row);
            return row;
        }

        private static Label Label(VisualElement parent, string text, int size)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            parent.Add(label);
            return label;
        }

        private static void Control(VisualElement parent, string name, string text, Action callback)
        {
            var button = new Button(callback) { name = name, text = text };
            button.style.backgroundColor = Linen;
            button.style.color = Ink;
            button.style.fontSize = 16;
            button.style.minHeight = 34;
            button.style.minWidth = 155;
            button.style.marginTop = 6;
            button.style.marginBottom = 2;
            parent.Add(button);
        }
    }
}
