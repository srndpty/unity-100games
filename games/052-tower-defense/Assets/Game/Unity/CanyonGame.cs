#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Screen = Game.App.Screen;

namespace Game.Unity
{
    public sealed class CanyonGame : MonoBehaviour
    {
        [SerializeField] private PanelRenderer _panel = null!;
        [SerializeField] private CanyonSettings _settings = null!;
        [SerializeField] private StyleSheet _style = null!;

        [SerializeField] private Texture2D _terrain = null!;
        [SerializeField] private Texture2D _atlas = null!;
        private readonly Sprite[] _icons = new Sprite[8];
        private VisualElement _layout = null!;
        private VisualElement _pause = null!;
        private VisualElement _effects = null!;

        private static readonly string[] _names = { "弓塔", "砲塔", "氷塔", "魔導塔" };
        private static readonly Color[] _colors =
        {
            new(0.3f, 0.65f, 0.43f), new(0.78f, 0.43f, 0.27f), new(0.34f, 0.66f, 0.82f), new(0.65f, 0.46f, 0.83f),
        };
        private const int Tile = 54;
        private const int FlightTicks = Defense.FlightTicks;
        private VisualElement? _root;
        private Font? _font;
        private Screen? _drawn;
        private Defense? _drawnBattle;
        private readonly Dictionary<int, (VisualElement Icon, VisualElement Health, VisualElement Fill)> _enemyViews = new();
        private readonly List<int> _expired = new();
        private Label _towerInfo = null!;
        private Label _waveInfo = null!;
        private VisualElement _board = null!;
        private VisualElement _units = null!;
        private VisualElement _range = null!;
        private Label _status = null!;
        private Label _notice = null!;
        private Button[] _slots = Array.Empty<Button>();
        private Button[] _kinds = Array.Empty<Button>();
        private Button _upgrade = null!;
        private Button _sell = null!;
        private Button _wave = null!;
        private Button _resume = null!;
        private int _focusFrame = -1;

        public Flow Flow { get; private set; } = null!;
        public VisualElement? Root => _root;
        public string CaptureRules => JsonUtility.ToJson(_settings.Rules);

        private void Awake()
        {
            Flow = new Flow(_settings.Rules);
            var width = _atlas.width / 4;
            var height = _atlas.height / 2;
            for (var i = 0; i < _icons.Length; i++)
            {
                _icons[i] = Sprite.Create(_atlas, new Rect(i % 4 * width, (1 - i / 4) * height, width, height),
                    new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            }
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo", "MS Gothic" }, 32);
            _panel.RegisterUIReloadCallback(OnReload);
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

            _drawn = null;
            Render();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            _focusFrame = Time.frameCount;
            Flow.Focus(focused);
        }

        private void Update()
        {
            if (EvidenceCapture.IsRunning) { return; }
            Flow.Advance(Time.unscaledDeltaTime);
            var button = Mouse.current?.leftButton;
            if (Time.frameCount > _focusFrame && (button == null ||
                (!button.isPressed && !button.wasPressedThisFrame && !button.wasReleasedThisFrame)))
            {
                Flow.PointerIdle();
            }

            Render();
        }

        private void OnDestroy()
        {
            Destroy(_font);
            foreach (var icon in _icons) { Destroy(icon); }
        }

        public void Render()
        {
            if (_root == null) { return; }
            if (_drawn != Flow.Screen || _drawnBattle != Flow.Defense)
            {
                _root.Clear();
                _drawn = Flow.Screen;
                _drawnBattle = Flow.Defense;
                _enemyViews.Clear();
                if (Flow.Screen == Screen.Play) { CreateBoard(_root); }
                else { Landing(_root); }
            }

            if (Flow.Screen == Screen.Play) { DrawBoard(); }
        }

        private void Landing(VisualElement root)
        {
            var panel = new VisualElement();
            panel.AddToClassList("landing");
            panel.style.backgroundImage = new StyleBackground(_terrain);
            root.Add(panel);
            panel = Group(panel, "landing-card");
            Label(panel, Flow.Screen switch
            {
                Screen.Help => "関門の守り方",
                Screen.Result when Flow.Defense.Phase == Phase.Won => "峡谷を守り抜いた",
                Screen.Result => "関門が突破された",
                _ => "峡谷の関門",
            }, "heading").name = "page-heading";
            Label(panel, Flow.Screen switch
            {
                Screen.Help => "建設枠と塔の種類を選び、建設を押す。\nWaveはボタンで開始。戦闘中も建設・強化・売却が可能。\n道を抜けた敵1体につきライフが1減る。第10Waveまで守り抜こう。",
                Screen.Result => $"Wave {Flow.Defense.Wave} / 10　残りライフ {Flow.Defense.Lives}",
                _ => "4種類の塔を配置し、10回の進軍を迎え撃つ。",
            }, "copy").name = "page-summary";
            Button(panel, "start", Flow.Screen == Screen.Result ? "もう一度守る" : "防衛開始", Intent.Start);
            if (Flow.Screen == Screen.Title) { Button(panel, "help", "遊び方", Intent.Help); }
            else { Button(panel, "title", "タイトルへ", Intent.Title); }
        }

        private void CreateBoard(VisualElement root)
        {
            var layout = _layout = new VisualElement();
            layout.AddToClassList("layout");
            root.Add(layout);
            _visualTick = -1;
            _board = new VisualElement { name = "board" };
            _board.AddToClassList("board");
            layout.style.backgroundImage = new StyleBackground(_terrain);
            layout.Add(_board);
            for (var progress = 0; progress <= Canyon.Length; progress += Canyon.Scale)
            {
                var road = new VisualElement();
                road.AddToClassList("road");
                Place(road, Canyon.At(progress), Tile);
                _board.Add(road);
            }

            Mark("入口", Canyon.At(0));
            Mark("関門", Canyon.At(Canyon.Length));
            var gate = new VisualElement { pickingMode = PickingMode.Ignore };
            gate.style.backgroundImage = new StyleBackground(_icons[7]);
            Place(gate, Canyon.At(Canyon.Length), 48);
            _board.Add(gate);
            _range = new VisualElement { pickingMode = PickingMode.Ignore };
            _range.AddToClassList("range");
            _board.Add(_range);
            _slots = new Button[Canyon.Slots.Count];
            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = Button(_board, $"slot-{i}", "", Intent.Slot, i);
                slot.AddToClassList("slot");
                Label(slot, "+", "slot-tag").pickingMode = PickingMode.Ignore;
                Place(slot, Canyon.Slots[i], 44);
                _slots[i] = slot;
            }

            _units = new VisualElement { pickingMode = PickingMode.Ignore };
            _units.style.position = Position.Absolute;
            _board.Add(_units);
            _effects = Group(_board, "effects");
            _effects.pickingMode = PickingMode.Ignore;
            var sidebar = new VisualElement();
            sidebar.AddToClassList("sidebar");
            layout.Add(sidebar);
            Label(sidebar, "峡谷の関門", "heading");
            _status = Label(sidebar, "", "status");
            var kinds = Group(sidebar, "choice-group");
            Label(kinds, "塔の種類", "section-heading");
            var grid = Group(kinds, "kind-grid");
            _kinds = new Button[_names.Length];
            for (var i = 0; i < _kinds.Length; i++)
            {
                var kind = _kinds[i] = Button(grid, $"kind-{i}", "", Intent.Kind, i);
                var icon = Group(kind, "kind-icon");
                icon.pickingMode = PickingMode.Ignore;
                icon.style.backgroundImage = new StyleBackground(_icons[i]);
            }

            _towerInfo = Label(kinds, "", "tactical-info");
            var actions = Group(sidebar, "action-group");
            Label(actions, "選んだ枠の操作", "section-heading");
            Button(actions, "build", "建設", Intent.Build).AddToClassList("primary");
            _upgrade = Button(actions, "upgrade", "強化", Intent.Upgrade);
            _sell = Button(actions, "sell", "売却", Intent.Sell);
            _notice = Label(sidebar, "", "notice");
            _waveInfo = Label(sidebar, "", "tactical-info");
            _wave = Button(sidebar, "wave", "", Intent.Wave);
            _wave.AddToClassList("primary");
            Button(sidebar, "title", "タイトルへ", Intent.Title);

            _pause = Group(root, "pause-scrim");
            _pause.name = "pause-overlay";
            var pauseCard = Group(_pause, "pause-card");
            Label(pauseCard, "一時停止", "heading");
            Label(pauseCard, "防衛は停止しています。再開すると続きから遊べます。", "pause-copy");
            _resume = Button(pauseCard, "resume", "防衛を再開", Intent.Resume);
            _resume.AddToClassList("primary");
        }

        private int _visualTick = -1;
        private Phase? _visualPhase;
        private void DrawBoard()
        {
            var state = Flow.Defense;
            _status.text = $"Wave {state.Wave} / 10　　ライフ {state.Lives}\n資金 {state.Money}";
            var selected = Flow.Slot >= 0 ? state.Towers[Flow.Slot] : null;
            for (var i = 0; i < _slots.Length; i++)
            {
                var tower = state.Towers[i];
                var slot = _slots[i];
                slot.EnableInClassList("selected", i == Flow.Slot);
                slot.Q<Label>(className: "slot-tag").text = tower == null ? "+" : $"Lv{tower.Level}";
                slot.EnableInClassList("occupied", tower != null);
                slot.style.backgroundImage = tower == null ? StyleKeyword.None : new StyleBackground(_icons[(int)tower.Kind]);
                slot.style.backgroundColor = tower == null ? new Color(0.24f, 0.33f, 0.30f) :
                    state.Phase == Phase.Running && tower.ShotTick >= state.Tick - 2 ? Color.white : _colors[(int)tower.Kind];
            }

            for (var i = 0; i < _kinds.Length; i++)
            {
                _kinds[i].text = $"{_names[i]}　{state.Cost((TowerKind)i)}";
                _kinds[i].EnableInClassList("selected", i == (int)Flow.Kind);
            }

            _range.style.display = Flow.Slot < 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (Flow.Slot >= 0)
            {
                var diameter = state.Range(selected?.Kind ?? Flow.Kind) * (2f * Tile / Canyon.Scale);
                Place(_range, Canyon.Slots[Flow.Slot], diameter);
                var radius = diameter * 0.5f;
                _range.style.borderTopLeftRadius = radius;
                _range.style.borderTopRightRadius = radius;
                _range.style.borderBottomLeftRadius = radius;
                _range.style.borderBottomRightRadius = radius;
            }

            var infoKind = selected?.Kind ?? Flow.Kind;
            var level = selected?.Level ?? 1;
            var trait = infoKind switch
            {
                TowerKind.Bow => "高速の単体攻撃",
                TowerKind.Cannon => "周囲0.8マスにも攻撃",
                TowerKind.Frost => "速度半減・1秒",
                _ => "重装の防御を無視",
            };
            _towerInfo.text = $"{_names[(int)infoKind]} Lv{level}　威力 {state.Power(infoKind, level)}\n"
                + $"射程 {state.Range(infoKind) / (float)Canyon.Scale:0.0}　間隔 {state.Cooldown(infoKind) / (float)Defense.TickRate:0.0#}秒\n{trait}";
            _waveInfo.text = state.Phase == Phase.Preparation
                ? $"通常 {state.WaveEnemies(EnemyKind.Soldier)}　速兵 {state.WaveEnemies(EnemyKind.Runner)}　重装 {state.WaveEnemies(EnemyKind.Armor)}"
                : "";
            _upgrade.text = selected == null ? "強化" : selected.Level == 3 ? "強化済み Lv3" : $"強化　{state.UpgradeCost(selected)}";
            _sell.text = selected == null ? "売却" : $"売却　+{Defense.Refund(selected)}";
            _notice.text = Flow.Paused ? "一時停止中。再開を押してください。" : Flow.Notice;
            _wave.text = state.Phase == Phase.Preparation ? $"Wave {state.Wave} 開始" : "進軍中";
            _wave.SetEnabled(!Flow.Paused && state.Phase == Phase.Preparation);
            _pause.style.display = Flow.Paused ? DisplayStyle.Flex : DisplayStyle.None;
            _layout.SetEnabled(!Flow.Paused);
            _resume.SetEnabled(Flow.Paused);
            if (_visualTick == state.Tick && _visualPhase == state.Phase) { return; }
            _visualTick = state.Tick;
            _visualPhase = state.Phase;
            DrawEffects(state);
            _expired.Clear();
            foreach (var pair in _enemyViews) { _expired.Add(pair.Key); }
            foreach (var enemy in state.Enemies)
            {
                _expired.Remove(enemy.Id);
                if (!_enemyViews.TryGetValue(enemy.Id, out var view))
                {
                    var icon = Group(_units, "enemy");
                    icon.pickingMode = PickingMode.Ignore;
                    icon.style.backgroundImage = new StyleBackground(_icons[4 + (int)enemy.Kind]);
                    var health = Group(_units, "health");
                    health.pickingMode = PickingMode.Ignore;
                    var fill = Group(health, "health-fill");
                    fill.pickingMode = PickingMode.Ignore;
                    view = (icon, health, fill);
                    _enemyViews.Add(enemy.Id, view);
                }

                view.Icon.EnableInClassList("slowed", enemy.SlowUntil >= state.Tick);
                var flashing = false;
                foreach (var hit in state.Hits)
                {
                    if (hit.EnemyId != enemy.Id) { continue; }
                    var delay = hit.Kind is TowerKind.Bow or TowerKind.Cannon ? FlightTicks : 0;
                    var hitAge = state.Tick - hit.Tick - delay;
                    if (hitAge is not (0 or 1 or 3)) { continue; }
                    flashing = true;
                    break;
                }

                view.Icon.EnableInClassList("hit", flashing);
                var size = enemy.Kind == EnemyKind.Armor ? 39 : 34;
                Place(view.Icon, enemy.Position, size);
                var center = Pixel(enemy.Position);
                view.Health.style.left = center.x - 12;
                view.Health.style.top = center.y - size / 2f - 5;
                view.Fill.style.width = Length.Percent(100f * enemy.Hp / enemy.MaxHp);
            }

            foreach (var id in _expired)
            {
                var view = _enemyViews[id];
                view.Icon.RemoveFromHierarchy();
                view.Health.RemoveFromHierarchy();
                _enemyViews.Remove(id);
            }
        }

        private void DrawEffects(Defense state)
        {
            _effects.Clear();
            if (state.Phase != Phase.Running) { return; }
            foreach (var shot in state.Shots)
            {
                var age = state.Tick - shot.Tick;
                var origin = Pixel(Canyon.Slots[shot.Slot]);
                var target = Pixel(shot.Target);
                if (shot.Kind is TowerKind.Bow or TowerKind.Cannon)
                {
                    foreach (var enemy in state.Enemies)
                    {
                        if (enemy.Id != shot.TargetId) { continue; }
                        target = Pixel(enemy.Position);
                        break;
                    }
                }

                var tint = _colors[(int)shot.Kind];
                if (shot.Kind is TowerKind.Bow or TowerKind.Cannon && age < FlightTicks)
                {
                    DrawProjectile(origin, target, shot.Kind, age / (float)FlightTicks);
                    continue;
                }

                if (shot.Kind == TowerKind.Arcane && age < 5)
                {
                    var beam = Effect("magic-beam", (origin + target) * 0.5f, (target - origin).magnitude, 5);
                    var delta = target - origin;
                    beam.style.rotate = new Rotate(new Angle(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg));
                    beam.style.backgroundColor = Color.Lerp(tint, Color.white, 0.4f);
                    beam.style.opacity = 1 - age / 5f;
                }

                var fraction = shot.Kind is TowerKind.Bow or TowerKind.Cannon ? (age - FlightTicks) / 3f : age / (float)(FlightTicks + 3);
                var size = Mathf.Lerp(10, shot.Kind == TowerKind.Cannon ? 86 : shot.Kind == TowerKind.Frost ? 53 : 32, fraction);
                var impact = Effect("impact", target, size, size);
                impact.style.backgroundColor = tint;
                impact.style.opacity = 0.8f * (1 - fraction);
            }
        }

        private void DrawProjectile(Vector2 origin, Vector2 target, TowerKind kind, float time)
        {
            var arcHeight = kind == TowerKind.Bow ? 45f : 27f;
            var position = Vector2.Lerp(origin, target, time);
            position.y -= 4 * arcHeight * time * (1 - time);
            if (kind == TowerKind.Cannon)
            {
                Effect("shell", position, 11, 11);
                return;
            }

            var tangent = target - origin;
            tangent.y -= 4 * arcHeight * (1 - 2 * time);
            var arrow = Effect("arrow", position, 23, 3);
            arrow.style.rotate = new Rotate(new Angle(Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg));
            Group(arrow, "arrow-head").pickingMode = PickingMode.Ignore;
            Group(arrow, "arrow-tail").pickingMode = PickingMode.Ignore;
        }

        private VisualElement Effect(string style, Vector2 center, float width, float height)
        {
            var element = Group(_effects, style);
            element.pickingMode = PickingMode.Ignore;
            element.style.position = Position.Absolute;
            element.style.left = center.x - width / 2;
            element.style.top = center.y - height / 2;
            element.style.width = width;
            element.style.height = height;
            return element;
        }

        private static Vector2 Pixel(Point point) =>
            new((point.X / (float)Canyon.Scale + 0.5f) * Tile, (point.Y / (float)Canyon.Scale + 0.5f) * Tile);

        private static VisualElement Group(VisualElement parent, string style)
        {
            var element = new VisualElement();
            element.AddToClassList(style);
            parent.Add(element);
            return element;
        }

        private void Mark(string text, Point point)
        {
            var mark = Label(_board, text, "mark");
            mark.pickingMode = PickingMode.Ignore;
            Place(mark, point, 50);
        }

        private static void Place(VisualElement element, Point center, float size)
        {
            element.style.position = Position.Absolute;
            element.style.left = (center.X / (float)Canyon.Scale + 0.5f) * Tile - size / 2;
            element.style.top = (center.Y / (float)Canyon.Scale + 0.5f) * Tile - size / 2;
            element.style.width = size;
            element.style.height = size;
        }

        private Button Button(VisualElement parent, string name, string text, Intent intent, int value = 0)
        {
            var button = new Button(() =>
            {
                if (!EvidenceCapture.IsRunning) { Flow.Push(intent, value); }
            })
            {
                name = name,
                text = text,
            };
            parent.Add(button);
            return button;
        }

        private static Label Label(VisualElement parent, string text, string style)
        {
            var label = new Label(text);
            label.AddToClassList(style);
            parent.Add(label);
            return label;
        }
    }
}
