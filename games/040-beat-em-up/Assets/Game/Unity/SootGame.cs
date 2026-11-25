using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>画面、入力、音、カメラ。描画は Core のその Tick の状態をそのまま置く。</summary>
    public sealed class SootGame : MonoBehaviour
    {
        /// <summary>カメラの中心の高さ（px）。地面の手前の端が画面の下から 20 px に来る。</summary>
        public const float CameraY = 88f;

        private static int _bootSeed;
        private static bool _booted;

        [SerializeField] private SootSettings _settings;
        [SerializeField] private TextAsset _moves;
        [SerializeField] private TextAsset _stageText;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private SootStage _stage;

        private readonly List<Intent> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly CombatFeedback _feedback = new();
        private BrawlConfig _config;
        private AudioSource _audio;
        private AudioClip[] _clips;
        private Font _font;
        private VisualElement _root;
        private int _hudVersion = -1;
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -1;

        private VisualElement _status;
        private VisualElement _healthFill;
        private Label _lives;
        private Label _score;
        private Label _section;
        private VisualElement _bossPanel;
        private VisualElement _bossFill;
        private Label _go;
        private Label _guide;
        private VisualElement _menu;
        private Label _banner;
        private Label _body;
        private Button _primary;
        private Button _secondary;

        private BrawlScreen _shownScreen = (BrawlScreen)(-1);
        private int _shownHealth = int.MinValue;
        private int _shownLives = int.MinValue;
        private long _shownScore = long.MinValue;
        private int _shownSection = int.MinValue;
        private int _shownBoss = int.MinValue;
        private int _shownGo = -1;
        private Outcome _shownOutcome = (Outcome)(-1);
        private float _shownAspect = -1f;

        public BrawlFlow Flow { get; private set; }

        public SootSettings Settings => _settings;

        public SootStage Stage => _stage;

        public VisualElement HudRoot => _root;

        public Camera View => _camera;

        /// <summary>試合の設定（検証済み、描画が毎フレーム使う）。</summary>
        public BrawlConfig Rules => _config;

        private void Awake()
        {
            _config = _settings.Validate();
            if (!_booted)
            {
                _bootSeed = Environment.TickCount;
                if (_bootSeed == 0) { _bootSeed = 1; }
                _booted = true;
            }

            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : _bootSeed;
            Flow = new BrawlFlow(MoveTable.Parse(_moves.text), StageData.Parse(_stageText.text), _config, seed);
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _clips = new[]
            {
                Tone("Swing", 520f, 0.03f, 0.03f),
                Tone("Hit", 110f, 0.07f, 0.05f),
                Tone("Hurt", 80f, 0.1f, 0.05f),
                Tone("Down", 60f, 0.14f, 0.05f),
                Tone("Defeat", 260f, 0.12f, 0.04f),
                Tone("Grab", 340f, 0.05f, 0.03f),
                Tone("Special", 700f, 0.18f, 0.03f),
                Tone("Death", 50f, 0.35f, 0.05f),
                Tone("Go", 880f, 0.2f, 0.03f),
                Tone("Heal", 660f, 0.22f, 0.03f),
            };
            _camera.orthographic = true;
            _camera.backgroundColor = new Color(0.08f, 0.06f, 0.1f);

            Track(IntentKind.LeftDown, IntentKind.LeftUp, (Key.A, "<Keyboard>/a"), (Key.LeftArrow, "<Keyboard>/leftArrow"));
            Track(IntentKind.RightDown, IntentKind.RightUp, (Key.D, "<Keyboard>/d"), (Key.RightArrow, "<Keyboard>/rightArrow"));
            Track(IntentKind.DownDown, IntentKind.DownUp, (Key.S, "<Keyboard>/s"), (Key.DownArrow, "<Keyboard>/downArrow"));
            Track(IntentKind.UpDown, IntentKind.UpUp, (Key.W, "<Keyboard>/w"), (Key.UpArrow, "<Keyboard>/upArrow"));
            Track(IntentKind.AttackDown, IntentKind.AttackUp, (Key.J, "<Keyboard>/j"));
            Track(IntentKind.JumpDown, IntentKind.JumpUp, (Key.K, "<Keyboard>/k"));
            Track(IntentKind.SpecialDown, IntentKind.SpecialUp, (Key.L, "<Keyboard>/l"));
            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.Cancel, "<Keyboard>/escape");
            Bind(IntentKind.Back, "<Keyboard>/backspace");
            Bind(IntentKind.Overlay, "<Keyboard>/f1");
            foreach (var action in _actions) { action.Enable(); }

            _font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic", "Yu Gothic UI", "Meiryo", "Arial" }, 28);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            if (_clips == null) { return; }
            foreach (var clip in _clips) { Destroy(clip); }
        }

        /// <summary>フォーカスの変化。撮影中は止めない。テストからも同じ経路を使う。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            Flow.SetFocus(focus);
            _intents.Clear();
            if (focus) { _focusRestoredFrame = Time.frameCount; }
        }

        private void OnApplicationFocus(bool focus) => SetFocus(focus);

        public void Render()
        {
            if (Flow == null) { return; }
            var brawl = Flow.Brawl;
            if (brawl == null) { _stage.ShowTitle(_config, Flow.Stage); }
            else { _stage.Show(brawl, _config, Flow.ShowBoxes); }
            PlaceCamera(brawl);
            RenderHud();
            PlaySounds();
        }

        private void PlaceCamera(Brawl brawl)
        {
            var aspect = _camera.aspect;
            if (!Mathf.Approximately(aspect, _shownAspect))
            {
                _shownAspect = aspect;
                var half = _config.ViewWidth / (float)Px.One / 2f;
                _camera.orthographicSize = Mathf.Max(108f, half / Mathf.Max(0.1f, aspect));
            }

            var left = brawl == null ? Flow.Stage.Sections[0].CameraX : brawl.CameraX;
            var x = (left + (_config.ViewWidth / 2)) / (float)Px.One;
            var position = new Vector3(x, CameraY, -10f);
            if (_camera.transform.position != position) { _camera.transform.position = position; }
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Track(IntentKind down, IntentKind up, params (Key Key, string Path)[] keys)
        {
            var bindings = new string[keys.Length];
            for (var i = 0; i < keys.Length; i++) { bindings[i] = keys[i].Path; }
            var action = NewAction(down.ToString(), InputActionType.PassThrough, bindings);
            action.performed += context =>
            {
                if (context.control is not KeyControl control) { return; }
                for (var i = 0; i < keys.Length; i++)
                {
                    if (keys[i].Key != control.keyCode) { continue; }
                    Queue(new Intent(control.isPressed ? down : up, EventTime(context), 1 << i));
                    return;
                }
            };
        }

        private void Bind(IntentKind kind, params string[] bindings)
        {
            var action = NewAction(kind.ToString(), InputActionType.Button, bindings);
            action.performed += context => Queue(new Intent(kind, EventTime(context)));
        }

        private static double EventTime(InputAction.CallbackContext context) => Math.Min(context.time, Time.realtimeSinceStartupAsDouble);

        private InputAction NewAction(string name, InputActionType type, params string[] bindings)
        {
            var action = new InputAction(name, type);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            _actions.Add(action);
            return action;
        }

        private void Queue(Intent intent)
        {
            if (AcceptInput()) { _intents.Add(intent); }
        }

        private void Update()
        {
            if (AcceptInput())
            {
                var restored = Time.frameCount <= _focusRestoredFrame + 1;
                for (var i = 0; i < _intents.Count; i++)
                {
                    if (restored && _intents[i].Kind == IntentKind.Submit) { continue; }
                    Flow.Apply(_intents[i]);
                }
            }

            _intents.Clear();
            if (!EvidenceCapture.IsRunning && Time.timeScale > 0f) { Flow.Advance(Time.realtimeSinceStartupAsDouble); }
            else { Flow.EndFrame(); }
            Render();
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _hudVersion == version) { return; }
            _root = root;
            _hudVersion = version;
            root.Clear();
            root.style.unityFont = _font;
            _shownScreen = (BrawlScreen)(-1);
            _shownHealth = int.MinValue;
            _shownLives = int.MinValue;
            _shownScore = long.MinValue;
            _shownSection = int.MinValue;
            _shownBoss = int.MinValue;
            _shownGo = -1;
            _shownOutcome = (Outcome)(-1);
            BuildHud(root);
            RenderHud();
        }

        private void BuildHud(VisualElement root)
        {
            _status = Box("status");
            _status.style.position = Position.Absolute;
            _status.style.left = 28;
            _status.style.top = 20;
            var top = Row();
            Label(top, "name", 26).text = Flow.Table[ActorKind.Player].Name;
            _lives = Label(top, "lives", 24);
            _lives.style.marginLeft = 18;
            _status.Add(top);
            _healthFill = Bar(_status, "health", 420, new Color(0.95f, 0.78f, 0.25f));
            _score = Label(_status, "score", 22);
            root.Add(_status);

            _section = Label(root, "section", 22);
            _section.style.position = Position.Absolute;
            _section.style.left = Length.Percent(44);
            _section.style.top = 22;

            _bossPanel = Box("boss");
            _bossPanel.style.position = Position.Absolute;
            _bossPanel.style.right = 28;
            _bossPanel.style.top = 20;
            Label(_bossPanel, "boss-name", 24).text = Flow.Table[ActorKind.Boss].Name;
            _bossFill = Bar(_bossPanel, "boss-health", 420, new Color(0.9f, 0.25f, 0.3f));
            root.Add(_bossPanel);

            _go = Label(root, "go", 72);
            _go.text = "GO →";
            _go.style.position = Position.Absolute;
            _go.style.right = 60;
            _go.style.top = Length.Percent(36);
            _go.style.color = new Color(1f, 0.85f, 0.3f);
            _go.style.unityFontStyleAndWeight = FontStyle.Bold;

            _guide = Label(root, "guide", 20);
            _guide.text = "移動 矢印/WASD   J 攻撃   K ジャンプ   L 緊急技（体力を消費）   敵へ歩いて掴み   Esc 一時停止   F1 判定";
            _guide.style.position = Position.Absolute;
            _guide.style.left = 28;
            _guide.style.bottom = 14;
            _guide.style.color = new Color(0.85f, 0.85f, 0.9f);

            _menu = Box("menu");
            _menu.style.position = Position.Absolute;
            _menu.style.left = Length.Percent(22);
            _menu.style.right = Length.Percent(22);
            _menu.style.top = Length.Percent(18);
            _menu.style.backgroundColor = new Color(0.06f, 0.05f, 0.09f, 0.9f);
            _menu.style.paddingTop = 26;
            _menu.style.paddingBottom = 26;
            _menu.style.alignItems = Align.Center;
            _banner = Label(_menu, "banner", 44);
            _banner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _body = Label(_menu, "body", 22);
            _body.style.whiteSpace = WhiteSpace.Normal;
            _body.style.unityTextAlign = TextAnchor.MiddleLeft;
            _body.style.marginTop = 14;
            _body.style.marginLeft = 40;
            _body.style.marginRight = 40;
            _primary = Button(_menu, "primary");
            _primary.style.backgroundColor = new Color(1f, 0.74f, 0.3f);
            _primary.style.color = new Color(0.1f, 0.07f, 0.06f);
            _primary.style.unityFontStyleAndWeight = FontStyle.Bold;
            _secondary = Button(_menu, "secondary");
            _secondary.style.backgroundColor = new Color(0.18f, 0.15f, 0.22f);
            _secondary.style.color = Color.white;
            _primary.clicked += () => Queue(new Intent(IntentKind.Submit, Time.realtimeSinceStartupAsDouble));
            _secondary.clicked += () => Queue(new Intent(Flow.Screen == BrawlScreen.Howto ? IntentKind.Cancel : IntentKind.Back, Time.realtimeSinceStartupAsDouble));
            root.Add(_menu);
        }

        private void RenderHud()
        {
            if (_root == null || Flow == null) { return; }
            var screen = Flow.Screen;
            var brawl = Flow.Brawl;
            var playing = brawl != null && (screen == BrawlScreen.Playing || screen == BrawlScreen.Paused || screen == BrawlScreen.Result);
            var health = playing ? brawl.Player.Health : int.MinValue;
            var lives = playing ? brawl.Lives : int.MinValue;
            var score = playing ? brawl.Score : long.MinValue;
            var section = playing ? brawl.SectionIndex : int.MinValue;
            var boss = playing ? BossHealth(brawl) : int.MinValue;
            var go = playing && screen == BrawlScreen.Playing && GoVisible(brawl) ? 1 : 0;
            var outcome = brawl?.Outcome ?? (Outcome)(-1);
            if (screen == _shownScreen && health == _shownHealth && lives == _shownLives && score == _shownScore && section == _shownSection && boss == _shownBoss && go == _shownGo && outcome == _shownOutcome)
            {
                return;
            }

            var screenChanged = screen != _shownScreen || outcome != _shownOutcome;
            _shownScreen = screen;
            _shownHealth = health;
            _shownLives = lives;
            _shownScore = score;
            _shownSection = section;
            _shownBoss = boss;
            _shownGo = go;
            _shownOutcome = outcome;

            Show(_status, playing);
            Show(_section, playing);
            Show(_guide, playing && screen == BrawlScreen.Playing);
            Show(_go, go == 1);
            Show(_bossPanel, playing && boss >= 0);
            if (playing)
            {
                var def = brawl.Player.Def;
                _healthFill.style.width = Length.Percent(100f * Mathf.Max(0, health) / def.Health);
                _lives.text = "残機 ×" + Mathf.Max(0, lives - 1);
                _score.text = "SCORE " + score;
                _section.text = section + 1 >= Flow.Stage.Sections.Count ? "最後の区画" : "区画 " + (section + 1) + " / " + Flow.Stage.Sections.Count;
                if (boss >= 0) { _bossFill.style.width = Length.Percent(100f * boss / Flow.Table[ActorKind.Boss].Health); }
            }

            if (!screenChanged) { return; }
            Show(_menu, screen != BrawlScreen.Playing);
            _banner.style.fontSize = screen == BrawlScreen.Title ? 84 : 44;
            switch (screen)
            {
                case BrawlScreen.Title:
                    _banner.text = "SOOT ALLEY";
                    _body.text = "路地にたむろする一味を片付けて、奥にいる親玉ガランを倒せ。";
                    SetButtons("はじめる（Enter）", string.Empty);
                    break;
                case BrawlScreen.Howto:
                    _banner.text = "操作説明";
                    _body.text =
                        "矢印 / WASD　移動（↑↓ で奥と手前）\n" +
                        "J　攻撃（当たると 3 段まで繋がる）\n" +
                        "K　ジャンプ（空中で J：跳び蹴り）\n" +
                        "L　緊急技（体力を払って周りを吹き飛ばす）\n" +
                        "敵へ歩いて触れると掴む：J で膝、3 回目か ←→＋J で投げ\n" +
                        "奥行きがずれていると攻撃は当たらない（足元の影をそろえる）\n" +
                        "敵が落とした肉を拾うと体力が回復\n" +
                        "敵を全滅させると GO →。Esc 一時停止、F1 判定箱";
                    SetButtons("スタート（Enter）", "もどる");
                    break;
                case BrawlScreen.Paused:
                    _banner.text = "一時停止";
                    _body.text = "Enter / Esc で再開、Backspace でタイトルへ。";
                    SetButtons("再開", "タイトルへ");
                    break;
                case BrawlScreen.Result:
                    var cleared = brawl != null && brawl.Outcome == Outcome.Cleared;
                    _banner.text = cleared ? "STAGE CLEAR" : "GAME OVER";
                    _body.text = brawl == null ? string.Empty :
                        "SCORE " + brawl.Score + "\n倒した敵 " + brawl.Defeats + "　残機 " + Mathf.Max(0, brawl.Lives - 1) + "\n時間 " + (brawl.Tick / 60) + " 秒";
                    SetButtons("もう一度（Enter）", "タイトルへ");
                    break;
            }
        }

        /// <summary>「GO →」を出す Tick か。区画が終わって先へ進める間、20 Tick ごとに点滅する。</summary>
        public static bool GoVisible(Brawl brawl) =>
            brawl != null && !brawl.Locked && brawl.Outcome == Outcome.Running && ((brawl.Tick / 20) & 1) == 0;

        private static int BossHealth(Brawl brawl)
        {
            for (var i = 1; i < brawl.SlotCount; i++)
            {
                var a = brawl.At(i);
                if (a.Active && a.Kind == ActorKind.Boss) { return Mathf.Max(0, a.Health); }
            }

            return -1;
        }

        private void PlaySounds()
        {
            var cue = _feedback.Observe(Flow.Brawl);
            if (AudioPolicy.ShouldMute || cue == CombatCue.None) { return; }
            _audio.volume = _settings.Volume;
            for (var i = 0; i < _clips.Length; i++)
            {
                if (((int)cue & (1 << i)) != 0) { _audio.PlayOneShot(_clips[i]); }
            }
        }

        private void SetButtons(string primary, string secondary)
        {
            _primary.text = primary;
            Show(_primary, primary.Length > 0);
            _secondary.text = secondary;
            Show(_secondary, secondary.Length > 0);
        }

        private static void Show(VisualElement element, bool shown)
        {
            var display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display.value != display || element.style.display.keyword != StyleKeyword.Undefined) { element.style.display = display; }
        }

        private static VisualElement Bar(VisualElement parent, string name, int width, Color color)
        {
            var frame = new VisualElement { name = name };
            frame.style.width = width;
            frame.style.height = 20;
            frame.style.marginTop = 4;
            frame.style.marginBottom = 4;
            frame.style.backgroundColor = new Color(0.16f, 0.1f, 0.12f);
            frame.style.borderBottomWidth = 2;
            frame.style.borderBottomColor = new Color(0f, 0f, 0f, 0.6f);
            var fill = new VisualElement { name = name + "-fill" };
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(100);
            fill.style.backgroundColor = color;
            frame.Add(fill);
            parent.Add(frame);
            return fill;
        }

        private static Label Label(VisualElement parent, string name, int size)
        {
            var label = new Label(" ") { name = name };
            label.style.fontSize = size;
            label.style.color = Color.white;
            label.pickingMode = PickingMode.Ignore;
            parent.Add(label);
            return label;
        }

        private static Button Button(VisualElement parent, string name)
        {
            var button = new Button { name = name, text = " " };
            button.focusable = false;
            button.style.marginTop = 14;
            button.style.fontSize = 24;
            button.style.minWidth = 300;
            parent.Add(button);
            return button;
        }

        private static VisualElement Box(string name)
        {
            var element = new VisualElement { name = name, pickingMode = PickingMode.Position };
            element.style.flexDirection = FlexDirection.Column;
            return element;
        }

        private static VisualElement Row()
        {
            var element = new VisualElement();
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.FlexEnd;
            element.pickingMode = PickingMode.Ignore;
            return element;
        }

        private static AudioClip Tone(string name, float frequency, float seconds, float amplitude)
        {
            const int Rate = 22050;
            var samples = Mathf.Max(1, (int)(Rate * seconds));
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var f = frequency * (1f - (0.4f * i / samples));
                var wave = ((i * f / Rate) % 1f) < 0.5f ? amplitude : -amplitude;
                var env = 1f - (i / (float)samples);
                data[i] = wave * env;
            }

            var clip = AudioClip.Create(name, samples, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
