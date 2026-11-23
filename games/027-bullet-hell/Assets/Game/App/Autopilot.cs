using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 自動操縦（撮影・テスト用）。いつもショットを撃ち、ボムは使わない。
    /// <para>
    /// 動き：敵弾の置き場を作業用の置き場へ写して <see cref="Horizon"/> Tick 先まで進め、9 通りの向き × 通常・低速の 18 通りの
    /// 「同じ操作を続けた自機」の通り道を Core と同じ移動（<see cref="VeilRun.Moved"/>）と掃引（<see cref="VeilRun.Touches"/>）で調べる。
    /// 選ぶ順：触れず、弾との隙間が <see cref="SafeClearance"/> 以上ある候補があれば、その中でボスの真下の決まった高さに近いもの。
    /// なければ、最初に触れる Tick が遅い（触れなければ最良）→ 弾との隙間が広い → 位置の好み。
    /// 先読みの間に新しく撃たれる弾は見ない（ボスの近くから出るので、先読みの長さの間には自機へ届かない）。
    /// </para>
    /// <para>
    /// 判断は <see cref="DecideEvery"/> Tick ごと。試合や弾幕が変わったら、続けている判断を捨てる（023 の知見）。
    /// 整数だけで決めるので、同じ試合なら Mono と .NET で同じ操作になる。
    /// </para>
    /// </summary>
    public sealed class Autopilot : IPilot
    {
        public const int Horizon = 16;

        public const int DecideEvery = 2;

        /// <summary>先読みの間の弾との隙間がこれ以上なら「安全」とし、位置の好みだけで選ぶ。</summary>
        public const int SafeClearance = 4 * Field.Unit;

        /// <summary>隙間を数える上限（これより広い隙間は同じとみなす）。</summary>
        public const int ClearanceCap = 64 * Field.Unit;

        /// <summary>判定の半径に足す余裕（先読みで見ない弾の分）。</summary>
        public const int SafetyMargin = Field.Unit;

        /// <summary>ボスの真下の、自機がとどまりたい高さ。</summary>
        public const int PreferredY = 80 * Field.Unit;

        private const int Candidates = 18;

        private static readonly int[] _horizontal = { 0, -1, 1, 0, 0, -1, 1, -1, 1 };
        private static readonly int[] _vertical = { 0, 0, 0, 1, -1, 1, 1, -1, -1 };

        private readonly Vec2i[] _position = new Vec2i[Candidates];
        private readonly Vec2i[] _previous = new Vec2i[Candidates];
        private readonly int[] _hitTick = new int[Candidates];
        private readonly long[] _clearance = new long[Candidates];
        private readonly List<int> _near = new(256);
        private BulletPool? _scratch;
        private VeilRun? _run;
        private VeilConfig? _config;
        private int _stage = -1;
        private PlayerCommand _plan;
        private int _hold;

        public PlayerCommand CommandFor(VeilRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (!ReferenceEquals(run, _run) || run.StageIndex != _stage)
            {
                _run = run;
                _config = run.Config;
                _stage = run.StageIndex;
                _hold = 0;
                if (_scratch == null || _scratch.Capacity != run.Bullets.Capacity) { _scratch = new BulletPool(run.Bullets.Capacity); }
            }

            if (_hold > 0)
            {
                _hold--;
                return _plan;
            }

            _plan = Choose(run);
            _hold = DecideEvery - 1;
            return _plan;
        }

        private PlayerCommand Choose(VeilRun run)
        {
            var config = _config!;
            var scratch = _scratch!;
            scratch.CopyFrom(run.Bullets);
            for (var c = 0; c < Candidates; c++)
            {
                _position[c] = run.Player;
                _hitTick[c] = int.MaxValue;
                _clearance[c] = ClearanceCap;
            }

            for (var k = 1; k <= Horizon; k++)
            {
                scratch.Step();
                CollectNear(scratch, run.Player, (k * config.PlayerSpeed) + (32 * Field.Unit));
                for (var c = 0; c < Candidates; c++)
                {
                    if (_hitTick[c] != int.MaxValue) { continue; }
                    _previous[c] = _position[c];
                    _position[c] = VeilRun.Moved(_position[c], _horizontal[c % 9], _vertical[c % 9], c >= 9, config);
                    Probe(scratch, c, k, config.PlayerRadius + SafetyMargin);
                }
            }

            var best = 0;
            for (var c = 1; c < Candidates; c++)
            {
                if (Better(c, best, run)) { best = c; }
            }

            return new PlayerCommand(_horizontal[best % 9], _vertical[best % 9], best >= 9, shoot: true, bomb: false);
        }

        /// <summary>自機の近くの弾だけを集める（遠い弾は先読みの間に届かない）。</summary>
        private void CollectNear(BulletPool pool, Vec2i center, int reach)
        {
            _near.Clear();
            for (var i = 0; i < pool.Count; i++)
            {
                var p = pool.PositionOf(i);
                if (Math.Abs(p.X - center.X) <= reach && Math.Abs(p.Y - center.Y) <= reach) { _near.Add(i); }
            }
        }

        private void Probe(BulletPool pool, int c, int tick, int playerRadius)
        {
            foreach (var i in _near)
            {
                var radius = pool.RadiusOf(i) + playerRadius;
                var end = pool.PositionOf(i);
                if (VeilRun.Touches(pool.StartOf(i), end, _previous[c], _position[c], radius))
                {
                    _hitTick[c] = tick;
                    return;
                }

                // 隙間は各軸の隔たりの大きいほう（チェビシェフ距離）から半径を引いた近似。
                var gap = (long)Math.Max(Math.Abs(end.X - _position[c].X), Math.Abs(end.Y - _position[c].Y)) - radius;
                if (gap < _clearance[c]) { _clearance[c] = gap; }
            }
        }

        private bool Better(int a, int b, VeilRun run)
        {
            var safeA = IsSafe(a);
            var safeB = IsSafe(b);
            if (safeA != safeB) { return safeA; }
            if (safeA) { return Penalty(a, run) < Penalty(b, run); }
            if (_hitTick[a] != _hitTick[b]) { return _hitTick[a] > _hitTick[b]; }
            var clearA = Math.Max(0, _clearance[a]);
            var clearB = Math.Max(0, _clearance[b]);
            if (clearA != clearB) { return clearA > clearB; }
            return Penalty(a, run) < Penalty(b, run);
        }

        private bool IsSafe(int c) => _hitTick[c] == int.MaxValue && _clearance[c] >= SafeClearance;

        private long Penalty(int c, VeilRun run) =>
            Math.Abs((long)_position[c].X - run.Boss.X) + (Math.Abs((long)_position[c].Y - PreferredY) / 2);
    }
}
