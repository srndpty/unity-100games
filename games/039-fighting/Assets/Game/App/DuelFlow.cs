using System;
using Game.Core;

namespace Game.App
{
    public enum DuelScreen
    {
        Title,
        Select,
        Intro,
        Fighting,
        RoundEnd,
        Paused,
        Result,
    }

    /// <summary>
    /// タイトル、キャラ選択、試合、一時停止、結果。入力は時刻つきの 1 本の列で、
    /// 1 フレームでは時計と入力を合わせて <see cref="MaxTicksPerFrame"/> Tick まで進める。
    /// 再戦のシードは起動時の値から試合ごとに進める。
    /// </summary>
    public sealed class DuelFlow
    {
        public const int MaxTicksPerFrame = 5;

        public const int TickRate = 60;

        private readonly MoveTable _table;
        private readonly DuelConfig _config;
        private readonly int _bootSeed;
        private int _nextSeed;
        private int _left;
        private int _right;
        private int _down;
        private int _up;
        private int _light;
        private int _heavy;
        private int _sweep;
        private bool _hasFocus = true;
        private bool _paused;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private CpuPilot _cpu;

        public DuelFlow(MoveTable table, DuelConfig config, int seed)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            _table = table;
            _config = (config ?? throw new ArgumentNullException(nameof(config))).Copy().Validate();
            _bootSeed = seed == 0 ? 1 : seed;
            _nextSeed = _bootSeed;
            _cpu = new CpuPilot(1, _bootSeed);
        }

        public DuelScreen Screen { get; private set; } = DuelScreen.Title;

        public MoveTable Table => _table;

        public DuelConfig Config => _config.Copy();

        public Match? Match { get; private set; }

        public int PlayerCharacter { get; private set; }

        public int CpuCharacter => 1 - PlayerCharacter;

        public int MatchSeed { get; private set; }

        public int BootSeed => _bootSeed;

        public bool ShowBoxes { get; private set; }

        /// <summary>プレイヤー側に置く操縦役。未設定ならキー入力。</summary>
        public CpuPilot? PlayerPilot { get; set; }

        public bool HasMatch => Match != null;

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (Screen)
            {
                case DuelScreen.Title:
                    if (intent.Kind == IntentKind.Submit) { Screen = DuelScreen.Select; }
                    break;
                case DuelScreen.Select:
                    ApplySelect(intent);
                    break;
                case DuelScreen.Intro:
                case DuelScreen.Fighting:
                case DuelScreen.RoundEnd:
                    ApplyLive(intent);
                    break;
                case DuelScreen.Paused:
                    if (intent.Kind == IntentKind.Submit || intent.Kind == IntentKind.Cancel) { Resume(intent.Time); }
                    else if (intent.Kind == IntentKind.Back) { ToTitle(); }
                    break;
                case DuelScreen.Result:
                    if (intent.Kind == IntentKind.Submit) { StartMatch(intent.Time); }
                    else if (intent.Kind == IntentKind.Cancel || intent.Kind == IntentKind.Back) { ToTitle(); }
                    break;
                default:
                    throw new InvalidOperationException($"未定義の画面です：{Screen}");
            }
        }

        /// <summary>フォーカスを失うとキーを離し、履歴と先行入力を消し、試合中は一時停止する。復帰は手動。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            ReleaseKeys();
            if (Match == null) { return; }
            Match.Duel.ClearCommands();
            if (Screen != DuelScreen.Result && Screen != DuelScreen.Title && Screen != DuelScreen.Select)
            {
                Pause();
            }
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
            if (_paused || Match == null) { return; }
            if (Screen == DuelScreen.Result || Screen == DuelScreen.Title || Screen == DuelScreen.Select) { return; }
            Step();
        }

        private void ApplySelect(Intent intent)
        {
            if (intent.Kind == IntentKind.LeftDown) { PlayerCharacter = 0; }
            else if (intent.Kind == IntentKind.RightDown) { PlayerCharacter = 1; }
            else if (intent.Kind == IntentKind.Submit) { StartMatch(intent.Time); }
            else if (intent.Kind == IntentKind.Cancel || intent.Kind == IntentKind.Back) { Screen = DuelScreen.Title; }
        }

        private void ApplyLive(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.LeftDown:
                    _left |= intent.KeyBit;
                    break;
                case IntentKind.LeftUp:
                    _left &= ~intent.KeyBit;
                    break;
                case IntentKind.RightDown:
                    _right |= intent.KeyBit;
                    break;
                case IntentKind.RightUp:
                    _right &= ~intent.KeyBit;
                    break;
                case IntentKind.DownDown:
                    _down |= intent.KeyBit;
                    break;
                case IntentKind.DownUp:
                    _down &= ~intent.KeyBit;
                    break;
                case IntentKind.UpDown:
                    _up |= intent.KeyBit;
                    break;
                case IntentKind.UpUp:
                    _up &= ~intent.KeyBit;
                    break;
                case IntentKind.LightDown:
                    _light |= intent.KeyBit;
                    break;
                case IntentKind.LightUp:
                    _light &= ~intent.KeyBit;
                    break;
                case IntentKind.HeavyDown:
                    _heavy |= intent.KeyBit;
                    break;
                case IntentKind.HeavyUp:
                    _heavy &= ~intent.KeyBit;
                    break;
                case IntentKind.SweepDown:
                    _sweep |= intent.KeyBit;
                    break;
                case IntentKind.SweepUp:
                    _sweep &= ~intent.KeyBit;
                    break;
                case IntentKind.Cancel:
                    Pause();
                    break;
                case IntentKind.Back:
                    ToTitle();
                    break;
                case IntentKind.Overlay:
                    ShowBoxes = !ShowBoxes;
                    break;
            }
        }

        private Pad PadFromKeys()
        {
            int x = 0;
            if (_left != 0 && _right == 0) { x = -1; }
            else if (_right != 0 && _left == 0) { x = 1; }
            int y = 0;
            if (_down != 0 && _up == 0) { y = -1; }
            else if (_up != 0 && _down == 0) { y = 1; }
            return new Pad(5 + x + (3 * y), _light != 0, _heavy != 0, _sweep != 0);
        }

        private void Step()
        {
            var match = Match;
            if (match == null) { return; }
            var left = Pad.Neutral;
            var right = Pad.Neutral;
            if (match.Phase == MatchPhase.Fight)
            {
                left = PlayerPilot != null ? PlayerPilot.PadFor(match.Duel) : PadFromKeys();
                right = _cpu.PadFor(match.Duel);
            }

            match.Step(left, right);
            Sync();
        }

        private void Sync()
        {
            var match = Match;
            if (match == null || _paused) { return; }
            switch (match.Phase)
            {
                case MatchPhase.Intro:
                    Screen = DuelScreen.Intro;
                    break;
                case MatchPhase.Fight:
                    Screen = DuelScreen.Fighting;
                    break;
                case MatchPhase.RoundEnd:
                    Screen = DuelScreen.RoundEnd;
                    break;
                case MatchPhase.MatchEnd:
                    ReleaseKeys();
                    Screen = DuelScreen.Result;
                    break;
                default:
                    throw new InvalidOperationException($"未定義の局面です：{match.Phase}");
            }
        }

        private void AdvanceTo(double time)
        {
            if (!Live || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * TickRate);
            if (elapsed <= _ticksDone) { return; }
            var target = elapsed > long.MaxValue / 2 ? long.MaxValue / 2 : (long)elapsed;
            if (target - _ticksDone > _frameBudget) { _ticksDone = target - _frameBudget; }
            while (_ticksDone < target && Live)
            {
                _ticksDone++;
                _frameBudget--;
                Step();
            }
        }

        private bool Live =>
            !_paused && Match != null && Screen != DuelScreen.Result && Screen != DuelScreen.Title && Screen != DuelScreen.Select;

        private void StartMatch(double time)
        {
            MatchSeed = _nextSeed;
            _nextSeed++;
            PlayerPilot?.Reset(MatchSeed);
            _cpu = new CpuPilot(1, MatchSeed);
            Match = new Match(_table, _config, PlayerCharacter, CpuCharacter);
            _paused = false;
            ReleaseKeys();
            _origin = time;
            _ticksDone = 0;
            Sync();
        }

        private void Pause()
        {
            ReleaseKeys();
            _paused = true;
            Match?.Duel.ClearCommands();
            Screen = DuelScreen.Paused;
        }

        private void Resume(double time)
        {
            _paused = false;
            _origin = time;
            _ticksDone = 0;
            Sync();
        }

        private void ToTitle()
        {
            _paused = false;
            Match = null;
            ReleaseKeys();
            Screen = DuelScreen.Title;
        }

        private void ReleaseKeys()
        {
            _left = 0;
            _right = 0;
            _down = 0;
            _up = 0;
            _light = 0;
            _heavy = 0;
            _sweep = 0;
        }
    }
}
