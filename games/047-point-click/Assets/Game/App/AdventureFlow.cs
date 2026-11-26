using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core.Analysis;
using Game.Core.Play;
using Game.Core.Rules;
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

    /// <summary>画面に出す台詞 1 つ。</summary>
    public sealed class SpokenLine
    {
        public SpokenLine(string speaker, string text, int speakerHotspot, bool isDefault)
        {
            Speaker = speaker;
            Text = text;
            SpeakerHotspot = speakerHotspot;
            IsDefault = isDefault;
        }

        /// <summary>話し手の名前（主人公は空）。</summary>
        public string Speaker { get; }

        public string Text { get; }

        /// <summary>話し手の人物の物の番号（主人公は -1）。</summary>
        public int SpeakerHotspot { get; }

        /// <summary>ルールが当たらない既定の返事か、拒否の返事か。</summary>
        public bool IsDefault { get; }
    }

    /// <summary>
    /// 画面の流れ（タイトル → 遊ぶ → 結果 → タイトル）と、遊ぶ画面の命令の組み立て（動詞・持ち物・対象）・台詞・会話。
    /// 世界（<see cref="World"/>）を動かすのはここだけで、Unity 層には表示用の値だけを見せる。
    /// 入力は 1 本の列で順に当てる（012）。時間は Tick で数え、実時間は <see cref="UpdateFrame"/> で Tick に直す。
    /// </summary>
    public sealed class AdventureFlow
    {
        public const int TitleNew = 0;
        public const int TitleHowTo = 1;
        public const int TitleQuit = 2;
        public const int PauseResume = 0;
        public const int PauseTitle = 1;

        /// <summary>画面の動詞の並び（1〜4 のキーと同じ順）。</summary>
        public static readonly ReadOnlyCollection<Verb> Verbs = Array.AsReadOnly(new[] { Verb.Look, Verb.Take, Verb.Use, Verb.Talk });

        private const string Goodbye = "さようなら";

        private static readonly ReadOnlyCollection<string> _titleItems = Array.AsReadOnly(new[] { "はじめから", "操作説明", "終了" });
        private static readonly ReadOnlyCollection<string> _pauseItems = Array.AsReadOnly(new[] { "つづける", "タイトルへ" });

        private readonly RuleBook _book;
        private readonly WorldSettings _worldSettings;
        private readonly FlowSettings _settings;
        private readonly Queue<Intent> _intents = new();
        private readonly Queue<SpokenLine> _lines = new();
        private World? _world;
        private int _lineTicks;
        private double _accumulator;
        private int _hoverHotspot = -1;
        private int _hoverItem = -1;

        public AdventureFlow(RuleBook book, WorldSettings? worldSettings = null, FlowSettings? settings = null)
        {
            _book = book ?? throw new ArgumentNullException(nameof(book));
            // 検証した値を外から書き換えられないよう、複製を持つ。
            _worldSettings = worldSettings?.Copy() ?? new WorldSettings();
            _worldSettings.Validate();
            _settings = settings?.Copy() ?? new FlowSettings();
            var errors = _settings.Validate();
            if (errors.Count > 0) { throw new ArgumentException(string.Join("\n", errors), nameof(settings)); }
        }

        public RuleBook Book => _book;

        public FlowScreen Screen { get; private set; } = FlowScreen.Title;

        /// <summary>画面の版。画面・台詞・会話の選択肢・部屋が変わるたびに増える。クリックはこの版を持って送る。</summary>
        public int Version { get; private set; }

        public long Ticks { get; private set; }

        public AudioCues Cues { get; } = new();

        public bool QuitRequested { get; private set; }

        public bool HasFocus { get; private set; } = true;

        public int MenuCursor { get; private set; }

        public IReadOnlyList<string> MenuItems => Screen == FlowScreen.Paused ? _pauseItems : _titleItems;

        // ---- 遊ぶ画面に出すもの（世界の読み取りはここを通す） ----

        public bool InGame => _world != null && Screen is FlowScreen.Playing or FlowScreen.Paused;

        public int Room => _world?.Room ?? _book.StartRoom;

        public int PlayerX => _world?.X ?? _book.Rooms[_book.StartRoom].Start;

        public int Facing => _world?.Facing ?? 1;

        public bool IsWalking => _world?.IsWalking ?? false;

        public long WorldTick => _world?.Tick ?? 0;

        public bool Visible(int hotspot) => _world?.Visible(hotspot) ?? _book.Hotspots[hotspot].InitiallyVisible;

        public bool Solved(int puzzle) => _world?.Solved(puzzle) ?? false;

        /// <summary>部屋の座標の点にある、今の部屋の見えている物で一番手前のもの（遊んでいなければ -1）。</summary>
        public int HotspotAt(int x, int y) => _world?.HotspotAt(new Vec2i(x, y)) ?? -1;

        /// <summary>持ち物（手に入れた順、枠の順）。</summary>
        public IReadOnlyList<int> Inventory => _world?.Inventory ?? Array.Empty<int>();

        public int SolvedCount => _world?.SolvedCount ?? 0;

        public int ActionCount => _world?.Actions ?? 0;

        public uint WorldHash => _world?.Hash() ?? 0;

        /// <summary>選んでいる動詞（選んでいなければ Walk）。</summary>
        public Verb SelectedVerb { get; private set; } = Verb.Walk;

        /// <summary>使う持ち物（選んでいなければ -1）。</summary>
        public int SelectedTool { get; private set; } = -1;

        /// <summary>今出している台詞（なければ null）。</summary>
        public SpokenLine? CurrentLine { get; private set; }

        public bool IsTalking => InGame && _world!.TalkingTo >= 0;

        /// <summary>会話の相手（会話中でなければ -1）。</summary>
        public int TalkingTo => _world?.TalkingTo ?? -1;

        /// <summary>会話の選択肢を出しているか（台詞を出し終えた会話中）。</summary>
        public bool ShowingTopics => Screen == FlowScreen.Playing && IsTalking && CurrentLine == null;

        /// <summary>会話の選択肢（最後は「さようなら」）。</summary>
        public IReadOnlyList<string> TopicOptions
        {
            get
            {
                var list = new List<string>();
                if (!IsTalking) { return list.AsReadOnly(); }
                foreach (var t in _world!.AvailableTopics()) { list.Add(_book.Topics[t].Question); }
                list.Add(Goodbye);
                return list.AsReadOnly();
            }
        }

        public int TopicCursor { get; private set; }

        /// <summary>作りかけの命令文（「使う 包丁 → 漁網」）。マウスの下の物・持ち物を対象として出す。</summary>
        public string Sentence
        {
            get
            {
                if (Screen != FlowScreen.Playing || IsTalking) { return ""; }
                var s = VerbName(SelectedVerb);
                if (SelectedTool >= 0) { s += " " + _book.Items[SelectedTool].Name + " →"; }
                var hover = HoverName;
                return hover.Length > 0 ? s + " " + hover : s;
            }
        }

        // ---- 結果画面 ----

        public bool Escaped => _world?.Escaped ?? false;

        /// <summary>脱出までの時間（秒、一時停止は含まない）。</summary>
        public double ElapsedSeconds => (double)WorldTick / _settings.TickRate;

        public static string VerbName(Verb verb) => verb switch
        {
            Verb.Look => "見る",
            Verb.Take => "取る",
            Verb.Use => "使う",
            Verb.Talk => "話す",
            _ => "歩く",
        };

        // ---- 入力 ----

        public void Enqueue(Intent intent) => _intents.Enqueue(intent);

        public void Press(IntentKind kind, int index = -1) => Enqueue(Intent.Key(kind, index));

        public void Click(ClickTarget target, int index) => Enqueue(Intent.Click(target, index, Version));

        public void ClickWorld(int x, int y) => Enqueue(Intent.ClickWorld(x, y, Version));

        /// <summary>マウスの下の物（今の部屋の見えている物でなければ無視）。描画のたびに Unity 層が渡す。</summary>
        public void SetHover(int hotspot, int item)
        {
            _hoverHotspot = hotspot;
            _hoverItem = item;
        }

        public void UpdateFrame(double deltaSeconds)
        {
            FlushIntents();
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
            for (int i = 0; i < ticks; i++) { TickOnce(); }
        }

        public void FlushIntents()
        {
            while (_intents.Count > 0) { Apply(_intents.Dequeue()); }
        }

        public void OnFocusChanged(bool hasFocus)
        {
            HasFocus = hasFocus;
            if (!hasFocus) { _intents.Clear(); }
        }

        /// <summary>
        /// 操縦役・撮影の台本が、遊ぶ画面で手を 1 つ命じる（クリックの代わり。台詞を出している間・歩いている間は受け付けない）。
        /// 受け付けたら true。
        /// </summary>
        public bool TryIssue(Move move)
        {
            if (!IsIdle) { return false; }
            switch (move.Kind)
            {
                case MoveKind.Ask:
                    {
                        if (!IsTalking) { return false; }
                        var o = _world!.Ask(move.Topic);
                        if (!o.Accepted) { return false; }
                        AfterTopic();
                        return true;
                    }

                case MoveKind.EndTalk:
                    {
                        if (!_world!.EndTalk().Accepted) { return false; }
                        AfterTopic();
                        return true;
                    }

                default:
                    return Issue(move.Verb, move.Target, move.Tool);
            }
        }

        /// <summary>遊ぶ画面で、台詞も歩きもない（次の手を受け付ける）か。</summary>
        public bool IsIdle => Screen == FlowScreen.Playing && CurrentLine == null && _lines.Count == 0 && _world != null && !_world.IsWalking && !_world.Escaped;

        // ---- 内部 ----

        private string HoverName
        {
            get
            {
                if (_world == null) { return ""; }
                if (_hoverItem >= 0 && _hoverItem < _book.Items.Count && _world.Has(_hoverItem) && _hoverItem != SelectedTool) { return _book.Items[_hoverItem].Name; }
                if (_hoverHotspot >= 0 && _hoverHotspot < _book.Hotspots.Count)
                {
                    var h = _book.Hotspots[_hoverHotspot];
                    if (h.Room == _world.Room && _world.Visible(h.Index)) { return h.Name; }
                }

                return "";
            }
        }

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
                    else { Menu(intent, _pauseItems.Count, ConfirmPause); }
                    break;
                case FlowScreen.HowTo:
                case FlowScreen.Result:
                    if (intent.Kind is IntentKind.Confirm or IntentKind.Pause or IntentKind.Click) { ToTitle(); }
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
            if (MenuCursor == PauseResume) { Resume(); }
            else { ToTitle(); }
        }

        private void Playing(Intent intent)
        {
            if (intent.Kind == IntentKind.Pause)
            {
                MenuCursor = PauseResume;
                SetScreen(FlowScreen.Paused);
                return;
            }

            // 台詞を出している間は、決定とクリックで台詞を送るだけ（演出中の入力を世界に通さない、015）。
            if (CurrentLine != null)
            {
                if (intent.Kind is IntentKind.Confirm or IntentKind.Click) { NextLine(); }
                return;
            }

            if (IsTalking)
            {
                Topics(intent);
                return;
            }

            switch (intent.Kind)
            {
                case IntentKind.VerbKey:
                    SelectVerb(intent.Index);
                    break;
                case IntentKind.Deselect:
                    ClearSelection();
                    break;
                case IntentKind.Click:
                    switch (intent.Target)
                    {
                        case ClickTarget.Verb:
                            SelectVerb(intent.Index);
                            break;
                        case ClickTarget.Item:
                            ClickItem(intent.Index);
                            break;
                        case ClickTarget.World:
                            ClickWorldAt(intent.X, intent.Y);
                            break;
                        default:
                            break;
                    }

                    break;
                default:
                    break;
            }
        }

        private void Topics(Intent intent)
        {
            var count = TopicOptions.Count;
            switch (intent.Kind)
            {
                case IntentKind.Up:
                    TopicCursor = (TopicCursor + count - 1) % count;
                    Cues.Cursor++;
                    break;
                case IntentKind.Down:
                    TopicCursor = (TopicCursor + 1) % count;
                    Cues.Cursor++;
                    break;
                case IntentKind.Confirm:
                    ChooseTopic(TopicCursor);
                    break;
                case IntentKind.Click when intent.Target == ClickTarget.Topic && intent.Index >= 0 && intent.Index < count:
                    ChooseTopic(intent.Index);
                    break;
                default:
                    break;
            }
        }

        private void ChooseTopic(int option)
        {
            var available = _world!.AvailableTopics();
            var o = option < available.Count ? _world.Ask(available[option]) : _world.EndTalk();
            if (!o.Accepted) { throw new InvalidOperationException($"選択肢 {option} を世界が拒否しました：{o.Reason}"); }
            Cues.Choose++;
            AfterTopic();
        }

        private void AfterTopic()
        {
            TopicCursor = 0;
            Version++;
            Absorb();
        }

        private void SelectVerb(int index)
        {
            if (index < 0 || index >= Verbs.Count) { return; }
            var verb = Verbs[index];
            SelectedVerb = SelectedVerb == verb ? Verb.Walk : verb;
            SelectedTool = -1;
            Cues.Cursor++;
        }

        private void ClearSelection()
        {
            SelectedVerb = Verb.Walk;
            SelectedTool = -1;
        }

        private void ClickItem(int slot)
        {
            var inventory = _world!.Inventory;
            if (slot < 0 || slot >= inventory.Count) { return; }
            var item = inventory[slot];
            switch (SelectedVerb)
            {
                case Verb.Walk:
                    // 動詞なしで持ち物を選んだら「使う ＜持ち物＞ →」にする。
                    SelectedVerb = Verb.Use;
                    SelectedTool = item;
                    Cues.Cursor++;
                    break;
                case Verb.Use when SelectedTool < 0:
                    SelectedTool = item;
                    Cues.Cursor++;
                    break;
                case Verb.Use when SelectedTool == item:
                    SelectedTool = -1;
                    Cues.Cursor++;
                    break;
                default:
                    Issue(SelectedVerb, Target.Item(item), SelectedTool);
                    break;
            }
        }

        private void ClickWorldAt(int x, int y)
        {
            var hotspot = _world!.HotspotAt(new Vec2i(x, y));
            if (hotspot >= 0)
            {
                Issue(SelectedVerb, Target.Hotspot(hotspot), SelectedTool);
                return;
            }

            // 床や壁をクリックしたら、その x へ歩く（選んだ動詞はそのまま）。
            var width = _book.Rooms[_world.Room].Width;
            _world.WalkTo(Math.Max(0, Math.Min(width, x)));
        }

        private bool Issue(Verb verb, Target target, int tool)
        {
            var o = _world!.Act(verb, target, tool);
            if (!o.Accepted)
            {
                Cues.Reject++;
                var text = RefusalLine(o.Reason);
                if (text.Length > 0) { Say(new SpokenLine("", text, -1, true)); }
                return false;
            }

            Cues.Command++;
            if (_settings.ResetVerbAfterCommand) { ClearSelection(); }
            Absorb();
            return true;
        }

        private static string RefusalLine(Refusal reason) => reason switch
        {
            Refusal.NotHeld => "それは持っていない。",
            Refusal.NotAllowed => "それはできない。",
            _ => "",
        };

        /// <summary>世界の出来事を台詞と音の合図に移す。</summary>
        private void Absorb()
        {
            foreach (var e in _world!.Events)
            {
                switch (e.Kind)
                {
                    case EventKind.Said:
                        Say(new SpokenLine(e.Speaker >= 0 ? _book.Hotspots[e.Speaker].Name : "", e.Text, e.Speaker, e.IsDefault));
                        break;
                    case EventKind.ItemGained:
                        Cues.Pickup++;
                        break;
                    case EventKind.PuzzleSolved:
                        Cues.Solve++;
                        break;
                    case EventKind.RoomChanged:
                        Cues.Door++;
                        Version++;
                        break;
                    case EventKind.TalkStarted:
                    case EventKind.TalkEnded:
                        TopicCursor = 0;
                        Version++;
                        break;
                    case EventKind.Escaped:
                        Cues.Escape++;
                        break;
                    default:
                        break;
                }
            }
        }

        private void Say(SpokenLine line)
        {
            if (CurrentLine == null)
            {
                CurrentLine = line;
                _lineTicks = 0;
                Version++;
            }
            else
            {
                _lines.Enqueue(line);
            }
        }

        private void NextLine()
        {
            CurrentLine = _lines.Count > 0 ? _lines.Dequeue() : null;
            _lineTicks = 0;
            Version++;
            if (CurrentLine == null && _world!.Escaped) { ShowResult(); }
        }

        private void TickOnce()
        {
            // 一時停止の間は時計も止める（044）。
            if (Screen != FlowScreen.Playing || _world == null) { return; }
            Ticks++;
            var shown = CurrentLine;
            _world.Step();
            Absorb();
            if (CurrentLine == null)
            {
                if (_world.Escaped) { ShowResult(); }
                return;
            }

            // この Tick に出た台詞は、次の Tick から数える。
            if (CurrentLine != shown) { return; }
            if (++_lineTicks >= _settings.LineBaseTicks + (CurrentLine.Text.Length * _settings.LineTicksPerChar)) { NextLine(); }
        }

        private void StartNew()
        {
            _world = new World(_book, _worldSettings);
            _lines.Clear();
            CurrentLine = null;
            ClearSelection();
            TopicCursor = 0;
            SetScreen(FlowScreen.Playing);
        }

        private void ShowResult()
        {
            if (Screen != FlowScreen.Result) { SetScreen(FlowScreen.Result); }
        }

        private void Resume() => SetScreen(FlowScreen.Playing);

        private void ToTitle()
        {
            _world = null;
            _lines.Clear();
            CurrentLine = null;
            ClearSelection();
            MenuCursor = TitleNew;
            SetScreen(FlowScreen.Title);
        }

        private void SetScreen(FlowScreen screen)
        {
            Screen = screen;
            Version++;
        }
    }
}
