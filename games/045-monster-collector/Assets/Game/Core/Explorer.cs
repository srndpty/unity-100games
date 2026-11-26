using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum FieldStep
    {
        None = 0,
        Moved = 1,
        Blocked = 2,
        Portal = 3,

        /// <summary>草むらで野生のモンスターに出会った（<see cref="Explorer.Encountered"/>）。</summary>
        Encounter = 4,
    }

    public static class DirectionMath
    {
        public static void Delta(Direction dir, out int dx, out int dy)
        {
            switch (dir)
            {
                case Direction.Up:
                    dx = 0;
                    dy = -1;
                    break;
                case Direction.Right:
                    dx = 1;
                    dy = 0;
                    break;
                case Direction.Down:
                    dx = 0;
                    dy = 1;
                    break;
                default:
                    dx = -1;
                    dy = 0;
                    break;
            }
        }
    }

    /// <summary>
    /// フィールドの位置・向き・1 マスの歩き（<see cref="Rules.StepTicks"/> Tick）・先行入力・草むらの遭遇（041 の Explorer を写して直した）。
    /// 遭遇は 1 マス歩き終えたときに、そのマスが草むらなら判定する。遭遇の直後と地図を移った直後の数歩は遭遇しない。
    /// </summary>
    public sealed class Explorer
    {
        private Explorer(string mapId, int x, int y, Direction facing, int stepsSinceEncounter, int totalSteps, RandomSeries rng)
        {
            MapId = mapId;
            X = x;
            Y = y;
            FromX = x;
            FromY = y;
            Facing = facing;
            BufferedDirection = -1;
            StepsSinceEncounter = stepsSinceEncounter;
            TotalSteps = totalSteps;
            EncounterRng = rng;
        }

        public string MapId { get; private set; }

        public int X { get; private set; }

        public int Y { get; private set; }

        public int FromX { get; private set; }

        public int FromY { get; private set; }

        public Direction Facing { get; private set; }

        /// <summary>今の 1 マスの歩きの残り Tick（0 なら止まっている）。</summary>
        public int MoveRemaining { get; private set; }

        public int BufferedDirection { get; private set; }

        public int StepsSinceEncounter { get; private set; }

        public int TotalSteps { get; private set; }

        public RandomSeries EncounterRng { get; private set; }

        public bool IsMoving => MoveRemaining > 0;

        /// <summary>直前の遭遇（種族とレベル）。</summary>
        public SpeciesDef? EncounteredSpecies { get; private set; }

        public int EncounteredLevel { get; private set; }

        public static Explorer Start(MonsterData data, int matchSeed)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            var r = data.Rules;
            return new Explorer(r.StartMapId, r.StartX, r.StartY, Direction.Up, 0, 0, RandomSeries.Derive(matchSeed, 0, (int)RngPurpose.Encounter));
        }

        internal static Explorer Restore(string mapId, int x, int y, Direction facing, int stepsSinceEncounter, int totalSteps, uint rngState) =>
            new(mapId, x, y, facing, stepsSinceEncounter, totalSteps, RandomSeries.FromState(rngState));

        public Explorer Clone()
        {
            var c = new Explorer(MapId, X, Y, Facing, StepsSinceEncounter, TotalSteps, EncounterRng.Clone())
            {
                FromX = FromX,
                FromY = FromY,
                MoveRemaining = MoveRemaining,
                BufferedDirection = BufferedDirection,
                EncounteredSpecies = EncounteredSpecies,
                EncounteredLevel = EncounteredLevel,
            };
            return c;
        }

        public void FrontTile(out int fx, out int fy)
        {
            DirectionMath.Delta(Facing, out var dx, out var dy);
            fx = X + dx;
            fy = Y + dy;
        }

        public void Face(Direction dir)
        {
            if (!IsMoving) { Facing = dir; }
        }

        public void ClearBufferedInput() => BufferedDirection = -1;

        public void WarpTo(string mapId, int x, int y, Direction facing)
        {
            MapId = mapId ?? throw new ArgumentNullException(nameof(mapId));
            X = x;
            Y = y;
            FromX = x;
            FromY = y;
            Facing = facing;
            MoveRemaining = 0;
            BufferedDirection = -1;
            StepsSinceEncounter = 0;
        }

        /// <summary>そのマスへ歩けるか（地形が歩けて、倒していないトレーナーが立っていない）。</summary>
        public static bool CanEnter(MonsterData data, int trainersDefeated, string mapId, int x, int y)
        {
            var map = data.Map(mapId);
            if (!Tiles.IsWalkableTerrain(map.TileAt(x, y))) { return false; }
            var trainer = data.TrainerAt(mapId, x, y);
            return trainer == null || (trainersDefeated & (1 << trainer.Index)) != 0;
        }

        /// <summary>1 Tick 進める。止まっていて方向が押されていれば歩き始める。</summary>
        public FieldStep Tick(MonsterData data, int trainersDefeated, Direction? held)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            if (MoveRemaining > 0)
            {
                MoveRemaining--;
                if (MoveRemaining > 0) { return FieldStep.None; }
                FromX = X;
                FromY = Y;
                TotalSteps++;
                var map = data.Map(MapId);
                var portal = map.PortalAt(X, Y);
                if (portal != null)
                {
                    WarpTo(portal.TargetMapId, portal.TargetX, portal.TargetY, portal.Facing);
                    return FieldStep.Portal;
                }

                var grass = Tiles.IsGrass(map.TileAt(X, Y)) ? map.GrassFor(map.TileAt(X, Y)) : null;
                if (grass != null)
                {
                    StepsSinceEncounter++;
                    if (StepsSinceEncounter > data.Rules.GraceSteps && EncounterRng.Next(100) < grass.RatePercent)
                    {
                        var wild = grass.Wild[EncounterRng.Weighted(grass.Weights())];
                        EncounteredSpecies = wild.Species;
                        EncounteredLevel = EncounterRng.Range(wild.MinLevel, wild.MaxLevel);
                        StepsSinceEncounter = 0;
                        BufferedDirection = -1;
                        return FieldStep.Encounter;
                    }
                }

                Direction? next = BufferedDirection >= 0 ? (Direction)BufferedDirection : held;
                BufferedDirection = -1;
                if (next.HasValue) { TryBeginStep(data, trainersDefeated, next.Value); }
                return FieldStep.Moved;
            }

            return held.HasValue ? TryBeginStep(data, trainersDefeated, held.Value) : FieldStep.None;
        }

        /// <summary>歩いている間に押した方向は 1 つだけ先行入力として持つ。</summary>
        public void Buffer(Direction dir)
        {
            if (IsMoving) { BufferedDirection = (int)dir; }
        }

        public void MixHash(MonsterData data, ref StateHash h)
        {
            h.Mix(data.Map(MapId).Index);
            h.Mix(X);
            h.Mix(Y);
            h.Mix(FromX);
            h.Mix(FromY);
            h.Mix((int)Facing);
            h.Mix(MoveRemaining);
            h.Mix(BufferedDirection);
            h.Mix(StepsSinceEncounter);
            h.Mix(TotalSteps);
            h.Mix(EncounterRng.State);
        }

        private FieldStep TryBeginStep(MonsterData data, int trainersDefeated, Direction dir)
        {
            Facing = dir;
            DirectionMath.Delta(dir, out var dx, out var dy);
            var nx = X + dx;
            var ny = Y + dy;
            if (!CanEnter(data, trainersDefeated, MapId, nx, ny)) { return FieldStep.Blocked; }
            FromX = X;
            FromY = Y;
            X = nx;
            Y = ny;
            MoveRemaining = data.Rules.StepTicks;
            BufferedDirection = -1;
            return FieldStep.Moved;
        }
    }
}
