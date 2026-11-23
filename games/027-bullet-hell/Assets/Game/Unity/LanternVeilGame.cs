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
    /// 入力・カメラ・HUD・音をつなぐ。キーの押下・解放と画面の操作は、起きた時刻付きの意図の列（<see cref="Intent"/>）にして
    /// 起きた順に App へ渡す（026 と同じ）。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class LanternVeilGame : MonoBehaviour
    {
        /// <summary>画面の高さに映すワールド単位（プレイ領域 28 単位＋上下の余白）。</summary>
        public const float ViewHeight = 30f;

        /// <summary>プレイ領域の左端から画面の左端までの余白（ワールド単位）。右側は HUD に使う。</summary>
        public const float LeftMargin = 1.5f;

        /// <summary>ショットの音を鳴らす最短の間隔（秒）。毎秒 15 回撃つので間引く。</summary>
        private const float ShotSoundInterval = .12f;

        [SerializeField] private LanternVeilSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private LanternVeilStage _stage;

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
        private VeilConfig _rules;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _shotClip;
        private float _lastShotSound = -1;

        public VeilFlow Flow { get; private set; }

        public LanternVeilSettings Settings => _settings;

        public LanternVeilStage Stage => _stage;

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
            var bosses = _settings.Validate();
            _rules = _settings.Rules.Copy();
            var mismatch = _stage.Mismatch(_rules);
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が設定と一致しません：" + mismatch); }
            _stage.Prepare(_rules);

            // 再戦・再挑戦は新しいシード（起動時の値から試合ごとに 1 進める。010 の知見）。撮影中は固定のシード。
            Flow = new VeilFlow(_rules, bosses, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす（ショットは別に間引いて鳴らす）。
            _sounds.Add((FlowEvents.Won, Tone("Won", 660, 1320, .8f)));
            _sounds.Add((FlowEvents.Lost, Tone("Lost", 440, 80, .7f)));
            _sounds.Add((FlowEvents.Hurt, Tone("Hurt", 320, 90, .3f)));
            _sounds.Add((FlowEvents.Bomb, Tone("Bomb", 120, 600, .5f)));
            _sounds.Add((FlowEvents.BossDefeated, Tone("BossDefeated", 400, 1200, .5f)));
            _sounds.Add((FlowEvents.PatternDefeated, Tone("PatternDefeated", 700, 1100, .25f)));
            _sounds.Add((FlowEvents.PatternTimedOut, Tone("PatternTimedOut", 500, 420, .25f)));
            _sounds.Add((FlowEvents.PatternStarted, Tone("PatternStarted", 260, 360, .15f)));
            _sounds.Add((FlowEvents.Started, Tone("Start", 520, 780, .12f)));
            _sounds.Add((FlowEvents.BossHit, Tone("Hit", 900, 700, .02f)));
            _shotClip = Tone("Shot", 1500, 1100, .02f);

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.RetryBoss, "<Keyboard>/c");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Hold(Control.Left, "<Keyboard>/a", "<Keyboard>/leftArrow");
            Hold(Control.Right, "<Keyboard>/d", "<Keyboard>/rightArrow");
            Hold(Control.Up, "<Keyboard>/w", "<Keyboard>/upArrow");
            Hold(Control.Down, "<Keyboard>/s", "<Keyboard>/downArrow");
            Hold(Control.Slow, "<Keyboard>/leftShift", "<Keyboard>/rightShift");
            Hold(Control.Shot, "<Keyboard>/z");
            Hold(Control.Bomb, "<Keyboard>/x");

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Bind(IntentKind kind, params string[] bindings) =>
            NewAction(kind.ToString(), bindings).performed += context => Queue(Intent.Of(kind, EventTime(context)), false);

        // 押している間だけ効くキー。押した・離したの両方を、起きた時刻つきで列に入れる。
        private void Hold(Control control, params string[] bindings)
        {
            var action = NewAction(control.ToString(), bindings);
            action.started += context => Queue(Intent.Down(control, EventTime(context)), false);
            action.canceled += context => Queue(Intent.Up(control, EventTime(context)), false);
        }

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
                    // ウィンドウを選び直すクリックで HUD のボタンを押さない（010 の知見）。
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
                return;
            }

            if ((events & FlowEvents.Shot) != 0 && Time.realtimeSinceStartup - _lastShotSound >= ShotSoundInterval)
            {
                _lastShotSound = Time.realtimeSinceStartup;
                _audio.PlayOneShot(_shotClip, .5f);
            }
        }

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            var run = Flow.Run;
            if (run == null) { _stage.RenderInitial(_rules); }
            else { _stage.Render(run, _rules); }
            Frame(_camera);
            RenderHud();
        }

        /// <summary>
        /// カメラは画面の高さに <see cref="ViewHeight"/> 単位を映し、プレイ領域の左端を画面の左から <see cref="LeftMargin"/> に置く。
        /// 画面の右側（領域より右）は HUD に使う。横長（16:10〜21:9）ならどの比率でも領域は切れない。
        /// </summary>
        public static void Frame(Camera camera)
        {
            if (camera == null) { throw new ArgumentNullException(nameof(camera)); }
            camera.orthographicSize = ViewHeight / 2;
            var halfWidth = ViewHeight / 2 * camera.aspect;
            var field = LanternVeilStage.FieldSize;
            camera.transform.position = new Vector3(halfWidth - LeftMargin, field.y / 2, camera.transform.position.z);
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            _shownHud = null;
            root.style.unityFont = _font;
            Connect("submit", IntentKind.Submit);
            Connect("retry", IntentKind.RetryBoss);
            Connect("pause", IntentKind.TogglePause);
            Connect("restart", IntentKind.Restart);
            Connect("title", IntentKind.BackToTitle);
            Connect("quit", IntentKind.Quit);
            root.Query<Button>().ForEach(button => button.focusable = false);
            RenderHud();
        }

        private void Connect(string name, IntentKind kind) => _root.Q<Button>(name).clicked += () => Queue(Intent.Of(kind, Now), true);

        // 文字とボタンは表示が変わるときだけ書き換える。残り時間は秒単位でしか変わらない。
        private void RenderHud()
        {
            if (_root == null) { return; }
            var run = Flow.Run;
            var screen = Flow.Screen;
            var seconds = run == null ? 0 : (run.PatternTicksLeft + Field.TicksPerSecond - 1) / Field.TicksPerSecond;
            var state = (screen, run?.Status, run?.StageIndex, run?.Phase, seconds, run?.Score ?? 0, run?.Lives ?? 0, run?.Bombs ?? 0, run?.Bullets.Count ?? 0, Flow.CanRetryBoss);
            if (Equals(_shownHud, state)) { return; }
            _shownHud = state;

            _root.Q("side").style.display = run == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (run != null)
            {
                _root.Q<Label>("score").text = $"SCORE  {run.Score}";
                _root.Q<Label>("lives").text = "残機  " + new string('◆', run.Lives) + new string('◇', Math.Max(0, _rules.Lives - run.Lives));
                _root.Q<Label>("bombs").text = "ボム  " + new string('★', run.Bombs) + new string('☆', Math.Max(0, _rules.Bombs - run.Bombs));
                _root.Q<Label>("boss").text = $"BOSS {run.BossIndex + 1}  {run.CurrentBoss.Name}";
                _root.Q<Label>("pattern").text = $"弾幕 {run.StageIndex + 1} / {VeilConfig.BossCount * VeilConfig.PatternsPerBoss}「{run.CurrentPattern.Name}」";
                _root.Q<Label>("time").text = run.Phase == RunPhase.Pattern ? $"残り {seconds} 秒" : "次の弾幕へ…";
                _root.Q<Label>("bullets").text = $"弾 {run.Bullets.Count} / {_rules.MaxBullets}";
            }

            _root.Q("panel").style.display = screen != AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<Label>("headline").text = Headline(screen, run);
            _root.Q<Label>("message").text = Message(screen, run, _rules);
            Show("submit", screen != AppScreen.Playing);
            _root.Q<Button>("submit").text = screen switch
            {
                AppScreen.Title => "はじめる  /  Enter",
                AppScreen.Paused => "つづける  /  Enter",
                _ => "最初から  /  Enter",
            };
            Show("retry", Flow.CanRetryBoss);
            Show("pause", screen == AppScreen.Playing);
            Show("restart", screen == AppScreen.Paused);
            Show("title", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("quit", screen == AppScreen.Title);
        }

        /// <summary>Tick 数を「分:秒」にする（切り捨て）。</summary>
        public static string FormatTicks(long ticks)
        {
            var seconds = ticks / Field.TicksPerSecond;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        public static string Headline(AppScreen screen, VeilRun run) => screen switch
        {
            AppScreen.Title => "LANTERN VEIL",
            AppScreen.Paused => "一時停止",
            AppScreen.Result when run?.Status == RunStatus.Won => "帳は晴れた！",
            AppScreen.Result => "灯が消えた…",
            _ => string.Empty,
        };

        public static string Message(AppScreen screen, VeilRun run, VeilConfig rules)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            const string controls = "← → ↑ ↓ / W A S D：動く    Shift：低速（当たり判定の点を表示）\nZ：ショット（押したまま連射）    X：ボム    Esc：一時停止";
            switch (screen)
            {
                case AppScreen.Title:
                    return "2 体のボスが 3 つずつ放つ弾幕をくぐり抜けろ。\n" +
                        $"当たり判定は自機の中心の小さな点だけ。残機 {rules.Lives}、ボム {rules.Bombs}（被弾するとボムの数は戻る）。\n" +
                        "被弾もボムもなく弾幕を削りきるとボーナス。\n\n" + controls;
                case AppScreen.Paused:
                    return "R：最初から    T：タイトルへ";
                case AppScreen.Result when run != null:
                    var stage = run.Status == RunStatus.Won ? "6 弾幕すべて突破" : $"弾幕 {run.StageIndex + 1}「{run.CurrentPattern.Name}」で力尽きた";
                    var bonus = run.Status == RunStatus.Won ? $"\n残機ボーナス  {run.Lives} × {rules.LifeBonus}    ボムボーナス  {run.Bombs} × {rules.BombBonus}" : string.Empty;
                    var retry = run.Status == RunStatus.Lost ? $"C：ボス {run.BossIndex + 1} から再挑戦（得点 {run.BossStartScore} から）    " : string.Empty;
                    return $"SCORE  {run.Score}\n{stage}\nプレイ時間  {FormatTicks(run.Elapsed)}    被弾  {run.Hits}{bonus}\n\n{retry}Enter：最初から    T：タイトルへ";
                default:
                    return string.Empty;
            }
        }

        private void Show(string name, bool visible) =>
            _root.Q<Button>(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        // 音程を from から to へ動かす矩形波。振幅は控えめ（AGENTS.md の方針）。
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
