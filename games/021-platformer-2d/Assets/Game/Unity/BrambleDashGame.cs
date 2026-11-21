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
    public sealed class BrambleDashGame : MonoBehaviour
    {
        /// <summary>画面の縦に映すタイル数の半分。どの面も 15 行なので、縦にちょうど収まる。</summary>
        public const float ViewHalfHeight = 7.5f;

        [SerializeField] private BrambleDashSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private BrambleDashStage[] _stages = Array.Empty<BrambleDashStage>();

        // 種類の違う操作も押した順に 1 本の列で処理する。Pointer は HUD のボタン由来（フォーカス復帰直後は捨てる）。
        private readonly List<(Intent Intent, bool Pointer)> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -2;
        private VisualElement _root;
        private int _version = -1;
        private (AppScreen, int, int, int, int, int, CampaignStatus)? _shownHud;
        private Level[] _levels;
        private LevelRun _cameraRun;
        private int _cameraCenter;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _startSound;
        private AudioClip _jumpSound;
        private AudioClip _stompSound;
        private AudioClip _clearSound;
        private AudioClip _missSound;
        private AudioClip _wonSound;
        private AudioClip _lostSound;

        public PlatformerFlow Flow { get; private set; }
        public BrambleDashSettings Settings => _settings;
        public Camera ViewCamera => _camera;
        public VisualElement HudRoot => _root;

        /// <summary>今の面の Stage（試合の前は 1 面）。</summary>
        public BrambleDashStage CurrentStage => _stages[Flow?.Campaign?.LevelIndex ?? 0];

        public BrambleDashStage StageAt(int index) => _stages[index];

        /// <summary>カメラの中心（Core の単位）。</summary>
        public int CameraCenter => _cameraCenter;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        private void Awake()
        {
            _settings.Validate();
            _levels = Levels.All();
            for (var i = 0; i < _levels.Length; i++)
            {
                if (_stages.Length != _levels.Length || _stages[i].LevelIndex != i)
                {
                    throw new InvalidOperationException($"Stage は面の順に {_levels.Length} 個並べてください（{_stages.Length} 個）。");
                }
            }

            Flow = new PlatformerFlow(_settings.Rules, _levels, _settings.InterludeTicks);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _startSound = Tone("Start", 520, 780, .1f);
            _jumpSound = Tone("Jump", 330, 660, .09f);
            _stompSound = Tone("Stomp", 880, 440, .08f);
            _clearSound = Tone("Clear", 660, 1320, .35f);
            _missSound = Tone("Miss", 400, 120, .4f);
            _wonSound = Tone("Won", 784, 1568, .6f);
            _lostSound = Tone("Lost", 260, 90, .7f);

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Hold(Control.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            Hold(Control.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Hold(Control.Run, "<Keyboard>/leftShift", "<Keyboard>/rightShift", "<Keyboard>/x");
            Hold(Control.Jump, "<Keyboard>/space", "<Keyboard>/z", "<Keyboard>/upArrow", "<Keyboard>/w");

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
            else if ((events & FlowEvents.Missed) != 0) { _audio.PlayOneShot(_missSound); }
            else if ((events & FlowEvents.Cleared) != 0) { _audio.PlayOneShot(_clearSound); }
            else if ((events & FlowEvents.Stomped) != 0) { _audio.PlayOneShot(_stompSound); }
            else if ((events & FlowEvents.Jumped) != 0) { _audio.PlayOneShot(_jumpSound); }
            else if ((events & FlowEvents.Started) != 0) { _audio.PlayOneShot(_startSound); }
        }

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            var current = CurrentStage;
            foreach (var stage in _stages)
            {
                if (stage.gameObject.activeSelf != (stage == current)) { stage.gameObject.SetActive(stage == current); }
            }

            var run = Flow.Campaign?.Run;
            if (run == null) { current.RenderInitial(); }
            else { current.Render(run); }
            RenderCamera(current, run);
            RenderHud();
        }

        // カメラは描画だけに使う。面が替わったら自機に合わせ、以後は遊びを出たぶんだけ追う。
        private void RenderCamera(BrambleDashStage stage, LevelRun run)
        {
            _camera.orthographicSize = ViewHalfHeight;
            var level = _levels[stage.LevelIndex];
            var viewWidth = Mathf.Max(1, Mathf.RoundToInt(ViewHalfHeight * 2 * _camera.aspect * Units.Tile));
            var target = run?.Player.Body.CenterX ?? Level.PlaceOnTile(level.StartColumn, level.StartRow, Player.Width, Player.Height).CenterX;
            if (run == null || run != _cameraRun)
            {
                _cameraCenter = CameraFollow.Clamp(target, viewWidth, level.Map.PixelWidth);
                _cameraRun = run;
            }
            else
            {
                _cameraCenter = CameraFollow.Follow(_cameraCenter, target, _settings.CameraDeadZone, viewWidth, level.Map.PixelWidth);
            }

            var center = stage.World(_cameraCenter, level.Map.Height * Units.Tile / 2);
            _camera.transform.position = new Vector3(center.x, center.y, _camera.transform.position.z);
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
            var campaign = Flow.Campaign;
            var run = campaign?.Run;
            var state = (Flow.Screen, campaign?.LevelIndex ?? -1, campaign?.Lives ?? 0, campaign?.Score ?? 0, run?.RemainingSeconds ?? 0, Flow.BestScore, campaign?.Status ?? CampaignStatus.Playing);
            if (_shownHud == state) { return; }
            _shownHud = state;

            var screen = Flow.Screen;
            _root.Q("stats").style.display = campaign == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (campaign != null)
            {
                _root.Q<Label>("level").text = $"{campaign.LevelIndex + 1} 面  {campaign.Levels[campaign.LevelIndex].Name}";
                _root.Q<Label>("lives").text = "残機  " + new string('●', campaign.Lives) + new string('○', Math.Max(0, _settings.Rules.Lives - campaign.Lives));
                _root.Q<Label>("score").text = $"スコア  {campaign.Score}";
                _root.Q<Label>("time").text = $"時間  {run!.RemainingSeconds}";
                _root.Q<Label>("best").text = $"最高  {Flow.BestScore}";
            }

            var showPanel = screen != AppScreen.Playing;
            _root.Q("panel").style.display = showPanel ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<Label>("headline").text = Headline(screen, campaign);
            _root.Q<Label>("message").text = Message(screen, campaign, Flow.IsNewBest);
            Show("submit", screen == AppScreen.Title || screen == AppScreen.Paused || screen == AppScreen.Result);
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

        public static string Headline(AppScreen screen, Campaign campaign) => screen switch
        {
            AppScreen.Title => "BRAMBLE DASH",
            AppScreen.Playing => string.Empty,
            AppScreen.Paused => "ひと休み",
            AppScreen.Interlude => campaign?.Status == CampaignStatus.LevelCleared ? "面クリア！" : "ミス",
            _ => campaign?.Status == CampaignStatus.Won ? "ぜんぶ抜けた！" : "ゲームオーバー",
        };

        public static string Message(AppScreen screen, Campaign campaign, bool newBest)
        {
            const string controls = "← →：歩く　Shift：走る\nSpace（長押しで高く）：跳ぶ\nEsc：一時停止";
            switch (screen)
            {
                case AppScreen.Title:
                    return "茨の野を駆けて、旗の柱を目指そう。\n全 3 面。\n\n"
                        + "虫は上から踏めば倒せる。\n横から触れるとミス。\nトゲの玉は踏めないので跳び越える。\n穴に落ちてもミス。\n\n" + controls;
                case AppScreen.Paused:
                    return "R：1 面から\nT：タイトルへ";
                case AppScreen.Interlude:
                    if (campaign?.Status == CampaignStatus.LevelCleared)
                    {
                        return $"残り時間ボーナス  {campaign.LastTimeBonus}\n次は {campaign.LevelIndex + 2} 面「{campaign.Levels[campaign.LevelIndex + 1].Name}」";
                    }

                    return $"{MissText(campaign?.Run.MissReason ?? MissReason.None)}\n残機 {campaign?.Lives ?? 0}。この面の最初から。";
                case AppScreen.Result:
                    var best = newBest ? "\n最高スコアを更新！" : string.Empty;
                    return campaign?.Status == CampaignStatus.Won
                        ? $"3 面すべてクリア。\nスコア  {campaign.Score}{best}"
                        : $"{(campaign == null ? 0 : campaign.LevelIndex + 1)} 面で残機がなくなった。\nスコア  {campaign?.Score ?? 0}{best}";
                default:
                    return string.Empty;
            }
        }

        private static string MissText(MissReason reason) => reason switch
        {
            MissReason.Enemy => "敵に触れた。",
            MissReason.Pit => "穴に落ちた。",
            MissReason.TimeUp => "時間切れ。",
            _ => string.Empty,
        };

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
