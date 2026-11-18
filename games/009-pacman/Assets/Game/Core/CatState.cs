using System;

namespace Game.Core
{
    public enum CatMode
    {
        /// <summary>寝床の中で待っている。</summary>
        InHouse,

        /// <summary>扉を通って外へ向かっている。</summary>
        Leaving,

        /// <summary>全体の予定に従って巡回または追跡している。</summary>
        Roaming,

        /// <summary>マタタビで逃げている。</summary>
        Frightened,

        /// <summary>捕まって寝床へ帰っている。</summary>
        Returning,
    }

    /// <summary>猫が分岐を決めるときに参照する盤面の状況。</summary>
    internal readonly struct CatWorld
    {
        public CatWorld(MazeGraph graph, SeededRandom random, bool isChase, GridPoint player, Direction playerHeading, GridPoint leader)
        {
            Graph = graph;
            Random = random;
            IsChase = isChase;
            Player = player;
            PlayerHeading = playerHeading;
            Leader = leader;
        }

        public MazeGraph Graph { get; }
        public SeededRandom Random { get; }
        public bool IsChase { get; }
        public GridPoint Player { get; }
        public Direction PlayerHeading { get; }
        public GridPoint Leader { get; }
    }

    /// <summary>猫1匹の状態遷移と分岐の選択。</summary>
    public sealed class CatState
    {
        private readonly ITargetSelector _selector;
        private readonly int _spawnCell;
        private readonly Direction _spawnHeading;
        private readonly CatMode _spawnMode;
        private readonly int _releaseTick;

        internal CatState(ITargetSelector selector, GridPoint corner, int spawnCell, Direction spawnHeading, CatMode spawnMode, int releaseTick)
        {
            _selector = selector;
            Corner = corner;
            _spawnCell = spawnCell;
            _spawnHeading = spawnHeading;
            _spawnMode = spawnMode;
            _releaseTick = releaseTick;
            Actor = new MazeActor(spawnCell, spawnHeading);
            Mode = spawnMode;
        }

        public MazeActor Actor { get; }
        public CatMode Mode { get; private set; }

        /// <summary>巡回先の隅。</summary>
        public GridPoint Corner { get; }

        /// <summary>接触するとミスになる状態か。</summary>
        public bool IsDangerous => Mode == CatMode.Roaming || Mode == CatMode.Leaving;

        /// <summary>今の状況で目指しているマス。巡回・追跡中以外は null。</summary>
        internal GridPoint? TargetIn(in CatWorld world) => Mode == CatMode.Roaming ? Target(world) : null;

        internal void ResetRound()
        {
            Actor.Reset(_spawnCell, _spawnHeading);
            Mode = _spawnMode;
        }

        internal void ReleaseIfDue(int roundTick, MazeGraph graph)
        {
            if (Mode != CatMode.InHouse || roundTick < _releaseTick) { return; }
            Mode = CatMode.Leaving;
            Actor.Turn(Descend(graph, Actor.Cell, graph.DistanceToExit));
        }

        /// <summary>マタタビの開始。巡回・追跡中の猫だけが逃走し、その場で反転する。</summary>
        internal void Frighten(MazeGraph graph)
        {
            if (Mode != CatMode.Roaming) { return; }
            Mode = CatMode.Frightened;
            Actor.Reverse(graph, Traversal.Corridor);
        }

        /// <summary>マタタビの終了。逃走中の猫だけが巡回・追跡へ戻る。</summary>
        internal void Calm()
        {
            if (Mode == CatMode.Frightened) { Mode = CatMode.Roaming; }
        }

        internal void Capture()
        {
            if (Mode != CatMode.Frightened) { throw new InvalidOperationException("逃走中の猫だけを捕まえられます。"); }
            Mode = CatMode.Returning;
        }

        /// <summary>巡回と追跡の切り替えで反転する。</summary>
        internal void ReverseForModeChange(MazeGraph graph)
        {
            if (Mode == CatMode.Roaming) { Actor.Reverse(graph, Traversal.Corridor); }
        }

        internal void Move(in CatWorld world, int units)
        {
            while (units > 0 && Mode != CatMode.InHouse)
            {
                units = Actor.Advance(world.Graph, units, out var arrived);
                if (arrived) { OnArrive(world); }
            }
        }

        private void OnArrive(in CatWorld world)
        {
            var graph = world.Graph;
            switch (Mode)
            {
                case CatMode.Leaving:
                    if (Actor.Cell == graph.ExitCell)
                    {
                        Mode = CatMode.Roaming;
                        Actor.Turn(ChooseTowards(world, Target(world)));
                    }
                    else
                    {
                        Actor.Turn(Descend(graph, Actor.Cell, graph.DistanceToExit));
                    }

                    break;
                case CatMode.Returning:
                    if (Actor.Cell == graph.HouseCell)
                    {
                        Mode = CatMode.Leaving;
                        Actor.Turn(Descend(graph, Actor.Cell, graph.DistanceToExit));
                    }
                    else
                    {
                        Actor.Turn(Descend(graph, Actor.Cell, graph.DistanceToHouse));
                    }

                    break;
                case CatMode.Frightened:
                    Actor.Turn(ChooseRandom(world));
                    break;
                case CatMode.Roaming:
                    Actor.Turn(ChooseTowards(world, Target(world)));
                    break;
                default:
                    throw new InvalidOperationException("寝床の中の猫は移動しません。");
            }
        }

        private GridPoint Target(in CatWorld world)
        {
            if (!world.IsChase) { return Corner; }
            var context = new TargetContext(world.Player, world.PlayerHeading, world.Graph.Point(Actor.Cell), world.Leader, Corner);
            return _selector.ChooseTarget(context);
        }

        /// <summary>逆走を除く通行可能な方向から、目標に最も近いものを選ぶ。同点は上→左→下→右。</summary>
        private Direction ChooseTowards(in CatWorld world, GridPoint target)
        {
            var graph = world.Graph;
            var back = Actor.Heading.Opposite();
            var best = Direction.None;
            var bestDistance = long.MaxValue;
            for (var i = 0; i < DirectionExtensions.Count; i++)
            {
                var direction = (Direction)i;
                if (direction == back || !graph.CanEnter(Actor.Cell, direction, Traversal.Corridor)) { continue; }
                var distance = graph.Point(graph.Neighbor(Actor.Cell, direction)).DistanceSquared(target);
                if (distance < bestDistance)
                {
                    best = direction;
                    bestDistance = distance;
                }
            }

            return best != Direction.None ? best : throw new InvalidOperationException("進める方向がありません: " + graph.Point(Actor.Cell));
        }

        private Direction ChooseRandom(in CatWorld world)
        {
            var graph = world.Graph;
            var back = Actor.Heading.Opposite();
            Span<Direction> options = stackalloc Direction[DirectionExtensions.Count];
            var count = 0;
            for (var i = 0; i < DirectionExtensions.Count; i++)
            {
                var direction = (Direction)i;
                if (direction != back && graph.CanEnter(Actor.Cell, direction, Traversal.Corridor)) { options[count++] = direction; }
            }

            if (count == 0) { throw new InvalidOperationException("進める方向がありません: " + graph.Point(Actor.Cell)); }
            return options[world.Random.Next(count)];
        }

        /// <summary>距離場を1つ下る方向を選ぶ。同点は上→左→下→右。</summary>
        private static Direction Descend(MazeGraph graph, int cell, Func<int, int> distance)
        {
            var current = distance(cell);
            for (var i = 0; i < DirectionExtensions.Count; i++)
            {
                var direction = (Direction)i;
                if (graph.CanEnter(cell, direction, Traversal.Gate) && distance(graph.Neighbor(cell, direction)) == current - 1) { return direction; }
            }

            throw new InvalidOperationException("距離場を下れません: " + graph.Point(cell));
        }
    }
}
