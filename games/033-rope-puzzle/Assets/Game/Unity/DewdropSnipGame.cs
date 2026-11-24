using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using IntGeometry;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// 入力・HUD・音。マウスの押下・移動・解放とキーを、起きた時刻つきで 1 本の列に入れ、フレームの初めに起きた順で <see cref="SnipFlow"/> へ渡す。
    /// マウスの位置は画面 → 世界 → 盤の座標に直して渡す。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class DewdropSnipGame : MonoBehaviour
    {
        [SerializeField] private DewdropSnipSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private DewdropSnipStage _stage;

        private readonly List<Intent> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<(FlowEvents Event, AudioClip Clip)> _sounds = new();
        private readonly Vector3[] _trail = new Vector3[DewdropSnipStage.TrailCapacity];
        private readonly long[] _trailTicks = new long[DewdropSnipStage.TrailCapacity];
        private readonly Vector3[] _trailShown = new Vector3[DewdropSnipStage.TrailCapacity];
        private PuzzleRun[] _previews = Array.Empty<PuzzleRun>();
        private bool _hasFocus = true;
        private int _trailCount;
        private VisualElement _root;
        private int _version = -1;
        private object _shownHud;
        private Font _font;
        private AudioSource _audio;
        private float _cameraSize;

        public SnipFlow Flow { get; private set; }

        public DewdropSnipSettings Settings => _settings;

        public DewdropSnipStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した出来事（撮影台本・テストが場面を待つのに使う）。</summary>
        public FlowEvents LastEvents { get; private set; }

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>問題選択の札の名前（UXML）。</summary>
        public static string TileName(int index) => "tile-" + index;

        private void Awake()
        {
            var book = _settings.Validate();
            var mismatch = _stage.Mismatch();
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が欠けています：" + mismatch); }

            Flow = new SnipFlow(_settings.Rules, book);
            _stage.Configure(_settings.Rules);
            _cameraSize = _camera.orthographicSize;

            // タイトル・問題選択の背景に出す問の初期状態。描画のために毎フレーム作らないよう、起動時に 1 回だけ作る（008 の知見）。
            _previews = new PuzzleRun[book.Puzzles.Count];
            for (var i = 0; i < _previews.Length; i++) { _previews[i] = new PuzzleRun(_settings.Rules, book.Puzzles[i]); }

            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            _sounds.Add((FlowEvents.Succeeded, Tone("Succeeded", 660, 1320, .6f)));
            _sounds.Add((FlowEvents.Failed, Tone("Failed", 330, 90, .6f)));
            _sounds.Add((FlowEvents.Cut, Tone("Cut", 1800, 900, .06f)));
            _sounds.Add((FlowEvents.Started | FlowEvents.Retried, Tone("Start", 520, 780, .1f)));
            _sounds.Add((FlowEvents.Selected, Tone("Select", 700, 700, .03f)));

            // マウス：押下・解放とその時の位置、押している間の移動。押下・解放と移動は PassThrough で 1 つずつ拾う。
            var button = NewAction("Press", InputActionType.PassThrough, "<Mouse>/leftButton");
            button.performed += OnButton;
            var position = NewAction("Point", InputActionType.PassThrough, "<Mouse>/position");
            position.performed += OnPosition;
            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space");
            Bind(IntentKind.Cancel, "<Keyboard>/escape");
            Bind(IntentKind.Retry, "<Keyboard>/r");
            Bind(IntentKind.Back, "<Keyboard>/backspace");
            BindMove(-1, "<Keyboard>/leftArrow");
            BindMove(1, "<Keyboard>/rightArrow");
            BindMove(-SnipFlow.SelectColumns, "<Keyboard>/upArrow");
            BindMove(SnipFlow.SelectColumns, "<Keyboard>/downArrow");
            foreach (var action in _actions) { action.Enable(); }

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void OnButton(InputAction.CallbackContext context)
        {
            if (context.control is not ButtonControl control || context.control.device is not Mouse mouse) { return; }
            var kind = control.isPressed ? IntentKind.PointerDown : IntentKind.PointerUp;
            Queue(Intent.Mouse(kind, ScreenToBoard(mouse.position.ReadValue()), EventTime(context)));
        }

        private void OnPosition(InputAction.CallbackContext context)
        {
            if (context.control.device is not Mouse mouse || !mouse.leftButton.isPressed) { return; }
            Queue(Intent.Mouse(IntentKind.PointerMove, ScreenToBoard(context.ReadValue<Vector2>()), EventTime(context)));
        }

        /// <summary>画面の座標（ピクセル）→ 盤の座標。</summary>
        public Vec2i ScreenToBoard(Vector2 screen)
        {
            var world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z));
            return DewdropSnipStage.ToBoard(_stage.transform.InverseTransformPoint(world));
        }

        /// <summary>盤の座標 → 画面の座標（ピクセル）。テストがマウスを動かすのに使う。</summary>
        public Vector2 BoardToScreen(Vec2i point) => _camera.WorldToScreenPoint(_stage.transform.TransformPoint(DewdropSnipStage.ToLocal(point)));

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
                foreach (var intent in _intents) { Apply(intent); }
            }

            _intents.Clear();

            // 撮影中は台本が Tick を進める。timeScale が 0 の間（テストが Tick を直接進めるとき）も時計で進めない。
            if (!EvidenceCapture.IsRunning && Time.timeScale > 0) { Flow.Advance(Now); }
            else { Flow.EndFrame(); }
            ConsumeEvents();
            Render();
        }

        /// <summary>意図を Flow へ渡し、なぞりの軌跡を記録する（撮影台本も使う）。</summary>
        public void Apply(Intent intent)
        {
            var before = Flow.SwipePoint;
            Flow.Apply(intent);
            var run = Flow.Run;
            if (run == null || Flow.Screen != AppScreen.Playing) { return; }
            if (intent.Kind == IntentKind.PointerDown) { _trailCount = 0; }
            if (intent.Kind == IntentKind.PointerDown || ((intent.Kind == IntentKind.PointerMove || intent.Kind == IntentKind.PointerUp) && before != null && before != intent.Point))
            {
                AddTrail(DewdropSnipStage.ToLocal(intent.Point), run.Ticks);
            }
        }

        private void AddTrail(Vector3 point, long tick)
        {
            if (_trailCount == _trail.Length)
            {
                Array.Copy(_trail, 1, _trail, 0, _trail.Length - 1);
                Array.Copy(_trailTicks, 1, _trailTicks, 0, _trailTicks.Length - 1);
                _trailCount--;
            }

            _trail[_trailCount] = point;
            _trailTicks[_trailCount] = tick;
            _trailCount++;
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
            if ((events & (FlowEvents.Started | FlowEvents.Retried | FlowEvents.ToSelect | FlowEvents.Title)) != 0) { _trailCount = 0; }
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

            // 画面が 16:9 より縦長でも、盤の幅（と左右の少しの余白）が収まるようにカメラを引く。
            _camera.orthographicSize = Mathf.Max(_cameraSize, (Board.Columns + .4f) / (2 * _camera.aspect));
            _stage.Render(run ?? _previews[Flow.Cursor], _settings.EffectTicks);
            RenderTrail(run);
            RenderHud();
        }

        /// <summary>軌跡は、最後の点から <see cref="DewdropSnipSettings.TrailTicks"/> Tick 以内の点だけを描く（撮影でも同じ絵になるよう Tick で決める）。</summary>
        private void RenderTrail(PuzzleRun run)
        {
            var shown = 0;
            if (run != null && Flow.Screen == AppScreen.Playing)
            {
                for (var i = 0; i < _trailCount; i++)
                {
                    if (run.Ticks - _trailTicks[i] < _settings.TrailTicks) { _trailShown[shown++] = _trail[i]; }
                }
            }

            _stage.RenderTrail(_trailShown, shown);
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
            for (var i = 0; i < Flow.Book.Puzzles.Count; i++)
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
            var state = (screen, Flow.Cursor, Flow.Progress.Unlocked, run, run?.Moves ?? 0, run?.Status ?? RunStatus.Playing, Flow.IsNewBest);
            if (Equals(_shownHud, state)) { return; }
            _shownHud = state;

            var playing = run != null && screen != AppScreen.Result;
            _root.Q("bar").style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<Label>("hint").style.display = playing && screen == AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
            if (playing && run != null)
            {
                var best = Flow.Progress.Best(Flow.Cursor);
                _root.Q<Label>("puzzle").text = $"第 {Flow.Cursor + 1} 問  {run.Puzzle.Name}";
                _root.Q<Label>("moves").text = $"手数  {run.Moves}";
                _root.Q<Label>("par").text = $"目安  {run.Puzzle.Par}";
                _root.Q<Label>("best").text = best < 0 ? "最少  —" : $"最少  {best}";
                _root.Q<Label>("hint").text = run.Puzzle.Hint + "    （R：やり直す・Esc：一時停止）";
            }

            var panel = _root.Q("panel");
            panel.style.display = screen == AppScreen.Playing ? DisplayStyle.None : DisplayStyle.Flex;
            panel.EnableInClassList("outcome", screen == AppScreen.Outcome);
            _root.Q<Label>("headline").text = Headline(screen, run);
            _root.Q<Label>("message").text = Message(Flow);
            _root.Q("tiles").style.display = screen == AppScreen.Select ? DisplayStyle.Flex : DisplayStyle.None;
            if (screen == AppScreen.Select) { RenderTiles(); }
        }

        private void RenderTiles()
        {
            for (var i = 0; i < Flow.Book.Puzzles.Count; i++)
            {
                var tile = _root.Q<Button>(TileName(i));
                var unlocked = Flow.Progress.IsUnlocked(i);
                tile.SetEnabled(unlocked);
                tile.EnableInClassList("selected", i == Flow.Cursor);
                tile.EnableInClassList("solved", Flow.Progress.IsSolved(i));
                tile.Q<Label>("tile-number").text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                tile.Q<Label>("tile-name").text = unlocked ? Flow.Book.Puzzles[i].Name : "？";
                var best = Flow.Progress.Best(i);
                tile.Q<Label>("tile-best").text = best < 0 ? " " : $"最少 {best} 手";
            }
        }

        public static string Headline(AppScreen screen, PuzzleRun run) => screen switch
        {
            AppScreen.Title => "DEWDROP SNIP",
            AppScreen.Select => "問題をえらぶ",
            AppScreen.Paused => "一時停止",
            AppScreen.Outcome => run?.Status == RunStatus.Succeeded ? "とどいた！" : run?.Reason == FailReason.Spike ? "トゲに触れた…" : "落ちてしまった…",
            AppScreen.Result => "全 10 問 クリア！",
            _ => string.Empty,
        };

        public static string Message(SnipFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var run = flow.Run;
            switch (flow.Screen)
            {
                case AppScreen.Title:
                    return "ロープで吊るした露玉を、花の器へ届けよう。\n" +
                        "マウスの左ボタンを押したまま動かすと、なぞった線が横切ったロープが切れる。\n" +
                        "器に触れたら成功。下へ落ちる・トゲに触れると失敗。\n\nクリック / Enter：はじめる";
                case AppScreen.Select:
                    return "← → ↑ ↓：えらぶ    Enter / クリック：はじめる    Esc：タイトルへ\n解いた問の次の問が解放される。";
                case AppScreen.Paused:
                    return "Esc / Enter：つづける    R：やり直す    Backspace：問題選択へ";
                case AppScreen.Outcome:
                    if (run == null) { return string.Empty; }
                    if (run.Status == RunStatus.Succeeded)
                    {
                        var next = flow.Cursor + 1 < flow.Book.Puzzles.Count ? "次の問へ" : "結果へ";
                        return $"手数 {run.Moves}（目安 {run.Puzzle.Par}）{(flow.IsNewBest ? "  <color=#FFD66E>最少記録！</color>" : string.Empty)}\n" +
                            $"クリック / Enter：{next}    R：やり直す    Esc：問題選択へ";
                    }

                    return "クリック / Enter / R：やり直す    Esc：問題選択へ";
                case AppScreen.Result:
                    var total = 0;
                    var par = 0;
                    for (var i = 0; i < flow.Book.Puzzles.Count; i++)
                    {
                        total += flow.Progress.Best(i);
                        par += flow.Book.Puzzles[i].Par;
                    }

                    return $"最少手数の合計 {total}（目安の合計 {par}）\n\nクリック / Enter：タイトルへ（解いた問はそのまま選べる）";
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
