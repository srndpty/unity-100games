using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using TileMotion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// 入力・カメラ・HUD・音をつなぐ。移動キーとマウスのボタンは時刻付きの意図の列（<see cref="Intent"/>）に、
    /// マウスの位置はフレームごとの照準点（<see cref="SiegeFlow.SetAim"/>）にして、別々の経路で App へ渡す。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class HaloSiegeGame : MonoBehaviour
    {
        [SerializeField] private HaloSiegeSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private HaloSiegeStage _stage;

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
        private SiegeConfig _rules;
        private Font _font;
        private AudioSource _audio;

        public SiegeFlow Flow { get; private set; }

        public HaloSiegeSettings Settings => _settings;

        public HaloSiegeStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した出来事（撮影台本が場面を待つのに使う）。</summary>
        public FlowEvents LastEvents { get; private set; }

        /// <summary>照準カーソルを描く位置（操縦役が動かしているときはその狙い、そうでなければマウスの位置）。</summary>
        public Vec2i CrosshairPoint => Flow.Pilot != null && Flow.Run != null ? Flow.LastCommand.Aim : Flow.Aim;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>カメラの中心（ワールド座標）：自機から決めた画面の範囲（<see cref="View.Of"/>）の中央。試合の前はアリーナの中央。</summary>
        public Vector3 ViewCenter
        {
            get
            {
                var rect = Flow?.Run?.View ?? View.Of(new Vec2i(_rules.ArenaWidth / 2, _rules.ArenaHeight / 2), _rules.ArenaWidth, _rules.ArenaHeight, _rules.ViewWidth, _rules.ViewHeight);
                var center = new Vec2i(rect.MinX + (rect.Width / 2), rect.MinY + (rect.Height / 2));
                return _stage.transform.TransformPoint(HaloSiegeStage.Local(center));
            }
        }

        private void Awake()
        {
            _settings.Validate();
            _rules = _settings.Rules.Copy();
            var mismatch = _stage.Mismatch(_rules);
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が設定のアリーナと一致しません：" + mismatch); }

            // 再戦は新しいシード（起動時の値から試合ごとに 1 進める。010 の知見）。撮影中は固定のシード。
            Flow = new SiegeFlow(_rules, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            _sounds.Add((FlowEvents.Won, Tone("Won", 660, 1320, .7f)));
            _sounds.Add((FlowEvents.Lost, Tone("Lost", 440, 80, .6f)));
            _sounds.Add((FlowEvents.Hurt, Tone("Hurt", 320, 110, .25f)));
            _sounds.Add((FlowEvents.EnemyDestroyed, Tone("Destroyed", 900, 160, .12f)));
            _sounds.Add((FlowEvents.EnemyHit, Tone("Hit", 520, 420, .04f)));
            _sounds.Add((FlowEvents.Started, Tone("Start", 520, 780, .12f)));
            _sounds.Add((FlowEvents.EnemyWarned, Tone("Warn", 240, 300, .05f)));
            _sounds.Add((FlowEvents.Fired, Tone("Fire", 1400, 1000, .025f)));

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Hold(Control.Left, "<Keyboard>/a", "<Keyboard>/leftArrow");
            Hold(Control.Right, "<Keyboard>/d", "<Keyboard>/rightArrow");
            Hold(Control.Up, "<Keyboard>/w", "<Keyboard>/upArrow");
            Hold(Control.Down, "<Keyboard>/s", "<Keyboard>/downArrow");

            // HUD のボタンの上で押したクリックは撃たない（ボタンの操作として扱う）。
            var fire = NewAction(nameof(Control.Fire), "<Mouse>/leftButton");
            fire.started += context => { if (!PointerOverButton()) { Queue(Intent.Down(Control.Fire, EventTime(context)), false); } };
            fire.canceled += context => Queue(Intent.Up(Control.Fire, EventTime(context)), false);

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

        private bool PointerOverButton()
        {
            if (_root?.panel == null || Mouse.current == null) { return false; }
            var point = Mouse.current.position.ReadValue();
            point.y = Screen.height - point.y;
            for (var element = _root.panel.Pick(RuntimePanelUtils.ScreenToPanel(_root.panel, point)); element != null; element = element.parent)
            {
                if (element is Button) { return true; }
            }

            return false;
        }

        private void Update()
        {
            // 照準は「今のマウスの位置」。前のフレームのカメラ（画面に見えていた位置）でアリーナの座標に直す。
            if (AcceptInput() && Mouse.current != null) { Flow.SetAim(PointerToCore(Mouse.current.position.ReadValue())); }

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

        /// <summary>画面の座標（ピクセル）をアリーナの座標（Core の単位）へ。</summary>
        public Vec2i PointerToCore(Vector2 screen)
        {
            var world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z));
            return HaloSiegeStage.ToCore(_stage.transform.InverseTransformPoint(world));
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
            if (run == null) { _stage.RenderInitial(_rules); }
            else { _stage.Render(run, CrosshairPoint, Flow.Screen == AppScreen.Playing || EvidenceCapture.IsRunning, _rules); }
            Frame();

            // 照準カーソルを描くプレイ中は、OS のカーソルを隠す。
            UnityEngine.Cursor.visible = Flow.Screen != AppScreen.Playing;
            RenderHud();
        }

        /// <summary>カメラは画面の範囲（32×18 セル）が収まる大きさにし、その中央に置く。</summary>
        private void Frame()
        {
            var height = _rules.ViewHeightCells / 2f;
            _camera.orthographicSize = Mathf.Max(height, _rules.ViewWidthCells / 2f / Mathf.Max(.01f, _camera.aspect));
            var center = ViewCenter;
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

        private void Connect(string name, IntentKind kind) => _root.Q<Button>(name).clicked += () => Queue(Intent.Of(kind, Now), true);

        // 文字とボタンは表示が変わるときだけ書き換える。残り時間は秒単位でしか変わらない。
        private void RenderHud()
        {
            if (_root == null) { return; }
            var run = Flow.Run;
            var screen = Flow.Screen;
            var seconds = run == null ? 0 : (run.RemainingTicks + Units.TickRate - 1) / Units.TickRate;
            var alive = run?.Enemies.Count ?? 0;
            var state = (screen, run?.Status, seconds, run?.Score ?? 0, run?.Ship.Lives ?? 0, alive);
            if (Equals(_shownHud, state)) { return; }
            _shownHud = state;
            _root.Q("stats").style.display = run == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (run != null)
            {
                _root.Q<Label>("time").text = $"残り {seconds / 60}:{seconds % 60:00}";
                _root.Q<Label>("score").text = $"SCORE {run.Score}";
                _root.Q<Label>("lives").text = new string('◆', run.Ship.Lives) + new string('◇', Math.Max(0, _rules.Lives - run.Ship.Lives));
                _root.Q<Label>("enemies").text = $"敵 {alive} / {_rules.MaxAlive}";
            }

            _root.Q("panel").style.display = screen != AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<Label>("headline").text = Headline(screen, run);
            _root.Q<Label>("message").text = Message(screen, run, _rules);
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

        /// <summary>Tick 数を「分:秒」にする（切り捨て）。</summary>
        public static string FormatTicks(long ticks)
        {
            var seconds = ticks / Units.TickRate;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        public static string Headline(AppScreen screen, SiegeRun run) => screen switch
        {
            AppScreen.Title => "HALO SIEGE",
            AppScreen.Paused => "一時停止",
            AppScreen.Result when run?.Status == RunStatus.Won => "守り抜いた！",
            AppScreen.Result => "撃墜された…",
            _ => string.Empty,
        };

        public static string Message(AppScreen screen, SiegeRun run, SiegeConfig rules)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            const string controls = "W A S D：動く    マウス：狙う    左クリック：撃つ（押したまま連射）\nEsc：一時停止";
            switch (screen)
            {
                case AppScreen.Title:
                    return $"包囲された光輪の要塞で {FormatTicks(rules.DurationTicks)} 生き残れ。\n動く向きと撃つ向きは別々に決められる。\n" +
                        $"敵は光の輪の予告から現れる。触れると残機が減り、少しの間は無敵になる。残機 {rules.Lives}。\n\n" + controls;
                case AppScreen.Paused:
                    return "R：最初から    T：タイトルへ";
                case AppScreen.Result when run != null:
                    var bonus = run.Status == RunStatus.Won ? $"\n残機ボーナス  {run.Ship.Lives} × {rules.LifeBonus}" : string.Empty;
                    return $"SCORE  {run.Score}\n生き残った時間  {FormatTicks(run.Elapsed)}    撃破  {run.Kills}{bonus}\n\nR：最初から    T：タイトルへ";
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
            UnityEngine.Cursor.visible = true;
            Destroy(_font);
            foreach (var clip in _clips) { Destroy(clip); }
        }
    }
}
