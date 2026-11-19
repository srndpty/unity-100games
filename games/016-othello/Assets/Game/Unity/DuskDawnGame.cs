using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Position = Game.Core.Position;

namespace Game.Unity
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class DuskDawnGame : MonoBehaviour
    {
        private const float ViewHalfHeight = 4.9f;
        private const float ViewHalfWidth = 8.7f;
        private const string Controls = "マウス：点の出ているマスをクリックして打つ\n矢印 / WASD：カーソル移動\nEnter / Space：カーソルの位置に打つ\nEsc / P：一時停止";

        [SerializeField] private DuskDawnSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private DuskDawnBoardView _view;

        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();

        // 押した順序が入れ替わらないよう、マウス・キー・ボタンのすべての操作を1本の列で処理する。
        private readonly List<Intent> _intents = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private VisualElement _root;
        private int _version = -1;
        private VisualElement _stats;
        private VisualElement _overlay;
        private VisualElement _options;
        private VisualElement _blackRow;
        private VisualElement _whiteRow;
        private Label _blackName;
        private Label _whiteName;
        private Label _blackCount;
        private Label _whiteCount;
        private Label _statusLabel;
        private Label _levelLabel;
        private Label _headlineLabel;
        private Label _messageLabel;
        private Button _submitButton;
        private Button _restartButton;
        private Button _titleButton;
        private Button _quitButton;
        private Button[] _levelButtons;
        private Button[] _sideButtons;

        // HUD の表示を決める値。変わったときだけ描き直す。
        private HudState? _shownHud;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _placeSound;
        private AudioClip _rejectSound;
        private AudioClip _passSound;
        private AudioClip _winSound;
        private AudioClip _loseSound;
        private AudioClip _drawSound;

        private enum IntentKind
        {
            PointerDown,
            Navigate,
            Submit,
            Pause,
            Restart,
            Title,
            Quit,
            Level,
            Side,
        }

        public OthelloFlow Flow { get; private set; }
        public DuskDawnSettings Settings => _settings;
        public DuskDawnBoardView View => _view;
        public Camera ViewCamera => _camera;
        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        private void Awake()
        {
            _settings.Validate();

            // 撮影中は決まったシードから始め、同じビルドなら同じ画像になるようにする。再戦は Flow が試合ごとにシードを進める。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            Flow = new OthelloFlow(_settings.Rules, seed);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _placeSound = Tone("Place", 520, .07f, .7f);
            _rejectSound = Tone("Reject", 140, .14f, .8f);
            _passSound = Tone("Pass", 330, .25f, .7f);
            _winSound = Tone("Win", 1046, .45f, 1f);
            _loseSound = Tone("Lose", 130, .55f, 1f);
            _drawSound = Tone("Draw", 523, .4f, .8f);

            Action("Press", "<Mouse>/leftButton").performed += PointerPressed;
            BindNavigate("Left", -1, 0, "<Keyboard>/leftArrow", "<Keyboard>/a");
            BindNavigate("Right", 1, 0, "<Keyboard>/rightArrow", "<Keyboard>/d");
            BindNavigate("Up", 0, -1, "<Keyboard>/upArrow", "<Keyboard>/w");
            BindNavigate("Down", 0, 1, "<Keyboard>/downArrow", "<Keyboard>/s");
            Action("Submit", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space").performed += _ => Request(new Intent(IntentKind.Submit));
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

        // 押した瞬間の位置で判定する。処理する Update までにカーソルが動いても別のマスに打たない。
        private void PointerPressed(InputAction.CallbackContext context)
        {
            if (context.control.device is Pointer pointer) { Request(new Intent(IntentKind.PointerDown, pointer.position.ReadValue())); }
        }

        private void BindNavigate(string name, int dx, int dy, params string[] bindings) =>
            Action(name, bindings).performed += _ => Request(new Intent(IntentKind.Navigate, new Vector2(dx, dy)));

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
                case IntentKind.PointerDown:
                    var board = ScreenToBoard(intent.Value);
                    Flow.PointerDown(board.x, board.y);
                    break;
                case IntentKind.Navigate: Flow.Navigate((int)intent.Value.x, (int)intent.Value.y); break;
                case IntentKind.Submit: Flow.Submit(); break;
                case IntentKind.Pause: Flow.TogglePause(); break;
                case IntentKind.Restart: Flow.Restart(); break;
                case IntentKind.Title: Flow.BackToTitle(); break;
                case IntentKind.Level: Flow.SelectLevel((CpuLevel)(int)intent.Value.x); break;
                case IntentKind.Side: Flow.SelectSide((Side)(int)intent.Value.x); break;
                case IntentKind.Quit when Flow.Screen == AppScreen.Title: QuitHandler(); break;
            }
        }

        private Vector2 ScreenToBoard(Vector2 screen) => _view.ToBoard(_camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z)));

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
            if ((events & FlowEvents.GameOver) != 0) { _audio.PlayOneShot(Flow.Outcome switch { Outcome.Win => _winSound, Outcome.Loss => _loseSound, _ => _drawSound }); }
            else if ((events & FlowEvents.Passed) != 0) { _audio.PlayOneShot(_passSound); }
            else if ((events & FlowEvents.Placed) != 0) { _audio.PlayOneShot(_placeSound); }
            else if ((events & FlowEvents.Rejected) != 0) { _audio.PlayOneShot(_rejectSound); }
        }

        /// <summary>現在の状態を描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(ViewHalfHeight, ViewHalfWidth / _camera.aspect);
            var game = Flow.Game;
            if (game == null)
            {
                _view.RenderStill(Position.Initial);
            }
            else
            {
                var playing = Flow.Screen == AppScreen.Playing;
                var hints = Flow.IsPlayerTurn ? game.LegalMoves : 0;
                _view.Render(game, hints, playing && Flow.Cursor.IsActive ? Flow.Cursor.Position : null);
            }

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
            _options = root.Q("options");
            _blackRow = root.Q("black-row");
            _whiteRow = root.Q("white-row");
            _blackName = root.Q<Label>("black-name");
            _whiteName = root.Q<Label>("white-name");
            _blackCount = root.Q<Label>("black-count");
            _whiteCount = root.Q<Label>("white-count");
            _statusLabel = root.Q<Label>("status");
            _levelLabel = root.Q<Label>("level");
            root.Q<Label>("controls").text = Controls;
            _headlineLabel = root.Q<Label>("headline");
            _messageLabel = root.Q<Label>("message");
            _submitButton = root.Q<Button>("submit");
            _restartButton = root.Q<Button>("restart");
            _titleButton = root.Q<Button>("title");
            _quitButton = root.Q<Button>("quit");
            _levelButtons = new[] { root.Q<Button>("level-weak"), root.Q<Button>("level-normal"), root.Q<Button>("level-strong") };
            _sideButtons = new[] { root.Q<Button>("side-black"), root.Q<Button>("side-white") };

            _submitButton.clicked += () => Request(new Intent(IntentKind.Submit));
            _restartButton.clicked += () => Request(new Intent(IntentKind.Restart));
            _titleButton.clicked += () => Request(new Intent(IntentKind.Title));
            _quitButton.clicked += () => Request(new Intent(IntentKind.Quit));
            for (var i = 0; i < _levelButtons.Length; i++)
            {
                var level = i;
                _levelButtons[i].clicked += () => Request(new Intent(IntentKind.Level, new Vector2(level, 0)));
            }

            for (var i = 0; i < _sideButtons.Length; i++)
            {
                var side = i;
                _sideButtons[i].clicked += () => Request(new Intent(IntentKind.Side, new Vector2(side, 0)));
            }

            root.Query<Button>().ForEach(button => button.focusable = false);
            _shownHud = null;
            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null) { return; }
            var game = Flow.Game;
            var state = new HudState(
                Flow.Screen,
                game?.Phase ?? GamePhase.AwaitingMove,
                game?.Count(Side.Black) ?? 2,
                game?.Count(Side.White) ?? 2,
                game?.ToMove ?? Side.Black,
                Flow.IsPlayerTurn,
                Flow.LastVerdict,
                Flow.Level,
                Flow.PlayerSide);
            if (_shownHud.HasValue && _shownHud.Value.Equals(state)) { return; }
            _shownHud = state;

            var screen = state.Screen;
            var options = screen == AppScreen.Title || screen == AppScreen.Result;

            // パネルの裏に数字が透けないよう、プレイ中だけ表示する。結果は案内の中に書く。
            _stats.style.display = Display(screen == AppScreen.Playing);
            _blackName.text = PlayerName(Side.Black);
            _whiteName.text = PlayerName(Side.White);
            _blackCount.text = state.Black.ToString();
            _whiteCount.text = state.White.ToString();
            var showTurn = game != null && !game.IsOver;
            _blackRow.EnableInClassList("side-row--turn", showTurn && state.ToMove == Side.Black);
            _whiteRow.EnableInClassList("side-row--turn", showTurn && state.ToMove == Side.White);
            _statusLabel.text = Status(state);
            _levelLabel.text = "CPU：" + LevelText(Flow.MatchLevel, Flow.Config);

            _overlay.style.display = Display(screen != AppScreen.Playing);
            _options.style.display = Display(options);
            _headlineLabel.text = Headline(screen, Flow.Outcome);
            _messageLabel.text = Message(screen, game);
            _submitButton.text = screen == AppScreen.Title ? "はじめる  /  Enter" : screen == AppScreen.Paused ? "再開  /  Enter" : "もう一度  /  Enter";
            _restartButton.style.display = Display(screen == AppScreen.Paused);
            _titleButton.style.display = Display(screen == AppScreen.Paused || screen == AppScreen.Result);
            _quitButton.style.display = Display(screen == AppScreen.Title);
            for (var i = 0; i < _levelButtons.Length; i++) { _levelButtons[i].EnableInClassList("option--selected", (int)state.Level == i); }
            for (var i = 0; i < _sideButtons.Length; i++) { _sideButtons[i].EnableInClassList("option--selected", (int)state.PlayerSide == i); }
        }

        private string PlayerName(Side side) => side == Flow.MatchPlayerSide ? $"あなた（{ColorName(side)}）" : $"CPU（{ColorName(side)}）";

        private static string ColorName(Side side) => side == Side.Black ? "黒" : "白";

        private static string LevelText(CpuLevel level, OthelloConfig config)
        {
            var name = level switch
            {
                CpuLevel.Weak => "弱い",
                CpuLevel.Normal => "普通",
                _ => "強い",
            };
            return $"{name}（{config.DepthOf(level)}手読み）";
        }

        private string Status(HudState state)
        {
            if (state.Screen != AppScreen.Playing) { return string.Empty; }
            if (state.Phase == GamePhase.Passing)
            {
                return $"{PlayerName(state.ToMove)}は打てる場所がないので、パスします。";
            }

            if (state.ToMove != Flow.MatchPlayerSide) { return "CPU が考えています…"; }
            var rejected = state.IsPlayerTurn && state.LastVerdict.HasValue && state.LastVerdict != PlayVerdict.Ok;
            return rejected ? "そこには打てません。\n点の出ているマスに打てます。" : "あなたの番です。";
        }

        private static string Headline(AppScreen screen, Outcome? outcome) => screen switch
        {
            AppScreen.Title => "DUSK & DAWN",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => outcome switch
            {
                Outcome.Win => "あなたの勝ち！",
                Outcome.Loss => "CPU の勝ち",
                _ => "引き分け",
            },
            _ => string.Empty,
        };

        private string Message(AppScreen screen, OthelloGame game)
        {
            switch (screen)
            {
                case AppScreen.Title:
                    return "相手の石を自分の石ではさむと、はさんだ石が自分の色に返ります。\n打てる場所がないときは自動でパス。両者とも打てなくなったら、石の多いほうの勝ち。\n\n" + Controls;
                case AppScreen.Paused:
                    return "Enter：再開\nR：やり直す\nT：タイトルへ";
                case AppScreen.Result:
                    return $"黒 {game.Count(Side.Black)}  −  白 {game.Count(Side.White)}（あなたは{ColorName(Flow.MatchPlayerSide)}）\nCPU：{LevelText(Flow.MatchLevel, Flow.Config)}\n強さと石の色を変えて、もう一度遊べます。";
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

        private readonly struct HudState : IEquatable<HudState>
        {
            public HudState(AppScreen screen, GamePhase phase, int black, int white, Side toMove, bool isPlayerTurn, PlayVerdict? lastVerdict, CpuLevel level, Side playerSide)
            {
                Screen = screen;
                Phase = phase;
                Black = black;
                White = white;
                ToMove = toMove;
                IsPlayerTurn = isPlayerTurn;
                LastVerdict = lastVerdict;
                Level = level;
                PlayerSide = playerSide;
            }

            public AppScreen Screen { get; }
            public GamePhase Phase { get; }
            public int Black { get; }
            public int White { get; }
            public Side ToMove { get; }
            public bool IsPlayerTurn { get; }
            public PlayVerdict? LastVerdict { get; }
            public CpuLevel Level { get; }
            public Side PlayerSide { get; }

            public bool Equals(HudState other) =>
                Screen == other.Screen && Phase == other.Phase && Black == other.Black && White == other.White && ToMove == other.ToMove &&
                IsPlayerTurn == other.IsPlayerTurn && LastVerdict == other.LastVerdict && Level == other.Level && PlayerSide == other.PlayerSide;

            public override bool Equals(object obj) => obj is HudState other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(HashCode.Combine(Screen, Phase, Black, White, ToMove), IsPlayerTurn, LastVerdict, Level, PlayerSide);
        }
    }
}
