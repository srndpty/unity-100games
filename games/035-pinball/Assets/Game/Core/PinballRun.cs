using System;
using System.Runtime.CompilerServices;
using IntGeometry;
using TileMotion;

[assembly: InternalsVisibleTo("Game.Core.Tests")]
[assembly: InternalsVisibleTo("G035.Core.Tests")]

namespace Game.Core
{
    public enum BallState
    {
        /// <summary>使っていない置き場（まだ出していない・失った）。</summary>
        Idle,

        /// <summary>打ち出しレーンの置き場で止まっている。</summary>
        Lane,

        /// <summary>台の上を動いている。</summary>
        Free,
    }

    public enum RunStatus
    {
        Playing,

        /// <summary>全ての球を失った。以後の <see cref="PinballRun.Step"/> は何もしない。</summary>
        Over,
    }

    /// <summary>1 Tick の入力（押しているか）。</summary>
    public readonly struct PinballInput : IEquatable<PinballInput>
    {
        public PinballInput(bool left, bool right, bool plunger)
        {
            Left = left;
            Right = right;
            Plunger = plunger;
        }

        public bool Left { get; }

        public bool Right { get; }

        public bool Plunger { get; }

        public bool Equals(PinballInput other) => Left == other.Left && Right == other.Right && Plunger == other.Plunger;

        public override bool Equals(object? obj) => obj is PinballInput other && Equals(other);

        public override int GetHashCode() => (Left ? 1 : 0) | (Right ? 2 : 0) | (Plunger ? 4 : 0);

        public static bool operator ==(PinballInput left, PinballInput right) => left.Equals(right);

        public static bool operator !=(PinballInput left, PinballInput right) => !left.Equals(right);
    }

    /// <summary>1 Tick に起きたこと（音・演出・テストの照合用）。</summary>
    public struct TickEvents
    {
        public int BumperHits;

        /// <summary>バンパーごとの当たった回数のビット（演出用、番号 i が当たったら 1 &lt;&lt; i）。</summary>
        public int BumpersLit;

        public int TargetHits;

        public int TargetSets;

        public int FlipperHits;

        public int WallHits;

        public int Launches;

        public int BallsLost;

        public bool MultiballStarted;

        /// <summary>台の上の最後の球を失い、1 球が終わった。</summary>
        public bool BallEnded;

        public bool GameOver;

        public long ScoreGained;
    }

    /// <summary>球の見え方（読み取り専用）。</summary>
    public readonly struct BallView
    {
        public BallView(BallState state, Vec2i position, Vec2i velocity)
        {
            State = state;
            Position = position;
            Velocity = velocity;
        }

        public BallState State { get; }

        public Vec2i Position { get; }

        public Vec2i Velocity { get; }
    }

    /// <summary>
    /// 1 試合。<see cref="Step"/> で 1 Tick 進める。Tick の始めに入力（フリッパー・プランジャー）を反映し、
    /// <see cref="Board.Substeps"/> 区間それぞれで「フリッパーを回す → 重なった球を押し出す → 重力 → 球を経路で動かし最初の接触で反射」の順に進める。
    /// 球どうしは当たらない（すり抜ける）。
    /// </summary>
    public sealed class PinballRun
    {
        private readonly PinballConfig _config;
        private readonly Table _table;
        private readonly Shape[] _shapes;
        private readonly int[] _shapeColliders;
        private readonly int[] _scratch;
        private Ball[] _balls;
        private bool[] _targetsUp;
        private Flipper _left;
        private Flipper _right;
        private SeededRandom _multiballRandom;
        private TickEvents _events;

        public PinballRun(PinballConfig config, Table table, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _table = table ?? throw new ArgumentNullException(nameof(table));
            table.Validate(config);
            _config = config.Copy();
            Seed = seed;
            _shapes = new Shape[table.Colliders.Count + 2];
            _shapeColliders = new int[_shapes.Length];
            _scratch = new int[_shapes.Length];
            _balls = new Ball[PinballConfig.MaxBalls];
            _targetsUp = new bool[Table.TargetCount];
            for (var i = 0; i < _targetsUp.Length; i++) { _targetsUp[i] = true; }
            _left = new Flipper(table.Left);
            _right = new Flipper(table.Right);
            _multiballRandom = new SeededRandom(unchecked(seed ^ 0x4D554C54));
            BallNumber = 1;
            Serve();
        }

        public int Seed { get; }

        public Table Table => _table;

        /// <summary>設定の複製（Core の内部の設定は書き換えられない）。</summary>
        public PinballConfig Config => _config.Copy();

        public RunStatus Status { get; private set; }

        public long Ticks { get; private set; }

        public long Score { get; private set; }

        /// <summary>今の球の番号（1〜<see cref="PinballConfig.Balls"/>）。</summary>
        public int BallNumber { get; private set; }

        /// <summary>まだ出していない球の数（今の球を含まない）。</summary>
        public int BallsInReserve => _config.Balls - BallNumber;

        /// <summary>プランジャーを引いている Tick 数（0〜<see cref="PinballConfig.PlungerMaxTicks"/>）。</summary>
        public int Pull { get; private set; }

        /// <summary>これまでに台へ出した球の数（各球の最初の 1 個 + マルチボールで足した数）。</summary>
        public int BallsServed { get; private set; }

        /// <summary>これまでに失った球の数。</summary>
        public int BallsLost { get; private set; }

        /// <summary>レーンか台の上にある球の数。</summary>
        public int BallsActive
        {
            get
            {
                var count = 0;
                foreach (var ball in _balls) { if (ball.State != BallState.Idle) { count++; } }
                return count;
            }
        }

        public bool IsMultiball => CountState(BallState.Free) + CountState(BallState.Lane) > 1;

        /// <summary>直前の <see cref="Step"/> の出来事。</summary>
        public TickEvents LastEvents => _events;

        public int BallSlots => _balls.Length;

        public BallView BallAt(int slot) => new(_balls[slot].State, _balls[slot].Position, _balls[slot].Velocity);

        public Flipper FlipperOf(FlipperSide side) => (side == FlipperSide.Left ? _left : _right).Clone();

        public bool TargetUp(int number) => _targetsUp[number];

        public PinballRun Clone()
        {
            var copy = (PinballRun)MemberwiseClone();
            copy._balls = (Ball[])_balls.Clone();
            copy._targetsUp = (bool[])_targetsUp.Clone();
            copy._left = _left.Clone();
            copy._right = _right.Clone();
            copy._multiballRandom = _multiballRandom.Clone();
            return copy;
        }

        public uint Hash()
        {
            var hash = StateHash.Start();
            _config.MixInto(ref hash);
            _table.MixInto(ref hash);
            hash.Mix(Seed);
            hash.Mix((int)Status);
            hash.Mix(Ticks);
            hash.Mix(Score);
            hash.Mix(BallNumber);
            hash.Mix(Pull);
            hash.Mix(BallsServed);
            hash.Mix(BallsLost);
            hash.Mix(_multiballRandom.State);
            foreach (var ball in _balls) { ball.MixInto(ref hash); }
            foreach (var up in _targetsUp) { hash.Mix(up); }
            _left.MixInto(ref hash);
            _right.MixInto(ref hash);
            return hash.Value;
        }

        /// <summary>1 Tick 進めて出来事を返す。終了後は何もしない。</summary>
        public TickEvents Step(PinballInput input)
        {
            _events = default;
            if (Status == RunStatus.Over) { return _events; }
            Ticks++;
            _left.Held = input.Left;
            _right.Held = input.Right;
            Plunge(input.Plunger);

            var lost = 0;
            for (var sub = 0; sub < Board.Substeps; sub++)
            {
                _left.Step(_config);
                _right.Step(_config);
                for (var slot = 0; slot < _balls.Length; slot++)
                {
                    if (_balls[slot].State != BallState.Free) { continue; }
                    PushOut(slot, _left);
                    PushOut(slot, _right);
                    Move(slot);
                    if (Lost(slot))
                    {
                        _balls[slot] = default;
                        lost++;
                    }
                }
            }

            if (lost > 0)
            {
                BallsLost += lost;
                _events.BallsLost = lost;
                if (BallsActive == 0) { EndBall(); }
            }

            return _events;
        }

        /// <summary>テスト用：置き場に球を置く（台の上で動かす）。</summary>
        internal void Place(int slot, Vec2i position, Vec2i velocity)
        {
            if (_balls[slot].State == BallState.Idle) { BallsServed++; }
            _balls[slot] = new Ball { State = BallState.Free, Position = position, Velocity = velocity };
        }

        /// <summary>テスト用：置き場を空ける（失ったことにしない）。</summary>
        internal void Remove(int slot)
        {
            if (_balls[slot].State != BallState.Idle) { BallsServed--; }
            _balls[slot] = default;
        }

        private int CountState(BallState state)
        {
            var count = 0;
            foreach (var ball in _balls) { if (ball.State == state) { count++; } }
            return count;
        }

        private int LaneSlot()
        {
            for (var slot = 0; slot < _balls.Length; slot++) { if (_balls[slot].State == BallState.Lane) { return slot; } }
            return -1;
        }

        private int IdleSlot()
        {
            for (var slot = 0; slot < _balls.Length; slot++) { if (_balls[slot].State == BallState.Idle) { return slot; } }
            return -1;
        }

        /// <summary>空いた置き場に球を出してレーンに置く。</summary>
        private int Serve()
        {
            var slot = IdleSlot();
            if (slot < 0 || LaneSlot() >= 0) { return -1; }
            _balls[slot] = new Ball { State = BallState.Lane, Position = _table.LaneRest };
            BallsServed++;
            return slot;
        }

        /// <summary>レーンに球があれば、押している間は引き、離したら引きに比例した速さで打ち出す。</summary>
        private void Plunge(bool held)
        {
            var slot = LaneSlot();
            if (slot < 0)
            {
                Pull = 0;
                return;
            }

            if (held)
            {
                Pull = Math.Min(Pull + 1, _config.PlungerMaxTicks);
                return;
            }

            if (Pull == 0) { return; }
            var span = _config.PlungerMaxSpeed - _config.PlungerMinSpeed;
            var speed = _config.PlungerMaxTicks == 1
                ? _config.PlungerMaxSpeed
                : _config.PlungerMinSpeed + (int)((long)span * (Pull - 1) / (_config.PlungerMaxTicks - 1));
            Launch(slot, speed);
            Pull = 0;
        }

        private void Launch(int slot, int speed)
        {
            _balls[slot].State = BallState.Free;
            _balls[slot].Velocity = new Vec2i(0, speed);
            _balls[slot].Fraction = Vec2i.Zero;
            _events.Launches++;
        }

        /// <summary>フリッパーが回って球に重なったら、面の外へ押し出し、面の速さを加えて反射する。</summary>
        private void PushOut(int slot, Flipper flipper)
        {
            ref var ball = ref _balls[slot];
            var reach = flipper.Spec.Radius + _config.BallRadius;
            var tip = flipper.Tip;
            if (!Capsule.Inside(ball.Position, flipper.Spec.Pivot, tip, reach)) { return; }
            var closest = Capsule.Closest(ball.Position, flipper.Spec.Pivot, tip);
            var normal = NormalAt(ball.Position, closest, flipper.Spec.Pivot, tip, ball.Velocity);
            ball.Position = Escape(closest, normal, flipper.Spec.Pivot, tip, reach);
            ball.Velocity = Reflect(ball.Velocity, normal, flipper.SurfaceVelocity(closest), _config.FlipperRestitution);
            _events.FlipperHits++;
        }

        /// <summary>
        /// 重力を加えて 1 区間動かす。経路の最初の接触の手前の点で反射し、区間の残りの割合を反射後の速度で続けて動く
        /// （斜面を滑る球が止まらない）。1 区間の接触は <see cref="MaxContactsPerSubstep"/> 回までで、超えたら残りを捨てる。
        /// </summary>
        private void Move(int slot)
        {
            ref var ball = ref _balls[slot];
            ball.Velocity = Limit(new Vec2i(ball.Velocity.X, ball.Velocity.Y - _config.Gravity));

            var fineX = ((long)ball.Position.X * Board.VelocityScale) + ball.Fraction.X + ball.Velocity.X;
            var fineY = ((long)ball.Position.Y * Board.VelocityScale) + ball.Fraction.Y + ball.Velocity.Y;
            var target = new Vec2i((int)(fineX >> 8), (int)(fineY >> 8));
            var fraction = new Vec2i((int)(fineX & (Board.VelocityScale - 1)), (int)(fineY & (Board.VelocityScale - 1)));

            for (var contact = 0; contact <= MaxContactsPerSubstep; contact++)
            {
                var count = BuildShapes(ball.Position, ball.Velocity);
                var hit = BallPath.First(ball.Position, target, _shapes, count, _scratch, out var step);
                if (hit < 0)
                {
                    ball.Position = target;
                    ball.Fraction = fraction;
                    return;
                }

                if (contact == MaxContactsPerSubstep) { break; }
                var total = BallPath.Steps(ball.Position, target);
                var start = ball.Position;
                var used = Math.Max(0, step - 1);
                var before = BallPath.PointAt(start, target, used, total);
                ball.Position = Collide(ref ball, hit, before, step == 0);

                // 区間の残り（経路の点の数の割合）を、反射後の速度で進む。位置の端数は始めの値を保つ（遅い滑りも積み上がる）。
                var remaining = total == 0 ? 1L : total - used;
                var span = total == 0 ? 1L : total;
                fineX = ((long)ball.Position.X * Board.VelocityScale) + ball.Fraction.X + (ball.Velocity.X * remaining / span);
                fineY = ((long)ball.Position.Y * Board.VelocityScale) + ball.Fraction.Y + (ball.Velocity.Y * remaining / span);
                target = new Vec2i((int)(fineX >> 8), (int)(fineY >> 8));
                fraction = new Vec2i((int)(fineX & (Board.VelocityScale - 1)), (int)(fineY & (Board.VelocityScale - 1)));
            }
        }

        /// <summary>1 区間の接触の回数の上限（隅に挟まった球が区間の中で跳ね続けない）。</summary>
        public const int MaxContactsPerSubstep = 4;

        /// <summary>接触を解決する：手前の点（始めから内側なら面の外）に置き、相手に応じて反射・得点する。球の新しい位置を返す。</summary>
        private Vec2i Collide(ref Ball ball, int hit, Vec2i before, bool startedInside)
        {
            var shape = _shapes[hit];
            var closest = Capsule.Closest(before, shape.A, shape.B);
            var normal = NormalAt(before, closest, shape.A, shape.B, ball.Velocity);
            var position = startedInside ? Escape(closest, normal, shape.A, shape.B, shape.Reach) : before;

            var collider = _shapeColliders[hit];
            if (collider < 0)
            {
                var flipper = collider == -1 ? _left : _right;
                ball.Velocity = Limit(Reflect(ball.Velocity, normal, flipper.SurfaceVelocity(closest), _config.FlipperRestitution));
                _events.FlipperHits++;
                return position;
            }

            var spec = _table.Colliders[collider];
            switch (spec.Kind)
            {
                case ColliderKind.Bumper:
                    ball.Velocity = Limit(Reflect(ball.Velocity, normal, Vec2i.Zero, _config.BumperRestitution) + IntMath.Scale(normal, _config.BumperKick));
                    _events.BumperHits++;
                    _events.BumpersLit |= 1 << spec.Number;
                    Gain(_config.BumperScore);
                    break;
                case ColliderKind.Target:
                    ball.Velocity = Reflect(ball.Velocity, normal, Vec2i.Zero, _config.TargetRestitution);
                    HitTarget(spec.Number);
                    break;
                default:
                    ball.Velocity = Reflect(ball.Velocity, normal, Vec2i.Zero, _config.WallRestitution);
                    _events.WallHits++;
                    break;
            }

            return position;
        }

        /// <summary>
        /// この区間で当たりうる相手を並べる。除くもの：倒れたターゲット、通り抜ける向きの扉・始めから重なった扉、
        /// 球が近づいていない相手（始点での法線と相対速度の内積が 0 以上）。凸な相手に近づいていない直線の経路は内側に入らないので、
        /// 除いても当たりを見逃さない。斜面を滑る球が、経路の点の丸めだけで内側に入って止まり続けることを防ぐ。フリッパーは最後に並べる。
        /// </summary>
        private int BuildShapes(Vec2i position, Vec2i velocity)
        {
            var count = 0;
            var colliders = _table.Colliders;
            for (var i = 0; i < colliders.Count; i++)
            {
                var collider = colliders[i];
                var reach = collider.Radius + _config.BallRadius;
                if (collider.Kind == ColliderKind.Target && !_targetsUp[collider.Number]) { continue; }
                if (collider.Kind == ColliderKind.Gate)
                {
                    var d = collider.B - collider.A;
                    var leftNormal = new Vec2i(-d.Y, d.X);
                    if (Vec2i.Dot(velocity, leftNormal) >= 0 || Capsule.Inside(position, collider.A, collider.B, reach)) { continue; }
                }

                if (!Approaching(position, velocity, collider.A, collider.B)) { continue; }
                _shapeColliders[count] = i;
                _shapes[count++] = new Shape(collider.A, collider.B, reach, i);
            }

            count = AddFlipper(count, _left, -1, position, velocity);
            return AddFlipper(count, _right, -2, position, velocity);
        }

        /// <summary>
        /// 区間の移動の間、フリッパーは今の姿勢で止まっているとみなす（動きは区間の始めの押し出しで扱う）。
        /// 近づいているかも止まった姿勢に対して判定する（面の速さを使うと、先端が速く遠ざかる間に球が今の姿勢へ食い込む）。
        /// </summary>
        private int AddFlipper(int count, Flipper flipper, int id, Vec2i position, Vec2i velocity)
        {
            var tip = flipper.Tip;
            if (!Approaching(position, velocity, flipper.Spec.Pivot, tip)) { return count; }
            _shapeColliders[count] = id;
            _shapes[count] = new Shape(flipper.Spec.Pivot, tip, flipper.Spec.Radius + _config.BallRadius, id);
            return count + 1;
        }

        /// <summary>球が止まった線分 a-b に近づいているか。中心が線分の上にあれば近づいているとみなす。</summary>
        private static bool Approaching(Vec2i position, Vec2i velocity, Vec2i a, Vec2i b)
        {
            var normal = position - Capsule.Closest(position, a, b);
            return normal.IsZero || Vec2i.Dot(velocity, normal) < 0;
        }

        private void HitTarget(int number)
        {
            if (!_targetsUp[number]) { return; }
            _targetsUp[number] = false;
            _events.TargetHits++;
            Gain(_config.TargetScore);
            foreach (var up in _targetsUp) { if (up) { return; } }

            for (var i = 0; i < _targetsUp.Length; i++) { _targetsUp[i] = true; }
            _events.TargetSets++;
            Gain(_config.TargetSetScore);

            // マルチボール：台の上が 1 個だけ（レーンに球がない）ときだけ、2 個目を出して自動で打ち出す。
            if (CountState(BallState.Free) != 1 || LaneSlot() >= 0) { return; }
            var slot = Serve();
            if (slot < 0) { return; }
            Launch(slot, _multiballRandom.Range(_config.AutoLaunchMin, _config.AutoLaunchMax));
            _events.MultiballStarted = true;
        }

        /// <summary>ドレイン線より下・台の外へ出たら失う。レーンの置き場より下へ落ちてきたらレーンに止める。</summary>
        private bool Lost(int slot)
        {
            ref var ball = ref _balls[slot];
            if (ball.Position.Y < _table.DrainY || !Board.InBounds(ball.Position)) { return true; }
            if (ball.Position.X >= _table.LaneX0 && ball.Position.X <= _table.LaneX1 && ball.Position.Y <= _table.LaneRestY &&
                ball.Velocity.Y <= 0 && LaneSlot() < 0)
            {
                ball = new Ball { State = BallState.Lane, Position = _table.LaneRest };
            }

            return false;
        }

        /// <summary>台の上の最後の球を失った：次の球をレーンへ出すか、試合を終える。</summary>
        private void EndBall()
        {
            _events.BallEnded = true;
            Pull = 0;
            if (BallNumber >= _config.Balls)
            {
                Status = RunStatus.Over;
                _events.GameOver = true;
                return;
            }

            BallNumber++;
            Serve();
        }

        private void Gain(int points)
        {
            Score += points;
            _events.ScoreGained += points;
        }

        private Vec2i Limit(Vec2i velocity) =>
            velocity.LengthSquared > (long)_config.MaxSpeed * _config.MaxSpeed ? IntMath.Scale(velocity, _config.MaxSpeed) : velocity;

        /// <summary>
        /// 接触の法線（相手から球へ向く、長さは半径の和ほど）。球の中心が線分の上にある（長さ 0）ときは、
        /// 線分の法線のうち速度と逆を向くほう（円なら速度の逆、止まっていれば上）。
        /// </summary>
        private static Vec2i NormalAt(Vec2i center, Vec2i closest, Vec2i a, Vec2i b, Vec2i velocity)
        {
            var normal = center - closest;
            if (!normal.IsZero) { return normal; }
            var d = b - a;
            if (d.IsZero) { return velocity.IsZero ? new Vec2i(0, 1) : -velocity; }
            var perpendicular = new Vec2i(-d.Y, d.X);
            return Vec2i.Dot(perpendicular, velocity) > 0 ? -perpendicular : perpendicular;
        }

        /// <summary>最も近い点から法線の向きへ、半径の和ちょうど外（内側でない最初の距離）に置く。</summary>
        private static Vec2i Escape(Vec2i closest, Vec2i normal, Vec2i a, Vec2i b, int reach)
        {
            for (var extra = 0; ; extra++)
            {
                var position = closest + IntMath.Scale(normal, reach + extra);
                if (!Capsule.Inside(position, a, b, reach)) { return position; }
            }
        }

        /// <summary>
        /// 面の速さ <paramref name="surface"/> に対する相対速度の法線成分を反発係数で返す（近づいているときだけ）。接線成分はそのまま（摩擦なし）。
        /// </summary>
        public static Vec2i Reflect(Vec2i velocity, Vec2i normal, Vec2i surface, int restitution)
        {
            var relative = velocity - surface;
            var dot = Vec2i.Dot(relative, normal);
            if (dot >= 0 || normal.IsZero) { return velocity; }
            var denominator = normal.LengthSquared * PinballConfig.RatioDenominator;
            var factor = dot * (PinballConfig.RatioDenominator + restitution);
            relative = new Vec2i(relative.X - IntMath.RoundDiv(normal.X * factor, denominator), relative.Y - IntMath.RoundDiv(normal.Y * factor, denominator));

            // 丸めで法線の向きへまだ近づいていたら、近づかなくなるまで法線の向きへ足す（反射の後は必ず離れるか滑る）。
            for (var extra = 1; Vec2i.Dot(relative, normal) < 0; extra++)
            {
                relative += IntMath.Scale(normal, extra);
            }

            return relative + surface;
        }

        private struct Ball
        {
            public BallState State;

            public Vec2i Position;

            /// <summary>位置の端数（各軸 0〜255、1/<see cref="Board.VelocityScale"/> 単位）。</summary>
            public Vec2i Fraction;

            public Vec2i Velocity;

            public void MixInto(ref StateHash hash)
            {
                hash.Mix((int)State);
                hash.Mix(Position.X);
                hash.Mix(Position.Y);
                hash.Mix(Fraction.X);
                hash.Mix(Fraction.Y);
                hash.Mix(Velocity.X);
                hash.Mix(Velocity.Y);
            }
        }
    }
}
