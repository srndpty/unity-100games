using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 敵弾をまとめて 1 つの Mesh で描く。弾 1 発 = 四角形 1 つで、形と色はアトラスの区画（<see cref="UvOf"/>）で選ぶ。
    /// 弾ごとの GameObject を作らず、毎フレームの更新は置き場の配列から頂点を書くだけで割り当てをしない（003 の知見）。
    /// 頂点の座標は Stage のローカル座標（1 単位 = 16 px）。置き場の順に描くので、後から出た弾が上に重なる。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BulletLayer : MonoBehaviour
    {
        /// <summary>アトラスの列（色）と行（形）の数。</summary>
        public const int Columns = BulletShapes.ColorCount;

        public const int Rows = BulletShapes.Count;

        /// <summary>アトラスの 1 区画のピクセル数（弾の絵の外枠）。</summary>
        public const int CellPixels = 24;

        /// <summary>四角形の半分の大きさ（Stage のローカル座標）。</summary>
        public const float HalfSize = CellPixels / 2f / LanternVeilStage.PixelsPerUnit;

        private Mesh _mesh;
        private Vector3[] _vertices;
        private Vector2[] _uv;
        private int _written;

        /// <summary>描ける弾の数（Mesh の四角形の数）。</summary>
        public int Capacity { get; private set; }

        /// <summary>今描いている弾の数。</summary>
        public int Visible { get; private set; }

        /// <summary>アトラスの区画の左下と右上の UV。</summary>
        public static (Vector2 Min, Vector2 Max) UvOf(BulletShape shape, BulletColor color)
        {
            var column = (int)color;
            var row = (int)shape;
            return (new Vector2((float)column / Columns, (float)row / Rows), new Vector2((float)(column + 1) / Columns, (float)(row + 1) / Rows));
        }

        /// <summary>置き場の容量ぶんの四角形を用意する（容量が変わったときだけ作り直す）。</summary>
        public void Allocate(int capacity)
        {
            if (Capacity == capacity && _mesh != null) { return; }
            Release();
            Capacity = capacity;
            _vertices = new Vector3[capacity * 4];
            _uv = new Vector2[capacity * 4];
            var triangles = new int[capacity * 6];
            for (var i = 0; i < capacity; i++)
            {
                var v = i * 4;
                var t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }

            _mesh = new Mesh { name = "Bullets", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.uv = _uv;
            _mesh.triangles = triangles;

            // 領域の外の余白まで覆う大きさにして、カメラの判定で消えないようにする。
            _mesh.bounds = new Bounds(new Vector3(Field.WidthPx / 2f / LanternVeilStage.PixelsPerUnit, Field.HeightPx / 2f / LanternVeilStage.PixelsPerUnit, 0), new Vector3(100, 100, 1));
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _written = 0;
            Visible = 0;
        }

        /// <summary>置き場の弾を描く。<paramref name="pool"/> が null なら何も描かない。</summary>
        public void Render(BulletPool pool)
        {
            if (_mesh == null) { return; }
            var count = pool == null ? 0 : Mathf.Min(pool.Count, Capacity);
            for (var i = 0; i < count; i++)
            {
                var center = LanternVeilStage.Local(pool.PositionOf(i));
                var radians = pool.AngleOf(i) * (2 * Mathf.PI / Trig.FullTurn);
                var cos = Mathf.Cos(radians) * HalfSize;
                var sin = Mathf.Sin(radians) * HalfSize;

                // 右向き（角度 0）の絵を弾の向きへ回す。丸い弾も同じ式で回す（見た目は変わらない）。
                var v = i * 4;
                _vertices[v] = center + new Vector3(-cos + sin, -sin - cos, 0);
                _vertices[v + 1] = center + new Vector3(-cos - sin, -sin + cos, 0);
                _vertices[v + 2] = center + new Vector3(cos - sin, sin + cos, 0);
                _vertices[v + 3] = center + new Vector3(cos + sin, sin - cos, 0);
                var (min, max) = UvOf(pool.ShapeOf(i), pool.ColorOf(i));
                _uv[v] = min;
                _uv[v + 1] = new Vector2(min.x, max.y);
                _uv[v + 2] = max;
                _uv[v + 3] = new Vector2(max.x, min.y);
            }

            // 前のフレームより減った分は大きさ 0 の四角形にする。
            for (var i = count; i < _written; i++)
            {
                var v = i * 4;
                _vertices[v] = _vertices[v + 1] = _vertices[v + 2] = _vertices[v + 3] = Vector3.zero;
            }

            // 配列の一部だけを渡す SetVertices(配列, 開始, 長さ) は頂点の数をその長さに縮め、三角形の番号が範囲外になって何も描かれない。
            // 容量ぶんの配列をそのまま渡す（使わない四角形は大きさ 0）。
            if (count > 0 || _written > 0)
            {
                _mesh.SetVertices(_vertices);
                _mesh.SetUVs(0, _uv);
            }

            _written = count;
            Visible = count;
        }

        private void Release()
        {
            if (_mesh == null) { return; }
            Destroy(_mesh);
            _mesh = null;
        }

        private void OnDestroy() => Release();
    }
}
