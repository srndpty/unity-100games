using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum FlowScreen
    {
        Title,
        HowTo,
        Playing,
        Paused,

        /// <summary>終局後の死石の確定。</summary>
        Scoring,
        Result,
    }

    /// <summary>プレイヤーから見た対局の結果。</summary>
    public enum Outcome
    {
        Win,
        Loss,
        Draw,
    }

    /// <summary>
    /// 画面の流れと、プレイヤーの操作を Core へ渡す窓口。入力は意図の列にため、フレームの始めに順に当てはめる。
    /// クリックは押した画面の版を持ち、版（画面の切り替え・局面の変化・死石の切り替えで進む）が違えば捨てる。
    /// 意図を当てはめて画面が変わったら、同じフレームにたまった残りの意図（キーも）を捨てる（前の画面で押したキーを次の画面に効かせない）。
    /// 実時間を固定 Tick に変える。探索（CPU と終局後の死石の推定）は <see cref="SearchInTicks"/> なら Tick ごとに決まった数だけ、
    /// そうでなければ <see cref="Think"/>（フレームの時間の予算）だけで回す。どちらも総数で止まるので、手と推定は回し方に依らない。
    /// 一時停止・対局の外では Tick（探索も）が止まる。
    /// </summary>
    public sealed class GoFlow
    {
        public const int TitleStart = 0;
        public const int TitleLevel = 1;
        public const int TitleSide = 2;
        public const int TitleHowTo = 3;
        public const int TitleQuit = 4;
        public const int TitleCount = 5;

        public const int PauseResume = 0;
        public const int PauseResign = 1;
        public const int PauseTitle = 2;
        public const int PauseCount = 3;

        public const int ResultAgain = 0;
        public const int ResultTitle = 1;
        public const int ResultCount = 2;

        /// <summary>HUD のボタンのクリックの番号（交点は 0〜80）。</summary>
        public const int ClickPass = 100;
        public const int ClickPause = 101;
        public const int ClickConfirmScore = 102;

        private readonly GoConfig _config;
        private readonly MctsConfig _mcts;
        private readonly FlowSettings _settings;
        private readonly Queue<Intent> _intents = new();
        private int _nextSeed;
        private double _accumulator;
        private bool _noticeIsRejection;

        public GoFlow(GoConfig config, MctsConfig mcts, int firstSeed, FlowSettings? settings = null)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (mcts == null) { throw new ArgumentNullException(nameof(mcts)); }
            settings ??= new FlowSettings();
            var errors = new List<string>();
            errors.AddRange(config.Validate());
            errors.AddRange(mcts.Validate());
            errors.AddRange(settings.Validate());
            if (errors.Count > 0) { throw new ArgumentException("設定が範囲外です: " + string.Join(" / ", errors)); }
            _config = config.Copy();
            _mcts = mcts.Copy();
            _settings = settings.Copy();
            _nextSeed = firstSeed;
        }

        public FlowScreen Screen { get; private set; } = FlowScreen.Title;

        /// <summary>画面の版。画面の切り替え・局面の変化・死石の切り替えで進む。クリックの照合と表示の更新に使う。</summary>
        public int Version { get; private set; }

        public AudioCues Cues { get; } = new();

        public bool QuitRequested { get; private set; }

        public bool HasFocus { get; private set; } = true;

        /// <summary>メニューの選択位置（タイトル・一時停止・結果）。</summary>
        public int MenuCursor { get; private set; }

        /// <summary>次の対局の強さ（タイトルで選ぶ）。</summary>
        public CpuLevel Level { get; private set; } = CpuLevel.Normal;

        /// <summary>次の対局のプレイヤーの色（タイトルで選ぶ）。</summary>
        public Stone PlayerSide { get; private set; } = Stone.Black;

        public CpuLevel MatchLevel { get; private set; } = CpuLevel.Normal;

        public Stone MatchPlayerSide { get; private set; } = Stone.Black;

        public Stone CpuSide => MatchPlayerSide.Opponent();

        public int MatchSeed { get; private set; }

        public GoGame? Game { get; private set; }

        public CpuPlayer? Cpu { get; private set; }

        public BoardCursor Cursor { get; } = new();

        /// <summary>石を置いた演出の残り Tick。この間は着手の入力を捨て、CPU も待つ。</summary>
        public int BusyTicks { get; private set; }

        /// <summary>通知（パス・拒否の理由）。消えたら null。</summary>
        public string? Notice { get; private set; }

        public int NoticeTicks { get; private set; }

        /// <summary>プレイヤーの直前の着手が拒否された理由（打てたら null）。</summary>
        public MoveVerdict? LastRejection { get; private set; }

        /// <summary>局面が変わった回数（表示の更新に使う）。</summary>
        public int MoveSerial { get; private set; }

        /// <summary>死石の印（推定とプレイヤーの切り替え）。</summary>
        public PointSet DeadStones { get; private set; }

        /// <summary>確定した計点（投了の対局は null）。</summary>
        public ScoreResult? FinalScore { get; private set; }

        public Outcome? Outcome { get; private set; }

        /// <summary>終局後の死石の推定（死石の確定の画面で、回し終えるまで印を付けず入力も受け付けない）。</summary>
        public OwnershipSearch? Estimate { get; private set; }

        /// <summary>死石を推定している（死石の確定の画面で、推定を回し終えていない）。</summary>
        public bool IsEstimating => Screen == FlowScreen.Scoring && Estimate != null && !Estimate.IsDone;

        /// <summary>
        /// true（既定、テスト・撮影）なら探索を Tick ごとに <see cref="FlowSettings.PlayoutsPerTick"/> だけ回す。
        /// false（Unity の対局）なら Tick は時間（考える間・演出・通知）だけを進め、探索は <see cref="Think"/> だけで回す。
        /// </summary>
        public bool SearchInTicks { get; set; } = true;

        /// <summary>対局の作り方。テストと撮影が決まった局面から始めるときに差し替える（引数は試合シード）。</summary>
        public Func<int, GoGame>? GameFactory { get; set; }

        /// <summary>プレイヤーが打てる状態（手番で、演出中でない）。</summary>
        public bool IsPlayerTurn => Screen == FlowScreen.Playing && Game != null && !Game.IsOver && Game.ToMove == MatchPlayerSide && BusyTicks == 0;

        /// <summary>CPU が考えている。</summary>
        public bool IsCpuThinking => Screen == FlowScreen.Playing && Game != null && !Game.IsOver && Game.ToMove == CpuSide;

        /// <summary>死石の印を反映した計点（死石の確定の画面で表示する）。</summary>
        public ScoreResult ScorePreview => AreaScore.Compute(Game!.CopyBoard(), DeadStones, Game.Komi);

        public static string LevelLabel(CpuLevel level) => level switch
        {
            CpuLevel.Weak => "弱い",
            CpuLevel.Normal => "普通",
            CpuLevel.Strong => "強い",
            _ => level.ToString(),
        };

        public static string RejectionText(MoveVerdict verdict) => verdict switch
        {
            MoveVerdict.Occupied => "そこには石があります",
            MoveVerdict.Suicide => "自殺手は打てません（呼吸点がなくなります）",
            MoveVerdict.Ko => "コウです。すぐには取り返せません",
            MoveVerdict.Superko => "同じ盤面に戻るので打てません（同型反復）",
            MoveVerdict.OffBoard => "盤の外です",
            _ => verdict.ToString(),
        };

        // ---- 入力 ----

        public void Enqueue(Intent intent) => _intents.Enqueue(intent);

        public void Press(InputButton button) => Enqueue(Intent.Press(button));

        /// <summary>今の画面の項目・交点・ボタンをクリックする（今の版で）。</summary>
        public void Click(int index) => Enqueue(Intent.Click(index, Version));

        /// <summary>実時間で進める。たまった入力を順に当ててから、経過時間ぶんの Tick を進める（1 フレームの上限あり）。</summary>
        public void UpdateFrame(double deltaSeconds)
        {
            FlushIntents();
            if (!IsTicking || !HasFocus)
            {
                _accumulator = 0;
                return;
            }

            if (double.IsFinite(deltaSeconds) && deltaSeconds > 0) { _accumulator += deltaSeconds * FlowSettings.TickRate; }
            var whole = Math.Floor(_accumulator);
            _accumulator -= whole;
            var ticks = whole > _settings.MaxTicksPerFrame ? _settings.MaxTicksPerFrame : (int)whole;
            for (var i = 0; i < ticks; i++) { TickOnce(); }
        }

        /// <summary>入力を当ててから Tick を進める（テスト・撮影）。</summary>
        public void StepTicks(int ticks)
        {
            FlushIntents();
            for (var i = 0; i < ticks; i++) { TickOnce(); }
        }

        /// <summary>
        /// 探索を Tick と別に進める（Unity がフレームの時間の予算で呼ぶ）。CPU が考えているとき、または死石を推定しているときだけ。戻り値は回した数。
        /// 一時停止・演出中・フォーカス喪失中は回さない。
        /// </summary>
        public int Think(int maxPlayouts)
        {
            if (!HasFocus) { return 0; }
            if (IsEstimating) { return RunEstimate(maxPlayouts); }
            if (!IsCpuThinking || BusyTicks > 0) { return 0; }
            return Cpu!.Think(Game!, maxPlayouts);
        }

        public void FlushIntents()
        {
            while (_intents.Count > 0)
            {
                var screen = Screen;
                Apply(_intents.Dequeue());
                if (Screen != screen) { _intents.Clear(); }
            }
        }

        /// <summary>フォーカスを失ったら保留の入力を捨て、対局中なら一時停止にする（戻っても手で再開する）。</summary>
        public void OnFocusChanged(bool hasFocus)
        {
            HasFocus = hasFocus;
            if (hasFocus) { return; }
            _intents.Clear();
            if (Screen == FlowScreen.Playing)
            {
                MenuCursor = PauseResume;
                SetScreen(FlowScreen.Paused);
            }
        }

        // ---- 内部 ----

        private void Apply(Intent intent)
        {
            // フォーカスを失っている間は当てはめない（喪失の通知の後に届いた入力も遷移させない）。
            if (!HasFocus) { return; }
            if (intent.Button == InputButton.Click && intent.Version != Version) { return; }
            switch (Screen)
            {
                case FlowScreen.Title:
                    ApplyTitle(intent);
                    break;
                case FlowScreen.HowTo:
                    if (intent.Button is InputButton.Confirm or InputButton.Cancel or InputButton.Click) { ToTitle(); }
                    break;
                case FlowScreen.Playing:
                    ApplyPlaying(intent);
                    break;
                case FlowScreen.Paused:
                    if (intent.Button == InputButton.Cancel) { Resume(); }
                    else { Menu(intent, PauseCount, ConfirmPause); }
                    break;
                case FlowScreen.Scoring:
                    ApplyScoring(intent);
                    break;
                case FlowScreen.Result:
                    Menu(intent, ResultCount, ConfirmResult);
                    break;
                default:
                    throw new InvalidOperationException($"未知の画面 {Screen}");
            }
        }

        private void ApplyTitle(Intent intent)
        {
            if (intent.Button is InputButton.Left or InputButton.Right)
            {
                var step = intent.Button == InputButton.Left ? -1 : 1;
                if (MenuCursor == TitleLevel) { ShiftLevel(step); }
                else if (MenuCursor == TitleSide) { ToggleSide(); }
                return;
            }

            Menu(intent, TitleCount, ConfirmTitle);
        }

        private void ApplyPlaying(Intent intent)
        {
            switch (intent.Button)
            {
                case InputButton.Cancel:
                case InputButton.Click when intent.Index == ClickPause:
                    MenuCursor = PauseResume;
                    SetScreen(FlowScreen.Paused);
                    break;
                case InputButton.Up:
                case InputButton.Down:
                case InputButton.Left:
                case InputButton.Right:
                    MoveCursor(intent.Button);
                    break;
                case InputButton.Confirm:
                    PlayerMove(Cursor.Point);
                    break;
                case InputButton.Pass:
                case InputButton.Click when intent.Index == ClickPass:
                    PlayerPass();
                    break;
                case InputButton.Click when Points.IsOnBoard(intent.Index):
                    Cursor.MoveTo(intent.Index);
                    PlayerMove(intent.Index);
                    break;
                default:
                    break;
            }
        }

        private void ApplyScoring(Intent intent)
        {
            // 推定を回し終えるまでは印がないので、切り替えも確定も受け付けない。
            if (IsEstimating) { return; }
            switch (intent.Button)
            {
                case InputButton.Cancel:
                case InputButton.Click when intent.Index == ClickConfirmScore:
                    FinishScoring();
                    break;
                case InputButton.Up:
                case InputButton.Down:
                case InputButton.Left:
                case InputButton.Right:
                    MoveCursor(intent.Button);
                    break;
                case InputButton.Confirm:
                    ToggleDead(Cursor.Point);
                    break;
                case InputButton.Click when Points.IsOnBoard(intent.Index):
                    Cursor.MoveTo(intent.Index);
                    ToggleDead(intent.Index);
                    break;
                default:
                    break;
            }
        }

        private void MoveCursor(InputButton button)
        {
            var (dx, dy) = button switch
            {
                InputButton.Up => (0, 1),
                InputButton.Down => (0, -1),
                InputButton.Left => (-1, 0),
                _ => (1, 0),
            };
            Cursor.Move(dx, dy);
            Cues.Cursor++;
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
                case TitleLevel:
                    ShiftLevel(1, wrap: true);
                    break;
                case TitleSide:
                    ToggleSide();
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

        private void ShiftLevel(int step, bool wrap = false)
        {
            var next = (int)Level + step;
            if (wrap) { next = (next + 3) % 3; }
            if (next < (int)CpuLevel.Weak || next > (int)CpuLevel.Strong) { return; }
            Level = (CpuLevel)next;
            Cues.Cursor++;
            Version++;
        }

        private void ToggleSide()
        {
            PlayerSide = PlayerSide.Opponent();
            Cues.Cursor++;
            Version++;
        }

        private void ConfirmPause()
        {
            Cues.Choose++;
            switch (MenuCursor)
            {
                case PauseResume:
                    Resume();
                    break;
                case PauseResign:
                    if (Game!.TryResign(MatchPlayerSide) == MoveVerdict.Ok)
                    {
                        Cues.End++;
                        ToResult();
                    }

                    break;
                default:
                    ToTitle();
                    break;
            }
        }

        private void ConfirmResult()
        {
            Cues.Choose++;
            if (MenuCursor == ResultAgain) { StartMatch(); }
            else { ToTitle(); }
        }

        private void StartMatch()
        {
            MatchSeed = _nextSeed;

            // 再戦は新しいシード（時刻を読み直さず、起動時の値から試合ごとに進める、010）。
            _nextSeed = unchecked((_nextSeed * 1103515245) + 12345);
            MatchLevel = Level;
            MatchPlayerSide = PlayerSide;
            Game = GameFactory?.Invoke(MatchSeed) ?? new GoGame(_config);
            Cpu = new CpuPlayer(CpuSide, _mcts, _mcts.PlayoutsOf(MatchLevel), _settings.PlayoutsPerTick, _settings.CpuThinkTicks, MatchSeed);
            BusyTicks = 0;
            Notice = null;
            NoticeTicks = 0;
            _noticeIsRejection = false;
            LastRejection = null;
            DeadStones = PointSet.Empty;
            Estimate = null;
            FinalScore = null;
            Outcome = null;
            _accumulator = 0;
            Cursor.Reset();
            MoveSerial++;
            SetScreen(FlowScreen.Playing);
        }

        private void PlayerMove(int point)
        {
            // 演出中・CPU の手番の着手は捨てる（先行入力しない）。
            if (!IsPlayerTurn) { return; }
            var game = Game!;
            var before = game.Captures(MatchPlayerSide);
            var verdict = game.TryPlay(MatchPlayerSide, point);
            if (verdict != MoveVerdict.Ok)
            {
                LastRejection = verdict;
                ShowNotice(RejectionText(verdict));
                _noticeIsRejection = true;
                Cues.Reject++;
                return;
            }

            AfterMove(MatchPlayerSide, before);
        }

        private void PlayerPass()
        {
            if (!IsPlayerTurn) { return; }
            if (Game!.TryPass(MatchPlayerSide) != MoveVerdict.Ok) { return; }
            AfterPass(MatchPlayerSide);
        }

        private void AfterMove(Stone side, int capturesBefore)
        {
            ClearRejection();
            Cues.Place++;
            if (Game!.Captures(side) > capturesBefore) { Cues.Capture++; }
            BusyTicks = _settings.PlaceAnimTicks;
            Changed();
        }

        private void AfterPass(Stone side)
        {
            ClearRejection();
            Cues.Pass++;
            ShowNotice($"{side.Label()}がパスしました");
            Changed();
        }

        private void Changed()
        {
            MoveSerial++;
            Version++;
            if (Game!.IsOver)
            {
                Cues.End++;
                if (Game.End == GameEnd.Resignation) { ToResult(); }
                else { StartScoring(); }
            }
        }

        private void ShowNotice(string text)
        {
            Notice = text;
            NoticeTicks = _settings.NoticeTicks;
            _noticeIsRejection = false;
        }

        /// <summary>着手・パスが通ったら、前の拒否の理由を消す（パスなどの通知は残す）。</summary>
        private void ClearRejection()
        {
            LastRejection = null;
            if (!_noticeIsRejection) { return; }
            Notice = null;
            NoticeTicks = 0;
            _noticeIsRejection = false;
        }

        private void StartScoring()
        {
            // 推定は Tick（SearchInTicks）か Think で少しずつ回し、回し終えたら印を付ける。
            DeadStones = PointSet.Empty;
            Estimate = Ownership.Start(Game!, _mcts, MatchSeed);
            SetScreen(FlowScreen.Scoring);
        }

        private int RunEstimate(int maxPlayouts)
        {
            var ran = Estimate!.Run(maxPlayouts);
            if (Estimate.IsDone)
            {
                DeadStones = Estimate.EstimateDead(_mcts.DeadPercent);
                Version++;
            }

            return ran;
        }

        private void ToggleDead(int point)
        {
            var game = Game!;
            if (game[point] == Stone.Empty) { return; }
            var chain = game.ChainAt(point);
            DeadStones = DeadStones.Contains(point) ? DeadStones.Except(chain) : DeadStones.Union(chain);
            Cues.Choose++;
            Version++;
        }

        private void FinishScoring()
        {
            FinalScore = ScorePreview;
            Cues.Choose++;
            ToResult();
        }

        private void ToResult()
        {
            var game = Game!;
            var winner = game.End == GameEnd.Resignation ? game.Resigned.Opponent() : FinalScore!.Value.Winner;
            Outcome = winner == Stone.Empty ? App.Outcome.Draw : winner == MatchPlayerSide ? App.Outcome.Win : App.Outcome.Loss;
            Notice = null;
            NoticeTicks = 0;
            _noticeIsRejection = false;
            MenuCursor = ResultAgain;
            SetScreen(FlowScreen.Result);
        }

        /// <summary>Tick を進める画面（対局中か、死石の推定中）。</summary>
        private bool IsTicking => (Screen == FlowScreen.Playing && Game != null && !Game.IsOver) || IsEstimating;

        private void TickOnce()
        {
            // 一時停止・対局の外では時計も探索も止める（044）。
            if (!IsTicking) { return; }
            if (IsEstimating)
            {
                if (SearchInTicks) { RunEstimate(_settings.PlayoutsPerTick); }
                return;
            }

            if (NoticeTicks > 0 && --NoticeTicks == 0)
            {
                Notice = null;
                _noticeIsRejection = false;
            }

            if (BusyTicks > 0)
            {
                BusyTicks--;
                return;
            }

            var game = Game!;
            if (game.ToMove != CpuSide) { return; }
            var before = game.Captures(CpuSide);
            switch (Cpu!.Tick(game, SearchInTicks))
            {
                case CpuAction.Played:
                    AfterMove(CpuSide, before);
                    break;
                case CpuAction.Passed:
                    AfterPass(CpuSide);
                    break;
                case CpuAction.Resigned:
                    ShowNotice($"{CpuSide.Label()}が投了しました");
                    Changed();
                    break;
                default:
                    break;
            }
        }

        private void Resume()
        {
            SetScreen(FlowScreen.Playing);
        }

        private void ToTitle()
        {
            Game = null;
            Cpu = null;
            DeadStones = PointSet.Empty;
            MenuCursor = TitleStart;
            Cursor.Reset();
            MoveSerial++;
            SetScreen(FlowScreen.Title);
        }

        private void SetScreen(FlowScreen screen)
        {
            Screen = screen;
            _accumulator = 0;
            Version++;
        }
    }
}
