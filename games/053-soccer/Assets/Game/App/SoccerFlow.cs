using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core;

namespace Game.App
{
    public enum FlowScreen
    {
        Title,
        HowTo,
        Playing,
        Paused,
        Result,
    }

    /// <summary>メニューの 1 項目。</summary>
    public sealed class MenuItem
    {
        public MenuItem(string label, bool enabled = true)
        {
            Label = label;
            Enabled = enabled;
        }

        public string Label { get; }
        public bool Enabled { get; }
    }

    /// <summary>
    /// 画面の流れ（タイトル → 試合 → 結果 → タイトル）。試合（<see cref="Match"/>）を動かすのはここだけで、Unity 層には表示用の値だけを見せる。
    /// 入力は 1 本の列で順に当てる（012）。時間は 60 Tick/秒で数え、実時間は <see cref="UpdateFrame"/> で Tick に直す（1 フレームの上限あり）。
    /// 青は人間（<see cref="HumanControl"/>）と味方の CPU（<see cref="TeamAi"/>）、赤は CPU。相手の方針はタイトルで選ぶ。
    /// </summary>
    public sealed class SoccerFlow
    {
        public const int TitleStart = 0;
        public const int TitleTactics = 1;
        public const int TitleHowTo = 2;
        public const int TitleQuit = 3;
        public const int PauseResume = 0;
        public const int PauseTitle = 1;

        /// <summary>遊んだ時間の上限（99:59:59）。</summary>
        public const double MaxPlaySeconds = 359999;

        /// <summary>選べる相手の方針（タイトルで順に切り替える）。</summary>
        public static readonly ReadOnlyCollection<Tactics> OpponentTactics = Array.AsReadOnly(new[]
        {
            new Tactics(DefenseStyle.PressHigh, AttackStyle.Possession),
            new Tactics(DefenseStyle.PressHigh, AttackStyle.Direct),
            new Tactics(DefenseStyle.SitBack, AttackStyle.Possession),
            new Tactics(DefenseStyle.SitBack, AttackStyle.Direct),
        });

        private readonly SoccerConfig _config;
        private readonly FlowSettings _settings;
        private readonly Queue<Intent> _intents = new();
        private readonly ReadOnlyCollection<MenuItem> _pauseItems;
        private readonly PlayerCommand[] _commands = new PlayerCommand[Match.PlayerCount];
        private Match? _match;
        private TeamAi? _blueAi;
        private TeamAi? _redAi;
        private HumanControl _human;
        private MatchStats _stats = new();
        private int _nextSeed;
        private double _accumulator;
        private int _endTicks;

        public SoccerFlow(SoccerConfig config, int firstSeed, FlowSettings? settings = null)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var errors = config.Validate();
            if (errors.Count > 0) { throw new ArgumentException(string.Join("\n", errors), nameof(config)); }
            _config = config.Copy();
            _settings = settings?.Copy() ?? new FlowSettings();
            var flowErrors = _settings.Validate();
            if (flowErrors.Count > 0) { throw new ArgumentException(string.Join("\n", flowErrors), nameof(settings)); }
            _nextSeed = firstSeed;
            _human = new HumanControl(_settings.AutoSwitchOnDefense);
            _pauseItems = Array.AsReadOnly(new[] { new MenuItem("つづける"), new MenuItem("タイトルへ") });
        }

        public FlowScreen Screen { get; private set; } = FlowScreen.Title;

        /// <summary>画面の版。画面が変わるたびに増える。クリックはこの版を持って送る。</summary>
        public int Version { get; private set; }

        public AudioCues Cues { get; } = new();

        public bool QuitRequested { get; private set; }

        public bool HasFocus { get; private set; } = true;

        public int MenuCursor { get; private set; }

        /// <summary>選んでいる相手の方針の番号。</summary>
        public int OpponentChoice { get; private set; }

        public Tactics Opponent => OpponentTactics[OpponentChoice];

        /// <summary>人間の代わりに青を全て CPU が動かす（撮影・テスト）。操作中の印は自動の切替に従う。</summary>
        public bool Autopilot { get; set; }

        /// <summary>遊んだ時間（試合中でフォーカスがある間だけ数える。Tick の上限とは別、017）。</summary>
        public double PlaySeconds { get; private set; }

        public IReadOnlyList<MenuItem> TitleItems => Array.AsReadOnly(new[]
        {
            new MenuItem("試合開始"),
            new MenuItem("相手：" + Opponent.Name),
            new MenuItem("操作説明"),
            new MenuItem("終了"),
        });

        public IReadOnlyList<MenuItem> PauseItems => _pauseItems;

        public IReadOnlyList<MenuItem> MenuItems => Screen == FlowScreen.Paused ? _pauseItems : TitleItems;

        /// <summary>今の試合（試合の外は null）。表示はここから読む（書き換えないこと）。</summary>
        public Match? Match => _match;

        public MatchStats Stats => _stats;

        /// <summary>人間が操作している選手。</summary>
        public int Controlled => _human.Controlled;

        /// <summary>切替の後、移動キーを離すのを待っているか（HUD の案内）。</summary>
        public bool WaitingForRelease => _human.WaitingForRelease;

        public TeamAi? BlueAi => _blueAi;

        public TeamAi? RedAi => _redAi;

        /// <summary>試合終了の笛の後、結果画面までの間か。</summary>
        public bool Finished => _match != null && _match.Phase == MatchPhase.Ended;

        /// <summary>結果：青の勝ち 1・引き分け 0・負け -1（試合の外は 0）。</summary>
        public int Outcome => _match == null ? 0 : Math.Sign(_match.Score(Team.Blue) - _match.Score(Team.Red));

        public uint MatchHash => _match?.Hash() ?? 0;

        // ---- 入力 ----

        public void Enqueue(Intent intent) => _intents.Enqueue(intent);

        public void Press(InputButton button, int keySlot = 0) => Enqueue(Intent.Press(button, keySlot));

        public void Release(InputButton button, int keySlot = 0) => Enqueue(Intent.Release(button, keySlot));

        /// <summary>押して離す（テスト・撮影）。</summary>
        public void Tap(InputButton button)
        {
            Press(button, 3);
            Release(button, 3);
        }

        /// <summary>今の画面の項目をクリックする（テスト・撮影）。</summary>
        public void Click(int index) => Enqueue(Intent.Click(index, Version));

        /// <summary>実時間で進める。たまった入力を順に当ててから、経過時間ぶんの Tick を進める（1 フレームの上限あり）。</summary>
        public void UpdateFrame(double deltaSeconds)
        {
            FlushIntents();
            if (Screen == FlowScreen.Playing && HasFocus) { PlaySeconds = Math.Min(MaxPlaySeconds, PlaySeconds + Math.Max(0, deltaSeconds)); }
            _accumulator += Math.Max(0, deltaSeconds) * Pitch.TickRate;
            var ticks = (int)Math.Floor(_accumulator);
            _accumulator -= ticks;
            if (ticks > _settings.MaxTicksPerFrame)
            {
                ticks = _settings.MaxTicksPerFrame;
                _accumulator = 0;
            }

            for (int i = 0; i < ticks; i++) { TickOnce(); }
        }

        /// <summary>Tick を進める（テスト・撮影）。</summary>
        public void StepTicks(int ticks)
        {
            FlushIntents();
            for (int i = 0; i < ticks; i++) { TickOnce(); }
        }

        public void FlushIntents()
        {
            while (_intents.Count > 0) { Apply(_intents.Dequeue()); }
        }

        /// <summary>フォーカスを失ったら保留の入力と押しているキーを捨て、試合中なら一時停止にする（戻っても手で再開する）。</summary>
        public void OnFocusChanged(bool hasFocus)
        {
            HasFocus = hasFocus;
            if (hasFocus) { return; }
            _intents.Clear();
            _human.ReleaseAll();
            if (Screen == FlowScreen.Playing)
            {
                MenuCursor = PauseResume;
                SetScreen(FlowScreen.Paused);
            }
        }

        // ---- 内部 ----

        private void Apply(Intent intent)
        {
            // フォーカスを失っている間は押下を当てはめない（喪失の通知の後に届いた入力も遷移させない）。離す入力は押している印を消すので通す。
            if (!HasFocus && intent.Pressed) { return; }
            if (intent.Button == InputButton.Click && intent.Version != Version) { return; }
            switch (Screen)
            {
                case FlowScreen.Title:
                    if (!intent.Pressed) { return; }
                    if (MenuCursor == TitleTactics && intent.Button is InputButton.Left or InputButton.Right)
                    {
                        CycleTactics(intent.Button == InputButton.Left ? -1 : 1);
                        return;
                    }

                    Menu(intent, 4, ConfirmTitle);
                    break;
                case FlowScreen.Paused:
                    if (!intent.Pressed) { _human.Apply(intent.Button, intent.KeySlot, false, _match!); return; }
                    if (intent.Button == InputButton.Cancel) { Resume(); }
                    else { Menu(intent, _pauseItems.Count, ConfirmPause); }
                    break;
                case FlowScreen.HowTo:
                case FlowScreen.Result:
                    if (intent.Pressed && intent.Button is InputButton.Confirm or InputButton.Cancel or InputButton.Click) { ToTitle(); }
                    break;
                case FlowScreen.Playing:
                    if (intent.Pressed && intent.Button == InputButton.Cancel)
                    {
                        _human.ReleaseAll();
                        MenuCursor = PauseResume;
                        SetScreen(FlowScreen.Paused);
                        return;
                    }

                    // 試合が終わった後の入力は捨てる（離す入力だけ押している印を消す）。
                    if (Finished && intent.Pressed) { return; }
                    if (intent.Button is InputButton.Click or InputButton.Confirm or InputButton.Cancel) { return; }
                    _human.Apply(intent.Button, intent.KeySlot, intent.Pressed, _match!);
                    break;
                default:
                    throw new InvalidOperationException($"未知の画面 {Screen}");
            }
        }

        private void Menu(Intent intent, int count, Action confirm)
        {
            switch (intent.Button)
            {
                case InputButton.Up:
                    MenuCursor = (MenuCursor + count - 1) % count;
                    Cues.Cursor++;
                    break;
                case InputButton.Down:
                    MenuCursor = (MenuCursor + 1) % count;
                    Cues.Cursor++;
                    break;
                case InputButton.Confirm:
                    confirm();
                    break;
                case InputButton.Click when intent.Index >= 0 && intent.Index < count:
                    MenuCursor = intent.Index;
                    confirm();
                    break;
                default:
                    break;
            }
        }

        private void ConfirmTitle()
        {
            Cues.Choose++;
            switch (MenuCursor)
            {
                case TitleStart:
                    StartMatch();
                    break;
                case TitleTactics:
                    CycleTactics(1);
                    break;
                case TitleHowTo:
                    SetScreen(FlowScreen.HowTo);
                    break;
                case TitleQuit:
                    QuitRequested = true;
                    break;
                default:
                    throw new InvalidOperationException($"未知の項目 {MenuCursor}");
            }
        }

        private void CycleTactics(int step)
        {
            OpponentChoice = (OpponentChoice + OpponentTactics.Count + step) % OpponentTactics.Count;
            Cues.Cursor++;
            Version++;
        }

        private void ConfirmPause()
        {
            Cues.Choose++;
            if (MenuCursor == PauseResume) { Resume(); }
            else { ToTitle(); }
        }

        private void StartMatch()
        {
            var seed = _nextSeed;

            // 再戦は新しいシード（時刻を読み直さず、起動時の値から試合ごとに進める、010）。
            _nextSeed = unchecked((_nextSeed * 1103515245) + 12345);
            _match = new Match(_config, seed);
            _blueAi = new TeamAi(Team.Blue, Tactics.Balanced);
            _redAi = new TeamAi(Team.Red, Opponent);
            _human = new HumanControl(_settings.AutoSwitchOnDefense);
            _stats = new MatchStats();
            _endTicks = 0;
            PlaySeconds = 0;
            _accumulator = 0;

            // キックオフの蹴る人を操作する。
            _human.Begin(_match);
            SetScreen(FlowScreen.Playing);
        }

        private void TickOnce()
        {
            // 一時停止・試合の外では時計も止める（044）。
            if (Screen != FlowScreen.Playing || _match == null) { return; }
            if (Finished)
            {
                if (++_endTicks >= _settings.EndBannerTicks) { SetScreen(FlowScreen.Result); }
                return;
            }

            Array.Clear(_commands, 0, _commands.Length);
            var human = Autopilot ? -1 : _human.Controlled;
            if (!Autopilot) { _commands[human] = _human.Command(_match); }
            _blueAi!.Decide(_match, human, _commands);
            _redAi!.Decide(_match, -1, _commands);
            _match.Step(_commands);
            _stats.Observe(_match);
            var before = _human.Switches;
            _human.AfterStep(_match);
            if (_human.Switches != before) { Cues.Switch++; }
            foreach (var e in _match.Events)
            {
                switch (e.Kind)
                {
                    case MatchEventKind.Kick:
                        Cues.Kick++;
                        break;
                    case MatchEventKind.Goal:
                        Cues.Goal++;
                        break;
                    case MatchEventKind.Tackle:
                        Cues.Tackle++;
                        break;
                    case MatchEventKind.Whistle:
                        Cues.Whistle++;
                        break;
                    default:
                        break;
                }
            }
        }

        private void Resume()
        {
            SetScreen(FlowScreen.Playing);
        }

        private void ToTitle()
        {
            _match = null;
            _blueAi = null;
            _redAi = null;
            _human = new HumanControl(_settings.AutoSwitchOnDefense);
            MenuCursor = TitleStart;
            SetScreen(FlowScreen.Title);
        }

        private void SetScreen(FlowScreen screen)
        {
            Screen = screen;
            Version++;
        }
    }
}
