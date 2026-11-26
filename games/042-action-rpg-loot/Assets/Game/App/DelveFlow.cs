using System;
using Game.Core;
using IntGeometry;
using TileMotion;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        HowTo,
        Playing,

        /// <summary>持ち物と装備の画面。開いている間は Tick が進まない。</summary>
        Inventory,
        Paused,
        Result,
    }

    /// <summary>キーとマウスの代わりに Tick ごとの操作を決める操縦役（撮影・テスト用）。持ち物の付け替えも行ってよい。</summary>
    public interface IPilot
    {
        HeroCommand CommandFor(DelveRun run);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口（026 の SiegeFlow と同じ時刻の扱い）。
    /// <para>時刻と Tick：プレイを始めた（再開した）時刻を t0 とし、Tick n は [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は τ を含む区間の Tick から効く。1 フレームで進める Tick は <see cref="MaxTicksPerFrame"/> まで。</para>
    /// <para>照準はマウスの「今の値」なので <see cref="SetAim"/> で毎フレーム渡す。</para>
    /// <para>シード：試合ごとに <c>firstSeed</c> から 1 ずつ進める（再戦は必ず新しいシード、010）。</para>
    /// </summary>
    public sealed class DelveFlow
    {
        public const int MaxTicksPerFrame = 5;

        private readonly DelveConfig _config;
        private readonly LootData _loot;
        private readonly FloorSet _floors;
        private readonly HeldControls _held = new();
        private bool _hasFocus = true;
        private int _nextSeed;
        private double _origin;
        private long _ticksDone;
        private int _frameBudget = MaxTicksPerFrame;
        private Vec2i _aim;
        private RunEvents _events;

        public DelveFlow(DelveConfig config, LootData loot, FloorSet floors, int firstSeed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(loot);
            _config = config.Copy();
            _loot = loot;
            _floors = floors ?? throw new ArgumentNullException(nameof(floors));
            _nextSeed = firstSeed;
        }

        public AppScreen Screen { get; private set; }

        public DelveRun? Run { get; private set; }

        /// <summary>始めた試合の数（新しい試合の始まりを描画が知るため）。</summary>
        public int RunNumber { get; private set; }

        public IPilot? Pilot { get; set; }

        public Vec2i Aim => _aim;

        /// <summary>直前の持ち物の操作の結果（画面の案内）。</summary>
        public ItemResult LastItemResult { get; private set; }

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                    if (Screen == AppScreen.Title) { SwitchTo(AppScreen.HowTo); }
                    else if (Screen == AppScreen.HowTo) { Begin(intent.Time); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    else if (Screen == AppScreen.Result) { ToTitle(); }
                    break;
                case IntentKind.Back:
                    if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
                    else if (Screen == AppScreen.Paused || Screen == AppScreen.Inventory) { Resume(intent.Time); }
                    else if (Screen == AppScreen.HowTo) { SwitchTo(AppScreen.Title); }
                    break;
                case IntentKind.ToggleInventory:
                    if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Inventory); }
                    else if (Screen == AppScreen.Inventory) { Resume(intent.Time); }
                    break;
                case IntentKind.BackToTitle:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.Result) { ToTitle(); }
                    break;
                case IntentKind.Quit:
                    break;
                case IntentKind.ControlDown:
                    // 押しっぱなしのキーを画面の切り替えの後へ持ち越さない（プレイ中に押したものだけ）。
                    if (Screen == AppScreen.Playing) { _held.Press(intent.Control, intent.Key); }
                    break;
                case IntentKind.ControlUp:
                    _held.Release(intent.Control, intent.Key);
                    break;
                case IntentKind.Equip:
                    if (Screen == AppScreen.Inventory) { LastItemResult = Run!.Equip(ToIndex(intent.Argument)); }
                    break;
                case IntentKind.Unequip:
                    if (Screen == AppScreen.Inventory && intent.Argument >= 0 && intent.Argument < SlotInfo.Count)
                    {
                        LastItemResult = Run!.Unequip((Slot)intent.Argument);
                    }

                    break;
                case IntentKind.Discard:
                    if (Screen == AppScreen.Inventory) { LastItemResult = Run!.Discard(ToIndex(intent.Argument)); }
                    break;
                case IntentKind.PickGround:
                    if (Screen == AppScreen.Playing) { LastItemResult = Run!.PickUp(intent.Argument); }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "未定義の操作です。");
            }
        }

        public void SetAim(Vec2i point) => _aim = point;

        /// <summary>フォーカスを失うとプレイ中は一時停止し、押しているキーを捨てる。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
            _held.Clear();
        }

        public void Advance(double now)
        {
            if (_hasFocus) { AdvanceTo(now); }
            EndFrame();
        }

        public void EndFrame() => _frameBudget = MaxTicksPerFrame;

        /// <summary>時刻と無関係に 1 Tick 進める（撮影・テスト用）。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            Step();
        }

        public RunEvents TakeEvents()
        {
            var e = _events;
            _events = RunEvents.None;
            return e;
        }

        private static int ToIndex(long argument) => argument < 0 || argument > int.MaxValue ? -1 : (int)argument;

        private void Step()
        {
            var run = Run!;
            HeroCommand command;
            if (Pilot != null)
            {
                command = Pilot.CommandFor(run);
            }
            else
            {
                var (attack, skill, potion, pick) = _held.Take();
                command = new HeroCommand(_held.Horizontal, _held.Vertical, _aim, attack, skill, potion, pick);
            }

            run.Step(command);
            _events |= run.TakeEvents();
            if (run.IsOver) { SwitchTo(AppScreen.Result); }
        }

        private void AdvanceTo(double time)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * Units.TickRate);
            if (elapsed <= _ticksDone) { return; }
            var target = elapsed > long.MaxValue / 2 ? long.MaxValue / 2 : (long)elapsed;
            if (target - _ticksDone > _frameBudget) { _ticksDone = target - _frameBudget; }
            while (_ticksDone < target && Screen == AppScreen.Playing)
            {
                _ticksDone++;
                _frameBudget--;
                Step();
            }
        }

        private void Begin(double time)
        {
            Run = new DelveRun(_config, _loot, _floors, _nextSeed);
            _nextSeed = unchecked(_nextSeed + 1);
            RunNumber++;
            _aim = Run.Hero.Position + new Vec2i(Units.Tile, 0);
            LastItemResult = ItemResult.Done;
            Resume(time);
        }

        private void ToTitle()
        {
            Run = null;
            SwitchTo(AppScreen.Title);
        }

        private void Resume(double time)
        {
            SwitchTo(AppScreen.Playing);
            _origin = time;
            _ticksDone = 0;
        }

        private void SwitchTo(AppScreen screen)
        {
            Screen = screen;
            _held.Clear();
        }
    }
}
