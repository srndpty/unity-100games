namespace Game.Core
{
    /// <summary>目標マスの選択に必要な盤面の状況。</summary>
    public readonly struct TargetContext
    {
        public TargetContext(GridPoint player, Direction playerHeading, GridPoint self, GridPoint leader, GridPoint corner)
        {
            Player = player;
            PlayerHeading = playerHeading;
            Self = self;
            Leader = leader;
            Corner = corner;
        }

        public GridPoint Player { get; }
        public Direction PlayerHeading { get; }
        public GridPoint Self { get; }

        /// <summary>挟み撃ちの基準になる猫（トラ）の位置。</summary>
        public GridPoint Leader { get; }

        /// <summary>その猫の巡回先の隅。</summary>
        public GridPoint Corner { get; }
    }

    /// <summary>追跡時の目標マスの選び方。猫ごとにこれを差し替える。</summary>
    public interface ITargetSelector
    {
        GridPoint ChooseTarget(in TargetContext context);
    }

    /// <summary>トラ：自機のマスを直接狙う。</summary>
    public sealed class DirectSelector : ITargetSelector
    {
        public GridPoint ChooseTarget(in TargetContext context) => context.Player;
    }

    /// <summary>ミケ：自機の進行方向の先に回り込む。</summary>
    public sealed class AmbushSelector : ITargetSelector
    {
        private readonly int _lookahead;

        public AmbushSelector(int lookahead)
        {
            _lookahead = lookahead;
        }

        public GridPoint ChooseTarget(in TargetContext context) => context.Player.Offset(context.PlayerHeading, _lookahead);
    }

    /// <summary>クロ：自機の少し先を中心に、トラの位置を点対称に移した点を狙う。</summary>
    public sealed class PincerSelector : ITargetSelector
    {
        private readonly int _lookahead;

        public PincerSelector(int lookahead)
        {
            _lookahead = lookahead;
        }

        public GridPoint ChooseTarget(in TargetContext context)
        {
            var pivot = context.Player.Offset(context.PlayerHeading, _lookahead);
            return new GridPoint((2 * pivot.X) - context.Leader.X, (2 * pivot.Y) - context.Leader.Y);
        }
    }

    /// <summary>シロ：離れていれば自機を追い、近づくと自分の隅へ引き返す。</summary>
    public sealed class ShySelector : ITargetSelector
    {
        private readonly int _radius;

        public ShySelector(int radius)
        {
            _radius = radius;
        }

        public GridPoint ChooseTarget(in TargetContext context)
        {
            long radius = _radius;
            return context.Self.DistanceSquared(context.Player) >= radius * radius ? context.Player : context.Corner;
        }
    }
}
