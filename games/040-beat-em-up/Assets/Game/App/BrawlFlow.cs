using System;
using Game.Core;

namespace Game.App
{
    public enum BrawlScreen
    {
        Title,
        Howto,
        Playing,
        Paused,
        Result,
    }

    /// <summary>
    /// タイトル、操作説明、試合、一時停止、結果。入力は時刻つきの 1 本の列で、
    /// 1 フレームでは時計と入力を合わせて <see cref="MaxTicksPerFrame"/> Tick まで進める。
    /// 再戦のシードは起動時の値から試合ごとに進める（1 試合 = 1 シード）。
    /// </summary>
    public sealed class BrawlFlow
    {
        public const int MaxTicksPerFrame = 5;

        private readonly MoveTable _table;
        private readonly StageData _stage;
        private readonly BrawlConfig _config;
        private int _nextSeed;
        private int _left;
        private int _right;
        private int _up;
        private int _down;
        private int _attack;
        private int _jump;
        private int _special;
        private bool _hasFocus = true;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;

        public BrawlFlow(MoveTable table, StageData stage, BrawlConfig config, int seed)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            _stage = stage ?? throw new ArgumentNullException(nameof(stage));
            _config = (config ?? throw new ArgumentNullException(nameof(config))).Copy().Validate();
            _stage.Check(_table, _config);
            BootSeed = seed == 0 ? 1 : seed;
            _nextSeed = BootSeed;
        }

        public BrawlScreen Screen { get; private set; } = BrawlScreen.Title;

        public MoveTable Table => _table;

        public StageData Stage => _stage;

        public BrawlConfig Config => _config.Copy();

        public Brawl? Brawl { get; private set; }

        public int BootSeed { get; }

        public int MatchSeed { get; private set; }

        public bool ShowBoxes { get; private set; }

        /// <summary>プレイヤー側の操縦役。未設定ならキー入力。</summary>
        public Autopilot? Pilot { get; set; }

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (Screen)
            {
                case BrawlScreen.Title:
                    if (intent.Kind == IntentKind.Submit) { Screen = BrawlScreen.Howto; }
                    break;
                case BrawlScreen.Howto:
                    if (intent.Kind == IntentKind.Submit) { StartMatch(intent.Time); }
                    else if (intent.Kind == IntentKind.Cancel || intent.Kind == IntentKind.Back) { Screen = BrawlScreen.Title; }
                    break;
                case BrawlScreen.Playing:
                    ApplyLive(intent);
                    break;
                case BrawlScreen.Paused:
                    if (intent.Kind == IntentKind.Submit || intent.Kind == IntentKind.Cancel) { Resume(intent.Time); }
                    else if (intent.Kind == IntentKind.Back) { ToTitle(); }
                    break;
                case BrawlScreen.Result:
                    if (intent.Kind == IntentKind.Submit) { StartMatch(intent.Time); }
                    else if (intent.Kind == IntentKind.Cancel || intent.Kind == IntentKind.Back) { ToTitle(); }
                    break;
                default:
                    throw new InvalidOperationException($"未定義の画面です：{Screen}");
            }
        }

        /// <summary>フォーカスを失うとキーと先行入力を捨て、試合中は一時停止する。復帰は手動。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            ReleaseKeys();
            Brawl?.ClearInput();
            if (Screen == BrawlScreen.Playing) { Pause(); }
        }

        public void Advance(double now)
        {
            if (_hasFocus) { AdvanceTo(now); }
            EndFrame();
        }

        public void EndFrame() => _frameBudget = MaxTicksPerFrame;

        /// <summary>時刻と無関係に 1 Tick 進める（撮影・テスト用）。</summary>
        public void StepTick()
        {
            if (Screen != BrawlScreen.Playing) { return; }
            Step();
        }

        /// <summary>キーの状態から作る今の入力。</summary>
        public Pad KeyPad()
        {
            int x = (_left != 0 ? -1 : 0) + (_right != 0 ? 1 : 0);
            int z = (_down != 0 ? -1 : 0) + (_up != 0 ? 1 : 0);
            return new Pad(x, z, _attack != 0, _jump != 0, _special != 0);
        }

        private void ApplyLive(Intent intent)
        {
            int bit = intent.KeyBit;
            switch (intent.Kind)
            {
                case IntentKind.LeftDown: _left |= bit; break;
                case IntentKind.LeftUp: _left &= ~bit; break;
                case IntentKind.RightDown: _right |= bit; break;
                case IntentKind.RightUp: _right &= ~bit; break;
                case IntentKind.UpDown: _up |= bit; break;
                case IntentKind.UpUp: _up &= ~bit; break;
                case IntentKind.DownDown: _down |= bit; break;
                case IntentKind.DownUp: _down &= ~bit; break;
                case IntentKind.AttackDown: _attack |= bit; break;
                case IntentKind.AttackUp: _attack &= ~bit; break;
                case IntentKind.JumpDown: _jump |= bit; break;
                case IntentKind.JumpUp: _jump &= ~bit; break;
                case IntentKind.SpecialDown: _special |= bit; break;
                case IntentKind.SpecialUp: _special &= ~bit; break;
                case IntentKind.Cancel: Pause(); break;
                case IntentKind.Back: ToTitle(); break;
                case IntentKind.Overlay: ShowBoxes = !ShowBoxes; break;
            }
        }

        private void Step()
        {
            var brawl = Brawl;
            if (brawl == null) { return; }
            var pad = Pilot != null ? Pilot.PadFor(brawl) : KeyPad();
            brawl.Step(pad);
            if (brawl.Outcome != Outcome.Running)
            {
                ReleaseKeys();
                Screen = BrawlScreen.Result;
            }
        }

        private void AdvanceTo(double time)
        {
            if (Screen != BrawlScreen.Playing || Brawl == null || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * _config.TickRate);
            if (elapsed <= _ticksDone) { return; }
            var target = elapsed > long.MaxValue / 2 ? long.MaxValue / 2 : (long)elapsed;
            if (target - _ticksDone > _frameBudget) { _ticksDone = target - _frameBudget; }
            while (_ticksDone < target && Screen == BrawlScreen.Playing)
            {
                _ticksDone++;
                _frameBudget--;
                Step();
            }
        }

        private void StartMatch(double time)
        {
            MatchSeed = _nextSeed;
            _nextSeed = _nextSeed == int.MaxValue ? 1 : _nextSeed + 1;
            Pilot?.Reset(MatchSeed);
            Brawl = new Brawl(_table, _config, _stage, MatchSeed);
            ReleaseKeys();
            _origin = time;
            _ticksDone = 0;
            Screen = BrawlScreen.Playing;
        }

        private void Pause()
        {
            ReleaseKeys();
            Brawl?.ClearInput();
            Screen = BrawlScreen.Paused;
        }

        private void Resume(double time)
        {
            _origin = time;
            _ticksDone = 0;
            Screen = BrawlScreen.Playing;
        }

        private void ToTitle()
        {
            Brawl = null;
            ReleaseKeys();
            Screen = BrawlScreen.Title;
        }

        private void ReleaseKeys()
        {
            _left = 0;
            _right = 0;
            _up = 0;
            _down = 0;
            _attack = 0;
            _jump = 0;
            _special = 0;
        }
    }
}
