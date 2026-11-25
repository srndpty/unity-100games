using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>ステージの Prefab。位置は Core の整数座標を 256 で割ったもの。補間しない。</summary>
    public sealed class EmberStage : MonoBehaviour
    {
        public const int PoseCount = 8;
        public const int MaxBoxes = 24;

        [SerializeField] private SpriteRenderer _left;
        [SerializeField] private SpriteRenderer _right;
        [SerializeField] private SpriteRenderer _shot0;
        [SerializeField] private SpriteRenderer _shot1;
        [SerializeField] private SpriteRenderer _ground;
        [SerializeField] private SpriteRenderer _backdrop;
        [SerializeField] private Sprite[] _cinder = new Sprite[PoseCount];
        [SerializeField] private Sprite[] _kiln = new Sprite[PoseCount];
        [SerializeField] private MeshFilter _boxes;
        [SerializeField] private MeshRenderer _boxRenderer;

        private readonly List<WorldBox> _boxList = new(MaxBoxes);
        private readonly Vector3[] _vertices = new Vector3[MaxBoxes * 8];
        private readonly Color32[] _colors = new Color32[MaxBoxes * 8];
        private Mesh _mesh;
        private MaterialPropertyBlock _block;
        private Stamp _leftStamp;
        private Stamp _rightStamp;
        private ShotStamp _shotStamp0;
        private ShotStamp _shotStamp1;

        public SpriteRenderer Left => _left;

        public SpriteRenderer Right => _right;

        public Mesh BoxMesh => _mesh;

        public void ShowTitle(DuelConfig config)
        {
            _leftStamp = default;
            _rightStamp = default;
            _shotStamp0 = default;
            _shotStamp1 = default;
            _left.sprite = _cinder[0];
            _left.flipX = false;
            _left.transform.localPosition = new Vector3(config.StartLeft / 256f, 0f, 0f);
            _left.enabled = true;
            _right.sprite = _kiln[0];
            _right.flipX = true;
            _right.transform.localPosition = new Vector3(config.StartRight / 256f, 0f, 0f);
            _right.enabled = true;
            _shot0.enabled = false;
            _shot1.enabled = false;
            ShowBoxes(false);
        }

        public void Show(Duel duel, bool boxes)
        {
            PlaceFighter(_left, duel.At(0), ref _leftStamp);
            PlaceFighter(_right, duel.At(1), ref _rightStamp);
            PlaceShot(_shot0, duel, 0, ref _shotStamp0);
            PlaceShot(_shot1, duel, 1, ref _shotStamp1);
            if (boxes) { DrawBoxes(duel); }
            else { ShowBoxes(false); }
        }

        /// <summary>Prefab の初期配置が Core の開始位置と一致しないときの説明。一致すれば null。</summary>
        public string Mismatch(DuelConfig config)
        {
            if (_left == null || _right == null || _ground == null || _backdrop == null || _shot0 == null || _shot1 == null)
            {
                return "描画物の参照が欠けています。";
            }

            if (_cinder.Length != PoseCount || _kiln.Length != PoseCount) { return "姿はキャラごとに 8 枚必要です。"; }
            for (var i = 0; i < PoseCount; i++)
            {
                if (_cinder[i] == null || _kiln[i] == null) { return "姿のスプライトが欠けています。"; }
            }

            if (Away(_left.transform.localPosition, config.StartLeft, 0)) { return "左の初期位置が開始位置と一致しません。"; }
            if (Away(_right.transform.localPosition, config.StartRight, 0)) { return "右の初期位置が開始位置と一致しません。"; }
            if (_left.flipX || !_right.flipX) { return "初期の向きは向かい合わせです。"; }
            if (Mathf.Abs(_ground.transform.localPosition.y) > 0.01f) { return "地面の上端は y = 0 です。"; }
            if (Mathf.Abs(_backdrop.transform.localPosition.x - (config.WallMax / 512f)) > 0.01f) { return "背景の中央が舞台の中央と一致しません。"; }
            return null;
        }

        private void Awake()
        {
            _mesh = new Mesh { name = "EmberBoxes" };
            _mesh.MarkDynamic();
            var indices = new int[_vertices.Length];
            for (var i = 0; i < indices.Length; i++) { indices[i] = i; }
            _mesh.vertices = _vertices;
            _mesh.colors32 = _colors;
            _mesh.SetIndices(indices, MeshTopology.Lines, 0);
            _boxes.sharedMesh = _mesh;
            _block = new MaterialPropertyBlock();
            _block.SetColor("_RendererColor", Color.white);
            _block.SetVector("_Flip", new Vector4(1f, 1f, 0f, 0f));
            _boxRenderer.SetPropertyBlock(_block);
            ShowBoxes(false);
        }

        private void OnDestroy()
        {
            if (_mesh != null) { Destroy(_mesh); }
        }

        private void PlaceFighter(SpriteRenderer renderer, Fighter fighter, ref Stamp stamp)
        {
            var pose = PoseOf(fighter);
            if (stamp.Ready && stamp.Pose == pose && stamp.X == fighter.X && stamp.Y == fighter.Y && stamp.Facing == fighter.Facing)
            {
                return;
            }

            stamp.Ready = true;
            stamp.Pose = pose;
            stamp.X = fighter.X;
            stamp.Y = fighter.Y;
            stamp.Facing = fighter.Facing;
            var poses = fighter.Character.Id == "kiln" ? _kiln : _cinder;
            renderer.sprite = poses[pose];
            renderer.flipX = fighter.Facing < 0;
            renderer.transform.localPosition = new Vector3(fighter.X / 256f, fighter.Y / 256f, 0f);
            renderer.enabled = true;
        }

        private static void PlaceShot(SpriteRenderer renderer, Duel duel, int owner, ref ShotStamp stamp)
        {
            for (var i = 0; i < duel.ShotCount; i++)
            {
                var shot = duel.ShotAt(i);
                if (shot.Owner != owner) { continue; }
                if (stamp.Shown && stamp.X == shot.X && stamp.Y == shot.Y && stamp.Facing == shot.Facing) { return; }
                stamp.Shown = true;
                stamp.X = shot.X;
                stamp.Y = shot.Y;
                stamp.Facing = shot.Facing;
                var hit = shot.Hit;
                renderer.enabled = true;
                renderer.flipX = shot.Facing < 0;
                renderer.transform.localPosition = new Vector3((hit.MinX + hit.MaxX) / 512f, (hit.MinY + hit.MaxY) / 512f, 0f);
                return;
            }

            if (!stamp.Shown) { return; }
            stamp.Shown = false;
            renderer.enabled = false;
        }

        private void DrawBoxes(Duel duel)
        {
            var count = 0;
            count = AddBoxes(duel, 0, count);
            count = AddBoxes(duel, 1, count);
            for (var i = 0; i < duel.ShotCount && count < MaxBoxes; i++)
            {
                var shot = duel.ShotAt(i);
                Write(count, BoxKind.Hit, shot.Hit);
                count++;
            }

            for (var i = count; i < MaxBoxes; i++) { Write(i, BoxKind.Push, new IntGeometry.Rect(0, 0, 0, 0)); }
            _mesh.vertices = _vertices;
            _mesh.colors32 = _colors;
            ShowBoxes(true);
        }

        private int AddBoxes(Duel duel, int player, int count)
        {
            duel.CopyBoxes(player, _boxList);
            for (var i = 0; i < _boxList.Count && count < MaxBoxes; i++)
            {
                Write(count, _boxList[i].Kind, _boxList[i].Rect);
                count++;
            }

            return count;
        }

        private void Write(int index, BoxKind kind, IntGeometry.Rect rect)
        {
            var min = new Vector3(rect.MinX / 256f, rect.MinY / 256f, 0f);
            var max = new Vector3(rect.MaxX / 256f, rect.MaxY / 256f, 0f);
            var color = ColorOf(kind);
            var o = index * 8;
            _vertices[o] = min;
            _vertices[o + 1] = new Vector3(max.x, min.y, 0f);
            _vertices[o + 2] = _vertices[o + 1];
            _vertices[o + 3] = max;
            _vertices[o + 4] = max;
            _vertices[o + 5] = new Vector3(min.x, max.y, 0f);
            _vertices[o + 6] = _vertices[o + 5];
            _vertices[o + 7] = min;
            for (var i = 0; i < 8; i++) { _colors[o + i] = color; }
        }

        private void ShowBoxes(bool enabled)
        {
            if (_boxRenderer != null && _boxRenderer.enabled != enabled) { _boxRenderer.enabled = enabled; }
        }

        private static bool Away(Vector3 position, int x, int y) =>
            Mathf.Abs(position.x - (x / 256f)) > 0.01f || Mathf.Abs(position.y - (y / 256f)) > 0.01f;

        private static int PoseOf(Fighter fighter)
        {
            switch (fighter.State)
            {
                case FighterState.Walk:
                    return 1;
                case FighterState.Crouch:
                    return 2;
                case FighterState.JumpPre:
                case FighterState.Air:
                    return 3;
                case FighterState.Attack:
                    return fighter.Move != null && fighter.Move.Id == "throw" ? 5 : 4;
                case FighterState.HitStun:
                case FighterState.Thrown:
                    return 5;
                case FighterState.BlockStun:
                    return 7;
                case FighterState.Knockdown:
                case FighterState.Wakeup:
                case FighterState.Ko:
                    return 6;
                default:
                    return 0;
            }
        }

        private struct Stamp
        {
            public bool Ready;
            public int Pose;
            public int X;
            public int Y;
            public int Facing;
        }

        private struct ShotStamp
        {
            public bool Shown;
            public int X;
            public int Y;
            public int Facing;
        }

        private static Color32 ColorOf(BoxKind kind)
        {
            switch (kind)
            {
                case BoxKind.Hit:
                    return new Color32(230, 50, 40, 255);
                case BoxKind.Hurt:
                    return new Color32(70, 210, 90, 255);
                case BoxKind.Throw:
                    return new Color32(240, 200, 40, 255);
                case BoxKind.Throwable:
                    return new Color32(40, 190, 220, 255);
                default:
                    return new Color32(180, 200, 255, 255);
            }
        }
    }
}
