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
    [RequireComponent(typeof(AudioSource))]
    public sealed class BaselineBloomGame : MonoBehaviour
    {
        /// <summary>画面の上に空ける HUD の帯（ワールド単位）。</summary>
        public const float HudBand = 1.6f;

        /// <summary>カメラに収める範囲：ベースラインの後ろ（m）。</summary>
        public const float ViewBackMeters = 5f;

        /// <summary>カメラに収める範囲：ダブルスのサイドラインの外（m）。</summary>
        public const float ViewSideMeters = 6.5f;

        /// <summary>カメラに収める範囲：奥のベースラインの上の高さ（m、ロブの頂点の余白）。</summary>
        public const float ViewHeightMeters = 3.5f;

        [SerializeField] private BaselineBloomSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private BaselineBloomStage _stage;

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
        private Font _font;
        private AudioSource _audio;
        private string _hint = string.Empty;
        private long _hintUntil;

        public TennisFlow Flow { get; private set; }

        public BaselineBloomSettings Settings => _settings;

        public Camera ViewCamera => _camera;

        public BaselineBloomStage Stage => _stage;

        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>今出している打球の案内（早い・ちょうど・遅い）。なければ空。</summary>
        public string TimingHint => Flow?.Match != null && Flow.Match.Tick < _hintUntil ? _hint : string.Empty;

        private void Awake()
        {
            _settings.Validate();
            var mismatch = _stage.Mismatch();
            if (mismatch != null) { throw new InvalidOperationException("Stage Prefab が投影と一致しません：" + mismatch); }

            // 撮影中は決まったシード。ふだんは起動時の値から試合ごとに 1 ずつ進める。
            Flow = new TennisFlow(_settings.Rules, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事が起きたら、この順で最初の 1 つだけ鳴らす。
            _sounds.Add((FlowEvents.MatchWon, Tone("Match", 660, 1320, .8f)));
            _sounds.Add((FlowEvents.PointWon, Tone("Point", 520, 780, .18f)));
            _sounds.Add((FlowEvents.Fault, Tone("Fault", 330, 220, .2f)));
            _sounds.Add((FlowEvents.NetCord, Tone("Net", 180, 120, .12f)));
            _sounds.Add((FlowEvents.Serve, Tone("Serve", 900, 500, .06f)));
            _sounds.Add((FlowEvents.Stroke, Tone("Stroke", 800, 450, .05f)));
            _sounds.Add((FlowEvents.Lob, Tone("Lob", 500, 700, .07f)));
            _sounds.Add((FlowEvents.Bounce, Tone("Bounce", 260, 200, .03f)));
            _sounds.Add((FlowEvents.Whiff, Tone("Whiff", 1400, 1100, .03f)));
            _sounds.Add((FlowEvents.Toss, Tone("Toss", 400, 600, .04f)));
            _sounds.Add((FlowEvents.Started, Tone("Start", 520, 780, .1f)));

            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.TogglePause, "<Keyboard>/escape", "<Keyboard>/p");
            Bind(IntentKind.Restart, "<Keyboard>/r");
            Bind(IntentKind.BackToTitle, "<Keyboard>/t");
            Hold(Control.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            Hold(Control.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Hold(Control.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            Hold(Control.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            Hold(Control.Stroke, "<Keyboard>/z", "<Keyboard>/j");
            Hold(Control.Lob, "<Keyboard>/x", "<Keyboard>/k");

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
                _hint = string.Empty;
                _hintUntil = 0;
            }

            var match = Flow.Match;
            if (match != null && (events & (FlowEvents.Stroke | FlowEvents.Lob | FlowEvents.Serve)) != 0 && match.Rally.LastHitter == Side.Near)
            {
                _hint = TimingText(match.LastTiming);
                _hintUntil = match.Tick + _settings.TimingHintTicks;
            }

            foreach (var pair in _sounds)
            {
                if ((events & pair.Event) == 0) { continue; }
                _audio.PlayOneShot(pair.Clip);
                break;
            }
        }

        /// <summary>早打ち・遅打ちの案内。ずれが 300‰ 未満なら「ちょうど」、600‰ 未満なら「少し」。</summary>
        public static string TimingText(int timing)
        {
            var size = Math.Abs(timing);
            if (size < 300) { return "ちょうど！"; }
            var degree = size < 600 ? "少し" : string.Empty;
            return timing > 0 ? $"{degree}早い  →  左へ・長く" : $"{degree}遅い  →  右へ・低く";
        }

        /// <summary>今の状態を描く。補間せず、その Tick の状態をそのまま描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            var match = Flow.Match;
            if (match == null) { _stage.RenderInitial(Flow.Config); }
            else { _stage.Render(match); }
            Frame();
            RenderHud();
        }

        // カメラはコートとその周り（ベースラインの後ろ・サイドの外・奥のロブの高さ）が収まる大きさにして、上に HUD の帯を空ける。
        private void Frame()
        {
            var y = CourtProjection.Meters(Court.HalfLength) + ViewBackMeters;
            var bottom = CourtProjection.ToWorld(0, -y, 0).y;
            var top = CourtProjection.ToWorld(0, y, ViewHeightMeters).y + HudBand;
            var width = CourtProjection.ToWorld(ViewSideMeters, -y, 0).x * 2;
            var height = top - bottom;
            _camera.orthographicSize = Mathf.Max(height / 2, width / 2 / Mathf.Max(.01f, _camera.aspect));
            _camera.transform.position = new Vector3(0, (top + bottom) / 2, _camera.transform.position.z);
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

        // 文字とボタンは表示が変わるときだけ書き換える。
        private void RenderHud()
        {
            if (_root == null) { return; }
            var m = Flow.Match;
            var screen = Flow.Screen;
            var verdict = VerdictLine(m);
            var guide = Guide(m, screen);
            var state = (screen, m?.Score.Points(Side.Near) ?? 0, m?.Score.Points(Side.Far) ?? 0, m?.Score.Games(Side.Near) ?? 0, m?.Score.Games(Side.Far) ?? 0,
                m?.Rally.Server ?? Side.Near, m?.Score.InTiebreak ?? false, verdict, guide, TimingHint);
            if (Equals(_shownHud, state)) { return; }
            _shownHud = state;

            _root.Q("scoreboard").style.display = m == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (m != null)
            {
                var score = m.Score;
                var showGames = score.TiebreakAtGames > 0;
                _root.Q<Label>("format").text = FormatName(Flow.Config) + (showGames && score.InTiebreak ? "  ・  TIEBREAK" : string.Empty);
                SetRow("near", score, Side.Near, showGames, m.Rally.Server == Side.Near);
                SetRow("far", score, Side.Far, showGames, m.Rally.Server == Side.Far);
            }

            var verdictLabel = _root.Q<Label>("verdict");
            verdictLabel.text = verdict;
            verdictLabel.style.display = verdict.Length > 0 && screen == AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;

            var guideLabel = _root.Q<Label>("guide");
            guideLabel.text = guide;
            guideLabel.style.display = guide.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            _root.Q("panel").style.display = screen != AppScreen.Playing ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<Label>("headline").text = Headline(screen, m);
            _root.Q<Label>("message").text = Message(screen, m, Flow.Config, Flow.PlayedTicks);
            Show("submit", screen != AppScreen.Playing);
            _root.Q<Button>("submit").text = screen switch
            {
                AppScreen.Title => "はじめる  /  Enter",
                AppScreen.Paused => "つづける  /  Enter",
                _ => "もう一度  /  Enter",
            };
            Show("pause", screen == AppScreen.Playing);
            Show("restart", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("title", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("quit", screen == AppScreen.Title);
        }

        private void SetRow(string prefix, MatchScore score, Side side, bool showGames, bool serving)
        {
            _root.Q<Label>(prefix + "-serve").text = serving ? "●" : " ";
            var games = _root.Q<Label>(prefix + "-games");
            games.text = score.Games(side).ToString(System.Globalization.CultureInfo.InvariantCulture);
            games.style.display = showGames ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<Label>(prefix + "-points").text = ScoreBoard.PointText(score, side);
        }

        /// <summary>判定の表示。ポイントの間は判定と得点した側、2 本目のサーブの前はフォルト。</summary>
        public static string VerdictLine(TennisMatch match)
        {
            if (match == null || match.IsFinished) { return string.Empty; }
            if (match.Phase == MatchPhase.Gap)
            {
                var who = match.LastPointWinner == Side.Near ? "YOU" : "CPU";
                return $"{ScoreBoard.VerdictText(match.LastVerdict)}  —  {who} のポイント";
            }

            var rally = match.Rally;
            var secondServe = rally.ServeNumber == 2 && (rally.Phase == RallyPhase.Ready || rally.Phase == RallyPhase.Toss);
            return secondServe ? "FAULT  —  セカンドサーブ" : string.Empty;
        }

        /// <summary>画面下の案内：プレイヤーのサーブの手順と、打球の時機。</summary>
        private string Guide(TennisMatch m, AppScreen screen)
        {
            if (m == null || screen != AppScreen.Playing) { return string.Empty; }
            var hint = TimingHint;
            if (hint.Length > 0) { return hint; }
            if (m.Phase == MatchPhase.Point && m.Rally.Server == Side.Near)
            {
                if (m.Rally.Phase == RallyPhase.Ready) { return "Z でトスを上げる"; }
                if (m.Rally.Phase == RallyPhase.Toss && !m.Rally.TossSwung) { return "球がいちばん高いところで、もう一度 Z（← → で狙い）"; }
            }

            return string.Empty;
        }

        public static string FormatName(TennisConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            return config.TiebreakAtGames == 0
                ? $"{config.TiebreakPoints} ポイント先取"
                : $"1 セット（{config.TiebreakAtGames} ゲーム先取）";
        }

        public static string Headline(AppScreen screen, TennisMatch match) => screen switch
        {
            AppScreen.Title => "BASELINE BLOOM",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => match?.Winner == Side.Near ? "YOU WIN!" : "CPU WINS",
            _ => string.Empty,
        };

        public static string Message(AppScreen screen, TennisMatch match, TennisConfig config, long playedTicks)
        {
            const string controls = "← → ↑ ↓：動く    Z：ストローク / サーブ    X：ロブ\n振った瞬間の ← → で左右に狙う    Esc：一時停止";
            switch (screen)
            {
                case AppScreen.Title:
                    var rule = config.TiebreakAtGames == 0
                        ? $"{config.TiebreakPoints} ポイントを先に取ったほうが勝ち（2 点差が必要）。"
                        : $"{config.TiebreakAtGames} ゲームを先に取ったほうが勝ち（2 ゲーム差、{config.TiebreakAtGames}-{config.TiebreakAtGames} でタイブレーク）。";
                    return $"CPU とシングルスで 1 セット。{rule}\n球が体の少し前に来たときに振るのが「ちょうど」。\n早く振ると左へ長く、遅く振ると右へ低く飛ぶ。\n\n{controls}";
                case AppScreen.Paused:
                    return controls + "\n\nR：最初から    T：タイトルへ";
                case AppScreen.Result:
                    if (match == null) { return string.Empty; }
                    var score = match.Score;
                    var games = config.TiebreakAtGames == 0 ? string.Empty : $"ゲーム  {score.Games(Side.Near)} - {score.Games(Side.Far)}\n";
                    var seconds = playedTicks / Units.TickRate;
                    return $"{games}ポイント数  {match.PointsPlayed}\nいちばん長いラリー  {match.LongestRally} 本\n試合時間  {seconds / 60}:{seconds % 60:00}\n\nR：最初から    T：タイトルへ";
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
