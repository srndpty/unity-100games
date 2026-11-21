using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Paused,

        /// <summary>面のクリア・ミスの後の短い間。時間が経つと次の面かやり直しへ進む。</summary>
        Interlude,
        Result,
    }

    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1 << 0,
        LevelStarted = 1 << 1,
        Jumped = 1 << 2,
        Stomped = 1 << 3,
        Cleared = 1 << 4,
        Missed = 1 << 5,
        Won = 1 << 6,
        Lost = 1 << 7,
    }

    /// <summary>画面遷移と、プレイヤーの意図を Core へ渡す窓口。実時間を固定 Tick へ変換する。</summary>
    public sealed class PlatformerFlow
    {
        public const int MaxTicksPerFrame = 5;
        public const int MaxInterludeTicks = 10 * Units.TickRate;

        private readonly PlatformerConfig _config;
        private readonly Level[] _levels;
        private readonly HeldControls _held = new();
        private bool _hasFocus = true;
        private int _interludeLeft;
        private double _elapsedTicks;
        private double _accountedTicks;
        private double _timeError;
        private FlowEvents _events;

        /// <param name="interludeTicks">面のクリア・ミスの後、次へ進むまでの Tick 数。</param>
        public PlatformerFlow(PlatformerConfig config, Level[] levels, int interludeTicks)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (levels == null) { throw new ArgumentNullException(nameof(levels)); }
            if (interludeTicks < 1 || interludeTicks > MaxInterludeTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(interludeTicks), interludeTicks, $"1〜{MaxInterludeTicks} の範囲で指定してください。");
            }

            config.Validate();
            _config = config.Copy();
            _levels = (Level[])levels.Clone();
            InterludeTicks = interludeTicks;

            // 面の組を確かめておく（空・null ならここで例外になる）。
            _ = new Campaign(_config, _levels);
        }

        public AppScreen Screen { get; private set; }

        public Campaign? Campaign { get; private set; }

        public int InterludeTicks { get; }

        /// <summary>設定されていれば、キー入力の代わりに自動操縦で動かす（撮影・テスト用）。</summary>
        public Autopilot? Pilot { get; set; }

        /// <summary>起動してからの最高スコア。</summary>
        public int BestScore { get; private set; }

        /// <summary>直前の試合で最高スコアを更新したか。</summary>
        public bool IsNewBest { get; private set; }

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                    if (Screen == AppScreen.Title || Screen == AppScreen.Result) { Begin(); }
                    else if (Screen == AppScreen.Paused) { SwitchTo(AppScreen.Playing); }
                    break;
                case IntentKind.TogglePause:
                    if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
                    else if (Screen == AppScreen.Paused) { SwitchTo(AppScreen.Playing); }
                    break;
                case IntentKind.Restart:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result) { Begin(); }
                    break;
                case IntentKind.BackToTitle:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result)
                    {
                        Campaign = null;
                        SwitchTo(AppScreen.Title);
                    }

                    break;
                case IntentKind.Quit:
                    // アプリの終了は Unity 層が行う。ここでは状態を変えない。
                    break;
                case IntentKind.ControlDown:
                    // 押しっぱなしのキーを一時停止・結果の後へ持ち越さないよう、プレイ中と面の合間に押したものだけを数える。
                    if (Screen == AppScreen.Playing || Screen == AppScreen.Interlude) { _held.Press(intent.Control); }
                    break;
                case IntentKind.ControlUp:
                    _held.Release(intent.Control);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "未定義の操作です。");
            }
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止し、面の合間は時間を止める。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
            _held.Clear();
            ResetTime();
        }

        /// <summary>実時間を進める。処理落ちしても 1 回で進める Tick 数には上限がある。</summary>
        public void Advance(double seconds)
        {
            if (!_hasFocus || (Screen != AppScreen.Playing && Screen != AppScreen.Interlude) || !double.IsFinite(seconds) || seconds <= 0)
            {
                return;
            }

            // 小さい時間の加算でも Tick を取りこぼさないよう、累積の丸め誤差を補償する。
            var elapsed = seconds * Units.TickRate;
            if (!double.IsFinite(elapsed)) { elapsed = MaxTicksPerFrame; }
            var whole = Math.Floor(elapsed);
            var increment = Math.Min(whole, MaxTicksPerFrame) + (elapsed - whole) - _timeError;
            var total = _elapsedTicks + increment;
            _timeError = (total - _elapsedTicks) - increment;
            _elapsedTicks = total;
            var accounted = Math.Floor(total);
            var count = (int)Math.Min(MaxTicksPerFrame, accounted - _accountedTicks);
            _accountedTicks = accounted;
            for (var i = 0; i < count; i++) { StepTick(); }
        }

        /// <summary>1 Tick 進める。プレイ中・面の合間でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen == AppScreen.Interlude)
            {
                if (--_interludeLeft <= 0) { NextLevel(); }
                return;
            }

            if (Screen != AppScreen.Playing) { return; }
            var campaign = Campaign!;
            var input = _held.TakeTickInput();
            if (Pilot != null) { input = Pilot.Next(campaign.Run); }
            campaign.Tick(input);
            Collect();
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private void Begin()
        {
            Campaign = new Campaign(_config, _levels);
            Pilot?.Reset();
            IsNewBest = false;
            _events |= FlowEvents.Started | FlowEvents.LevelStarted;
            SwitchTo(AppScreen.Playing);
        }

        private void NextLevel()
        {
            var campaign = Campaign!;
            if (!campaign.Continue())
            {
                throw new InvalidOperationException($"面の合間なのに次へ進めません（{campaign.Status}）。");
            }

            Pilot?.Reset();
            _events |= FlowEvents.LevelStarted;
            Screen = AppScreen.Playing;
        }

        private void SwitchTo(AppScreen screen)
        {
            Screen = screen;
            _held.Clear();
            ResetTime();
        }

        // 端数の時間を次の画面へ持ち越さない。
        private void ResetTime()
        {
            _elapsedTicks = 0;
            _accountedTicks = 0;
            _timeError = 0;
        }

        private void Collect()
        {
            var campaign = Campaign!;
            var kinds = campaign.TakeEvents().Kinds;
            if ((kinds & RunEventKind.Jumped) != 0) { _events |= FlowEvents.Jumped; }
            if ((kinds & RunEventKind.Stomped) != 0) { _events |= FlowEvents.Stomped; }
            if ((kinds & RunEventKind.Cleared) != 0) { _events |= FlowEvents.Cleared; }
            if ((kinds & RunEventKind.Missed) != 0) { _events |= FlowEvents.Missed; }

            switch (campaign.Status)
            {
                case CampaignStatus.LevelCleared:
                case CampaignStatus.Missed:
                    // 面の合間では押しているキーを保つ（次の面へそのまま持ち越せる）。ジャンプは Core が一度離すまで受け付けない。
                    _interludeLeft = InterludeTicks;
                    Screen = AppScreen.Interlude;
                    break;
                case CampaignStatus.Won:
                case CampaignStatus.Lost:
                    _events |= campaign.Status == CampaignStatus.Won ? FlowEvents.Won : FlowEvents.Lost;
                    IsNewBest = campaign.Score > BestScore;
                    if (IsNewBest) { BestScore = campaign.Score; }
                    SwitchTo(AppScreen.Result);
                    break;
            }
        }
    }
}
