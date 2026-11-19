using System;
using System.Collections.Generic;
using System.Globalization;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class StrataDropGame : MonoBehaviour
    {
        private const float ViewHalfHeight = 11.5f;
        private const float ViewHalfWidth = 20.45f;
        private const string Controls = "← → / A D：移動\n↓ / S：ゆっくり落とす\nSpace：すぐ落とす\n↑ / X：右回転　Z：左回転\nC / 左Shift：ホールド\nEsc / P：一時停止";

        [SerializeField] private StrataDropSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private StrataDropView _view;

        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();

        // 押した順序が入れ替わらないよう、すべての操作を1本の列で処理する。
        private readonly List<Intent> _intents = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private VisualElement _root;
        private int _version = -1;
        private VisualElement _stats;
        private VisualElement _overlay;
        private Label _controlsLabel;
        private Label _scoreLabel;
        private Label _linesLabel;
        private Label _levelLabel;
        private Label _bestLabel;
        private Label _headlineLabel;
        private Label _messageLabel;
        private Button _submitButton;
        private Button _restartButton;
        private Button _titleButton;
        private Button _quitButton;

        // HUD の表示を決める値。変わったときだけ描き直す。
        private (AppScreen Screen, long Score, int Lines, int Level, long Best, bool IsNewBest)? _shownHud;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _moveSound;
        private AudioClip _rotateSound;
        private AudioClip _lockSound;
        private AudioClip _dropSound;
        private AudioClip _holdSound;
        private AudioClip _clearSound;
        private AudioClip _levelSound;
        private AudioClip _overSound;

        private enum IntentKind
        {
            Press,
            Release,
            Submit,
            Pause,
            Restart,
            Title,
            Quit,
        }

        public StrataFlow Flow { get; private set; }
        public StrataDropSettings Settings => _settings;
        public StrataDropView View => _view;
        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        private void Awake()
        {
            _settings.Validate();
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            Flow = new StrataFlow(_settings.Rules, _settings.RepeatDelayTicks, _settings.RepeatIntervalTicks, seed);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _moveSound = Tone("Move", 240, .025f, .4f);
            _rotateSound = Tone("Rotate", 380, .04f, .5f);
            _lockSound = Tone("Lock", 130, .07f, .8f);
            _dropSound = Tone("Drop", 90, .1f, 1f);
            _holdSound = Tone("Hold", 520, .07f, .6f);
            _clearSound = Tone("Clear", 700, .22f, 1f);
            _levelSound = Tone("Level", 960, .3f, 1f);
            _overSound = Tone("Over", 110, .6f, 1f);

            PlayerAction("Left", Game.App.PlayerAction.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            PlayerAction("Right", Game.App.PlayerAction.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            PlayerAction("SoftDrop", Game.App.PlayerAction.SoftDrop, "<Keyboard>/downArrow", "<Keyboard>/s");
            PlayerAction("RotateClockwise", Game.App.PlayerAction.RotateClockwise, "<Keyboard>/upArrow", "<Keyboard>/x");
            PlayerAction("RotateCounterClockwise", Game.App.PlayerAction.RotateCounterClockwise, "<Keyboard>/z");
            PlayerAction("HardDrop", Game.App.PlayerAction.HardDrop, "<Keyboard>/space");
            PlayerAction("Hold", Game.App.PlayerAction.Hold, "<Keyboard>/c", "<Keyboard>/leftShift");
            Action("Submit", "<Keyboard>/enter", "<Keyboard>/numpadEnter").performed += _ => Request(new Intent(IntentKind.Submit));
            Action("Pause", "<Keyboard>/escape", "<Keyboard>/p").performed += _ => Request(new Intent(IntentKind.Pause));
            Action("Restart", "<Keyboard>/r").performed += _ => Request(new Intent(IntentKind.Restart));
            Action("Title", "<Keyboard>/t").performed += _ => Request(new Intent(IntentKind.Title));

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Request(Intent intent)
        {
            if (AcceptInput()) { _intents.Add(intent); }
        }

        private void PlayerAction(string name, PlayerAction action, params string[] bindings)
        {
            var input = Action(name, bindings);
            input.performed += _ => Request(new Intent(IntentKind.Press, action));
            input.canceled += _ => Request(new Intent(IntentKind.Release, action));
        }

        private InputAction Action(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.Enable();
            _actions.Add(action);
            return action;
        }

        private void Update()
        {
            // ウィンドウを選び直すクリックで操作しないよう、フォーカス復帰の直後は入力を捨てる。
            if (AcceptInput() && Time.frameCount > _focusRestoredFrame + 1)
            {
                foreach (var intent in _intents) { Apply(intent); }
                Flow.Advance(Time.deltaTime);
            }

            _intents.Clear();
            PlayEvents();
            Render();
        }

        private void Apply(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.Press: Flow.Press(intent.Action); break;
                case IntentKind.Release: Flow.Release(intent.Action); break;
                case IntentKind.Submit: Flow.Submit(); break;
                case IntentKind.Pause: Flow.TogglePause(); break;
                case IntentKind.Restart: Flow.Restart(); break;
                case IntentKind.Title: Flow.BackToTitle(); break;
                case IntentKind.Quit when Flow.Screen == AppScreen.Title: QuitHandler(); break;
            }
        }

        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            Flow.SetFocus(focus);
            _intents.Clear();
            if (focus) { _focusRestoredFrame = Time.frameCount; }
        }

        private void PlayEvents()
        {
            var events = Flow.TakeEvents();
            if ((events & GameEvents.GameOver) != 0) { _audio.PlayOneShot(_overSound); }
            else if ((events & GameEvents.LevelUp) != 0) { _audio.PlayOneShot(_levelSound); }
            else if ((events & GameEvents.LinesCleared) != 0) { _audio.PlayOneShot(_clearSound); }
            else if ((events & GameEvents.HardDropped) != 0) { _audio.PlayOneShot(_dropSound); }
            else if ((events & GameEvents.Locked) != 0) { _audio.PlayOneShot(_lockSound); }
            else if ((events & GameEvents.Held) != 0) { _audio.PlayOneShot(_holdSound); }
            else if ((events & GameEvents.Rotated) != 0) { _audio.PlayOneShot(_rotateSound); }
            else if ((events & GameEvents.Moved) != 0) { _audio.PlayOneShot(_moveSound); }
        }

        /// <summary>現在の状態を描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(ViewHalfHeight, ViewHalfWidth / _camera.aspect);
            _view.Render(Flow.Game);
            RenderHud();
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            root.style.unityFont = _font;
            _stats = root.Q("stats");
            _overlay = root.Q("overlay");
            _scoreLabel = root.Q<Label>("score");
            _linesLabel = root.Q<Label>("lines");
            _levelLabel = root.Q<Label>("level");
            _bestLabel = root.Q<Label>("best");
            _headlineLabel = root.Q<Label>("headline");
            _messageLabel = root.Q<Label>("message");
            _submitButton = root.Q<Button>("submit");
            _restartButton = root.Q<Button>("restart");
            _titleButton = root.Q<Button>("title");
            _quitButton = root.Q<Button>("quit");
            _controlsLabel = root.Q<Label>("controls");
            _controlsLabel.text = Controls;

            _submitButton.clicked += () => Request(new Intent(IntentKind.Submit));
            _restartButton.clicked += () => Request(new Intent(IntentKind.Restart));
            _titleButton.clicked += () => Request(new Intent(IntentKind.Title));
            _quitButton.clicked += () => Request(new Intent(IntentKind.Quit));
            root.Query<Button>().ForEach(button => button.focusable = false);
            _shownHud = null;
            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null) { return; }
            var screen = Flow.Screen;
            var game = Flow.Game;
            var score = game?.Score ?? 0;
            var lines = game?.Lines ?? 0;
            var level = game?.Level ?? 1;
            var state = (screen, score, lines, level, Flow.BestScore, Flow.IsNewBest);
            if (_shownHud.HasValue && _shownHud.Value.Equals(state)) { return; }
            _shownHud = state;

            _scoreLabel.text = Number(score);
            _linesLabel.text = Number(lines);
            _levelLabel.text = Number(level);
            _bestLabel.text = Number(Flow.BestScore);
            _stats.style.display = Display(game != null);

            // タイトルではパネルに操作説明を載せるので、同じ内容を並べない。
            _controlsLabel.style.display = Display(screen != AppScreen.Title);

            _overlay.style.display = Display(screen != AppScreen.Playing);
            _headlineLabel.text = Headline(screen);
            _messageLabel.text = Message(screen, score, lines, level);
            _submitButton.text = SubmitText(screen);
            _restartButton.style.display = Display(screen == AppScreen.Paused);
            _titleButton.style.display = Display(screen == AppScreen.Paused || screen == AppScreen.Result);
            _quitButton.style.display = Display(screen == AppScreen.Title);
        }

        private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string Headline(AppScreen screen) => screen switch
        {
            AppScreen.Title => "STRATA DROP",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => "採掘終了",
            _ => string.Empty,
        };

        private static string SubmitText(AppScreen screen) => screen switch
        {
            AppScreen.Title => "はじめる  /  Enter",
            AppScreen.Paused => "再開  /  Enter",
            _ => "もう一度  /  Enter",
        };

        private string Message(AppScreen screen, long score, int lines, int level)
        {
            switch (screen)
            {
                case AppScreen.Title:
                    return "鉱石を積んで横一列にそろえ、地層を掘り抜こう。\n積み上がって次の鉱石が入らなくなると終了です。\n\n" + Controls;
                case AppScreen.Paused:
                    return "R：やり直す\nT：タイトルへ";
                case AppScreen.Result:
                    var record = Flow.IsNewBest ? "\n\n最高スコア更新！" : string.Empty;
                    return $"スコア  {Number(score)}\nライン  {Number(lines)}\nレベル  {Number(level)}{record}";
                default:
                    return string.Empty;
            }
        }

        private static StyleEnum<DisplayStyle> Display(bool visible) => visible ? DisplayStyle.Flex : DisplayStyle.None;

        private AudioClip Tone(string name, int frequency, float seconds, float level)
        {
            var samples = new float[(int)(44100 * seconds)];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = .05f * level * Mathf.Sin(2 * Mathf.PI * frequency * i / 44100f) * (1f - ((float)i / samples.Length));
            }

            var clip = AudioClip.Create(name, samples.Length, 1, 44100, false);
            clip.SetData(samples, 0);
            _clips.Add(clip);
            return clip;
        }

        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            if (_hudRenderer != null) { _hudRenderer.UnregisterUIReloadCallback(OnHudReloaded); }
            Destroy(_font);
            foreach (var clip in _clips) { Destroy(clip); }
        }

        private readonly struct Intent
        {
            public Intent(IntentKind kind, PlayerAction action = default)
            {
                Kind = kind;
                Action = action;
            }

            public IntentKind Kind { get; }
            public PlayerAction Action { get; }
        }
    }
}
