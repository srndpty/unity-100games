using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 舞台の Prefab。1 ワールド単位 = 1 px（Core の 256 単位）。位置は <see cref="Depth.ScreenY"/>、
    /// 重なり順は <see cref="Depth.DrawOrder"/> をそのまま写す（奥の登場者ほど先に描く）。補間しない。
    /// </summary>
    public sealed class SootStage : MonoBehaviour
    {
        public const int Stand = 0;
        public const int Walk = 1;
        public const int Windup = 2;
        public const int Strike = 3;
        public const int Hurt = 4;
        public const int Air = 5;
        public const int Kick = 6;
        public const int Hold = 7;
        public const int Down = 8;
        public const int PoseCount = 9;

        /// <summary>描ける登場者の数（プレイヤー ＋ 敵の置き場）。設定の Capacity はこれ未満にする。</summary>
        public const int Pool = 9;

        public const int BodyOrder = 100;
        public const int ShadowOrder = 50;
        public const int EffectOrder = 300;
        public const int BoxOrder = 400;
        public const int MaxBoxes = Pool * 4;

        public static readonly string[] PoseNames = { "Stand", "Walk", "Windup", "Strike", "Hurt", "Air", "Kick", "Hold", "Down" };

        [SerializeField] private SpriteRenderer _backdrop;
        [SerializeField] private SpriteRenderer _ground;
        [SerializeField] private SpriteRenderer[] _bodies = new SpriteRenderer[Pool];
        [SerializeField] private SpriteRenderer[] _shadows = new SpriteRenderer[Pool];
        [SerializeField] private SpriteRenderer[] _lamps = Array.Empty<SpriteRenderer>();
        [SerializeField] private Sprite[] _player = new Sprite[PoseCount];
        [SerializeField] private Sprite[] _grunt = new Sprite[PoseCount];
        [SerializeField] private Sprite[] _brute = new Sprite[PoseCount];
        [SerializeField] private Sprite[] _kicker = new Sprite[PoseCount];
        [SerializeField] private Sprite[] _boss = new Sprite[PoseCount];
        [SerializeField] private Sprite _flare;
        [SerializeField] private Sprite _whirl;
        [SerializeField] private MeshFilter _boxes;
        [SerializeField] private MeshRenderer _boxRenderer;

        private readonly int[] _order = new int[Pool];
        private readonly SpriteRenderer[] _flares = new SpriteRenderer[Pool];
        private readonly Vector3[] _vertices = new Vector3[MaxBoxes * 8];
        private readonly Color32[] _colors = new Color32[MaxBoxes * 8];
        private SpriteRenderer _special;
        private Mesh _mesh;
        private MaterialPropertyBlock _block;

        public SpriteRenderer Body(int slot) => _bodies[slot];

        public SpriteRenderer ShadowOf(int slot) => _shadows[slot];

        public SpriteRenderer Flare(int slot) => _flares[slot];

        public SpriteRenderer SpecialEffect => _special;

        public Mesh BoxMesh => _mesh;

        public int LampCount => _lamps.Length;

        public SpriteRenderer Lamp(int index) => _lamps[index];

        /// <summary>タイトルの絵：プレイヤーと 3 種の敵。生成スクリプトと照合が同じ値を使う。</summary>
        public static (ActorKind Kind, int X, int Z, int Facing)[] TitleLayout(BrawlConfig config, StageData stage)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (stage == null) { throw new ArgumentNullException(nameof(stage)); }
            int x = stage.Sections[0].CameraX + stage.StartX;
            int mid = (config.ZMin + config.ZMax) / 2;
            int depth = config.ZMax - config.ZMin;
            return new[]
            {
                (ActorKind.Player, x, mid, 1),
                (ActorKind.Grunt, x + (110 * Px.One), mid + (depth / 4), -1),
                (ActorKind.Brute, x + (150 * Px.One), mid - (depth / 8), -1),
                (ActorKind.Kicker, x + (196 * Px.One), mid + (depth / 3), -1),
            };
        }

        /// <summary>区画の右端（画面が止まったときの右の端）に立つ街灯の位置。</summary>
        public static int LampX(Section section, BrawlConfig config)
        {
            if (section == null) { throw new ArgumentNullException(nameof(section)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            return section.CameraX + config.ViewWidth - (8 * Px.One);
        }

        public static Vector3 WorldOf(int x, int y, int z, BrawlConfig config) =>
            new(x / (float)Px.One, Depth.ScreenY(y, z, config) / (float)Px.One, 0f);

        public void ShowTitle(BrawlConfig config, StageData stage)
        {
            var layout = TitleLayout(config, stage);
            for (var slot = 0; slot < Pool; slot++)
            {
                var shown = slot < layout.Length;
                _bodies[slot].enabled = shown;
                _shadows[slot].enabled = shown;
                if (_flares[slot] != null) { _flares[slot].enabled = false; }
                if (!shown) { continue; }
                var (kind, x, z, facing) = layout[slot];
                PlaceBody(slot, kind, Stand, x, 0, z, facing, 1f, config);
            }

            for (var slot = 0; slot < layout.Length; slot++)
            {
                var rank = 0;
                for (var other = 0; other < layout.Length; other++)
                {
                    if (layout[other].Z > layout[slot].Z || (layout[other].Z == layout[slot].Z && other < slot)) { rank++; }
                }

                _bodies[slot].sortingOrder = BodyOrder + rank;
            }

            if (_special != null) { _special.enabled = false; }
            ShowBoxes(false);
        }

        /// <summary>その Tick の状態を描く。<paramref name="config"/> は試合の設定（毎フレーム複製しないよう呼ぶ側が持つ）。</summary>
        public void Show(Brawl brawl, BrawlConfig config, bool boxes)
        {
            if (brawl == null) { throw new ArgumentNullException(nameof(brawl)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            int count = Depth.DrawOrder(brawl, _order);
            for (var slot = 0; slot < Pool; slot++)
            {
                _bodies[slot].enabled = false;
                _shadows[slot].enabled = false;
                _flares[slot].enabled = false;
            }

            for (var rank = 0; rank < count; rank++)
            {
                var slot = _order[rank];
                var a = brawl.At(slot);
                var alpha = 1f;
                if (slot == 0 && a.Invuln > 0 && a.State != ActorState.Attack && ((brawl.Tick >> 2) & 1) == 0) { alpha = 0.45f; }
                if (a.State == ActorState.Dead && a.Timer > 0 && a.Timer < 30 && ((a.Timer >> 2) & 1) == 0) { alpha = 0f; }
                PlaceBody(slot, a.Kind, PoseOf(a), a.X, a.Y, a.Z, a.Facing, alpha, config);
                _bodies[slot].sortingOrder = BodyOrder + rank;
                PlaceFlare(slot, a, config);
            }

            PlaceSpecial(brawl.Player, config);
            if (boxes) { DrawBoxes(brawl, config); }
            else { ShowBoxes(false); }
        }

        /// <summary>Prefab が Core のデータと食い違うときの説明。一致すれば null。</summary>
        public string Mismatch(BrawlConfig config, StageData stage)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (stage == null) { throw new ArgumentNullException(nameof(stage)); }
            if (_backdrop == null || _ground == null || _flare == null || _whirl == null || _boxes == null || _boxRenderer == null)
            {
                return "描画物の参照が欠けています。";
            }

            if (_bodies.Length != Pool || _shadows.Length != Pool) { return $"登場者と影は {Pool} 組必要です。"; }
            for (var i = 0; i < Pool; i++)
            {
                if (_bodies[i] == null || _shadows[i] == null) { return "登場者か影の参照が欠けています。"; }
                if (_bodies[i].transform.parent != transform || _shadows[i].transform.parent != transform) { return "登場者と影は Stage の直下に置きます。"; }
            }

            foreach (var poses in new[] { _player, _grunt, _brute, _kicker, _boss })
            {
                if (poses.Length != PoseCount) { return $"姿は種類ごとに {PoseCount} 枚必要です。"; }
                foreach (var sprite in poses)
                {
                    if (sprite == null) { return "姿のスプライトが欠けています。"; }
                }
            }

            if (_lamps.Length != stage.Sections.Count) { return $"街灯は区画の数（{stage.Sections.Count}）だけ必要です。"; }
            for (var i = 0; i < _lamps.Length; i++)
            {
                if (_lamps[i] == null) { return "街灯の参照が欠けています。"; }
                var expected = WorldOf(LampX(stage.Sections[i], config), 0, config.ZMax, config);
                if (Vector3.Distance(_lamps[i].transform.localPosition, expected) > 0.01f) { return $"街灯 {i + 1} が区画 {i + 1} の右端と一致しません。"; }
            }

            var layout = TitleLayout(config, stage);
            for (var i = 0; i < layout.Length; i++)
            {
                var (kind, x, z, facing) = layout[i];
                var expected = WorldOf(x, 0, z, config);
                if (Vector3.Distance(_bodies[i].transform.localPosition, expected) > 0.01f) { return $"タイトルの {kind} の位置が一致しません。"; }
                if (_bodies[i].sprite != PosesOf(kind)[Stand]) { return $"タイトルの {kind} の姿が一致しません。"; }
                if (_bodies[i].flipX != (facing < 0)) { return $"タイトルの {kind} の向きが一致しません。"; }
            }

            if (Vector3.Distance(_ground.transform.localPosition, new Vector3(0f, -24f, 0f)) > 0.01f) { return "地面の位置が一致しません。"; }
            if (Vector3.Distance(_backdrop.transform.localPosition, WorldOf(0, 0, config.ZMax, config)) > 0.01f) { return "背景の下端が奥行きの帯の奥と一致しません。"; }
            return null;
        }

        private void Awake()
        {
            for (var slot = 0; slot < Pool; slot++)
            {
                var item = new GameObject("Flare " + slot);
                item.transform.SetParent(transform, false);
                var renderer = item.AddComponent<SpriteRenderer>();
                renderer.sharedMaterial = _bodies[0].sharedMaterial;
                renderer.sprite = _flare;
                renderer.sortingOrder = EffectOrder;
                renderer.enabled = false;
                _flares[slot] = renderer;
            }

            var whirl = new GameObject("Whirl");
            whirl.transform.SetParent(transform, false);
            _special = whirl.AddComponent<SpriteRenderer>();
            _special.sharedMaterial = _bodies[0].sharedMaterial;
            _special.sprite = _whirl;
            _special.sortingOrder = EffectOrder - 1;
            _special.enabled = false;

            _mesh = new Mesh { name = "SootBoxes" };
            _mesh.MarkDynamic();
            var indices = new int[_vertices.Length];
            for (var i = 0; i < indices.Length; i++) { indices[i] = i; }
            _mesh.vertices = _vertices;
            _mesh.colors32 = _colors;
            _mesh.SetIndices(indices, MeshTopology.Lines, 0);
            // 頂点を書き換えても bounds は作ったときの原点のまま残り、画面が右へ進むとカリングされて描かれない。
            // 面の全体を覆う bounds を 1 回だけ置く（毎フレームの計算をしない）。
            _mesh.bounds = new Bounds(new Vector3(4096f, 0f, 0f), new Vector3(16384f, 4096f, 16f));
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

        private void PlaceBody(int slot, ActorKind kind, int pose, int x, int y, int z, int facing, float alpha, BrawlConfig config)
        {
            var body = _bodies[slot];
            var shadow = _shadows[slot];
            body.sprite = PosesOf(kind)[pose];
            body.flipX = facing < 0;
            body.color = new Color(1f, 1f, 1f, alpha);
            body.transform.localPosition = WorldOf(x, y, z, config);
            shadow.transform.localPosition = WorldOf(x, 0, z, config);

            shadow.transform.localScale = kind == ActorKind.Brute || kind == ActorKind.Boss ? new Vector3(1.3f, 1.2f, 1f) : Vector3.one;
            body.enabled = alpha > 0f;
            shadow.enabled = true;
        }

        private void PlaceFlare(int slot, Actor a, BrawlConfig config)
        {
            var flare = _flares[slot];
            var struck = a.HitStop > 0 && (a.State == ActorState.HitStun || a.State == ActorState.Knockdown || a.State == ActorState.Grabbed || (a.State != ActorState.Attack && a.State != ActorState.Jump && a.State != ActorState.Grab));
            if (!struck) { return; }
            var height = a.Def == null ? 40 : a.Def.Body.H * 2 / 3;
            flare.transform.localPosition = WorldOf(a.X - (a.Facing * 6 * Px.One), a.Y + height, a.Z, config);
            flare.transform.localRotation = Quaternion.Euler(0f, 0f, (a.Serial * 37) % 90);
            flare.color = a.Slot == 0 ? new Color(1f, 0.55f, 0.45f, 0.95f) : new Color(1f, 0.95f, 0.6f, 0.95f);
            flare.enabled = true;
        }

        private void PlaceSpecial(Actor player, BrawlConfig config)
        {
            var move = player.Move;
            var on = player.State == ActorState.Attack && move != null && move.Kind == MoveKind.Special && player.MoveTick < move.Startup + move.Active;
            _special.enabled = on;
            if (!on) { return; }
            _special.transform.localPosition = WorldOf(player.X, player.Y + (24 * Px.One), player.Z, config);
            _special.flipX = (player.MoveTick & 2) != 0;
            _special.color = new Color(1f, 1f, 1f, player.MoveTick < move.Startup ? 0.5f : 0.95f);
        }

        private Sprite[] PosesOf(ActorKind kind)
        {
            switch (kind)
            {
                case ActorKind.Player: return _player;
                case ActorKind.Grunt: return _grunt;
                case ActorKind.Brute: return _brute;
                case ActorKind.Kicker: return _kicker;
                default: return _boss;
            }
        }

        /// <summary>Core の状態と技の Tick から姿を決める（実時間を使わない）。</summary>
        public static int PoseOf(Actor a)
        {
            if (a == null) { throw new ArgumentNullException(nameof(a)); }
            var move = a.Move;
            switch (a.State)
            {
                case ActorState.Walk:
                    return ((a.StateTick / 8) & 1) == 0 ? Walk : Stand;
                case ActorState.Attack:
                    if (move == null) { return Stand; }
                    if (move.Kind == MoveKind.Special) { return a.MoveTick < move.Startup ? Windup : Strike; }
                    if (a.MoveTick < move.Startup) { return move.Advance > 0 && a.Kind == ActorKind.Kicker ? Air : Windup; }
                    if (a.MoveTick < move.Startup + move.Active + 4) { return a.Kind == ActorKind.Kicker ? Kick : Strike; }
                    return Stand;
                case ActorState.Jump:
                    return move != null && a.MoveTick >= move.Startup ? Kick : Air;
                case ActorState.Grab:
                    if (move != null && a.MoveTick >= move.Startup) { return move.Kind == MoveKind.Knee ? Kick : Strike; }
                    return Hold;
                case ActorState.Grabbed:
                case ActorState.HitStun:
                    return Hurt;
                case ActorState.Knockdown:
                    return a.Timer == 0 ? Hurt : Down;
                case ActorState.Dead:
                    return Down;
                case ActorState.Wakeup:
                case ActorState.Land:
                    return Windup;
                default:
                    return Stand;
            }
        }

        private void DrawBoxes(Brawl brawl, BrawlConfig config)
        {
            var index = 0;
            for (var slot = 0; slot < brawl.SlotCount && slot < Pool; slot++)
            {
                var a = brawl.At(slot);
                if (!a.Active) { continue; }
                if (Brawl.Hurtable(a)) { index = Add(index, a.Body, new Color32(70, 220, 100, 255), config); }
                var move = a.Move;
                if (move != null && move.HasBox && a.Phase == MovePhase.Active) { index = Add(index, a.AttackBox, new Color32(240, 60, 50, 255), config); }
            }

            for (var i = index; i < MaxBoxes * 8; i++)
            {
                _vertices[i] = Vector3.zero;
                _colors[i] = default;
            }

            _mesh.vertices = _vertices;
            _mesh.colors32 = _colors;
            ShowBoxes(true);
        }

        /// <summary>箱の正面（奥行きの中心の高さ）と、地面に落とした奥行きの帯を描く。</summary>
        private int Add(int index, DepthBox box, Color32 color, BrawlConfig config)
        {
            if (index + 16 > _vertices.Length) { return index; }
            var minX = box.MinX / (float)Px.One;
            var maxX = box.MaxX / (float)Px.One;
            var bottom = Depth.ScreenY(box.MinY, box.Z, config) / (float)Px.One;
            var top = Depth.ScreenY(box.MaxY, box.Z, config) / (float)Px.One;
            index = Rect(index, minX, bottom, maxX, top, color);
            var near = Depth.ShadowY(box.MinZ, config) / (float)Px.One;
            var far = Depth.ShadowY(box.MaxZ, config) / (float)Px.One;
            var faint = new Color32(color.r, color.g, color.b, 150);
            return Rect(index, minX, near, maxX, far, faint);
        }

        private int Rect(int index, float minX, float minY, float maxX, float maxY, Color32 color)
        {
            _vertices[index] = new Vector3(minX, minY, 0f);
            _vertices[index + 1] = new Vector3(maxX, minY, 0f);
            _vertices[index + 2] = _vertices[index + 1];
            _vertices[index + 3] = new Vector3(maxX, maxY, 0f);
            _vertices[index + 4] = _vertices[index + 3];
            _vertices[index + 5] = new Vector3(minX, maxY, 0f);
            _vertices[index + 6] = _vertices[index + 5];
            _vertices[index + 7] = _vertices[index];
            for (var i = 0; i < 8; i++) { _colors[index + i] = color; }
            return index + 8;
        }

        private void ShowBoxes(bool enabled)
        {
            if (_boxRenderer != null && _boxRenderer.enabled != enabled) { _boxRenderer.enabled = enabled; }
        }
    }
}
