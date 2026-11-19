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
    public sealed class SporeBloomGame : MonoBehaviour
    {
        private const float ViewHalfHeight = 8f;
        private const float ViewHalfWidth = 14.3f;
        private const int ChainLabelMinimum = 2;
        private const string Controls = "← → / A D：移動\n↓ / S：速く落とす\n↑ / X：右回転\nZ：左回転\nEsc / P：一時停止";

        [SerializeField] private SporeBloomSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private SporeBloomView _view;

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
        private Label _playerScore;
        private Label _playerPending;
        private Label _playerChain;
        private Label _cpuScore;
        private Label _cpuPending;
        private Label _cpuChain;
        private Label _headlineLabel;
        private Label _messageLabel;
        private Button _submitButton;
        private Button _restartButton;
        private Button _titleButton;
        private Button _quitButton;

        // HUD の表示を決める値。変わったときだけ描き直す。
        private (AppScreen Screen, MatchOutcome Outcome, long PlayerScore, int PlayerPending, int PlayerChain, long CpuScore, int CpuPending, int CpuChain)? _shownHud;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _moveSound;
        private AudioClip _rotateSound;
        private AudioClip _lockSound;
        private AudioClip _popSound;
        private AudioClip _garbageSound;
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

        public VersusFlow Flow { get; private set; }
        public SporeBloomSettings Settings => _settings;
        public SporeBloomView View => _view;
        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        private void Awake()
        {
            _settings.Validate();
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            Flow = new VersusFlow(_settings.Rules, _settings.CpuActionIntervalTicks, _settings.CpuMistakePercent, _settings.RepeatDelayTicks, _settings.RepeatIntervalTicks, seed);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _moveSound = Tone("Move", 260, .025f, .4f);
            _rotateSound = Tone("Rotate", 420, .04f, .5f);
            _lockSound = Tone("Lock", 150, .07f, .8f);
            _popSound = Tone("Pop", 720, .18f, 1f);
            _garbageSound = Tone("Garbage", 95, .16f, 1f);
            _overSound = Tone("Over", 120, .6f, 1f);

            BindPlayer("Left", PlayerAction.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            BindPlayer("Right", PlayerAction.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            BindPlayer("SoftDrop", PlayerAction.SoftDrop, "<Keyboard>/downArrow", "<Keyboard>/s");
            BindPlayer("RotateClockwise", PlayerAction.RotateClockwise, "<Keyboard>/upArrow", "<Keyboard>/x");
            BindPlayer("RotateCounterClockwise", PlayerAction.RotateCounterClockwise, "<Keyboard>/z");
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

        private void BindPlayer(string name, PlayerAction action, params string[] bindings)
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
            var events = Flow.TakePlayerEvents();
            var cpuEvents = Flow.TakeCpuEvents();
            if (((events | cpuEvents) & FieldEvents.Defeated) != 0) { _audio.PlayOneShot(_overSound); }
            else if ((events & FieldEvents.Popped) != 0) { _audio.PlayOneShot(_popSound); }
            else if ((events & FieldEvents.GarbageLanded) != 0) { _audio.PlayOneShot(_garbageSound); }
            else if ((events & FieldEvents.Locked) != 0) { _audio.PlayOneShot(_lockSound); }
            else if ((events & FieldEvents.Rotated) != 0) { _audio.PlayOneShot(_rotateSound); }
            else if ((events & FieldEvents.Moved) != 0) { _audio.PlayOneShot(_moveSound); }
        }

        /// <summary>現在の状態を描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(ViewHalfHeight, ViewHalfWidth / _camera.aspect);
            _view.Render(Flow.Match, _settings.Rules.SettleTicksPerRow, Flow.Screen == AppScreen.Playing);
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
            _playerScore = root.Q<Label>("player-score");
            _playerPending = root.Q<Label>("player-pending");
            _playerChain = root.Q<Label>("player-chain");
            _cpuScore = root.Q<Label>("cpu-score");
            _cpuPending = root.Q<Label>("cpu-pending");
            _cpuChain = root.Q<Label>("cpu-chain");
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
            var match = Flow.Match;
            var player = match?.Player;
            var cpu = match?.Cpu;
            var state = (screen, match?.Outcome ?? MatchOutcome.InProgress, player?.Score ?? 0, player?.PendingGarbage ?? 0, ResolvingChain(player), cpu?.Score ?? 0, cpu?.PendingGarbage ?? 0, ResolvingChain(cpu));
            if (_shownHud.HasValue && _shownHud.Value.Equals(state)) { return; }
            _shownHud = state;

            // パネルの周りに HUD がはみ出さないよう、プレイ中だけ表示する。
            _stats.style.display = Display(screen == AppScreen.Playing);
            _playerScore.text = Number(state.Item3);
            _playerPending.text = Pending(state.Item4);
            _playerChain.text = ChainText(state.Item5);
            _cpuScore.text = Number(state.Item6);
            _cpuPending.text = Pending(state.Item7);
            _cpuChain.text = ChainText(state.Item8);

            _controlsLabel.style.display = Display(screen == AppScreen.Playing);

            _overlay.style.display = Display(screen != AppScreen.Playing);
            _headlineLabel.text = Headline(screen, state.Item2);
            _messageLabel.text = Message(screen, match);
            _submitButton.text = SubmitText(screen);
            _restartButton.style.display = Display(screen == AppScreen.Paused);
            _titleButton.style.display = Display(screen == AppScreen.Paused || screen == AppScreen.Result);
            _quitButton.style.display = Display(screen == AppScreen.Title);
        }

        private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        // 連鎖の表示は解決の間だけ。次のペアを操作し始めたら消す。
        private static int ResolvingChain(PlayerField field) => field == null || field.Phase == FieldPhase.Falling ? 0 : field.Chain;

        private static string Pending(int count) => count > 0 ? $"枯れ胞子 {Number(count)}" : string.Empty;

        private static string ChainText(int chain) => chain >= ChainLabelMinimum ? $"{Number(chain)} 連鎖！" : string.Empty;

        private static string Headline(AppScreen screen, MatchOutcome outcome) => screen switch
        {
            AppScreen.Title => "SPORE BLOOM",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => outcome switch
            {
                MatchOutcome.PlayerWin => "あなたの勝ち",
                MatchOutcome.CpuWin => "CPU の勝ち",
                _ => "引き分け",
            },
            _ => string.Empty,
        };

        private static string SubmitText(AppScreen screen) => screen switch
        {
            AppScreen.Title => "はじめる  /  Enter",
            AppScreen.Paused => "再開  /  Enter",
            _ => "もう一度  /  Enter",
        };

        private static string Message(AppScreen screen, VersusMatch match)
        {
            switch (screen)
            {
                case AppScreen.Title:
                    return "同じ色の胞子を4つつなげて開花させよう。\n連鎖すると相手へ枯れ胞子を送れます。\n3列目の一番上が埋まると負けです。\n\n" + Controls;
                case AppScreen.Paused:
                    return "R：やり直す\nT：タイトルへ";
                case AppScreen.Result:
                    return $"あなた  {Number(match.Player.Score)} 点  最大 {Number(match.Player.MaxChain)} 連鎖\nCPU  {Number(match.Cpu.Score)} 点  最大 {Number(match.Cpu.MaxChain)} 連鎖";
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
