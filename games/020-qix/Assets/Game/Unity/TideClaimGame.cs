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
    public sealed class TideClaimGame : MonoBehaviour
    {
        private const float ViewHalfHeight = 6f;
        private const float ViewHalfWidth = 32f / 3f;

        [SerializeField] private TideClaimSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private TideClaimFieldView _view;

        // 種類の違う操作も押した順に 1 本の列で処理する。Pointer は HUD のボタン由来（フォーカス復帰直後は捨てる）。
        private readonly List<(Intent Intent, bool Pointer)> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -2;
        private VisualElement _root;
        private int _version = -1;
        private (AppScreen Screen, int Claimed, int Lives, int Score, GameStatus Status, int Seed, int Best)? _shownHud;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _startSound;
        private AudioClip _drawSound;
        private AudioClip _closeSound;
        private AudioClip _missSound;
        private AudioClip _wonSound;
        private AudioClip _lostSound;

        public QixFlow Flow { get; private set; }
        public TideClaimSettings Settings => _settings;
        public TideClaimFieldView View => _view;
        public Camera ViewCamera => _camera;
        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        private void Awake()
        {
            _settings.Validate();

            // 撮影中は決まったシードから始め、同じビルドなら同じ画像になるようにする。
            // それ以外は起動時の値から試合ごとに 1 ずつ進め、短い間隔で再戦しても同じ試合にしない。
            Flow = new QixFlow(_settings.Rules, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _startSound = Tone("Start", 600, 600, .08f);
            _drawSound = Tone("Draw", 440, 520, .04f);
            _closeSound = Tone("Close", 520, 880, .2f);
            _missSound = Tone("Miss", 300, 140, .3f);
            _wonSound = Tone("Won", 660, 1320, .5f);
            _lostSound = Tone("Lost", 260, 110, .6f);

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Hold(Control.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            Hold(Control.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            Hold(Control.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            Hold(Control.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Hold(Control.Draw, "<Keyboard>/space");

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Bind(IntentKind kind, params string[] bindings) =>
            NewAction(kind.ToString(), bindings).performed += _ => Queue(Intent.Of(kind), false);

        // 押している間だけ効くキー。押した・離したの両方を列に入れる。
        private void Hold(Control control, params string[] bindings)
        {
            var action = NewAction(control.ToString(), bindings);
            action.started += _ => Queue(Intent.Down(control), false);
            action.canceled += _ => Queue(Intent.Up(control), false);
        }

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

        private void Update()
        {
            if (AcceptInput())
            {
                var justRestored = Time.frameCount <= _focusRestoredFrame + 1;
                foreach (var (intent, pointer) in _intents)
                {
                    // ウィンドウを選び直すクリックで HUD のボタンを押さない。
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

            // 撮影中は台本が Tick を進める。
            if (!EvidenceCapture.IsRunning) { Flow.Advance(Time.deltaTime); }
            PlayEvents();
            Render();
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
            if ((events & FlowEvents.Won) != 0) { _audio.PlayOneShot(_wonSound); }
            else if ((events & FlowEvents.Lost) != 0) { _audio.PlayOneShot(_lostSound); }
            else if ((events & FlowEvents.Miss) != 0) { _audio.PlayOneShot(_missSound); }
            else if ((events & FlowEvents.Closed) != 0) { _audio.PlayOneShot(_closeSound); }
            else if ((events & FlowEvents.DrawStarted) != 0) { _audio.PlayOneShot(_drawSound); }
            else if ((events & FlowEvents.Started) != 0) { _audio.PlayOneShot(_startSound); }
        }

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(ViewHalfHeight, ViewHalfWidth / _camera.aspect);
            if (Flow.Game == null) { _view.RenderInitial(TideClaimSettings.BoardWidth, TideClaimSettings.BoardHeight); }
            else { _view.Render(Flow.Game); }
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
            Connect("restart", IntentKind.Restart);
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
            var state = (Flow.Screen, game?.ClaimedCells ?? 0, game?.Lives ?? 0, game?.Score ?? 0, game?.Status ?? GameStatus.Playing, game?.Seed ?? 0, Flow.BestScore);
            if (_shownHud == state) { return; }
            _shownHud = state;

            var screen = Flow.Screen;
            _root.Q<Label>("headline").text = Headline(screen, game);
            _root.Q("stats").style.display = game == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (game != null)
            {
                var percent = Percent(game);
                _root.Q<Label>("ratio").text = $"陸  {percent}  ／  目標 {game.TargetPercent}%";
                _root.Q("ratioFill").style.width = Length.Percent((float)Math.Min(1, game.ClaimedRatio) * 100);
                _root.Q("ratioTarget").style.left = Length.Percent(game.TargetPercent);
                _root.Q<Label>("lives").text = "残機  " + new string('●', game.Lives) + new string('○', Math.Max(0, _settings.Rules.Lives - game.Lives));
                _root.Q<Label>("score").text = $"スコア  {game.Score}　最高  {Flow.BestScore}";
            }

            _root.Q<Label>("message").text = Message(screen, game, Flow.IsNewBest);
            Show("submit", screen != AppScreen.Playing);
            _root.Q<Button>("submit").text = screen switch
            {
                AppScreen.Title => "はじめる  /  Enter",
                AppScreen.Paused => "つづける  /  Enter",
                _ => "もう一度  /  Enter",
            };
            Show("pause", screen == AppScreen.Playing);
            Show("restart", screen == AppScreen.Paused);
            Show("title", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("quit", screen == AppScreen.Title);
        }

        /// <summary>占有率の表示。目標の手前で切り上げて「達成」に見えないよう、小数 1 桁へ切り捨てる。</summary>
        public static string Percent(QixGame game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            var permille = game.ClaimedCells * 1000L / game.InitialOpen;
            return $"{permille / 10}.{permille % 10}%";
        }

        private static string Headline(AppScreen screen, QixGame game) => screen switch
        {
            AppScreen.Title => "TIDE CLAIM",
            AppScreen.Playing => "埋め立て中",
            AppScreen.Paused => "ひと休み",
            _ => game?.Status == GameStatus.Won ? "埋め立て完了" : "潮に負けた",
        };

        private static string Message(AppScreen screen, QixGame game, bool newBest)
        {
            const string controls = "矢印キー / WASD：移動\nSpace（押している間）：堤を引く\nEsc：一時停止";
            switch (screen)
            {
                case AppScreen.Title:
                    return "干潟に堤を引いて、海を陸に変えよう。\n\n"
                        + "Space を押しながら海へ進むと、\n堤を引ける。陸に戻ると、\n渦のいない側が陸になる。\n"
                        + "途中の堤に渦が触れると崩れる。\n\n海の 75% を陸にすれば勝ち。\n\n" + controls;
                case AppScreen.Playing:
                    return "堤で囲うと、渦のいない側が陸になる。\n明るい縁の上は安全。\n\n" + controls;
                case AppScreen.Paused:
                    return "R：新しい試合\nT：タイトルへ";
                default:
                    var best = newBest ? "\n最高スコアを更新！" : string.Empty;
                    return game?.Status == GameStatus.Won
                        ? $"海の {Percent(game)} を陸にした。{best}"
                        : $"堤がすべて崩された（陸 {(game == null ? "0.0%" : Percent(game))}）。{best}";
            }
        }

        private void Show(string name, bool visible) =>
            _root.Q<Button>(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        // 音程を from から to へ動かす矩形波。振幅は控えめ。
        private AudioClip Tone(string name, int from, int to, float seconds)
        {
            var samples = new float[(int)(44100 * seconds)];
            var phase = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = (float)i / samples.Length;
                phase += Mathf.Lerp(from, to, t) / 44100f;
                samples[i] = (phase % 1f < .5f ? .05f : -.05f) * (1f - t);
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
