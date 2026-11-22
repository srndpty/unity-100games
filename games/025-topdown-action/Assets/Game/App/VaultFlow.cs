using System;
using Game.Core;
using TileMotion;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        Playing,
        Paused,

        /// <summary>体力が 0 になった。決定で入口から再開する。</summary>
        GameOver,

        /// <summary>秘宝を取った。</summary>
        Victory,
    }

    /// <summary>キーの代わりに Tick ごとの入力を決める操縦役（撮影・テスト用）。</summary>
    public interface IPilot
    {
        HeroInput InputFor(Dungeon dungeon);
    }

    /// <summary>
    /// 画面遷移と、プレイヤーの意図を Core へ渡す窓口（023 の ExploreFlow と同じ時刻の扱い）。
    /// <para>
    /// 時刻と Tick の対応：プレイを始めた（再開した）時刻を t0 とし、Tick n は区間 [t0 + (n-1)/60, t0 + n/60) を表す。
    /// 時刻 τ の入力は、τ を含む区間の Tick から効く（その前の Tick を先に進めてから入力を反映する）。
    /// </para>
    /// <para>
    /// 1 フレームの処理量：<see cref="Apply"/> と <see cref="Advance"/> は 1 フレームで合わせて <see cref="MaxTicksPerFrame"/> Tick までしか
    /// 進めない。上限を超えた区間は飛ばすが、入力は起きた順にすべて反映する。遊んだ時間（<see cref="PlayedTicks"/>）は飛ばした区間も数える。
    /// </para>
    /// <para>
    /// シード：試合ごとに <c>firstSeed</c> から 1 ずつ進めた値を使う（再戦は必ず新しいシード。010 の知見）。
    /// </para>
    /// </summary>
    public sealed class VaultFlow
    {
        /// <summary>1 フレームで進める Tick 数の上限。</summary>
        public const int MaxTicksPerFrame = 5;

        private readonly VaultConfig _config;
        private readonly Func<World> _createWorld;
        private readonly HeldControls _held = new();
        private bool _hasFocus = true;
        private int _nextSeed;
        private double _origin;
        private long _ticksDone;
        private long _playedBase;
        private int _frameBudget = MaxTicksPerFrame;
        private FlowEvents _events;

        /// <param name="config">設定（複製して使う）。</param>
        /// <param name="firstSeed">最初の試合のシード。以後の試合は 1 ずつ進める。</param>
        /// <param name="createWorld">試合ごとにダンジョンを作る関数（既定は本番の 6 部屋）。</param>
        public VaultFlow(VaultConfig config, int firstSeed, Func<World>? createWorld = null)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _nextSeed = firstSeed;
            _createWorld = createWorld ?? Rooms.Create;
        }

        public AppScreen Screen { get; private set; }

        public Dungeon? Dungeon { get; private set; }

        /// <summary>設定されていれば、キー入力の代わりにこれで動かす（撮影・テスト用）。</summary>
        public IPilot? Pilot { get; set; }

        /// <summary>今の試合で遊んだ時間（Tick 単位）。一時停止・ゲームオーバー・フォーカス喪失・タイトルは含まない。勝った Tick で止まる。</summary>
        public long PlayedTicks => _playedBase + _ticksDone;

        /// <summary>直前の試合のクリアタイム（勝っていなければ null）。</summary>
        public long? ClearTicks { get; private set; }

        public void Apply(Intent intent)
        {
            if (!_hasFocus) { return; }
            AdvanceTo(intent.Time);
            switch (intent.Kind)
            {
                case IntentKind.Submit:
                    if (Screen == AppScreen.Title || Screen == AppScreen.Victory) { Begin(intent.Time); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    else if (Screen == AppScreen.GameOver) { ContinueFromEntrance(intent.Time); }
                    break;
                case IntentKind.TogglePause:
                    if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
                    else if (Screen == AppScreen.Paused) { Resume(intent.Time); }
                    break;
                case IntentKind.Restart:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.GameOver || Screen == AppScreen.Victory) { Begin(intent.Time); }
                    break;
                case IntentKind.BackToTitle:
                    if (Screen == AppScreen.Paused || Screen == AppScreen.GameOver || Screen == AppScreen.Victory)
                    {
                        Dungeon = null;
                        SwitchTo(AppScreen.Title);
                    }

                    break;
                case IntentKind.Quit:
                    break;
                case IntentKind.ControlDown:
                    // 押しっぱなしのキーを一時停止・ゲームオーバーの後へ持ち越さないよう、プレイ中に押したものだけを数える。
                    if (Screen == AppScreen.Playing) { _held.Press(intent.Control); }
                    break;
                case IntentKind.ControlUp:
                    _held.Release(intent.Control);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(intent), intent.Kind, "未定義の操作です。");
            }
        }

        /// <summary>フォーカスを失うとプレイ中は一時停止する。戻っても自動では再開しない。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (focus) { return; }
            if (Screen == AppScreen.Playing) { SwitchTo(AppScreen.Paused); }
            _held.Clear();
        }

        /// <summary>時刻 <paramref name="now"/> までの Tick を進め、フレームを終える。1 フレームに 1 回、入力をすべて反映した後に呼ぶ。</summary>
        public void Advance(double now)
        {
            if (_hasFocus) { AdvanceTo(now); }
            EndFrame();
        }

        /// <summary>時計で進めずにフレームを終える（撮影中など、Tick を直接進めるとき）。</summary>
        public void EndFrame() => _frameBudget = MaxTicksPerFrame;

        /// <summary>時刻と無関係に 1 Tick 進める（撮影・テスト用）。プレイ中でなければ何もしない。</summary>
        public void StepTick()
        {
            if (Screen != AppScreen.Playing) { return; }
            _playedBase++;
            Step();
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private void Step()
        {
            var dungeon = Dungeon!;
            var input = _held.TakeTickInput();
            if (Pilot != null) { input = Pilot.InputFor(dungeon); }
            dungeon.Tick(input);
            _events |= FlowEventsOf(dungeon.TakeEvents());
            if (dungeon.Status == DungeonStatus.Down)
            {
                SwitchTo(AppScreen.GameOver);
            }
            else if (dungeon.IsWon)
            {
                ClearTicks = PlayedTicks;
                SwitchTo(AppScreen.Victory);
            }
        }

        private void AdvanceTo(double time)
        {
            if (Screen != AppScreen.Playing || !double.IsFinite(time)) { return; }
            var elapsed = Math.Floor((time - _origin) * Units.TickRate);
            if (elapsed <= _ticksDone) { return; }

            // このフレームの残りの上限を超える区間は、手前を飛ばして直前の Tick だけを進める。
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
            Dungeon = new Dungeon(_config, _createWorld(), _nextSeed);
            _nextSeed = unchecked(_nextSeed + 1);
            ClearTicks = null;
            _playedBase = 0;
            _ticksDone = 0;
            _events |= FlowEvents.Started;
            Resume(time);
        }

        private void ContinueFromEntrance(double time)
        {
            Dungeon!.Continue();
            _events |= FlowEventsOf(Dungeon.TakeEvents());
            Resume(time);
        }

        private void Resume(double time)
        {
            SwitchTo(AppScreen.Playing);
            _playedBase += _ticksDone;
            _origin = time;
            _ticksDone = 0;
        }

        private void SwitchTo(AppScreen screen)
        {
            Screen = screen;
            _held.Clear();
        }

        private static FlowEvents FlowEventsOf(DungeonEvents events) => (FlowEvents)((long)events << 1);
    }

    /// <summary>画面に反映する出来事。試合の出来事（<see cref="DungeonEvents"/>）に、試合の開始を足したもの。</summary>
    [Flags]
    public enum FlowEvents : long
    {
        None = 0,
        Started = 1,
        Swung = (long)DungeonEvents.Swung << 1,
        Rolled = (long)DungeonEvents.Rolled << 1,
        EnemyHit = (long)DungeonEvents.EnemyHit << 1,
        EnemyDefeated = (long)DungeonEvents.EnemyDefeated << 1,
        BossHit = (long)DungeonEvents.BossHit << 1,
        BossDefeated = (long)DungeonEvents.BossDefeated << 1,
        Hurt = (long)DungeonEvents.Hurt << 1,
        DoorOpened = (long)DungeonEvents.DoorOpened << 1,
        DoorLocked = (long)DungeonEvents.DoorLocked << 1,
        ChestOpened = (long)DungeonEvents.ChestOpened << 1,
        RewardShown = (long)DungeonEvents.RewardShown << 1,
        KeyTaken = (long)DungeonEvents.KeyTaken << 1,
        RoomChanged = (long)DungeonEvents.RoomChanged << 1,
        Arrived = (long)DungeonEvents.Arrived << 1,
        Down = (long)DungeonEvents.Down << 1,
        Continued = (long)DungeonEvents.Continued << 1,
        Won = (long)DungeonEvents.Won << 1,
    }
}
