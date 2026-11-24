using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 敵の奥行き（<see cref="Enemy.Distance"/>）と列から、画面の位置と大きさを決める疑似奥行き。描画とテストで同じ関数を使う。
    /// 奥ほど小さく地平線（消失点）に寄る：大きさ = 1 / (1 + 3t)（t = 奥行き / <see cref="Enemy.Far"/>）。
    /// 奥行きは時間に比例して減るので、手前に来るほど速く大きくなって見える。
    /// </summary>
    public static class InkVigilPerspective
    {
        /// <summary>消失点の高さ（世界座標）。</summary>
        public const float HorizonY = 1.4f;

        /// <summary>手前の線の高さ（世界座標）。</summary>
        public const float NearY = -3.1f;

        /// <summary>手前の線での列の間隔。</summary>
        public const float LaneSpacing = 2.7f;

        /// <summary>手前の線での敵の大きさ（スプライトの拡大率）。</summary>
        public const float NearScale = 1.25f;

        /// <summary>奥行き t（0＝手前の線、1＝奥）での大きさの比。</summary>
        public static float ScaleAt(float t) => 1f / (1f + (3f * Mathf.Clamp01(t)));

        /// <summary>列の中央からのずれ（手前の線での x）。</summary>
        public static float LaneX(int lane, int lanes) => (lane - ((lanes - 1) / 2f)) * LaneSpacing;

        /// <summary>敵の足もとの位置（世界座標）。</summary>
        public static Vector3 Foot(int lane, int lanes, int distance)
        {
            var s = ScaleAt((float)distance / Enemy.Far);
            return new Vector3(LaneX(lane, lanes) * s, HorizonY + ((NearY - HorizonY) * s), 0);
        }

        /// <summary>敵のスプライトの拡大率。</summary>
        public static float SpriteScale(int distance) => NearScale * ScaleAt((float)distance / Enemy.Far);

        /// <summary>手前ほど前に描く順序（大きいほど前）。</summary>
        public static int SortingOrder(int distance) => 100 + ((Enemy.Far - distance) / 100);
    }
}
