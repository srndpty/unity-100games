using System;

namespace Game.Core
{
    public enum GamePhase
    {
        Ready,
        Active,
        Dying,
        Won,
        GameOver,
    }

    /// <summary>1Tick の間に起きた出来事。音と演出、テストの観測に使う。</summary>
    [Flags]
    public enum GameEvents
    {
        None = 0,
        DotEaten = 1 << 0,
        PowerEaten = 1 << 1,
        CatCaptured = 1 << 2,
        PlayerCaught = 1 << 3,
        PowerEnded = 1 << 4,
        ModeChanged = 1 << 5,
        RoundStarted = 1 << 6,
        Won = 1 << 7,
        GameOver = 1 << 8,
    }

    /// <summary>1試合の進行。入力と Tick だけで進み、同じシードと入力列なら同じ結果になる。</summary>
    public sealed class MazeGameState
    {
        private readonly MazeGameConfig _config;
        private readonly SeededRandom _random;
        private readonly CatState[] _cats;
        private readonly Pellet[] _pellets;
        private Direction _desired = Direction.None;
        private bool _moving;
        private int _phaseTicks;
        private int _roundTick;
        private int _modeIndex;
        private int _modeTicksLeft;
        private int _captureChain;

        public MazeGameState(MazeGameConfig config, int seed, MazeLayout? layout = null)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _random = new SeededRandom(seed);
            Seed = seed;
            Graph = new MazeGraph(layout ?? MazeLayout.CreateDefault());
            Player = new MazeActor(Graph.PlayerCell, Direction.Left);
            Lives = _config.Lives;

            _pellets = new Pellet[Graph.CellCount];
            for (var cell = 0; cell < Graph.CellCount; cell++)
            {
                var point = Graph.Point(cell);
                _pellets[cell] = Graph.Layout.PelletAt(point.X, point.Y);
                if (_pellets[cell] != Pellet.None) { PelletsRemaining++; }
            }

            PelletsTotal = PelletsRemaining;
            _cats = CreateCats();
            ResetRound();
        }

        public MazeGraph Graph { get; }
        public int Seed { get; }
        public MazeActor Player { get; }
        public GamePhase Phase { get; private set; }
        public GameEvents Events { get; private set; }
        public long TickCount { get; private set; }
        public int Score { get; private set; }
        public int Lives { get; private set; }
        public int PelletsTotal { get; }
        public int PelletsRemaining { get; private set; }
        public int PowerTicksLeft { get; private set; }

        /// <summary>準備またはミス演出の残り Tick。</summary>
        public int PhaseTicksLeft => _phaseTicks;

        public bool IsPlayerMoving => _moving;
        public Direction DesiredDirection => _desired;
        public bool IsOver => Phase == GamePhase.Won || Phase == GamePhase.GameOver;

        /// <summary>巡回・追跡の予定が追跡の区間にあるか。</summary>
        public bool IsChase => _modeIndex >= _config.ModeTicks.Length || _modeIndex % 2 == 1;

        public CatState Cat(int index) => _cats[index];

        /// <summary>猫の開始マス。トラは扉の外、残りは寝床の中に並ぶ。</summary>
        public static int CatSpawnCell(MazeGraph graph, int index)
        {
            if (graph == null) { throw new ArgumentNullException(nameof(graph)); }
            var door = graph.Layout.Door;
            switch (index)
            {
                case 0: return graph.ExitCell;
                case 1: return graph.CellId(door.X - 2, door.Y + 2);
                case 2: return graph.HouseCell;
                case 3: return graph.CellId(door.X + 2, door.Y + 2);
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public Pellet PelletAt(int cell) => _pellets[cell];

        /// <summary>猫が今目指しているマス。巡回・追跡中でなければ null。</summary>
        public GridPoint? TargetOf(int index) => _cats[index].TargetIn(World());

        public void Step(Direction desired = Direction.None)
        {
            if (desired != Direction.None && !desired.IsMove()) { throw new ArgumentOutOfRangeException(nameof(desired)); }
            Events = GameEvents.None;
            if (IsOver) { return; }

            TickCount++;
            switch (Phase)
            {
                case GamePhase.Ready:
                    Buffer(desired);
                    if (--_phaseTicks <= 0) { Phase = GamePhase.Active; }
                    break;
                case GamePhase.Dying:
                    if (--_phaseTicks <= 0) { FinishDying(); }
                    break;
                default:
                    StepActive(desired);
                    break;
            }
        }

        private CatState[] CreateCats()
        {
            var graph = Graph;
            var right = graph.Width - 2;
            var bottom = graph.Height + 1;
            var release = _config.ReleaseTicks;
            return new[]
            {
                new CatState(new DirectSelector(), new GridPoint(right, -2), CatSpawnCell(graph, 0), Direction.Left, CatMode.Roaming, release[0]),
                new CatState(new AmbushSelector(_config.AmbushLookahead), new GridPoint(1, -2), CatSpawnCell(graph, 1), Direction.Up, CatMode.InHouse, release[1]),
                new CatState(new PincerSelector(_config.PincerLookahead), new GridPoint(right + 1, bottom), CatSpawnCell(graph, 2), Direction.Up, CatMode.InHouse, release[2]),
                new CatState(new ShySelector(_config.ShyRadius), new GridPoint(0, bottom), CatSpawnCell(graph, 3), Direction.Up, CatMode.InHouse, release[3]),
            };
        }

        private void ResetRound()
        {
            Player.Reset(Graph.PlayerCell, Direction.Left);
            foreach (var cat in _cats) { cat.ResetRound(); }
            _desired = Direction.None;
            _moving = false;
            _roundTick = 0;
            _modeIndex = 0;
            _modeTicksLeft = _config.ModeTicks.Length > 0 ? _config.ModeTicks[0] : 0;
            _captureChain = 0;
            PowerTicksLeft = 0;
            Phase = GamePhase.Ready;
            _phaseTicks = _config.ReadyTicks;
            Events |= GameEvents.RoundStarted;
        }

        private void FinishDying()
        {
            if (Lives > 0)
            {
                ResetRound();
            }
            else
            {
                Phase = GamePhase.GameOver;
                Events |= GameEvents.GameOver;
            }
        }

        private void Buffer(Direction desired)
        {
            if (desired != Direction.None) { _desired = desired; }
        }

        private void StepActive(Direction desired)
        {
            Buffer(desired);
            if (_moving && Player.Progress > 0 && _desired == Player.Heading.Opposite())
            {
                Player.Reverse(Graph, Traversal.Corridor);
            }

            AdvanceClocks();
            for (var substep = 0; substep < MazeGameConfig.SubstepsPerTick && Phase == GamePhase.Active; substep++)
            {
                MovePlayer(Share(_config.PlayerSpeed, substep));
                if (Phase != GamePhase.Active) { return; }
                MoveCats(substep);
                ResolveContacts();
            }
        }

        private void AdvanceClocks()
        {
            _roundTick++;
            foreach (var cat in _cats) { cat.ReleaseIfDue(_roundTick, Graph); }

            if (PowerTicksLeft > 0)
            {
                if (--PowerTicksLeft == 0) { EndPower(); }
                return;
            }

            if (_modeIndex >= _config.ModeTicks.Length || --_modeTicksLeft > 0) { return; }
            var wasChase = IsChase;
            _modeIndex++;
            _modeTicksLeft = _modeIndex < _config.ModeTicks.Length ? _config.ModeTicks[_modeIndex] : 0;
            if (wasChase == IsChase) { return; }
            foreach (var cat in _cats) { cat.ReverseForModeChange(Graph); }
            Events |= GameEvents.ModeChanged;
        }

        private void EndPower()
        {
            foreach (var cat in _cats) { cat.Calm(); }
            _captureChain = 0;
            Events |= GameEvents.PowerEnded;
        }

        // 1Tick の移動量を部分ステップへ端数なく配分する。
        private static int Share(int speed, int substep) =>
            (speed * (substep + 1) / MazeGameConfig.SubstepsPerTick) - (speed * substep / MazeGameConfig.SubstepsPerTick);

        private void MovePlayer(int units)
        {
            Player.BeginMotion(Graph, units);
            while (units > 0)
            {
                if (Player.Progress == 0 && !ChooseHeading()) { return; }
                units = Player.AdvanceInMotion(Graph, units, out var arrived);
                if (!arrived) { continue; }
                Eat(Player.Cell);
                if (Phase != GamePhase.Active) { return; }
            }
        }

        private bool ChooseHeading()
        {
            if (_desired != Direction.None && Graph.CanEnter(Player.Cell, _desired, Traversal.Corridor))
            {
                Player.Turn(_desired);
                _moving = true;
                return true;
            }

            _moving = _moving && Graph.CanEnter(Player.Cell, Player.Heading, Traversal.Corridor);
            return _moving;
        }

        private void Eat(int cell)
        {
            var pellet = _pellets[cell];
            if (pellet == Pellet.None) { return; }

            _pellets[cell] = Pellet.None;
            PelletsRemaining--;
            if (pellet == Pellet.Dot)
            {
                Score += _config.DotScore;
                Events |= GameEvents.DotEaten;
            }
            else
            {
                Score += _config.PowerScore;
                Events |= GameEvents.PowerEaten;
            }

            if (PelletsRemaining == 0)
            {
                Phase = GamePhase.Won;
                Events |= GameEvents.Won;
            }
            else if (pellet == Pellet.Power)
            {
                StartPower();
            }
        }

        private void StartPower()
        {
            PowerTicksLeft = _config.PowerTicks;
            _captureChain = 0;
            foreach (var cat in _cats) { cat.Frighten(Graph); }
        }

        private void MoveCats(int substep)
        {
            var world = World();
            foreach (var cat in _cats)
            {
                var units = Share(SpeedOf(cat), substep);
                cat.Actor.BeginMotion(Graph, units);
                cat.Move(world, units);
            }
        }

        private int SpeedOf(CatState cat)
        {
            return cat.Mode switch
            {
                CatMode.Frightened => _config.FrightenedSpeed,
                CatMode.Returning => _config.ReturningSpeed,
                CatMode.Leaving => _config.LeavingSpeed,
                CatMode.Roaming => _config.CatSpeed,
                _ => 0,
            };
        }

        private CatWorld World() =>
            new(Graph, _random, IsChase, Graph.Point(Player.Cell), Player.Heading, Graph.Point(_cats[0].Actor.Cell));

        // 部分ステップの途中のかすり接触も拾う。同じ部分ステップで複数の猫に触れたら、危険な猫を優先してミスにする。
        private void ResolveContacts()
        {
            foreach (var cat in _cats)
            {
                if (cat.IsDangerous && Player.TouchedDuringMotion(cat.Actor, Graph, _config.ContactRadius))
                {
                    Miss();
                    return;
                }
            }

            foreach (var cat in _cats)
            {
                if (cat.Mode != CatMode.Frightened || !Player.TouchedDuringMotion(cat.Actor, Graph, _config.ContactRadius)) { continue; }
                cat.Capture();
                Score += _config.CatScores[Math.Min(_captureChain, _config.CatScores.Length - 1)];
                _captureChain++;
                Events |= GameEvents.CatCaptured;
            }
        }

        private void Miss()
        {
            Lives--;
            PowerTicksLeft = 0;
            Phase = GamePhase.Dying;
            _phaseTicks = _config.DyingTicks;
            Events |= GameEvents.PlayerCaught;
        }
    }
}
