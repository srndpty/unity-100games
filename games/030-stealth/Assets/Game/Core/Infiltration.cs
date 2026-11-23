using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>施設全体の警戒レベル。</summary>
    public enum AlertLevel
    {
        Normal,

        /// <summary>警戒：巡回が速く、視程が長い。</summary>
        Caution,

        /// <summary>厳戒：誰かが追跡している（または追跡が終わって間もない）。</summary>
        Alert,
    }

    /// <summary>
    /// 警戒レベルの移り変わり。誰かが追跡している Tick は厳戒（残りを <see cref="WardConfig.AlertTicks"/> に戻す）。
    /// 誰も追跡していない Tick が <see cref="WardConfig.AlertTicks"/> 続いたら警戒へ、さらに <see cref="WardConfig.CautionTicks"/> 続いたら平常へ下がる。
    /// </summary>
    public struct AlertState
    {
        public AlertLevel Level { get; private set; }

        /// <summary>今のレベルから下がるまでの残り Tick 数（平常では 0）。</summary>
        public int Remaining { get; private set; }

        /// <summary>1 Tick 進める。平常・警戒から厳戒へ上がったら true（発見の 1 回）。</summary>
        public bool Update(bool anyChasing, WardConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (anyChasing)
            {
                var rose = Level != AlertLevel.Alert;
                Level = AlertLevel.Alert;
                Remaining = config.AlertTicks;
                return rose;
            }

            if (Level == AlertLevel.Normal) { return false; }
            Remaining--;
            if (Remaining > 0) { return false; }
            if (Level == AlertLevel.Alert)
            {
                Level = AlertLevel.Caution;
                Remaining = config.CautionTicks;
            }
            else
            {
                Level = AlertLevel.Normal;
                Remaining = 0;
            }

            return false;
        }
    }

    /// <summary>警備員（判断と体の組）。</summary>
    public sealed class Guard
    {
        internal Guard(GuardPlan plan, GuardBrain brain, GuardBody body, VisionSpec vision)
        {
            Plan = plan;
            Brain = brain;
            Body = body;
            Perceived(body.Position, body.Facing, vision);
        }

        public GuardPlan Plan { get; }

        public GuardBrain Brain { get; }

        public GuardBody Body { get; }

        /// <summary>直前の Tick に侵入者が見えていたか。</summary>
        public bool Sees { get; internal set; }

        /// <summary>直前の Tick に足音が聞こえたか。</summary>
        public bool Hears { get; internal set; }

        /// <summary>直前の Tick の知覚に使った目の位置（その Tick に動く前の位置）。視界の扇はこの姿勢で描く。</summary>
        public Vec2i SightOrigin { get; private set; }

        /// <summary>直前の Tick の知覚に使った向き。</summary>
        public int SightFacing { get; private set; }

        /// <summary>直前の Tick の知覚に使った視界（その Tick の始めの警戒レベル）。</summary>
        public VisionSpec SightVision { get; private set; }

        internal void Perceived(Vec2i origin, int facing, VisionSpec vision)
        {
            SightOrigin = origin;
            SightFacing = facing;
            SightVision = vision;
        }

        internal Guard Clone()
        {
            var copy = new Guard(Plan, Brain.Clone(), Body.Clone(), SightVision) { Sees = Sees, Hears = Hears };
            copy.Perceived(SightOrigin, SightFacing, SightVision);
            return copy;
        }
    }

    public enum MatchStatus
    {
        Playing,

        /// <summary>追跡中の警備員に触れられた。</summary>
        Caught,

        /// <summary>目的物を持って出口に着いた。</summary>
        Escaped,
    }

    /// <summary>画面に反映する出来事（Tick ごとに積み、<see cref="Infiltration.TakeEvents"/> で取り出す）。</summary>
    [Flags]
    public enum WardEvents
    {
        None = 0,
        Step = 1,

        /// <summary>警備員が調査を始めた。</summary>
        Suspicious = 1 << 1,

        /// <summary>厳戒に上がった（発見）。</summary>
        Discovered = 1 << 2,

        /// <summary>追跡していた警備員が捜索に移った。</summary>
        LostTrack = 1 << 3,

        ObjectiveTaken = 1 << 4,
        Caught = 1 << 5,
        Escaped = 1 << 6,

        /// <summary>警戒レベルが下がった。</summary>
        Calmed = 1 << 7,
    }

    /// <summary>
    /// 試合。1 Tick の順序：自機の移動 → 足音 → 目的物 → 知覚（視覚・聴覚・接触・無線）→ 判断 → 警備員の移動 → 捕獲 → 脱出 → 警戒レベル。
    /// 捕獲と脱出が同じ Tick に起きたら捕獲。終わった試合の Tick は何も変えない。
    /// </summary>
    public sealed class Infiltration
    {
        private readonly WardConfig _config;
        private readonly Guard[] _guards;
        private readonly Hearing _hearing;
        private readonly Vec2i?[] _touched;
        private Cell? _radio;
        private AlertState _alert;
        private WardEvents _events;

        public Infiltration(WardConfig config, Facility facility, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();

            // 検証済みの設定はこの試合だけが持つ（外へは複製しか渡さない）。複製した試合とは共有する。
            _config = config.Copy();
            Facility = facility ?? throw new ArgumentNullException(nameof(facility));
            Seed = seed;
            Intruder = new Intruder(_config, facility.Exit);
            var area = new SearchArea(facility.Map, _config.SearchRadius);
            _guards = new Guard[facility.Guards.Count];
            for (var i = 0; i < _guards.Length; i++)
            {
                var plan = facility.Guards[i];

                // 警備員ごとに乱数の系列を分ける（ある警備員の乱数の使用回数が他に影響しない）。
                var random = new SeededRandom(unchecked((seed * 7919) + ((i + 1) * 104729)));
                _guards[i] = new Guard(plan, new GuardBrain(_config, plan.Route, area, random), new GuardBody(_config, facility.Map, plan.Start, plan.StartFacing), _config.VisionAt(AlertLevel.Normal));
            }

            Guards = Array.AsReadOnly(_guards);
            _hearing = new Hearing(facility.Map);
            _touched = new Vec2i?[_guards.Length];
        }

        private Infiltration(Infiltration source)
        {
            _config = source._config;
            Facility = source.Facility;
            Seed = source.Seed;
            Intruder = source.Intruder.Clone();
            _guards = new Guard[source._guards.Length];
            for (var i = 0; i < _guards.Length; i++) { _guards[i] = source._guards[i].Clone(); }
            Guards = Array.AsReadOnly(_guards);
            _hearing = new Hearing(source.Facility.Map);
            _touched = (Vec2i?[])source._touched.Clone();
            _radio = source._radio;
            _alert = source._alert;
            Status = source.Status;
            Ticks = source.Ticks;
            Discoveries = source.Discoveries;
            HasObjective = source.HasObjective;
            CaughtBy = source.CaughtBy;
            LastNoise = source.LastNoise;
            LastNoiseTick = source.LastNoiseTick;
        }

        /// <summary>検証済みの設定の複製。書き換えても試合には効かない（試合は自分の設定だけを使う）。</summary>
        public WardConfig Config => _config.Copy();

        /// <summary>今の警戒レベルの視界（描画・操縦役用。複製を作らない）。</summary>
        public VisionSpec CurrentVision => _config.VisionAt(_alert.Level);

        /// <summary>歩き方の 1 Tick の移動量（斜めでない向き）。</summary>
        public int SpeedOf(Gait gait) => gait switch
        {
            Gait.Sneak => _config.SneakSpeed,
            Gait.Run => _config.RunSpeed,
            _ => _config.WalkSpeed,
        };

        public Facility Facility { get; }

        public FacilityMap Map => Facility.Map;

        public int Seed { get; }

        public Intruder Intruder { get; }

        public ReadOnlyCollection<Guard> Guards { get; }

        public AlertLevel Level => _alert.Level;

        public int AlertRemaining => _alert.Remaining;

        public MatchStatus Status { get; private set; }

        public long Ticks { get; private set; }

        /// <summary>厳戒に上がった回数。</summary>
        public int Discoveries { get; private set; }

        public bool HasObjective { get; private set; }

        /// <summary>捕まえた警備員の番号（捕まっていなければ -1）。</summary>
        public int CaughtBy { get; private set; } = -1;

        /// <summary>最後に出た足音（まだなければ null）。描画用で、以後の動きには影響しない。</summary>
        public Noise? LastNoise { get; private set; }

        /// <summary><see cref="LastNoise"/> が出た Tick（1 から数える）。</summary>
        public long LastNoiseTick { get; private set; }

        public bool IsOver => Status != MatchStatus.Playing;

        public Infiltration Clone() => new(this);

        public WardEvents TakeEvents()
        {
            var events = _events;
            _events = WardEvents.None;
            return events;
        }

        public void Tick(IntruderInput input)
        {
            if (IsOver) { return; }
            Ticks++;

            var noise = Intruder.Move(Map, input);
            if (noise != null)
            {
                LastNoise = noise;
                LastNoiseTick = Ticks;
                _hearing.Spread(noise.Value.Source, noise.Value.Radius);
                _events |= WardEvents.Step;
            }
            else
            {
                _hearing.Clear();
            }

            if (!HasObjective && Intruder.Motion.Touches(new Box(Facility.Objective.Column * Units.Tile, Facility.Objective.Row * Units.Tile, Units.Tile, Units.Tile)))
            {
                HasObjective = true;
                _events |= WardEvents.ObjectiveTaken;
            }

            var vision = _config.VisionAt(_alert.Level);
            var radio = _radio;
            _radio = null;
            var target = Intruder.Center;
            for (var i = 0; i < _guards.Length; i++)
            {
                var guard = _guards[i];
                var body = guard.Body;
                var seen = Vision.Sees(Map, body.Position, body.Facing, vision, target);
                var distance = seen ? (int)IntMath.Sqrt(Vec2i.DistanceSquared(body.Position, target)) : 0;
                var heard = noise != null && _hearing.Hears(Cell.Of(body.Position)) ? noise.Value.Source : (Cell?)null;
                guard.Sees = seen;
                guard.Hears = heard != null;
                guard.Perceived(body.Position, body.Facing, vision);
                var percept = new Percept(seen ? target : (Vec2i?)null, distance, heard, radio, _touched[i], body.StandingOn, body.Facing);
                _touched[i] = null;

                var before = guard.Brain.Mode;
                var order = guard.Brain.Think(percept, vision.Range);
                var after = guard.Brain.Mode;
                if (after == GuardMode.Investigate && before != GuardMode.Investigate) { _events |= WardEvents.Suspicious; }
                if (before == GuardMode.Chase && after == GuardMode.Search) { _events |= WardEvents.LostTrack; }
                body.Execute(order, _alert.Level);
            }

            var anyChasing = false;
            Cell? chaseStart = null;
            for (var i = 0; i < _guards.Length; i++)
            {
                var guard = _guards[i];
                if (guard.Brain.Mode == GuardMode.Chase)
                {
                    anyChasing = true;
                    if (guard.Brain.StartedChase && chaseStart == null) { chaseStart = guard.Brain.Goal; }
                }

                if (!BoxSweep.Touches(Intruder.Motion, guard.Body)) { continue; }
                if (guard.Brain.Mode == GuardMode.Chase)
                {
                    if (CaughtBy < 0) { CaughtBy = i; }
                }
                else
                {
                    _touched[i] = target;
                }
            }

            if (CaughtBy >= 0)
            {
                Status = MatchStatus.Caught;
                _events |= WardEvents.Caught;
            }
            else if (HasObjective && Cell.Of(Intruder.Center) == Facility.Exit)
            {
                Status = MatchStatus.Escaped;
                _events |= WardEvents.Escaped;
            }

            var level = _alert.Level;
            if (_alert.Update(anyChasing, _config))
            {
                Discoveries++;
                _events |= WardEvents.Discovered;
                _radio = chaseStart;
            }
            else if (_alert.Level < level)
            {
                _events |= WardEvents.Calmed;
            }
        }

        /// <summary>状態のハッシュ。以後の動きを決める状態（判断・体・自機・警戒・無線・接触・乱数）をすべて含める。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Ticks);
            hash.Mix((int)Status);
            hash.Mix(HasObjective);
            hash.Mix(Discoveries);
            hash.Mix(CaughtBy);
            hash.Mix((int)_alert.Level);
            hash.Mix(_alert.Remaining);
            hash.Mix(_radio != null);
            hash.Mix(_radio?.Column ?? 0);
            hash.Mix(_radio?.Row ?? 0);
            Intruder.MixInto(ref hash);
            for (var i = 0; i < _guards.Length; i++)
            {
                _guards[i].Brain.MixInto(ref hash);
                _guards[i].Body.MixInto(ref hash);
                hash.Mix(_touched[i] != null);
                hash.Mix(_touched[i]?.X ?? 0);
                hash.Mix(_touched[i]?.Y ?? 0);
            }

            return hash.Value;
        }

        /// <summary>テスト・撮影用：自機をマスの中心に置く。</summary>
        public void PlaceIntruder(Cell cell) => Intruder.Place(cell);

        /// <summary>テスト・撮影用：警備員の体をマスの中心に置く（判断の状態はそのまま）。</summary>
        public void PlaceGuard(int index, Cell cell, int facing) => _guards[index].Body.Place(cell, facing);

        /// <summary>テスト・撮影用：目的物を持たせる。</summary>
        public void GiveObjective() => HasObjective = true;
    }
}
