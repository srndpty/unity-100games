using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core;

namespace Game.App
{
    public enum QuestScreen
    {
        Title = 0,
        HowTo = 1,
        Field = 2,
        CampMenu = 3,
        Shop = 4,
        Inn = 5,
        Church = 6,
        Dialogue = 7,
        Battle = 8,
        Ending = 9,
        Result = 10,
    }

    public enum TitleOptionKind
    {
        Continue = 0,
        NewGame = 1,
        HowTo = 2,
    }

    public enum CampSubMode
    {
        Root = 0,
        Status = 1,
        SpellCaster = 2,
        SpellSelect = 3,
        SpellTarget = 4,
        ItemSelect = 5,
        ItemTarget = 6,
        EquipMember = 7,
        EquipSlot = 8,
        EquipCandidate = 9,
    }

    public enum ShopSubMode
    {
        Root = 0,
        BuySelect = 1,
        BuyEquipTarget = 2,
        SellSelect = 3,
    }

    public enum ChurchSubMode
    {
        Root = 0,
        ReviveTarget = 1,
    }

    public readonly struct SellCandidate
    {
        public SellCandidate(string productId, string label, int sellPrice, int equippedMemberIndex, EquipSlot slot)
        {
            ProductId = productId;
            Label = label;
            SellPrice = sellPrice;
            EquippedMemberIndex = equippedMemberIndex;
            Slot = slot;
        }

        public string ProductId { get; }
        public string Label { get; }
        public int SellPrice { get; }
        public int EquippedMemberIndex { get; }
        public EquipSlot Slot { get; }
    }

    /// <summary>
    /// 画面遷移・入力時刻解決・固定刻み進行・メニュー/施設/戦闘の仲介を担うユースケース層。
    /// Unity API に依存しない。
    /// </summary>
    public sealed class QuestFlow
    {
        public const int MaxTicksPerFrame = 6;

        private static readonly string[] _campRootLabels = { "つよさ", "じゅもん", "どうぐ", "そうび", "とじる", "タイトルへ" };
        private static readonly string[] _shopRootLabels = { "かう", "うる", "やめる" };
        private static readonly string[] _innLabels = { "泊まる", "やめる" };
        private static readonly string[] _churchRootLabels = { "いのる（セーブ）", "蘇生する", "やめる" };
        private static readonly string[] _pauseLabels = { "ゲームに戻る", "タイトルへ戻る" };

        private readonly ISaveStore _saveStore;
        private readonly int _eventDurationTicks;
        private readonly uint[] _heldKeyMasks = new uint[8];
        private readonly List<Intent> _pendingIntents = new(16);
        private readonly KeyRepeatTracker _menuRepeat = new(initialDelayTicks: 16, intervalTicks: 6);

        private TitleOptionKind[] _titleOptions = Array.Empty<TitleOptionKind>();
        private ReadOnlyCollection<TitleOptionKind> _titleOptionsView;
        private uint _nextSeedState;
        private bool _hasFocus = true;
        private bool _clockInitialized;
        private double _clockTime;
        private double _tickAccumulator;
        private double _tickRemainderError;

        private string _pendingBossGroupId = string.Empty;
        private bool _campTargetRequiresDead;
        private string _stagingActionId = string.Empty;

        public QuestFlow(GameData data, ISaveStore saveStore, int initialSeed = 1, int eventDurationTicks = 18)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            _saveStore = saveStore ?? throw new ArgumentNullException(nameof(saveStore));
            _eventDurationTicks = Math.Max(1, eventDurationTicks);
            _nextSeedState = new GameRng(initialSeed).State;
            _titleOptionsView = Array.AsReadOnly(_titleOptions);
            RefreshTitleState();
        }

        public GameData Data { get; }
        public World? World { get; private set; }
        public BattlePresenter? ActiveBattle { get; private set; }
        public QuestScreen Screen { get; private set; }
        public bool IsPaused { get; private set; }
        public int PauseCursor { get; private set; }
        public bool HasFocus => _hasFocus;

        // タイトル状態
        public IReadOnlyList<TitleOptionKind> TitleOptions => _titleOptionsView;
        public int TitleCursor { get; private set; }
        public bool HasValidSave { get; private set; }
        public string CorruptSaveNotice { get; private set; } = string.Empty;

        // フィールド・会話状態
        public string FieldBanner { get; private set; } = string.Empty;
        public string DialogueSpeaker { get; private set; } = string.Empty;
        public string DialogueMessage { get; private set; } = string.Empty;

        // メニュー・施設状態
        public CampSubMode CampMode { get; private set; }
        public int CampRootCursor { get; private set; }
        public int CampMemberCursor { get; private set; }
        public int CampSubCursor { get; private set; }
        public int CampTargetCursor { get; private set; }
        public EquipSlot SelectedEquipSlot { get; private set; }
        public string MenuFeedback { get; private set; } = string.Empty;

        public ShopInfo? ActiveShop { get; private set; }
        public ShopSubMode ShopMode { get; private set; }
        public int ShopRootCursor { get; private set; }
        public int ShopSubCursor { get; private set; }
        public int ShopTargetCursor { get; private set; }

        public int InnCursor { get; private set; }
        public ChurchSubMode ChurchMode { get; private set; }
        public int ChurchRootCursor { get; private set; }
        public int ChurchTargetCursor { get; private set; }

        // 統計・音響カウンタ
        public double UnscaledPlaySeconds { get; private set; }
        public AudioCueCounter AudioCues { get; } = new();

        public static IReadOnlyList<string> CampMenuLabels => _campRootLabels;
        public static IReadOnlyList<string> ShopMenuLabels => _shopRootLabels;
        public static IReadOnlyList<string> InnMenuLabels => _innLabels;
        public static IReadOnlyList<string> ChurchMenuLabels => _churchRootLabels;
        public static IReadOnlyList<string> PauseMenuLabels => _pauseLabels;

        public static string TitleOptionLabel(TitleOptionKind kind) => kind switch
        {
            TitleOptionKind.Continue => "つづきから",
            TitleOptionKind.NewGame => "はじめから",
            TitleOptionKind.HowTo => "あそびかた",
            _ => string.Empty,
        };

        /// <summary>タイムスタンプ付き入力をキューへ積む（フレーム更新時に時刻順で反映される）。</summary>
        public void EnqueueIntent(Intent intent)
        {
            if (!_hasFocus)
            {
                return;
            }

            _pendingIntents.Add(intent);
        }

        /// <summary>テスト・自動操縦向けに、現在時刻でキー押下または解放を即時反映する。</summary>
        public void SendButton(InputButton button, bool pressed, int keySlot = 0)
        {
            if (!_hasFocus)
            {
                return;
            }

            ApplyKeyChange(button, Math.Clamp(keySlot, 0, 31), pressed);
        }

        /// <summary>テスト・自動操縦向けに 1 回の押下＋解放を送る。</summary>
        public void TapButton(InputButton button, int keySlot = 0)
        {
            SendButton(button, pressed: true, keySlot);
            SendButton(button, pressed: false, keySlot);
        }

        /// <summary>明示的なシードで新規ゲームを開始する（撮影・決定論テスト用）。</summary>
        public void StartNewGameWithSeed(int seed)
        {
            ClearAllInputs();
            IsPaused = false;
            ActiveBattle = null;
            ActiveShop = null;
            World = World.CreateNew(Data, seed);
            UnscaledPlaySeconds = 0.0;
            FieldBanner = "北の階段から『焔鎖の坑道』へ向かおう。";
            Screen = QuestScreen.Field;
        }

        /// <summary>実時間フレームを進める（入力の時刻順反映 + 1 フレームの Tick 上限共有）。</summary>
        public void UpdateFrame(double currentTimeSeconds, double unscaledDeltaSeconds)
        {
            if (!double.IsFinite(currentTimeSeconds))
            {
                return;
            }

            if (double.IsFinite(unscaledDeltaSeconds) && unscaledDeltaSeconds > 0.0 && IsCountingPlayTime())
            {
                UnscaledPlaySeconds += Math.Min(1.0, unscaledDeltaSeconds);
            }

            if (!_clockInitialized)
            {
                _clockInitialized = true;
                _clockTime = currentTimeSeconds;
            }

            var ticksUsedThisFrame = 0;
            if (_pendingIntents.Count > 1)
            {
                _pendingIntents.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            for (var i = 0; i < _pendingIntents.Count; i++)
            {
                var intent = _pendingIntents[i];
                if (intent.Time > _clockTime)
                {
                    AdvanceClockTo(intent.Time, ref ticksUsedThisFrame);
                }

                if (_hasFocus)
                {
                    ApplyKeyChange(intent.Button, Math.Clamp(intent.KeySlot, 0, 31), intent.Pressed);
                }
            }

            _pendingIntents.Clear();

            if (currentTimeSeconds > _clockTime)
            {
                AdvanceClockTo(currentTimeSeconds, ref ticksUsedThisFrame);
            }
        }

        /// <summary>保留中の入力イベントを時計を進めずに時刻順で反映する（Time.timeScale == 0 のテスト駆動用）。</summary>
        public void FlushPendingIntents()
        {
            if (_pendingIntents.Count > 1)
            {
                _pendingIntents.Sort((a, b) => a.Time.CompareTo(b.Time));
            }

            for (var i = 0; i < _pendingIntents.Count; i++)
            {
                if (_hasFocus)
                {
                    var intent = _pendingIntents[i];
                    ApplyKeyChange(intent.Button, Math.Clamp(intent.KeySlot, 0, 31), intent.Pressed);
                }
            }

            _pendingIntents.Clear();
        }

        /// <summary>テスト・撮影台本向けに指定 Tick 数だけ決定的に進める。</summary>
        public void StepTicks(int ticks = 1)
        {
            FlushPendingIntents();
            for (var i = 0; i < ticks; i++)
            {
                StepSingleTick();
            }
        }

        public void OnFocusChanged(bool hasFocus)
        {
            if (!hasFocus)
            {
                _hasFocus = false;
                _pendingIntents.Clear();
                ClearHeldKeysAndBuffers();
                _tickAccumulator = 0.0;
                _tickRemainderError = 0.0;

                if (CanPauseCurrentScreen())
                {
                    IsPaused = true;
                    PauseCursor = 0;
                }
            }
            else
            {
                _hasFocus = true;
                _pendingIntents.Clear();
                _clockInitialized = false;
            }
        }

        public void RefreshTitleState()
        {
            ClearAllInputs();
            IsPaused = false;
            World = null;
            ActiveBattle = null;
            ActiveShop = null;
            Screen = QuestScreen.Title;

            if (_saveStore.Exists)
            {
                var raw = _saveStore.Read();
                if (SaveCodec.TryDecode(Data, raw, out _, out var saveErr))
                {
                    HasValidSave = true;
                    CorruptSaveNotice = string.Empty;
                    _titleOptions = new[] { TitleOptionKind.Continue, TitleOptionKind.NewGame, TitleOptionKind.HowTo };
                }
                else
                {
                    HasValidSave = false;
                    CorruptSaveNotice = $"セーブデータが破損しているため読み込めません。（{saveErr!.Message}）";
                    _titleOptions = new[] { TitleOptionKind.NewGame, TitleOptionKind.HowTo };
                }
            }
            else
            {
                HasValidSave = false;
                CorruptSaveNotice = string.Empty;
                _titleOptions = new[] { TitleOptionKind.NewGame, TitleOptionKind.HowTo };
            }

            _titleOptionsView = Array.AsReadOnly(_titleOptions);
            TitleCursor = 0;
        }

        public IReadOnlyList<SelectableOption> GetCampSpellOptions()
        {
            if (World == null)
            {
                return Array.Empty<SelectableOption>();
            }

            var caster = World.Party[CampMemberCursor];
            var spells = caster.LearnedSpells();
            var list = new List<SelectableOption>(spells.Count);
            for (var i = 0; i < spells.Count; i++)
            {
                var sp = spells[i];
                var ok = BattleRules.CanCast(Data, caster, sp.Id, inBattle: false, out _);
                if (ok && sp.Kind == SpellKind.Revive && World.Party.LivingCount == World.Party.Count)
                {
                    ok = false;
                }

                list.Add(new SelectableOption(sp.Id, sp.Name, $"MP {sp.MpCost}  {sp.Description}", ok));
            }

            return list.AsReadOnly();
        }

        public IReadOnlyList<SelectableOption> GetCampItemOptions()
        {
            if (World == null)
            {
                return Array.Empty<SelectableOption>();
            }

            var entries = World.Inventory.UsableItems();
            var list = new List<SelectableOption>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var item = Data.GetItem(entries[i].ProductId);
                list.Add(new SelectableOption(item.Id, $"{item.Name} x{entries[i].Count}", item.Description, item.UsableInField));
            }

            return list.AsReadOnly();
        }

        public IReadOnlyList<SelectableOption> GetEquipCandidateOptions()
        {
            if (World == null)
            {
                return Array.Empty<SelectableOption>();
            }

            var member = World.Party[CampMemberCursor];
            var list = new List<SelectableOption>();
            var bagEquips = World.Inventory.EquippableInBag(member, SelectedEquipSlot);
            for (var i = 0; i < bagEquips.Count; i++)
            {
                var eq = bagEquips[i];
                list.Add(new SelectableOption(eq.Id, eq.Name, eq.Description, enabled: true));
            }

            var currentId = SelectedEquipSlot == EquipSlot.Weapon ? member.WeaponId : member.ArmorId;
            if (currentId.Length > 0)
            {
                list.Add(new SelectableOption("__unequip__", "外す", "現在の装備を袋に戻す", World.Inventory.Count(currentId) < World.Inventory.MaxStack));
            }

            return list.AsReadOnly();
        }

        public IReadOnlyList<SelectableOption> GetShopBuyOptions()
        {
            if (World == null || ActiveShop == null)
            {
                return Array.Empty<SelectableOption>();
            }

            var list = new List<SelectableOption>(ActiveShop.ProductIds.Count);
            for (var i = 0; i < ActiveShop.ProductIds.Count; i++)
            {
                var id = ActiveShop.ProductIds[i];
                var price = Data.ProductPrice(id);
                var owned = World.Inventory.TotalOwned(World.Party, id);
                var desc = Data.TryGetItem(id, out var it) ? it!.Description : Data.GetEquip(id).Description;
                var ok = World.Gold >= price && World.Inventory.CanAdd(World.Party, id, 1);
                list.Add(new SelectableOption(id, $"{Data.ProductName(id)}  {price}G", $"所持:{owned}  {desc}", ok));
            }

            return list.AsReadOnly();
        }

        public IReadOnlyList<SelectableOption> GetShopEquipTargetOptions()
        {
            if (World == null || !Data.TryGetEquip(_stagingActionId, out var equip))
            {
                return Array.Empty<SelectableOption>();
            }

            var list = new List<SelectableOption>(World.Party.Count + 1)
            {
                new("-1", "袋に入れる", "装備せず所持袋にしまう", enabled: true),
            };

            for (var i = 0; i < World.Party.Count; i++)
            {
                var m = World.Party[i];
                if (!m.CanEquip(equip!))
                {
                    continue;
                }

                var curId = equip!.Slot == EquipSlot.Weapon ? m.WeaponId : m.ArmorId;
                var curAtk = curId.Length > 0 ? Data.GetEquip(curId).AtkBonus : 0;
                var curDef = curId.Length > 0 ? Data.GetEquip(curId).DefBonus : 0;
                var dAtk = equip.AtkBonus - curAtk;
                var dDef = equip.DefBonus - curDef;
                var diffText = equip.Slot == EquipSlot.Weapon
                    ? $"攻{(dAtk >= 0 ? "+" : string.Empty)}{dAtk}"
                    : $"守{(dDef >= 0 ? "+" : string.Empty)}{dDef}";
                list.Add(new SelectableOption(
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"{m.Name}が今すぐ装備",
                    diffText,
                    enabled: true));
            }

            return list.AsReadOnly();
        }

        public IReadOnlyList<SellCandidate> GetShopSellCandidates()
        {
            if (World == null)
            {
                return Array.Empty<SellCandidate>();
            }

            var list = new List<SellCandidate>();
            var bag = World.Inventory.AllEntries();
            for (var i = 0; i < bag.Count; i++)
            {
                var id = bag[i].ProductId;
                var price = Data.ProductSellPrice(id);
                list.Add(new SellCandidate(id, $"{Data.ProductName(id)} x{bag[i].Count}", price, -1, EquipSlot.Weapon));
            }

            for (var i = 0; i < World.Party.Count; i++)
            {
                var m = World.Party[i];
                if (m.WeaponId.Length > 0)
                {
                    list.Add(new SellCandidate(
                        m.WeaponId,
                        $"{Data.ProductName(m.WeaponId)} [装備:{m.Name}]",
                        Data.ProductSellPrice(m.WeaponId),
                        i,
                        EquipSlot.Weapon));
                }

                if (m.ArmorId.Length > 0)
                {
                    list.Add(new SellCandidate(
                        m.ArmorId,
                        $"{Data.ProductName(m.ArmorId)} [装備:{m.Name}]",
                        Data.ProductSellPrice(m.ArmorId),
                        i,
                        EquipSlot.Armor));
                }
            }

            return list.AsReadOnly();
        }

        public IReadOnlyList<int> GetFallenMemberIndices()
        {
            if (World == null)
            {
                return Array.Empty<int>();
            }

            var list = new List<int>(World.Party.Count);
            for (var i = 0; i < World.Party.Count; i++)
            {
                if (!World.Party[i].IsAlive)
                {
                    list.Add(i);
                }
            }

            return list.AsReadOnly();
        }

        private void AdvanceClockTo(double targetTime, ref int ticksUsedThisFrame)
        {
            var dt = targetTime - _clockTime;
            _clockTime = targetTime;
            if (dt <= 0.0)
            {
                return;
            }

            // 補償加算で 0.01 秒刻みなどの丸め誤差を防ぐ（商と剰余を同じ累積値から取る）
            var addedTicks = (dt * Data.Rules.TickRate) - _tickRemainderError;
            var total = _tickAccumulator + addedTicks;
            _tickRemainderError = (total - _tickAccumulator) - addedTicks;
            _tickAccumulator = total;

            var dueTicks = (long)Math.Floor(_tickAccumulator + 1e-12);
            if (dueTicks <= 0)
            {
                return;
            }

            _tickAccumulator = Math.Max(0.0, _tickAccumulator - dueTicks);
            var remainingBudget = MaxTicksPerFrame - ticksUsedThisFrame;
            var toRun = (int)Math.Min(dueTicks, Math.Max(0, remainingBudget));
            for (var i = 0; i < toRun; i++)
            {
                StepSingleTick();
            }

            ticksUsedThisFrame += toRun;
        }

        private void StepSingleTick()
        {
            if (IsPaused)
            {
                if (_menuRepeat.TickRepeat(CurrentHeldDirection(), out var pauseDir))
                {
                    MovePauseCursor(pauseDir);
                }

                return;
            }

            if (World != null && IsCountingPlayTime())
            {
                World.AdvancePlayTicks(1);
            }

            switch (Screen)
            {
                case QuestScreen.Field:
                    StepFieldTick();
                    break;

                case QuestScreen.Battle:
                    if (ActiveBattle != null)
                    {
                        if (_menuRepeat.TickRepeat(CurrentHeldDirection(), out var bDir))
                        {
                            ActiveBattle.MoveCursor(bDir, AudioCues);
                        }

                        ActiveBattle.Tick(AudioCues);
                    }

                    break;

                default:
                    if (_menuRepeat.TickRepeat(CurrentHeldDirection(), out var mDir))
                    {
                        HandleDirectionalPress(mDir);
                    }

                    break;
            }
        }

        private void StepFieldTick()
        {
            if (World == null)
            {
                return;
            }

            var held = CurrentHeldDirection();
            var prevX = World.Explorer.X;
            var prevY = World.Explorer.Y;
            var result = World.Explorer.Tick(Data, World.Flags, held, out var groupId, out var msg);
            if (World.Explorer.X != prevX || World.Explorer.Y != prevY)
            {
                AudioCues.Step++;
            }

            switch (result)
            {
                case FieldStepResult.PortalMoved:
                    AudioCues.Portal++;
                    FieldBanner = World.CurrentMap.Name;
                    break;

                case FieldStepResult.EncounterTriggered:
                    BeginBattle(groupId, msg);
                    break;

                case FieldStepResult.BossTriggered:
                    DialogueSpeaker = "焔鎖の魔将ヴァルグ";
                    DialogueMessage = msg;
                    _pendingBossGroupId = groupId;
                    Screen = QuestScreen.Dialogue;
                    break;
            }
        }

        private void ApplyKeyChange(InputButton button, int keySlot, bool pressed)
        {
            var idx = (int)button;
            var prevMask = _heldKeyMasks[idx];
            var bit = 1u << keySlot;
            var nextMask = pressed ? (prevMask | bit) : (prevMask & ~bit);
            _heldKeyMasks[idx] = nextMask;

            var justPressed = prevMask == 0 && nextMask != 0;
            if (!justPressed)
            {
                return;
            }

            if (button == InputButton.Pause)
            {
                if (IsPaused)
                {
                    IsPaused = false;
                    AudioCues.Cancel++;
                }
                else if (CanPauseCurrentScreen())
                {
                    ClearHeldDirectionsAndFieldBuffer();
                    IsPaused = true;
                    PauseCursor = 0;
                    AudioCues.Confirm++;
                }

                return;
            }

            if (IsPaused)
            {
                HandlePausedInput(button);
                return;
            }

            if (button == InputButton.Up || button == InputButton.Right || button == InputButton.Down || button == InputButton.Left)
            {
                var dir = ButtonToDirection(button);
                _menuRepeat.OnDirectionPressed(dir);
                HandleDirectionalPress(dir);
                return;
            }

            switch (button)
            {
                case InputButton.Confirm:
                    HandleConfirm();
                    break;

                case InputButton.Cancel:
                    HandleCancel();
                    break;

                case InputButton.Menu:
                    HandleMenuKey();
                    break;
            }
        }

        private void HandlePausedInput(InputButton button)
        {
            if (button == InputButton.Up || button == InputButton.Down || button == InputButton.Left || button == InputButton.Right)
            {
                var dir = ButtonToDirection(button);
                _menuRepeat.OnDirectionPressed(dir);
                MovePauseCursor(dir);
                return;
            }

            if (button == InputButton.Cancel)
            {
                IsPaused = false;
                AudioCues.Cancel++;
                return;
            }

            if (button == InputButton.Confirm)
            {
                if (PauseCursor == 0)
                {
                    IsPaused = false;
                    AudioCues.Confirm++;
                }
                else
                {
                    AudioCues.Confirm++;
                    RefreshTitleState();
                }
            }
        }

        private void MovePauseCursor(Direction dir)
        {
            var delta = (dir == Direction.Down || dir == Direction.Right) ? 1 : -1;
            PauseCursor = Wrap(PauseCursor + delta, _pauseLabels.Length);
            AudioCues.CursorMove++;
        }

        private void HandleDirectionalPress(Direction dir)
        {
            var delta = (dir == Direction.Down || dir == Direction.Right) ? 1 : -1;
            switch (Screen)
            {
                case QuestScreen.Title:
                    if (_titleOptions.Length > 1)
                    {
                        TitleCursor = Wrap(TitleCursor + delta, _titleOptions.Length);
                        AudioCues.CursorMove++;
                    }

                    break;

                case QuestScreen.Field:
                    if (World != null)
                    {
                        var prevX = World.Explorer.X;
                        var prevY = World.Explorer.Y;
                        World.Explorer.RequestMove(Data, World.Flags, dir);
                        if (World.Explorer.X != prevX || World.Explorer.Y != prevY)
                        {
                            AudioCues.Step++;
                        }
                    }

                    break;

                case QuestScreen.CampMenu:
                    MoveCampCursor(delta);
                    break;

                case QuestScreen.Shop:
                    MoveShopCursor(delta);
                    break;

                case QuestScreen.Inn:
                    InnCursor = Wrap(InnCursor + delta, _innLabels.Length);
                    AudioCues.CursorMove++;
                    break;

                case QuestScreen.Church:
                    MoveChurchCursor(delta);
                    break;

                case QuestScreen.Battle:
                    ActiveBattle?.MoveCursor(dir, AudioCues);
                    break;
            }
        }

        private void HandleConfirm()
        {
            switch (Screen)
            {
                case QuestScreen.Title:
                    HandleTitleConfirm();
                    break;

                case QuestScreen.HowTo:
                    AudioCues.Confirm++;
                    Screen = QuestScreen.Title;
                    break;

                case QuestScreen.Field:
                    HandleFieldInteract();
                    break;

                case QuestScreen.Dialogue:
                    AudioCues.Confirm++;
                    if (_pendingBossGroupId.Length > 0)
                    {
                        var bossGroup = _pendingBossGroupId;
                        _pendingBossGroupId = string.Empty;
                        BeginBattle(bossGroup, DialogueMessage);
                    }
                    else
                    {
                        Screen = QuestScreen.Field;
                    }

                    break;

                case QuestScreen.CampMenu:
                    HandleCampConfirm();
                    break;

                case QuestScreen.Shop:
                    HandleShopConfirm();
                    break;

                case QuestScreen.Inn:
                    HandleInnConfirm();
                    break;

                case QuestScreen.Church:
                    HandleChurchConfirm();
                    break;

                case QuestScreen.Battle:
                    if (ActiveBattle != null && ActiveBattle.Confirm(AudioCues))
                    {
                        FinishBattle();
                    }

                    break;

                case QuestScreen.Ending:
                    AudioCues.Confirm++;
                    Screen = QuestScreen.Result;
                    break;

                case QuestScreen.Result:
                    AudioCues.Confirm++;
                    RefreshTitleState();
                    break;
            }
        }

        private void HandleCancel()
        {
            switch (Screen)
            {
                case QuestScreen.HowTo:
                    AudioCues.Cancel++;
                    Screen = QuestScreen.Title;
                    break;

                case QuestScreen.Field:
                    OpenCampMenu();
                    break;

                case QuestScreen.Dialogue:
                    if (_pendingBossGroupId.Length == 0)
                    {
                        AudioCues.Cancel++;
                        Screen = QuestScreen.Field;
                    }

                    break;

                case QuestScreen.CampMenu:
                    HandleCampCancel();
                    break;

                case QuestScreen.Shop:
                    HandleShopCancel();
                    break;

                case QuestScreen.Inn:
                    AudioCues.Cancel++;
                    Screen = QuestScreen.Field;
                    break;

                case QuestScreen.Church:
                    if (ChurchMode == ChurchSubMode.ReviveTarget)
                    {
                        ChurchMode = ChurchSubMode.Root;
                        AudioCues.Cancel++;
                    }
                    else
                    {
                        AudioCues.Cancel++;
                        Screen = QuestScreen.Field;
                    }

                    break;

                case QuestScreen.Battle:
                    ActiveBattle?.Cancel(AudioCues);
                    break;
            }
        }

        private void HandleMenuKey()
        {
            if (Screen == QuestScreen.Field && World != null && !World.Explorer.IsMoving)
            {
                OpenCampMenu();
            }
            else if (Screen == QuestScreen.CampMenu && CampMode == CampSubMode.Root)
            {
                AudioCues.Cancel++;
                Screen = QuestScreen.Field;
            }
        }

        private void HandleTitleConfirm()
        {
            if (_titleOptions.Length == 0)
            {
                return;
            }

            var chosen = _titleOptions[Math.Clamp(TitleCursor, 0, _titleOptions.Length - 1)];
            switch (chosen)
            {
                case TitleOptionKind.NewGame:
                    {
                        AudioCues.Confirm++;
                        var seedRng = GameRng.FromState(_nextSeedState);
                        var seed = (int)(seedRng.NextUInt() & 0x7FFFFFFFu);
                        if (seed == 0)
                        {
                            seed = 1;
                        }

                        _nextSeedState = seedRng.State;
                        StartNewGameWithSeed(seed);
                        break;
                    }

                case TitleOptionKind.Continue:
                    {
                        var raw = _saveStore.Read();
                        if (!SaveCodec.TryDecode(Data, raw, out var restored, out _))
                        {
                            AudioCues.Reject++;
                            RefreshTitleState();
                            return;
                        }

                        AudioCues.Confirm++;
                        ClearAllInputs();
                        World = restored!;
                        UnscaledPlaySeconds = World.PlayTicks / (double)Data.Rules.TickRate;
                        FieldBanner = $"{World.CurrentMap.Name} から冒険を再開した。";
                        Screen = QuestScreen.Field;
                        break;
                    }

                case TitleOptionKind.HowTo:
                    AudioCues.Confirm++;
                    Screen = QuestScreen.HowTo;
                    break;
            }
        }

        private void HandleFieldInteract()
        {
            if (World == null || World.Explorer.IsMoving)
            {
                return;
            }

            var interaction = World.Interact();
            switch (interaction.Kind)
            {
                case FieldInteractionKind.None:
                    break;

                case FieldInteractionKind.Dialogue:
                case FieldInteractionKind.ChestAlreadyOpen:
                case FieldInteractionKind.ChestBagFull:
                case FieldInteractionKind.LockedDoorBlocked:
                    AudioCues.Confirm++;
                    DialogueSpeaker = interaction.Speaker;
                    DialogueMessage = interaction.Message;
                    Screen = QuestScreen.Dialogue;
                    break;

                case FieldInteractionKind.OpenedChest:
                    AudioCues.Chest++;
                    DialogueSpeaker = string.Empty;
                    DialogueMessage = interaction.Message;
                    Screen = QuestScreen.Dialogue;
                    break;

                case FieldInteractionKind.UnlockedDoor:
                    AudioCues.DoorUnlock++;
                    DialogueSpeaker = string.Empty;
                    DialogueMessage = interaction.Message;
                    Screen = QuestScreen.Dialogue;
                    break;

                case FieldInteractionKind.OpenInn:
                    AudioCues.Confirm++;
                    InnCursor = 0;
                    MenuFeedback = interaction.Message;
                    Screen = QuestScreen.Inn;
                    break;

                case FieldInteractionKind.OpenShop:
                    AudioCues.Confirm++;
                    ActiveShop = Data.GetShop(interaction.TargetId);
                    ShopMode = ShopSubMode.Root;
                    ShopRootCursor = 0;
                    ShopSubCursor = 0;
                    MenuFeedback = interaction.Message;
                    Screen = QuestScreen.Shop;
                    break;

                case FieldInteractionKind.OpenChurch:
                    AudioCues.Confirm++;
                    ChurchMode = ChurchSubMode.Root;
                    ChurchRootCursor = 0;
                    ChurchTargetCursor = 0;
                    MenuFeedback = interaction.Message;
                    Screen = QuestScreen.Church;
                    break;

                case FieldInteractionKind.BossTriggered:
                    AudioCues.Confirm++;
                    DialogueSpeaker = "焔鎖の魔将ヴァルグ";
                    DialogueMessage = interaction.Message;
                    _pendingBossGroupId = interaction.TargetId;
                    Screen = QuestScreen.Dialogue;
                    break;
            }
        }

        private void OpenCampMenu()
        {
            if (World == null || World.Explorer.IsMoving)
            {
                return;
            }

            ClearHeldDirectionsAndFieldBuffer();
            CampMode = CampSubMode.Root;
            CampRootCursor = 0;
            CampMemberCursor = 0;
            CampSubCursor = 0;
            CampTargetCursor = 0;
            MenuFeedback = string.Empty;
            AudioCues.Confirm++;
            Screen = QuestScreen.CampMenu;
        }

        private void MoveCampCursor(int delta)
        {
            if (World == null)
            {
                return;
            }

            MenuFeedback = string.Empty;
            switch (CampMode)
            {
                case CampSubMode.Root:
                    CampRootCursor = Wrap(CampRootCursor + delta, _campRootLabels.Length);
                    AudioCues.CursorMove++;
                    break;

                case CampSubMode.SpellCaster:
                case CampSubMode.EquipMember:
                    CampMemberCursor = Wrap(CampMemberCursor + delta, World.Party.Count);
                    AudioCues.CursorMove++;
                    break;

                case CampSubMode.SpellSelect:
                    {
                        var opts = GetCampSpellOptions();
                        if (opts.Count > 0)
                        {
                            CampSubCursor = Wrap(CampSubCursor + delta, opts.Count);
                            AudioCues.CursorMove++;
                        }

                        break;
                    }

                case CampSubMode.ItemSelect:
                    {
                        var opts = GetCampItemOptions();
                        if (opts.Count > 0)
                        {
                            CampSubCursor = Wrap(CampSubCursor + delta, opts.Count);
                            AudioCues.CursorMove++;
                        }

                        break;
                    }

                case CampSubMode.SpellTarget:
                case CampSubMode.ItemTarget:
                    CampTargetCursor = NextCampAllyTarget(CampTargetCursor, delta, _campTargetRequiresDead);
                    AudioCues.CursorMove++;
                    break;

                case CampSubMode.EquipSlot:
                    SelectedEquipSlot = SelectedEquipSlot == EquipSlot.Weapon ? EquipSlot.Armor : EquipSlot.Weapon;
                    AudioCues.CursorMove++;
                    break;

                case CampSubMode.EquipCandidate:
                    {
                        var opts = GetEquipCandidateOptions();
                        if (opts.Count > 0)
                        {
                            CampSubCursor = Wrap(CampSubCursor + delta, opts.Count);
                            AudioCues.CursorMove++;
                        }

                        break;
                    }
            }
        }

        private void HandleCampConfirm()
        {
            if (World == null)
            {
                return;
            }

            switch (CampMode)
            {
                case CampSubMode.Root:
                    switch (CampRootCursor)
                    {
                        case 0: // つよさ
                            CampMode = CampSubMode.Status;
                            AudioCues.Confirm++;
                            break;
                        case 1: // じゅもん
                            CampMemberCursor = 1; // 既定で僧侶に合わせる
                            CampMode = CampSubMode.SpellCaster;
                            AudioCues.Confirm++;
                            break;
                        case 2: // どうぐ
                            if (GetCampItemOptions().Count == 0)
                            {
                                MenuFeedback = "所持している道具がありません。";
                                AudioCues.Reject++;
                                return;
                            }

                            CampSubCursor = 0;
                            CampMode = CampSubMode.ItemSelect;
                            AudioCues.Confirm++;
                            break;
                        case 3: // そうび
                            CampMemberCursor = 0;
                            CampMode = CampSubMode.EquipMember;
                            AudioCues.Confirm++;
                            break;
                        case 4: // とじる
                            AudioCues.Cancel++;
                            Screen = QuestScreen.Field;
                            break;
                        case 5: // タイトルへ
                            AudioCues.Confirm++;
                            RefreshTitleState();
                            break;
                    }

                    break;

                case CampSubMode.Status:
                    CampMode = CampSubMode.Root;
                    AudioCues.Cancel++;
                    break;

                case CampSubMode.SpellCaster:
                    {
                        var caster = World.Party[CampMemberCursor];
                        if (!caster.IsAlive)
                        {
                            MenuFeedback = CommandErrorText.Describe(CommandError.ActorDead);
                            AudioCues.Reject++;
                            return;
                        }

                        if (caster.LearnedSpells().Count == 0)
                        {
                            MenuFeedback = "覚えている呪文がない。";
                            AudioCues.Reject++;
                            return;
                        }

                        CampSubCursor = 0;
                        CampMode = CampSubMode.SpellSelect;
                        AudioCues.Confirm++;
                        break;
                    }

                case CampSubMode.SpellSelect:
                    {
                        var opts = GetCampSpellOptions();
                        if (opts.Count == 0)
                        {
                            CampMode = CampSubMode.SpellCaster;
                            return;
                        }

                        CampSubCursor = Math.Clamp(CampSubCursor, 0, opts.Count - 1);
                        var spId = opts[CampSubCursor].Id;
                        var caster = World.Party[CampMemberCursor];
                        if (!BattleRules.CanCast(Data, caster, spId, inBattle: false, out var err))
                        {
                            MenuFeedback = CommandErrorText.Describe(err);
                            AudioCues.Reject++;
                            return;
                        }

                        var sp = Data.GetSpell(spId);
                        if (sp.Kind == SpellKind.AllHeal)
                        {
                            FieldActions.UseSpellInField(World, CampMemberCursor, sp.Id, 0, out var msg, out _);
                            MenuFeedback = msg;
                            AudioCues.Heal++;
                            return;
                        }

                        if (sp.Kind == SpellKind.Revive && World.Party.LivingCount == World.Party.Count)
                        {
                            MenuFeedback = "倒れている仲間はいない。";
                            AudioCues.Reject++;
                            return;
                        }

                        _stagingActionId = sp.Id;
                        _campTargetRequiresDead = sp.Kind == SpellKind.Revive;
                        CampTargetCursor = FirstCampAllyTarget(_campTargetRequiresDead);
                        CampMode = CampSubMode.SpellTarget;
                        AudioCues.Confirm++;
                        break;
                    }

                case CampSubMode.SpellTarget:
                    {
                        if (FieldActions.UseSpellInField(World, CampMemberCursor, _stagingActionId, CampTargetCursor, out var msg, out _))
                        {
                            MenuFeedback = msg;
                            AudioCues.Heal++;
                            CampMode = CampSubMode.SpellSelect;
                        }
                        else
                        {
                            MenuFeedback = msg;
                            AudioCues.Reject++;
                        }

                        break;
                    }

                case CampSubMode.ItemSelect:
                    {
                        var opts = GetCampItemOptions();
                        if (opts.Count == 0)
                        {
                            CampMode = CampSubMode.Root;
                            return;
                        }

                        CampSubCursor = Math.Clamp(CampSubCursor, 0, opts.Count - 1);
                        var chosen = opts[CampSubCursor];
                        if (!chosen.Enabled)
                        {
                            MenuFeedback = "移動中には使えません。";
                            AudioCues.Reject++;
                            return;
                        }

                        _stagingActionId = chosen.Id;
                        _campTargetRequiresDead = false;
                        CampTargetCursor = FirstCampAllyTarget(requireDead: false);
                        CampMode = CampSubMode.ItemTarget;
                        AudioCues.Confirm++;
                        break;
                    }

                case CampSubMode.ItemTarget:
                    {
                        if (FieldActions.UseItemInField(World, _stagingActionId, CampTargetCursor, out var msg, out _))
                        {
                            MenuFeedback = msg;
                            AudioCues.Heal++;
                            if (GetCampItemOptions().Count == 0)
                            {
                                CampMode = CampSubMode.Root;
                            }
                            else
                            {
                                CampSubCursor = Math.Clamp(CampSubCursor, 0, GetCampItemOptions().Count - 1);
                                CampMode = CampSubMode.ItemSelect;
                            }
                        }
                        else
                        {
                            MenuFeedback = msg;
                            AudioCues.Reject++;
                        }

                        break;
                    }

                case CampSubMode.EquipMember:
                    SelectedEquipSlot = EquipSlot.Weapon;
                    CampMode = CampSubMode.EquipSlot;
                    AudioCues.Confirm++;
                    break;

                case CampSubMode.EquipSlot:
                    if (GetEquipCandidateOptions().Count == 0)
                    {
                        MenuFeedback = "付け替えられる装備を持っていない。";
                        AudioCues.Reject++;
                        return;
                    }

                    CampSubCursor = 0;
                    CampMode = CampSubMode.EquipCandidate;
                    AudioCues.Confirm++;
                    break;

                case CampSubMode.EquipCandidate:
                    {
                        var opts = GetEquipCandidateOptions();
                        if (opts.Count == 0)
                        {
                            CampMode = CampSubMode.EquipSlot;
                            return;
                        }

                        CampSubCursor = Math.Clamp(CampSubCursor, 0, opts.Count - 1);
                        var chosen = opts[CampSubCursor];
                        var member = World.Party[CampMemberCursor];
                        if (string.Equals(chosen.Id, "__unequip__", StringComparison.Ordinal))
                        {
                            if (World.Inventory.UnequipToBag(member, SelectedEquipSlot))
                            {
                                MenuFeedback = $"{member.Name}の装備を外した。";
                                AudioCues.Confirm++;
                                CampMode = CampSubMode.EquipSlot;
                            }
                            else
                            {
                                MenuFeedback = "持ち物がいっぱいで外せない。";
                                AudioCues.Reject++;
                            }
                        }
                        else if (World.Inventory.EquipFromBag(member, chosen.Id))
                        {
                            MenuFeedback = $"{member.Name}は {Data.ProductName(chosen.Id)} を装備した！";
                            AudioCues.Confirm++;
                            CampMode = CampSubMode.EquipSlot;
                        }
                        else
                        {
                            MenuFeedback = "装備できなかった。";
                            AudioCues.Reject++;
                        }

                        break;
                    }
            }
        }

        private void HandleCampCancel()
        {
            MenuFeedback = string.Empty;
            AudioCues.Cancel++;
            switch (CampMode)
            {
                case CampSubMode.Root:
                    Screen = QuestScreen.Field;
                    break;
                case CampSubMode.Status:
                case CampSubMode.SpellCaster:
                case CampSubMode.ItemSelect:
                case CampSubMode.EquipMember:
                    CampMode = CampSubMode.Root;
                    break;
                case CampSubMode.SpellSelect:
                    CampMode = CampSubMode.SpellCaster;
                    break;
                case CampSubMode.SpellTarget:
                    CampMode = CampSubMode.SpellSelect;
                    break;
                case CampSubMode.ItemTarget:
                    CampMode = CampSubMode.ItemSelect;
                    break;
                case CampSubMode.EquipSlot:
                    CampMode = CampSubMode.EquipMember;
                    break;
                case CampSubMode.EquipCandidate:
                    CampMode = CampSubMode.EquipSlot;
                    break;
            }
        }

        private void MoveShopCursor(int delta)
        {
            MenuFeedback = string.Empty;
            switch (ShopMode)
            {
                case ShopSubMode.Root:
                    ShopRootCursor = Wrap(ShopRootCursor + delta, _shopRootLabels.Length);
                    AudioCues.CursorMove++;
                    break;

                case ShopSubMode.BuySelect:
                    {
                        var opts = GetShopBuyOptions();
                        if (opts.Count > 0)
                        {
                            ShopSubCursor = Wrap(ShopSubCursor + delta, opts.Count);
                            AudioCues.CursorMove++;
                        }

                        break;
                    }

                case ShopSubMode.BuyEquipTarget:
                    {
                        var opts = GetShopEquipTargetOptions();
                        if (opts.Count > 0)
                        {
                            ShopTargetCursor = Wrap(ShopTargetCursor + delta, opts.Count);
                            AudioCues.CursorMove++;
                        }

                        break;
                    }

                case ShopSubMode.SellSelect:
                    {
                        var opts = GetShopSellCandidates();
                        if (opts.Count > 0)
                        {
                            ShopSubCursor = Wrap(ShopSubCursor + delta, opts.Count);
                            AudioCues.CursorMove++;
                        }

                        break;
                    }
            }
        }

        private void HandleShopConfirm()
        {
            if (World == null || ActiveShop == null)
            {
                return;
            }

            switch (ShopMode)
            {
                case ShopSubMode.Root:
                    if (ShopRootCursor == 0)
                    {
                        ShopSubCursor = 0;
                        ShopMode = ShopSubMode.BuySelect;
                        AudioCues.Confirm++;
                    }
                    else if (ShopRootCursor == 1)
                    {
                        if (GetShopSellCandidates().Count == 0)
                        {
                            MenuFeedback = TownErrorText.Describe(TownActionError.NothingToSell);
                            AudioCues.Reject++;
                            return;
                        }

                        ShopSubCursor = 0;
                        ShopMode = ShopSubMode.SellSelect;
                        AudioCues.Confirm++;
                    }
                    else
                    {
                        AudioCues.Cancel++;
                        Screen = QuestScreen.Field;
                    }

                    break;

                case ShopSubMode.BuySelect:
                    {
                        var opts = GetShopBuyOptions();
                        if (opts.Count == 0)
                        {
                            return;
                        }

                        ShopSubCursor = Math.Clamp(ShopSubCursor, 0, opts.Count - 1);
                        var prodId = opts[ShopSubCursor].Id;
                        if (Data.TryGetEquip(prodId, out _))
                        {
                            var price = Data.ProductPrice(prodId);
                            if (World.Gold < price)
                            {
                                MenuFeedback = TownErrorText.Describe(TownActionError.InsufficientGold);
                                AudioCues.Reject++;
                                return;
                            }

                            if (!World.Inventory.CanAdd(World.Party, prodId, 1))
                            {
                                MenuFeedback = TownErrorText.Describe(TownActionError.OwnershipCapReached);
                                AudioCues.Reject++;
                                return;
                            }

                            _stagingActionId = prodId;
                            ShopTargetCursor = GetShopEquipTargetOptions().Count > 1 ? 1 : 0;
                            ShopMode = ShopSubMode.BuyEquipTarget;
                            AudioCues.Confirm++;
                            return;
                        }

                        if (TownServices.BuyProduct(World, ActiveShop, prodId, -1, out var err))
                        {
                            MenuFeedback = $"{Data.ProductName(prodId)} を購入した！";
                            AudioCues.Confirm++;
                        }
                        else
                        {
                            MenuFeedback = TownErrorText.Describe(err);
                            AudioCues.Reject++;
                        }

                        break;
                    }

                case ShopSubMode.BuyEquipTarget:
                    {
                        var targets = GetShopEquipTargetOptions();
                        ShopTargetCursor = Math.Clamp(ShopTargetCursor, 0, targets.Count - 1);
                        var memberIdx = int.Parse(targets[ShopTargetCursor].Id, System.Globalization.CultureInfo.InvariantCulture);
                        if (TownServices.BuyProduct(World, ActiveShop, _stagingActionId, memberIdx, out var err))
                        {
                            MenuFeedback = memberIdx >= 0
                                ? $"{Data.ProductName(_stagingActionId)} を購入し、{World.Party[memberIdx].Name}が装備した！"
                                : $"{Data.ProductName(_stagingActionId)} を購入した！";
                            AudioCues.Confirm++;
                            ShopMode = ShopSubMode.BuySelect;
                        }
                        else
                        {
                            MenuFeedback = TownErrorText.Describe(err);
                            AudioCues.Reject++;
                        }

                        break;
                    }

                case ShopSubMode.SellSelect:
                    {
                        var list = GetShopSellCandidates();
                        if (list.Count == 0)
                        {
                            ShopMode = ShopSubMode.Root;
                            return;
                        }

                        ShopSubCursor = Math.Clamp(ShopSubCursor, 0, list.Count - 1);
                        var chosen = list[ShopSubCursor];
                        bool ok;
                        int gained;
                        TownActionError err;
                        if (chosen.EquippedMemberIndex >= 0)
                        {
                            ok = TownServices.SellEquipped(World, chosen.EquippedMemberIndex, chosen.Slot, out gained, out err);
                        }
                        else
                        {
                            ok = TownServices.SellProduct(World, chosen.ProductId, out gained, out err);
                        }

                        if (ok)
                        {
                            MenuFeedback = $"{Data.ProductName(chosen.ProductId)} を {gained}G で売却した！";
                            AudioCues.Confirm++;
                            var updated = GetShopSellCandidates();
                            if (updated.Count == 0)
                            {
                                ShopMode = ShopSubMode.Root;
                            }
                            else
                            {
                                ShopSubCursor = Math.Clamp(ShopSubCursor, 0, updated.Count - 1);
                            }
                        }
                        else
                        {
                            MenuFeedback = TownErrorText.Describe(err);
                            AudioCues.Reject++;
                        }

                        break;
                    }
            }
        }

        private void HandleShopCancel()
        {
            MenuFeedback = string.Empty;
            AudioCues.Cancel++;
            switch (ShopMode)
            {
                case ShopSubMode.Root:
                    Screen = QuestScreen.Field;
                    break;
                case ShopSubMode.BuySelect:
                case ShopSubMode.SellSelect:
                    ShopMode = ShopSubMode.Root;
                    break;
                case ShopSubMode.BuyEquipTarget:
                    ShopMode = ShopSubMode.BuySelect;
                    break;
            }
        }

        private void HandleInnConfirm()
        {
            if (World == null)
            {
                return;
            }

            if (InnCursor == 0)
            {
                if (TownServices.RestAtInn(World, out var err))
                {
                    AudioCues.Heal++;
                    DialogueSpeaker = "宿屋の主人";
                    DialogueMessage = "ぐっすり眠って HP と MP が全快した！（戦闘不能の仲間は教会へ）";
                    Screen = QuestScreen.Dialogue;
                }
                else
                {
                    MenuFeedback = TownErrorText.Describe(err);
                    AudioCues.Reject++;
                }
            }
            else
            {
                AudioCues.Cancel++;
                Screen = QuestScreen.Field;
            }
        }

        private void MoveChurchCursor(int delta)
        {
            MenuFeedback = string.Empty;
            if (ChurchMode == ChurchSubMode.Root)
            {
                ChurchRootCursor = Wrap(ChurchRootCursor + delta, _churchRootLabels.Length);
                AudioCues.CursorMove++;
            }
            else
            {
                var fallen = GetFallenMemberIndices();
                if (fallen.Count > 0)
                {
                    ChurchTargetCursor = Wrap(ChurchTargetCursor + delta, fallen.Count);
                    AudioCues.CursorMove++;
                }
            }
        }

        private void HandleChurchConfirm()
        {
            if (World == null)
            {
                return;
            }

            if (ChurchMode == ChurchSubMode.Root)
            {
                switch (ChurchRootCursor)
                {
                    case 0: // セーブ
                        if (SaveCodec.TryEncodeAtChurch(World, out var saveText, out var saveErr))
                        {
                            _saveStore.Write(saveText);
                            MenuFeedback = "冒険の記録を灯に刻んだ！（セーブ完了）";
                            AudioCues.Save++;
                        }
                        else
                        {
                            MenuFeedback = saveErr!.Message;
                            AudioCues.Reject++;
                        }

                        break;

                    case 1: // 蘇生
                        {
                            var fallen = GetFallenMemberIndices();
                            if (fallen.Count == 0)
                            {
                                MenuFeedback = "倒れている仲間はいません。";
                                AudioCues.Reject++;
                                return;
                            }

                            ChurchTargetCursor = 0;
                            ChurchMode = ChurchSubMode.ReviveTarget;
                            AudioCues.Confirm++;
                            break;
                        }

                    case 2:
                        AudioCues.Cancel++;
                        Screen = QuestScreen.Field;
                        break;
                }

                return;
            }

            var fallenList = GetFallenMemberIndices();
            if (fallenList.Count == 0)
            {
                ChurchMode = ChurchSubMode.Root;
                return;
            }

            ChurchTargetCursor = Math.Clamp(ChurchTargetCursor, 0, fallenList.Count - 1);
            var memberIdx = fallenList[ChurchTargetCursor];
            if (TownServices.ReviveAtChurch(World, memberIdx, out var paid, out var err))
            {
                MenuFeedback = $"{World.Party[memberIdx].Name}が息を吹き返した！（{paid}G）";
                AudioCues.Heal++;
                if (GetFallenMemberIndices().Count == 0)
                {
                    ChurchMode = ChurchSubMode.Root;
                }
                else
                {
                    ChurchTargetCursor = 0;
                }
            }
            else
            {
                MenuFeedback = TownErrorText.Describe(err);
                AudioCues.Reject++;
            }
        }

        private void BeginBattle(string groupId, string openingMessage)
        {
            if (World == null)
            {
                return;
            }

            ClearHeldDirectionsAndFieldBuffer();
            var group = Data.GetGroup(groupId);
            ActiveBattle = new BattlePresenter(World, group, openingMessage, _eventDurationTicks);
            Screen = QuestScreen.Battle;
        }

        private void FinishBattle()
        {
            if (World == null || ActiveBattle == null)
            {
                return;
            }

            var battle = ActiveBattle.Battle;
            ActiveBattle = null;
            World.ApplyBattleOutcome(battle);

            if (battle.Outcome == BattleOutcome.PartyWiped)
            {
                DialogueSpeaker = "教会の神父";
                DialogueMessage = "おお、気がついたか！ 全滅してしまったようだが、灯の加護でここまで運び込まれたのだよ。（所持金が半分になった）";
                FieldBanner = World.CurrentMap.Name;
                Screen = QuestScreen.Dialogue;
                return;
            }

            if (World.IsCleared)
            {
                Screen = QuestScreen.Ending;
                return;
            }

            FieldBanner = World.CurrentMap.Name;
            Screen = QuestScreen.Field;
        }

        private Direction? CurrentHeldDirection()
        {
            if (_heldKeyMasks[(int)InputButton.Up] != 0)
            {
                return Direction.Up;
            }

            if (_heldKeyMasks[(int)InputButton.Right] != 0)
            {
                return Direction.Right;
            }

            if (_heldKeyMasks[(int)InputButton.Down] != 0)
            {
                return Direction.Down;
            }

            if (_heldKeyMasks[(int)InputButton.Left] != 0)
            {
                return Direction.Left;
            }

            return null;
        }

        private static Direction ButtonToDirection(InputButton button) => button switch
        {
            InputButton.Up => Direction.Up,
            InputButton.Right => Direction.Right,
            InputButton.Down => Direction.Down,
            _ => Direction.Left,
        };

        private bool CanPauseCurrentScreen() =>
            Screen != QuestScreen.Title &&
            Screen != QuestScreen.HowTo &&
            Screen != QuestScreen.Result &&
            Screen != QuestScreen.Ending;

        private bool IsCountingPlayTime() =>
            World != null &&
            !IsPaused &&
            Screen != QuestScreen.Title &&
            Screen != QuestScreen.HowTo &&
            Screen != QuestScreen.Ending &&
            Screen != QuestScreen.Result;

        private void ClearAllInputs()
        {
            _pendingIntents.Clear();
            ClearHeldKeysAndBuffers();
        }

        private void ClearHeldKeysAndBuffers()
        {
            Array.Clear(_heldKeyMasks, 0, _heldKeyMasks.Length);
            _menuRepeat.Reset();
            World?.Explorer.ClearBufferedInput();
        }

        private void ClearHeldDirectionsAndFieldBuffer()
        {
            _heldKeyMasks[(int)InputButton.Up] = 0;
            _heldKeyMasks[(int)InputButton.Right] = 0;
            _heldKeyMasks[(int)InputButton.Down] = 0;
            _heldKeyMasks[(int)InputButton.Left] = 0;
            _menuRepeat.Reset();
            World?.Explorer.ClearBufferedInput();
        }

        private int FirstCampAllyTarget(bool requireDead)
        {
            if (World == null)
            {
                return 0;
            }

            for (var i = 0; i < World.Party.Count; i++)
            {
                if (requireDead ? !World.Party[i].IsAlive : World.Party[i].IsAlive)
                {
                    return i;
                }
            }

            return 0;
        }

        private int NextCampAllyTarget(int current, int delta, bool requireDead)
        {
            if (World == null)
            {
                return 0;
            }

            var n = World.Party.Count;
            for (var step = 1; step <= n; step++)
            {
                var idx = Wrap(current + (delta * step), n);
                if (requireDead ? !World.Party[idx].IsAlive : World.Party[idx].IsAlive)
                {
                    return idx;
                }
            }

            return current;
        }

        private static int Wrap(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            var r = index % count;
            return r < 0 ? r + count : r;
        }
    }
}
