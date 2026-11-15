using System;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 自動でパドルを動かす操縦役（証跡撮影とテスト用）。ボールを追い、打ち返すたびに乱数で選んだずれで当てて角度をばらつかせる。
    /// ボールが上がっている間は落ちてくるカプセルを取りに行く。
    /// 乱数はシードから決まり、ゲームの乱数とは別系列。
    /// </summary>
    public sealed class Autopilot
    {
        /// <summary>パドルの半幅に対する、当てる位置のずれの最大の割合。</summary>
        private const float MaxOffsetRatio = 0.8f;

        // 狙いとのずれ 1 単位あたりの入力の強さ。ずれが小さいときに行き過ぎないよう比例させる。
        private const float Gain = 8f;

        private readonly SeededRandom _random;
        private float _offset;
        private bool _rising;

        public Autopilot(int seed)
        {
            _random = new SeededRandom(seed);
        }

        /// <summary>この Tick のパドル入力（-1〜+1）。</summary>
        public float Decide(BreakoutState state)
        {
            var rising = state.Ball.Velocity.Y > 0f;
            if (rising && !_rising)
            {
                _offset = _random.Range(-MaxOffsetRatio, MaxOffsetRatio) * state.Paddle.HalfWidth;
            }

            _rising = rising;
            var target = rising && LowestCapsule(state) is Capsule capsule ? capsule.Position.X : state.Ball.Position.X - _offset;
            return Math.Clamp((target - state.Paddle.X) * Gain, -1f, 1f);
        }

        // ボールが上がっている間は、いちばん下のカプセルを取りに行く。
        private static Capsule? LowestCapsule(BreakoutState state)
        {
            Capsule? lowest = null;
            foreach (var capsule in state.Capsules)
            {
                if (lowest == null || capsule.Position.Y < lowest.Position.Y)
                {
                    lowest = capsule;
                }
            }

            return lowest;
        }
    }
}
