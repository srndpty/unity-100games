using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 敵・宝石・弾をまとめて 1 つの Mesh で描く（027 の BulletLayer を一般化）。1 体 = 四角形 1 つで、絵は横に並んだ区画の
    /// アトラス（<see cref="Cells"/> 区画）から選び、色は頂点の色（被弾の点滅）。毎フレームの更新は配列に書くだけで割り当てをしない。
    /// 書いた順に描くので、宝石 → 敵 → 弾の順に書けば弾がいちばん上に重なる。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class QuadLayer : MonoBehaviour
    {
        /// <summary>アトラスの区画の数（横 1 列）。</summary>
        public const int Cells = 8;

        /// <summary>1 区画のピクセル数。</summary>
        public const int CellPixels = 32;

        private Mesh _mesh;
        private Vector3[] _vertices;
        private Vector2[] _uv;
        private Color32[] _colors;
        private int _count;
        private int _written;

        /// <summary>描ける四角形の数。</summary>
        public int Capacity { get; private set; }

        /// <summary>今描いている四角形の数。</summary>
        public int Visible { get; private set; }

        /// <summary>区画の左下と右上の UV。</summary>
        public static (Vector2 Min, Vector2 Max) UvOf(int cell) => (new Vector2((float)cell / Cells, 0), new Vector2((float)(cell + 1) / Cells, 1));

        /// <summary>容量ぶんの四角形を用意する（容量が変わったときだけ作り直す）。</summary>
        public void Allocate(int capacity)
        {
            if (Capacity == capacity && _mesh != null) { return; }
            Release();
            Capacity = capacity;
            _vertices = new Vector3[capacity * 4];
            _uv = new Vector2[capacity * 4];
            _colors = new Color32[capacity * 4];
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

            _mesh = new Mesh { name = "Swarm", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.uv = _uv;
            _mesh.colors32 = _colors;
            _mesh.triangles = triangles;

            // 果てのない平面を動くので、どこにいてもカメラの判定で消えない大きさにする。
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1));
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _written = 0;
            _count = 0;
            Visible = 0;
        }

        /// <summary>1 フレームの書き込みを始める。</summary>
        public void Begin() => _count = 0;

        /// <summary>中心 <paramref name="center"/>（Stage のローカル座標）・半分の大きさ <paramref name="half"/> の四角形を足す。容量を超えた分は描かない。</summary>
        public void Add(Vector3 center, int cell, float half, Color32 color)
        {
            if (_mesh == null || _count >= Capacity) { return; }
            var v = _count * 4;
            _vertices[v] = center + new Vector3(-half, -half, 0);
            _vertices[v + 1] = center + new Vector3(-half, half, 0);
            _vertices[v + 2] = center + new Vector3(half, half, 0);
            _vertices[v + 3] = center + new Vector3(half, -half, 0);
            var (min, max) = UvOf(cell);
            _uv[v] = min;
            _uv[v + 1] = new Vector2(min.x, max.y);
            _uv[v + 2] = max;
            _uv[v + 3] = new Vector2(max.x, min.y);
            _colors[v] = _colors[v + 1] = _colors[v + 2] = _colors[v + 3] = color;
            _count++;
        }

        /// <summary>書き込みを終えて Mesh に渡す。前のフレームより減った分は大きさ 0 の四角形にする。</summary>
        public void End()
        {
            if (_mesh == null) { return; }
            for (var i = _count; i < _written; i++)
            {
                var v = i * 4;
                _vertices[v] = _vertices[v + 1] = _vertices[v + 2] = _vertices[v + 3] = Vector3.zero;
            }

            // SetVertices(配列, 開始, 長さ) は頂点の数をその長さに縮めて何も描かれなくなるので、配列をそのまま渡す（027 の知見）。
            if (_count > 0 || _written > 0)
            {
                _mesh.SetVertices(_vertices);
                _mesh.SetUVs(0, _uv);
                _mesh.SetColors(_colors);
            }

            _written = _count;
            Visible = _count;
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
