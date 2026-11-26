using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using IntGeometry;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// 入力・カメラ・HUD・音をつなぐ。キーとマウスのボタンは時刻付きの意図の列（<see cref="Intent"/>）に、
    /// マウスの位置はフレームごとの照準点（<see cref="DelveFlow.SetAim"/>）にして App へ渡す（026 と同じ）。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AshenGame : MonoBehaviour
    {
        [SerializeField] private AshenSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private AshenStage _stage;

        // 種類の違う操作も起きた順に 1 本の列で処理する。Pointer は HUD のボタン由来（フォーカス復帰直後は捨てる）。
        private readonly List<(Intent Intent, bool Pointer)> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<(RunEvents Event, AudioClip Clip)> _sounds = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -2;
        private int _version = -1;
        private DelveConfig _rules;
        private LootData _loot;
        private FloorSet _floors;
        private Font _font;
        private AudioSource _audio;

        public DelveFlow Flow { get; private set; }

        public AshenSettings Settings => _settings;

        public AshenStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public AshenHud Hud { get; private set; }

        public VisualElement HudRoot => Hud?.Root;

        public DelveConfig Rules => _rules;

        public FloorSet Floors => _floors;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した出来事（撮影台本・テストが場面を待つのに使う）。</summary>
        public RunEvents LastEvents { get; private set; }

        /// <summary>音を鳴らした回数（テスト用）。</summary>
        public int SoundsPlayed { get; private set; }

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>照準カーソルの位置：操縦役が動かしているときは何も出さない位置（自機）、そうでなければマウスの点。</summary>
        public Vec2i CrosshairPoint => Flow.Aim;

        private void Awake()
        {
            _settings.Validate();
            _rules = _settings.Rules.Copy();
            _loot = _settings.ParseLoot();
            _floors = _settings.ParseFloors();
            var mismatch = _stage.Mismatch(_floors);
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が地図と一致しません：" + mismatch); }

            // 再戦は新しいシード（起動時の値から試合ごとに 1 進める。010）。撮影中は固定のシード。
            Flow = new DelveFlow(_rules, _loot, _floors, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            _sounds.Add((RunEvents.BossKilled, Tone("BossKilled", 440, 1320, .9f)));
            _sounds.Add((RunEvents.Died, Tone("Died", 400, 60, .7f)));
            _sounds.Add((RunEvents.DropRare, Tone("DropRare", 880, 1760, .35f)));
            _sounds.Add((RunEvents.NextFloor, Tone("NextFloor", 300, 600, .4f)));
            _sounds.Add((RunEvents.Hurt, Tone("Hurt", 300, 120, .18f)));
            _sounds.Add((RunEvents.DropMagic, Tone("DropMagic", 700, 1050, .2f)));
            _sounds.Add((RunEvents.FoeKilled, Tone("Killed", 520, 160, .14f)));
            _sounds.Add((RunEvents.Equipped, Tone("Equip", 600, 900, .1f)));
            _sounds.Add((RunEvents.PickedUp, Tone("Pick", 900, 1200, .07f)));
            _sounds.Add((RunEvents.PotionUsed, Tone("Potion", 500, 800, .2f)));
            _sounds.Add((RunEvents.FoeHit, Tone("Hit", 260, 200, .05f)));
            _sounds.Add((RunEvents.Spin, Tone("Spin", 200, 500, .15f)));
            _sounds.Add((RunEvents.Swing, Tone("Swing", 1200, 700, .04f)));
            _sounds.Add((RunEvents.FoeShot, Tone("Shot", 1500, 1100, .03f)));

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space");
            Bind(IntentKind.Back, "<Keyboard>/escape");
            Bind(IntentKind.ToggleInventory, "<Keyboard>/i", "<Keyboard>/tab");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Hold(Control.Left, "<Keyboard>/a", "<Keyboard>/leftArrow");
            Hold(Control.Right, "<Keyboard>/d", "<Keyboard>/rightArrow");
            Hold(Control.Up, "<Keyboard>/w", "<Keyboard>/upArrow");
            Hold(Control.Down, "<Keyboard>/s", "<Keyboard>/downArrow");
            Hold(Control.Potion, "<Keyboard>/q");
            Hold(Control.Pick, "<Keyboard>/e");

            // HUD のボタン・名前札の上で押したクリックは攻撃にしない（ボタンの操作として扱う）。
            var attack = NewAction(nameof(Control.Attack), "<Mouse>/leftButton");
            attack.started += context => { if (!PointerOverButton()) { Queue(Intent.Down(Control.Attack, EventTime(context)), false); } };
            attack.canceled += context => Queue(Intent.Up(Control.Attack, EventTime(context)), false);
            var skill = NewAction(nameof(Control.Skill), "<Mouse>/rightButton");
            skill.started += context => { if (!PointerOverButton()) { Queue(Intent.Down(Control.Skill, EventTime(context)), false); } };
            skill.canceled += context => Queue(Intent.Up(Control.Skill, EventTime(context)), false);

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Bind(IntentKind kind, params string[] bindings) =>
            NewAction(kind.ToString(), bindings).performed += context => Queue(Intent.Of(kind, EventTime(context)), false);

        /// <summary>
        /// 押したままの操作。物理キーごとに別のアクションにし、キーの番号を付けて送る。1 つのアクションに束ねると、
        /// A を押したまま一時停止して再開した後に左矢印を押しても started が来ず、移動が再開しない（035・037）。
        /// </summary>
        private void Hold(Control control, params string[] bindings)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                int key = i;
                var action = NewAction($"{control}{key}", bindings[i]);
                action.started += context => Queue(Intent.Down(control, EventTime(context), key), false);
                action.canceled += context => Queue(Intent.Up(control, EventTime(context), key), false);
            }
        }

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
            var root = HudRoot;
            if (root?.panel == null || Mouse.current == null) { return false; }
            var point = Mouse.current.position.ReadValue();
            point.y = UnityEngine.Screen.height - point.y;
            for (var element = root.panel.Pick(RuntimePanelUtils.ScreenToPanel(root.panel, point)); element != null; element = element.parent)
            {
                if (element is Button) { return true; }
            }

            return false;
        }

        private void Update()
        {
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

                    Apply(intent);
                }
            }

            _intents.Clear();

            // 撮影中は台本が Tick を進める。timeScale が 0 の間（テストが Tick を直接進めるとき）も時計で進めない。
            if (!EvidenceCapture.IsRunning && Time.timeScale > 0) { Flow.Advance(Now); }
            else { Flow.EndFrame(); }
            ConsumeEvents();
            Render();
        }

        /// <summary>意図を Flow に渡す。持ち物の画面を開いたら選択を消す。撮影台本・テストもこれを使う。</summary>
        public void Apply(Intent intent)
        {
            var before = Flow.Screen;
            Flow.Apply(intent);
            if (Flow.Screen == AppScreen.Inventory && before != AppScreen.Inventory) { Hud?.Select(AshenHud.Selection.None); }
            // 装備・外す・捨てると品の並びが変わるので、選択を消す（別の品を選んだまま操作しない）。
            bool changed = intent.Kind == IntentKind.Equip || intent.Kind == IntentKind.Unequip || intent.Kind == IntentKind.Discard;
            if (changed && Flow.LastItemResult == ItemResult.Done) { Hud?.Select(AshenHud.Selection.None); }
        }

        /// <summary>画面の座標（ピクセル）を階の座標（Core の単位）へ。</summary>
        public Vec2i PointerToCore(Vector2 screen)
        {
            var world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z));
            return AshenStage.ToCore(_stage.transform.InverseTransformPoint(world));
        }

        /// <summary>階の座標を画面の座標（ピクセル）へ（テストがクリックの位置を決めるのに使う）。</summary>
        public Vector2 CoreToScreen(Vec2i point)
        {
            var world = _stage.transform.TransformPoint(AshenStage.Local(point));
            var screen = _camera.WorldToScreenPoint(world);
            return new Vector2(screen.x, screen.y);
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
            if (events == RunEvents.None) { return; }
            var run = Flow.Run;
            if ((events & RunEvents.InventoryFull) != 0) { Hud?.Toast("持ち物がいっぱいです（I で持ち物を開いて捨てる）", run); }
            else if ((events & RunEvents.DropRare) != 0) { Hud?.Toast("希少な装備が落ちた！", run); }
            else if ((events & RunEvents.NextFloor) != 0) { Hud?.Toast($"地下 {run?.FloorNumber} 階  {run?.Map.Name}", run); }
            else if ((events & RunEvents.Respawned) != 0) { Hud?.Toast("入口で目を覚ました", run); }

            foreach (var pair in _sounds)
            {
                if ((events & pair.Event) == 0) { continue; }
                _audio.PlayOneShot(pair.Clip);
                SoundsPlayed++;
                break;
            }
        }

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            var run = Flow.Run;
            bool playing = Flow.Screen == AppScreen.Playing;
            if (run == null) { _stage.RenderInitial(_floors, _rules); }
            else { _stage.Render(run, _rules, CrosshairPoint, playing && Flow.Pilot == null); }
            Frame(run);
            UnityEngine.Cursor.visible = !(playing && Flow.Pilot == null);
            Hud?.Render(Flow, _rules, _camera, _stage, true);
        }

        /// <summary>カメラは自機を中央に、階の外が映らないよう地図の範囲で止める。</summary>
        private void Frame(DelveRun run)
        {
            float half = _settings.ViewHeightTiles / 2f;
            _camera.orthographicSize = half;
            float halfWidth = half * Mathf.Max(.01f, _camera.aspect);
            var map = run?.Map ?? _floors[1];
            var center = AshenStage.Local(run?.Hero.Position ?? map.Entrance);
            float x = Clamp(center.x, halfWidth, map.Width - halfWidth);
            float y = Clamp(center.y, half, map.Height - half);
            var world = _stage.transform.TransformPoint(new Vector3(x, y, 0));
            _camera.transform.position = new Vector3(world.x, world.y, _camera.transform.position.z);
        }

        private static float Clamp(float value, float min, float max) => min > max ? (min + max) / 2 : Mathf.Clamp(value, min, max);

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (Hud != null && _version == version) { return; }
            _version = version;
            root.style.unityFont = _font;
            Hud = new AshenHud(root, _rules.InventorySize, _rules.GroundCap, intent => Queue(intent, true));
            Connect(root, "submit", IntentKind.Submit);
            Connect(root, "back", IntentKind.Back);
            Connect(root, "title", IntentKind.BackToTitle);
            Connect(root, "quit", IntentKind.Quit);
            Render();
        }

        private void Connect(VisualElement root, string name, IntentKind kind) => root.Q<Button>(name).clicked += () => Queue(Intent.Of(kind, Now), true);

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
