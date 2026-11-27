using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core.Data;
using Game.Core.Play;
using Game.Core.Save;

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

    /// <summary>遊んでいる間の段（一時停止の間も残る）。</summary>
    public enum PlayView
    {
        /// <summary>朝：行動を選ぶ。</summary>
        Morning,

        /// <summary>行動の結果（段階と能力値の増減）。</summary>
        Report,

        /// <summary>イベントの会話と選択肢。</summary>
        Scene,

        /// <summary>ED の会話。</summary>
        Ending,
    }

    /// <summary>メニューの 1 項目。</summary>
    public sealed class MenuItem
    {
        public MenuItem(string label, bool enabled, string detail = "")
        {
            Label = label;
            Enabled = enabled;
            Detail = detail;
        }

        public string Label { get; }
        public bool Enabled { get; }

        /// <summary>選べない理由など（空なら出さない）。</summary>
        public string Detail { get; }
    }

    /// <summary>能力値の 1 行（HUD の棒・ハート）。</summary>
    public sealed class StatLine
    {
        public StatLine(string id, string name, int value, int min, int max, bool isHeart)
        {
            Id = id;
            Name = name;
            Value = value;
            Min = min;
            Max = max;
            IsHeart = isHeart;
        }

        public string Id { get; }
        public string Name { get; }
        public int Value { get; }
        public int Min { get; }
        public int Max { get; }
        public bool IsHeart { get; }

        /// <summary>好感度の段階（ハート 0〜5）。</summary>
        public int Hearts => IsHeart ? Math.Min(5, (Value - Min) * 5 / Math.Max(1, Max - Min)) : 0;

        /// <summary>棒の長さ（0〜1）。</summary>
        public float Fill => (float)(Value - Min) / Math.Max(1, Max - Min);
    }

    /// <summary>
    /// 画面の流れ（タイトル → 夏休み（朝 → 結果 → イベント → … → ED）→ 結果画面 → タイトル）。
    /// 夏休み（<see cref="Summer"/>）を動かすのはここだけで、Unity 層には表示用の値だけを見せる。
    /// 入力は 1 本の列で順に当てる（012）。時間は Tick で数え、実時間は <see cref="UpdateFrame"/> で Tick に直す。
    /// 朝ごとに自動セーブ（1 枠）し、ED に着いたら消す。
    /// </summary>
    public sealed class SummerFlow
    {
        public const int TitleNew = 0;
        public const int TitleContinue = 1;
        public const int TitleHowTo = 2;
        public const int TitleQuit = 3;
        public const int PauseResume = 0;
        public const int PauseTitle = 1;

        /// <summary>遊んだ時間の上限（99:59:59）。</summary>
        public const double MaxPlaySeconds = 359999;

        private static readonly string[] _weekdayNames = { "月", "火", "水", "木", "金", "土", "日" };

        private readonly SummerBook _book;
        private readonly ISaveStore _store;
        private readonly FlowSettings _settings;
        private readonly Queue<Intent> _intents = new();
        private readonly List<MenuItem> _titleItems = new();
        private readonly ReadOnlyCollection<MenuItem> _pauseItems;
        private readonly List<string> _reportChanges = new();
        private readonly List<string> _sceneChanges = new();
        private Summer? _summer;
        private int _nextSeed;
        private int _line;
        private bool _choosing;
        private int _revealTicks;
        private bool _revealedAll;
        private int _background = -1;
        private int _scene = -1;
        private int _reportAction = -1;
        private int _reportOutcome = -1;
        private int _ending = -1;
        private double _accumulator;
        private long _noticeUntil;

        public SummerFlow(SummerBook book, ISaveStore store, int firstSeed, FlowSettings? settings = null)
        {
            _book = book ?? throw new ArgumentNullException(nameof(book));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            // 検証した値を外から書き換えられないよう、複製を持つ。
            _settings = settings?.Copy() ?? new FlowSettings();
            var errors = _settings.Validate();
            if (errors.Count > 0) { throw new ArgumentException(string.Join("\n", errors), nameof(settings)); }
            _nextSeed = firstSeed;
            _pauseItems = Array.AsReadOnly(new[] { new MenuItem("つづける", true), new MenuItem("タイトルへ", true) });
            RefreshTitle();
        }

        public SummerBook Book => _book;

        public FlowScreen Screen { get; private set; } = FlowScreen.Title;

        public PlayView View { get; private set; } = PlayView.Morning;

        /// <summary>画面の版。画面・段・会話の行・選択肢が変わるたびに増える。クリックはこの版を持って送る。</summary>
        public int Version { get; private set; }

        public long Ticks { get; private set; }

        public AudioCues Cues { get; } = new();

        /// <summary>案内の文（出していなければ空）。</summary>
        public string Notice { get; private set; } = "";

        public bool QuitRequested { get; private set; }

        public bool HasFocus { get; private set; } = true;

        /// <summary>遊んだ時間（遊んでいる画面でフォーカスがある間だけ数える。Tick の上限とは別、017）。</summary>
        public double PlaySeconds { get; private set; }

        /// <summary>夏休みに受け付けられた命令（行動・送る・選ぶ）の通し番号（操縦役が実行を確かめる、048）。</summary>
        public int Commands { get; private set; }

        public int MenuCursor { get; private set; }

        public IReadOnlyList<MenuItem> TitleItems => _titleItems.AsReadOnly();

        public IReadOnlyList<MenuItem> PauseItems => _pauseItems;

        /// <summary>能力値の詳細を開いているか（Tab）。</summary>
        public bool DetailOpen { get; private set; }

        /// <summary>遊んでいる（一時停止を含む）か。</summary>
        public bool InSummer => _summer != null && Screen is FlowScreen.Playing or FlowScreen.Paused;

        // ---- 夏休みの表示 ----

        public int Day => _summer?.Day ?? 0;

        public int Days => _book.Days;

        public string WeekdayName => _summer == null ? "" : _weekdayNames[(int)_summer.Weekday];

        public IReadOnlyList<StatLine> Stats
        {
            get
            {
                var list = new List<StatLine>();
                if (_summer == null) { return list.AsReadOnly(); }
                foreach (var s in _book.Stats) { list.Add(new StatLine(s.Id, s.Name, _summer.Stat(s.Index), s.Min, s.Max, s.IsHeart)); }
                return list.AsReadOnly();
            }
        }

        /// <summary>朝の行動の一覧（選べない行動は理由付き）。</summary>
        public IReadOnlyList<MenuItem> Actions
        {
            get
            {
                var list = new List<MenuItem>();
                if (_summer == null) { return list.AsReadOnly(); }
                foreach (var a in _book.Actions)
                {
                    var ok = _summer.CanChoose(a.Index);
                    list.Add(new MenuItem(a.Name, ok, ok ? "" : UnavailableReason(a)));
                }

                return list.AsReadOnly();
            }
        }

        /// <summary>今のメニュー（タイトル・一時停止・朝の行動・選択肢）。</summary>
        public IReadOnlyList<MenuItem> MenuItems => Screen switch
        {
            FlowScreen.Title => TitleItems,
            FlowScreen.Paused => _pauseItems,
            FlowScreen.Playing when View == PlayView.Morning => Actions,
            FlowScreen.Playing when IsChoice => OptionItems(),
            _ => Array.Empty<MenuItem>(),
        };

        /// <summary>結果の見出し（「勉強 — 大成功」）。</summary>
        public string ReportTitle => InSummer && View == PlayView.Report
            ? _book.Actions[_reportAction].Name + " — " + _book.Outcomes[_reportOutcome].Name
            : "";

        /// <summary>結果の段階の番号（結果の段の外は -1）。</summary>
        public int ReportOutcome => InSummer && View == PlayView.Report ? _reportOutcome : -1;

        /// <summary>結果の能力値の増減（「学力 +6」）。</summary>
        public IReadOnlyList<string> ReportChanges => _reportChanges.AsReadOnly();

        public bool InText => InSummer && View is PlayView.Scene or PlayView.Ending;

        public bool IsChoice => InSummer && View == PlayView.Scene && _choosing;

        private SceneLine? CurrentLine
        {
            get
            {
                var lines = CurrentLines;
                return lines != null && _line < lines.Count ? lines[_line] : null;
            }
        }

        private IReadOnlyList<SceneLine>? CurrentLines => !InText ? null
            : View == PlayView.Ending ? _book.Endings[_ending].Lines
            : _book.Events[_scene].Lines;

        /// <summary>話者の人物の ID（地の文・会話の外は空）。</summary>
        public string SpeakerId => CurrentLine is { Speaker: >= 0 } l ? _book.Characters[l.Speaker].Id : "";

        public string SpeakerName => CurrentLine is { Speaker: >= 0 } l ? _book.Characters[l.Speaker].Name : "";

        /// <summary>話者の名前の色（0xRRGGBB、地の文は 0）。</summary>
        public uint SpeakerColor => CurrentLine is { Speaker: >= 0 } l ? _book.Characters[l.Speaker].Color : 0;

        /// <summary>話者の表情の ID。</summary>
        public string Face => CurrentLine is { Speaker: >= 0 } l ? _book.Characters[l.Speaker].Faces[l.Face] : "";

        public string FullText => CurrentLine?.Text ?? "";

        public int VisibleChars => IsRevealed ? FullText.Length : Math.Min(FullText.Length, 1 + (_revealTicks / Math.Max(1, _settings.TicksPerChar)));

        public string VisibleText => FullText.Substring(0, VisibleChars);

        public bool IsRevealed => _revealedAll || _settings.TicksPerChar == 0 || (1 + (_revealTicks / Math.Max(1, _settings.TicksPerChar))) >= FullText.Length;

        public IReadOnlyList<string> Options
        {
            get
            {
                var list = new List<string>();
                if (IsChoice)
                {
                    foreach (var c in _book.Events[_scene].Choices) { list.Add(c.Text); }
                }

                return list.AsReadOnly();
            }
        }

        /// <summary>今の背景の ID（夏休みの外は空）。</summary>
        public string Background => InSummer || Screen == FlowScreen.Result ? _book.Backgrounds[Math.Max(0, _background)].Id : "";

        /// <summary>今のイベント（会話の外は -1）。</summary>
        public int SceneIndex => InSummer && View == PlayView.Scene ? _scene : -1;

        // ---- 結果画面 ----

        public int EndingIndex => _ending;

        public string EndingName => _ending >= 0 ? _book.Endings[_ending].Name : "";

        /// <summary>試合の状態のハッシュ（夏休みの外は 0）。</summary>
        public uint SummerHash => _summer?.Hash() ?? 0;

        internal Summer? Summer => _summer;

        // ---- 入力 ----

        public void Enqueue(Intent intent) => _intents.Enqueue(intent);

        public void Press(InputButton button) => Enqueue(Intent.Press(button));

        /// <summary>今の画面の項目をクリックする（テスト・操縦役）。</summary>
        public void Click(int index) => Enqueue(Intent.Click(index, Version));

        /// <summary>実時間で進める。たまった入力を順に当ててから、経過時間ぶんの Tick を進める（1 フレームの上限あり）。</summary>
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

        /// <summary>フォーカスを失ったら保留の入力を捨て、遊んでいれば一時停止にする（戻っても手で再開する）。</summary>
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
                    Menu(intent, TitleItems.Count, ConfirmTitle);
                    break;
                case FlowScreen.Paused:
                    if (intent.Button == InputButton.Cancel) { Resume(); }
                    else { Menu(intent, _pauseItems.Count, ConfirmPause); }
                    break;
                case FlowScreen.HowTo:
                case FlowScreen.Result:
                    if (intent.Button is InputButton.Confirm or InputButton.Cancel or InputButton.Click) { ToTitle(); }
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
            var item = _titleItems[MenuCursor];
            if (!item.Enabled)
            {
                Cues.Reject++;
                if (item.Detail.Length > 0) { ShowNotice(item.Detail); }
                return;
            }

            Cues.Choose++;
            switch (MenuCursor)
            {
                case TitleNew:
                    var seed = _nextSeed;
                    // 再戦は新しいシード（時刻を読み直さず、起動時の値から試合ごとに進める、010）。
                    _nextSeed = unchecked(_nextSeed * 1103515245 + 12345);
                    StartSummer(new Summer(_book, seed));
                    break;
                case TitleContinue:
                    if (TryReadSave(out var restored, out var reason)) { StartSummer(restored!); }
                    else
                    {
                        Cues.Reject++;
                        ShowNotice(reason);
                        RefreshTitle();
                    }

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

        private void ConfirmPause()
        {
            Cues.Choose++;
            if (MenuCursor == PauseResume) { Resume(); }
            else { ToTitle(); }
        }

        private void Playing(Intent intent)
        {
            switch (intent.Button)
            {
                case InputButton.Cancel:
                    MenuCursor = PauseResume;
                    SetScreen(FlowScreen.Paused);
                    return;
                case InputButton.Detail:
                    DetailOpen = !DetailOpen;
                    return;
                default:
                    break;
            }

            switch (View)
            {
                case PlayView.Morning:
                    Menu(intent, _book.Actions.Count, ConfirmAction);
                    break;
                case PlayView.Report:
                    if (intent.Button is InputButton.Confirm or InputButton.Click) { AfterCommand(); }
                    break;
                case PlayView.Scene:
                case PlayView.Ending:
                    if (IsChoice) { Menu(intent, _book.Events[_scene].Choices.Count, ConfirmChoice); }
                    else if (intent.Button is InputButton.Confirm or InputButton.Click or InputButton.Down) { ConfirmText(); }
                    break;
                default:
                    throw new InvalidOperationException($"未知の段 {View}");
            }
        }

        private void ConfirmAction()
        {
            var s = _summer!;
            var a = _book.Actions[MenuCursor];
            if (!s.CanChoose(a.Index))
            {
                Cues.Reject++;
                ShowNotice(UnavailableReason(a));
                return;
            }

            var r = s.ChooseAction(a.Index);
            if (!r.Accepted) { throw new InvalidOperationException(r.ToString()); }
            Commands++;
            Cues.Choose++;
            // 出来事のない日は次の朝まで進むので、行動と結果の段階は出来事から読む。
            foreach (var e in s.Events)
            {
                if (e.Kind != SummerEventKind.ActionDone) { continue; }
                _reportAction = e.Index;
                _reportOutcome = e.Value;
            }

            if (_book.Outcomes[_reportOutcome].Percent > 100) { Cues.Great++; }
            _background = a.Background >= 0 ? a.Background : 0;
            _reportChanges.Clear();
            Collect(s.Events, _reportChanges);
            SetView(PlayView.Report);
        }

        private void ConfirmChoice()
        {
            var r = _summer!.Choose(MenuCursor);
            if (!r.Accepted)
            {
                Cues.Reject++;
                return;
            }

            Commands++;
            Cues.Choose++;
            Collect(_summer.Events, _sceneChanges);
            AfterCommand();
        }

        /// <summary>会話の決定：表示途中なら全文を出し、出し終えていれば次の行・選択肢・次の段へ。</summary>
        private void ConfirmText()
        {
            if (!IsRevealed)
            {
                // 全文を出したら受付の版を進め、同じフレームの 2 回目のクリックで送らない（046）。
                _revealedAll = true;
                Version++;
                return;
            }

            Cues.Advance++;
            var lines = CurrentLines!;
            if (_line + 1 < lines.Count)
            {
                _line++;
                ShowLine();
                return;
            }

            if (View == PlayView.Ending)
            {
                SetScreen(FlowScreen.Result);
                return;
            }

            if (_book.Events[_scene].Choices.Count > 0)
            {
                _choosing = true;
                MenuCursor = 0;
                Version++;
                return;
            }

            var r = _summer!.Continue();
            if (!r.Accepted) { throw new InvalidOperationException(r.ToString()); }
            Commands++;
            Collect(_summer.Events, _sceneChanges);
            AfterCommand();
        }

        /// <summary>命令の後：夏休みの段に合わせて、イベント・ED・次の朝へ進む。</summary>
        private void AfterCommand()
        {
            var s = _summer!;
            if (View == PlayView.Scene && _sceneChanges.Count > 0)
            {
                ShowNotice(string.Join("　", _sceneChanges));
                _sceneChanges.Clear();
            }

            switch (s.Phase)
            {
                case Phase.Scene:
                    BeginScene(s.Scene);
                    break;
                case Phase.Ended:
                    BeginEnding(s.Ending);
                    break;
                default:
                    Morning();
                    break;
            }
        }

        private void BeginScene(int index)
        {
            _scene = index;
            var e = _book.Events[index];
            if (e.Background >= 0) { _background = e.Background; }
            _line = 0;
            _choosing = false;
            // 始めに当たった効果（@do）は、会話が終わったときに案内する。
            Collect(_summer!.Events, _sceneChanges, afterScene: true);
            SetView(PlayView.Scene);
            ShowLine();
        }

        private void BeginEnding(int index)
        {
            _ending = index;
            _scene = -1;
            var e = _book.Endings[index];
            if (e.Background >= 0) { _background = e.Background; }
            _line = 0;
            _choosing = false;
            Cues.Ending++;
            DeleteSave();
            SetView(PlayView.Ending);
            ShowLine();
        }

        private void Morning()
        {
            _scene = -1;
            _choosing = false;
            _background = 0;
            MenuCursor = FirstAvailable();
            SetView(PlayView.Morning);
            AutoSave();
        }

        private void ShowLine()
        {
            var bg = CurrentLine?.Background ?? -1;
            if (bg >= 0) { _background = bg; }
            _revealTicks = 0;
            _revealedAll = false;
            Version++;
        }

        /// <summary>出来事から能力値の増減の文を作る。会話の中の出来事は、始まりの区切り（SceneStarted）の前後で分ける。</summary>
        private void Collect(IReadOnlyList<SummerEvent> events, List<string> into, bool afterScene = false)
        {
            var started = false;
            foreach (var e in events)
            {
                if (e.Kind == SummerEventKind.SceneStarted) { started = true; }
                if (e.Kind != SummerEventKind.StatChanged || started != afterScene) { continue; }
                var stat = _book.Stats[e.Index];
                if (stat.IsHeart && e.Value > 0) { Cues.Heart++; }
                into.Add(stat.Name + (e.Value > 0 ? " +" : " ") + e.Value);
            }
        }

        private void StartSummer(Summer s)
        {
            _summer = s;
            _ending = -1;
            _reportChanges.Clear();
            _sceneChanges.Clear();
            DetailOpen = false;
            PlaySeconds = 0;
            SetScreen(FlowScreen.Playing);
            Morning();
        }

        private void AutoSave()
        {
            if (!_store.TryWrite(SummerSaveCodec.Encode(_book, _summer!.Snapshot()), out var failure))
            {
                ShowNotice("セーブできませんでした：" + failure);
            }
        }

        private void DeleteSave()
        {
            if (!_store.TryDelete(out var failure)) { ShowNotice("セーブを消せませんでした：" + failure); }
        }

        private bool TryReadSave(out Summer? summer, out string reason)
        {
            summer = null;
            switch (_store.TryRead(out var content, out var failure))
            {
                case SaveReadStatus.Missing:
                    reason = "セーブがありません";
                    return false;
                case SaveReadStatus.Failed:
                    reason = "セーブを読めませんでした：" + failure;
                    return false;
                default:
                    break;
            }

            if (!SummerSaveCodec.TryDecode(_book, content, out summer, out var error))
            {
                reason = "セーブを読めませんでした：" + error;
                return false;
            }

            reason = "";
            return true;
        }

        private void RefreshTitle()
        {
            _titleItems.Clear();
            _titleItems.Add(new MenuItem("はじめから", true));
            var canContinue = TryReadSave(out var s, out var reason);
            _titleItems.Add(new MenuItem("つづきから", canContinue, canContinue ? $"{s!.Day} 日目の朝から" : reason));
            _titleItems.Add(new MenuItem("操作説明", true));
            _titleItems.Add(new MenuItem("終了", true));
        }

        private ReadOnlyCollection<MenuItem> OptionItems()
        {
            var list = new List<MenuItem>();
            foreach (var o in Options) { list.Add(new MenuItem(o, true)); }
            return list.AsReadOnly();
        }

        private int FirstAvailable()
        {
            for (int a = 0; a < _book.Actions.Count; a++)
            {
                if (_summer!.CanChoose(a)) { return a; }
            }

            return 0;
        }

        private string UnavailableReason(ActionDef a)
        {
            if (_summer!.Forced) { return "疲れて動けない。今日は休むしかない"; }
            return $"{WeekdayName}曜日は選べない";
        }

        private void TickOnce()
        {
            // 一時停止の間は時計も止める（案内の期限も減らさない、044）。
            if (Screen == FlowScreen.Paused) { return; }
            Ticks++;
            if (Notice.Length > 0 && Ticks >= _noticeUntil) { Notice = ""; }
            if (Screen == FlowScreen.Playing && InText && !IsChoice && !_revealedAll) { _revealTicks++; }
        }

        private void CountPlayTime(double seconds)
        {
            if (Screen != FlowScreen.Playing || !HasFocus) { return; }
            PlaySeconds = Math.Min(MaxPlaySeconds, PlaySeconds + seconds);
        }

        private void Resume() => SetScreen(FlowScreen.Playing);

        private void ToTitle()
        {
            _summer = null;
            _ending = -1;
            _scene = -1;
            _choosing = false;
            DetailOpen = false;
            _background = -1;
            Notice = "";
            RefreshTitle();
            MenuCursor = TitleNew;
            SetScreen(FlowScreen.Title);
        }

        private void SetView(PlayView view)
        {
            View = view;
            Version++;
        }

        private void SetScreen(FlowScreen screen)
        {
            Screen = screen;
            Version++;
        }

        private void ShowNotice(string text)
        {
            Notice = text;
            _noticeUntil = Ticks + _settings.NoticeTicks;
        }
    }
}
