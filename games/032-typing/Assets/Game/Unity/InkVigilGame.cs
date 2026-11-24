using System;
using System.Collections.Generic;
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
    /// 入力・HUD・音。打鍵は物理キー（Input System の <see cref="Key"/>）を読み、IME と配列の文字変換を通さない。
    /// 打鍵と画面の操作は起きた時刻つきで 1 本の列に入れ、フレームの初めに起きた順で <see cref="VigilFlow"/> へ渡す。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class InkVigilGame : MonoBehaviour
    {
        private const string TypedColor = "#FFD66E";
        private const string DoneKanaColor = "#6E6A86";

        [SerializeField] private InkVigilSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private InkVigilStage _stage;

        private readonly List<Intent> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<(FlowEvents Event, AudioClip Clip)> _sounds = new();
        private readonly Plate[] _plates = new Plate[InkVigilStage.MaxLanes];
        private readonly List<Enemy> _order = new();
        private bool _hasFocus = true;
        private VisualElement _root;
        private int _version = -1;
        private object _shownHud;
        private object _shownTyping;
        private Vector3 _cameraHome;
        private Font _font;
        private AudioSource _audio;

        public VigilFlow Flow { get; private set; }

        public InkVigilSettings Settings => _settings;

        public InkVigilStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した出来事（撮影台本が場面を待つのに使う）。</summary>
        public FlowEvents LastEvents { get; private set; }

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>列 <paramref name="lane"/> の札（テスト用）。</summary>
        public VisualElement PlateOf(int lane) => _plates[lane].Root;

        private void Awake()
        {
            var book = _settings.Validate();
            var mismatch = _stage.Mismatch();
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が欠けています：" + mismatch); }

            // 再戦は新しいシード（起動時の値から試合ごとに 1 進める。010 の知見）。撮影中は固定のシード。
            Flow = new VigilFlow(_settings.Rules, book, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _cameraHome = _camera.transform.position;
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            _sounds.Add((FlowEvents.Cleared, Tone("Cleared", 660, 1320, .7f)));
            _sounds.Add((FlowEvents.Failed, Tone("Failed", 330, 80, .7f)));
            _sounds.Add((FlowEvents.Hit, Tone("Hit", 300, 90, .3f)));
            _sounds.Add((FlowEvents.Defeated, Tone("Defeated", 660, 990, .12f)));
            _sounds.Add((FlowEvents.Miss, Tone("Miss", 180, 120, .08f)));
            _sounds.Add((FlowEvents.Correct, Tone("Correct", 1250, 1150, .025f)));
            _sounds.Add((FlowEvents.Started, Tone("Start", 520, 780, .1f)));
            _sounds.Add((FlowEvents.Selected, Tone("Select", 700, 700, .03f)));

            // 打鍵：文字キーと Backspace の押下を、同時押し（ロールオーバー）でも 1 つずつ拾う。
            var keys = NewAction("Keys", InputActionType.PassThrough);
            for (var key = Key.A; key <= Key.Z; key++) { keys.AddBinding("<Keyboard>/" + key.ToString().ToLowerInvariant()); }
            keys.AddBinding("<Keyboard>/minus");
            keys.AddBinding("<Keyboard>/backspace");
            keys.performed += OnKey;
            Bind(IntentKind.Up, "<Keyboard>/upArrow");
            Bind(IntentKind.Down, "<Keyboard>/downArrow");
            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.Cancel, "<Keyboard>/escape");
            foreach (var action in _actions) { action.Enable(); }

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private void OnEnable() => Keyboard.current?.SetIMEEnabled(false);

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void OnKey(InputAction.CallbackContext context)
        {
            if (context.control is not KeyControl control || !control.isPressed) { return; }
            var time = EventTime(context);
            var key = control.keyCode;
            if (key == Key.Backspace) { Queue(Intent.Of(IntentKind.Backspace, time)); }
            else if (key == Key.Minus) { Queue(Intent.Type('-', time)); }
            else if (key >= Key.A && key <= Key.Z) { Queue(Intent.Type((char)('a' + (key - Key.A)), time)); }
        }

        private void Bind(IntentKind kind, params string[] bindings)
        {
            var action = NewAction(kind.ToString(), InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.performed += context => Queue(Intent.Of(kind, EventTime(context)));
        }

        /// <summary>入力イベントが起きた時刻。今より後にはしない。</summary>
        private static double EventTime(InputAction.CallbackContext context) => Math.Min(context.time, Now);

        private InputAction NewAction(string name, InputActionType type)
        {
            var action = new InputAction(name, type);
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

            // 同じフレームに届いていた打鍵も捨てる（フォーカス喪失の後に再開しないように）。
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
            var run = Flow.Run;
            if (run == null) { _stage.RenderEmpty(); }
            else { _stage.Render(run, _settings.EffectTicks); }
            _camera.transform.position = _cameraHome + InkVigilStage.Shake(run, _settings.EffectTicks);
            RenderHud();
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            _shownHud = null;
            _shownTyping = null;
            root.style.unityFont = _font;
            var container = root.Q("plates");
            container.Clear();
            for (var i = 0; i < _plates.Length; i++)
            {
                _plates[i] = new Plate();
                container.Add(_plates[i].Root);
            }

            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null) { return; }
            var run = Flow.Run;
            var screen = Flow.Screen;
            var state = (screen, Flow.Selected, run?.Defeated ?? 0, run?.Score ?? 0, run?.Combo ?? 0, run?.Hp ?? 0, run?.Status ?? RunStatus.Playing);
            if (!Equals(_shownHud, state))
            {
                _shownHud = state;
                _root.Q("stats").style.display = run == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (run != null)
                {
                    _root.Q<Label>("difficulty").text = DifficultyName(run.Difficulty);
                    _root.Q<Label>("defeated").text = $"撃破  {run.Defeated} / {run.Goal}";
                    _root.Q<Label>("score").text = $"得点  {run.Score}";
                    _root.Q<Label>("combo").text = $"連続  {run.Combo}";
                    _root.Q<Label>("hp").text = Hearts(run.Hp, run.MaxHp);
                }

                _root.Q("panel").style.display = screen != AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
                _root.Q<Label>("headline").text = Headline(screen, run);
                _root.Q<Label>("message").text = Message(Flow);
            }

            RenderTyping(run, screen);
            RenderPlates(run, screen);
        }

        /// <summary>画面下の大きな表示：狙っている敵の表示文・読み・ローマ字（打った部分は色を変える）。</summary>
        private void RenderTyping(TypingRun run, AppScreen screen)
        {
            var typing = _root.Q("typing");
            var visible = run != null && screen == AppScreen.Playing;
            typing.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) { return; }
            var target = run.Target;
            var miss = run.LastMissTick >= 0 && run.Ticks - run.LastMissTick < _settings.EffectTicks;
            var state = (target?.Id ?? -1, target?.Matcher.TypedLength ?? 0, miss);
            if (Equals(_shownTyping, state)) { return; }
            _shownTyping = state;
            typing.EnableInClassList("miss", miss);
            _root.Q<Label>("typing-display").text = target == null ? "狙いなし" : target.Word.Display;
            _root.Q<Label>("typing-kana").text = target == null ? "最初の 1 文字が合う、いちばん手前の敵を狙う" : KanaText(target.Matcher);
            _root.Q<Label>("typing-romaji").text = target == null ? " " : RomajiText(target.Matcher);
        }

        /// <summary>敵の頭の上の札。奥ほど小さく、狙いの札をいちばん前に出す。</summary>
        private void RenderPlates(TypingRun run, AppScreen screen)
        {
            foreach (var plate in _plates) { plate.Root.style.display = DisplayStyle.None; }
            if (run == null || screen == AppScreen.Title || _root.panel == null) { return; }

            _order.Clear();
            _order.AddRange(run.Enemies);
            _order.Sort((a, b) => b.Distance != a.Distance ? b.Distance.CompareTo(a.Distance) : a.Id.CompareTo(b.Id));
            foreach (var enemy in _order) { Show(_plates[enemy.Lane], enemy, run, enemy == run.Target); }
            if (run.Target != null) { _plates[run.Target.Lane].Root.BringToFront(); }
        }

        private void Show(Plate plate, Enemy enemy, TypingRun run, bool targeted)
        {
            plate.Root.style.display = DisplayStyle.Flex;
            plate.Root.BringToFront();
            var position = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, _stage.HeadOf(enemy, run.Lanes), _camera);
            plate.Root.style.left = position.x;
            plate.Root.style.top = position.y;
            var scale = .7f + (.3f * InkVigilPerspective.ScaleAt((float)enemy.Distance / Enemy.Far));
            plate.Root.style.scale = new Scale(new Vector3(scale, scale, 1));
            plate.Root.EnableInClassList("target", targeted);
            var state = (enemy.Id, enemy.Matcher.TypedLength);
            if (Equals(plate.Shown, state)) { return; }
            plate.Shown = state;
            plate.Display.text = enemy.Word.Display;
            plate.Kana.text = KanaText(enemy.Matcher);
            plate.Romaji.text = RomajiText(enemy.Matcher);
        }

        /// <summary>読み。打ち終えたかなを暗くする。</summary>
        public static string KanaText(RomajiMatcher matcher)
        {
            if (matcher == null) { throw new ArgumentNullException(nameof(matcher)); }
            var kana = matcher.Reading.Kana;
            var done = matcher.KanaDone;
            return done == 0 ? kana : $"<color={DoneKanaColor}>{kana.Substring(0, done)}</color>{kana.Substring(done)}";
        }

        /// <summary>ローマ字。打った部分は色を変え、残りは今の綴りに合わせた案内。</summary>
        public static string RomajiText(RomajiMatcher matcher)
        {
            if (matcher == null) { throw new ArgumentNullException(nameof(matcher)); }
            var typed = matcher.Typed;
            return typed.Length == 0 ? matcher.Guide : $"<color={TypedColor}>{typed}</color>{matcher.Guide}";
        }

        public static string DifficultyName(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => "やさしい",
            Difficulty.Normal => "ふつう",
            Difficulty.Hard => "むずかしい",
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未定義の難度です。"),
        };

        public static string Hearts(int hp, int maxHp) => "体力  " + new string('♥', Math.Max(0, hp)) + new string('♡', Math.Max(0, maxHp - hp));

        public static string Headline(AppScreen screen, TypingRun run) => screen switch
        {
            AppScreen.Title => "INK VIGIL",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => run?.Status == RunStatus.Cleared ? "夜を守りきった！" : "灯が消えた…",
            _ => string.Empty,
        };

        public static string Message(VigilFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var run = flow.Run;
            switch (flow.Screen)
            {
                case AppScreen.Title:
                    var lines = "回廊の奥から、言葉を背負った影が迫ってくる。\n影の頭上の言葉をローマ字で打ち切ると消える。手前の線まで来られると体力が減る。\n\n";
                    foreach (var difficulty in new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
                    {
                        var selected = difficulty == flow.Selected;
                        var best = flow.BestScore(difficulty);
                        lines += $"{(selected ? "<color=#FFD66E>▶ " : "    ")}{DifficultyName(difficulty)}{(best > 0 ? $"    最高 {best}" : string.Empty)}{(selected ? "</color>" : string.Empty)}\n";
                    }

                    return lines + "\n↑ ↓：難度    Enter：はじめる\n打鍵中  Backspace：狙いを外す    Esc：一時停止\n綴りの例  し＝shi / si    つ＝tsu / tu    ん＝nn（次が子音なら n）    っ＝次の子音を重ねる";
                case AppScreen.Paused:
                    return "Esc / Enter：つづける    T：タイトルへ";
                case AppScreen.Result:
                    if (run == null) { return string.Empty; }
                    return $"{DifficultyName(run.Difficulty)}    撃破 {run.Defeated} / {run.Goal}\n" +
                        $"得点 {run.Score}{(flow.IsNewBest ? "  <color=#FFD66E>最高記録！</color>" : string.Empty)}\n" +
                        $"正しい打鍵 {run.CorrectKeys}    誤字 {run.Misses}    正確さ {run.AccuracyPermil / 10}.{run.AccuracyPermil % 10}%\n" +
                        $"打鍵の速さ {flow.KeysPerMinute} 打/分    最大の連続 {run.MaxCombo}\n\nEnter：もう一度    Esc：タイトルへ";
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

        /// <summary>敵 1 体の札（表示文・読み・ローマ字）。</summary>
        private sealed class Plate
        {
            public Plate()
            {
                Root = new VisualElement { pickingMode = PickingMode.Ignore };
                Root.AddToClassList("plate");
                Root.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
                Root.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(100));
                Display = Add("plate-display");
                Kana = Add("plate-kana");
                Romaji = Add("plate-romaji");
            }

            public VisualElement Root { get; }

            public Label Display { get; }

            public Label Kana { get; }

            public Label Romaji { get; }

            public object Shown { get; set; }

            private Label Add(string className)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList(className);
                Root.Add(label);
                return label;
            }
        }
    }
}
