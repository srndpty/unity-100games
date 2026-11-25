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
    /// <summary>画面、入力、音。描画は Core のその Tick の状態をそのまま置く。</summary>
    public sealed class EmberGame : MonoBehaviour
    {
        private static int _bootSeed;
        private static bool _booted;

        [SerializeField] private EmberSettings _settings;
        [SerializeField] private TextAsset _moves;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private EmberStage _stage;

        private readonly List<Intent> _intents = new();
        private readonly List<InputAction> _actions = new();
        private AudioSource _audio;
        private AudioClip _swing;
        private AudioClip _hit;
        private AudioClip _guard;
        private AudioClip _ko;
        private Font _font;
        private VisualElement _root;
        private int _hudVersion = -1;
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -1;
        private readonly CombatFeedback _feedback = new();
        private Label _banner;
        private Label _timer;
        private Label _stars;
        private VisualElement _guide;
        private Label _specialCommand;
        private readonly string[] _specialNames = new string[2];
        private VisualElement _selection;
        private readonly Button[] _characterCards = new Button[2];
        private readonly Label[] _characterRoles = new Label[2];
        private int _shownCharacter = -1;
        private VisualElement _fill0;
        private VisualElement _fill1;
        private float _fill0Width = -1f;
        private float _fill1Width = -1f;
        private VisualElement _fight;
        private VisualElement _menu;
        private Button _primary;
        private Button _secondary;
        private string _bannerText = string.Empty;
        private string _timerText = string.Empty;
        private string _starsText = string.Empty;
        private string _primaryText = string.Empty;
        private string _secondaryText = string.Empty;
        private DuelScreen _shownScreen = (DuelScreen)(-1);
        private int _shownHealth0 = int.MinValue;
        private int _shownHealth1 = int.MinValue;
        private int _shownSecond = int.MinValue;
        private int _shownRound = int.MinValue;

        public DuelFlow Flow { get; private set; }

        public EmberSettings Settings => _settings;

        public EmberStage Stage => _stage;

        public VisualElement HudRoot => _root;

        public Camera View => _camera;

        private void Awake()
        {
            var rules = _settings.Validate();
            if (!_booted)
            {
                _bootSeed = Environment.TickCount;
                if (_bootSeed == 0) { _bootSeed = 1; }
                _booted = true;
            }

            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : _bootSeed;
            Flow = new DuelFlow(MoveTable.Parse(_moves.text), rules, seed);
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _swing = Tone("Swing", 180f, 0.05f);
            _hit = Tone("Hit", 90f, 0.08f);
            _guard = Tone("Guard", 240f, 0.04f);
            _ko = Tone("Ko", 70f, 0.2f);
            _camera.orthographic = true;
            _camera.orthographicSize = 150f;
            _camera.transform.position = new Vector3(240f, 130f, -10f);
            _camera.backgroundColor = new Color(0.12f, 0.08f, 0.1f);

            Track(IntentKind.LeftDown, IntentKind.LeftUp, (Key.A, "<Keyboard>/a"), (Key.LeftArrow, "<Keyboard>/leftArrow"));
            Track(IntentKind.RightDown, IntentKind.RightUp, (Key.D, "<Keyboard>/d"), (Key.RightArrow, "<Keyboard>/rightArrow"));
            Track(IntentKind.DownDown, IntentKind.DownUp, (Key.S, "<Keyboard>/s"), (Key.DownArrow, "<Keyboard>/downArrow"));
            Track(IntentKind.UpDown, IntentKind.UpUp, (Key.W, "<Keyboard>/w"), (Key.UpArrow, "<Keyboard>/upArrow"));
            Track(IntentKind.LightDown, IntentKind.LightUp, (Key.J, "<Keyboard>/j"));
            Track(IntentKind.HeavyDown, IntentKind.HeavyUp, (Key.K, "<Keyboard>/k"));
            Track(IntentKind.SweepDown, IntentKind.SweepUp, (Key.L, "<Keyboard>/l"));
            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.Cancel, "<Keyboard>/escape");
            Bind(IntentKind.Back, "<Keyboard>/backspace");
            Bind(IntentKind.Overlay, "<Keyboard>/f1");
            foreach (var action in _actions) { action.Enable(); }

            _font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic", "Yu Gothic UI", "Arial" }, 28);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            Destroy(_swing);
            Destroy(_hit);
            Destroy(_guard);
            Destroy(_ko);
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
            if (Flow.Match == null) { _stage.ShowTitle(Flow.Config); }
            else { _stage.Show(Flow.Match.Duel, Flow.ShowBoxes); }
            RenderHud();
            PlaySounds();
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
            _shownScreen = (DuelScreen)(-1);
            _bannerText = string.Empty;
            _timerText = string.Empty;
            _starsText = string.Empty;
            _primaryText = string.Empty;
            _secondaryText = string.Empty;
            _fill0Width = -1f;
            _fill1Width = -1f;
            BuildHud(root);
            RenderHud();
        }

        private void BuildHud(VisualElement root)
        {
            _fight = Column("fight");
            _fight.style.flexDirection = FlexDirection.Row;
            _fight.style.justifyContent = Justify.SpaceBetween;
            _fight.style.marginTop = 16;
            _fight.style.marginLeft = 24;
            _fight.style.marginRight = 24;
            _fill0 = Bar(_fight, new Color(0.9f, 0.35f, 0.12f));
            _stars = Label(_fight, "stars", 22);
            _fill1 = Bar(_fight, new Color(0.35f, 0.55f, 0.95f));
            root.Add(_fight);

            _timer = Label(root, "timer", 36);
            _timer.style.unityTextAlign = TextAnchor.UpperCenter;
            _timer.style.marginTop = 4;
            _guide = Column("guide");
            _guide.pickingMode = PickingMode.Ignore;
            _guide.style.position = Position.Absolute;
            _guide.style.left = 24;
            _guide.style.top = 64;
            _guide.style.minWidth = 260;
            _guide.style.paddingLeft = 14;
            _guide.style.paddingRight = 14;
            _guide.style.paddingTop = 8;
            _guide.style.paddingBottom = 8;
            _guide.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 0.87f);
            var commandTitle = Label(_guide, "commands", 22);
            commandTitle.text = "コマンド";
            commandTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            commandTitle.style.marginBottom = 5;
            Label(_guide, "movement", 19).text = "移動・ジャンプ   矢印 / WASD";
            Label(_guide, "guard", 19).text = "ガード   相手と逆方向";
            Label(_guide, "light", 19).text = "弱攻撃   J";
            Label(_guide, "heavy", 19).text = "強攻撃   K";
            Label(_guide, "sweep", 19).text = "足払い   ↓ ＋ L";
            Label(_guide, "throw", 19).text = "投げ   J ＋ K";
            _specialCommand = Label(_guide, "special", 19);
            for (var i = 0; i < _specialNames.Length; i++)
            {
                _specialNames[i] = Flow.Table[i].Find("special").Name + "   ↓ ↘ → ＋ J（右向き）";
            }
            Label(_guide, "reverse", 17).text = "左向きでは ↓ ↙ ← ＋ J";
            Label(_guide, "pause", 16).text = "Esc  一時停止     F1  判定表示";
            root.Add(_guide);

            _menu = Column("menu");
            _menu.style.position = Position.Absolute;
            _menu.style.left = Length.Percent(25);
            _menu.style.right = Length.Percent(25);
            _menu.style.top = Length.Percent(28);
            _menu.style.backgroundColor = new Color(0.08f, 0.05f, 0.07f, 0.88f);
            _menu.style.paddingTop = 24;
            _menu.style.paddingBottom = 24;
            _menu.style.alignItems = Align.Center;
            _banner = Label(_menu, "banner", 40);
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            _banner.style.whiteSpace = WhiteSpace.Normal;
            _selection = Column("selection");
            _selection.style.alignItems = Align.Center;
            var cards = new VisualElement();
            cards.style.flexDirection = FlexDirection.Row;
            for (var i = 0; i < _characterCards.Length; i++)
            {
                var card = new Button { name = "character-" + i, focusable = false };
                var direction = i == 0 ? IntentKind.LeftDown : IntentKind.RightDown;
                card.clicked += () => Queue(new Intent(direction, Time.realtimeSinceStartupAsDouble));
                card.style.width = 236;
                card.style.height = 280;
                card.style.marginLeft = 8;
                card.style.marginRight = 8;
                card.style.alignItems = Align.Center;
                card.style.justifyContent = Justify.Center;
                card.style.borderBottomWidth = 5;
                _characterRoles[i] = Label(card, "role", 18);
                var portrait = new Image { sprite = _stage.Portrait(i), scaleMode = ScaleMode.ScaleToFit };
                portrait.pickingMode = PickingMode.Ignore;
                portrait.style.width = 204;
                portrait.style.height = 174;
                card.Add(portrait);
                Label(card, "name", 24).text = Flow.Table[i].Name;
                Label(card, "style", 16).text = i == 0 ? "柔道家  /  飛び道具" : "米軍出身  /  突進";
                cards.Add(card);
                _characterCards[i] = card;
            }

            _selection.Add(cards);
            Label(_selection, "select-help", 17).text = "← → またはクリックで選択";
            _menu.Add(_selection);
            _primary = Button(_menu, "primary");
            _primary.style.backgroundColor = new Color(1f, 0.67f, 0.36f);
            _primary.style.color = new Color(0.11f, 0.06f, 0.07f);
            _primary.style.unityFontStyleAndWeight = FontStyle.Bold;
            _secondary = Button(_menu, "secondary");
            _secondary.style.backgroundColor = new Color(0.16f, 0.12f, 0.15f);
            _secondary.style.color = Color.white;
            _primary.clicked += () => Queue(new Intent(IntentKind.Submit, Time.realtimeSinceStartupAsDouble));
            _secondary.clicked += () => Queue(new Intent(IntentKind.Back, Time.realtimeSinceStartupAsDouble));
            root.Add(_menu);
        }

        private void RenderHud()
        {
            if (_root == null || Flow == null) { return; }
            var screen = Flow.Screen;
            var match = Flow.Match;
            var health0 = match == null ? int.MinValue : match.Duel.At(0).Health;
            var health1 = match == null ? int.MinValue : match.Duel.At(1).Health;
            var second = match == null ? int.MinValue : match.Clock / 60;
            var round = match == null ? int.MinValue : match.Round;
            if (screen == _shownScreen && Flow.PlayerCharacter == _shownCharacter && health0 == _shownHealth0 && health1 == _shownHealth1 && second == _shownSecond && round == _shownRound)
            {
                return;
            }

            _shownScreen = screen;
            _shownCharacter = Flow.PlayerCharacter;
            _shownHealth0 = health0;
            _shownHealth1 = health1;
            _shownSecond = second;
            _shownRound = round;
            var fighting = screen == DuelScreen.Intro || screen == DuelScreen.Fighting || screen == DuelScreen.RoundEnd;
            _fight.style.display = fighting || screen == DuelScreen.Paused || screen == DuelScreen.Result ? DisplayStyle.Flex : DisplayStyle.None;
            if (match != null)
            {
                SetFill(_fill0, match.Duel.At(0));
                SetFill(_fill1, match.Duel.At(1));
                SetText(_stars, "stars", Stars(match.Wins[0]) + "  " + (match.Clock / 60) + "  " + Stars(match.Wins[1]));
                SetText(_timer, "timer", screen == DuelScreen.Intro ? "ROUND " + match.Round : string.Empty);
            }

            _guide.style.display = fighting ? DisplayStyle.Flex : DisplayStyle.None;
            if (fighting && _specialCommand.text != _specialNames[Flow.PlayerCharacter])
            {
                _specialCommand.text = _specialNames[Flow.PlayerCharacter];
            }

            var selecting = screen == DuelScreen.Select;
            _selection.style.display = selecting ? DisplayStyle.Flex : DisplayStyle.None;
            _menu.style.top = Length.Percent(selecting ? 19 : 28);
            _banner.style.fontSize = screen == DuelScreen.Title ? 76 : 40;
            _banner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _menu.style.display = fighting ? DisplayStyle.None : DisplayStyle.Flex;
            switch (screen)
            {
                case DuelScreen.Title:
                    SetText(_banner, "banner", "EMBER DUEL");
                    SetButton(_primary, "primary", "はじめる");
                    SetButton(_secondary, "secondary", string.Empty);
                    break;
                case DuelScreen.Select:
                    SetText(_banner, "banner", "キャラクター選択");
                    for (var i = 0; i < _characterCards.Length; i++)
                    {
                        var selected = i == Flow.PlayerCharacter;
                        _characterRoles[i].text = selected ? "▶ 1P　選択中" : "CPU　対戦相手";
                        _characterCards[i].style.backgroundColor = selected
                            ? new Color(0.24f, 0.14f, 0.1f) : new Color(0.09f, 0.11f, 0.16f);
                        _characterCards[i].style.borderBottomColor = selected
                            ? new Color(1f, 0.65f, 0.29f) : new Color(0.28f, 0.38f, 0.53f);
                    }

                    SetButton(_primary, "primary", "このキャラで戦う");
                    SetButton(_secondary, "secondary", "タイトルへ");
                    break;
                case DuelScreen.Paused:
                    SetText(_banner, "banner", "一時停止");
                    SetButton(_primary, "primary", "再開");
                    SetButton(_secondary, "secondary", "タイトルへ");
                    break;
                case DuelScreen.Result:
                    SetText(_banner, "banner", ResultText(match));
                    SetButton(_primary, "primary", "再戦");
                    SetButton(_secondary, "secondary", "タイトルへ");
                    break;
            }
        }

        private void PlaySounds()
        {
            var cue = _feedback.Observe(Flow.Match?.Duel);
            if (AudioPolicy.ShouldMute) { return; }
            _audio.volume = _settings.Volume;
            if ((cue & CombatCue.Swing) != 0) { _audio.PlayOneShot(_swing); }
            if ((cue & CombatCue.Hit) != 0) { _audio.PlayOneShot(_hit); }
            if ((cue & CombatCue.Guard) != 0) { _audio.PlayOneShot(_guard); }
            if ((cue & CombatCue.Ko) != 0) { _audio.PlayOneShot(_ko); }
        }

        private static string Stars(int wins)
        {
            if (wins <= 0) { return "☆ ☆"; }
            if (wins == 1) { return "★ ☆"; }
            return "★ ★";
        }

        private static string ResultText(Match match)
        {
            if (match == null) { return "結果"; }
            if (match.Result == MatchResult.Draw) { return "引き分け"; }
            if (match.Result == MatchResult.Left) { return "あなたの勝ち"; }
            return "CPU の勝ち";
        }

        private void SetFill(VisualElement fill, Fighter fighter)
        {
            var width = fighter.MaxHealth == 0 ? 0f : 100f * fighter.Health / fighter.MaxHealth;
            var cached = fill == _fill0 ? _fill0Width : _fill1Width;
            if (Mathf.Abs(width - cached) < 0.05f) { return; }
            fill.style.width = Length.Percent(width);
            if (fill == _fill0) { _fill0Width = width; }
            else { _fill1Width = width; }
        }

        private void SetText(Label label, string slot, string text)
        {
            if (slot == "banner" && text == _bannerText) { return; }
            if (slot == "timer" && text == _timerText) { return; }
            if (slot == "stars" && text == _starsText) { return; }
            label.text = text;
            if (slot == "banner") { _bannerText = text; }
            else if (slot == "timer") { _timerText = text; }
            else { _starsText = text; }
        }

        private void SetButton(Button button, string slot, string text)
        {
            var hidden = text.Length == 0;
            button.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            if (hidden) { return; }
            var current = slot == "primary" ? _primaryText : _secondaryText;
            if (current == text) { return; }
            button.text = text;
            if (slot == "primary") { _primaryText = text; }
            else { _secondaryText = text; }
        }

        private VisualElement Bar(VisualElement parent, Color color)
        {
            var frame = new VisualElement();
            frame.style.width = 360;
            frame.style.height = 22;
            frame.style.backgroundColor = new Color(0.15f, 0.1f, 0.1f);
            var fill = new VisualElement();
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
            button.style.marginTop = 12;
            button.style.fontSize = 22;
            button.style.minWidth = 280;
            parent.Add(button);
            return button;
        }

        private static VisualElement Column(string name)
        {
            var element = new VisualElement { name = name };
            element.style.flexDirection = FlexDirection.Column;
            return element;
        }

        private static AudioClip Tone(string name, float frequency, float seconds)
        {
            const int Rate = 22050;
            var samples = Mathf.Max(1, (int)(Rate * seconds));
            var data = new float[samples];
            for (var i = 0; i < samples; i++)
            {
                var wave = ((i * frequency / Rate) % 1f) < 0.5f ? 0.05f : -0.05f;
                var env = 1f - (i / (float)samples);
                data[i] = wave * env;
            }

            var clip = AudioClip.Create(name, samples, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
