using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 弾幕 1 つを演じる：ボスを動きの列どおりに動かし、発射器の予定の Tick に弾を置き場へ入れる。
    /// <para>
    /// 1 Tick（<see cref="Step"/>）の順序：経過 Tick を 1 進める → ボスを動かす → 発射器を台本の順に撃つ。
    /// 弾は動いた後のボスの位置（＋ずれ）から出る。
    /// </para>
    /// <para>
    /// 乱数は発射器ごと・動きで系列を分ける（ある発射器に rand を足しても、他の発射器とボスの動きは変わらない）。
    /// </para>
    /// </summary>
    public sealed class PatternPlayer
    {
        private SeededRandom[] _emitterRandoms;
        private SeededRandom _moveRandom;
        private int _moveIndex;
        private int _moveElapsed;
        private Vec2i _moveFrom;
        private Vec2i _moveTarget;

        /// <param name="pattern">演じる弾幕。</param>
        /// <param name="bossStart">ボスの始めの位置（<see cref="Field.Unit"/> 単位）。</param>
        /// <param name="seed">この弾幕の乱数のシード。</param>
        public PatternPlayer(PatternSpec pattern, Vec2i bossStart, int seed)
        {
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
            Boss = bossStart;
            _moveFrom = bossStart;
            _moveTarget = bossStart;
            _moveRandom = new SeededRandom(unchecked(seed ^ 0x4D4F5645));
            _emitterRandoms = new SeededRandom[pattern.Emitters.Count];
            for (var i = 0; i < _emitterRandoms.Length; i++)
            {
                _emitterRandoms[i] = new SeededRandom(unchecked(seed ^ ((i + 1) * (int)0x9E3779B1)));
            }
        }

        public PatternSpec Pattern { get; }

        /// <summary>弾幕が始まってから進めた Tick 数。</summary>
        public int Elapsed { get; private set; }

        /// <summary>ボスの位置（<see cref="Field.Unit"/> 単位）。</summary>
        public Vec2i Boss { get; private set; }

        /// <summary>1 Tick 進める。<paramref name="player"/> は自機の位置（自機狙いの向きに使う）。</summary>
        public void Step(Vec2i player, BulletPool pool)
        {
            if (pool == null) { throw new ArgumentNullException(nameof(pool)); }
            Elapsed++;
            Move();

            var t = Elapsed - 1;
            for (var i = 0; i < Pattern.Emitters.Count; i++)
            {
                var emitter = Pattern.Emitters[i];
                if (t < emitter.Delay || (t - emitter.Delay) % emitter.Every != 0) { continue; }
                Fire(emitter, (t - emitter.Delay) / emitter.Every, _emitterRandoms[i], Boss, player, pool);
            }
        }

        /// <summary>
        /// 発射器の <paramref name="volley"/> 回目（0 始まり）の弾を置き場に入れる。
        /// 中心の向き = angle + volley × spin（aimed なら自機の向きを足す）。ring は全周を count 等分、fan / aimed は spread を count-1 等分する。
        /// </summary>
        public static void Fire(EmitterSpec emitter, int volley, SeededRandom random, Vec2i boss, Vec2i player, BulletPool pool)
        {
            if (emitter == null) { throw new ArgumentNullException(nameof(emitter)); }
            if (random == null) { throw new ArgumentNullException(nameof(random)); }
            if (pool == null) { throw new ArgumentNullException(nameof(pool)); }

            var origin = boss + new Vec2i(emitter.OffsetX.Pick(random), emitter.OffsetY.Pick(random));
            var center = emitter.Angle.Pick(random) + (int)((long)volley * emitter.Spin % Trig.FullTurn);
            if (emitter.Kind == EmitterKind.Aimed)
            {
                var toPlayer = player - origin;
                center += Trig.Atan2(toPlayer.Y, toPlayer.X, Trig.Down);
            }

            for (var i = 0; i < emitter.Count; i++)
            {
                var angle = center + OffsetOf(emitter, i);
                var spawn = new BulletSpawn(
                    origin,
                    angle,
                    emitter.Speed.Pick(random),
                    emitter.Accel.Pick(random),
                    emitter.MaxSpeed,
                    emitter.Curve.Pick(random),
                    emitter.Life,
                    emitter.Shape,
                    emitter.Color);
                pool.Spawn(spawn);
            }
        }

        /// <summary>i 発目の中心からの向きのずれ。</summary>
        public static int OffsetOf(EmitterSpec emitter, int index)
        {
            if (emitter == null) { throw new ArgumentNullException(nameof(emitter)); }
            if (emitter.Kind == EmitterKind.Ring) { return IntMath.RoundDiv((long)Trig.FullTurn * index, emitter.Count); }
            if (emitter.Count == 1) { return 0; }

            // -spread/2 から +spread/2 まで等間隔（中心について対称に丸める）。
            return IntMath.RoundDiv((long)emitter.Spread * ((2 * index) - (emitter.Count - 1)), 2L * (emitter.Count - 1));
        }

        public PatternPlayer Clone()
        {
            var copy = (PatternPlayer)MemberwiseClone();
            copy._moveRandom = _moveRandom.Clone();
            copy._emitterRandoms = new SeededRandom[_emitterRandoms.Length];
            for (var i = 0; i < _emitterRandoms.Length; i++) { copy._emitterRandoms[i] = _emitterRandoms[i].Clone(); }
            return copy;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Elapsed);
            hash.Mix(Boss.X);
            hash.Mix(Boss.Y);
            hash.Mix(_moveIndex);
            hash.Mix(_moveElapsed);
            hash.Mix(_moveFrom.X);
            hash.Mix(_moveFrom.Y);
            hash.Mix(_moveTarget.X);
            hash.Mix(_moveTarget.Y);
            hash.Mix(_moveRandom.State);
            foreach (var random in _emitterRandoms) { hash.Mix(random.State); }
        }

        /// <summary>動きの列を 1 Tick 進める。各動きは始めの Tick に行き先を決め、ticks Tick かけて一定の速さで動く。列は繰り返す。</summary>
        private void Move()
        {
            var moves = Pattern.Moves;
            if (moves.Count == 0) { return; }

            var move = moves[_moveIndex];
            if (_moveElapsed == 0)
            {
                _moveFrom = Boss;
                _moveTarget = move.Kind switch
                {
                    MoveKind.To => move.Target,
                    MoveKind.Wander => new Vec2i(
                        _moveRandom.Range(move.Area.MinX, move.Area.MaxX),
                        _moveRandom.Range(move.Area.MinY, move.Area.MaxY)),
                    _ => Boss,
                };
            }

            _moveElapsed++;
            var delta = _moveTarget - _moveFrom;
            Boss = _moveFrom + new Vec2i(
                IntMath.RoundDiv((long)delta.X * _moveElapsed, move.Ticks),
                IntMath.RoundDiv((long)delta.Y * _moveElapsed, move.Ticks));

            if (_moveElapsed == move.Ticks)
            {
                _moveElapsed = 0;
                _moveIndex = (_moveIndex + 1) % moves.Count;
            }
        }
    }
}
