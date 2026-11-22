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
    public sealed class EmberVaultGame : MonoBehaviour
    {
        /// <summary>部屋の上に空ける HUD の帯の高さ（タイル）。</summary>
        public const float HudBandTiles = 1.5f;

        /// <summary>ワールドに並べた部屋どうしの間（タイル）。隣の部屋の壁が画面の端に見えないように空ける。</summary>
        public const int RoomGap = 6;

        [SerializeField] private EmberVaultSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private EmberVaultStage[] _stages = Array.Empty<EmberVaultStage>();

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
        private string _toast = string.Empty;
        private long _toastUntil;

        public VaultFlow Flow { get; private set; }

        public EmberVaultSettings Settings => _settings;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した出来事（撮影台本が場面を待つのに使う）。</summary>
        public FlowEvents LastEvents { get; private set; }

        /// <summary>タイトルで描くダンジョン（試合中は試合のダンジョン）。</summary>
        public World World => Flow?.Dungeon?.World ?? _world;

        /// <summary>今の部屋の Stage（試合の前は開始の部屋）。</summary>
        public EmberVaultStage CurrentStage => _stages[Flow?.Dungeon?.RoomIndex ?? _world.StartRoom];

        public EmberVaultStage StageAt(int index) => _stages[index];

        /// <summary>今出している案内（なければ空）。</summary>
        public string Toast => Flow?.Dungeon != null && Flow.Dungeon.ElapsedTicks < _toastUntil ? _toast : string.Empty;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>部屋の Stage を置くワールドの位置（部屋の格子に、間を空けて並べる）。</summary>
        public static Vector3 RoomOrigin(Room room)
        {
            if (room == null) { throw new ArgumentNullException(nameof(room)); }
            return new Vector3(room.GridX * (Room.Columns + RoomGap), room.GridY * (Room.Rows + RoomGap), 0);
        }

        /// <summary>部屋を写すときのカメラの中心（部屋と上の HUD の帯の中央）。</summary>
        public static Vector3 RoomView(Room room) => RoomOrigin(room) + new Vector3(Room.Columns / 2f, (Room.Rows + HudBandTiles) / 2f, 0);

        private void Awake()
        {
            _settings.Validate();
            _world = Rooms.Create();
            if (_stages.Length != _world.Rooms.Count) { throw new InvalidOperationException($"Stage は部屋の順に {_world.Rooms.Count} 個並べてください（{_stages.Length} 個）。"); }
            for (var i = 0; i < _stages.Length; i++)
            {
                if (_stages[i].RoomIndex != i) { throw new InvalidOperationException($"{i} 番目の Stage が {_stages[i].RoomIndex} 部屋目を指しています。"); }
                _stages[i].transform.position = RoomOrigin(_world.Rooms[i]);
            }

            // 再戦は新しいシード（起動時の値から試合ごとに 1 進める。010 の知見）。撮影中は固定のシード。
            Flow = new VaultFlow(_settings.Rules, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            _sounds.Add((FlowEvents.Won, Tone("Won", 784, 1568, .7f)));
            _sounds.Add((FlowEvents.Down, Tone("Down", 440, 90, .5f)));
            _sounds.Add((FlowEvents.BossDefeated, Tone("BossDown", 330, 990, .6f)));
            _sounds.Add((FlowEvents.ChestOpened, Tone("Chest", 523, 1046, .35f)));
            _sounds.Add((FlowEvents.KeyTaken, Tone("Key", 880, 1320, .2f)));
            _sounds.Add((FlowEvents.DoorOpened, Tone("Door", 200, 400, .25f)));
            _sounds.Add((FlowEvents.RewardShown, Tone("Reward", 660, 990, .3f)));
            _sounds.Add((FlowEvents.Hurt, Tone("Hurt", 300, 120, .18f)));
            _sounds.Add((FlowEvents.EnemyDefeated, Tone("Defeat", 700, 200, .16f)));
            _sounds.Add((FlowEvents.BossHit, Tone("BossHit", 240, 180, .06f)));
            _sounds.Add((FlowEvents.EnemyHit, Tone("Hit", 520, 380, .05f)));
            _sounds.Add((FlowEvents.DoorLocked, Tone("Locked", 160, 140, .08f)));
            _sounds.Add((FlowEvents.Continued, Tone("Continue", 520, 780, .2f)));
            _sounds.Add((FlowEvents.Started, Tone("Start", 520, 780, .1f)));
            _sounds.Add((FlowEvents.RoomChanged, Tone("Room", 300, 360, .06f)));
            _sounds.Add((FlowEvents.Rolled, Tone("Roll", 260, 520, .07f)));
            _sounds.Add((FlowEvents.Swung, Tone("Swing", 1100, 700, .04f)));

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Hold(Control.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            Hold(Control.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Hold(Control.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            Hold(Control.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            Hold(Control.Attack, "<Keyboard>/z", "<Keyboard>/j", "<Keyboard>/space");
            Hold(Control.Roll, "<Keyboard>/x", "<Keyboard>/k", "<Keyboard>/leftShift");

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

            var d = Flow.Dungeon;
            if (d != null)
            {
                if ((events & FlowEvents.BossDefeated) != 0) { ShowToast(d, "番人を倒した！  現れた秘宝を手に取ろう"); }
                else if ((events & FlowEvents.ChestOpened) != 0) { ShowToast(d, d.State.HasBossKey && ChestOpenedHere(d, ChestContent.BossKey) ? "ボス鍵を手に入れた！  前室の黒い扉が開けられる" : "小鍵を手に入れた"); }
                else if ((events & FlowEvents.KeyTaken) != 0) { ShowToast(d, "小鍵を手に入れた"); }
                else if ((events & FlowEvents.RewardShown) != 0) { ShowToast(d, "敵を全滅させた。小鍵が現れた"); }
                else if ((events & FlowEvents.DoorOpened) != 0) { ShowToast(d, "扉を開けた"); }
                else if ((events & FlowEvents.DoorLocked) != 0) { ShowToast(d, LockedMessage(d)); }
                else if ((events & FlowEvents.Continued) != 0) { ShowToast(d, "入口から再開。手に入れた鍵と開けた扉はそのまま"); }
            }

            foreach (var pair in _sounds)
            {
                if ((events & pair.Event) == 0) { continue; }
                _audio.PlayOneShot(pair.Clip);
                break;
            }
        }

        private static bool ChestOpenedHere(Dungeon d, ChestContent content)
        {
            var chests = d.Run.Room.Chests;
            for (var i = 0; i < chests.Count; i++)
            {
                if (chests[i].Content == content && d.State.IsChestOpen(d.RoomIndex, i)) { return true; }
            }

            return false;
        }

        /// <summary>押し当てた扉の種類に合わせた案内。</summary>
        private static string LockedMessage(Dungeon d)
        {
            var body = d.Hero.Body;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (d.Run.Terrain.ClosedDoorUnder(body.Offset(dx, dy)) is Side side)
                {
                    return d.Run.Room.Door(side) == DoorKind.Boss ? "黒い扉。ボス鍵が必要だ" : "鍵がかかっている。小鍵が必要だ";
                }
            }

            return "鍵がかかっている";
        }

        private void ShowToast(Dungeon d, string text)
        {
            _toast = text;
            _toastUntil = d.ElapsedTicks + _settings.ToastTicks;
        }

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            var d = Flow.Dungeon;
            var current = CurrentStage;
            var leaving = d != null && d.Status == DungeonStatus.Transition && d.PreviousRoom >= 0 ? _stages[d.PreviousRoom] : null;
            foreach (var stage in _stages)
            {
                var visible = stage == current || stage == leaving;
                if (stage.gameObject.activeSelf != visible) { stage.gameObject.SetActive(visible); }
            }

            if (d == null) { current.RenderInitial(_world.Rooms[current.RoomIndex]); }
            else { current.Render(d); }
            if (leaving != null) { leaving.RenderVacant(d!.State); }
            Frame(d);
            RenderHud();
        }

        /// <summary>
        /// カメラは部屋全体と上の HUD の帯が収まる大きさにして、今の部屋の中央に置く。切り替え中は、出ていく部屋から
        /// 入る部屋へ、切り替えの進み具合（Tick）に比例して動かす。
        /// </summary>
        private void Frame(Dungeon d)
        {
            var height = Room.Rows + HudBandTiles;
            _camera.orthographicSize = Mathf.Max(height / 2, Room.Columns / 2f / Mathf.Max(.01f, _camera.aspect));
            var to = RoomView(World.Rooms[CurrentStage.RoomIndex]);
            var center = to;
            if (d != null && d.Status == DungeonStatus.Transition && d.PreviousRoom >= 0)
            {
                var from = RoomView(d.World.Rooms[d.PreviousRoom]);
                var total = _settings.Rules.TransitionTicks;
                center = Vector3.Lerp(from, to, (float)(total - d.TransitionRemaining) / total);
            }

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
            var d = Flow.Dungeon;
            var screen = Flow.Screen;
            var played = d == null ? 0 : Flow.PlayedTicks * 100 / Units.TickRate;
            var state = (screen, d?.RoomIndex ?? -1, d?.Hero.Health ?? 0, d?.State.SmallKeys ?? 0, d?.State.HasBossKey ?? false, d?.Deaths ?? 0, played, Toast);
            if (!Equals(_shownHud, state))
            {
                _shownHud = state;
                _root.Q("stats").style.display = d == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (d != null)
                {
                    var max = _settings.Rules.MaxHealth;
                    _root.Q<Label>("room").text = d.World.Rooms[d.RoomIndex].Name;
                    _root.Q<Label>("health").text = new string('♥', d.Hero.Health) + new string('♡', Math.Max(0, max - d.Hero.Health));
                    _root.Q<Label>("keys").text = $"小鍵 ×{d.State.SmallKeys}";
                    _root.Q<Label>("boss-key").EnableInClassList("owned", d.State.HasBossKey);
                    _root.Q<Label>("time").text = FormatTicks(Flow.PlayedTicks);
                    _root.Q<Label>("deaths").text = $"倒れた  {d.Deaths}";
                }

                var toast = _root.Q<Label>("toast");
                toast.text = Toast;
                toast.style.display = Toast.Length > 0 && screen == AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;

                _root.Q("panel").style.display = screen != AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
                _root.Q<Label>("headline").text = Headline(screen);
                _root.Q<Label>("message").text = Message(screen, d, Flow.ClearTicks);
                Show("submit", screen != AppScreen.Playing);
                _root.Q<Button>("submit").text = screen switch
                {
                    AppScreen.Title => "はじめる  /  Enter",
                    AppScreen.Paused => "つづける  /  Enter",
                    AppScreen.GameOver => "入口から再開  /  Enter",
                    _ => "もう一度  /  Enter",
                };
                Show("pause", screen == AppScreen.Playing);
                Show("restart", screen == AppScreen.Paused || screen == AppScreen.GameOver || screen == AppScreen.Victory);
                Show("title", screen == AppScreen.Paused || screen == AppScreen.GameOver || screen == AppScreen.Victory);
                Show("quit", screen == AppScreen.Title);
                _root.Q("bigmap").style.display = screen == AppScreen.Paused ? DisplayStyle.Flex : DisplayStyle.None;
            }

            RenderMaps(d);
        }

        // 地図は部屋の格子。訪れた部屋だけを描き、今いる部屋を光らせる。出入口のつながりは扉の状態（開いた・小扉・ボス扉）で色を分ける。
        private void RenderMaps(Dungeon d)
        {
            var visited = 0;
            var open = 0;
            if (d != null)
            {
                for (var i = 0; i < d.World.Rooms.Count; i++)
                {
                    if (d.State.IsVisited(i)) { visited |= 1 << i; }
                    for (var side = 0; side < 4; side++)
                    {
                        if (d.State.IsOpen(i, (Side)side)) { open |= 1 << ((i * 4) + side); }
                    }
                }
            }

            var key = (d?.RoomIndex ?? -1, visited, open, d?.State.BossDefeated ?? false);
            if (Equals(_shownMap, key)) { return; }
            _shownMap = key;
            DrawMap(_root.Q("minimap"), d, 26, 14, 5);
            DrawMap(_root.Q("bigmap"), d, 120, 64, 16);
        }

        /// <summary>地図を描く。部屋 1 つを幅 <paramref name="width"/>・高さ <paramref name="height"/> の箱にし、<paramref name="gap"/> だけ空ける。</summary>
        public static void DrawMap(VisualElement container, Dungeon d, float width, float height, float gap)
        {
            if (container == null) { return; }
            container.Clear();
            if (d == null) { return; }
            var rooms = d.World.Rooms;
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
                if (!d.State.IsVisited(i)) { continue; }
                var room = rooms[i];
                var left = (room.GridX - minX) * (width + gap);
                var top = (maxY - room.GridY) * (height + gap);
                var cell = new VisualElement { name = $"map-room-{i}" };
                cell.AddToClassList("map-room");
                if (i == d.RoomIndex) { cell.AddToClassList("map-current"); }
                if (room.Boss.HasValue) { cell.AddToClassList(d.State.BossDefeated ? "map-cleared" : "map-boss"); }
                cell.style.left = left;
                cell.style.top = top;
                cell.style.width = width;
                cell.style.height = height;
                container.Add(cell);
                Link(container, d, i, Side.Right, left + width, top + (height / 2) - 1.5f, gap, 3);
                Link(container, d, i, Side.Left, left - gap, top + (height / 2) - 1.5f, gap, 3);
                Link(container, d, i, Side.Top, left + (width / 2) - 1.5f, top - gap, 3, gap);
                Link(container, d, i, Side.Bottom, left + (width / 2) - 1.5f, top + height, 3, gap);
            }
        }

        private static void Link(VisualElement container, Dungeon d, int room, Side side, float left, float top, float width, float height)
        {
            var kind = d.World.Rooms[room].Door(side);
            if (kind == DoorKind.None) { return; }
            var link = new VisualElement();
            link.AddToClassList("map-link");
            if (!d.State.IsOpen(room, side)) { link.AddToClassList(kind == DoorKind.Boss ? "map-link-boss" : "map-link-locked"); }
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
            AppScreen.Title => "EMBER VAULT",
            AppScreen.Paused => "地図",
            AppScreen.GameOver => "倒れてしまった…",
            AppScreen.Victory => "秘宝を手に入れた！",
            _ => string.Empty,
        };

        public static string Message(AppScreen screen, Dungeon d, long? clearTicks)
        {
            const string controls = "← → ↑ ↓：歩く    Z：剣（宝箱の前では開ける）    X：回避ロール\nEsc：一時停止と地図";
            switch (screen)
            {
                case AppScreen.Title:
                    return "燠火の宝物殿の奥に、番人が守る秘宝が眠っている。\n小鍵で扉を開け、宝箱のボス鍵で番人の間へ。\n回避ロールの間は攻撃を受けない。\n\n" + controls;
                case AppScreen.Paused:
                    return "黄：小扉    赤：ボス扉\nR：最初から    T：タイトルへ";
                case AppScreen.GameOver:
                    return d == null ? string.Empty : "入口から体力満タンで再開できる。\n手に入れた鍵・開けた扉・宝箱はそのまま。\n\nR：最初から    T：タイトルへ";
                case AppScreen.Victory:
                    return d == null || clearTicks == null ? string.Empty : $"タイム  {FormatTicks(clearTicks.Value)}\n倒れた回数  {d.Deaths}";
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
