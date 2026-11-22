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
    [RequireComponent(typeof(AudioSource))]
    public sealed class MossDepthsGame : MonoBehaviour
    {
        /// <summary>部屋の上に空ける HUD の帯の高さ（タイル）。</summary>
        public const float HudBandTiles = 1.5f;

        [SerializeField] private MossDepthsSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private MossDepthsStage[] _stages = Array.Empty<MossDepthsStage>();

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
        private object _shownMap;
        private World _world;
        private Font _font;
        private AudioSource _audio;
        private Ability _lastAbilities;
        private string _toast = string.Empty;
        private long _toastUntil;

        public ExploreFlow Flow { get; private set; }

        public MossDepthsSettings Settings => _settings;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        /// <summary>タイトルで描く世界（試合中は試合の世界）。</summary>
        public World World => Flow?.Expedition?.World ?? _world;

        /// <summary>今の部屋の Stage（試合の前は開始の部屋）。</summary>
        public MossDepthsStage CurrentStage => _stages[Flow?.Expedition?.RoomIndex ?? _world.StartRoom];

        public MossDepthsStage StageAt(int index) => _stages[index];

        /// <summary>今出している案内（なければ空）。</summary>
        public string Toast => Flow?.Expedition != null && Flow.Expedition.ElapsedTicks < _toastUntil ? _toast : string.Empty;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            _settings.Validate();
            _world = Rooms.Create();
            if (_stages.Length != _world.Rooms.Count)
            {
                throw new InvalidOperationException($"Stage は部屋の順に {_world.Rooms.Count} 個並べてください（{_stages.Length} 個）。");
            }

            for (var i = 0; i < _stages.Length; i++)
            {
                if (_stages[i].RoomIndex != i) { throw new InvalidOperationException($"{i} 番目の Stage が {_stages[i].RoomIndex} 部屋目を指しています。"); }
            }

            Flow = new ExploreFlow(_settings.Rules);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            _sounds.Add((FlowEvents.Won, Tone("Won", 784, 1568, .7f)));
            _sounds.Add((FlowEvents.Down, Tone("Down", 440, 90, .5f)));
            _sounds.Add((FlowEvents.GainedAbility, Tone("Ability", 523, 1046, .45f)));
            _sounds.Add((FlowEvents.Saved, Tone("Saved", 660, 990, .3f)));
            _sounds.Add((FlowEvents.Hurt, Tone("Hurt", 300, 120, .18f)));
            _sounds.Add((FlowEvents.WallBroken, Tone("Break", 200, 60, .22f)));
            _sounds.Add((FlowEvents.EnemyDefeated, Tone("Defeat", 700, 200, .16f)));
            _sounds.Add((FlowEvents.BossHit, Tone("BossHit", 240, 180, .06f)));
            _sounds.Add((FlowEvents.EnemyHit, Tone("Hit", 520, 380, .05f)));
            _sounds.Add((FlowEvents.PickedFruit, Tone("Fruit", 600, 900, .2f)));
            _sounds.Add((FlowEvents.Continued, Tone("Continue", 520, 780, .2f)));
            _sounds.Add((FlowEvents.Started, Tone("Start", 520, 780, .1f)));
            _sounds.Add((FlowEvents.DoubleJumped, Tone("DoubleJump", 520, 880, .08f)));
            _sounds.Add((FlowEvents.Jumped, Tone("Jump", 360, 620, .07f)));
            _sounds.Add((FlowEvents.Shot, Tone("Shot", 1200, 900, .04f)));

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p", "<Keyboard>/m");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Hold(Control.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            Hold(Control.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Hold(Control.Jump, "<Keyboard>/space", "<Keyboard>/z", "<Keyboard>/upArrow", "<Keyboard>/w");
            Hold(Control.Shoot, "<Keyboard>/x", "<Keyboard>/j");

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
            if ((events & FlowEvents.Started) != 0)
            {
                _toast = string.Empty;
                _toastUntil = 0;
                _lastAbilities = Ability.None;
            }

            var expedition = Flow.Expedition;
            if (expedition != null)
            {
                var gained = expedition.State.Abilities & ~_lastAbilities;
                if ((gained & Ability.DoubleJump) != 0) { ShowToast(expedition, "二段ジャンプを手に入れた！  空中でもう一度ジャンプできる"); }
                else if ((gained & Ability.BreakShot) != 0) { ShowToast(expedition, "破壊ショットを手に入れた！  ひびの入った壁を撃って壊せる"); }
                else if ((events & FlowEvents.Saved) != 0) { ShowToast(expedition, "セーブしました。体力が全回復した"); }
                else if ((events & FlowEvents.PickedFruit) != 0) { ShowToast(expedition, $"回復の実  体力 +{_settings.Rules.FruitHeal}"); }
                else if ((events & FlowEvents.Continued) != 0) { ShowToast(expedition, "セーブ像から再開"); }
                _lastAbilities = expedition.State.Abilities;
            }

            foreach (var pair in _sounds)
            {
                if ((events & pair.Event) == 0) { continue; }
                _audio.PlayOneShot(pair.Clip);
                break;
            }
        }

        private void ShowToast(Expedition expedition, string text)
        {
            _toast = text;
            _toastUntil = expedition.ElapsedTicks + _settings.ToastTicks;
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

            var expedition = Flow.Expedition;
            if (expedition == null) { current.RenderInitial(_world.Rooms[current.RoomIndex]); }
            else { current.Render(expedition); }
            Frame(current);
            RenderHud();
        }

        // カメラは部屋全体と上の HUD の帯が収まる大きさにして、部屋の中央に固定する。
        private void Frame(MossDepthsStage stage)
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
            _shownMap = null;
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
            var e = Flow.Expedition;
            var screen = Flow.Screen;
            var played = e == null ? 0 : Flow.PlayedTicks * 100 / Units.TickRate;
            var state = (screen, e?.RoomIndex ?? -1, e?.Player.Health ?? 0, e?.State.Abilities ?? Ability.None, e?.Deaths ?? 0, played, Toast, e?.State.TakenCount ?? 0);
            if (!Equals(_shownHud, state))
            {
                _shownHud = state;
                _root.Q("stats").style.display = e == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (e != null)
                {
                    var max = _settings.Rules.MaxHealth;
                    _root.Q<Label>("room").text = e.World.Rooms[e.RoomIndex].Name;
                    _root.Q<Label>("health").text = new string('♥', e.Player.Health) + new string('♡', Math.Max(0, max - e.Player.Health));
                    SetAbility("ability-jump", e.State.Has(Ability.DoubleJump));
                    SetAbility("ability-break", e.State.Has(Ability.BreakShot));
                    _root.Q<Label>("time").text = FormatTicks(Flow.PlayedTicks);
                    _root.Q<Label>("deaths").text = $"力尽きた  {e.Deaths}";
                }

                var toast = _root.Q<Label>("toast");
                toast.text = Toast;
                toast.style.display = Toast.Length > 0 && screen == AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;

                _root.Q("panel").style.display = screen != AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
                _root.Q<Label>("headline").text = Headline(screen);
                _root.Q<Label>("message").text = Message(screen, e, Flow.ClearTicks);
                Show("submit", screen != AppScreen.Playing);
                _root.Q<Button>("submit").text = screen switch
                {
                    AppScreen.Title => "はじめる  /  Enter",
                    AppScreen.Paused => "つづける  /  Enter",
                    AppScreen.GameOver => "セーブ像から再開  /  Enter",
                    _ => "もう一度  /  Enter",
                };
                Show("pause", screen == AppScreen.Playing);
                Show("restart", screen == AppScreen.Paused || screen == AppScreen.GameOver || screen == AppScreen.Victory);
                Show("title", screen == AppScreen.Paused || screen == AppScreen.GameOver || screen == AppScreen.Victory);
                Show("quit", screen == AppScreen.Title);
                _root.Q("bigmap").style.display = screen == AppScreen.Paused ? DisplayStyle.Flex : DisplayStyle.None;
            }

            RenderMaps(e);
        }

        private void SetAbility(string name, bool owned)
        {
            var label = _root.Q<Label>(name);
            label.EnableInClassList("owned", owned);
        }

        // 地図は部屋の格子。訪れた部屋だけを描き、今いる部屋を光らせる。扉のつながりは訪れた部屋から出ている分だけ描く。
        private void RenderMaps(Expedition e)
        {
            var visited = 0;
            if (e != null)
            {
                for (var i = 0; i < e.World.Rooms.Count; i++)
                {
                    if (e.State.IsVisited(i)) { visited |= 1 << i; }
                }
            }

            var key = (e?.RoomIndex ?? -1, visited);
            if (Equals(_shownMap, key)) { return; }
            _shownMap = key;
            DrawMap(_root.Q("minimap"), e, 22, 14, 4);
            DrawMap(_root.Q("bigmap"), e, 120, 70, 14);
        }

        /// <summary>地図を描く。部屋 1 つを幅 <paramref name="width"/>・高さ <paramref name="height"/> の箱にし、<paramref name="gap"/> だけ空ける。</summary>
        public static void DrawMap(VisualElement container, Expedition e, float width, float height, float gap)
        {
            if (container == null) { return; }
            container.Clear();
            if (e == null) { return; }
            var rooms = e.World.Rooms;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (var room in rooms)
            {
                minX = Math.Min(minX, room.GridX);
                maxX = Math.Max(maxX, room.GridX);
                minY = Math.Min(minY, room.GridY);
                maxY = Math.Max(maxY, room.GridY);
            }

            container.style.width = ((maxX - minX + 1) * (width + gap)) - gap;
            container.style.height = ((maxY - minY + 1) * (height + gap)) - gap;
            for (var i = 0; i < rooms.Count; i++)
            {
                if (!e.State.IsVisited(i)) { continue; }
                var room = rooms[i];
                var left = (room.GridX - minX) * (width + gap);
                var top = (maxY - room.GridY) * (height + gap);
                var cell = new VisualElement { name = $"map-room-{i}" };
                cell.AddToClassList("map-room");
                if (i == e.RoomIndex) { cell.AddToClassList("map-current"); }
                if (room.Boss.HasValue) { cell.AddToClassList("map-boss"); }
                if (room.Statues.Count > 0) { cell.AddToClassList("map-save"); }
                cell.style.left = left;
                cell.style.top = top;
                cell.style.width = width;
                cell.style.height = height;
                container.Add(cell);
                if (room.HasDoor(Side.Right)) { Link(container, left + width, top + (height / 2) - 1.5f, gap, 3); }
                if (room.HasDoor(Side.Left)) { Link(container, left - gap, top + (height / 2) - 1.5f, gap, 3); }
                if (room.HasDoor(Side.Top)) { Link(container, left + (width / 2) - 1.5f, top - gap, 3, gap); }
                if (room.HasDoor(Side.Bottom)) { Link(container, left + (width / 2) - 1.5f, top + height, 3, gap); }
            }
        }

        private static void Link(VisualElement container, float left, float top, float width, float height)
        {
            var link = new VisualElement();
            link.AddToClassList("map-link");
            link.style.left = left;
            link.style.top = top;
            link.style.width = width;
            link.style.height = height;
            container.Add(link);
        }

        /// <summary>Tick 数を「分:秒.1/100 秒」にする。</summary>
        public static string FormatTicks(long ticks)
        {
            var hundredths = ticks * 100 / Units.TickRate;
            return $"{hundredths / 6000}:{hundredths / 100 % 60:00}.{hundredths % 100:00}";
        }

        public static string Headline(AppScreen screen) => screen switch
        {
            AppScreen.Title => "MOSSLIGHT DEPTHS",
            AppScreen.Paused => "地図",
            AppScreen.GameOver => "力尽きた…",
            AppScreen.Victory => "塔の主を倒した！",
            _ => string.Empty,
        };

        public static string Message(AppScreen screen, Expedition e, long? clearTicks)
        {
            const string controls = "← →：動く    Space / Z：跳ぶ    X / J：撃つ\nEsc / M：一時停止と地図";
            switch (screen)
            {
                case AppScreen.Title:
                    return "苔むした地下の遺跡を探り、塔の上に棲む主を倒そう。\n新しい力を手に入れると、通れなかった道が開ける。\n光る像に触れると体力が戻り、そこから再開できる。\n\n" + controls;
                case AppScreen.Paused:
                    return "R：最初から    T：タイトルへ";
                case AppScreen.GameOver:
                    return e == null
                        ? string.Empty
                        : $"最後に触れたセーブ像（{e.World.Rooms[e.Save.Room].Name}）から再開できる。\nセーブの後に手に入れたものは失われる。\n\nR：最初から    T：タイトルへ";
                case AppScreen.Victory:
                    return e == null || clearTicks == null
                        ? string.Empty
                        : $"タイム  {FormatTicks(clearTicks.Value)}\n力尽きた回数  {e.Deaths}\n見つけたもの  {e.State.TakenCount} / {e.World.PickupCount}";
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
