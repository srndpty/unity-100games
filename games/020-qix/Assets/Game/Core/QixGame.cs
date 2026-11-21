using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum Direction
    {
        None,
        Up,
        Down,
        Left,
        Right,
    }

    public enum GameStatus
    {
        Playing,
        Won,
        Lost,
    }

    [Flags]
    public enum GameEventKind
    {
        None = 0,
        DrawStarted = 1 << 0,
        Closed = 1 << 1,
        Miss = 1 << 2,
        Won = 1 << 3,
        Lost = 1 << 4,
    }

    /// <summary>前回取り出してから起きた出来事。</summary>
    public readonly struct GameEvents
    {
        public GameEvents(GameEventKind kinds, int claimedCells)
        {
            Kinds = kinds;
            ClaimedCells = claimedCells;
        }

        public GameEventKind Kinds { get; }

        /// <summary>線を閉じて陸になったセルの数の合計。</summary>
        public int ClaimedCells { get; }

        public bool Has(GameEventKind kind) => (Kinds & kind) == kind && kind != GameEventKind.None;
    }

    /// <summary>
    /// 1 試合。1 試合 = 1 シードで、シードから敵の初期位置と動きが決まる。
    /// 入力は「押している向き」と「描画キーを押しているか」の状態で、<see cref="Tick"/> のたびに読む。
    /// 1 Tick の順序は「自機の移動（閉じたら確定）→ 敵の移動（線に触れたらミス）」。
    /// </summary>
    public sealed class QixGame
    {
        private const int EnemyStream = 1;
        private static readonly Direction[] _neighbors = { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

        private readonly QixConfig _config;
        private readonly CellGrid _grid;
        private readonly List<int> _trail = new();
        private readonly List<int> _scratch = new();
        private Direction _direction;
        private bool _drawHeld;
        private bool _awaitingDrawRelease;
        private int _stepCooldown;
        private int _trailStartX;
        private int _trailStartY;
        private GameEventKind _events;
        private int _eventClaimed;

        public QixGame(QixConfig config, int seed)
            : this(config, seed, null)
        {
        }

        private QixGame(QixConfig config, int seed, Func<QixConfig, SeededRandom, Enemy>? placeEnemy)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            Seed = seed;
            _grid = new CellGrid(_config.Width, _config.Height);
            InitialOpen = _grid.OpenCount;
            Lives = _config.Lives;
            PlayerX = _config.Width / 2;
            PlayerY = 0;
            var random = new SeededRandom(unchecked(seed + EnemyStream * (int)0x9E3779B9u));
            Enemy = placeEnemy == null ? Enemy.Spawn(_config, random) : placeEnemy(_config, random);
        }

        public int Seed { get; }

        public int Width => _grid.Width;

        public int Height => _grid.Height;

        public int PlayerX { get; private set; }

        public int PlayerY { get; private set; }

        /// <summary>線を描いている途中か。</summary>
        public bool IsDrawing => _trail.Count > 0;

        /// <summary>
        /// ミスの後、描画キーを一度離すまで新しい線を引けない状態か。
        /// キーを押したままだと同じ場所から同じ敵へ踏み出し続け、数 Tick で残機をすべて失うのを防ぐ。縁を歩くことはできる。
        /// </summary>
        public bool AwaitingDrawRelease => _awaitingDrawRelease;

        public Enemy Enemy { get; }

        public int Lives { get; private set; }

        public int Score { get; private set; }

        public long Ticks { get; private set; }

        public GameStatus Status { get; private set; }

        public bool IsOver => Status != GameStatus.Playing;

        /// <summary>最初の海のセル数（外周の陸を含まない）。占有率の分母。</summary>
        public int InitialOpen { get; }

        /// <summary>最初の海のうち陸になったセルの数。描いている途中の線は数えない。</summary>
        public int ClaimedCells => InitialOpen - _grid.OpenCount - _grid.TrailCount;

        public int TargetPercent => _config.TargetPercent;

        /// <summary>占有率（0〜1）。表示用。勝敗は整数の <see cref="HasReachedTarget"/> で決める。</summary>
        public double ClaimedRatio => (double)ClaimedCells / InitialOpen;

        public bool HasReachedTarget => ClaimedCells * 100L >= (long)_config.TargetPercent * InitialOpen;

        /// <summary>今の線のセル番号（描いた順）。</summary>
        public IReadOnlyList<int> Trail => _trail.AsReadOnly();

        /// <summary>敵の位置と向きを指定して試合を作る（テスト・撮影用）。</summary>
        public static QixGame WithEnemy(QixConfig config, int seed, int x, int y, int velocityX, int velocityY)
        {
            return new QixGame(config, seed, (c, random) => new Enemy(c, random, x, y, velocityX, velocityY));
        }

        /// <summary>盤が変わるたびに増える番号。</summary>
        public int GridVersion => _grid.Version;

        public Cell CellAt(int x, int y) => _grid[x, y];

        public Cell CellAt(int index) => _grid[index];

        public bool IsBoundary(int x, int y) => _grid.IsBoundary(x, y);

        /// <summary>押している向きと描画キーの状態。試合が終わったら受け付けない。</summary>
        public bool SetInput(Direction direction, bool draw)
        {
            if (!Enum.IsDefined(typeof(Direction), direction))
            {
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "未定義の向きです。");
            }

            if (IsOver)
            {
                return false;
            }

            if (direction != _direction)
            {
                // 向きを変えた・押し始めたときはすぐ 1 歩目を出す。
                _stepCooldown = 0;
            }

            _direction = direction;
            _drawHeld = draw;
            if (!draw) { _awaitingDrawRelease = false; }
            return true;
        }

        /// <summary>1 Tick 進める。試合が終わっていれば何もしない。</summary>
        public void Tick()
        {
            if (IsOver)
            {
                return;
            }

            Ticks++;
            StepPlayer();
            if (IsOver)
            {
                return;
            }

            if (Enemy.Step(_grid))
            {
                Miss();
            }
        }

        public GameEvents TakeEvents()
        {
            var events = new GameEvents(_events, _eventClaimed);
            _events = GameEventKind.None;
            _eventClaimed = 0;
            return events;
        }

        /// <summary>試合の状態のハッシュ（FNV-1a）。</summary>
        public uint Hash()
        {
            var hash = _grid.Hash();
            unchecked
            {
                foreach (var value in new[] { PlayerX, PlayerY, Lives, Score, (int)Status, _trail.Count })
                {
                    hash = (hash ^ (uint)value) * 16777619u;
                }
            }

            return Enemy.Hash(hash);
        }

        private void StepPlayer()
        {
            if (_direction == Direction.None)
            {
                return;
            }

            if (_stepCooldown > 0)
            {
                _stepCooldown--;
                return;
            }

            if (TryMove())
            {
                _stepCooldown = _config.PlayerStepTicks - 1;
            }
        }

        private bool TryMove()
        {
            Offset(_direction, out var dx, out var dy);
            var x = PlayerX + dx;
            var y = PlayerY + dy;
            if (!_grid.Contains(x, y))
            {
                return false;
            }

            var target = _grid[x, y];
            if (!IsDrawing)
            {
                if (target == Cell.Claimed)
                {
                    if (!_grid.IsBoundary(x, y)) { return false; }
                    MoveTo(x, y);
                    return true;
                }

                if (target != Cell.Open || !_drawHeld || _awaitingDrawRelease)
                {
                    return false;
                }

                if (Enemy.Overlaps(x, y))
                {
                    Miss();
                    return false;
                }

                _trailStartX = PlayerX;
                _trailStartY = PlayerY;
                Extend(x, y);
                _events |= GameEventKind.DrawStarted;
                return true;
            }

            // 描いている途中は、描画キーを離すとその場で止まる。自分の線（逆走を含む）には進めない。
            if (!_drawHeld || target == Cell.Trail)
            {
                return false;
            }

            if (target == Cell.Open)
            {
                if (Enemy.Overlaps(x, y))
                {
                    Miss();
                    return false;
                }

                Extend(x, y);
                return true;
            }

            MoveTo(x, y);
            Close();
            return true;
        }

        private void Extend(int x, int y)
        {
            var index = _grid.Index(x, y);
            _grid.Set(index, Cell.Trail);
            _trail.Add(index);
            MoveTo(x, y);
        }

        private void MoveTo(int x, int y)
        {
            PlayerX = x;
            PlayerY = y;
        }

        private void Close()
        {
            Enemy.OverlappedCells(_grid, _scratch);
            var claimed = Territory.Close(_grid, _scratch);
            _trail.Clear();
            Score = (int)Math.Min(int.MaxValue, (long)Score + claimed);
            _events |= GameEventKind.Closed;
            _eventClaimed += claimed;
            if (!_grid.IsBoundary(PlayerX, PlayerY))
            {
                MoveToNearestBoundary();
            }

            if (HasReachedTarget)
            {
                Status = GameStatus.Won;
                _events |= GameEventKind.Won;
            }
        }

        private void Miss()
        {
            foreach (var index in _trail)
            {
                _grid.Set(index, Cell.Open);
            }

            if (_trail.Count > 0)
            {
                MoveTo(_trailStartX, _trailStartY);
                _trail.Clear();
            }

            Lives--;
            _awaitingDrawRelease = true;
            _events |= GameEventKind.Miss;
            if (Lives <= 0)
            {
                Status = GameStatus.Lost;
                _events |= GameEventKind.Lost;
            }
        }

        /// <summary>
        /// 閉じた先のセルが縁でなくなった（周りの海がすべて陸になった）とき、陸を通って最も近い縁へ移す。
        /// 探索は上・下・左・右の順に広げるので、移る先は盤の状態だけで決まる。
        /// </summary>
        private void MoveToNearestBoundary()
        {
            var visited = new bool[_grid.CellCount];
            var queue = new Queue<int>();
            var start = _grid.Index(PlayerX, PlayerY);
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % _grid.Width;
                var y = index / _grid.Width;
                if (_grid.IsBoundary(x, y))
                {
                    MoveTo(x, y);
                    return;
                }

                foreach (var direction in _neighbors)
                {
                    Offset(direction, out var dx, out var dy);
                    var nx = x + dx;
                    var ny = y + dy;
                    if (!_grid.Contains(nx, ny)) { continue; }
                    var next = _grid.Index(nx, ny);
                    if (visited[next] || _grid[next] != Cell.Claimed) { continue; }
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }

            throw new InvalidOperationException("縁が見つかりません。敵の領域は必ず残るので、ここには来ないはずです。");
        }

        public static void Offset(Direction direction, out int dx, out int dy)
        {
            dx = direction == Direction.Left ? -1 : direction == Direction.Right ? 1 : 0;
            dy = direction == Direction.Down ? -1 : direction == Direction.Up ? 1 : 0;
        }
    }
}
