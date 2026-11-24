using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum RacePhase
    {
        Staging,
        Racing,
        Over,
    }

    /// <summary>1 Tick に起きた、音と表示のための出来事。</summary>
    [Flags]
    public enum RaceMoment
    {
        None = 0,
        Wall = 1,
        Boost = 2,
        Lap = 4,
        Finished = 8,
        Over = 16,
    }

    /// <summary>
    /// 1 レース。Tick の順は、全員の操作、各車の移動と壁、各車のゲート、順位、終了判定。
    /// 車の動きと、周回・順位は別の型が決める。
    /// </summary>
    public sealed class Race
    {
        private readonly KartConfig _config;
        private readonly Track _track;
        private readonly Kart[] _karts;
        private readonly Vec2i[] _path = new Vec2i[4];
        private readonly int[] _order = { 0, 1, 2, 3 };
        private readonly int[] _places = new int[KartConfig.KartCount];

        public Race(KartConfig config, Track track)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _track = track ?? throw new ArgumentNullException(nameof(track));
            _config = config.Validate();
            _karts = Grid(track);
            Standings.Write(_karts, track.CheckpointCount, _places, _order);
        }

        private Race(KartConfig config, Track track, Kart[] karts, int tick, RacePhase phase)
        {
            _config = config;
            _track = track;
            _karts = new Kart[KartConfig.KartCount];
            for (var i = 0; i < _karts.Length; i++) { _karts[i] = karts[i]; }
            Tick = tick;
            Phase = phase;
            Standings.Write(_karts, track.CheckpointCount, _places, _order);
        }

        public int Tick { get; private set; }

        public RacePhase Phase { get; private set; } = RacePhase.Staging;

        public KartConfig Config => _config.Copy();

        public Track Track => _track;

        public bool IsOver => Phase == RacePhase.Over;

        public Kart KartAt(int index) => _karts[index];

        public int PlaceOf(int index) => _places[index];

        public Race Clone() => new(_config, _track, _karts, Tick, Phase);

        /// <summary>プレイヤーのドリフトを、ブーストにせず消す（フォーカスを失ったとき）。</summary>
        public void CancelDrift()
        {
            _karts[0].DriftTicks = 0;
        }

        /// <summary>1 Tick。終わった後は何もしない。<paramref name="player"/> は 0 番の操作。1〜3 番は速度計画。</summary>
        public RaceMoment Step(Drive player)
        {
            if (Phase == RacePhase.Over) { return RaceMoment.None; }
            Tick++;
            if (Tick <= _config.StartDelayTicks)
            {
                Phase = RacePhase.Staging;
                return RaceMoment.None;
            }

            Phase = RacePhase.Racing;
            var moment = RaceMoment.None;
            for (var i = 0; i < _karts.Length; i++)
            {
                var kart = _karts[i];
                if (kart.Finished)
                {
                    _karts[i] = kart;
                    continue;
                }

                var lapsBefore = kart.Laps;
                var placeBefore = new TrackPlace(kart.Station, kart.Along);
                var drive = i == 0 ? player : CpuPlan.Intent(kart, _track, _config, _config.PaceOf(i));
                if (kart.RecoverLeft > 0) { kart.RecoverLeft--; }
                var delta = Vehicle.Integrate(_config, ref kart, drive, out var released);
                var hit = Move(ref kart, delta);
                if (hit)
                {
                    kart.DriftTicks = 0;
                    released = 0;
                    moment |= RaceMoment.Wall;
                }
                else if (released >= _config.MiniTurboTicks)
                {
                    kart.BoostLeft = _config.BoostTicks;
                    moment |= RaceMoment.Boost;
                }

                var place = _track.Project(kart.Position, kart.Station);
                kart.Station = place.Station;
                kart.Along = place.Along;
                if (!kart.Finished)
                {
                    kart.SectionAlong = _track.SectionProgress(place, kart.NextGate);
                    var advanced = kart.Laps > lapsBefore || _track.AdvanceOf(placeBefore, place) > 30;
                    if (advanced) { kart.StallTicks = 0; }
                    else
                    {
                        kart.StallTicks++;
                        if (kart.StallTicks >= _config.StuckTicks)
                        {
                            kart.RecoverLeft = _config.RecoverTicks;
                            kart.StallTicks = 0;
                        }
                    }
                }

                if (kart.Laps > lapsBefore) { moment |= RaceMoment.Lap; }
                if (kart.Finished) { moment |= RaceMoment.Finished; }
                _karts[i] = kart;
            }

            Standings.Write(_karts, _track.CheckpointCount, _places, _order);
            if (AllFinished() || Tick >= _config.MaxRaceTicks)
            {
                Phase = RacePhase.Over;
                moment |= RaceMoment.Over;
            }

            return moment;
        }

        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Tick);
            hash.Mix((int)Phase);
            MixConfig(ref hash);
            _track.Mix(ref hash);
            for (var i = 0; i < _karts.Length; i++)
            {
                var kart = _karts[i];
                hash.Mix(kart.Position.X);
                hash.Mix(kart.Position.Y);
                hash.Mix(kart.Heading);
                hash.Mix(kart.Speed);
                hash.Mix(kart.DriftTicks);
                hash.Mix(kart.DriftSteer);
                hash.Mix(kart.BoostLeft);
                hash.Mix(kart.Laps);
                hash.Mix(kart.NextGate);
                hash.Mix(kart.Station);
                hash.Mix(kart.Along);
                hash.Mix(kart.SectionAlong);
                hash.Mix(kart.StallTicks);
                hash.Mix(kart.RecoverLeft);
                hash.Mix(kart.FinishTick);
                hash.Mix(kart.Finished);
                hash.Mix(_places[i]);
            }

            return hash.Value;
        }

        private bool Move(ref Kart kart, Vec2i delta)
        {
            var hit = false;
            var count = 0;
            _path[count++] = kart.Position;
            var position = kart.Position;
            for (var pass = 0; pass < 2 && (delta.X != 0 || delta.Y != 0); pass++)
            {
                var contact = WallContact.First(position, delta, _track.Walls, _config.Radius);
                if (!contact.IsHit)
                {
                    position += delta;
                    delta = Vec2i.Zero;
                    break;
                }

                hit = true;
                var traveled = new Vec2i(
                    (int)(((long)delta.X * contact.Numerator) / contact.Denominator),
                    (int)(((long)delta.Y * contact.Numerator) / contact.Denominator));
                position += traveled;
                var wall = _track.Walls[contact.Index];
                position = WallContact.PushOut(position, wall, _config.Radius);
                delta = WallContact.Slide(delta - traveled, wall.Inward);
                if (count < _path.Length) { _path[count++] = position; }
                if (Crossed(ref kart, count)) { delta = Vec2i.Zero; }
            }

            if (!kart.Finished && (delta.X != 0 || delta.Y != 0)) { position += delta; }
            if (StillInside(ref position))
            {
                hit = true;
                kart.Speed = 0;
            }
            if (count < _path.Length) { _path[count++] = position; }
            kart.Position = position;
            if (!kart.Finished) { Crossed(ref kart, count); }
            if (kart.Finished) { kart.Speed = 0; }
            return hit;
        }

        private bool Crossed(ref Kart kart, int count)
        {
            for (var i = 1; i < count; i++)
            {
                if (kart.Finished) { return true; }
                if (Progress.Cross(_track, ref kart, _path[i - 1], _path[i], Tick)) { return true; }
            }

            return false;
        }

        private bool StillInside(ref Vec2i position)
        {
            var stuck = false;
            var walls = _track.Walls;
            for (var pass = 0; pass < 8; pass++)
            {
                var moved = false;
                for (var i = 0; i < walls.Length; i++)
                {
                    if (!WallContact.CloserThan(position, walls[i].A, walls[i].B, _config.Radius)) { continue; }
                    position = WallContact.PushOut(position, walls[i], _config.Radius);
                    stuck = true;
                    moved = true;
                }

                if (!moved) { break; }
            }

            return stuck;
        }

        private bool AllFinished()
        {
            for (var i = 0; i < _karts.Length; i++)
            {
                if (!_karts[i].Finished) { return false; }
            }

            return true;
        }

        private void MixConfig(ref StateHash hash)
        {
            hash.Mix(_config.Radius);
            hash.Mix(_config.Accel);
            hash.Mix(_config.MaxSpeed);
            hash.Mix(_config.BoostSpeed);
            hash.Mix(_config.Drag);
            hash.Mix(_config.Brake);
            hash.Mix(_config.ReverseSpeed);
            hash.Mix(_config.TurnAtLow);
            hash.Mix(_config.TurnAtHigh);
            hash.Mix(_config.DriftTurn);
            hash.Mix(_config.DriftDrag);
            hash.Mix(_config.DriftMinSpeed);
            hash.Mix(_config.SlipAngle);
            hash.Mix(_config.MiniTurboTicks);
            hash.Mix(_config.BoostTicks);
            hash.Mix(_config.Lookahead);
            hash.Mix(_config.DriftCorner);
            hash.Mix(_config.CornerSpeed);
            hash.Mix(_config.StuckTicks);
            hash.Mix(_config.RecoverTicks);
            hash.Mix(_config.CpuPace1);
            hash.Mix(_config.CpuPace2);
            hash.Mix(_config.CpuPace3);
            hash.Mix(_config.StartDelayTicks);
            hash.Mix(_config.MaxRaceTicks);
        }

        private static Kart[] Grid(Track track)
        {
            var station = track.CheckpointStation(0);
            var heading = track.Direction(station);
            var origin = track.StationPosition(station);
            var forward = new Vec2i(Trig.Cos(heading), Trig.Sin(heading));
            var back = new Vec2i(-forward.X, -forward.Y);
            var right = new Vec2i(forward.Y, -forward.X);
            var karts = new Kart[KartConfig.KartCount];
            for (var i = 0; i < karts.Length; i++)
            {
                var backMillimeters = 2000 + ((KartConfig.KartCount - 1 - i) * 2200);
                var side = IntMath.Scale(right, 1200);
                if ((i % 2) == 0) { side = new Vec2i(-side.X, -side.Y); }
                var kart = new Kart
                {
                    Position = origin + IntMath.Scale(back, backMillimeters) + side,
                    Heading = heading,
                    NextGate = 1,
                    FinishTick = -1,
                };
                var place = track.Project(kart.Position, station);
                kart.Station = place.Station;
                kart.Along = place.Along;
                kart.SectionAlong = track.SectionProgress(place, 1);
                karts[i] = kart;
            }

            return karts;
        }
    }
}
