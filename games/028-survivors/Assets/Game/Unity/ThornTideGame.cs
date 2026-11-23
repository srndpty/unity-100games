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
    /// <summary>
    /// 入力・カメラ・HUD・音をつなぐ。キーの押下・解放・数字キー・3 択のカードのクリックは、時刻付きの意図の列（<see cref="Intent"/>）にして
    /// 起きた順に App へ渡す（012 の知見）。カメラは自機を中心にした視野（<see cref="TideRun.ViewOf"/>）に合わせる。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class ThornTideGame : MonoBehaviour
    {
        /// <summary>撃破・取得の音を続けて鳴らさない最短の間隔（秒）。数百体を倒しても耳障りにしない。</summary>
        private const float BusySoundGap = .09f;

        [SerializeField] private ThornTideSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private ThornTideStage _stage;

        // 種類の違う操作も起きた順に 1 本の列で処理する。Pointer は HUD のボタン由来（フォーカス復帰直後は捨てる）。
        private readonly List<(Intent Intent, bool Pointer)> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<(FlowEvents Event, AudioClip Clip, bool Busy)> _sounds = new();
        private readonly StringBuilder _text = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -2;
        private float _lastBusySound = -1;
        private VisualElement _root;
        private int _version = -1;
        private object _shownHud;
        private object _shownCards;
        private bool _cardsDrawn;
        private TideConfig _rules;
        private Font _font;
        private AudioSource _audio;

        public TideFlow Flow { get; private set; }

        public ThornTideSettings Settings => _settings;

        /// <summary>定義アセットから組み立てた試合の設定（複製）。</summary>
        public TideConfig Rules => _rules.Copy();

        public ThornTideStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public VisualElement HudRoot => _root;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した出来事（撮影台本が場面を待つのに使う）。</summary>
        public FlowEvents LastEvents { get; private set; }

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>カメラの中心（ワールド座標）：自機の位置（試合の前は原点）。視野は自機を中心にした矩形なので、その中央と同じ。</summary>
        public Vector3 ViewCenter
        {
            get
            {
                var run = Flow?.Run;
                if (run == null) { return _stage.transform.position; }
                var view = run.View;
                return _stage.transform.TransformPoint(ThornTideStage.Local(new IntGeometry.Vec2i(view.MinX + (view.Width / 2), view.MinY + (view.Height / 2))));
            }
        }

        private void Awake()
        {
            _rules = _settings.BuildRules();
            var mismatch = _stage.Mismatch();
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が正しくありません：" + mismatch); }
            _stage.Prepare(_rules, _settings.EnemyCells());

            // 再戦は新しいシード（起動時の値から試合ごとに 1 進める。010 の知見）。撮影中は固定のシード。
            Flow = new TideFlow(_rules, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。Busy の音（撃破・取得）は間を空ける。
            _sounds.Add((FlowEvents.Won, Tone("Won", 660, 1320, .8f), false));
            _sounds.Add((FlowEvents.Lost, Tone("Lost", 440, 80, .7f), false));
            _sounds.Add((FlowEvents.BossSpawned, Tone("Boss", 180, 90, .6f), false));
            _sounds.Add((FlowEvents.LevelUp, Tone("LevelUp", 520, 1040, .25f), false));
            _sounds.Add((FlowEvents.Hurt, Tone("Hurt", 300, 110, .2f), false));
            _sounds.Add((FlowEvents.Chose, Tone("Chose", 780, 980, .08f), false));
            _sounds.Add((FlowEvents.Started, Tone("Start", 520, 780, .12f), false));
            _sounds.Add((FlowEvents.GemCollected, Tone("Gem", 1300, 1700, .03f), true));
            _sounds.Add((FlowEvents.EnemyKilled, Tone("Killed", 700, 220, .05f), true));

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Hold(Control.Left, "<Keyboard>/a", "<Keyboard>/leftArrow");
            Hold(Control.Right, "<Keyboard>/d", "<Keyboard>/rightArrow");
            Hold(Control.Up, "<Keyboard>/w", "<Keyboard>/upArrow");
            Hold(Control.Down, "<Keyboard>/s", "<Keyboard>/downArrow");
            for (var i = 0; i < TideRun.ChoiceSlots; i++)
            {
                var choice = i;
                NewAction($"Pick{i + 1}", $"<Keyboard>/{i + 1}", $"<Keyboard>/numpad{i + 1}").performed +=
                    context => Queue(Intent.Pick(choice, EventTime(context)), false);
            }

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
                    // ウィンドウを選び直すクリックで HUD のボタン（3 択のカードを含む）を押さない。
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

        /// <summary>出来事を音にする。撮影台本も Tick を進めた後に呼ぶ。</summary>
        public void ConsumeEvents()
        {
            var events = Flow.TakeEvents();
            LastEvents = events;
            foreach (var (flag, clip, busy) in _sounds)
            {
                if ((events & flag) == 0) { continue; }
                if (busy)
                {
                    if (Time.unscaledTime - _lastBusySound < BusySoundGap) { continue; }
                    _lastBusySound = Time.unscaledTime;
                }

                _audio.PlayOneShot(clip);
                break;
            }
        }

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            var center = ViewCenter;
            var run = Flow.Run;
            if (run == null) { _stage.RenderInitial(_rules, center); }
            else { _stage.Render(run, _rules, center); }
            Frame(center);
            RenderHud();
        }

        /// <summary>カメラは視野（32×18 セル）が収まる大きさにし、その中央に置く。</summary>
        private void Frame(Vector3 center)
        {
            var height = _rules.ViewHeightCells / 2f;
            _camera.orthographicSize = Mathf.Max(height, _rules.ViewWidthCells / 2f / Mathf.Max(.01f, _camera.aspect));
            _camera.transform.position = new Vector3(center.x, center.y, _camera.transform.position.z);
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            _shownHud = null;
            _shownCards = null;
            _cardsDrawn = false;
            root.style.unityFont = _font;
            Connect("submit", IntentKind.Submit);
            Connect("pause", IntentKind.TogglePause);
            Connect("restart", IntentKind.Restart);
            Connect("title", IntentKind.BackToTitle);
            Connect("quit", IntentKind.Quit);
            for (var i = 0; i < TideRun.ChoiceSlots; i++)
            {
                var choice = i;
                root.Q<Button>("card" + i).clicked += () => Queue(Intent.Pick(choice, Now), true);
            }

            root.Query<Button>().ForEach(button => button.focusable = false);
            RenderHud();
        }

        private void Connect(string name, IntentKind kind) => _root.Q<Button>(name).clicked += () => Queue(Intent.Of(kind, Now), true);

        // 文字とボタンは表示が変わるときだけ書き換える。
        private void RenderHud()
        {
            if (_root == null) { return; }
            var run = Flow.Run;
            var screen = Flow.Screen;
            var boss = run?.BossIndex() ?? -1;
            var seconds = run == null ? 0 : (TimeLeft(run) + Units.TickRate - 1) / Units.TickRate;
            var state = (screen, run?.Status, seconds, run?.Level ?? 0, run?.Experience ?? 0, run?.Health ?? 0, run?.Kills ?? 0, run?.NormalCount ?? 0,
                boss >= 0 ? run!.Enemies.Health(boss) : -1, WeaponsKey(run));
            if (!Equals(_shownHud, state))
            {
                _shownHud = state;
                WriteHud(run, screen, boss, seconds);
            }

            RenderCards(run, screen);
        }

        private void WriteHud(TideRun run, AppScreen screen, int boss, int seconds)
        {
            _root.Q("stats").style.display = run == null ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<Label>("weapons").style.display = run == null ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q("boss").style.display = boss >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (run != null)
            {
                _root.Q<Label>("time").text = (run.BossSpawned ? "ボス 残り " : "ボスまで ") + $"{seconds / 60}:{seconds % 60:00}";
                _root.Q<Label>("level").text = $"Lv {run.Level}";
                _root.Q("xp").style.width = Length.Percent(100f * run.Experience / run.ExperienceToNext);
                _root.Q("hp").style.width = Length.Percent(100f * run.Health / _rules.PlayerHealth);
                _root.Q<Label>("kills").text = $"撃破 {run.Kills}";
                _root.Q<Label>("enemies").text = $"敵 {run.NormalCount}";
                _root.Q<Label>("weapons").text = WeaponList(run);
                if (boss >= 0)
                {
                    var def = run.Enemies.Def(boss);
                    _root.Q<Label>("boss-name").text = _settings.Enemies[def].DisplayName;
                    _root.Q("boss-hp").style.width = Length.Percent(100f * run.Enemies.Health(boss) / _rules.Enemies[def].Health);
                }
            }

            var menu = screen == AppScreen.Title || screen == AppScreen.Paused || screen == AppScreen.Result;
            _root.Q("panel").style.display = menu ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<Label>("headline").text = Headline(screen, run);
            _root.Q<Label>("message").text = Message(screen, run, _rules);
            Show("submit", menu);
            _root.Q<Button>("submit").text = screen switch
            {
                AppScreen.Title => "はじめる  /  Enter",
                AppScreen.Paused => "つづける  /  Enter",
                _ => "もう一度  /  Enter",
            };
            Show("pause", screen == AppScreen.Playing || screen == AppScreen.LevelUp);
            Show("restart", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("title", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("quit", screen == AppScreen.Title);
        }

        /// <summary>3 択のカード。候補と選んでいる番号が変わったときだけ書き換える。</summary>
        private void RenderCards(TideRun run, AppScreen screen)
        {
            var showing = screen == AppScreen.LevelUp && run != null && run.IsChoosing;
            var key = showing ? (object)(run!.Level - run.PendingLevelUps + 1, run.ChoiceCount, run.ChoiceAt(0), Flow.Highlight, run.PendingLevelUps) : null;
            if (_cardsDrawn && Equals(_shownCards, key)) { return; }
            _cardsDrawn = true;
            _shownCards = key;
            _root.Q("levelup").style.display = showing ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showing) { return; }
            _root.Q<Label>("levelup-title").text = run!.PendingLevelUps > 1 ? $"レベルアップ！（あと {run.PendingLevelUps - 1} 回）" : "レベルアップ！";
            for (var i = 0; i < TideRun.ChoiceSlots; i++)
            {
                var card = _root.Q<Button>("card" + i);
                card.style.display = i < run.ChoiceCount ? DisplayStyle.Flex : DisplayStyle.None;
                if (i >= run.ChoiceCount) { continue; }
                card.text = $"{i + 1}.  " + CardText(run.ChoiceAt(i));
                card.EnableInClassList("selected", i == Flow.Highlight);
            }
        }

        /// <summary>3 択のカードの文。</summary>
        public string CardText(LevelChoice choice)
        {
            if (choice.Kind == ChoiceKind.Heal) { return $"手当て\n\n体力を {_rules.HealAmount} 戻す"; }
            var weapon = _settings.Weapons[choice.Weapon];
            if (choice.Kind == ChoiceKind.Acquire) { return $"{weapon.DisplayName}  （新しい武器）\n\n{weapon.Summary}"; }
            var def = _rules.Weapons[choice.Weapon];
            return $"{weapon.DisplayName}  Lv{choice.Level - 1} → Lv{choice.Level}\n\n" + Describe(def.Kind, def.At(choice.Level - 1), def.At(choice.Level));
        }

        /// <summary>レベルの表の 2 行の違いを、種類に合った言葉で並べる（変わった値だけ）。</summary>
        public static string Describe(WeaponKind kind, WeaponLevel from, WeaponLevel to)
        {
            if (from == null) { throw new ArgumentNullException(nameof(from)); }
            if (to == null) { throw new ArgumentNullException(nameof(to)); }
            var lines = new List<string>();
            void Add(string label, int a, int b, Func<int, string> format)
            {
                if (a != b) { lines.Add($"{label}  {format(a)} → {format(b)}"); }
            }

            static string Plain(int value) => value.ToString();
            static string Cells(int value) => (value / (float)Units.Tile).ToString("0.#");
            static string Seconds(int ticks) => (ticks / (float)Units.TickRate).ToString("0.##") + " 秒";

            Add("ダメージ", from.Damage, to.Damage, Plain);
            Add(kind == WeaponKind.Orbit ? "同じ敵へ" : "間隔", from.IntervalTicks, to.IntervalTicks, Seconds);
            Add(kind switch { WeaponKind.Bolt => "弾の数", WeaponKind.Orbit => "刃の数", WeaponKind.Lash => "向き", _ => "連鎖" }, from.Count, to.Count, Plain);
            Add(kind switch { WeaponKind.Orbit => "回る半径", WeaponKind.Pulse => "範囲", WeaponKind.Lash => "長さ", _ => "射程" }, from.Range, to.Range, Cells);
            Add(kind switch { WeaponKind.Lash => "幅", WeaponKind.Arc => "飛ぶ距離", _ => "大きさ" }, from.Size, to.Size, Cells);
            if (kind == WeaponKind.Bolt) { Add("弾の速さ", from.Speed, to.Speed, Plain); }
            if (kind == WeaponKind.Orbit) { Add("回る速さ", from.Speed, to.Speed, Plain); }
            Add("貫通", from.Pierce, to.Pierce, Plain);
            return lines.Count == 0 ? "（変化なし）" : string.Join("\n", lines);
        }

        /// <summary>今の時間の表示に使う残り Tick：ボスが出る前はボスまで、出た後はボスの猶予。</summary>
        public static int TimeLeft(TideRun run) => run == null ? 0 : run.BossSpawned ? run.BossTicksLeft : run.TicksToBoss;

        private string WeaponList(TideRun run)
        {
            _text.Clear();
            for (var w = 0; w < _rules.Weapons.Length; w++)
            {
                var level = run.WeaponLevelOf(w);
                if (level == 0) { continue; }
                if (_text.Length > 0) { _text.Append('\n'); }
                _text.Append(_settings.Weapons[w].DisplayName).Append("  Lv").Append(level);
            }

            return _text.ToString();
        }

        /// <summary>持っている武器のレベルを 1 つの数にまとめる（HUD を書き換えるかの判断用）。</summary>
        private long WeaponsKey(TideRun run)
        {
            if (run == null) { return 0; }
            long key = 0;
            for (var w = 0; w < _rules.Weapons.Length; w++) { key = (key * 17) + run.WeaponLevelOf(w); }
            return key;
        }

        /// <summary>Tick 数を「分:秒」にする（切り捨て）。</summary>
        public static string FormatTicks(long ticks)
        {
            var seconds = ticks / Units.TickRate;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        public static string Headline(AppScreen screen, TideRun run) => screen switch
        {
            AppScreen.Title => "THORN TIDE",
            AppScreen.Paused => "一時停止",
            AppScreen.Result when run?.Status == RunStatus.Won => "茨の番人を倒した！",
            AppScreen.Result when run?.End == RunEnd.TimeUp => "時間切れ…",
            AppScreen.Result => "力尽きた…",
            _ => string.Empty,
        };

        public static string Message(AppScreen screen, TideRun run, TideConfig rules)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            const string controls = "W A S D / 矢印：動く（攻撃は自動）    Esc：一時停止";
            switch (screen)
            {
                case AppScreen.Title:
                    return $"押し寄せる群れの中を歩き回り、{FormatTicks(rules.DurationTicks)} 後に現れるボスを倒せ。\n" +
                        "武器は近くの敵を自動で攻撃する。倒した敵が落とす宝石でレベルを上げ、\n3 択から新しい武器か強化を選ぶ（1 / 2 / 3・クリック・矢印と Enter）。\n" +
                        $"敵に触れると体力が減る（体力 {rules.PlayerHealth}）。ボスが出てから {FormatTicks(rules.BossTimeLimitTicks)} で時間切れ。\n\n" + controls;
                case AppScreen.Paused:
                    return "R：最初から    T：タイトルへ";
                case AppScreen.Result when run != null:
                    return $"生き残った時間  {FormatTicks(run.Elapsed)}    レベル  {run.Level}\n撃破  {run.Kills}    集めた経験値  {run.TotalExperience}\n\nR：最初から    T：タイトルへ";
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
