using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class GlintAscentGame : MonoBehaviour
    {
        /// <summary>部屋の上に空ける HUD の帯の高さ（タイル）。</summary>
        public const float HudBandTiles = 1.5f;

        [SerializeField] private GlintAscentSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private GlintAscentStage[] _stages = Array.Empty<GlintAscentStage>();

        // 種類の違う操作も起きた順に 1 本の列で処理する。Pointer は HUD のボタン由来（フォーカス復帰直後は捨てる）。
        private readonly List<(Intent Intent, bool Pointer)> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -2;
        private VisualElement _root;
        private int _version = -1;
        private (AppScreen, int, int, long, int, long?)? _shownHud;
        private ReadOnlyCollection<Room> _rooms;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _startSound;
        private AudioClip _jumpSound;
        private AudioClip _wallJumpSound;
        private AudioClip _dashSound;
        private AudioClip _deathSound;
        private AudioClip _clearSound;
        private AudioClip _wonSound;

        public PrecisionFlow Flow { get; private set; }

        public GlintAscentSettings Settings => _settings;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        public ReadOnlyCollection<Room> Rooms => _rooms;

        /// <summary>今の部屋の Stage（挑戦の前は 1 部屋目）。</summary>
        public GlintAscentStage CurrentStage => _stages[Flow?.Climb?.RoomIndex ?? 0];

        public GlintAscentStage StageAt(int index) => _stages[index];

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            _settings.Validate();
            _rooms = Core.Rooms.All();
            if (_stages.Length != _rooms.Count)
            {
                throw new InvalidOperationException($"Stage は部屋の順に {_rooms.Count} 個並べてください（{_stages.Length} 個）。");
            }

            for (var i = 0; i < _stages.Length; i++)
            {
                if (_stages[i].RoomIndex != i) { throw new InvalidOperationException($"{i} 番目の Stage が {_stages[i].RoomIndex} 部屋目を指しています。"); }
            }

            Flow = new PrecisionFlow(_settings.Rules, _rooms);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _startSound = Tone("Start", 520, 780, .1f);
            _jumpSound = Tone("Jump", 360, 620, .07f);
            _wallJumpSound = Tone("WallJump", 480, 760, .07f);
            _dashSound = Tone("Dash", 900, 300, .12f);
            _deathSound = Tone("Death", 420, 110, .3f);
            _clearSound = Tone("Clear", 660, 1320, .25f);
            _wonSound = Tone("Won", 784, 1568, .6f);

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Hold(Control.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            Hold(Control.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Hold(Control.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            Hold(Control.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            Hold(Control.Jump, "<Keyboard>/space", "<Keyboard>/z", "<Keyboard>/c");
            Hold(Control.Dash, "<Keyboard>/x", "<Keyboard>/leftShift", "<Keyboard>/rightShift");

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

                    // 入力の時刻までの Tick を進めてから反映する（フレームの区切りに依らない）。
                    Flow.Apply(intent);
                }
            }

            _intents.Clear();

            // 撮影中は台本が Tick を進める。timeScale が 0 の間（テストが Tick を直接進めるとき）も時計で進めない。
            if (!EvidenceCapture.IsRunning && Time.timeScale > 0) { Flow.Advance(Now); }
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
            else if ((events & FlowEvents.Died) != 0) { _audio.PlayOneShot(_deathSound); }
            else if ((events & FlowEvents.RoomCleared) != 0) { _audio.PlayOneShot(_clearSound); }
            else if ((events & FlowEvents.Dashed) != 0) { _audio.PlayOneShot(_dashSound); }
            else if ((events & FlowEvents.WallJumped) != 0) { _audio.PlayOneShot(_wallJumpSound); }
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

            var run = Flow.Climb?.Run;
            if (run == null) { current.RenderInitial(_rooms[current.RoomIndex]); }
            else { current.Render(run); }
            Frame(current);
            RenderHud();
        }

        // カメラは部屋全体と上の HUD の帯が収まる大きさにして、部屋の中央に固定する。
        private void Frame(GlintAscentStage stage)
        {
            var height = Room.Rows + HudBandTiles;
            _camera.orthographicSize = Mathf.Max(height / 2, Room.Columns / 2f / Mathf.Max(.01f, _camera.aspect));
            var center = stage.transform.TransformPoint(new Vector3(Room.Columns / 2f, (Room.Rows + HudBandTiles) / 2f, 0));
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

        // 文字とボタンは表示が変わるときだけ書き換える。時間は 1/100 秒単位でしか変わらない。
        private void RenderHud()
        {
            if (_root == null) { return; }
            var climb = Flow.Climb;
            var state = (Flow.Screen, climb?.RoomIndex ?? -1, climb?.Deaths ?? 0, (climb?.ElapsedTicks ?? 0) * 100 / Units.TickRate, climb?.Run.Player.DashesLeft ?? 0, Flow.BestTicks);
            if (_shownHud == state) { return; }
            _shownHud = state;

            var screen = Flow.Screen;
            _root.Q("stats").style.display = climb == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (climb != null)
            {
                _root.Q<Label>("room").text = $"{climb.RoomIndex + 1} / {climb.Rooms.Count}  {climb.Rooms[climb.RoomIndex].Name}";
                _root.Q<Label>("deaths").text = $"死亡  {climb.Deaths}";
                _root.Q<Label>("time").text = $"時間  {FormatTicks(climb.ElapsedTicks)}";
                var dashes = climb.Run.Player.DashesLeft;
                _root.Q<Label>("dash").text = "ダッシュ  " + new string('◆', dashes) + new string('◇', Math.Max(0, _settings.Rules.DashCount - dashes));
            }

            _root.Q("panel").style.display = screen != AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<Label>("headline").text = Headline(screen);
            _root.Q<Label>("message").text = Message(screen, climb, Flow.BestTicks, Flow.IsNewBest);
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

        /// <summary>Tick 数を「分:秒.1/100 秒」にする。</summary>
        public static string FormatTicks(long ticks)
        {
            var hundredths = ticks * 100 / Units.TickRate;
            return $"{hundredths / 6000}:{hundredths / 100 % 60:00}.{hundredths % 100:00}";
        }

        public static string Headline(AppScreen screen) => screen switch
        {
            AppScreen.Title => "GLINT ASCENT",
            AppScreen.Playing => string.Empty,
            AppScreen.Paused => "ひと休み",
            _ => "登りきった！",
        };

        public static string Message(AppScreen screen, Climb climb, long? bestTicks, bool newBest)
        {
            const string controls = "← → ↑ ↓：動く・ダッシュの向き\nSpace / Z：跳ぶ（壁際で壁蹴り）\nX / Shift：ダッシュ（空中で 1 回）\nEsc：一時停止";
            switch (screen)
            {
                case AppScreen.Title:
                    var best = bestTicks.HasValue ? $"\n最短  {FormatTicks(bestTicks.Value)}" : string.Empty;
                    return "10 の部屋を抜けて、山の上の光を目指そう。\nトゲに触れたり落ちたりしても、\nその部屋の入口からすぐやり直せる。\nダッシュは着地すると戻る。\n\n" + controls + best;
                case AppScreen.Paused:
                    return "R：1 部屋目から\nT：タイトルへ";
                case AppScreen.Result:
                    var record = newBest ? "\n最短記録を更新！" : string.Empty;
                    return climb == null
                        ? string.Empty
                        : $"10 部屋をすべて抜けた。\nタイム  {FormatTicks(climb.ElapsedTicks)}\n死亡  {climb.Deaths} 回{record}";
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
