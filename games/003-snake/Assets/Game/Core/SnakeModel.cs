using System;

namespace Game.Core
{
    public enum Direction { Right, Up, Left, Down }
    public enum GamePhase { Playing, Lost, Won }

    public readonly struct Cell : IEquatable<Cell>
    {
        public Cell(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
        public bool Equals(Cell other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
        public static bool operator ==(Cell left, Cell right) => left.Equals(right);
        public static bool operator !=(Cell left, Cell right) => !left.Equals(right);
        public Cell Move(Direction direction) => direction switch
        {
            Direction.Right => new Cell(X + 1, Y),
            Direction.Up => new Cell(X, Y + 1),
            Direction.Left => new Cell(X - 1, Y),
            Direction.Down => new Cell(X, Y - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
    }

    [Serializable]
    public sealed class SnakeConfig
    {
        // 体節ごとにSpriteRendererを使う描画方式で計測したサポート範囲。
        public const int MaxWidth = 32;
        public const int MaxHeight = 20;
        public int Width = 20;
        public int Height = 16;
        public int InitialLength = 3;
        public float StepsPerSecond = 8f;

        public void Validate()
        {
            // Coreの配列容量だけでなく、Unityの描画・オブジェクト数の上限も保証する。
            if (Width < 4 || Width > MaxWidth || Height < 2 || Height > MaxHeight ||
                InitialLength < 2 || InitialLength > Width / 2 + 1 ||
                !float.IsFinite(StepsPerSecond) || StepsPerSecond <= 0f || StepsPerSecond > 60f)
            {
                throw new ArgumentException("盤面、初期長、速度の設定が不正です。");
            }
        }

        public SnakeConfig Copy() => new()
        {
            Width = Width,
            Height = Height,
            InitialLength = InitialLength,
            StepsPerSecond = StepsPerSecond,
        };
    }

    /// <summary>胴体は固定容量のリングバッファ。添字0が頭、Length-1が末尾。</summary>
    public sealed class SnakeState
    {
        private readonly Cell[] _body;
        private readonly bool[] _occupied;
        private readonly int _initialLength;
        private int _head;
        private uint _random;

        public SnakeState(SnakeConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            Width = config.Width;
            Height = config.Height;
            _initialLength = config.InitialLength;
            _body = new Cell[Width * Height];
            _occupied = new bool[_body.Length];
            _random = unchecked((uint)seed);
            if (_random == 0) { _random = 0x9E3779B9u; }
            Length = _initialLength;
            for (var i = 0; i < Length; i++)
            {
                var cell = new Cell(Width / 2 - i, Height / 2);
                _body[i] = cell;
                _occupied[Index(cell)] = true;
            }
            PlaceFood();
        }

        public int Width { get; }
        public int Height { get; }
        public int Length { get; private set; }
        public int Score => Length - _initialLength;
        public int TickCount { get; private set; }
        public Direction Direction { get; private set; } = Direction.Right;
        public GamePhase Phase { get; private set; }
        public Cell? Food { get; private set; }
        public Cell Head => SegmentAt(0);
        public Cell SegmentAt(int index)
        {
            if (index < 0 || index >= Length) { throw new ArgumentOutOfRangeException(nameof(index)); }
            return _body[(_head + index) % _body.Length];
        }

        public bool Occupies(Cell cell) => Contains(cell) && _occupied[Index(cell)];
        public static bool IsDirection(Direction direction) => direction >= Direction.Right && direction <= Direction.Down;
        public static bool IsOpposite(Direction left, Direction right) => ((int)left + 2) % 4 == (int)right;

        public void Step(Direction requested)
        {
            if (Phase != GamePhase.Playing) { return; }
            if (!IsDirection(requested)) { throw new ArgumentOutOfRangeException(nameof(requested)); }
            if (!IsOpposite(Direction, requested)) { Direction = requested; }
            TickCount = unchecked(TickCount + 1);
            var next = Head.Move(Direction);
            var grows = Food.HasValue && next == Food.Value;
            var tail = SegmentAt(Length - 1);
            if (!Contains(next) || (Occupies(next) && (grows || next != tail)))
            {
                Phase = GamePhase.Lost;
                return;
            }
            if (!grows) { _occupied[Index(tail)] = false; }
            _head = (_head + _body.Length - 1) % _body.Length;
            _body[_head] = next;
            _occupied[Index(next)] = true;
            if (grows)
            {
                Length++;
                if (Length == _body.Length)
                {
                    Food = null;
                    Phase = GamePhase.Won;
                }
                else { PlaceFood(); }
            }
        }

        private bool Contains(Cell cell) => cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;
        private int Index(Cell cell) => cell.Y * Width + cell.X;
        private void PlaceFood()
        {
            _random ^= _random << 13;
            _random ^= _random >> 17;
            _random ^= _random << 5;
            var freeIndex = (int)(_random % (uint)(_body.Length - Length));
            for (var i = 0; i < _occupied.Length; i++)
            {
                if (!_occupied[i] && freeIndex-- == 0)
                {
                    Food = new Cell(i % Width, i / Width);
                    return;
                }
            }
            throw new InvalidOperationException("空きマス数と占有状態が一致しません。");
        }
    }
}
