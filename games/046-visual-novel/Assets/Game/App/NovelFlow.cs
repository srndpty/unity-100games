using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core.Runtime;
using Game.Core.Save;

namespace Game.App
{
    public enum FlowScreen
    {
        Title,
        HowTo,
        Reading,
        Paused,
        Result,
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

    /// <summary>結果画面の結末の一覧の 1 行。</summary>
    public sealed class EndingEntry
    {
        public EndingEntry(string name, bool reached, bool current)
        {
            Name = name;
            Reached = reached;
            Current = current;
        }

        public string Name { get; }
        public bool Reached { get; }

        /// <summary>今回たどり着いた結末か。</summary>
        public bool Current { get; }
    }

    /// <summary>
    /// 画面の流れ（タイトル → 読む → 結果 → タイトル）と、読むときの文字送り・選択・既読スキップ・クイックセーブ。
    /// 実行器（<see cref="ScriptRunner"/>）を動かすのはここだけで、Unity 層には表示用の値だけを見せる。
    /// 入力は 1 本の列で順に当てる（012）。時間は Tick で数え、実時間は <see cref="UpdateFrame"/> で Tick に直す。
    /// </summary>
    public sealed class NovelFlow
    {
        public const int TitleNew = 0;
        public const int TitleContinue = 1;
        public const int TitleHowTo = 2;
        public const int TitleQuit = 3;
        public const int PauseResume = 0;
        public const int PauseTitle = 1;

        private readonly CompiledScript _script;
        private readonly ISaveStore _quickStore;
        private readonly ISaveStore _readStore;
        private readonly FlowSettings _settings;
        private readonly Queue<Intent> _intents = new();
        private readonly List<MenuItem> _titleItems = new();
        private readonly ReadOnlyCollection<MenuItem> _pauseItems;
        private readonly ReadLog _log;
        private ScriptRunner? _runner;
        private bool _readDirty;
        private int _skipHeld;
        private int _skipTimer;
        private int _revealTicks;
        private bool _revealedAll;
        private double _accumulator;
        private long _noticeUntil;
        private int _lastEnding = -1;

        public NovelFlow(CompiledScript script, ISaveStore quickStore, ISaveStore readStore, FlowSettings? settings = null)
        {
            _script = script ?? throw new ArgumentNullException(nameof(script));
            _quickStore = quickStore ?? throw new ArgumentNullException(nameof(quickStore));
            _readStore = readStore ?? throw new ArgumentNullException(nameof(readStore));
            _settings = settings ?? new FlowSettings();
            var errors = _settings.Validate();
            if (errors.Count > 0) { throw new ArgumentException(string.Join("\n", errors), nameof(settings)); }
            _pauseItems = Array.AsReadOnly(new[] { new MenuItem("つづける", true), new MenuItem("タイトルへ", true) });
            _log = LoadReadLog();
            RefreshTitle();
        }

        public FlowScreen Screen { get; private set; } = FlowScreen.Title;

        /// <summary>画面の版。画面・待ち（文・選択肢）が変わるたびに増える。クリックはこの版を持って送る。</summary>
        public int Version { get; private set; }

        public long Ticks { get; private set; }

        public AudioCues Cues { get; } = new();

        /// <summary>案内の文（出していなければ空）。</summary>
        public string Notice { get; private set; } = "";

        public bool QuitRequested { get; private set; }

        public int MenuCursor { get; private set; }

        public IReadOnlyList<MenuItem> TitleItems => _titleItems.AsReadOnly();

        public IReadOnlyList<MenuItem> PauseItems => _pauseItems;

        public IReadOnlyList<MenuItem> MenuItems => Screen == FlowScreen.Paused ? _pauseItems : TitleItems;

        public bool HasFocus { get; private set; } = true;

        // ---- 読む画面に出すもの ----

        public bool InStory => _runner != null && Screen is FlowScreen.Reading or FlowScreen.Paused;

        public bool IsChoice => InStory && _runner!.Phase == RunnerPhase.Choice;

        public string SpeakerName => InStory && !IsChoice && _runner!.Current.Speaker >= 0 ? _script.Characters[_runner.Current.Speaker].Name : "";

        /// <summary>話者の名前の色（0xRRGGBB、地の文は 0）。</summary>
        public uint SpeakerColor => InStory && !IsChoice && _runner!.Current.Speaker >= 0 ? _script.Characters[_runner.Current.Speaker].Color : 0;

        public string FullText => InStory && !IsChoice ? _runner!.Current.Text : "";

        public int VisibleChars => IsRevealed ? FullText.Length : Math.Min(FullText.Length, 1 + (_revealTicks / Math.Max(1, _settings.TicksPerChar)));

        public string VisibleText => FullText.Substring(0, VisibleChars);

        public bool IsRevealed => _revealedAll || _settings.TicksPerChar == 0 || (SkipActive && IsCurrentRead) ||
            (1 + (_revealTicks / Math.Max(1, _settings.TicksPerChar))) >= FullText.Length;

        /// <summary>今の文が既読か（既読の文は色を変えて出す）。</summary>
        public bool IsCurrentRead => InStory && !IsChoice && _log.IsRead(_runner!.Current.TextId);

        public IReadOnlyList<string> Options
        {
            get
            {
                var list = new List<string>();
                if (IsChoice)
                {
                    foreach (var o in _runner!.Current.Options) { list.Add(o.Text); }
                }

                return list.AsReadOnly();
            }
        }

        public int ChoiceCursor { get; private set; }

        public string Background => InStory ? _runner!.Background : "";

        /// <summary>立ち絵の枠の中身（空なら人物の ID が空）。</summary>
        public (string Character, string Expression) Portrait(StageSlot slot)
        {
            if (!InStory) { return ("", ""); }
            var p = _runner!.Slot(slot);
            return p.IsEmpty ? ("", "") : (_script.Characters[p.Character].Id, p.Expression);
        }

        public bool WindowHidden { get; private set; }

        public bool SkipToggled { get; private set; }

        /// <summary>スキップが効いているか（切り替えか押し続け、読む画面で枠を出しているとき）。</summary>
        public bool SkipActive => (SkipToggled || _skipHeld != 0) && Screen == FlowScreen.Reading && !WindowHidden && _runner != null;

        public int ReadCount => _log.ReadCount;

        public int TextCount => _log.TextCount;

        // ---- 結果画面 ----

        public string EndingName => _lastEnding >= 0 ? _script.Endings[_lastEnding].Name : "";

        public IReadOnlyList<EndingEntry> Endings
        {
            get
            {
                var list = new List<EndingEntry>();
                for (int i = 0; i < _script.Endings.Count; i++)
                {
                    list.Add(new EndingEntry(_script.Endings[i].Name, _log.Reached(i), i == _lastEnding && Screen == FlowScreen.Result));
                }

                return list.AsReadOnly();
            }
        }

        public int EndingsReached => _log.EndingsReached;

        /// <summary>話の状態のハッシュ（話の外なら 0）。</summary>
        public uint StoryHash => _runner?.Hash() ?? 0;

        internal ScriptRunner? Runner => _runner;

        internal ReadLog Log => _log;

        // ---- 入力 ----

        public void Enqueue(Intent intent) => _intents.Enqueue(intent);

        public void Press(InputButton button, int keySlot = 0) => Enqueue(Intent.Press(button, keySlot));

        public void Release(InputButton button, int keySlot = 0) => Enqueue(Intent.Release(button, keySlot));

        /// <summary>押して離す（テスト・操縦役）。</summary>
        public void Tap(InputButton button)
        {
            Press(button, 7);
            Release(button, 7);
        }

        /// <summary>今の画面の項目をクリックする（テスト・操縦役）。</summary>
        public void Click(int index) => Enqueue(Intent.Click(index, Version));

        /// <summary>実時間で進める。たまった入力を順に当ててから、経過時間ぶんの Tick を進める（1 フレームの上限あり）。</summary>
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

        /// <summary>フォーカスを失ったら保留の入力と押し続けのスキップを捨て、切り替えのスキップも止める（復帰しても手で再開する）。</summary>
        public void OnFocusChanged(bool hasFocus)
        {
            HasFocus = hasFocus;
            if (hasFocus) { return; }
            _intents.Clear();
            _skipHeld = 0;
            SkipToggled = false;
        }

        /// <summary>既読の記録を書く（変わっていなければ書かない）。選択・結末・タイトルへ戻る・終了のときに呼ぶ。</summary>
        public void SaveReadLog()
        {
            if (!_readDirty) { return; }
            if (_readStore.TryWrite(NovelSaveCodec.EncodeRead(_log), out var failure))
            {
                _readDirty = false;
            }
            else
            {
                ShowNotice("既読の記録を保存できませんでした：" + failure);
            }
        }

        // ---- 内部 ----

        private void Apply(Intent intent)
        {
            if (intent.Button == InputButton.SkipHold)
            {
                var bit = 1 << (intent.KeySlot & 31);
                _skipHeld = intent.Pressed ? _skipHeld | bit : _skipHeld & ~bit;
                return;
            }

            if (!intent.Pressed) { return; }
            if (intent.Button == InputButton.Click)
            {
                if (intent.Version != Version) { return; }
                Pointer(intent.Index);
                return;
            }

            switch (Screen)
            {
                case FlowScreen.Title:
                    Menu(intent.Button, TitleItems.Count, ConfirmTitle);
                    if (intent.Button == InputButton.QuickLoad) { LoadQuick(); }
                    break;
                case FlowScreen.Paused:
                    if (intent.Button == InputButton.Cancel) { Resume(); }
                    else { Menu(intent.Button, _pauseItems.Count, ConfirmPause); }
                    break;
                case FlowScreen.HowTo:
                case FlowScreen.Result:
                    if (intent.Button is InputButton.Confirm or InputButton.Cancel) { ToTitle(); }
                    break;
                case FlowScreen.Reading:
                    Reading(intent.Button);
                    break;
                default:
                    throw new InvalidOperationException($"未知の画面 {Screen}");
            }
        }

        private void Pointer(int index)
        {
            switch (Screen)
            {
                case FlowScreen.Title:
                case FlowScreen.Paused:
                    if (index < 0 || index >= MenuItems.Count) { return; }
                    MenuCursor = index;
                    if (Screen == FlowScreen.Title) { ConfirmTitle(); }
                    else { ConfirmPause(); }
                    break;
                case FlowScreen.HowTo:
                case FlowScreen.Result:
                    ToTitle();
                    break;
                case FlowScreen.Reading:
                    if (WindowHidden) { WindowHidden = false; return; }
                    if (IsChoice)
                    {
                        if (index >= 0 && index < _runner!.Current.Options.Count) { Choose(index); }
                    }
                    else
                    {
                        Confirm();
                    }

                    break;
                default:
                    throw new InvalidOperationException($"未知の画面 {Screen}");
            }
        }

        private void Menu(InputButton button, int count, Action confirm)
        {
            switch (button)
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

            switch (MenuCursor)
            {
                case TitleNew:
                    StartNew();
                    break;
                case TitleContinue:
                    LoadQuick();
                    break;
                case TitleHowTo:
                    SetScreen(FlowScreen.HowTo);
                    break;
                case TitleQuit:
                    SaveReadLog();
                    QuitRequested = true;
                    break;
                default:
                    throw new InvalidOperationException($"未知の項目 {MenuCursor}");
            }

            Cues.Choose++;
        }

        private void ConfirmPause()
        {
            if (MenuCursor == PauseResume) { Resume(); }
            else { ToTitle(); }
            Cues.Choose++;
        }

        private void Reading(InputButton button)
        {
            if (button == InputButton.HideWindow)
            {
                WindowHidden = !WindowHidden;
                return;
            }

            if (WindowHidden)
            {
                if (button is InputButton.Confirm or InputButton.Cancel) { WindowHidden = false; }
                return;
            }

            switch (button)
            {
                case InputButton.Cancel:
                    SkipToggled = false;
                    MenuCursor = PauseResume;
                    SetScreen(FlowScreen.Paused);
                    break;
                case InputButton.SkipToggle:
                    SkipToggled = !SkipToggled;
                    _skipTimer = 0;
                    break;
                case InputButton.QuickSave:
                    SaveQuick();
                    break;
                case InputButton.QuickLoad:
                    LoadQuick();
                    break;
                case InputButton.Up:
                    if (IsChoice)
                    {
                        ChoiceCursor = (ChoiceCursor + Options.Count - 1) % Options.Count;
                        Cues.Cursor++;
                    }

                    break;
                case InputButton.Down:
                    if (IsChoice)
                    {
                        ChoiceCursor = (ChoiceCursor + 1) % Options.Count;
                        Cues.Cursor++;
                    }
                    else
                    {
                        Confirm();
                    }

                    break;
                case InputButton.Confirm:
                    if (IsChoice) { Choose(ChoiceCursor); }
                    else { Confirm(); }
                    break;
                default:
                    break;
            }
        }

        /// <summary>文の待ちでの決定：表示途中なら全文を出し、出し終えていれば送る。</summary>
        private void Confirm()
        {
            if (!IsRevealed)
            {
                _revealedAll = true;
                return;
            }

            AdvanceText();
        }

        private void AdvanceText()
        {
            if (_log.MarkRead(_runner!.Current.TextId)) { _readDirty = true; }
            var r = _runner.Advance();
            if (!r.Accepted) { throw new InvalidOperationException(r.Reason); }
            Cues.Advance++;
            Moved();
        }

        private void Choose(int option)
        {
            var r = _runner!.Choose(option);
            if (!r.Accepted)
            {
                Cues.Reject++;
                return;
            }

            Cues.Choose++;
            Moved();
            SaveReadLog();
        }

        /// <summary>実行器が次の待ちへ進んだ。文字送りをやり直し、選択肢・未読・結末でスキップを止める。</summary>
        private void Moved()
        {
            Version++;
            _revealTicks = 0;
            _revealedAll = false;
            _skipTimer = 0;
            ChoiceCursor = 0;
            switch (_runner!.Phase)
            {
                case RunnerPhase.Ended:
                    _lastEnding = _runner.EndingIndex;
                    _log.MarkEnding(_lastEnding);
                    _readDirty = true;
                    SaveReadLog();
                    SkipToggled = false;
                    Cues.Ending++;
                    SetScreen(FlowScreen.Result);
                    break;
                case RunnerPhase.Choice:
                    SkipToggled = false;
                    break;
                default:
                    if (!_log.IsRead(_runner.Current.TextId)) { SkipToggled = false; }
                    break;
            }
        }

        private void TickOnce()
        {
            Ticks++;
            if (Notice.Length > 0 && Ticks >= _noticeUntil) { Notice = ""; }
            if (Screen != FlowScreen.Reading || _runner == null || _runner.Phase != RunnerPhase.Text) { return; }
            if (!_revealedAll) { _revealTicks++; }
            if (!SkipActive) { return; }
            if (!IsCurrentRead)
            {
                // 未読の文では止まる。押し続けのスキップは離すまで効かないだけで、文字送りは普通に進む。
                SkipToggled = false;
                return;
            }

            if (++_skipTimer >= _settings.SkipIntervalTicks)
            {
                _skipTimer = 0;
                AdvanceText();
            }
        }

        private void StartNew()
        {
            _runner = new ScriptRunner(_script);
            _lastEnding = -1;
            WindowHidden = false;
            SkipToggled = false;
            SetScreen(FlowScreen.Reading);
            Moved();
        }

        private void SaveQuick()
        {
            if (_runner == null || _runner.Phase == RunnerPhase.Ended) { return; }
            if (_quickStore.TryWrite(NovelSaveCodec.EncodeQuick(_runner), out var failure))
            {
                Cues.Save++;
                ShowNotice("クイックセーブしました");
            }
            else
            {
                Cues.Reject++;
                ShowNotice("クイックセーブできませんでした：" + failure);
            }
        }

        private void LoadQuick()
        {
            if (!TryReadQuick(out var runner, out var reason))
            {
                Cues.Reject++;
                ShowNotice(reason);
                return;
            }

            _runner = runner;
            _lastEnding = -1;
            WindowHidden = false;
            SkipToggled = false;
            SetScreen(FlowScreen.Reading);
            Moved();
            Cues.Load++;
            ShowNotice("クイックロードしました");
        }

        private bool TryReadQuick(out ScriptRunner? runner, out string reason)
        {
            runner = null;
            switch (_quickStore.TryRead(out var content, out var failure))
            {
                case SaveReadStatus.Missing:
                    reason = "クイックセーブがありません";
                    return false;
                case SaveReadStatus.Failed:
                    reason = "クイックセーブを読めませんでした：" + failure;
                    return false;
                default:
                    break;
            }

            if (!NovelSaveCodec.TryDecodeQuick(_script, content, out runner, out var error))
            {
                reason = "クイックセーブを読めませんでした：" + error;
                return false;
            }

            reason = "";
            return true;
        }

        private ReadLog LoadReadLog()
        {
            switch (_readStore.TryRead(out var content, out var failure))
            {
                case SaveReadStatus.Missing:
                    return new ReadLog(_script);
                case SaveReadStatus.Failed:
                    ShowNotice("既読の記録を読めなかったので、空から始めます：" + failure);
                    return new ReadLog(_script);
                default:
                    break;
            }

            if (NovelSaveCodec.TryDecodeRead(_script, content, out var log, out var error)) { return log!; }
            ShowNotice("既読の記録を読めなかったので、空から始めます：" + error);
            return new ReadLog(_script);
        }

        private void RefreshTitle()
        {
            _titleItems.Clear();
            _titleItems.Add(new MenuItem("はじめから", true));
            var canContinue = TryReadQuick(out _, out var reason);
            _titleItems.Add(new MenuItem("つづきから", canContinue, canContinue ? "" : reason));
            _titleItems.Add(new MenuItem("操作説明", true));
            _titleItems.Add(new MenuItem("終了", true));
        }

        private void Resume()
        {
            SetScreen(FlowScreen.Reading);
        }

        private void ToTitle()
        {
            SaveReadLog();
            _runner = null;
            _skipHeld = 0;
            SkipToggled = false;
            WindowHidden = false;
            _lastEnding = -1;
            RefreshTitle();
            MenuCursor = TitleNew;
            SetScreen(FlowScreen.Title);
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
