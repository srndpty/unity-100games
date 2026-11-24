using System;
using System.Collections.Generic;
using System.Globalization;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// 入力・HUD・音。キーを起きた時刻つきで 1 本の列に入れ、フレームの初めに起きた順で <see cref="PinballFlow"/> へ渡す（012・022）。
    /// フリッパーとプランジャーは押下と解放の両方をキーごとに拾う（PassThrough、032 の知見：束ねたキーのロールオーバー）。
    /// 1 つの操作に割り当てた複数のキーは、組の中の番号つきの意図にし、どれが押されているかは Flow が持つ。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class CometCoilGame : MonoBehaviour
    {
        private static readonly (Key Key, string Path)[] _leftKeys = { (Key.Z, "<Keyboard>/z"), (Key.LeftArrow, "<Keyboard>/leftArrow"), (Key.LeftShift, "<Keyboard>/leftShift") };
        private static readonly (Key Key, string Path)[] _rightKeys = { (Key.Slash, "<Keyboard>/slash"), (Key.RightArrow, "<Keyboard>/rightArrow"), (Key.RightShift, "<Keyboard>/rightShift") };
        private static readonly (Key Key, string Path)[] _plungerKeys = { (Key.Space, "<Keyboard>/space") };

        [SerializeField] private CometCoilSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private CometCoilStage _stage;

        private readonly List<Intent> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private PinballRun _preview;
        private bool _hasFocus = true;
        private VisualElement _root;
        private int _version = -1;
        private object _shownHud;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _flipSound;
        private AudioClip _bumperSound;
        private AudioClip _targetSound;
        private AudioClip _launchSound;
        private AudioClip _multiballSound;
        private AudioClip _lostSound;
        private AudioClip _overSound;

        public PinballFlow Flow { get; private set; }

        public CometCoilSettings Settings => _settings;

        public CometCoilStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した画面の出来事（撮影台本・テストが場面を待つのに使う）。</summary>
        public FlowEvents LastEvents { get; private set; }

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した試合の出来事の合計。</summary>
        public TickEvents LastTickEvents { get; private set; }

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            var table = _settings.Validate();
            var mismatch = _stage.Mismatch(table, _settings.Rules);
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が台のデータと合いません：" + mismatch); }

            // 再戦のシードは起動時の値から試合ごとに進める（010 の知見）。撮影では固定のシード。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            Flow = new PinballFlow(_settings.Rules, table, seed);
            _stage.Configure(_settings.Rules);

            // タイトルの背景に出す台の初期状態。描画のために毎フレーム作らないよう、起動時に 1 回だけ作る（008 の知見）。
            _preview = new PinballRun(_settings.Rules, table, 0);

            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _flipSound = Tone("Flip", 300, 160, .04f);
            _bumperSound = Tone("Bumper", 1400, 1900, .05f);
            _targetSound = Tone("Target", 880, 660, .08f);
            _launchSound = Tone("Launch", 200, 900, .18f);
            _multiballSound = Tone("Multiball", 520, 1560, .5f);
            _lostSound = Tone("Lost", 520, 140, .45f);
            _overSound = Tone("Over", 300, 70, .9f);

            Track(IntentKind.LeftDown, IntentKind.LeftUp, _leftKeys);
            Track(IntentKind.RightDown, IntentKind.RightUp, _rightKeys);
            Track(IntentKind.PlungerDown, IntentKind.PlungerUp, _plungerKeys);
            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.Cancel, "<Keyboard>/escape");
            Bind(IntentKind.Retry, "<Keyboard>/r");
            Bind(IntentKind.Back, "<Keyboard>/backspace");
            foreach (var action in _actions) { action.Enable(); }

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        /// <summary>
        /// キーの組の押下・解放を、組の中のキーの番号つきで意図にする。どのキーが押されているかは Flow がキーごとに持つ
        /// （一時停止・フォーカス喪失で Flow が全て離したとき、Unity 側の集約状態と食い違わない）。
        /// </summary>
        private void Track(IntentKind down, IntentKind up, (Key Key, string Path)[] keys)
        {
            var bindings = new string[keys.Length];
            for (var i = 0; i < keys.Length; i++) { bindings[i] = keys[i].Path; }
            var action = NewAction(down.ToString(), InputActionType.PassThrough, bindings);
            action.performed += context =>
            {
                if (context.control is not KeyControl control) { return; }
                for (var i = 0; i < keys.Length; i++)
                {
                    if (keys[i].Key != control.keyCode) { continue; }
                    Queue(new Intent(control.isPressed ? down : up, EventTime(context), i));
                    return;
                }
            };
        }

        private void Bind(IntentKind kind, params string[] bindings)
        {
            var action = NewAction(kind.ToString(), InputActionType.Button, bindings);
            action.performed += context => Queue(new Intent(kind, EventTime(context)));
        }

        /// <summary>入力イベントが起きた時刻。今より後にはしない。</summary>
        private static double EventTime(InputAction.CallbackContext context) => Math.Min(context.time, Now);

        private InputAction NewAction(string name, InputActionType type, params string[] bindings)
        {
            var action = new InputAction(name, type);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            _actions.Add(action);
            return action;
        }

        private void Queue(Intent intent)
        {
            if (AcceptInput()) { _intents.Add(intent); }
        }

        private void Update()
        {
            if (AcceptInput())
            {
                foreach (var intent in _intents) { Apply(intent); }
            }

            _intents.Clear();

            // 撮影中は台本が Tick を進める。timeScale が 0 の間（テストが Tick を直接進めるとき）も時計で進めない。
            if (!EvidenceCapture.IsRunning && Time.timeScale > 0) { Flow.Advance(Now); }
            else { Flow.EndFrame(); }
            ConsumeEvents();
            Render();
        }

        /// <summary>意図を Flow へ渡す（撮影台本も使う）。フリッパーを上げたら音を鳴らす。</summary>
        public void Apply(Intent intent)
        {
            Flow.Apply(intent);
            if (Flow.Screen == AppScreen.Playing && (intent.Kind == IntentKind.LeftDown || intent.Kind == IntentKind.RightDown)) { _audio.PlayOneShot(_flipSound); }
        }

        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }

            // 同じフレームに届いていた入力も捨てる（フォーカス喪失の後に再開しないように）。押していたキーは Flow が離したとみなす。
            Flow.SetFocus(focus);
            _intents.Clear();
        }

        /// <summary>出来事を音と演出にする。撮影台本も Tick を進めた後に呼ぶ。</summary>
        public void ConsumeEvents()
        {
            LastEvents = Flow.TakeEvents();
            var events = Flow.TakeTickEvents();
            LastTickEvents = events;
            var run = Flow.Run;
            if (run != null && events.BumpersLit != 0) { _stage.Flash(events.BumpersLit, run.Ticks); }

            if (events.GameOver) { _audio.PlayOneShot(_overSound); }
            else if (events.BallEnded) { _audio.PlayOneShot(_lostSound); }
            if (events.MultiballStarted) { _audio.PlayOneShot(_multiballSound); }
            if (events.BumperHits > 0) { _audio.PlayOneShot(_bumperSound); }
            if (events.TargetHits > 0) { _audio.PlayOneShot(_targetSound); }
            if (events.Launches > 0 && !events.MultiballStarted) { _audio.PlayOneShot(_launchSound); }
        }

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            _stage.Render(Flow.Run ?? _preview, _settings.FlashTicks);
            RenderHud();
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            _shownHud = null;
            root.style.unityFont = _font;
            root.Q<Label>("controls").text = Controls;
            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null) { return; }
            var run = Flow.Run;
            var screen = Flow.Screen;
            var state = (screen, run, run?.Score ?? 0, run?.BallNumber ?? 0, run?.IsMultiball ?? false, Targets(run), Flow.BestScore, Flow.IsNewBest);
            if (Equals(_shownHud, state)) { return; }
            _shownHud = state;

            _root.Q<Label>("score").text = (run?.Score ?? 0).ToString("N0", CultureInfo.InvariantCulture);
            _root.Q<Label>("ball").text = run == null ? $"BALL  - / {Flow.Config.Balls}" : $"BALL  {run.BallNumber} / {Flow.Config.Balls}";
            _root.Q<Label>("best").text = Flow.BestScore == 0 ? "BEST  —" : "BEST  " + Flow.BestScore.ToString("N0", CultureInfo.InvariantCulture);
            _root.Q<Label>("targets").text = "ターゲット  " + Targets(run);
            _root.Q<Label>("multiball").style.display = run != null && run.IsMultiball && screen != AppScreen.Result ? DisplayStyle.Flex : DisplayStyle.None;

            var panel = _root.Q("panel");
            panel.style.display = screen == AppScreen.Playing ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<Label>("headline").text = Headline(screen);
            _root.Q<Label>("message").text = Message(Flow);
        }

        /// <summary>ターゲットの状態（● 起きている・○ 倒れた）。</summary>
        private static string Targets(PinballRun run)
        {
            if (run == null) { return "● ● ●"; }
            var text = string.Empty;
            for (var i = 0; i < Table.TargetCount; i++) { text += (i == 0 ? string.Empty : " ") + (run.TargetUp(i) ? "●" : "○"); }
            return text;
        }

        public const string Controls =
            "左フリッパー    Z ・ ← ・ 左 Shift\n" +
            "右フリッパー    / ・ → ・ 右 Shift\n" +
            "プランジャー    Space を押して引き、離して打つ\n" +
            "一時停止        Esc";

        public static string Headline(AppScreen screen) => screen switch
        {
            AppScreen.Title => "COMET COIL",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => "GAME OVER",
            _ => string.Empty,
        };

        public static string Message(PinballFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var config = flow.Config;
            switch (flow.Screen)
            {
                case AppScreen.Title:
                    return $"フリッパーで球を打ち返して、{config.Balls} 球を失うまでに得点を重ねよう。\n" +
                        $"バンパー {config.BumperScore} 点・ターゲット {config.TargetScore} 点。ターゲットを 3 枚倒すとマルチボール！\n\n" +
                        "Space / Enter：はじめる";
                case AppScreen.Paused:
                    return "Esc / Enter：つづける    R：やり直す    Backspace：タイトルへ";
                case AppScreen.Result:
                    var run = flow.Run;
                    var score = (run?.Score ?? 0).ToString("N0", CultureInfo.InvariantCulture);
                    var best = flow.IsNewBest ? "  <color=#FFD66E>最高記録！</color>" : string.Empty;
                    return $"得点 {score}{best}\n\nSpace / Enter：もう一度    Esc：タイトルへ";
                default:
                    return string.Empty;
            }
        }

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
