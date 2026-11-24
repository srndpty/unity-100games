using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
    /// 入力・HUD・音。マウスの押下・移動・解放とキーを、起きた時刻つきで 1 本の列に入れ、フレームの初めに起きた順で <see cref="SiegeFlow"/> へ渡す。
    /// 物理は <see cref="PebbleSiegeWorld"/>（Script モードの Physics2D）を Flow が 1 Tick ごとに進める。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class PebbleSiegeGame : MonoBehaviour
    {
        /// <summary>カメラに収める盤の横幅（盤 32 m と左右の余白）。</summary>
        public const float ViewWidth = Field.Width + 2f;

        private static readonly Color _worn = new(.55f, .45f, .4f, 1);

        [SerializeField] private PebbleSiegeSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private PebbleSiegeStage _stage;

        private readonly List<Intent> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<(FlowEvents Event, AudioClip Clip)> _sounds = new();
        private readonly StringBuilder _text = new();
        private bool _hasFocus = true;
        private VisualElement _root;
        private int _version = -1;
        private object _shownHud;
        private Font _font;
        private AudioSource _audio;
        private float _cameraSize;
        private SiegeConfig _rules;

        public SiegeFlow Flow { get; private set; }

        public PebbleSiegeWorld World { get; private set; }

        public PebbleSiegeSettings Settings => _settings;

        public PebbleSiegeStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した出来事（撮影台本・テストが場面を待つのに使う）。</summary>
        public FlowEvents LastEvents { get; private set; }

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>面選択の札の名前（UXML）。</summary>
        public static string TileName(int index) => "tile-" + index;

        private void Awake()
        {
            if (Physics2D.simulationMode != SimulationMode2D.Script)
            {
                throw new InvalidOperationException("Physics2D の simulationMode を Script にしてください（物理は Tick ごとに Simulate する）。");
            }

            var book = _settings.Validate();
            var mismatch = _stage.Mismatch();
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が欠けています：" + mismatch); }
            if (_stage.transform.position != Vector3.zero || _stage.transform.lossyScale != Vector3.one)
            {
                throw new InvalidOperationException("Stage は原点に大きさ 1 で置いてください（ワールド座標 = 盤の座標）。");
            }

            // 検証済みの設定の複製を 1 つだけ作り、Flow・物理の世界・描画が同じ値を使う（設定アセットを後から書き換えても分かれない）。
            _rules = _settings.Rules.Copy();
            World = new PebbleSiegeWorld(_stage, _rules);
            Flow = new SiegeFlow(_rules, book, World);
            _cameraSize = _camera.orthographicSize;

            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            _sounds.Add((FlowEvents.Won, Tone("Won", 660, 1320, .6f)));
            _sounds.Add((FlowEvents.Lost, Tone("Lost", 330, 90, .6f)));
            _sounds.Add((FlowEvents.Detonated, Noise("Blast", .35f)));
            _sounds.Add((FlowEvents.Broke, Noise("Break", .1f)));
            _sounds.Add((FlowEvents.Launched, Tone("Launch", 300, 900, .12f)));
            _sounds.Add((FlowEvents.Started | FlowEvents.Retried, Tone("Start", 520, 780, .1f)));
            _sounds.Add((FlowEvents.Selected | FlowEvents.Grabbed, Tone("Select", 700, 700, .03f)));

            // マウス：左ボタンの押下・解放とその時の位置、移動、右ボタン。PassThrough で 1 つずつ拾う。
            var left = NewAction("Press", InputActionType.PassThrough, "<Mouse>/leftButton");
            left.performed += OnLeftButton;
            var right = NewAction("CancelAim", InputActionType.PassThrough, "<Mouse>/rightButton");
            right.performed += OnRightButton;
            var position = NewAction("Point", InputActionType.PassThrough, "<Mouse>/position");
            position.performed += OnPosition;
            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space");
            Bind(IntentKind.Cancel, "<Keyboard>/escape");
            Bind(IntentKind.Retry, "<Keyboard>/r");
            Bind(IntentKind.Back, "<Keyboard>/backspace");
            BindMove(-1, "<Keyboard>/leftArrow");
            BindMove(1, "<Keyboard>/rightArrow");
            foreach (var action in _actions) { action.Enable(); }

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void OnLeftButton(InputAction.CallbackContext context)
        {
            if (context.control is not ButtonControl control || context.control.device is not Mouse mouse) { return; }
            var kind = control.isPressed ? IntentKind.PointerDown : IntentKind.PointerUp;
            Queue(Intent.Mouse(kind, ScreenToField(mouse.position.ReadValue()), EventTime(context)));
        }

        private void OnRightButton(InputAction.CallbackContext context)
        {
            if (context.control is ButtonControl { isPressed: true }) { Queue(Intent.Of(IntentKind.CancelAim, EventTime(context))); }
        }

        private void OnPosition(InputAction.CallbackContext context)
        {
            if (context.control.device is not Mouse mouse || !mouse.leftButton.isPressed) { return; }
            Queue(Intent.Mouse(IntentKind.PointerMove, ScreenToField(context.ReadValue<Vector2>()), EventTime(context)));
        }

        /// <summary>画面の座標（ピクセル）→ 盤の座標（= ワールド座標）。</summary>
        public V2 ScreenToField(Vector2 screen)
        {
            var world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z));
            return PebbleSiegeStage.ToField(world);
        }

        /// <summary>盤の座標 → 画面の座標（ピクセル）。テストがマウスを動かすのに使う。</summary>
        public Vector2 FieldToScreen(V2 point) => _camera.WorldToScreenPoint(PebbleSiegeStage.ToWorld(point));

        private void Bind(IntentKind kind, params string[] bindings)
        {
            var action = NewAction(kind.ToString(), InputActionType.Button, bindings);
            action.performed += context => Queue(Intent.Of(kind, EventTime(context)));
        }

        private void BindMove(int delta, string binding)
        {
            var action = NewAction("Move" + delta, InputActionType.Button, binding);
            action.performed += context => Queue(Intent.Move(delta, EventTime(context)));
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
                foreach (var intent in _intents) { Flow.Apply(intent); }
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

            // 同じフレームに届いていた入力も捨てる（フォーカス喪失の後に再開しないように）。
            Flow.SetFocus(focus);
            _intents.Clear();
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

            // 画面が 16:9 より縦長でも、盤の幅が収まるようにカメラを引く。
            _camera.orthographicSize = Mathf.Max(_cameraSize, ViewWidth / (2 * _camera.aspect));

            // タイトル・面選択の背景には、選んでいる面の初期配置を置く（物理は進めない）。
            if (Flow.Run == null && World.Loaded != Flow.Cursor) { World.Load(Flow.Cursor, Flow.Book.Stages[Flow.Cursor]); }

            _stage.RenderLauncher(Flow, _rules);
            _stage.RenderEffects(World, _rules, _settings.EffectTicks);
            RenderPieces();
            RenderHud();
        }

        /// <summary>部材の色を残りの HP で暗くする。</summary>
        private void RenderPieces()
        {
            var run = Flow.Run;
            for (var i = 0; i < World.PieceCount; i++)
            {
                var piece = World.Piece(i);
                if (piece == null) { continue; }
                var full = _rules.Spec(World.Stage.Pieces[i].Material).Health;
                var ratio = run == null ? 1f : (float)run.Health(i) / full;
                piece.Visual.color = Color.Lerp(_worn, Color.white, ratio);
            }
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            _shownHud = null;
            root.style.unityFont = _font;
            var tiles = root.Q("tiles");
            tiles.Clear();
            for (var i = 0; i < Flow.Book.Stages.Count; i++)
            {
                var index = i;
                var tile = new Button(() => Queue(Intent.Choose(index, Now))) { name = TileName(i) };
                tile.AddToClassList("tile");
                tile.Add(new Label { name = "tile-number", pickingMode = PickingMode.Ignore });
                tile.Add(new Label { name = "tile-name", pickingMode = PickingMode.Ignore });
                tile.Add(new Label { name = "tile-best", pickingMode = PickingMode.Ignore });
                tiles.Add(tile);
            }

            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null) { return; }
            var run = Flow.Run;
            var screen = Flow.Screen;
            var state = (screen, Flow.Cursor, Flow.Progress.Unlocked, run, run?.ShotsUsed ?? 0, run?.Score ?? 0, run?.TargetsLeft ?? 0, run?.Phase ?? RunPhase.Aiming, run?.CanDetonate ?? false, Flow.IsNewBest);
            if (Equals(_shownHud, state)) { return; }
            _shownHud = state;

            var playing = run != null && screen != AppScreen.Result;
            _root.Q("bar").style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<Label>("hint").style.display = playing && screen == AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
            if (playing && run != null)
            {
                var best = Flow.Progress.Best(Flow.Cursor);
                _root.Q<Label>("stage").text = $"第 {Flow.Cursor + 1} 面  {run.Stage.Name}";
                _root.Q<Label>("shots").text = "弾  " + ShotList(run);
                _root.Q<Label>("score").text = $"得点  {run.Score}";
                _root.Q<Label>("targets").text = $"灯籠  残り {run.TargetsLeft}";
                _root.Q<Label>("best").text = best < 0 ? "最高  —" : $"最高  {best}";
                _root.Q<Label>("hint").text = run.CanDetonate
                    ? "クリック / Space：起爆（触れてから少しで自動で起爆）"
                    : run.Stage.Hint + "    （R：やり直す・Esc：一時停止）";
            }

            var panel = _root.Q("panel");
            panel.style.display = screen == AppScreen.Playing ? DisplayStyle.None : DisplayStyle.Flex;
            panel.EnableInClassList("outcome", screen == AppScreen.Outcome);
            _root.Q<Label>("headline").text = Headline(screen, run);
            _root.Q<Label>("message").text = Message(Flow);
            _root.Q("tiles").style.display = screen == AppScreen.Select ? DisplayStyle.Flex : DisplayStyle.None;
            if (screen == AppScreen.Select) { RenderTiles(); }
        }

        /// <summary>残りの弾の名前（次に撃つ弾から）。</summary>
        private string ShotList(SiegeRun run)
        {
            _text.Clear();
            for (var i = run.ShotsUsed; i < run.Stage.Shots.Count; i++)
            {
                if (_text.Length > 0) { _text.Append(" ・ "); }
                _text.Append(ShotName(run.Stage.Shots[i]));
            }

            return _text.Length == 0 ? "なし" : _text.ToString();
        }

        public static string ShotName(ShotKind kind) => kind switch
        {
            ShotKind.Normal => "通常",
            ShotKind.Heavy => "重い",
            ShotKind.Bomb => "爆発",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の弾です。"),
        };

        public static string Stars(int stars) => new string('★', stars) + new string('☆', 3 - stars);

        private void RenderTiles()
        {
            for (var i = 0; i < Flow.Book.Stages.Count; i++)
            {
                var tile = _root.Q<Button>(TileName(i));
                var unlocked = Flow.Progress.IsUnlocked(i);
                tile.SetEnabled(unlocked);
                tile.EnableInClassList("selected", i == Flow.Cursor);
                tile.EnableInClassList("solved", Flow.Progress.IsCleared(i));
                tile.Q<Label>("tile-number").text = (i + 1).ToString(CultureInfo.InvariantCulture);
                tile.Q<Label>("tile-name").text = unlocked ? Flow.Book.Stages[i].Name : "？";
                var best = Flow.Progress.Best(i);
                tile.Q<Label>("tile-best").text = best < 0 ? " " : $"{Stars(Flow.Progress.Stars(i))}  {best}";
            }
        }

        public static string Headline(AppScreen screen, SiegeRun run) => screen switch
        {
            AppScreen.Title => "PEBBLE SIEGE",
            AppScreen.Select => "面をえらぶ",
            AppScreen.Paused => "一時停止",
            AppScreen.Outcome => run?.Phase == RunPhase.Won ? $"砦を落とした！  {Stars(run.Stars)}" : "灯籠が残った…",
            AppScreen.Result => "全 5 面 クリア！",
            _ => string.Empty,
        };

        public static string Message(SiegeFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var run = flow.Run;
            switch (flow.Screen)
            {
                case AppScreen.Title:
                    return "石つぶてで砦の灯籠（赤い灯り）を全部落とそう。\n" +
                        "発射台の弾を押して引き、離すと引いた逆向きに飛ぶ。点は飛ぶ道すじ。\n" +
                        "爆発の弾は飛んでいる間にクリックで起爆。全部止まってから勝ち負けが決まる。\n\nクリック / Enter：はじめる";
                case AppScreen.Select:
                    return "← →：えらぶ    Enter / クリック：はじめる    Esc：タイトルへ\n勝った面の次の面が解放される。";
                case AppScreen.Paused:
                    return "Esc / Enter：つづける    R：やり直す    Backspace：面選択へ";
                case AppScreen.Outcome:
                    if (run == null) { return string.Empty; }
                    if (run.Phase == RunPhase.Won)
                    {
                        var next = flow.Cursor + 1 < flow.Book.Stages.Count ? "次の面へ" : "結果へ";
                        return $"得点 {run.Score}（残った弾 {run.ShotsLeft} 発）{(flow.IsNewBest ? "  <color=#FFD66E>最高記録！</color>" : string.Empty)}\n" +
                            $"クリック / Enter：{next}    R：やり直す    Esc：面選択へ";
                    }

                    return "弾を使い切った。\nクリック / Enter / R：やり直す    Esc：面選択へ";
                case AppScreen.Result:
                    var stars = 0;
                    for (var i = 0; i < flow.Book.Stages.Count; i++) { stars += flow.Progress.Stars(i); }
                    return $"最高点の合計 {flow.Progress.Total}    星 {stars} / {3 * flow.Book.Stages.Count}\n\nクリック / Enter：タイトルへ（勝った面はそのまま選べる）";
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

            return Clip(name, samples);
        }

        // 減衰する雑音（決まった系列：xorshift）。破壊・爆発の音。
        private AudioClip Noise(string name, float seconds)
        {
            var samples = new float[(int)(44100 * seconds)];
            var state = 2463534242u;
            var low = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                var white = ((state & 0xFFFF) / 32768f) - 1f;
                low += (white - low) * .2f;
                var t = (float)i / samples.Length;
                samples[i] = low * .12f * (1f - t) * (1f - t);
            }

            return Clip(name, samples);
        }

        private AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, 44100, false);
            clip.SetData(samples, 0);
            _clips.Add(clip);
            return clip;
        }

        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            if (_hudRenderer != null) { _hudRenderer.UnregisterUIReloadCallback(OnHudReloaded); }
            World?.Dispose();
            Destroy(_font);
            foreach (var clip in _clips) { Destroy(clip); }
        }
    }
}
