using System;
using System.Collections.Generic;
using System.Text;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using TileMotion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class HushWardGame : MonoBehaviour
    {
        [SerializeField] private HushWardSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private HushWardStage _stage;

        // 種類の違う操作も起きた順に 1 本の列で処理する。Pointer は HUD のボタン由来（フォーカス復帰直後は捨てる）。
        private readonly List<(Intent Intent, bool Pointer)> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<(FlowEvents Event, AudioClip Clip)> _sounds = new();
        private readonly StringBuilder _debugText = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -2;
        private VisualElement _root;
        private int _version = -1;
        private object _shownHud;
        private string _shownDebug;
        private Facility _facility;
        private Font _font;
        private AudioSource _audio;
        private string _toast = string.Empty;
        private long _toastUntil;

        public WardFlow Flow { get; private set; }

        public HushWardSettings Settings => _settings;

        public HushWardStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した出来事（撮影台本が場面を待つのに使う）。</summary>
        public FlowEvents LastEvents { get; private set; }

        /// <summary>今出している案内（なければ空）。</summary>
        public string Toast => Flow?.Match != null && Flow.Match.Ticks < _toastUntil ? _toast : string.Empty;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            _settings.Validate();
            _facility = Facility.Ward();
            var mismatch = _stage.Mismatch(_facility);
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が Core の施設と一致しません：" + mismatch); }

            // 再戦は新しいシード（起動時の値から試合ごとに 1 進める。010 の知見）。撮影中は固定のシード。
            Flow = new WardFlow(_settings.Rules, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            _sounds.Add((FlowEvents.Escaped, Tone("Escaped", 660, 1320, .7f)));
            _sounds.Add((FlowEvents.Caught, Tone("Caught", 330, 80, .6f)));
            _sounds.Add((FlowEvents.Discovered, Tone("Discovered", 1200, 1500, .3f)));
            _sounds.Add((FlowEvents.ObjectiveTaken, Tone("Objective", 784, 1175, .3f)));
            _sounds.Add((FlowEvents.Suspicious, Tone("Suspicious", 600, 760, .12f)));
            _sounds.Add((FlowEvents.LostTrack, Tone("LostTrack", 700, 420, .25f)));
            _sounds.Add((FlowEvents.Calmed, Tone("Calmed", 520, 390, .2f)));
            _sounds.Add((FlowEvents.Started, Tone("Start", 520, 780, .1f)));
            _sounds.Add((FlowEvents.Step, Tone("Step", 140, 110, .03f)));

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Bind(IntentKind.ToggleDebug, "<Keyboard>/f1");
            Hold(Control.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            Hold(Control.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Hold(Control.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            Hold(Control.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            Hold(Control.Run, "<Keyboard>/leftShift", "<Keyboard>/rightShift");
            Hold(Control.Sneak, "<Keyboard>/c", "<Keyboard>/leftCtrl");

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

        /// <summary>出来事を音と案内にする。撮影台本も Tick を進めた後に呼ぶ。</summary>
        public void ConsumeEvents()
        {
            var events = Flow.TakeEvents();
            LastEvents = events;
            if ((events & FlowEvents.Started) != 0)
            {
                _toast = string.Empty;
                _toastUntil = 0;
            }

            var m = Flow.Match;
            if (m != null)
            {
                if ((events & FlowEvents.Discovered) != 0) { ShowToast(m, "見つかった！  走って視界から逃れろ"); }
                else if ((events & FlowEvents.ObjectiveTaken) != 0) { ShowToast(m, "機密ファイルを手に入れた。入口へ戻れ"); }
                else if ((events & FlowEvents.LostTrack) != 0) { ShowToast(m, "見失ったようだ。まだ探している"); }
                else if ((events & FlowEvents.Calmed) != 0) { ShowToast(m, m.Level == AlertLevel.Normal ? "警戒が解けた" : "厳戒が解けた。まだ警戒している"); }
            }

            foreach (var pair in _sounds)
            {
                if ((events & pair.Event) == 0) { continue; }
                _audio.PlayOneShot(pair.Clip);
                break;
            }
        }

        private void ShowToast(Infiltration m, string text)
        {
            _toast = text;
            _toastUntil = m.Ticks + _settings.ToastTicks;
        }

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            var m = Flow.Match;
            if (m == null) { _stage.RenderInitial(_facility, _settings.Rules, _settings.FanRays); }
            else { _stage.Render(m, _settings.FanRays, Flow.ShowDebug, _settings.NoiseShowTicks); }
            Frame();
            RenderHud();
        }

        /// <summary>カメラは施設全体が収まる大きさにして中央に置く。上端の壁の行に HUD の帯が重なる。</summary>
        private void Frame()
        {
            var map = _facility.Map;
            _camera.orthographicSize = Mathf.Max(map.Height / 2f, map.Width / 2f / Mathf.Max(.01f, _camera.aspect));
            var center = _stage.transform.TransformPoint(new Vector3(map.Width / 2f, map.Height / 2f, 0));
            _camera.transform.position = new Vector3(center.x, center.y, _camera.transform.position.z);
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            _shownHud = null;
            _shownDebug = null;
            root.style.unityFont = _font;
            Connect("submit", IntentKind.Submit);
            Connect("pause", IntentKind.TogglePause);
            Connect("restart", IntentKind.Restart);
            Connect("title", IntentKind.BackToTitle);
            Connect("quit", IntentKind.Quit);
            Connect("debug-toggle", IntentKind.ToggleDebug);
            root.Query<Button>().ForEach(button => button.focusable = false);
            RenderHud();
        }

        private void Connect(string name, IntentKind kind) => _root.Q<Button>(name).clicked += () => Queue(Intent.Of(kind, Now), true);

        // 文字とボタンは表示が変わるときだけ書き換える。時間は 1/100 秒単位でしか変わらない。
        private void RenderHud()
        {
            if (_root == null) { return; }
            var m = Flow.Match;
            var screen = Flow.Screen;
            var played = m == null ? 0 : Flow.PlayedTicks * 100 / Units.TickRate;
            var state = (screen, m?.Level ?? AlertLevel.Normal, m?.HasObjective ?? false, m?.Discoveries ?? 0, played, Toast, Flow.ShowDebug);
            if (!Equals(_shownHud, state))
            {
                _shownHud = state;
                _root.Q("stats").style.display = m == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (m != null)
                {
                    var level = _root.Q<Label>("level");
                    level.text = LevelName(m.Level);
                    level.EnableInClassList("level-caution", m.Level == AlertLevel.Caution);
                    level.EnableInClassList("level-alert", m.Level == AlertLevel.Alert);
                    var objective = _root.Q<Label>("objective");
                    objective.text = m.HasObjective ? "機密ファイル  回収済み → 入口へ" : "機密ファイル  未回収";
                    objective.EnableInClassList("owned", m.HasObjective);
                    _root.Q<Label>("time").text = FormatTicks(Flow.PlayedTicks);
                    _root.Q<Label>("discoveries").text = $"発見  {m.Discoveries}";
                }

                var toast = _root.Q<Label>("toast");
                toast.text = Toast;
                toast.style.display = Toast.Length > 0 && screen == AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;

                _root.Q("panel").style.display = screen != AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
                _root.Q<Label>("headline").text = Headline(screen);
                _root.Q<Label>("message").text = Message(screen, m, Flow.PlayedTicks);
                Show("submit", screen != AppScreen.Playing);
                _root.Q<Button>("submit").text = screen switch
                {
                    AppScreen.Title => "はじめる  /  Enter",
                    AppScreen.Paused => "つづける  /  Enter",
                    _ => "もう一度  /  Enter",
                };
                Show("pause", screen == AppScreen.Playing);
                Show("restart", screen == AppScreen.Paused || screen == AppScreen.Caught || screen == AppScreen.Escaped);
                Show("title", screen == AppScreen.Paused || screen == AppScreen.Caught || screen == AppScreen.Escaped);
                Show("quit", screen == AppScreen.Title);
                _root.Q<Button>("debug-toggle").text = Flow.ShowDebug ? "詳細 ON / F1" : "詳細 OFF / F1";
            }

            RenderDebugPanel(m);
        }

        /// <summary>F1 の詳しい表示：警備員ごとの状態・気づき値・向かう先と、警戒レベルの残り。</summary>
        private void RenderDebugPanel(Infiltration m)
        {
            var visible = Flow.ShowDebug && m != null;
            var panel = _root.Q("debug");
            panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) { return; }
            var text = DebugText(m, _debugText);
            if (text == _shownDebug) { return; }
            _shownDebug = text;
            _root.Q<Label>("debug-text").text = text;
        }

        /// <summary>警備員の状態の一覧（HUD の詳しい表示と撮影の記録で使う）。</summary>
        public static string DebugText(Infiltration m, StringBuilder builder)
        {
            if (m == null) { throw new ArgumentNullException(nameof(m)); }
            if (builder == null) { throw new ArgumentNullException(nameof(builder)); }
            builder.Clear();
            builder.Append(LevelName(m.Level));
            if (m.Level != AlertLevel.Normal) { builder.Append("  残り ").Append(m.AlertRemaining).Append(" Tick"); }
            foreach (var guard in m.Guards)
            {
                var brain = guard.Brain;
                builder.Append('\n').Append(guard.Plan.Name).Append("  ").Append(ModeName(brain.Mode))
                    .Append("  気づき ").Append(brain.Awareness)
                    .Append("  → (").Append(brain.Goal.Column).Append(", ").Append(brain.Goal.Row).Append(')');
                if (guard.Sees) { builder.Append("  見えている"); }
                if (guard.Hears) { builder.Append("  聞こえた"); }
                if (brain.Mode == GuardMode.Search) { builder.Append("  捜索残り ").Append(brain.SearchLeft); }
            }

            return builder.ToString();
        }

        public static string ModeName(GuardMode mode) => mode switch
        {
            GuardMode.Investigate => "調査",
            GuardMode.Chase => "追跡",
            GuardMode.Search => "捜索",
            _ => "巡回",
        };

        public static string LevelName(AlertLevel level) => level switch
        {
            AlertLevel.Caution => "警戒",
            AlertLevel.Alert => "厳戒",
            _ => "平常",
        };

        /// <summary>Tick 数を「分:秒.1/100 秒」にする。</summary>
        public static string FormatTicks(long ticks)
        {
            var hundredths = ticks * 100 / Units.TickRate;
            return $"{hundredths / 6000}:{hundredths / 100 % 60:00}.{hundredths % 100:00}";
        }

        public static string Headline(AppScreen screen) => screen switch
        {
            AppScreen.Title => "HUSH WARD",
            AppScreen.Paused => "一時停止",
            AppScreen.Caught => "捕まった…",
            AppScreen.Escaped => "脱出成功！",
            _ => string.Empty,
        };

        public static string Message(AppScreen screen, Infiltration m, long playedTicks)
        {
            const string controls = "← → ↑ ↓：歩く    Shift：走る（足音が大きい）    C：忍び足（足音が小さい）\nEsc：一時停止    F1：警備員の状態の詳しい表示";
            switch (screen)
            {
                case AppScreen.Title:
                    return "北東の資料室から機密ファイルを持ち出し、南西の入口へ戻れ。\n警備員の視界（扇）に入ると気づかれ、足音は壁を回り込んで届く。\n見つかっても、追ってくる警備員に触れられなければ逃げ切れる。\n\n" + controls;
                case AppScreen.Paused:
                    return "R：最初から    T：タイトルへ";
                case AppScreen.Caught:
                    return m == null ? string.Empty : $"追ってきた警備員に捕まった。\n遊んだ時間  {FormatTicks(playedTicks)}    発見  {m.Discoveries} 回\n\nEnter：もう一度    T：タイトルへ";
                case AppScreen.Escaped:
                    return m == null ? string.Empty : $"{(m.Discoveries == 0 ? "評価  未発見（ゴースト）" : $"発見  {m.Discoveries} 回")}\nタイム  {FormatTicks(playedTicks)}\n\nEnter：もう一度    T：タイトルへ";
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
