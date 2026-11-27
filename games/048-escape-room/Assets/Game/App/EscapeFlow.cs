using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core.Mechanisms;
using Game.Core.Play;
using Game.Core.Rooms;
using IntGeometry;

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

    /// <summary>
    /// 画面の流れ（タイトル → 操作説明 → 遊ぶ → 一時停止 → 結果 → タイトル）と、遊ぶ画面の入力の当てはめ
    /// （クリック → 命令、持ち物の選択、手に取った物のドラッグ、ヒント、リセット）。
    /// 世界（<see cref="World"/>）を動かすのはここだけで、Unity 層には表示用の値だけを見せる。
    /// 入力は 1 本の列で順に当て（012）、クリックは押した画面の版を当てはめる直前に照合する（044・047）。
    /// 視点が変わったらカメラの演出の間（<see cref="FlowSettings.TransitionTicks"/>）、クリック・向き直り・戻るを捨てる（015）。
    /// 時間は Tick で数え、一時停止の間は止める（044）。
    /// </summary>
    public sealed class EscapeFlow
    {
        public const int TitleNew = 0;
        public const int TitleHowTo = 1;
        public const int TitleQuit = 2;
        public const int PauseResume = 0;
        public const int PauseReset = 1;
        public const int PauseTitle = 2;

        /// <summary>試合のシードを派生する乱数の用途の番号。</summary>
        public const int SeedPurpose = 7;

        private static readonly ReadOnlyCollection<string> _titleItems = Array.AsReadOnly(new[] { "はじめから", "操作説明", "終了" });
        private static readonly ReadOnlyCollection<string> _pauseItems = Array.AsReadOnly(new[] { "つづける", "リセット", "タイトルへ" });

        private readonly RoomDef _def;
        private readonly int _baseSeed;
        private readonly FlowSettings _settings;
        private readonly Queue<Intent> _intents = new();
        private readonly World _idle;
        private World? _world;
        private double _accumulator;
        private int _transition;
        private int _messageTicks;
        private int _hintTicks;
        private int _resultDelay;
        private int _dragX;
        private int _dragY;

        public EscapeFlow(RoomDef def, int baseSeed, FlowSettings? settings = null)
        {
            _def = def ?? throw new ArgumentNullException(nameof(def));
            _baseSeed = baseSeed;

            // 検証した値を外から書き換えられないよう、複製を持つ。
            _settings = settings?.Copy() ?? new FlowSettings();
            var errors = _settings.Validate();
            if (errors.Count > 0) { throw new ArgumentException(string.Join("\n", errors), nameof(settings)); }

            // タイトルの背景に描く部屋（始めの状態）。描画のたびに作らず 1 回だけ作る（008）。
            _idle = new World(new Room(def, MatchSeed(baseSeed, 0)));
        }

        public RoomDef Def => _def;

        public FlowScreen Screen { get; private set; } = FlowScreen.Title;

        /// <summary>画面の版。画面・視点・手に取った物・リセットのたびに増える。クリックはこの版を持って送る。</summary>
        public int Version { get; private set; }

        public AudioCues Cues { get; } = new();

        public bool QuitRequested { get; private set; }

        /// <summary>手に取った物を 1 段回すドラッグの長さ。</summary>
        public int DragStepUnits => _settings.DragStepUnits;

        public bool HasFocus { get; private set; } = true;

        public int MenuCursor { get; private set; }

        public IReadOnlyList<string> MenuItems => Screen == FlowScreen.Paused ? _pauseItems : _titleItems;

        /// <summary>何試合目か（はじめから のたびに増える。リセットでは増えない）。</summary>
        public int Match { get; private set; }

        // ---- 遊ぶ画面に出すもの（世界の読み取りはここを通す） ----

        public bool InGame => _world != null && Screen is FlowScreen.Playing or FlowScreen.Paused or FlowScreen.Result;

        private World Shown => _world ?? _idle;

        /// <summary>今の試合（答えを持つ）。タイトルでは背景の部屋。</summary>
        public Room Room => Shown.Room;

        public int Seed => Shown.Room.Seed;

        public int View => Shown.View;

        /// <summary>カメラの演出の始まりの視点（演出していなければ今の視点）。</summary>
        public int PreviousView { get; private set; }

        /// <summary>カメラの演出の進み具合（0 始まり → 1 終わり。演出していなければ 1）。</summary>
        public double TransitionProgress => _settings.TransitionTicks == 0 ? 1 : 1 - ((double)_transition / _settings.TransitionTicks);

        public bool InTransition => _transition > 0;

        public int Held => Shown.Held;

        public Orientation Orientation => Shown.Orientation;

        public bool ClueFacing => Shown.ClueFacing;

        public IReadOnlyList<int> Inventory => Shown.Inventory;

        /// <summary>選んでいる持ち物（なければ -1）。</summary>
        public int SelectedItem { get; private set; } = -1;

        public Phase PhaseOf(int mechanism) => Shown.PhaseOf(mechanism);

        public int ValueOf(int mechanism, int index) => Shown.ValueOf(mechanism, index);

        public bool IsPresent(int part) => Shown.IsPresent(part);

        public int SolvedCount => Shown.SolvedCount;

        /// <summary>短い知らせ（なければ空）。</summary>
        public string Message { get; private set; } = "";

        /// <summary>ヒントの文（出していなければ空）。</summary>
        public string HintText { get; private set; } = "";

        /// <summary>出しているヒントの段階（0〜2）。</summary>
        public int HintLevel { get; private set; }

        /// <summary>最後の命令の結果。</summary>
        public Outcome LastOutcome { get; private set; }

        /// <summary>最後に実行した命令（入力から作って世界に当てはめた命令。拒否されたものも含む）。</summary>
        public Command LastCommand { get; private set; }

        /// <summary>実行した命令の通し番号（世界に当てはめるたびに 1 増える。入力が捨てられたら増えない）。操縦役が実行を確かめる。</summary>
        public long Executed { get; private set; }

        /// <summary>部屋のクリックが届く画面の範囲（Unity が画面の比率と HUD から渡す。既定は対応する全ての画面で届く範囲）。</summary>
        public ScreenRegion Region { get; private set; } = ScreenRegion.Guaranteed;

        public bool Escaped => _world?.Escaped ?? false;

        /// <summary>経過時間の上限（秒）。これを超えたら飽和させる。</summary>
        public const double MaxPlaySeconds = 359999;

        /// <summary>遊んだ Tick（演出の時計。一時停止・脱出の後は数えない。リセットで 0 に戻る）。処理落ちのフレームの上限で捨てた Tick は含まない。</summary>
        public long PlayTicks { get; private set; }

        /// <summary>
        /// 経過時間（秒）。ロジックの 1 フレームの Tick 上限から切り離し、<see cref="UpdateFrame"/> に渡された実時間（Tick を進めるときは Tick の秒）で数える（017）。
        /// 遊ぶ画面で脱出の前だけ数え、一時停止・結果・フォーカスのない間は数えない。リセットで 0 に戻る。
        /// </summary>
        public double PlaySeconds { get; private set; }

        public int Actions => Shown.Actions;

        public int HintsUsed => Shown.HintsUsed;

        public uint WorldHash => _world?.Hash() ?? 0;

        /// <summary>遊ぶ画面で、演出も手に取った物もなく、次のクリックを受け付けるか。</summary>
        public bool IsIdle => Screen == FlowScreen.Playing && _world != null && !_world.Escaped && _transition == 0;

        /// <summary>操縦役が先読みに使う世界の複製（遊んでいなければ null）。</summary>
        public World? Snapshot() => _world?.Clone();

        /// <summary>画面の点に当たる、今クリックすると命令になる部品（マウスの下の名前を出す）。なければ -1。</summary>
        public int PartAt(int sx, int sy)
        {
            if (!IsIdle || _world!.Held >= 0) { return -1; }
            return _world.CommandAt(sx, sy, -1).IsNone ? -1 : _world.HitTest(sx, sy);
        }

        public static int MatchSeed(int baseSeed, int match) => (int)RandomSeries.Derive(baseSeed, match, SeedPurpose).NextUInt();

        // ---- 入力 ----

        public void Enqueue(Intent intent) => _intents.Enqueue(intent);

        public void Press(IntentKind kind, int index = 0) => Enqueue(Intent.Key(kind, index));

        public void Click(ClickTarget target, int index) => Enqueue(Intent.Click(target, index, Version));

        public void ClickWorld(int sx, int sy) => Enqueue(Intent.ClickWorld(sx, sy, Version));

        /// <summary>今の画面の版でドラッグを送る（App のテスト・操縦役）。Unity はドラッグを始めた版を付けて <see cref="Enqueue"/> する。</summary>
        public void Drag(int dx, int dy) => Enqueue(Intent.Drag(dx, dy, Version));

        /// <summary>部屋のクリックが届く画面の範囲を決める（範囲の外のクリックは部屋に通さない）。</summary>
        public void SetRegion(ScreenRegion region) => Region = region;

        public void UpdateFrame(double deltaSeconds)
        {
            FlushIntents();
            CountPlayTime(Math.Max(0, deltaSeconds));
            _accumulator += Math.Max(0, deltaSeconds) * _settings.TickRate;
            var ticks = (int)Math.Floor(_accumulator);
            _accumulator -= ticks;
            if (ticks > _settings.MaxTicksPerFrame)
            {
                ticks = _settings.MaxTicksPerFrame;
                _accumulator = 0;
            }

            for (int i = 0; i < ticks; i++) { TickOnce(); }
        }

        public void StepTicks(int ticks)
        {
            FlushIntents();
            for (int i = 0; i < ticks; i++)
            {
                CountPlayTime(1.0 / _settings.TickRate);
                TickOnce();
            }
        }

        public void FlushIntents()
        {
            while (_intents.Count > 0) { Apply(_intents.Dequeue()); }
        }

        /// <summary>
        /// フォーカスを失ったら、遊ぶ画面なら一時停止にし（時計・演出・知らせの時間を止める）、保留中の入力とドラッグの途中を捨て、
        /// 戻るまで入力を当てはめない（046）。戻っても一時停止のままにし、プレイヤーが明示的に再開する。
        /// </summary>
        public void OnFocusChanged(bool hasFocus)
        {
            HasFocus = hasFocus;
            if (hasFocus) { return; }
            _intents.Clear();
            _dragX = 0;
            _dragY = 0;
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
            if (intent.Kind == IntentKind.Click && intent.Version != Version) { return; }
            switch (Screen)
            {
                case FlowScreen.Title:
                    Menu(intent, _titleItems.Count, ConfirmTitle);
                    break;
                case FlowScreen.Paused:
                    if (intent.Kind == IntentKind.Pause) { Resume(); }
                    else if (intent.Kind == IntentKind.Reset) { ResetMatch(); }
                    else { Menu(intent, _pauseItems.Count, ConfirmPause); }
                    break;
                case FlowScreen.HowTo:
                case FlowScreen.Result:
                    if (intent.Kind is IntentKind.Confirm or IntentKind.Pause || (intent.Kind == IntentKind.Click && intent.Target != ClickTarget.World)) { ToTitle(); }
                    break;
                case FlowScreen.Playing:
                    Playing(intent);
                    break;
                default:
                    throw new InvalidOperationException($"未知の画面 {Screen}");
            }
        }

        private void Menu(Intent intent, int count, Action confirm)
        {
            switch (intent.Kind)
            {
                case IntentKind.Up:
                    MenuCursor = (MenuCursor + count - 1) % count;
                    Cues.Cursor++;
                    break;
                case IntentKind.Down:
                    MenuCursor = (MenuCursor + 1) % count;
                    Cues.Cursor++;
                    break;
                case IntentKind.Confirm:
                    confirm();
                    break;
                case IntentKind.Click when intent.Target == ClickTarget.Menu && intent.Index >= 0 && intent.Index < count:
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
                case TitleNew:
                    StartNew();
                    break;
                case TitleHowTo:
                    SetScreen(FlowScreen.HowTo);
                    break;
                default:
                    QuitRequested = true;
                    break;
            }
        }

        private void ConfirmPause()
        {
            Cues.Choose++;
            switch (MenuCursor)
            {
                case PauseResume:
                    Resume();
                    break;
                case PauseReset:
                    ResetMatch();
                    break;
                default:
                    ToTitle();
                    break;
            }
        }

        private void Playing(Intent intent)
        {
            var w = _world!;

            // 脱出の後（扉が開く演出の間）は何も受け付けない。
            if (w.Escaped) { return; }
            switch (intent.Kind)
            {
                case IntentKind.Pause:
                    if (SelectedItem >= 0) { Deselect(); }
                    else if (w.Held >= 0 || !w.OnWall)
                    {
                        if (_transition == 0) { Execute(Command.Back); }
                    }
                    else
                    {
                        MenuCursor = PauseResume;
                        SetScreen(FlowScreen.Paused);
                    }

                    break;
                case IntentKind.Back:
                    if (SelectedItem >= 0) { Deselect(); }
                    else if (_transition == 0 && (w.Held >= 0 || !w.OnWall)) { Execute(Command.Back); }
                    break;
                case IntentKind.Turn:
                    if (_transition == 0 && w.Held < 0) { Execute(Command.Turn(intent.Index >= 0 ? 1 : -1)); }
                    break;
                case IntentKind.Hint:
                    Execute(Command.Hint);
                    break;
                case IntentKind.Drag:
                    // ドラッグを始めた後に手に取った物・視点・画面が変わっていたら、古い操作なので捨てる。
                    if (intent.Version == Version) { DragHeld(intent.X, intent.Y); }
                    break;
                case IntentKind.Click:
                    _dragX = 0;
                    _dragY = 0;
                    if (intent.Target == ClickTarget.Item) { ClickItem(intent.Index); }
                    else if (intent.Target == ClickTarget.World) { ClickWorld(w, intent.X, intent.Y); }
                    break;
                default:
                    break;
            }
        }

        private void ClickWorld(World w, int sx, int sy)
        {
            // カメラの演出の間と手に取っている間のクリックは命令にしない（手に取っている間はドラッグの始まり）。
            if (_transition > 0 || w.Held >= 0 || !Region.Contains(sx, sy)) { return; }
            var command = w.CommandAt(sx, sy, SelectedItem);
            if (command.IsNone) { return; }
            Execute(command);
        }

        private void ClickItem(int slot)
        {
            var w = _world!;
            if (w.Held >= 0 || slot < 0 || slot >= w.Inventory.Count) { return; }
            var item = w.Inventory[slot];
            if (SelectedItem != item)
            {
                SelectedItem = item;
                Cues.Cursor++;
                return;
            }

            // 選んだ持ち物をもう一度：手に取れる物なら手に取る、ほかは選択を外す。
            Deselect();
            if (_def.Items[item].Examinable >= 0 && _transition == 0) { Execute(Command.InspectItem(item)); }
        }

        private void DragHeld(int dx, int dy)
        {
            var w = _world!;
            if (w.Held < 0) { return; }
            _dragX += dx;
            _dragY += dy;
            var step = _settings.DragStepUnits;

            // 右へドラッグすると手前の面が右へ回る（ヨーは負）、上へドラッグすると手前の面が上へ回る（ピッチは正）。
            while (Math.Abs(_dragX) >= step)
            {
                var sign = Math.Sign(_dragX);
                _dragX -= sign * step;
                Execute(Command.Rotate(-sign, 0));
            }

            while (Math.Abs(_dragY) >= step)
            {
                var sign = Math.Sign(_dragY);
                _dragY -= sign * step;
                Execute(Command.Rotate(0, sign));
            }
        }

        private void Deselect()
        {
            SelectedItem = -1;
            Cues.Cursor++;
        }

        /// <summary>命令を世界に当てはめ、結果を知らせ・音の合図・カメラの演出に移す。</summary>
        private bool Execute(Command command)
        {
            var w = _world!;
            var view = w.View;
            var held = w.Held;
            var o = w.Apply(command);
            LastOutcome = o;
            LastCommand = command;
            Executed++;
            if (!o.Accepted)
            {
                Cues.Reject++;
                var text = RefusalText(command, o.Reason);
                if (text.Length > 0) { Say(text); }
                return false;
            }

            if (w.View != view)
            {
                PreviousView = view;
                _transition = _settings.TransitionTicks;
                Cues.Move++;
                Version++;
                _dragX = 0;
                _dragY = 0;
            }

            if (w.Held != held)
            {
                // 手に取った物が変わったら、ドラッグの端数も前の物とともに捨てる。
                Version++;
                _dragX = 0;
                _dragY = 0;
            }

            if (SelectedItem >= 0 && !w.Has(SelectedItem)) { SelectedItem = -1; }
            Absorb(w);
            return true;
        }

        private string RefusalText(Command command, Refusal reason)
        {
            switch (reason)
            {
                case Refusal.WrongItem:
                    return "それは合わない。";
                case Refusal.AlreadySolved:
                    return "もう開いている。";
                case Refusal.Locked:
                    {
                        var part = command.Kind is CommandKind.Operate or CommandKind.UseItem ? _def.Parts[command.Kind == CommandKind.UseItem ? command.B : command.A] : null;
                        return part?.Kind switch
                        {
                            PartKind.Keyhole => "鍵がかかっている。",
                            PartKind.Socket => "何かをはめるくぼみがある。",
                            _ => "固くて動かない。何かが足りないようだ。",
                        };
                    }

                default:
                    return "";
            }
        }

        /// <summary>世界の出来事を知らせと音の合図に移す。</summary>
        private void Absorb(World w)
        {
            foreach (var e in w.Events)
            {
                switch (e.Kind)
                {
                    case EventKind.Operated:
                        Cues.Click++;
                        break;
                    case EventKind.Mistake:
                        Cues.Mistake++;
                        Say("宝石の光が消えた。最初からだ。");
                        break;
                    case EventKind.ItemTaken:
                        Cues.Pickup++;
                        Say($"{_def.Items[e.Index].Name}を手に入れた。");
                        break;
                    case EventKind.Installed:
                        Cues.Install++;
                        break;
                    case EventKind.Solved:
                        Cues.Solve++;
                        Say($"{_def.Mechanisms[e.Index].Name}が開いた。");
                        break;
                    case EventKind.ClueShown:
                        Cues.Clue++;
                        break;
                    case EventKind.Hint:
                        Cues.Hint++;
                        HintText = e.Text;
                        HintLevel = e.Index;
                        _hintTicks = _settings.HintTicks;
                        break;
                    case EventKind.Escaped:
                        Cues.Escape++;
                        Say("扉が開いた！");
                        _resultDelay = _settings.ResultDelayTicks;
                        SelectedItem = -1;
                        break;
                    default:
                        break;
                }
            }
        }

        private void Say(string text)
        {
            Message = text;
            _messageTicks = _settings.MessageTicks;
        }

        private void CountPlayTime(double seconds)
        {
            if (Screen != FlowScreen.Playing || _world == null || _world.Escaped || !HasFocus) { return; }
            PlaySeconds = Math.Min(MaxPlaySeconds, PlaySeconds + seconds);
        }

        private void TickOnce()
        {
            // 一時停止・結果の間は時計も止める（044）。
            if (Screen != FlowScreen.Playing || _world == null) { return; }
            if (_transition > 0) { _transition--; }
            if (_messageTicks > 0 && --_messageTicks == 0) { Message = ""; }
            if (_hintTicks > 0 && --_hintTicks == 0) { HintText = ""; }
            if (!_world.Escaped)
            {
                PlayTicks++;
                return;
            }

            if (_resultDelay > 0) { _resultDelay--; }
            if (_resultDelay == 0) { SetScreen(FlowScreen.Result); }
        }

        private void StartNew()
        {
            _world = new World(new Room(_def, MatchSeed(_baseSeed, Match)));
            Match++;
            ClearPlayState();
            SetScreen(FlowScreen.Playing);
        }

        private void ResetMatch()
        {
            var o = _world!.Apply(Command.Reset);
            if (!o.Accepted) { throw new InvalidOperationException($"リセットを世界が拒否しました：{o.Reason}"); }
            ClearPlayState();
            SetScreen(FlowScreen.Playing);
        }

        private void ClearPlayState()
        {
            PlayTicks = 0;
            PlaySeconds = 0;
            SelectedItem = -1;
            Message = "";
            HintText = "";
            HintLevel = 0;
            _messageTicks = 0;
            _hintTicks = 0;
            _transition = 0;
            _resultDelay = 0;
            _dragX = 0;
            _dragY = 0;
            LastOutcome = Outcome.Ok;
            PreviousView = _world?.View ?? _idle.View;
        }

        private void Resume() => SetScreen(FlowScreen.Playing);

        private void ToTitle()
        {
            _world = null;
            ClearPlayState();
            MenuCursor = TitleNew;
            SetScreen(FlowScreen.Title);
        }

        private void SetScreen(FlowScreen screen)
        {
            Screen = screen;
            Version++;
            _dragX = 0;
            _dragY = 0;
        }
    }
}
