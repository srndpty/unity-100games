using System;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Paused,
        Result,
    }

    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Started = 1 << 0,
        DrawStarted = 1 << 1,
        Closed = 1 << 2,
        Miss = 1 << 3,
        Won = 1 << 4,
        Lost = 1 << 5,
    }

    /// <summary>画面遷移と、プレイヤーの意図を Core へ渡す窓口。実時間を固定 Tick へ変換する。</summary>
    public sealed class QixFlow
    {
        public const int MaxTicksPerFrame = 5;

        private readonly QixConfig _config;
        private readonly HeldControls _held = new();
        private int _nextSeed;
        private bool _hasFocus = true;
        private double _elapsedTicks;
        private double _accountedTicks;
        private double _timeError;
        private FlowEvents _events;

        /// <summary>seed は最初の試合のシード。試合ごとに 1 ずつ進め、再戦では必ず新しいシードを使う。</summary>
        public QixFlow(QixConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _nextSeed = seed;
        }

        public AppScreen Screen { get; private set; }

        public QixGame? Game { get; private set; }

        /// <summary>設定されていれば、キー入力の代わりに自動操縦で動かす（撮影・テスト用）。</summary>
        public Autopilot? Pilot { get; set; }

        /// <summary>起動してからの最高スコア。</summary>
        public int BestScore { get; private set; }

        /// <summary>直前の試合で最高スコアを更新したか。</summary>
        public bool IsNewBest { get; private set; }

        /// <summary>今の試合で使っている向きと描画キー（HUD の案内用）。</summary>
        public HeldControls Held => _held;

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
                        Game = null;
                        SwitchTo(AppScreen.Title);
                    }

                    break;
                case IntentKind.Quit:
                    // アプリの終了は Unity 層が行う。ここでは状態を変えない。
                    break;
                case IntentKind.ControlDown:
                    // 押しっぱなしのキーを次の画面へ持ち越さないよう、プレイ中に押したものだけを数える。
                    if (Screen == AppScreen.Playing) { _held.Press(intent.Control); }
                    break;
                case IntentKind.ControlUp:
                    _held.Release(intent.Control);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "未定義の操作です。");
            }
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止する。戻っても自動では再開しない。失っている間は操作を受け付けない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
            ResetInput();
        }

        /// <summary>実時間を進める。処理落ちしても 1 回で進める Tick 数には上限がある。</summary>
        public void Advance(double seconds)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(seconds) || seconds <= 0) { return; }

            // 小さい時間の加算でも Tick を取りこぼさないよう、累積の丸め誤差を補償する。
            var elapsed = seconds * QixConfig.TickRate;
            if (!double.IsFinite(elapsed)) { elapsed = MaxTicksPerFrame; }
            var whole = Math.Floor(elapsed);
            var increment = Math.Min(whole, MaxTicksPerFrame) + (elapsed - whole) - _timeError;
            var total = _elapsedTicks + increment;
            _timeError = (total - _elapsedTicks) - increment;
            _elapsedTicks = total;
            var accounted = Math.Floor(total);
            var count = (int)Math.Min(MaxTicksPerFrame, accounted - _accountedTicks);
            _accountedTicks = accounted;
            for (var i = 0; i < count && Screen == AppScreen.Playing; i++) { StepTick(); }
        }

        /// <summary>1 Tick 進める。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            var game = Game!;
            if (Pilot != null)
            {
                var (direction, draw) = Pilot.Next(game);
                game.SetInput(direction, draw);
            }
            else
            {
                game.SetInput(_held.Direction, _held.Draw);
            }

            game.Tick();
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
            var seed = _nextSeed;
            _nextSeed = unchecked(_nextSeed + 1);
            Game = new QixGame(_config, seed);
            Pilot?.Reset();
            IsNewBest = false;
            _events |= FlowEvents.Started;
            SwitchTo(AppScreen.Playing);
        }

        private void SwitchTo(AppScreen screen)
        {
            Screen = screen;
            ResetInput();
        }

        // 押しっぱなしのキーと端数の時間を次の画面へ持ち越さない。
        private void ResetInput()
        {
            _held.Clear();
            _elapsedTicks = 0;
            _accountedTicks = 0;
            _timeError = 0;
        }

        private void Collect()
        {
            var game = Game!;
            var kinds = game.TakeEvents().Kinds;
            if ((kinds & GameEventKind.DrawStarted) != 0) { _events |= FlowEvents.DrawStarted; }
            if ((kinds & GameEventKind.Closed) != 0) { _events |= FlowEvents.Closed; }
            if ((kinds & GameEventKind.Miss) != 0) { _events |= FlowEvents.Miss; }
            if ((kinds & GameEventKind.Won) != 0) { _events |= FlowEvents.Won; }
            if ((kinds & GameEventKind.Lost) != 0) { _events |= FlowEvents.Lost; }
            if (!game.IsOver) { return; }

            IsNewBest = game.Score > BestScore;
            if (IsNewBest) { BestScore = game.Score; }
            SwitchTo(AppScreen.Result);
        }
    }
}
