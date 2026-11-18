using System;

namespace Game.Core
{
    [Serializable]
    public sealed class LaneConfig
    {
        public float Speed;
        public float Width;
        public float Phase;
        public LaneConfig(float speed, float width, float phase) { Speed = speed; Width = width; Phase = phase; }
        public LaneConfig Copy() => new(Speed, Width, Phase);
    }

    [Serializable]
    public sealed class FroggerConfig
    {
        public const int TickRate = 60;
        public const int Columns = 11;
        public const int GoalRow = 8;
        public const int ObjectsPerLane = 4;
        public const double Period = 16;
        public float FrogRadius = .25f;
        public LaneConfig[] Lanes =
        {
            new(1.6f, 1.5f, 2), new(-2f, 1.8f, 2), new(2.4f, 1.4f, 2),
            new(.7f, 3.4f, 0), new(-.6f, 3.4f, 0), new(.8f, 3.4f, 0),
        };
        public void Validate()
        {
            if (!float.IsFinite(FrogRadius) || FrogRadius < .1f || FrogRadius > .4f)
            { throw new ArgumentOutOfRangeException(nameof(FrogRadius)); }
            if (Lanes == null || Lanes.Length != 6) { throw new ArgumentException("レーンは6列必要です。", nameof(Lanes)); }
            foreach (var lane in Lanes)
            {
                if (lane == null || !float.IsFinite(lane.Speed) || Math.Abs(lane.Speed) > 12 ||
                    !float.IsFinite(lane.Width) || lane.Width < 1 || lane.Width > 3.5f ||
                    !float.IsFinite(lane.Phase) || lane.Phase < 0 || lane.Phase >= Period)
                { throw new ArgumentException("レーン設定が許容範囲外です。", nameof(Lanes)); }
            }
        }
        public FroggerConfig Copy()
        {
            var copy = new FroggerConfig { FrogRadius = FrogRadius, Lanes = new LaneConfig[Lanes.Length] };
            for (var i = 0; i < Lanes.Length; i++) { copy.Lanes[i] = Lanes[i].Copy(); }
            return copy;
        }
        public static int LaneRow(int lane) => lane < 3 ? lane + 1 : lane + 2;
        public static int RowLane(int row) => row >= 1 && row <= 3 ? row - 1 : row >= 5 && row <= 7 ? row - 2 : -1;
    }
}
