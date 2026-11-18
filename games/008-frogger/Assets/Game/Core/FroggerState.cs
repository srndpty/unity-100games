using System;

namespace Game.Core
{
    public enum MoveDirection { None, Up, Down, Left, Right }
    public enum RoundResult { Playing, Won, CarHit, Drowned, CarriedOut }

    public sealed class FroggerState
    {
        private readonly FroggerConfig _config;
        public FroggerState(FroggerConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(); _config = config.Copy();
        }
        private FroggerState(FroggerState source)
        {
            _config = source._config; X = source.X; Row = source.Row; TickCount = source.TickCount;
            Result = source.Result; SupportId = source.SupportId;
        }
        public double X { get; private set; } = 5.5;
        public int Row { get; private set; }
        public long TickCount { get; private set; }
        public RoundResult Result { get; private set; }
        public int SupportId { get; private set; } = -1;
        public FroggerState Copy() => new(this);

        public double ObjectX(int lane, int id)
        {
            if (lane < 0 || lane >= 6 || id < 0 || id >= FroggerConfig.ObjectsPerLane)
            { throw new ArgumentOutOfRangeException(nameof(lane)); }
            var value = _config.Lanes[lane].Phase + id * 4 + _config.Lanes[lane].Speed * (TickCount / (double)FroggerConfig.TickRate);
            return value - Math.Floor(value / FroggerConfig.Period) * FroggerConfig.Period - 2.5;
        }
        public void Step(MoveDirection direction = MoveDirection.None)
        {
            if (direction < MoveDirection.None || direction > MoveDirection.Right)
            { throw new ArgumentOutOfRangeException(nameof(direction)); }
            if (Result != RoundResult.Playing) { return; }
            var nextX = X + (direction == MoveDirection.Left ? -1 : direction == MoveDirection.Right ? 1 : 0);
            var nextRow = Row + (direction == MoveDirection.Up ? 1 : direction == MoveDirection.Down ? -1 : 0);
            if (nextX - _config.FrogRadius >= 0 && nextX + _config.FrogRadius <= FroggerConfig.Columns && nextRow >= 0 && nextRow <= FroggerConfig.GoalRow)
            { X = nextX; Row = nextRow; }
            if (Row == FroggerConfig.GoalRow) { Result = RoundResult.Won; SupportId = -1; return; }
            var lane = FroggerConfig.RowLane(Row);
            SupportId = -1;
            if (lane >= 3)
            {
                for (var id = 0; id < FroggerConfig.ObjectsPerLane; id++)
                {
                    if (Math.Abs(X - ObjectX(lane, id)) + _config.FrogRadius <= _config.Lanes[lane].Width / 2.0)
                    { SupportId = id; break; }
                }
                if (SupportId < 0) { Result = RoundResult.Drowned; return; }
                X += _config.Lanes[lane].Speed / (double)FroggerConfig.TickRate;
                if (X - _config.FrogRadius < 0 || X + _config.FrogRadius > FroggerConfig.Columns) { Result = RoundResult.CarriedOut; }
            }
            else if (lane >= 0)
            {
                var delta = _config.Lanes[lane].Speed / (double)FroggerConfig.TickRate;
                var radius = _config.Lanes[lane].Width / 2.0 + _config.FrogRadius;
                for (var id = 0; id < FroggerConfig.ObjectsPerLane; id++)
                {
                    // 速度上限12/60、半幅上限2.15。前後1周期のコピーで掃引区間を覆える。
                    for (var copy = -1; copy <= 1; copy++)
                    {
                        var start = ObjectX(lane, id) + copy * FroggerConfig.Period;
                        if (X >= Math.Min(start, start + delta) - radius && X <= Math.Max(start, start + delta) + radius)
                        { Result = RoundResult.CarHit; }
                    }
                }
            }
            TickCount++;
        }
    }
}
