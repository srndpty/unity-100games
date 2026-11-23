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
    /// <summary>
    /// 入力・カメラ・HUD・音をつなぐ。キーの押下と画面の操作は、起きた時刻つきの意図（<see cref="Intent"/>）の 1 本の列にして App へ渡す。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class CinderLanesGame : MonoBehaviour
    {
        [SerializeField] private CinderLanesSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private CinderLanesStage _stage;

        /// <summary>カメラの自機からの位置（m）。横は自機の横位置の <see cref="CameraFollow"/> 倍だけ追う。</summary>
        public static readonly Vector3 CameraOffset = new(0, 3.4f, -6.5f);

        public const float CameraFollow = .45f;

        // 種類の違う操作も起きた順に 1 本の列で処理する。Pointer は HUD のボタン由来（フォーカス復帰直後は捨てる）。
        private readonly List<(Intent Intent, bool Pointer)> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<(FlowEvents Event, AudioClip Clip)> _sounds = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -2;
        private VisualElement _root;
        private int _version = -1;
        private object _shownHud;
        private Font _font;
        private AudioSource _audio;

        public RunnerFlow Flow { get; private set; }

        public CinderLanesSettings Settings => _settings;

        public CinderLanesStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した出来事（撮影台本が場面を待つのに使う）。</summary>
        public FlowEvents LastEvents { get; private set; }

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            var rules = _settings.BuildRules();
            var mismatch = _stage.Mismatch(rules.Config);
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が設定と一致しません：" + mismatch); }
            _stage.Initialize(rules);

            // 再戦は新しいシード（起動時の値から試合ごとに 1 進める。010 の知見）。撮影中は固定のシード。
            Flow = new RunnerFlow(rules, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            _sounds.Add((FlowEvents.Crashed, Tone("Crash", 300, 60, .5f)));
            _sounds.Add((FlowEvents.SpedUp, Tone("SpeedUp", 600, 1200, .25f)));
            _sounds.Add((FlowEvents.Started, Tone("Start", 520, 780, .12f)));
            _sounds.Add((FlowEvents.Jumped, Tone("Jump", 420, 840, .1f)));
            _sounds.Add((FlowEvents.Slid, Tone("Slide", 500, 220, .1f)));
            _sounds.Add((FlowEvents.Steered, Tone("Steer", 700, 760, .04f)));
            _sounds.Add((FlowEvents.Landed, Tone("Land", 160, 120, .04f)));

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");

            // Space はタイトル・結果では決定、プレイ中はジャンプ（Flow がそれぞれの画面で片方だけを受け付ける）。
            Press(RunMove.Left, "<Keyboard>/a", "<Keyboard>/leftArrow");
            Press(RunMove.Right, "<Keyboard>/d", "<Keyboard>/rightArrow");
            Press(RunMove.Jump, "<Keyboard>/w", "<Keyboard>/upArrow", "<Keyboard>/space");
            Press(RunMove.Slide, "<Keyboard>/s", "<Keyboard>/downArrow");

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Bind(IntentKind kind, params string[] bindings) =>
            NewAction(kind.ToString(), bindings).performed += context => Queue(Intent.Of(kind, EventTime(context)), false);

        private void Press(RunMove move, params string[] bindings) =>
            NewAction(move.ToString(), bindings).performed += context => Queue(Intent.Press(move, EventTime(context)), false);

        /// <summary>入力イベントが起きた時刻。今より後にはしない。</summary>
        private static double EventTime(InputAction.CallbackContext context) => Math.Min(context.time, Now);

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

            // 撮影中は台本が Tick を進める。timeScale が 0 の間（テストが Tick を直接進めるとき）も時計で進めない。
            if (!EvidenceCapture.IsRunning && Time.timeScale > 0) { Flow.Advance(Now); }
            else { Flow.EndFrame(); }
            ConsumeEvents();
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

        /// <summary>出来事を音にする。撮影台本も Tick を進めた後に呼ぶ。</summary>
        public void ConsumeEvents()
        {
            var events = Flow.TakeEvents();
            LastEvents = events;
            foreach (var pair in _sounds)
            {
                if ((events & pair.Event) == 0) { continue; }
                _audio.PlayOneShot(pair.Clip);
                break;
            }
        }

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            var run = Flow.Run;
            if (run == null) { _stage.RenderInitial(); }
            else { _stage.Render(run); }
            Frame();
            RenderHud();
        }

        /// <summary>カメラは自機の後ろ上から前を見る。横は自機に少し遅れて追う（自機の横位置の一定の割合）。</summary>
        private void Frame()
        {
            var x = _stage.Runner.localPosition.x * CameraFollow;
            var origin = _stage.transform.TransformPoint(new Vector3(x, 0, 0));
            _camera.transform.position = origin + CameraOffset;
            _camera.transform.LookAt(origin + new Vector3(0, 1.2f, 10f));
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

        private void Connect(string name, IntentKind kind) => _root.Q<Button>(name).clicked += () => Queue(Intent.Of(kind, Now), true);

        // 文字とボタンは表示が変わるときだけ書き換える。距離は m 単位でしか変わらない。
        private void RenderHud()
        {
            if (_root == null) { return; }
            var run = Flow.Run;
            var screen = Flow.Screen;
            var level = run?.SpeedLevel ?? 0;
            var state = (screen, run?.Meters ?? 0, level, Flow.BestMeters, Flow.IsNewBest);
            if (Equals(_shownHud, state)) { return; }
            _shownHud = state;
            var config = Flow.Rules.Config;
            _root.Q("stats").style.display = run == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (run != null)
            {
                _root.Q<Label>("distance").text = $"{run.Meters} m";
                _root.Q<Label>("speed").text = $"SPEED {level + 1} / {config.MaxSpeedLevel + 1}";
                _root.Q<Label>("best").text = $"BEST {Math.Max(Flow.BestMeters, screen == AppScreen.Result ? 0 : run.Meters)} m";
            }

            _root.Q("panel").style.display = screen != AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<Label>("headline").text = Headline(screen);
            _root.Q<Label>("message").text = Message(screen, run, Flow.BestMeters, Flow.IsNewBest, config);
            Show("submit", screen != AppScreen.Playing);
            _root.Q<Button>("submit").text = screen switch
            {
                AppScreen.Title => "はじめる  /  Enter",
                AppScreen.Paused => "つづける  /  Enter",
                _ => "もう一度  /  Enter",
            };
            Show("pause", screen == AppScreen.Playing);
            Show("restart", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("title", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("quit", screen == AppScreen.Title);
        }

        public static string Headline(AppScreen screen) => screen switch
        {
            AppScreen.Title => "CINDER LANES",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => "ぶつかった！",
            _ => string.Empty,
        };

        public static string Message(AppScreen screen, RunnerRun run, long best, bool newBest, RunConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            const string controls = "← →：レーンを移る    ↑ / Space：ジャンプ    ↓：スライド    Esc：一時停止";
            switch (screen)
            {
                case AppScreen.Title:
                    return "燃えさしの道を走り続けろ。ぶつかったら終わり、進んだ距離が記録になる。\n" +
                        "低い柵は跳び越え、頭上の梁はスライドでくぐり、壁はレーンを移ってよける。\n" +
                        $"速さは {Difficulty.CapMeters(config)} m まで少しずつ上がる。\n\n" + controls;
                case AppScreen.Paused:
                    return "R：最初から    T：タイトルへ";
                case AppScreen.Result when run != null:
                    var record = newBest ? "  （最高記録！）" : string.Empty;
                    return $"距離  {run.Meters} m{record}\n最高記録  {best} m\n\nR：最初から    T：タイトルへ";
                default:
                    return string.Empty;
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
