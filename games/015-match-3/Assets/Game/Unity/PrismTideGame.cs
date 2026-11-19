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
    public sealed class PrismTideGame : MonoBehaviour
    {
        private const float ViewHalfHeight = 4.8f;
        private const float ViewHalfWidth = 8.6f;
        private const int TitleBoardSeed = 2026;
        private const int ChainLabelMinimum = 2;
        private const string Controls = "マウス：結晶をクリックして隣をクリック\n　　　　またはドラッグで入れ替え\n矢印：カーソル移動\nSpace：選ぶ → 矢印で入れ替え\nEsc / P：一時停止";

        [SerializeField] private PrismTideSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private PrismBoardView _view;

        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();

        // 押した順序が入れ替わらないよう、マウス・キー・ボタンのすべての操作を1本の列で処理する。
        private readonly List<Intent> _intents = new();
        private bool _hasFocus = true;
        private bool _pointerHeld;
        private int _focusRestoredFrame = -10;
        private Board _titleBoard;
        private VisualElement _root;
        private int _version = -1;
        private VisualElement _stats;
        private VisualElement _overlay;
        private VisualElement _progressFill;
        private Label _scoreLabel;
        private Label _targetLabel;
        private Label _movesLabel;
        private Label _chainLabel;
        private Label _headlineLabel;
        private Label _messageLabel;
        private Button _submitButton;
        private Button _restartButton;
        private Button _titleButton;
        private Button _quitButton;

        // HUD の表示を決める値。変わったときだけ描き直す。
        private (AppScreen Screen, GamePhase Phase, long Score, int Moves, int Chain)? _shownHud;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _selectSound;
        private AudioClip _swapSound;
        private AudioClip _rejectSound;
        private AudioClip[] _clearSounds;
        private AudioClip _reshuffleSound;
        private AudioClip _winSound;
        private AudioClip _loseSound;

        private enum IntentKind
        {
            PointerDown,
            PointerMove,
            PointerUp,
            Move,
            Select,
            Submit,
            Pause,
            Restart,
            Title,
            Quit,
        }

        public Match3Flow Flow { get; private set; }
        public PrismTideSettings Settings => _settings;
        public PrismBoardView View => _view;
        public Camera ViewCamera => _camera;
        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        private void Awake()
        {
            _settings.Validate();

            // 撮影中は決まったシードから始め、同じビルドなら同じ画像になるようにする。再戦は Flow が試合ごとにシードを進める。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            Flow = new Match3Flow(_settings.Rules, seed);
            _titleBoard = BoardGenerator.Generate(new SeededRandom(TitleBoardSeed), _settings.Rules.Colors, _settings.Rules.MaxGenerationAttempts);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _selectSound = Tone("Select", 640, .03f, .4f);
            _swapSound = Tone("Swap", 420, .06f, .6f);
            _rejectSound = Tone("Reject", 140, .14f, .8f);
            _clearSounds = new[] { Tone("Clear1", 660, .16f, 1f), Tone("Clear2", 784, .16f, 1f), Tone("Clear3", 880, .16f, 1f), Tone("Clear4", 1046, .18f, 1f) };
            _reshuffleSound = Tone("Reshuffle", 330, .3f, .8f);
            _winSound = Tone("Win", 1046, .45f, 1f);
            _loseSound = Tone("Lose", 130, .55f, 1f);

            var press = Action("Press", "<Mouse>/leftButton");
            press.performed += PointerPressed;
            press.canceled += _ => PointerReleased();
            var point = Action("Point", InputActionType.Value, "<Mouse>/position");
            point.performed += context => { if (_pointerHeld) { Request(new Intent(IntentKind.PointerMove, context.ReadValue<Vector2>())); } };
            BindMove("Left", -1, 0, "<Keyboard>/leftArrow", "<Keyboard>/a");
            BindMove("Right", 1, 0, "<Keyboard>/rightArrow", "<Keyboard>/d");
            BindMove("Down", 0, -1, "<Keyboard>/downArrow", "<Keyboard>/s");
            BindMove("Up", 0, 1, "<Keyboard>/upArrow", "<Keyboard>/w");
            Action("Select", "<Keyboard>/space").performed += _ => Request(new Intent(IntentKind.Select));
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

        // 押した瞬間の位置で判定する。処理する Update までにカーソルが動いても別のマスを選ばない。
        private void PointerPressed(InputAction.CallbackContext context)
        {
            if (context.control.device is not Pointer pointer) { return; }
            _pointerHeld = true;
            Request(new Intent(IntentKind.PointerDown, pointer.position.ReadValue()));
        }

        private void PointerReleased()
        {
            _pointerHeld = false;
            Request(new Intent(IntentKind.PointerUp));
        }

        private void BindMove(string name, int dx, int dy, params string[] bindings) =>
            Action(name, bindings).performed += _ => Request(new Intent(IntentKind.Move, new Vector2(dx, dy)));

        private InputAction Action(string name, params string[] bindings) => Action(name, InputActionType.Button, bindings);

        private InputAction Action(string name, InputActionType type, params string[] bindings)
        {
            var action = new InputAction(name, type);
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
            var selected = Flow.Input.Selected;
            switch (intent.Kind)
            {
                case IntentKind.PointerDown:
                    var down = ScreenToBoard(intent.Value);
                    Flow.PointerDown(down.x, down.y);
                    break;
                case IntentKind.PointerMove:
                    var moved = ScreenToBoard(intent.Value);
                    Flow.PointerMove(moved.x, moved.y);
                    break;
                case IntentKind.PointerUp: Flow.PointerUp(); break;
                case IntentKind.Move: Flow.Move((int)intent.Value.x, (int)intent.Value.y); break;
                case IntentKind.Select: Flow.ToggleSelect(); break;
                case IntentKind.Submit: Flow.Submit(); break;
                case IntentKind.Pause: Flow.TogglePause(); break;
                case IntentKind.Restart: Flow.Restart(); break;
                case IntentKind.Title: Flow.BackToTitle(); break;
                case IntentKind.Quit when Flow.Screen == AppScreen.Title: QuitHandler(); break;
            }

            if (Flow.Input.Selected.HasValue && Flow.Input.Selected != selected) { _audio.PlayOneShot(_selectSound); }
        }

        private Vector2 ScreenToBoard(Vector2 screen) => _view.ToBoard(_camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z)));

        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            Flow.SetFocus(focus);
            _intents.Clear();
            _pointerHeld = false;
            if (focus) { _focusRestoredFrame = Time.frameCount; }
        }

        private void PlayEvents()
        {
            var events = Flow.TakeEvents();
            var game = Flow.Game;
            if ((events & GameEvents.Won) != 0) { _audio.PlayOneShot(_winSound); }
            else if ((events & GameEvents.Lost) != 0) { _audio.PlayOneShot(_loseSound); }
            else if ((events & GameEvents.Reshuffled) != 0) { _audio.PlayOneShot(_reshuffleSound); }
            else if ((events & GameEvents.Cleared) != 0 && game != null) { _audio.PlayOneShot(_clearSounds[Mathf.Clamp(game.Chain, 1, _clearSounds.Length) - 1]); }
            else if ((events & GameEvents.Rejected) != 0) { _audio.PlayOneShot(_rejectSound); }
            else if ((events & GameEvents.Swapped) != 0) { _audio.PlayOneShot(_swapSound); }
        }

        /// <summary>現在の状態を描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(ViewHalfHeight, ViewHalfWidth / _camera.aspect);
            if (Flow.Game == null) { _view.RenderStill(_titleBoard); }
            else { _view.Render(Flow.Game, Flow.Screen == AppScreen.Playing ? Flow.Input : null); }
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
            _progressFill = root.Q("progress-fill");
            _scoreLabel = root.Q<Label>("score");
            _targetLabel = root.Q<Label>("target");
            _movesLabel = root.Q<Label>("moves");
            _chainLabel = root.Q<Label>("chain");
            root.Q<Label>("controls").text = Controls;
            _headlineLabel = root.Q<Label>("headline");
            _messageLabel = root.Q<Label>("message");
            _submitButton = root.Q<Button>("submit");
            _restartButton = root.Q<Button>("restart");
            _titleButton = root.Q<Button>("title");
            _quitButton = root.Q<Button>("quit");

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
            var state = (screen, game?.Phase ?? GamePhase.Idle, game?.Score ?? 0, game?.MovesLeft ?? Flow.Config.Moves, ResolvingChain(game));
            if (_shownHud.HasValue && _shownHud.Value.Equals(state)) { return; }
            _shownHud = state;

            var target = game?.TargetScore ?? Flow.Config.TargetScore;

            // パネルの裏に数字が透けないよう、プレイ中だけ表示する。結果は案内の中に書く。
            _stats.style.display = Display(screen == AppScreen.Playing);
            _scoreLabel.text = Number(state.Item3);
            _targetLabel.text = "目標  " + Number(target);
            _movesLabel.text = Number(state.Item4);
            _chainLabel.text = state.Item5 >= ChainLabelMinimum ? $"{Number(state.Item5)} 連鎖！" : string.Empty;
            _progressFill.style.width = Length.Percent(Mathf.Clamp01((float)state.Item3 / target) * 100f);

            _overlay.style.display = Display(screen != AppScreen.Playing);
            _headlineLabel.text = Headline(screen, state.Item2);
            _messageLabel.text = Message(screen, game, Flow.Config);
            _submitButton.text = SubmitText(screen);
            _restartButton.style.display = Display(screen == AppScreen.Paused || screen == AppScreen.Result);
            _titleButton.style.display = Display(screen == AppScreen.Paused || screen == AppScreen.Result);
            _quitButton.style.display = Display(screen == AppScreen.Title);
        }

        private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        // 連鎖の表示は解決の間だけ。入れ替えを待つ間は消す。
        private static int ResolvingChain(Match3Game game) =>
            game == null || game.Phase == GamePhase.Clearing || game.Phase == GamePhase.Falling ? game?.Chain ?? 0 : 0;

        private static string Headline(AppScreen screen, GamePhase phase) => screen switch
        {
            AppScreen.Title => "PRISM TIDE",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => phase == GamePhase.Won ? "目標達成！" : "手数切れ",
            _ => string.Empty,
        };

        private static string SubmitText(AppScreen screen) => screen switch
        {
            AppScreen.Title => "はじめる  /  Enter",
            AppScreen.Paused => "再開  /  Enter",
            _ => "もう一度  /  Enter",
        };

        private static string Message(AppScreen screen, Match3Game game, Match3Config config)
        {
            switch (screen)
            {
                case AppScreen.Title:
                    return $"潮だまりの結晶を入れ替えて、\n同じ色を縦か横に3つ以上そろえよう。\n{Number(config.Moves)} 手のうちに {Number(config.TargetScore)} 点を集めればクリア。\nそろわない入れ替えは元に戻り、手数は減りません。\n\n" + Controls;
                case AppScreen.Paused:
                    return "Enter：再開\nR：やり直す\nT：タイトルへ";
                case AppScreen.Result:
                    return $"スコア  {Number(game.Score)} 点（目標 {Number(game.TargetScore)}）\n使った手数  {Number(game.MovesUsed)} 手\n最大連鎖  {Number(game.MaxChain)}";
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
            public Intent(IntentKind kind, Vector2 value = default)
            {
                Kind = kind;
                Value = value;
            }

            public IntentKind Kind { get; }
            public Vector2 Value { get; }
        }
    }
}
