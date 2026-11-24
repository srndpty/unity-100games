using System;
using Game.Core;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 盤の描画。部品の置き場（支点・壁・トゲ・目標・露玉）は Stage Prefab（<c>DewdropSnipStage.prefab</c>）にそろえておき、
    /// 問ごとの配置は Core の問題データから並べる。ロープは全ての節を 1 つの Mesh の四角形で描く。
    /// 盤の座標と Stage の座標は <see cref="ToLocal"/> / <see cref="ToBoard"/> で変換する（1 マス = 1 単位、盤の中心が原点）。
    /// 補間せず、その Tick の状態をそのまま描く。
    /// </summary>
    public sealed class DewdropSnipStage : MonoBehaviour
    {
        public const int MaxRopes = 4;
        public const int MaxWalls = 6;
        public const int MaxSpikes = 10;

        /// <summary>1 本のロープで描ける節の数の上限（設定の <see cref="RopeConfig.MaxLinks"/> はこれ以下に限る）。</summary>
        public const int MaxLinks = 32;

        /// <summary>なぞりの軌跡に残す点の数。</summary>
        public const int TrailCapacity = 24;

        /// <summary>ロープの太さ（Stage の単位）。</summary>
        public const float RopeWidth = .07f;

        private const int Segments = MaxRopes * MaxLinks;

        private static readonly Color32 _ropeColor = new(122, 178, 92, 255);
        private static readonly Color32 _fragmentColor = new(122, 178, 92, 120);

        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private SpriteRenderer _body;
        [SerializeField] private SpriteRenderer _goal;
        [SerializeField] private SpriteRenderer _snip;
        [SerializeField] private SpriteRenderer[] _anchors = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] _walls = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] _spikes = Array.Empty<SpriteRenderer>();
        [SerializeField] private MeshFilter _ropes;
        [SerializeField] private LineRenderer _trail;

        private readonly Vector3[] _vertices = new Vector3[Segments * 4];
        private readonly Color32[] _colors = new Color32[Segments * 4];
        private readonly Vector3[] _trailPoints = new Vector3[TrailCapacity];
        private Mesh _mesh;
        private Puzzle _shown;
        private float _bodyDiameter = 1;

        /// <summary>ロープの Mesh（テストが頂点を読む）。</summary>
        public Mesh RopeMesh => _mesh;

        public SpriteRenderer Body => _body;

        public SpriteRenderer Goal => _goal;

        public SpriteRenderer Snip => _snip;

        public LineRenderer Trail => _trail;

        public SpriteRenderer Anchor(int index) => _anchors[index];

        public SpriteRenderer Wall(int index) => _walls[index];

        public SpriteRenderer Spike(int index) => _spikes[index];

        /// <summary>ロープ <paramref name="rope"/> の節 <paramref name="link"/> の四角形の番号（頂点はこの 4 倍から 4 つ）。</summary>
        public static int SegmentIndex(int rope, int link) => (rope * MaxLinks) + link;

        /// <summary>盤の座標 → Stage の座標。</summary>
        public static Vector3 ToLocal(Vec2i point) =>
            new(((float)point.X / Board.Unit) - (Board.Columns / 2f), ((float)point.Y / Board.Unit) - (Board.Rows / 2f), 0);

        /// <summary>Stage の座標 → 盤の座標（四捨五入）。</summary>
        public static Vec2i ToBoard(Vector3 local) => new(
            Mathf.RoundToInt((float)(((double)local.x + (Board.Columns / 2.0)) * Board.Unit)),
            Mathf.RoundToInt((float)(((double)local.y + (Board.Rows / 2.0)) * Board.Unit)));

        private void Awake() => EnsureMesh();

        /// <summary>露玉の大きさを設定の半径に合わせる（円の絵は直径 1 単位で描いてある）。</summary>
        public void Configure(RopeConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _bodyDiameter = 2f * config.BodyRadius / Board.Unit;
        }

        private void EnsureMesh()
        {
            if (_mesh != null) { return; }
            _mesh = new Mesh { name = "Ropes" };
            _mesh.MarkDynamic();
            var triangles = new int[Segments * 6];
            for (var i = 0; i < Segments; i++)
            {
                var v = i * 4;
                var t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            _mesh.vertices = _vertices;
            _mesh.colors32 = _colors;
            _mesh.triangles = triangles;
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(Board.Columns * 4, Board.Rows * 4, 1));
            _ropes.sharedMesh = _mesh;

            // Sprites/Default は SpriteRenderer が渡す色（_RendererColor）と反転（_Flip）を掛ける。MeshRenderer は渡さないので、
            // 渡さないと直前の描画の値が残って消えたり暗くなったりする（030 の知見）。
            var block = new MaterialPropertyBlock();
            block.SetColor("_RendererColor", Color.white);
            block.SetVector("_Flip", Vector4.one);
            _ropes.GetComponent<MeshRenderer>().SetPropertyBlock(block);
        }

        /// <summary>試合の状態を描く。<paramref name="effectTicks"/> は切った瞬間の演出の長さ。</summary>
        public void Render(PuzzleRun run, int effectTicks)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            EnsureMesh();
            Layout(run.Puzzle);
            _body.enabled = true;
            _body.transform.localPosition = ToLocal(run.Body);
            _body.transform.localScale = new Vector3(_bodyDiameter, _bodyDiameter, 1);
            for (var r = 0; r < MaxRopes; r++)
            {
                for (var j = 0; j < MaxLinks; j++)
                {
                    var visible = r < run.RopeCount && j < run.LinksOf(r) && run.IsLinkActive(r, j);
                    if (!visible)
                    {
                        Hide(SegmentIndex(r, j));
                        continue;
                    }

                    var fragment = run.IsCut(r) && j > run.CutLinkOf(r);
                    Quad(SegmentIndex(r, j), ToLocal(run.RopePoint(r, j)), ToLocal(run.RopePoint(r, j + 1)), fragment ? _fragmentColor : _ropeColor);
                }
            }

            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);

            var since = run.LastCutTick < 0 ? long.MaxValue : run.Ticks - run.LastCutTick;
            _snip.enabled = since < effectTicks && run.Status == RunStatus.Playing;
            if (_snip.enabled)
            {
                var t = (float)since / effectTicks;
                _snip.transform.localPosition = ToLocal(run.LastCutPoint);
                _snip.transform.localScale = Vector3.one * (.6f + t);
                _snip.color = new Color(1, 1, 1, 1 - t);
            }

            _goal.color = run.Status == RunStatus.Succeeded ? new Color(1, .95f, .6f, 1) : Color.white;
            _body.color = run.Reason == FailReason.Spike ? new Color(1, .45f, .45f, 1) : Color.white;
        }

        /// <summary>なぞりの軌跡（Stage の座標、古い順）を描く。<paramref name="count"/> が 2 未満なら隠す。</summary>
        public void RenderTrail(Vector3[] points, int count)
        {
            if (points == null) { throw new ArgumentNullException(nameof(points)); }
            var shown = Math.Min(count, TrailCapacity);
            _trail.enabled = shown >= 2;
            if (!_trail.enabled) { return; }
            Array.Copy(points, count - shown, _trailPoints, 0, shown);
            _trail.positionCount = shown;
            _trail.SetPositions(_trailPoints);
        }

        /// <summary>Stage Prefab が描画に必要な部品と絵をそろえているか。そろっていれば null、欠けていれば理由。</summary>
        public string Mismatch()
        {
            if (_background == null || _background.sprite == null) { return "背景の絵がありません。"; }
            if (_body == null || _body.sprite == null) { return "露玉の絵がありません。"; }
            if (_goal == null || _goal.sprite == null) { return "目標の絵がありません。"; }
            if (_snip == null || _snip.sprite == null) { return "切った瞬間の絵がありません。"; }
            if (_ropes == null || _ropes.GetComponent<MeshRenderer>() == null || _ropes.GetComponent<MeshRenderer>().sharedMaterial == null) { return "ロープの Mesh の描画がありません。"; }
            if (_trail == null || _trail.sharedMaterial == null) { return "なぞりの軌跡がありません。"; }
            var parts = Parts("支点", _anchors, MaxRopes) ?? Parts("壁", _walls, MaxWalls) ?? Parts("トゲ", _spikes, MaxSpikes);
            if (parts != null) { return parts; }
            foreach (var wall in _walls)
            {
                if (wall.drawMode != SpriteDrawMode.Tiled) { return "壁はタイル表示にしてください。"; }
            }

            return null;
        }

        /// <summary>問題集の全ての問を描けるか（ロープ・壁・トゲの数と節の数）。描けなければ理由。</summary>
        public static string Capacity(PuzzleBook book, RopeConfig config)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (config.MaxLinks > MaxLinks) { return $"節の数の上限は描画できる {MaxLinks} までにしてください。"; }
            foreach (var puzzle in book.Puzzles)
            {
                if (puzzle.Ropes.Count > MaxRopes) { return $"問「{puzzle.Name}」のロープが多すぎます。"; }
                if (puzzle.Walls.Count > MaxWalls) { return $"問「{puzzle.Name}」の壁が多すぎます。"; }
                if (puzzle.Spikes.Count > MaxSpikes) { return $"問「{puzzle.Name}」のトゲが多すぎます。"; }
            }

            return null;
        }

        private static string Parts(string name, SpriteRenderer[] parts, int count)
        {
            if (parts.Length != count) { return $"{name}の置き場が {count} 個ではありません（{parts.Length} 個）。"; }
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null || parts[i].sprite == null) { return $"{name}の置き場 {i} がありません。"; }
            }

            return null;
        }

        /// <summary>問の固定の部品（支点・壁・トゲ・目標）を並べる。問が変わったときだけ。</summary>
        private void Layout(Puzzle puzzle)
        {
            if (ReferenceEquals(_shown, puzzle)) { return; }
            _shown = puzzle;
            for (var i = 0; i < _anchors.Length; i++)
            {
                _anchors[i].enabled = i < puzzle.Ropes.Count;
                if (_anchors[i].enabled) { _anchors[i].transform.localPosition = ToLocal(puzzle.Ropes[i].Anchor); }
            }

            for (var i = 0; i < _walls.Length; i++)
            {
                _walls[i].enabled = i < puzzle.Walls.Count;
                if (!_walls[i].enabled) { continue; }
                var wall = puzzle.Walls[i];
                var min = ToLocal(new Vec2i(wall.MinX, wall.MinY));
                var max = ToLocal(new Vec2i(wall.MaxX, wall.MaxY));
                _walls[i].transform.localPosition = (min + max) / 2;
                _walls[i].size = new Vector2(max.x - min.x, max.y - min.y);
            }

            for (var i = 0; i < _spikes.Length; i++)
            {
                _spikes[i].enabled = i < puzzle.Spikes.Count;
                if (_spikes[i].enabled) { PlaceCircle(_spikes[i], puzzle.Spikes[i]); }
            }

            PlaceCircle(_goal, puzzle.Goal);
        }

        /// <summary>円の絵（幅 1 単位で描いてある）を、半径に合わせて置く。</summary>
        private static void PlaceCircle(SpriteRenderer renderer, Circle circle)
        {
            renderer.enabled = true;
            renderer.transform.localPosition = ToLocal(circle.Center);
            var diameter = 2f * circle.Radius / Board.Unit;
            renderer.transform.localScale = new Vector3(diameter, diameter, 1);
        }

        private void Quad(int index, Vector3 a, Vector3 b, Color32 color)
        {
            var direction = b - a;
            var side = direction.sqrMagnitude > 0 ? new Vector3(-direction.y, direction.x, 0).normalized * (RopeWidth / 2) : new Vector3(RopeWidth / 2, 0, 0);
            var v = index * 4;
            _vertices[v] = a - side;
            _vertices[v + 1] = a + side;
            _vertices[v + 2] = b - side;
            _vertices[v + 3] = b + side;
            for (var i = 0; i < 4; i++) { _colors[v + i] = color; }
        }

        private void Hide(int index)
        {
            var v = index * 4;
            for (var i = 0; i < 4; i++)
            {
                _vertices[v + i] = Vector3.zero;
                _colors[v + i] = default;
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null) { Destroy(_mesh); }
        }
    }
}
