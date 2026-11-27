#nullable enable
using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    public sealed class FortGame : MonoBehaviour
    {
        [SerializeField] private PanelRenderer _panel = null!;
        [SerializeField] private FortSettings _settings = null!;
        [SerializeField] private StyleSheet _style = null!;
        private VisualElement? _root;
        private Font? _font;
        private int _focusFrame = -1;

        public BattleFlow Flow { get; private set; } = null!;
        public VisualElement? Root => _root;

        private void Awake()
        {
            var seed = EvidenceCapture.IsRunning ? (uint)EvidenceCapture.Seed : unchecked((uint)Environment.TickCount);
            ResetBattle(seed);
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo", "MS Gothic" }, 32);
            _panel.RegisterUIReloadCallback(OnReload);
        }

        public void ResetBattle(uint seed)
        {
            if (!float.IsFinite(_settings.EnemyInterval) || _settings.EnemyInterval <= 0f)
            {
                throw new InvalidOperationException("敵の演出間隔が不正です。");
            }

            Flow = new BattleFlow(seed, _settings.Units);
            Render();
        }

        private void OnReload(PanelRenderer panel, VisualElement root, int version)
        {
            _root = root.Q("hud");
            _root.styleSheets.Add(_style);
            if (_font != null)
            {
                _root.style.unityFont = _font;
                _root.style.unityFontDefinition = FontDefinition.FromFont(_font);
            }

            Render();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (EvidenceCapture.IsRunning || Flow == null) { return; }
            _focusFrame = Time.frameCount;
            Flow.SetFocus(focused);
        }

        private void Update()
        {
            if (EvidenceCapture.IsRunning) { return; }
            if (Flow.Advance(Time.unscaledDeltaTime, _settings.EnemyInterval)) { Render(); }
            var left = Mouse.current?.leftButton;
            if (Time.frameCount > _focusFrame && (left == null ||
                (!left.isPressed && !left.wasPressedThisFrame && !left.wasReleasedThisFrame)))
            {
                Flow.PointerIdle();
            }
        }

        private void OnDestroy() => Destroy(_font);

        public void Render()
        {
            if (_root == null) { return; }
            _root.Clear();
            if (Flow.Screen == Game.App.Screen.Battle)
            {
                RenderBattle(_root);
                return;
            }

            var landing = Box(_root, "landing");
            var outcome = Flow.Battle.Result;
            Label(landing, Flow.Screen switch
            {
                Game.App.Screen.Help => "作戦の進め方",
                Game.App.Screen.Result when outcome == Result.Won => "砦を制圧した",
                Game.App.Screen.Result => "部隊が全滅した",
                _ => "霧の砦",
            }, "title");
            if (Flow.Screen == Game.App.Screen.Help)
            {
                Label(landing, "味方を選択 → 青いマスへ移動 → 赤枠の敵をクリック。\n林の移動コストは2、岩とユニットは通行不可。\n弓は2マス先まで攻撃できます。\n敵指揮官を倒せば勝利。味方全滅で敗北です。", "body");
            }
            else if (Flow.Screen == Game.App.Screen.Title)
            {
                Label(landing, "地形を読み、4人の部隊で敵指揮官を倒す。", "body");
            }

            Button(landing, "start", Flow.Screen == Game.App.Screen.Result ? "再戦" : "戦闘開始", Flow.Start);
            if (Flow.Screen == Game.App.Screen.Title) { Button(landing, "help", "遊び方", Flow.Help); }
            else { Button(landing, "title", "タイトルへ", Flow.Title); }
        }

        private void RenderBattle(VisualElement root)
        {
            var state = Flow.Battle;
            var layout = Box(root, "battle");
            var board = Box(layout, "board");
            var info = Box(layout, "info");
            var movement = Flow.Movement;
            var preview = new Label { name = "preview" };
            void ClearPreview()
            {
                foreach (var tile in board.Query<Button>(className: "path").ToList()) { tile.RemoveFromClassList("path"); }
                preview.text = "";
            }

            board.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                if (evt.target == board) { ClearPreview(); }
            });
            for (var y = 0; y < Battle.Width; y++)
            {
                var row = Box(board, "row");
                for (var x = 0; x < Battle.Width; x++)
                {
                    var cell = y * Battle.Width + x;
                    var occupant = state.Occupant(cell);
                    var tile = Button(row, $"cell-{cell}", occupant < 0 ? "" : $"{BattleFlow.NameOf(occupant)}\n{state.Units[occupant].Hp}", () => Flow.ClickCell(cell));
                    tile.AddToClassList("tile");
                    tile.AddToClassList(Battle.Terrain(cell) switch
                    {
                        Ground.Woods => "woods",
                        Ground.Rock => "rock",
                        _ => "plain",
                    });
                    if (occupant >= 0)
                    {
                        tile.AddToClassList(occupant < Battle.AllyCount ? "ally" : "enemy");
                        if (state.Units[occupant].Done && Battle.Team(occupant) == state.Turn) { tile.AddToClassList("spent"); }
                        if (occupant == Flow.Selected) { tile.AddToClassList("selected"); }
                    }

                    if (movement?.Reaches(cell) == true && occupant < 0) { tile.AddToClassList("reachable"); }
                    if (state.CanAttack(Flow.Selected, occupant, Side.Ally)) { tile.AddToClassList("target"); }
                    int selected = Flow.Selected;
                    tile.RegisterCallback<PointerEnterEvent>(_ =>
                    {
                        ClearPreview();
                        if (movement?.Reaches(cell) == true)
                        {
                            foreach (var step in movement.Path(cell)) { board.Q<Button>($"cell-{step}").AddToClassList("path"); }
                        }

                        preview.text = state.CanAttack(selected, occupant, Side.Ally)
                            ? $"予測　{state.Units[selected].Power} ダメージ / 命中 {state.Units[selected].Accuracy}%"
                            : Battle.Terrain(cell) switch { Ground.Woods => "林　移動コスト 2", Ground.Rock => "岩　通行不可", _ => "平地　移動コスト 1" };
                    });
                }
            }

            Label(info, $"{state.Round} ターン目", "title");
            Label(info, state.Turn == Side.Ally ? "味方の手番" : "敵の手番", "phase");
            Label(info, "勝利条件　敵指揮官を撃破", "body");
            var selectedUnit = Flow.Selected >= 0 ? state.Units[Flow.Selected] : null;
            Label(info, selectedUnit == null ? "味方を選択" :
                $"{BattleFlow.NameOf(Flow.Selected)}　HP {selectedUnit.Hp}\n移動 {selectedUnit.Move}　射程 {selectedUnit.Range}\n威力 {selectedUnit.Power}　命中 {selectedUnit.Accuracy}%", "unit");
            preview.AddToClassList("preview");
            info.Add(preview);
            Label(info, Flow.Notice, "notice");
            Label(info, "青枠　移動先　　赤枠　攻撃対象", "body");
            Button(info, "end", "ターン終了", Flow.EndTurn).SetEnabled(state.Turn == Side.Ally);
            Button(info, "title", "タイトルへ", Flow.Title);
        }

        private Button Button(VisualElement parent, string id, string text, Func<bool> action)
        {
            var button = new Button(() => { if (!EvidenceCapture.IsRunning && action()) { Render(); } })
            {
                name = id,
                text = text,
            };
            parent.Add(button);
            return button;
        }

        private static VisualElement Box(VisualElement parent, string style)
        {
            var element = new VisualElement();
            element.AddToClassList(style);
            parent.Add(element);
            return element;
        }

        private static Label Label(VisualElement parent, string text, string style)
        {
            var element = new Label(text);
            element.AddToClassList(style);
            parent.Add(element);
            return element;
        }

    }
}
