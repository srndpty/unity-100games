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
    /// 入力・カメラ・HUD・音をつなぐ。キーは時刻付きの意図の列（<see cref="Intent"/>）にして App へ渡し、
    /// 描画は <see cref="PlayerView"/>（既知マップと視界の中だけ）から行う。試合の中身（敵の一覧など）は読まない。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class LanternGame : MonoBehaviour
    {
        /// <summary>HUD の上の帯と下のメッセージ欄の高さ（1080 を基準にした px）。盤はこの間に収める。</summary>
        public const float TopBand = 64;
        public const float BottomBand = 200;
        public const float ReferenceHeight = 1080;

        [SerializeField] private LanternSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private LanternStage _stage;

        // 種類の違う操作も起きた順に 1 本の列で処理する。Pointer は HUD のボタン由来（フォーカス復帰直後は捨てる）。
        private readonly List<(Intent Intent, bool Pointer)> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<(Func<RunEvent, bool> Match, AudioClip Clip)> _sounds = new();
        private readonly List<RunEvent> _events = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -2;
        private int _version = -1;
        private bool _titleShown;
        private CrawlConfig _rules;
        private FloorLayout _preview;
        private Font _font;
        private AudioSource _audio;

        public CrawlFlow Flow { get; private set; }

        public LanternSettings Settings => _settings;

        public LanternStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public LanternHud Hud { get; private set; }

        public VisualElement HudRoot => Hud?.Root;

        public CrawlConfig Rules => _rules;

        /// <summary>タイトルの背景に描く見本の階（Prefab に焼いたものと同じ）。</summary>
        public FloorLayout Preview => _preview;

        /// <summary>音を鳴らした回数（テスト用）。</summary>
        public int SoundsPlayed { get; private set; }

        /// <summary>直前に鳴らした音の名前（テスト用）。</summary>
        public string LastSound { get; private set; } = string.Empty;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            _settings.Validate();
            _rules = _settings.Rules.Copy();
            _preview = FloorGenerator.Generate(LanternStage.PreviewSeed, LanternStage.PreviewFloor, _rules);
            var mismatch = _stage.Mismatch(_preview.Grid);
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が見本の階と一致しません：" + mismatch); }

            // 再戦は新しいシード（起動時の値から試合ごとに 1 進める。010）。撮影中は固定のシード。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            Flow = new CrawlFlow(_rules, seed, _settings.RepeatDelay, _settings.RepeatInterval);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            Sound(e => e.Kind == EventKind.Died, "Died", 400, 60, .8f);
            Sound(e => e.Kind == EventKind.Won, "Won", 440, 1320, 1f);
            Sound(e => e.Kind == EventKind.LevelUp, "LevelUp", 600, 1200, .35f);
            Sound(e => e.Kind == EventKind.Descended, "Descend", 500, 200, .4f);
            Sound(e => e.Kind == EventKind.Stolen, "Stolen", 1200, 400, .3f);
            Sound(e => (e.Kind == EventKind.Attack || e.Kind == EventKind.Shot) && e.Target == 0 && e.Value >= 0, "Hurt", 300, 120, .16f);
            Sound(e => e.Kind == EventKind.Killed, "Killed", 520, 160, .16f);
            Sound(e => (e.Kind == EventKind.Attack && e.Actor == 0 && e.Value >= 0) || e.Kind == EventKind.ThrowHit, "Hit", 260, 200, .06f);
            Sound(e => e.Kind == EventKind.Starving || e.Kind == EventKind.Hungry, "Hungry", 200, 150, .3f);
            Sound(e => e.Kind == EventKind.PickedUp, "Pick", 900, 1200, .07f);
            Sound(e => e.Kind == EventKind.Healed || e.Kind == EventKind.Ate, "Heal", 500, 800, .18f);
            Sound(e => e.Kind == EventKind.Thrown, "Throw", 1000, 600, .08f);
            Sound(e => e.Kind == EventKind.Attack || e.Kind == EventKind.Shot, "Miss", 1400, 1100, .04f);

            Bind(new[] { IntentKind.Submit }, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(new[] { IntentKind.Back }, "<Keyboard>/escape");
            Bind(new[] { IntentKind.ToggleInventory }, "<Keyboard>/i", "<Keyboard>/tab");

            // T は一時停止・結果ではタイトルへ、持ち物の画面では投げる（Flow はその画面で意味のない意図を捨てる）。
            Bind(new[] { IntentKind.BackToTitle, IntentKind.InventoryThrow }, "<Keyboard>/t");
            Bind(new[] { IntentKind.InventoryUse }, "<Keyboard>/u");
            Bind(new[] { IntentKind.InventoryDrop }, "<Keyboard>/x");
            Select(-1, "<Keyboard>/upArrow", "<Keyboard>/w", "<Keyboard>/numpad8");
            Select(1, "<Keyboard>/downArrow", "<Keyboard>/s", "<Keyboard>/numpad2");

            Hold(Control.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            Hold(Control.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Hold(Control.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            Hold(Control.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            Hold(Control.Pad, "<Keyboard>/numpad8", "<Keyboard>/numpad9", "<Keyboard>/numpad6", "<Keyboard>/numpad3",
                "<Keyboard>/numpad2", "<Keyboard>/numpad1", "<Keyboard>/numpad4", "<Keyboard>/numpad7");
            Hold(Control.Diagonal, "<Keyboard>/leftShift", "<Keyboard>/rightShift");
            Hold(Control.Turn, "<Keyboard>/leftCtrl", "<Keyboard>/rightCtrl");
            Hold(Control.Wait, "<Keyboard>/space", "<Keyboard>/numpad5");
            Hold(Control.Pickup, "<Keyboard>/g");
            Hold(Control.Descend, "<Keyboard>/period", "<Keyboard>/numpadPeriod");

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Bind(IntentKind[] kinds, params string[] bindings)
        {
            NewAction(kinds[0].ToString(), bindings).performed += context =>
            {
                foreach (var kind in kinds) { Queue(Intent.Of(kind, EventTime(context)), false); }
            };
        }

        private void Select(int delta, params string[] bindings) =>
            NewAction($"Select{delta}", bindings).performed += context => Queue(Intent.Move(delta, EventTime(context)), false);

        /// <summary>
        /// 押したままの操作。物理キーごとに別のアクションにし、キーの番号を付けて送る。1 つのアクションに束ねると、
        /// 一方を押したまま一時停止して再開した後にもう一方を押しても started が来ない（035・037・042）。
        /// テンキーはキーの番号が方向（北から時計回り）。
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

            // 撮影中は台本が操作を進める。
            if (!EvidenceCapture.IsRunning) { Flow.Advance(Now); }
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

        /// <summary>見えた出来事から音を 1 つ鳴らす。撮影台本も操作の後に呼ぶ。</summary>
        public void ConsumeEvents()
        {
            _events.Clear();
            Flow.TakeEvents(_events);
            if (_events.Count == 0) { return; }
            foreach (var (match, clip) in _sounds)
            {
                foreach (var e in _events)
                {
                    if (!match(e)) { continue; }
                    _audio.PlayOneShot(clip);
                    SoundsPlayed++;
                    LastSound = clip.name;
                    return;
                }
            }
        }

        /// <summary>今の状態を描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            var view = Flow.View;
            if (view == null)
            {
                if (!_titleShown) { _stage.RenderTitle(_preview); }
                _titleShown = true;
            }
            else
            {
                _titleShown = false;
                _stage.Render(view);
            }

            Frame();
            Hud?.Render(Flow);
        }

        /// <summary>盤の全体を、上の帯と下のメッセージ欄の間に収める（盤は 1 画面に収まる大きさ）。</summary>
        private void Frame()
        {
            var aspect = Mathf.Max(.01f, _camera.aspect);
            var bottomShare = BottomBand / ReferenceHeight;
            var mapShare = (ReferenceHeight - TopBand - BottomBand) / ReferenceHeight;

            // 縦は帯の間に盤の高さ、横は盤の幅と左右 1 マスの余白が入る大きさ。
            var height = Mathf.Max(_stage.Height / mapShare, (_stage.Width + 2) / aspect);
            _camera.orthographicSize = height / 2;

            // 盤の中心が、帯の間の中心に来るようにする。
            var centerY = (_stage.Height / 2f) + (height / 2) - (height * bottomShare) - (height * mapShare / 2);
            var world = _stage.transform.TransformPoint(new Vector3(_stage.Width / 2f, centerY, 0));
            _camera.transform.position = new Vector3(world.x, world.y, _camera.transform.position.z);
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (Hud != null && _version == version) { return; }
            _version = version;
            root.style.unityFont = _font;
            Hud = new LanternHud(root, _rules.InventoryCapacity, intent => Queue(intent, true));
            Connect(root, "submit", IntentKind.Submit);
            Connect(root, "back", IntentKind.Back);
            Connect(root, "title", IntentKind.BackToTitle);
            Connect(root, "quit", IntentKind.Quit);
            Render();
        }

        private void Connect(VisualElement root, string name, IntentKind kind) => root.Q<Button>(name).clicked += () => Queue(Intent.Of(kind, Now), true);

        // 音程を from から to へ動かす矩形波。振幅は控えめ。
        private void Sound(Func<RunEvent, bool> match, string name, int from, int to, float seconds)
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
            _sounds.Add((match, clip));
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
