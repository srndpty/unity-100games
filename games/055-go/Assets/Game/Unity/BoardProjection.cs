#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 盤の交点と世界の座標の対応。交点の間隔が 1、天元（E5）が原点。盤の絵は 10 × 10（端の線から半マスの余白）。
    /// 描画とクリックの判定は同じ式を使う（018 の TableLayout と同じ考え）。
    /// </summary>
    public static class BoardProjection
    {
        /// <summary>盤の絵の 1 辺（世界の単位）。</summary>
        public const float BoardSize = Points.Size + 1;

        /// <summary>交点を拾う半径（隣の交点との中間より少し内側）。</summary>
        public const float PickRadius = 0.48f;

        /// <summary>カメラの位置（盤を左寄せにし、右側を HUD の板に空ける）。</summary>
        public static readonly Vector3 CameraPosition = new(3.6f, 0f, -10f);

        /// <summary>カメラの縦の半分の大きさ（16:9 で盤と右の板が収まる）。</summary>
        public const float CameraSize = 5.9f;

        public static Vector3 World(int point)
        {
            var half = (Points.Size - 1) / 2f;
            return new Vector3(Points.Column(point) - half, Points.Row(point) - half, 0f);
        }

        /// <summary>世界の座標に最も近い交点（<see cref="PickRadius"/> より遠ければ -1）。</summary>
        public static int PointAt(Vector2 world)
        {
            var half = (Points.Size - 1) / 2f;
            var column = Mathf.RoundToInt(world.x + half);
            var row = Mathf.RoundToInt(world.y + half);
            if (column < 0 || column >= Points.Size || row < 0 || row >= Points.Size) { return -1; }
            var point = Points.At(column, row);
            var d = world - (Vector2)World(point);
            return d.sqrMagnitude <= PickRadius * PickRadius ? point : -1;
        }

        /// <summary>16:9 より縦長の画面でも盤が切れないように、カメラの大きさを決める（横長の範囲だけを考える）。</summary>
        public static float CameraSizeFor(float aspect)
        {
            // 盤の左端（-5）と少しの余白が、カメラの左端より内側に入る。
            var needHalfWidth = CameraPosition.x + (BoardSize / 2f) + 0.4f;
            return Mathf.Max(CameraSize, needHalfWidth / Mathf.Max(0.1f, aspect));
        }
    }
}
