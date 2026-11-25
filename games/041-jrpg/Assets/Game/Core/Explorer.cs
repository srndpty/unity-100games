using System;
using TileMotion;

namespace Game.Core
{
    public enum FieldStepResult
    {
        None = 0,
        Moved = 1,
        Blocked = 2,
        PortalMoved = 3,
        EncounterTriggered = 4,
        BossTriggered = 5,
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

    /// <summary>フィールド上のパーティ位置・向き・1 マス歩行補間・先行入力・遭遇歩数を管理する。</summary>
    public sealed class Explorer
    {
        public Explorer(GameData data, int matchSeed)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            MapId = data.Rules.StartMapId;
            X = data.Rules.StartX;
            Y = data.Rules.StartY;
            FromX = X;
            FromY = Y;
            Facing = Direction.Down;
            MoveRemaining = 0;
            BufferedDirection = -1;
            StepsSinceEncounter = 0;
            TotalSteps = 0;
            EncounterRng = GameRng.ForEncounter(matchSeed);
        }

        private Explorer(
            string mapId,
            int x,
            int y,
            int fromX,
            int fromY,
            Direction facing,
            int moveRemaining,
            int bufferedDirection,
            int stepsSinceEncounter,
            int totalSteps,
            GameRng encounterRng)
        {
            MapId = mapId;
            X = x;
            Y = y;
            FromX = fromX;
            FromY = fromY;
            Facing = facing;
            MoveRemaining = moveRemaining;
            BufferedDirection = bufferedDirection;
            StepsSinceEncounter = stepsSinceEncounter;
            TotalSteps = totalSteps;
            EncounterRng = encounterRng;
        }

        public string MapId { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public int FromX { get; private set; }
        public int FromY { get; private set; }
        public Direction Facing { get; private set; }
        public int MoveRemaining { get; private set; }
        public int BufferedDirection { get; private set; }
        public int StepsSinceEncounter { get; private set; }
        public int TotalSteps { get; private set; }
        public GameRng EncounterRng { get; }
        public bool IsMoving => MoveRemaining > 0;

        public void FrontTile(out int fx, out int fy)
        {
            DirectionMath.Delta(Facing, out var dx, out var dy);
            fx = X + dx;
            fy = Y + dy;
        }

        public void SetFacing(Direction dir)
        {
            if (!IsMoving)
            {
                Facing = dir;
            }
        }

        public void ClearBufferedInput() => BufferedDirection = -1;

        public void ResetEncounterSteps() => StepsSinceEncounter = 0;

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

        public FieldStepResult RequestMove(GameData data, FlagSet flags, Direction dir)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (flags == null)
            {
                throw new ArgumentNullException(nameof(flags));
            }

            if (IsMoving)
            {
                BufferedDirection = (int)dir;
                return FieldStepResult.None;
            }

            return TryBeginStep(data, flags, dir);
        }

        public FieldStepResult Tick(
            GameData data,
            FlagSet flags,
            Direction? heldDirection,
            out string triggeredGroupId,
            out string triggerMessage)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (flags == null)
            {
                throw new ArgumentNullException(nameof(flags));
            }

            triggeredGroupId = string.Empty;
            triggerMessage = string.Empty;

            if (MoveRemaining > 0)
            {
                MoveRemaining--;
                if (MoveRemaining > 0)
                {
                    return FieldStepResult.None;
                }

                // 1 マス移動完了
                FromX = X;
                FromY = Y;
                TotalSteps++;

                var map = data.GetMap(MapId);
                var portal = map.PortalAt(X, Y);
                if (portal != null)
                {
                    WarpTo(portal.TargetMapId, portal.TargetX, portal.TargetY, Facing);
                    return FieldStepResult.PortalMoved;
                }

                if (map.Boss.Enabled &&
                    !flags.Get(map.Boss.DefeatedFlag) &&
                    X == map.Boss.TriggerX &&
                    Y == map.Boss.TriggerY)
                {
                    StepsSinceEncounter = 0;
                    BufferedDirection = -1;
                    triggeredGroupId = map.Boss.BossGroupId;
                    triggerMessage = map.Boss.IntroMessage;
                    return FieldStepResult.BossTriggered;
                }

                if (!map.IsSafeZone && map.EncounterTableId.Length > 0)
                {
                    var table = data.GetEncounterTable(map.EncounterTableId);
                    StepsSinceEncounter++;
                    if (StepsSinceEncounter > table.GraceSteps && table.EffectiveRatePercent > 0)
                    {
                        if (EncounterRng.Next(100) < table.EffectiveRatePercent)
                        {
                            StepsSinceEncounter = 0;
                            BufferedDirection = -1;
                            triggeredGroupId = table.RollGroupId(EncounterRng);
                            triggerMessage = "魔物の群れが現れた！";
                            return FieldStepResult.EncounterTriggered;
                        }
                    }
                }

                Direction? nextDir = BufferedDirection >= 0 ? (Direction)BufferedDirection : heldDirection;
                BufferedDirection = -1;
                if (nextDir.HasValue)
                {
                    TryBeginStep(data, flags, nextDir.Value);
                }

                return FieldStepResult.Moved;
            }

            if (heldDirection.HasValue)
            {
                return TryBeginStep(data, flags, heldDirection.Value);
            }

            return FieldStepResult.None;
        }

        internal static Explorer Restore(
            string mapId,
            int x,
            int y,
            Direction facing,
            int stepsSinceEncounter,
            int totalSteps,
            uint encounterRngState)
        {
            return new Explorer(
                mapId,
                x,
                y,
                x,
                y,
                facing,
                moveRemaining: 0,
                bufferedDirection: -1,
                stepsSinceEncounter,
                totalSteps,
                GameRng.FromState(encounterRngState));
        }

        public Explorer Clone() => new(
            MapId,
            X,
            Y,
            FromX,
            FromY,
            Facing,
            MoveRemaining,
            BufferedDirection,
            StepsSinceEncounter,
            TotalSteps,
            EncounterRng.Clone());

        public void MixHash(GameData data, ref StateHash hash)
        {
            hash.Mix(data.GetMap(MapId).Index);
            hash.Mix(X);
            hash.Mix(Y);
            hash.Mix(FromX);
            hash.Mix(FromY);
            hash.Mix((int)Facing);
            hash.Mix(MoveRemaining);
            hash.Mix(BufferedDirection);
            hash.Mix(StepsSinceEncounter);
            hash.Mix(TotalSteps);
            hash.Mix(EncounterRng.State);
        }

        private FieldStepResult TryBeginStep(GameData data, FlagSet flags, Direction dir)
        {
            Facing = dir;
            DirectionMath.Delta(dir, out var dx, out var dy);
            var nx = X + dx;
            var ny = Y + dy;
            var map = data.GetMap(MapId);
            if (!map.IsWalkable(nx, ny, flags))
            {
                return FieldStepResult.Blocked;
            }

            FromX = X;
            FromY = Y;
            X = nx;
            Y = ny;
            MoveRemaining = data.Rules.StepTicks;
            BufferedDirection = -1;
            return FieldStepResult.Moved;
        }
    }
}
