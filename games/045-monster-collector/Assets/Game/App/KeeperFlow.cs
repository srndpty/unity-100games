using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    public enum FlowScreen
    {
        Title = 1,
        HowTo = 2,
        StarterPick = 3,
        Field = 4,
        FieldMenu = 5,
        Party = 6,
        Dex = 7,
        CenterMenu = 8,
        Storage = 9,
        Battle = 10,
        LearnMove = 11,
        Result = 12,
    }

    /// <summary>戦闘画面のメニューの段。<see cref="Playing"/> は出来事を見せている間（入力は文送りだけ）。</summary>
    public enum BattleMenu
    {
        Playing = 0,
        Root = 1,
        Moves = 2,
        Switch = 3,
        Items = 4,
        PotionTarget = 5,

        /// <summary>倒れた味方の交代先を選ぶ（取り消せない）。</summary>
        ForcedSwitch = 6,
    }

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

        public string Detail { get; }
    }

    /// <summary>遊んで決める値（Unity では設定アセットから渡す）。</summary>
    public sealed class FlowSettings
    {
        public int TickRate { get; set; } = 60;
        public int MaxTicksPerFrame { get; set; } = 6;

        /// <summary>文のある出来事を見せる Tick（決定キーで送れる）。</summary>
        public int TextTicks { get; set; } = 54;

        /// <summary>HP だけが動く出来事の Tick。</summary>
        public int QuietTicks { get; set; } = 16;

        /// <summary>文が出てから送れるようになるまでの Tick（連打で読み飛ばしすぎない）。</summary>
        public int MinTicks { get; set; } = 6;
    }

    /// <summary>
    /// 画面の遷移とユースケース。入力（意図）を受けてメニューを動かし、Core の世界・戦闘へコマンドを渡す。
    /// 時間は Tick で進み、戦闘の出来事は <see cref="BattlePresenter"/> が Tick で見せる。
    /// </summary>
    public sealed class KeeperFlow
    {
        /// <summary>ぶつかる音の最短の間隔（Tick）。</summary>
        public const int BumpCooldownTicks = 20;

        private readonly ISaveStore _save;
        private readonly ISaveStore _records;
        private readonly FlowSettings _settings;
        private readonly RandomSeries _seedRng;
        private readonly Queue<Intent> _intents = new();
        private readonly Queue<string> _dialogue = new();
        private readonly int[] _heldSlots = new int[4];
        private readonly List<Direction> _heldOrder = new();
        private List<MenuItem> _items = new();
        private Action? _afterDialogue;
        private double _accumulator;
        private int _swapFrom = -1;
        private Direction? _tapStep;
        private int _bumpCooldown;

        public KeeperFlow(MonsterData data, ISaveStore save, ISaveStore records, int initialSeed, FlowSettings? settings = null)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _records = records ?? throw new ArgumentNullException(nameof(records));
            _settings = settings ?? new FlowSettings();
            _seedRng = new RandomSeries(initialSeed);
            Screen = FlowScreen.Title;
            RefreshTitle();
        }

        public MonsterData Data { get; }

        public World? World { get; private set; }

        public BattlePresenter? Presenter { get; private set; }

        public FlowScreen Screen { get; private set; }

        public BattleMenu BattleMenu { get; private set; }

        public IReadOnlyList<MenuItem> Items => _items;

        public int Cursor { get; private set; }

        /// <summary>今出している会話・案内の文（なければ空）。決定キーで送る。</summary>
        public string Dialogue { get; private set; } = string.Empty;

        public bool InDialogue => Dialogue.Length > 0;

        /// <summary>メニューの見出し（覚えたい技の案内など）。</summary>
        public string Heading { get; private set; } = string.Empty;

        /// <summary>拒否した理由などの短い案内（次の操作で消える）。</summary>
        public string Notice { get; private set; } = string.Empty;

        public bool IsPaused { get; private set; }

        public bool HasFocus { get; private set; } = true;

        public bool HasSave { get; private set; }

        /// <summary>タイトルの案内（セーブが読めない理由など）。</summary>
        public string TitleNotice { get; private set; } = string.Empty;

        public Records Records { get; private set; } = Records.Empty;

        /// <summary>結果画面に出したクリアの記録（この回）。</summary>
        public long ResultTicks { get; private set; }

        public int SwapFrom => _swapFrom;

        public AudioCues Cues { get; } = new();

        public long TotalTicks { get; private set; }

        /// <summary>押されている方向（最後に押した方向が優先）。</summary>
        public Direction? HeldDirection => _heldOrder.Count == 0 ? null : _heldOrder[_heldOrder.Count - 1];

        // --- 入力 ---

        public void Enqueue(Intent intent) => _intents.Enqueue(intent);

        public void Press(InputButton button, int keySlot = 0) => Enqueue(new Intent(button, keySlot, true));

        public void Release(InputButton button, int keySlot = 0) => Enqueue(new Intent(button, keySlot, false));

        /// <summary>押して離す（テスト・操縦役）。</summary>
        public void Tap(InputButton button)
        {
            Press(button, 7);
            Release(button, 7);
        }

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

            StepTicks(ticks);
        }

        /// <summary>
        /// 自動プレイの間の人の入力：一時停止（P）と、一時停止中の再開（P・決定）だけを当て、他のキーと押しているキーは捨てる。
        /// </summary>
        public void ApplyPauseInputOnly()
        {
            while (_intents.Count > 0)
            {
                var intent = _intents.Dequeue();
                if (!intent.Pressed || !InGame) { continue; }
                if (intent.Button == InputButton.Pause) { IsPaused = !IsPaused; }
                else if (intent.Button == InputButton.Confirm && IsPaused) { IsPaused = false; }
            }

            ClearHeld();
        }

        /// <summary>一時停止を解く（眺める自動プレイを始めたとき）。</summary>
        public void Resume() => IsPaused = false;

        public void FlushIntents()
        {
            while (_intents.Count > 0) { Apply(_intents.Dequeue()); }
        }

        public void StepTicks(int ticks)
        {
            FlushIntents();
            for (int i = 0; i < ticks; i++) { TickOnce(); }
        }

        /// <summary>フォーカスを失ったら一時停止し、押されているキーと保留の入力を捨てる。復帰しても手で再開する。</summary>
        public void OnFocusChanged(bool hasFocus)
        {
            HasFocus = hasFocus;
            if (hasFocus) { return; }
            _intents.Clear();
            ClearHeld();
            if (InGame) { IsPaused = true; }
        }

        private bool InGame => Screen != FlowScreen.Title && Screen != FlowScreen.HowTo && Screen != FlowScreen.Result;

        private void ClearHeld()
        {
            Array.Clear(_heldSlots, 0, _heldSlots.Length);
            _heldOrder.Clear();
            _tapStep = null;
            World?.Explorer.ClearBufferedInput();
        }

        private void Apply(Intent intent)
        {
            if (intent.Button <= InputButton.Left)
            {
                var d = (int)intent.Button;
                var bit = 1 << (intent.KeySlot & 31);
                var dir = (Direction)d;
                if (intent.Pressed)
                {
                    var was = _heldSlots[d] != 0;
                    _heldSlots[d] |= bit;
                    if (!was)
                    {
                        _heldOrder.Remove(dir);
                        _heldOrder.Add(dir);
                        if (!IsPaused) { OnDirection(dir); }
                    }
                }
                else
                {
                    _heldSlots[d] &= ~bit;
                    if (_heldSlots[d] == 0) { _heldOrder.Remove(dir); }
                }

                return;
            }

            if (!intent.Pressed) { return; }
            if (intent.Button == InputButton.Pause)
            {
                if (InGame)
                {
                    IsPaused = !IsPaused;
                    ClearHeld();
                }

                return;
            }

            if (IsPaused)
            {
                // 一時停止は決定キーでも戻せる（手で再開する）。
                if (intent.Button == InputButton.Confirm) { IsPaused = false; }
                return;
            }

            if (intent.Button == InputButton.Confirm) { OnConfirm(); }
            else if (intent.Button == InputButton.Cancel) { OnCancel(); }
        }

        // --- Tick ---

        private void TickOnce()
        {
            TotalTicks++;
            if (IsPaused) { return; }
            if (World != null && InGame) { World.AddPlayTicks(1); }
            switch (Screen)
            {
                case FlowScreen.Field:
                    if (!InDialogue) { TickField(); }
                    break;
                case FlowScreen.Battle:
                    TickBattle();
                    break;
            }
        }

        private void TickField()
        {
            var w = World!;
            var step = w.FieldTick(HeldDirection ?? _tapStep);
            _tapStep = null;
            // 壁に向かって押し続けても、ぶつかる音は一定の Tick に 1 回だけ鳴らす。
            if (_bumpCooldown > 0) { _bumpCooldown--; }
            if (step == FieldStep.Blocked && !w.Explorer.IsMoving && _bumpCooldown == 0)
            {
                Cues.Bump++;
                _bumpCooldown = BumpCooldownTicks;
            }
            if (step == FieldStep.Encounter) { EnterBattle(); }
        }

        private void TickBattle()
        {
            var p = Presenter!;
            p.Tick();
            if (BattleMenu != BattleMenu.Playing || !p.IsIdle) { return; }
            var battle = World!.Battle!;
            if (battle.IsOver)
            {
                AfterBattle();
                return;
            }

            OpenBattleMenu(battle.Phase == BattlePhase.ForcedSwitch ? BattleMenu.ForcedSwitch : BattleMenu.Root);
        }

        // --- 入力の当てはめ ---

        private void OnDirection(Direction dir)
        {
            if (InDialogue) { return; }
            if (Screen == FlowScreen.Field)
            {
                // 歩いている間は 1 つだけ先行入力、止まっていれば次の Tick で歩き出す（同じフレームで離しても 1 歩は歩く）。
                if (World!.Explorer.IsMoving) { World.Explorer.Buffer(dir); }
                else { _tapStep = dir; }
                return;
            }

            if (_items.Count == 0) { return; }
            if (dir == Direction.Up || dir == Direction.Left) { MoveCursor(-1); }
            else { MoveCursor(1); }
        }

        private void MoveCursor(int delta)
        {
            Cursor = (Cursor + delta + _items.Count) % _items.Count;
            Cues.Cursor++;
        }

        private void OnConfirm()
        {
            Notice = string.Empty;
            if (InDialogue)
            {
                AdvanceDialogue();
                return;
            }

            switch (Screen)
            {
                case FlowScreen.Title: ConfirmTitle(); break;
                case FlowScreen.HowTo: GoTitle(); break;
                case FlowScreen.StarterPick: ConfirmStarter(); break;
                case FlowScreen.Field: Interact(); break;
                case FlowScreen.FieldMenu: ConfirmFieldMenu(); break;
                case FlowScreen.Party: ConfirmParty(); break;
                case FlowScreen.Dex: OpenFieldMenu(); break;
                case FlowScreen.CenterMenu: ConfirmCenter(); break;
                case FlowScreen.Storage: ConfirmStorage(); break;
                case FlowScreen.Battle: ConfirmBattle(); break;
                case FlowScreen.LearnMove: ConfirmLearn(); break;
                case FlowScreen.Result: GoTitle(); break;
            }
        }

        private void OnCancel()
        {
            Notice = string.Empty;
            if (InDialogue) { return; }
            switch (Screen)
            {
                case FlowScreen.HowTo: GoTitle(); break;
                case FlowScreen.Field: OpenFieldMenu(); break;
                case FlowScreen.FieldMenu: CloseToField(); break;
                case FlowScreen.Party:
                    if (_swapFrom >= 0)
                    {
                        _swapFrom = -1;
                        OpenParty();
                    }
                    else
                    {
                        OpenFieldMenu();
                    }

                    break;
                case FlowScreen.Dex: OpenFieldMenu(); break;
                case FlowScreen.CenterMenu: CloseToField(); break;
                case FlowScreen.Storage: OpenCenterMenu(); break;
                case FlowScreen.Battle: CancelBattle(); break;
                default: return;
            }

            Cues.Cancel++;
        }

        private void Reject(string notice)
        {
            Notice = notice;
            Cues.Reject++;
        }

        private bool CursorEnabled(out int index)
        {
            index = Cursor;
            if (index < 0 || index >= _items.Count) { return false; }
            if (!_items[index].Enabled)
            {
                Cues.Reject++;
                return false;
            }

            Cues.Confirm++;
            return true;
        }

        // --- 会話 ---

        private void Say(IEnumerable<string> lines, Action? then)
        {
            foreach (var l in lines)
            {
                if (l.Length > 0) { _dialogue.Enqueue(l); }
            }

            _afterDialogue = then;
            if (Dialogue.Length == 0) { AdvanceDialogue(); }
        }

        private void Say(string line, Action? then) => Say(new List<string> { line }, then);

        private void AdvanceDialogue()
        {
            if (_dialogue.Count > 0)
            {
                Dialogue = _dialogue.Dequeue();
                return;
            }

            Dialogue = string.Empty;
            var then = _afterDialogue;
            _afterDialogue = null;
            then?.Invoke();
        }

        // --- タイトル ---

        private enum TitleOption
        {
            NewGame,
            Continue,
            HowTo,
        }

        private readonly List<TitleOption> _titleOptions = new();

        /// <summary>セーブと記録を読み直してタイトルの項目を作る。</summary>
        public void RefreshTitle()
        {
            HasSave = false;
            TitleNotice = string.Empty;
            switch (_save.TryRead(out var content, out var failure))
            {
                case SaveReadStatus.Loaded:
                    if (SaveCodec.TryDecode(Data, content, out _, out var error)) { HasSave = true; }
                    else { TitleNotice = $"セーブデータを読み込めません（{error!.Message}）"; }
                    break;
                case SaveReadStatus.Failed:
                    TitleNotice = $"セーブデータを読み込めません（{failure}）";
                    break;
            }

            Records = Records.Empty;
            if (_records.TryRead(out var rec, out _) == SaveReadStatus.Loaded && Records.TryDecode(rec, Data.Species.Count, out var r, out _))
            {
                Records = r;
            }

            _titleOptions.Clear();
            _titleOptions.Add(TitleOption.NewGame);
            if (HasSave) { _titleOptions.Add(TitleOption.Continue); }
            _titleOptions.Add(TitleOption.HowTo);
            if (Screen == FlowScreen.Title) { BuildTitleItems(); }
        }

        private void BuildTitleItems()
        {
            _items = new List<MenuItem>();
            foreach (var o in _titleOptions)
            {
                _items.Add(new MenuItem(o switch { TitleOption.NewGame => "はじめから", TitleOption.Continue => "つづきから", _ => "あそびかた" }, true));
            }

            Cursor = HasSave ? 1 : 0;
        }

        private void GoTitle()
        {
            Screen = FlowScreen.Title;
            World = null;
            Presenter = null;
            IsPaused = false;
            Dialogue = string.Empty;
            _dialogue.Clear();
            _afterDialogue = null;
            ClearHeld();
            RefreshTitle();
            BuildTitleItems();
        }

        private void ConfirmTitle()
        {
            if (!CursorEnabled(out var i)) { return; }
            switch (_titleOptions[i])
            {
                case TitleOption.NewGame:
                    StartNewGame(unchecked((int)_seedRng.NextUInt()));
                    break;
                case TitleOption.Continue:
                    ContinueGame();
                    break;
                default:
                    Screen = FlowScreen.HowTo;
                    _items = new List<MenuItem>();
                    break;
            }
        }

        /// <summary>はじめから（シードを決めて）。再戦は毎回新しいシード。</summary>
        public void StartNewGame(int seed)
        {
            World = World.New(Data, seed);
            Presenter = null;
            Screen = FlowScreen.StarterPick;
            _items = new List<MenuItem>();
            foreach (var s in Data.Rules.Starters) { _items.Add(new MenuItem(s.Name, true, s.Type.Name)); }
            Cursor = 0;
            Heading = "相棒にする 1 匹を えらぶ";
        }

        private void ContinueGame()
        {
            if (_save.TryRead(out var content, out var failure) != SaveReadStatus.Loaded || !SaveCodec.TryDecode(Data, content, out var world, out var error))
            {
                Reject("セーブデータを読み込めません");
                RefreshTitle();
                return;
            }

            World = world!;
            Presenter = null;
            CloseToField();
            Say("回復所の前から 冒険を つづける。", null);
        }

        private void ConfirmStarter()
        {
            if (!CursorEnabled(out var i)) { return; }
            var species = Data.Rules.Starters[i];
            World!.ChooseStarter(i);
            CloseToField();
            Say(new List<string> { $"{species.Name}を 相棒に した！", "茨野ノ道の 奥へ すすみ、道を ふさぐ トレーナー 3 人に 勝とう。" }, null);
        }

        // --- フィールド ---

        private void CloseToField()
        {
            Screen = FlowScreen.Field;
            _items = new List<MenuItem>();
            Heading = string.Empty;
            _swapFrom = -1;
        }

        private void Interact()
        {
            var kind = World!.Interact(out var trainer);
            switch (kind)
            {
                case InteractKind.Trainer:
                    Cues.Confirm++;
                    Say($"{trainer!.Name}「{trainer.Intro}」", EnterBattle);
                    break;
                case InteractKind.Center:
                    Cues.Heal++;
                    Say(new List<string> { "回復所「みんな 元気に なりましたよ。」", "捕獲玉と 傷薬を 補充した。" }, OpenCenterMenu);
                    break;
            }
        }

        private void OpenFieldMenu()
        {
            Screen = FlowScreen.FieldMenu;
            _items = new List<MenuItem> { new("てもち", true), new("ずかん", true), new("とじる", true) };
            Cursor = 0;
            Heading = string.Empty;
            _swapFrom = -1;
            ClearHeld();
        }

        private void ConfirmFieldMenu()
        {
            if (!CursorEnabled(out var i)) { return; }
            switch (i)
            {
                case 0: OpenParty(); break;
                case 1: OpenDex(); break;
                default: CloseToField(); break;
            }
        }

        private void OpenParty()
        {
            Screen = FlowScreen.Party;
            var keep = Cursor;
            _items = new List<MenuItem>();
            foreach (var m in World!.Roster.Party) { _items.Add(new MenuItem(MonLabel(m), true, MonDetail(m))); }
            Cursor = Math.Min(keep, _items.Count - 1);
            Heading = _swapFrom >= 0 ? "入れかえる 相手を えらぶ" : "先頭の 1 匹から 戦闘に 出る。決定で 並びを 入れかえる";
        }

        private void ConfirmParty()
        {
            if (!CursorEnabled(out var i)) { return; }
            if (_swapFrom < 0)
            {
                _swapFrom = i;
                OpenParty();
                return;
            }

            World!.SwapParty(_swapFrom, i);
            _swapFrom = -1;
            OpenParty();
        }

        private void OpenDex()
        {
            Screen = FlowScreen.Dex;
            _items = new List<MenuItem>();
            var w = World!;
            foreach (var s in Data.Species)
            {
                var caught = (w.Caught & (1 << s.Index)) != 0;
                var seen = (w.Seen & (1 << s.Index)) != 0;
                _items.Add(new MenuItem(seen ? s.Name : "？？？", true, caught ? $"{s.Type.Name}・つかまえた" : seen ? $"{s.Type.Name}・見た" : string.Empty));
            }

            Cursor = 0;
            Heading = $"つかまえた {w.CaughtCount} / {Data.Species.Count}";
        }

        // --- 回復所 ---

        private void OpenCenterMenu()
        {
            Screen = FlowScreen.CenterMenu;
            _items = new List<MenuItem> { new("記録する", true), new("ボックス", true), new("やめる", true) };
            Cursor = 0;
            Heading = "回復所";
        }

        private void ConfirmCenter()
        {
            if (!CursorEnabled(out var i)) { return; }
            switch (i)
            {
                case 0:
                    SaveHere();
                    break;
                case 1:
                    OpenStorage();
                    break;
                default:
                    CloseToField();
                    break;
            }
        }

        private void SaveHere()
        {
            if (!SaveCodec.TryEncode(World!, out var text, out var error))
            {
                Say(error!.Message, OpenCenterMenu);
                return;
            }

            if (!_save.TryWrite(text, out var failure))
            {
                Cues.Reject++;
                Say($"記録できませんでした（{failure}）", OpenCenterMenu);
                return;
            }

            Cues.Save++;
            Say("冒険を 記録した。", OpenCenterMenu);
        }

        private void OpenStorage()
        {
            Screen = FlowScreen.Storage;
            var keep = Cursor;
            var r = World!.Roster;
            _items = new List<MenuItem>();
            foreach (var m in r.Party) { _items.Add(new MenuItem("てもち " + MonLabel(m), true, "あずける")); }
            foreach (var m in r.Box) { _items.Add(new MenuItem("ボックス " + MonLabel(m), true, "ひきだす")); }
            _items.Add(new MenuItem("もどる", true));
            Cursor = Math.Min(Math.Max(0, keep), _items.Count - 1);
            Heading = $"てもち {r.Party.Count}/{r.PartyMax}　ボックス {r.Box.Count}/{r.BoxMax}";
        }

        private void ConfirmStorage()
        {
            if (!CursorEnabled(out var i)) { return; }
            var r = World!.Roster;
            if (i == _items.Count - 1)
            {
                OpenCenterMenu();
                return;
            }

            var error = i < r.Party.Count ? World.Deposit(i) : World.Withdraw(i - r.Party.Count);
            switch (error)
            {
                case RosterError.None:
                    break;
                case RosterError.PartyFull:
                    Reject("手持ちが いっぱいです");
                    break;
                case RosterError.BoxFull:
                    Reject("ボックスが いっぱいです");
                    break;
                case RosterError.LastHealthy:
                    Reject("戦える モンスターが いなくなります");
                    break;
                default:
                    Reject("えらべません");
                    break;
            }

            var notice = Notice;
            OpenStorage();
            Notice = notice;
        }

        // --- 戦闘 ---

        /// <summary>テスト用：世界で始めた戦闘を画面に出す。</summary>
        internal void EnterBattleForTest() => EnterBattle();

        private void EnterBattle()
        {
            var battle = World!.Battle!;
            Screen = FlowScreen.Battle;
            ClearHeld();
            Presenter = new BattlePresenter(battle.Kind, _settings.TextTicks, _settings.QuietTicks, _settings.MinTicks, Cues);
            Presenter.Enqueue(battle.Log);
            BattleMenu = BattleMenu.Playing;
            _items = new List<MenuItem>();
            Heading = string.Empty;
        }

        private void OpenBattleMenu(BattleMenu menu)
        {
            BattleMenu = menu;
            var b = World!.Battle!;
            var me = b.PlayerMonster;
            _items = new List<MenuItem>();
            switch (menu)
            {
                case BattleMenu.Root:
                    _items.Add(new MenuItem("たたかう", true));
                    _items.Add(new MenuItem("こうたい", b.HasHealthyBench()));
                    _items.Add(new MenuItem("どうぐ", true));
                    _items.Add(new MenuItem("にげる", b.Kind == BattleKind.Wild));
                    Heading = $"{me.Name}は どうする？";
                    break;
                case BattleMenu.Moves:
                    foreach (var s in me.Moves)
                    {
                        _items.Add(new MenuItem(s.Move.Name, s.Pp > 0, $"{MoveDetail(s.Move)}　PP {s.Pp}/{s.Move.Pp}"));
                    }

                    Heading = "わざを えらぶ";
                    break;
                case BattleMenu.Switch:
                case BattleMenu.ForcedSwitch:
                case BattleMenu.PotionTarget:
                    for (int i = 0; i < b.Party.Count; i++)
                    {
                        var m = b.Party[i];
                        var ok = menu == BattleMenu.PotionTarget ? !m.IsFainted && m.Hp < m.MaxHp : !m.IsFainted && i != b.Active;
                        _items.Add(new MenuItem(MonLabel(m), ok, MonDetail(m)));
                    }

                    Heading = menu == BattleMenu.PotionTarget ? "だれに 使う？" : menu == BattleMenu.ForcedSwitch ? "つぎに 出す モンスターを えらぶ" : "入れかえる モンスターを えらぶ";
                    break;
                case BattleMenu.Items:
                    _items.Add(new MenuItem("傷薬", b.Potions > 0, $"×{b.Potions}"));
                    _items.Add(new MenuItem("捕獲玉", b.Balls > 0 && b.Kind == BattleKind.Wild, $"×{b.Balls}"));
                    Heading = "どうぐを えらぶ";
                    break;
            }

            Cursor = 0;
            if (menu == BattleMenu.Switch || menu == BattleMenu.ForcedSwitch || menu == BattleMenu.PotionTarget)
            {
                var first = _items.FindIndex(x => x.Enabled);
                Cursor = Math.Max(0, first);
            }
        }

        private void ConfirmBattle()
        {
            if (BattleMenu == BattleMenu.Playing)
            {
                Presenter!.Skip();
                return;
            }

            var b = World!.Battle!;
            if (BattleMenu == BattleMenu.Root && Cursor == 3 && b.Kind == BattleKind.Trainer)
            {
                Reject("トレーナーとの 勝負から にげられない！");
                return;
            }

            if (BattleMenu == BattleMenu.Items && Cursor == 1 && b.Kind == BattleKind.Trainer)
            {
                Reject("トレーナーの モンスターは つかまえられない！");
                return;
            }

            if (!CursorEnabled(out var i)) { return; }
            switch (BattleMenu)
            {
                case BattleMenu.Root:
                    if (i == 0)
                    {
                        if (!b.PlayerMonster.HasUsableMove()) { Submit(BattleCommand.Fight(0)); }
                        else { OpenBattleMenu(BattleMenu.Moves); }
                    }
                    else if (i == 1) { OpenBattleMenu(BattleMenu.Switch); }
                    else if (i == 2) { OpenBattleMenu(BattleMenu.Items); }
                    else { Submit(BattleCommand.Run()); }
                    break;
                case BattleMenu.Moves:
                    Submit(BattleCommand.Fight(i));
                    break;
                case BattleMenu.Switch:
                case BattleMenu.ForcedSwitch:
                    Submit(BattleCommand.Switch(i));
                    break;
                case BattleMenu.Items:
                    if (i == 0) { OpenBattleMenu(BattleMenu.PotionTarget); }
                    else { Submit(BattleCommand.Ball()); }
                    break;
                case BattleMenu.PotionTarget:
                    Submit(BattleCommand.Potion(i));
                    break;
            }
        }

        private void CancelBattle()
        {
            switch (BattleMenu)
            {
                case BattleMenu.Moves:
                case BattleMenu.Switch:
                case BattleMenu.Items:
                    OpenBattleMenu(BattleMenu.Root);
                    break;
                case BattleMenu.PotionTarget:
                    OpenBattleMenu(BattleMenu.Items);
                    Cursor = 0;
                    break;
            }
        }

        /// <summary>戦闘のコマンドを Core へ渡す。拒否されたら理由を出してメニューに残る。</summary>
        private void Submit(BattleCommand command)
        {
            var result = World!.Battle!.Submit(command);
            if (!result.Accepted)
            {
                Reject(RejectText(result.Reason));
                return;
            }

            Presenter!.Enqueue(result.Events);
            BattleMenu = BattleMenu.Playing;
            _items = new List<MenuItem>();
            Heading = string.Empty;
        }

        public static string RejectText(RejectReason reason) => reason switch
        {
            RejectReason.NoPp => "PP が のこっていない！",
            RejectReason.NoRoom => "手持ちも ボックスも いっぱいで つかまえられない！",
            RejectReason.TrainerBattle => "トレーナーとの 勝負では できない！",
            RejectReason.FullHp => "HP は まんたんだ！",
            RejectReason.NoPotion => "傷薬が ない！",
            RejectReason.NoBall => "捕獲玉が ない！",
            RejectReason.MemberFainted => "たおれている！",
            RejectReason.AlreadyActive => "もう 出ている！",
            _ => "えらべない！",
        };

        private void AfterBattle()
        {
            var w = World!;
            var after = w.FinishBattle();
            Presenter = null;
            var lines = new List<string>();
            switch (after.Outcome)
            {
                case BattleOutcome.Captured:
                    lines.Add(after.Place == RosterPlace.Box
                        ? $"手持ちが いっぱいなので {after.Captured!.Name}を ボックスへ 送った。"
                        : $"{after.Captured!.Name}が 手持ちに くわわった！");
                    break;
                case BattleOutcome.Won:
                    if (after.Trainer != null) { lines.Add($"{after.Trainer.Name}「{after.Trainer.Defeat}」"); }
                    break;
                case BattleOutcome.Lost:
                    lines.Add("回復所へ もどり、モンスターたちを 休ませた……");
                    break;
            }

            CloseToField();
            if (after.Cleared)
            {
                lines.Add("茨野ノ道の トレーナー 3 人に 勝った！");
                lines.Add("あなたは 新しい 茨守りに なった。");
                Say(lines, ShowResult);
                return;
            }

            Say(lines, w.Mode == WorldMode.LearnMove ? OpenLearn : null);
        }

        // --- 技を覚える ---

        private void OpenLearn()
        {
            var w = World!;
            if (w.Mode != WorldMode.LearnMove || w.PendingLearns.Count == 0)
            {
                CloseToField();
                return;
            }

            var p = w.PendingLearns[0];
            var m = w.Roster.Party[p.Member];
            Screen = FlowScreen.LearnMove;
            _items = new List<MenuItem>();
            foreach (var s in m.Moves) { _items.Add(new MenuItem(s.Move.Name, true, MoveDetail(s.Move))); }
            _items.Add(new MenuItem($"{p.Move.Name}を おぼえない", true));
            Cursor = 0;
            Heading = $"{m.Name}は {p.Move.Name}（{MoveDetail(p.Move)}）を おぼえたい。わすれる 技を えらぶ";
        }

        private void ConfirmLearn()
        {
            if (!CursorEnabled(out var i)) { return; }
            var w = World!;
            var p = w.PendingLearns[0];
            var m = w.Roster.Party[p.Member];
            var skip = i >= m.Moves.Count;
            var old = skip ? string.Empty : m.Moves[i].Move.Name;
            w.ResolveLearn(skip ? -1 : i);
            CloseToField();
            Say(skip ? $"{m.Name}は {p.Move.Name}を おぼえなかった。" : $"{m.Name}は {old}を わすれて {p.Move.Name}を おぼえた！", OpenLearn);
        }

        // --- 結果 ---

        private void ShowResult()
        {
            var w = World!;
            ResultTicks = w.PlayTicks;
            Records = Records.WithClear(w.PlayTicks, w.CaughtCount);
            _records.TryWrite(Records.Encode(), out _);
            Screen = FlowScreen.Result;
            _items = new List<MenuItem> { new("タイトルへ", true) };
            Cursor = 0;
            Heading = string.Empty;
        }

        // --- 表示用 ---

        public static string MonLabel(Monster m) => $"{m.Name} Lv{m.Level}";

        /// <summary>技の属性（なしは「―」）と威力（威力のない技は「変化」）。</summary>
        public static string MoveDetail(MoveDef move) => $"{move.Type?.Name ?? "―"}・{(move.Power > 0 ? "威力 " + move.Power : "変化")}";

        public static string MonDetail(Monster m)
        {
            var status = m.IsFainted ? "ひんし" : m.Status?.Name ?? string.Empty;
            return $"{m.Species.Type.Name}　HP {m.Hp}/{m.MaxHp}{(status.Length > 0 ? "　" + status : string.Empty)}";
        }

        /// <summary>経過 Tick を「分:秒」に。</summary>
        public string FormatTime(long ticks)
        {
            var seconds = ticks / Math.Max(1, _settings.TickRate);
            return $"{seconds / 60}:{seconds % 60:00}";
        }
    }
}
