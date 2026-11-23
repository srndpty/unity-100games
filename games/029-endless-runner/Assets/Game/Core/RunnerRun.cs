using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>画面に反映する試合の出来事。</summary>
    [Flags]
    public enum RunEvents
    {
        None = 0,
        Steered = (int)RunnerEvents.Steered,
        Jumped = (int)RunnerEvents.Jumped,
        Slid = (int)RunnerEvents.Slid,
        Landed = (int)RunnerEvents.Landed,
        Crashed = 16,

        /// <summary>速さの段階が上がった。</summary>
        SpedUp = 32,
    }

    /// <summary>
    /// 1 試合。自機は毎 Tick 自動で前進し、障害物に 1 度触れたら終わる（残機なし）。
    /// 1 Tick の順序：操作の反映（自機の 1 Tick）→ 区間の速さで前進 → 掃引による衝突 → チャンクの回収と生成。
    /// </summary>
    public sealed class RunnerRun
    {
        private readonly RunRules _rules;
        private RunnerBody _body;
        private ChunkTrack _chunks;
        private RunEvents _events;

        public RunnerRun(RunRules rules, int seed)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Seed = seed;
            _body = RunnerBody.Rest(rules.Settings, 1);
            _chunks = new ChunkTrack(rules, seed);
            _chunks.Update(0);
            Speed = Difficulty.Speed(rules.Settings, 0);
        }

        public RunRules Rules => _rules;

        /// <summary>検証済みの設定の複製（<see cref="RunRules.Config"/>）。変えても試合には効かない。</summary>
        public RunConfig Config => _rules.Config;

        public int Seed { get; }

        public RunnerBody Body => _body;

        public ChunkTrack Chunks => _chunks;

        /// <summary>自機の中心の奥行き（スタート地点が 0）。</summary>
        public long Z { get; private set; }

        /// <summary>直前の Tick の始めの奥行き。</summary>
        public long PrevZ { get; private set; }

        /// <summary>直前の Tick の前進量。</summary>
        public int Speed { get; private set; }

        public long Ticks { get; private set; }

        public bool IsOver { get; private set; }

        /// <summary>障害物に触れた時刻（最後の Tick の中の割合）。<see cref="IsOver"/> のときだけ意味がある。</summary>
        public Moment CrashTime { get; private set; }

        /// <summary>触れた障害物の種類。</summary>
        public ObstacleKind CrashKind { get; private set; }

        /// <summary>得点：進んだ距離（m、切り捨て）。</summary>
        public long Meters => Z / Units.Tile;

        public int SpeedLevel => Difficulty.SpeedLevel(_rules.Settings, _chunks.SegmentAt(Z).Start);

        public void Step(Command command)
        {
            if (IsOver) { return; }
            var config = _rules.Settings;
            var segment = _chunks.SegmentAt(Z);
            if (Ticks > 0 && segment.Speed > Speed) { _events |= RunEvents.SpedUp; }
            _events |= (RunEvents)_body.Step(command, config);
            PrevZ = Z;
            Speed = segment.Speed;
            Z += Speed;
            Ticks++;
            Collide();
            _chunks.Update(Z);
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public RunEvents TakeEvents()
        {
            var events = _events;
            _events = RunEvents.None;
            return events;
        }

        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Ticks);
            hash.Mix(Z);
            hash.Mix(PrevZ);
            hash.Mix(Speed);
            hash.Mix(IsOver);
            if (IsOver)
            {
                // 結果に出る値（ぶつかった障害物の種類と時刻）も混ぜ、違う結果を同じハッシュにしない。
                hash.Mix((int)CrashKind);
                hash.Mix(CrashTime.Numerator);
                hash.Mix(CrashTime.Denominator);
            }
            _body.MixInto(ref hash);
            _chunks.MixInto(ref hash);
            return hash.Value;
        }

        public RunnerRun Clone()
        {
            var copy = (RunnerRun)MemberwiseClone();
            copy._chunks = _chunks.Clone();
            return copy;
        }

        private void Collide()
        {
            var config = _rules.Settings;
            var lane = _body.Lane;
            var vertical = _body.Vertical;
            var reach = (config.RunnerDepth / 2) + config.RowLength;
            var earliest = Moment.One;
            var hit = false;
            for (var i = 0; i < _chunks.Count; i++)
            {
                var start = _chunks.StartAt(i);
                var chunk = _chunks.ChunkAt(i);

                // 自機の軌跡の近くにあるチャンクだけを見る（座標を int に収める）。
                if (start > Z + reach || start + chunk.Length(config) < PrevZ - reach) { continue; }
                var from = RunnerShape.From(config, lane, vertical, (int)(PrevZ - start));
                var to = RunnerShape.To(config, lane, vertical, (int)(Z - start));
                foreach (var obstacle in chunk.Obstacles)
                {
                    // Hurdle は上から触れても当たりにしない（踏み越えた扱い）。自動操縦とチャンクの検証（Planner）は、
                    // 上から触れない厳しい判定で道を求めるので、この判定でも必ず通れる。
                    bool touches;
                    Moment contact;
                    if (obstacle.Kind == ObstacleKind.Hurdle)
                    {
                        touches = Sweep3.HitsFromFrontOrSide(from, to, obstacle.Box(config), out contact);
                    }
                    else
                    {
                        var span = Sweep3.Boxes(from, to, obstacle.Box(config));
                        touches = span.Hits;
                        contact = span.Contact;
                    }

                    if (!touches || (hit && contact >= earliest)) { continue; }
                    hit = true;
                    earliest = contact;
                    CrashKind = obstacle.Kind;
                }
            }

            if (!hit) { return; }
            IsOver = true;
            CrashTime = earliest;
            _events |= RunEvents.Crashed;
        }
    }
}
