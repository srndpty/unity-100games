using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class FireflyHushGame : MonoBehaviour
    {
        private const float ViewHalfHeight = 6f;
        private const float ViewHalfWidth = 32f / 3f;

        /// <summary>タイトルで飾りに灯しておく形（盤の状態を作らず、この値をそのまま描く）。</summary>
        private static readonly LightsBoard _titlePattern = new(0b00100_01010_10101_01010_00100u);

        [SerializeField] private FireflyHushSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private FireflyHushBoardView _view;

        // 種類の違う操作も押した順に 1 本の列で処理する。Pointer はクリック由来（フォーカス復帰直後は捨てる）。
        private readonly List<(Intent Intent, bool Pointer)> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -2;
        private Vector2 _lastPointer = new(float.NaN, float.NaN);
        private VisualElement _root;
        private int _version = -1;
        private (AppScreen Screen, int Moves, GameStatus Status, int Seed)? _shownHud;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _startSound;
        private AudioClip _pressSound;
        private AudioClip _clearSound;
        private AudioClip _giveUpSound;
        private AudioClip _retrySound;

        public LightsOutFlow Flow { get; private set; }
        public FireflyHushSettings Settings => _settings;
        public FireflyHushBoardView View => _view;
        public Camera ViewCamera => _camera;
        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        private void Awake()
        {
            _settings.Validate();

            // 撮影中は決まったシードから始め、同じビルドなら同じ画像になるようにする。
            // それ以外は起動時の値から試合ごとに 1 ずつ進め、短い間隔で再戦しても同じ盤にしない。
            Flow = new LightsOutFlow(_settings.Puzzle, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _startSound = Tone("Start", 600, .08f);
            _pressSound = Tone("Press", 700, .05f);
            _clearSound = Tone("Clear", 990, .4f);
            _giveUpSound = Tone("GiveUp", 330, .3f);
            _retrySound = Tone("Retry", 520, .1f);

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.CursorUp, "<Keyboard>/upArrow");
            Bind(IntentKind.CursorDown, "<Keyboard>/downArrow");
            Bind(IntentKind.CursorLeft, "<Keyboard>/leftArrow");
            Bind(IntentKind.CursorRight, "<Keyboard>/rightArrow");
            Bind(IntentKind.PressCursor, "<Keyboard>/space");
            Bind(IntentKind.Retry, "<Keyboard>/r");
            Bind(IntentKind.NewPuzzle, "<Keyboard>/n");
            Bind(IntentKind.GiveUp, "<Keyboard>/g");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            NewAction("Click", "<Mouse>/leftButton").performed += QueueClick;

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Bind(IntentKind kind, params string[] bindings) =>
            NewAction(kind.ToString(), bindings).performed += _ => Queue(Intent.Of(kind), false);

        private InputAction NewAction(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.Enable();
            _actions.Add(action);
            return action;
        }

        private void Queue(Intent intent, bool pointer)
        {
            if (AcceptInput()) { _intents.Add((intent, pointer)); }
        }

        // 押した瞬間の位置でマスを決める。処理する Update までにカーソルが動いても別のマスを押さない。
        private void QueueClick(InputAction.CallbackContext context)
        {
            if (context.control.device is not Pointer pointer) { return; }
            var world = _camera.ScreenToWorldPoint(pointer.position.ReadValue());
            if (_view.TryCellAt(world, out var cell)) { Queue(Intent.PressCell(cell), true); }
        }

        private void Update()
        {
            QueueHover();
            if (AcceptInput())
            {
                var justRestored = Time.frameCount <= _focusRestoredFrame + 1;
                foreach (var (intent, pointer) in _intents)
                {
                    // ウィンドウを選び直すクリックで盤を押したりボタンを押したりしない。
                    if (pointer && justRestored) { continue; }
                    if (intent.Kind == IntentKind.Quit)
                    {
                        if (Flow.Screen == AppScreen.Title) { QuitHandler(); }
                        continue;
                    }

                    Flow.Apply(intent);
                }
            }

            _intents.Clear();
            PlayEvents();
            Render();
        }

        // マウスを動かしたときだけ、指したマスへカーソルを移す。止まっている間はキーボードの操作を上書きしない。
        private void QueueHover()
        {
            if (Mouse.current == null || !AcceptInput() || Flow.Screen != AppScreen.Playing) { return; }
            var position = Mouse.current.position.ReadValue();
            if (position == _lastPointer) { return; }
            _lastPointer = position;
            if (_view.TryCellAt(_camera.ScreenToWorldPoint(position), out var cell) && cell != Flow.Cursor.Cell)
            {
                Queue(Intent.PointCell(cell), true);
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
            if ((events & FlowEvents.Cleared) != 0) { _audio.PlayOneShot(_clearSound); }
            else if ((events & FlowEvents.GaveUp) != 0) { _audio.PlayOneShot(_giveUpSound); }
            else if ((events & FlowEvents.Retried) != 0) { _audio.PlayOneShot(_retrySound); }
            else if ((events & FlowEvents.Pressed) != 0) { _audio.PlayOneShot(_pressSound); }
            else if ((events & FlowEvents.Started) != 0) { _audio.PlayOneShot(_startSound); }
        }

        /// <summary>今の状態を描く。撮影中は演出で補間せず、その状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(ViewHalfHeight, ViewHalfWidth / _camera.aspect);
            var game = Flow.Game;
            var board = game?.Board ?? _titlePattern;
            var cursor = Flow.Screen == AppScreen.Playing ? Flow.Cursor.Cell : -1;
            var fade = EvidenceCapture.IsRunning || _settings.FadeSeconds <= 0 ? 1f : Time.deltaTime / _settings.FadeSeconds;
            _view.Render(board, cursor, Flow.Screen == AppScreen.Result ? Flow.Answer : 0, fade);
            RenderHud();
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            _shownHud = null;
            root.style.unityFont = _font;
            Connect("submit", IntentKind.Submit);
            Connect("pause", IntentKind.TogglePause);
            Connect("retry", IntentKind.Retry);
            Connect("newPuzzle", IntentKind.NewPuzzle);
            Connect("giveUp", IntentKind.GiveUp);
            Connect("title", IntentKind.BackToTitle);
            Connect("quit", IntentKind.Quit);
            root.Query<Button>().ForEach(button => button.focusable = false);
            RenderHud();
        }

        private void Connect(string name, IntentKind kind) => _root.Q<Button>(name).clicked += () => Queue(Intent.Of(kind), true);

        // 文字とボタンは表示が変わるときだけ書き換える。
        private void RenderHud()
        {
            if (_root == null) { return; }
            var game = Flow.Game;
            var state = (Flow.Screen, game?.Moves ?? 0, game?.Status ?? GameStatus.Playing, game?.Seed ?? 0);
            if (_shownHud == state) { return; }
            _shownHud = state;

            var screen = Flow.Screen;
            _root.Q<Label>("headline").text = Headline(screen, game);
            var moves = _root.Q<Label>("moves");
            moves.text = MovesText(screen, game);
            moves.style.display = game == null ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<Label>("message").text = Message(screen, game);
            Show("submit", screen != AppScreen.Playing);
            _root.Q<Button>("submit").text = screen switch
            {
                AppScreen.Title => "はじめる  /  Enter",
                AppScreen.Paused => "つづける  /  Enter",
                _ => "次の問題  /  Enter",
            };
            Show("pause", screen == AppScreen.Playing);
            Show("retry", screen == AppScreen.Paused);
            Show("newPuzzle", screen == AppScreen.Paused);
            Show("giveUp", screen == AppScreen.Paused);
            Show("title", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("quit", screen == AppScreen.Title);
        }

        private static string Headline(AppScreen screen, LightsOutGame game) => screen switch
        {
            AppScreen.Title => "FIREFLY HUSH",
            AppScreen.Playing => "灯をしずめる",
            AppScreen.Paused => "ひと休み",
            _ => game?.Status == GameStatus.Cleared ? "夜が静まった" : "解答",
        };

        private static string MovesText(AppScreen screen, LightsOutGame game)
        {
            if (game == null) { return string.Empty; }
            if (screen != AppScreen.Result) { return $"手数  {game.Moves}"; }
            return game.Status == GameStatus.Cleared
                ? $"手数  {game.Moves}（最短 {game.OptimalMoves}）"
                : $"手数  {game.Moves}  ／  この問題の最短 {game.OptimalMoves}";
        }

        private static string Message(AppScreen screen, LightsOutGame game)
        {
            const string controls = "クリック：押す\n矢印キー＋Space：選んで押す\nEsc：一時停止";
            switch (screen)
            {
                case AppScreen.Title:
                    return "夜の草むらに灯った蛍を、ぜんぶしずめよう。\n\n"
                        + "マスを押すと、そのマスと上下左右の灯が\n入れ替わる（点く ⇄ 消える）。\n"
                        + "問題は必ず解けるように作られています。\n\n" + controls;
                case AppScreen.Playing:
                    return "押したマスと上下左右の灯が入れ替わる。\nすべて消せばクリア。\n\n" + controls;
                case AppScreen.Paused:
                    return "やり直すと最初の盤に戻り、手数は 0 になる。\nあきらめると、今の盤の最短の押し方を表示する。";
                default:
                    return game?.Status == GameStatus.Cleared
                        ? "すべての灯が消えました。"
                        : "印のマスを 1 回ずつ押すと、すべて消える。\n（押す順番は関係ない）";
            }
        }

        private void Show(string name, bool visible) =>
            _root.Q<Button>(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        private AudioClip Tone(string name, int frequency, float seconds)
        {
            var samples = new float[(int)(44100 * seconds)];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = .05f * Mathf.Sin(2 * Mathf.PI * frequency * i / 44100f) * (1f - ((float)i / samples.Length));
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
    }
}
