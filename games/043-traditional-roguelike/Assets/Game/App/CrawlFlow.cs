using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        HowTo,
        Playing,

        /// <summary>持ち物の画面。開いている間は時間が進まない（ターン制なので操作を待つだけ）。</summary>
        Inventory,
        Paused,
        Result,
    }

    /// <summary>キーの代わりに 1 操作ずつ決める操縦役（撮影・テスト用）。画面に出してよい情報（<see cref="PlayerView"/>）だけで決める。</summary>
    public interface IPilot
    {
        Command Decide(PlayerView view);

        /// <summary>新しい試合の始めに、前の試合の判断の状態を捨てる（023）。</summary>
        void Reset();
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core の操作へ変える窓口。
    /// <para>方向キーを押すとすぐ 1 歩。押したままなら <see cref="RepeatDelay"/> 秒後から <see cref="RepeatInterval"/> 秒ごとに歩き続ける。
    /// 押したフレームの経過時間は待ちに含めない（012）。敵が新しく見えた・攻撃した／された・品を拾った・壁に当たったなどで連続移動を止め、
    /// 方向キーを押し直すまで再開しない。</para>
    /// <para>シード：試合ごとに <c>firstSeed</c> から 1 ずつ進める（再戦は必ず新しいシード、010）。</para>
    /// </summary>
    public sealed class CrawlFlow
    {
        /// <summary>持ち物の画面の列の数。左右で 1 つ、上下で 1 行（この数）ずつ選択を動かす。HUD の並びもこの数に合わせる。</summary>
        public const int InventoryColumns = 2;

        private readonly CrawlConfig _config;
        private readonly HeldKeys _held = new();
        private readonly List<RunEvent> _pendingEvents = new();
        private readonly HashSet<int> _seenMonsters = new();
        private bool _hasFocus = true;
        private int _nextSeed;
        private bool _repeating;
        private bool _repeatWait;
        private bool _halted;
        private double _repeatAt;

        public CrawlFlow(CrawlConfig config, int firstSeed, double repeatDelay = 0.22, double repeatInterval = 0.07)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            if (!double.IsFinite(repeatDelay) || repeatDelay <= 0 || repeatDelay > 2) { throw new ArgumentOutOfRangeException(nameof(repeatDelay)); }
            if (!double.IsFinite(repeatInterval) || repeatInterval <= 0 || repeatInterval > 1) { throw new ArgumentOutOfRangeException(nameof(repeatInterval)); }
            _config = config.Copy();
            _nextSeed = firstSeed;
            RepeatDelay = repeatDelay;
            RepeatInterval = repeatInterval;
        }

        public double RepeatDelay { get; }

        public double RepeatInterval { get; }

        public CrawlConfig Config => _config;

        public AppScreen Screen { get; private set; }

        /// <summary>画面に出してよい情報。試合がなければ null。</summary>
        public PlayerView? View => Run?.View;

        /// <summary>始めた試合の数（新しい試合の始まりを描画が知るため）。</summary>
        public int RunNumber { get; private set; }

        /// <summary>今の試合のシード。</summary>
        public int Seed { get; private set; }

        public MessageLog Messages { get; } = new();

        /// <summary>持ち物の画面で選んでいる番号。</summary>
        public int Selected { get; private set; }

        public IPilot? Pilot { get; set; }

        /// <summary>全体の地図を重ねて出すか（プレイ中に地図のキーを押している間だけ）。</summary>
        public bool MapShown => Screen == AppScreen.Playing && _held.IsHeld(Control.Map);

        /// <summary>連続移動が止まっていて、方向キーを押し直すまで再開しないか。</summary>
        public bool RepeatHalted => _halted;

        internal CrawlRun? Run { get; private set; }

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                    if (Screen == AppScreen.Title) { SwitchTo(AppScreen.HowTo); }
                    else if (Screen == AppScreen.HowTo) { Begin(); }
                    else if (Screen == AppScreen.Playing) { Execute(Command.Descend()); }
                    else if (Screen == AppScreen.Inventory) { UseSelected(Command.Use(Selected)); }
                    else if (Screen == AppScreen.Paused) { SwitchTo(AppScreen.Playing); }
                    else if (Screen == AppScreen.Result) { ToTitle(); }
                    break;
                case IntentKind.Back:
                    if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
                    else if (Screen == AppScreen.Paused || Screen == AppScreen.Inventory) { SwitchTo(AppScreen.Playing); }
                    else if (Screen == AppScreen.HowTo) { SwitchTo(AppScreen.Title); }
                    break;
                case IntentKind.ToggleInventory:
                    if (Screen == AppScreen.Playing) { OpenInventory(); }
                    else if (Screen == AppScreen.Inventory) { SwitchTo(AppScreen.Playing); }
                    break;
                case IntentKind.BackToTitle:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result) { ToTitle(); }
                    break;
                case IntentKind.Quit:
                    break;
                case IntentKind.KeyDown:
                    KeyDown(intent);
                    break;
                case IntentKind.KeyUp:
                    KeyUp(intent);
                    break;
                case IntentKind.InventoryMove:
                    if (Screen == AppScreen.Inventory && Run!.View.Inventory.Count > 0)
                    {
                        Selected = MoveSelection(Selected, intent.Argument, Run.View.Inventory.Count);
                    }

                    break;
                case IntentKind.InventoryUse:
                    if (Screen == AppScreen.Inventory) { UseSelected(Command.Use(Selected)); }
                    break;
                case IntentKind.InventoryThrow:
                    if (Screen == AppScreen.Inventory) { UseSelected(Command.Throw(Selected)); }
                    break;
                case IntentKind.InventoryDrop:
                    if (Screen == AppScreen.Inventory) { UseSelected(Command.Drop(Selected)); }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "未定義の操作です。");
            }
        }

        /// <summary>押したままの連続移動を時刻まで進める（1 フレームで 1 歩まで）。</summary>
        public void Advance(double now)
        {
            if (!_hasFocus || Screen != AppScreen.Playing || !_repeating || _halted || !double.IsFinite(now)) { return; }
            if (now < _repeatAt) { return; }
            if (_repeatWait)
            {
                Repeat(Command.Wait());
            }
            else
            {
                var dir = _held.Direction();
                if (dir == null || _held.IsHeld(Control.Turn))
                {
                    _repeating = false;
                    return;
                }

                Repeat(Command.Move(dir.Value));
            }

            _repeatAt += RepeatInterval;
            if (_repeatAt <= now) { _repeatAt = now + RepeatInterval; }
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止し、押しているキーと連続移動を捨てる。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
            StopKeys();
        }

        /// <summary>操縦役で 1 操作進める（撮影・テスト用）。プレイ中でなければ何もしない。</summary>
        public Outcome StepPilot()
        {
            if (Screen != AppScreen.Playing || Pilot == null) { return Outcome.Reject(RejectReason.NotPlaying); }
            return Execute(Pilot.Decide(Run!.View));
        }

        /// <summary>前回から見えた出来事（音を鳴らすため）。取り出すと空になる。</summary>
        public void TakeEvents(List<RunEvent> into)
        {
            into.AddRange(_pendingEvents);
            _pendingEvents.Clear();
        }

        private void KeyDown(Intent intent)
        {
            // 押しっぱなしのキーを画面の切り替えの後へ持ち越さない（プレイ中に押したものだけ）。
            if (Screen != AppScreen.Playing) { return; }
            _held.Press(intent.Control, intent.Key);
            switch (intent.Control)
            {
                case Control.Wait:
                    _halted = false;
                    Execute(Command.Wait());
                    StartRepeat(true, intent.Time);
                    return;
                case Control.Pickup:
                    Execute(Command.Pickup());
                    return;
                case Control.Descend:
                    Execute(Command.Descend());
                    return;
                case Control.Diagonal:
                case Control.Turn:
                case Control.Map:
                    return;
            }

            // 方向キー。
            var dir = _held.Direction();
            if (dir == null) { return; }
            if (_held.IsHeld(Control.Turn))
            {
                Execute(Command.Face(dir.Value));
                return;
            }

            _halted = false;
            Repeat(Command.Move(dir.Value));
            StartRepeat(false, intent.Time);
        }

        private void KeyUp(Intent intent)
        {
            _held.Release(intent.Control, intent.Key);
            if (!_repeating) { return; }
            if (_repeatWait ? !_held.IsHeld(Control.Wait) : _held.Direction() == null) { _repeating = false; }
        }

        private void StartRepeat(bool wait, double time)
        {
            if (Screen != AppScreen.Playing) { return; }
            _repeating = true;
            _repeatWait = wait;
            _repeatAt = time + RepeatDelay;
        }

        /// <summary>
        /// 選択を動かした先。左右（±1）は並びの順に端で折り返し、上下（±<see cref="InventoryColumns"/>）は同じ列の中で端から反対の端へ折り返す。
        /// </summary>
        public static int MoveSelection(int selected, int delta, int count)
        {
            if (count <= 0) { throw new ArgumentOutOfRangeException(nameof(count)); }
            if (selected < 0 || selected >= count) { throw new ArgumentOutOfRangeException(nameof(selected)); }
            if (delta != InventoryColumns && delta != -InventoryColumns) { return (((selected + delta) % count) + count) % count; }
            var target = selected + delta;
            if (target >= 0 && target < count) { return target; }
            var column = selected % InventoryColumns;
            if (delta > 0) { return column; }
            var last = count - 1;
            return last - ((last - column) % InventoryColumns);
        }

        /// <summary>連続移動の 1 歩。止める出来事があれば、押し直すまで止める。</summary>
        private void Repeat(Command command)
        {
            var before = new HashSet<int>(_seenMonsters);
            var outcome = Execute(command);
            if (!outcome.Accepted || Screen != AppScreen.Playing)
            {
                _halted = true;
                return;
            }

            foreach (var id in _seenMonsters)
            {
                if (!before.Contains(id)) { _halted = true; }
            }

            foreach (var e in Run!.View.Events)
            {
                if (StopsRepeat(e)) { _halted = true; }
            }
        }

        private static bool StopsRepeat(RunEvent e) => e.Kind switch
        {
            EventKind.Attack or EventKind.Shot or EventKind.Killed or EventKind.Stolen or EventKind.PickedUp
                or EventKind.FullCannotPickUp or EventKind.Hungry or EventKind.Starving or EventKind.Descended
                or EventKind.MonsterWoke or EventKind.LevelUp or EventKind.Warped => true,
            _ => false,
        };

        private Outcome Execute(Command command)
        {
            var run = Run!;
            var outcome = run.Submit(command);
            if (outcome.Accepted)
            {
                Messages.AddEvents(run.View);
                _pendingEvents.AddRange(run.View.Events);
            }
            else if (outcome.Reason != RejectReason.Blocked)
            {
                Messages.Add(Names.Reason(outcome.Reason));
            }

            _seenMonsters.Clear();
            foreach (var m in run.View.Monsters) { _seenMonsters.Add(m.Id); }
            if (run.State != RunState.Playing) { SwitchTo(AppScreen.Result); }
            return outcome;
        }

        private void UseSelected(Command command)
        {
            if (Run!.View.Inventory.Count == 0) { return; }
            var outcome = Execute(command);
            if (outcome.Accepted && Screen == AppScreen.Inventory) { SwitchTo(AppScreen.Playing); }
        }

        private void OpenInventory()
        {
            var count = Run!.View.Inventory.Count;
            Selected = count == 0 ? 0 : Math.Min(Selected, count - 1);
            SwitchTo(AppScreen.Inventory);
        }

        private void Begin()
        {
            Seed = _nextSeed;
            _nextSeed = unchecked(_nextSeed + 1);
            Run = new CrawlRun(_config, Seed);
            RunNumber++;
            Selected = 0;
            Messages.Clear();
            Messages.Add("1 階に降り立った。5 階の階段を目指せ。");
            _pendingEvents.Clear();
            _seenMonsters.Clear();
            foreach (var m in Run.View.Monsters) { _seenMonsters.Add(m.Id); }
            Pilot?.Reset();
            SwitchTo(AppScreen.Playing);
        }

        private void ToTitle()
        {
            Run = null;
            SwitchTo(AppScreen.Title);
        }

        private void SwitchTo(AppScreen screen)
        {
            Screen = screen;
            StopKeys();
        }

        private void StopKeys()
        {
            _held.Clear();
            _repeating = false;
            _halted = false;
        }
    }
}
