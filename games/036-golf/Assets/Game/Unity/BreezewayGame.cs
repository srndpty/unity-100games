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
    /// 入力・カメラ・HUD・音。キーを起きた時刻つきで 1 本の列に入れ、フレームの初めに起きた順で <see cref="GolfFlow"/> へ渡す（012・022）。
    /// 向きのキーは押下と解放の両方を拾う（押し続けると Flow が Tick で繰り返す）。Tab の表示の切り替えは見た目だけなので Flow を通さない。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class BreezewayGame : MonoBehaviour
    {
        public const string Controls =
            "向き        ← →（押し続けると速く回る）\n" +
            "クラブ      ↑ ↓\n" +
            "打点        W A S D\n" +
            "スイング    Space：振り始め → パワー → インパクト\n" +
            "表示        Tab：全体 / グリーン\n" +
            "一時停止    Esc";

        private static readonly string[] _clubNames = { "ウッド", "アイアン", "ウェッジ", "パター" };

        [SerializeField] private BreezewaySettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private BreezewayStage _stage;

        private readonly List<Intent> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private Course _course;
        private bool _hasFocus = true;
        private bool? _viewOverride;
        private VisualElement _root;
        private VisualElement _meter;
        private VisualElement _meterBand;
        private VisualElement _meterFill;
        private VisualElement _meterCursor;
        private VisualElement _meterPower;
        private Label _meterText;
        private int _version = -1;
        private object _shownHud;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _swingSound;
        private AudioClip _hitSound;
        private AudioClip _landSound;
        private AudioClip _treeSound;
        private AudioClip _cupSound;
        private AudioClip _penaltySound;
        private AudioClip _finishSound;
        private string _banner = string.Empty;
        private GolfRound _bannerRound;
        private long _bannerUntil;

        public GolfFlow Flow { get; private set; }

        public BreezewaySettings Settings => _settings;

        public BreezewayStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public Course Course => _course;

        public VisualElement HudRoot => _root;

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出した画面の出来事（撮影台本・テストが場面を待つのに使う）。</summary>
        public FlowEvents LastEvents { get; private set; }

        /// <summary>直前に <see cref="ConsumeEvents"/> で取り出したラウンドの出来事の合計。</summary>
        public RoundEvents LastRoundEvents { get; private set; }

        /// <summary>グリーンを見る表示か（構えた場所がグリーンなら自動、Tab で切り替え）。</summary>
        public bool GreenView => IsGreenView(Flow?.Round);

        /// <summary>今出している知らせ（なければ空）。</summary>
        public string Banner => BannerShown(Flow?.Round) ? _banner : string.Empty;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        private void Awake()
        {
            _course = _settings.Validate();
            var mismatch = _stage.Mismatch(_course, _settings.Rules);
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab がコースのデータと合いません：" + mismatch); }

            // 再戦のシードは起動時の値からラウンドごとに進める（010 の知見）。撮影では固定のシード。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            Flow = new GolfFlow(_settings.Rules, _course, seed);
            _stage.Configure(_settings.Rules, _settings.HeightScale);

            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _swingSound = Tone("Swing", 180, 420, .12f);
            _hitSound = Tone("Hit", 1500, 900, .05f);
            _landSound = Tone("Land", 220, 120, .08f);
            _treeSound = Tone("Tree", 520, 300, .07f);
            _cupSound = Tone("Cup", 880, 1760, .35f);
            _penaltySound = Tone("Penalty", 260, 90, .5f);
            _finishSound = Tone("Finish", 520, 1560, .8f);

            Track(IntentKind.AimLeftDown, IntentKind.AimLeftUp, Key.LeftArrow, "<Keyboard>/leftArrow");
            Track(IntentKind.AimRightDown, IntentKind.AimRightUp, Key.RightArrow, "<Keyboard>/rightArrow");
            Bind(IntentKind.ClubUp, "<Keyboard>/upArrow");
            Bind(IntentKind.ClubDown, "<Keyboard>/downArrow");
            Bind(IntentKind.ImpactUp, "<Keyboard>/w");
            Bind(IntentKind.ImpactDown, "<Keyboard>/s");
            Bind(IntentKind.ImpactLeft, "<Keyboard>/a");
            Bind(IntentKind.ImpactRight, "<Keyboard>/d");
            Bind(IntentKind.Swing, "<Keyboard>/space");
            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.Cancel, "<Keyboard>/escape");
            Bind(IntentKind.Retry, "<Keyboard>/r");
            Bind(IntentKind.Back, "<Keyboard>/backspace");
            var view = NewAction("View", InputActionType.Button, "<Keyboard>/tab");
            view.performed += _ =>
            {
                if (AcceptInput()) { ToggleView(); }
            };
            foreach (var action in _actions) { action.Enable(); }

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        /// <summary>全体の表示とグリーンの表示を切り替える（次に構えるまで）。</summary>
        public void ToggleView()
        {
            var round = Flow.Round;
            if (round == null) { return; }
            _viewOverride = !IsGreenView(round);
        }

        private bool IsGreenView(GolfRound round)
        {
            if (round == null) { return false; }
            return _viewOverride ?? round.LieSurface == Surface.Green;
        }

        /// <summary>1 つのキーの押下・解放を意図にする（PassThrough で、押したまま別のキーを押しても拾う。032 の知見）。</summary>
        private void Track(IntentKind down, IntentKind up, Key key, string path)
        {
            var action = NewAction(down.ToString(), InputActionType.PassThrough, path);
            action.performed += context =>
            {
                if (context.control is not KeyControl control || control.keyCode != key) { return; }
                Queue(new Intent(control.isPressed ? down : up, EventTime(context)));
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

        /// <summary>意図を Flow へ渡す（撮影台本・テストも使う）。</summary>
        public void Apply(Intent intent) => Flow.Apply(intent);

        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }

            // 同じフレームに届いていた入力も捨てる（フォーカス喪失の後に再開しないように）。
            Flow.SetFocus(focus);
            _intents.Clear();
        }

        /// <summary>出来事を音・知らせ・表示の切り替えにする。撮影台本も Tick を進めた後に呼ぶ。</summary>
        public void ConsumeEvents()
        {
            LastEvents = Flow.TakeEvents();
            var events = Flow.TakeRoundEvents();
            LastRoundEvents = events;
            var round = Flow.Round;
            if ((LastEvents & (FlowEvents.Started | FlowEvents.Title)) != 0 || (events & (RoundEvents.Ready | RoundEvents.HoleStarted)) != 0) { _viewOverride = null; }
            if ((LastEvents & (FlowEvents.Started | FlowEvents.Title)) != 0) { _banner = string.Empty; }
            if (round == null || events == RoundEvents.None) { return; }

            if ((events & RoundEvents.SwingStarted) != 0) { _audio.PlayOneShot(_swingSound); }
            if ((events & RoundEvents.Launched) != 0) { _audio.PlayOneShot(_hitSound); }
            if ((events & RoundEvents.Landed) != 0) { _audio.PlayOneShot(_landSound); }
            if ((events & RoundEvents.TreeHit) != 0) { _audio.PlayOneShot(_treeSound); }
            if ((events & RoundEvents.Holed) != 0) { _audio.PlayOneShot(_cupSound); }
            if ((events & RoundEvents.Penalty) != 0) { _audio.PlayOneShot(_penaltySound); }
            if ((events & RoundEvents.Finished) != 0) { _audio.PlayOneShot(_finishSound); }

            var text = BannerFor(events, round);
            if (text != null) { Announce(round, text); }
        }

        /// <summary>出来事の知らせ（大事なものを優先）。なければ null。</summary>
        public static string BannerFor(RoundEvents events, GolfRound round)
        {
            if (round == null) { throw new ArgumentNullException(nameof(round)); }
            if ((events & RoundEvents.Holed) != 0) { return "カップイン！ " + ScoreName(round.Strokes, round.Hole.Par); }
            if ((events & RoundEvents.GaveUp) != 0) { return "打数の上限"; }
            if ((events & RoundEvents.Penalty) != 0) { return round.LastOutcome == ShotOutcome.Water ? "池  1 打罰" : "OB  1 打罰"; }
            if ((events & RoundEvents.TreeHit) != 0) { return "木に当たった"; }
            if ((events & RoundEvents.Mishit) != 0) { return "ミスショット"; }
            if ((events & RoundEvents.LipOut) != 0) { return "カップの上を通過"; }
            return null;
        }

        private void Announce(GolfRound round, string text)
        {
            _banner = text;
            _bannerRound = round;
            _bannerUntil = round.Ticks + Mathf.RoundToInt(_settings.BannerSeconds * GolfFlow.TickRate);
        }

        private bool BannerShown(GolfRound round) => round != null && ReferenceEquals(round, _bannerRound) && round.Ticks < _bannerUntil && _banner.Length > 0;

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            var round = Flow.Round;
            var hole = round?.Hole ?? _course.Holes[0];
            var green = IsGreenView(round);
            var (center, size) = BreezewayStage.Frame(hole, green, round?.Lie ?? hole.Tee, _camera.aspect);
            _camera.transform.position = center;
            _camera.orthographicSize = size;
            _stage.Render(round, _course, 0, size);
            var view = _stage.Hole(round?.HoleIndex ?? 0);
            if (view.Slopes.activeSelf != green) { view.Slopes.SetActive(green); }
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
            _meter = root.Q("meter");
            _meterBand = root.Q("meter-band");
            _meterFill = root.Q("meter-fill");
            _meterCursor = root.Q("meter-cursor");
            _meterPower = root.Q("meter-power");
            _meterText = root.Q<Label>("meter-text");
            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null) { return; }
            var round = Flow.Round;
            var screen = Flow.Screen;
            RenderMeter(round, screen);
            var banner = Banner;
            var state = (screen, round, round?.HoleIndex ?? -1, round?.Strokes ?? 0, round?.Phase ?? RoundPhase.Aiming, round?.Club ?? 0, round?.Lie ?? default,
                round?.ImpactX ?? 0, round?.ImpactY ?? 0, round?.ShotCount ?? 0, banner, Flow.BestTotal, Flow.IsNewBest);
            if (Equals(_shownHud, state)) { return; }
            _shownHud = state;

            var hole = round?.Hole ?? _course.Holes[0];
            _root.Q<Label>("hole").text = $"{hole.Number}番  PAR {hole.Par}";
            _root.Q<Label>("strokes").text = round == null ? "打数 —" : $"打数 {round.Strokes}";
            _root.Q<Label>("total").text = round == null ? "合計 —" : $"合計 {round.Total}（{ToPar(round.Total - round.ParPlayed)}）";

            var wind = round?.Wind ?? default;
            var speed = Mathf.Sqrt((float)wind.LengthSquared) * Units.TickRate / Units.PerMeter;
            var arrow = _root.Q<Label>("wind-arrow");
            arrow.style.visibility = speed > 0 ? Visibility.Visible : Visibility.Hidden;
            arrow.style.rotate = new Rotate(-Mathf.Atan2(wind.Y, wind.X) * Mathf.Rad2Deg);
            _root.Q<Label>("wind-speed").text = round == null ? "—" : speed > 0 ? speed.ToString("0.0", CultureInfo.InvariantCulture) + " m/s" : "無風";

            var shot = _root.Q("shot-panel");
            shot.style.display = round != null && screen != AppScreen.Title ? DisplayStyle.Flex : DisplayStyle.None;
            if (round != null)
            {
                var carry = round.CarryOf(round.Club) / (float)Units.PerMeter;
                _root.Q<Label>("club").text = round.Club == Clubs.Putter ? _clubNames[round.Club] : $"{_clubNames[round.Club]}  最大 {carry:0} m";
                _root.Q<Label>("distance").text = $"カップまで {round.DistanceToCup / (float)Units.PerMeter:0.0} m";
                _root.Q<Label>("lie").text = "ライ：" + SurfaceName(round.LieSurface);
                var dot = _root.Q("impact-dot");
                dot.style.left = 39 + (round.ImpactX * 18);
                dot.style.top = 39 - (round.ImpactY * 18);
                _root.Q<Label>("diff").text = LastShotText(round);
            }

            var bannerLabel = _root.Q<Label>("banner");
            bannerLabel.text = banner;
            bannerLabel.style.display = banner.Length > 0 && screen == AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;

            var panel = _root.Q("panel");
            var holeOut = screen == AppScreen.Playing && round != null && round.Phase == RoundPhase.HoleOut;
            panel.style.display = screen == AppScreen.Playing && !holeOut ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<Label>("headline").text = holeOut ? $"{hole.Number}番  {ScoreName(round.ScoreOf(round.HoleIndex), hole.Par)}" : Headline(screen);
            _root.Q<Label>("message").text = holeOut ? Scorecard(round) : Message(Flow);
        }

        /// <summary>メーター：0 → 100% → 0 を左右に動く。左端の帯がインパクト帯（理想の Tick の前後）。</summary>
        private void RenderMeter(GolfRound round, AppScreen screen)
        {
            var meterBox = _meter;
            var visible = screen == AppScreen.Playing && round != null && (round.Phase == RoundPhase.Aiming || round.Phase == RoundPhase.Swinging);
            meterBox.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) { return; }
            var config = _settings.Rules;
            var half = (float)config.MeterHalfTicks;
            var window = config.ImpactWindow / half;
            float U(float position) => Mathf.Clamp01((position + window) / (1 + window)) * 100f;
            var meter = round.MeterState;
            var t = meter?.T ?? 0;
            var position = t <= half ? t / half : ((2 * half) - t) / half;
            _meterBand.style.left = Length.Percent(0);
            _meterBand.style.width = Length.Percent(U(window));
            _meterFill.style.width = Length.Percent(meter == null ? 0 : U(position));
            _meterCursor.style.left = Length.Percent(U(position));
            var power = _meterPower;
            var powerSet = meter is { PowerSet: true };
            power.style.display = powerSet ? DisplayStyle.Flex : DisplayStyle.None;
            if (powerSet) { power.style.left = Length.Percent(U(meter.Value.Power / 1000f)); }
            var text = meter == null ? "Space：振り始める"
                : powerSet ? $"パワー {meter.Value.Power / 10}%    Space：青い帯でインパクト"
                : $"Space：パワーを決める（{meter.Value.Gauge / 10}%）";
            if (_meterText.text != text) { _meterText.text = text; }
        }

        private static string LastShotText(GolfRound round)
        {
            if (round.LastShot is not { } shot) { return "前の打球：—"; }
            var setup = shot.Setup;
            var error = setup.Mishit ? "ミス" : setup.Error == 0 ? "ずれ 0" : $"ずれ {setup.Error:+0;-0} Tick";
            return $"前の打球：予測との差 {shot.Difference / (float)Units.PerMeter:0.00} m（{error}）";
        }

        public static string SurfaceName(Surface surface) => surface switch
        {
            Surface.Rough => "ラフ",
            Surface.Fairway => "フェアウェイ",
            Surface.Tee => "ティー",
            Surface.Green => "グリーン",
            Surface.Bunker => "バンカー",
            Surface.Water => "池",
            _ => "OB",
        };

        /// <summary>打数の呼び名（ホールインワン・バーディ・パー・ボギーなど）。</summary>
        public static string ScoreName(int strokes, int par)
        {
            if (strokes == 1) { return "ホールインワン"; }
            return (strokes - par) switch
            {
                <= -3 => "アルバトロス",
                -2 => "イーグル",
                -1 => "バーディ",
                0 => "パー",
                1 => "ボギー",
                2 => "ダブルボギー",
                var over => $"+{over}",
            };
        }

        public static string ToPar(int difference) => difference == 0 ? "±0" : difference.ToString("+0;−0", CultureInfo.InvariantCulture);

        /// <summary>スコアカード（終えたホールの打数と呼び名、合計とパー差）。</summary>
        public static string Scorecard(GolfRound round)
        {
            if (round == null) { throw new ArgumentNullException(nameof(round)); }
            var text = new StringBuilder();
            for (var i = 0; i < round.Course.Holes.Count; i++)
            {
                var hole = round.Course.Holes[i];
                var score = round.ScoreOf(i);
                text.Append(hole.Number).Append("番（PAR ").Append(hole.Par).Append("）　");
                text.Append(score == 0 ? "—" : score.ToString(CultureInfo.InvariantCulture) + " 打　" + ScoreName(score, hole.Par));
                text.Append('\n');
            }

            text.Append("\n合計 ").Append(round.Total).Append("（").Append(ToPar(round.Total - round.ParPlayed)).Append("）");
            return text.ToString();
        }

        public static string Headline(AppScreen screen) => screen switch
        {
            AppScreen.Title => "BREEZEWAY LINKS",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => "ラウンド終了",
            _ => string.Empty,
        };

        public static string Message(GolfFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var config = flow.Config;
            switch (flow.Screen)
            {
                case AppScreen.Title:
                    return "3 ホール（PAR 3・4・5）を少ない打数で回ろう。\n" +
                        "← → で向き、↑ ↓ でクラブ、W A S D で打点を決めて、\n" +
                        "Space で振り始め → もう一度でパワー → 青い帯でインパクト。\n" +
                        $"白い点は予測線（ずれ 0）。風とグリーンの傾き（矢印）に注意。池・OB は 1 打罰、上限は PAR + {config.MaxOver}。\n\n" +
                        "Space / Enter：はじめる";
                case AppScreen.Paused:
                    return "Esc / Enter：つづける    R：やり直す    Backspace：タイトルへ";
                case AppScreen.Result:
                    var round = flow.Round;
                    var best = flow.IsNewBest ? "\n<color=#FFE07A>最少記録！</color>" : string.Empty;
                    return (round == null ? string.Empty : Scorecard(round)) + best + "\n\nSpace / Enter：もう一度    Esc：タイトルへ";
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
