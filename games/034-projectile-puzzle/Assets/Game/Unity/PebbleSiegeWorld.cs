using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.Unity
{
    /// <summary>部材・弾の物理の体。衝突の始まりと接合の外れを世界へ伝える。</summary>
    public sealed class SiegeBody : MonoBehaviour
    {
        /// <summary>部材の番号（弾は -1）。</summary>
        public int Index { get; private set; }

        public Rigidbody2D Body { get; private set; }

        public Collider2D Shape { get; private set; }

        public SpriteRenderer Visual { get; private set; }

        private PebbleSiegeWorld _world;

        internal void Setup(PebbleSiegeWorld world, int index, Rigidbody2D body, Collider2D shape, SpriteRenderer visual)
        {
            _world = world;
            Index = index;
            Body = body;
            Shape = shape;
            Visual = visual;
        }

        private void OnCollisionEnter2D(Collision2D collision) => _world?.OnContact(this, collision);

        private void OnJointBreak2D(Joint2D joint) => _world?.OnJointBreak(joint);
    }

    /// <summary>
    /// Physics2D の世界（<see cref="ISiegeWorld"/>）。面を読み込むたびに、局所の物理を持つ新しいシーン（<c>LocalPhysicsMode.Physics2D</c>）を作るので、
    /// 前の面・やり直しの履歴に依らず、同じ面・同じ命令の列なら同じ結果になる（同じビルド・同じ機械）。
    /// 物理は <see cref="Simulate"/> が 1 Tick に 1 回だけ進める。衝突の撃力・外れた接合・消滅線・最大の速さを <see cref="TickReport"/> に書く。
    /// </summary>
    public sealed class PebbleSiegeWorld : ISiegeWorld, IDisposable
    {
        /// <summary>地面の当たり（盤の左右に余白を取る）。</summary>
        public const float GroundMargin = 12f;

        private readonly PebbleSiegeStage _stage;
        private readonly SiegeConfig _config;
        private readonly Dictionary<Joint2D, int> _jointIndex = new();
        private readonly (V2 Point, float Size, int Tick)[] _puffs = new (V2, float, int)[PebbleSiegeStage.MaxPuffs];
        private SiegeBody[] _pieces = Array.Empty<SiegeBody>();
        private Joint2D[] _joints = Array.Empty<Joint2D>();
        private bool[] _outReported = Array.Empty<bool>();
        private Scene _scene;
        private PhysicsScene2D _physics;
        private TickReport _report;
        private static int _sceneCount;
        private int _nextPuff;

        public PebbleSiegeWorld(PebbleSiegeStage stage, SiegeConfig config)
        {
            _stage = stage != null ? stage : throw new ArgumentNullException(nameof(stage));
            _config = (config ?? throw new ArgumentNullException(nameof(config))).Copy();
            _config.Validate();
            ResetPuffs();
        }

        /// <summary>読み込んでいる面の番号（なければ -1）。</summary>
        public int Loaded { get; private set; } = -1;

        public Stage Stage { get; private set; }

        /// <summary>読み込んでから進めた Tick 数。</summary>
        public int Ticks { get; private set; }

        public SiegeBody Shot { get; private set; }

        public ShotKind ShotKind { get; private set; }

        public int LastBlastTick { get; private set; } = -1;

        public V2 LastBlastPoint { get; private set; }

        public int PieceCount => _pieces.Length;

        /// <summary>部材 <paramref name="index"/> の体（消えていれば null）。</summary>
        public SiegeBody Piece(int index) => _pieces[index];

        /// <summary>接合 <paramref name="index"/>（外れていれば null）。</summary>
        public Joint2D Joint(int index) => _joints[index];

        public (V2 Point, float Size, int Tick) PuffAt(int index) => _puffs[index];

        public Scene Scene => _scene;

        public void Load(int index, Stage stage)
        {
            if (stage == null) { throw new ArgumentNullException(nameof(stage)); }
            stage.Validate(_config);
            Clear();
            _scene = SceneManager.CreateScene($"PebbleSiegeWorld{++_sceneCount}", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            _physics = _scene.GetPhysicsScene2D();
            Loaded = index;
            Stage = stage;
            Ticks = 0;

            var ground = NewObject("Ground");
            var groundShape = ground.AddComponent<BoxCollider2D>();
            groundShape.size = new Vector2(Field.Width + (2 * GroundMargin), 2);
            ground.transform.position = new Vector3(Field.Width / 2, -1, 0);

            _pieces = new SiegeBody[stage.Pieces.Count];
            _outReported = new bool[stage.Pieces.Count];
            for (var i = 0; i < _pieces.Length; i++)
            {
                var def = stage.Pieces[i];
                var root = NewObject($"Piece{i}");
                root.transform.position = PebbleSiegeStage.ToWorld(def.Center);
                var body = root.AddComponent<Rigidbody2D>();
                body.sleepMode = RigidbodySleepMode2D.StartAsleep;
                body.useAutoMass = true;
                var shape = root.AddComponent<BoxCollider2D>();
                shape.size = new Vector2(def.Size.X, def.Size.Y);
                shape.density = _config.Spec(def.Material).Density;
                var visual = Visual(root.transform, _stage.Template(def.Material));
                if (visual.drawMode == SpriteDrawMode.Tiled) { visual.size = new Vector2(def.Size.X, def.Size.Y); }
                else { visual.transform.localScale = new Vector3(def.Size.X, def.Size.Y, 1); }
                var piece = root.AddComponent<SiegeBody>();
                piece.Setup(this, i, body, shape, visual);
                _pieces[i] = piece;
            }

            _joints = new Joint2D[stage.Joints.Count];
            for (var k = 0; k < _joints.Length; k++)
            {
                var def = stage.Joints[k];
                var joint = _pieces[def.A].gameObject.AddComponent<FixedJoint2D>();
                joint.connectedBody = _pieces[def.B].Body;
                joint.autoConfigureConnectedAnchor = true;
                joint.breakForce = def.BreakForce;
                _joints[k] = joint;
                _jointIndex[joint] = k;
            }

            // 当たりの大きさを変えると体が起きるので、組み終えてから眠らせる（触れられるまで動かない）。
            foreach (var piece in _pieces) { piece.Body.Sleep(); }
        }

        public void Simulate(TickCommands commands, TickReport report)
        {
            if (Stage == null) { throw new InvalidOperationException("面を読み込んでいません。"); }
            _report = report ?? throw new ArgumentNullException(nameof(report));
            try
            {
                if (commands.RemoveShot) { RemoveShot(); }
                if (commands.Launch is { } velocity)
                {
                    RemoveShot();
                    SpawnShot(commands.Kind, velocity);
                }

                if (commands.Detonate) { Detonate(report); }
                _physics.Simulate(Trajectory.TickSeconds);
                Ticks++;
                Measure(report);
            }
            finally
            {
                _report = null;
            }
        }

        public void Remove(IReadOnlyList<int> pieces, IReadOnlyList<int> joints)
        {
            if (pieces == null) { throw new ArgumentNullException(nameof(pieces)); }
            if (joints == null) { throw new ArgumentNullException(nameof(joints)); }
            for (var i = 0; i < joints.Count; i++) { DropJoint(joints[i]); }
            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = _pieces[pieces[i]];
                if (piece == null) { continue; }
                var size = Stage.Pieces[pieces[i]].Size;
                AddPuff(PebbleSiegeStage.ToField(piece.transform.position), Mathf.Max(size.X, size.Y) + .4f);
                Discard(piece.gameObject);
                _pieces[pieces[i]] = null;
            }
        }

        public void Clear()
        {
            Shot = null;
            _pieces = Array.Empty<SiegeBody>();
            _joints = Array.Empty<Joint2D>();
            _outReported = Array.Empty<bool>();
            _jointIndex.Clear();
            Loaded = -1;
            Stage = null;
            LastBlastTick = -1;
            ResetPuffs();
            if (!_scene.IsValid()) { return; }

            // 描かれないよう、すぐに無効にしてから非同期で片付ける。
            foreach (var root in _scene.GetRootGameObjects()) { root.SetActive(false); }
            if (_scene.isLoaded) { SceneManager.UnloadSceneAsync(_scene); }
            _scene = default;
        }

        public void Dispose() => Clear();

        internal void OnContact(SiegeBody body, Collision2D collision)
        {
            if (_report == null) { return; }
            if (body.Index < 0)
            {
                _report.ShotTouched = true;
                return;
            }

            var impulse = 0f;
            for (var i = 0; i < collision.contactCount; i++) { impulse += collision.GetContact(i).normalImpulse; }
            _report.Impacts.Add(new Impact(body.Index, -1, impulse));
        }

        internal void OnJointBreak(Joint2D joint)
        {
            if (!_jointIndex.TryGetValue(joint, out var index)) { return; }
            _jointIndex.Remove(joint);
            _joints[index] = null;
            _report?.BrokenJoints.Add(index);
        }

        private void SpawnShot(ShotKind kind, V2 velocity)
        {
            var spec = _config.Spec(kind);
            var root = NewObject("Shot");
            root.transform.position = PebbleSiegeStage.ToWorld(Stage.Launcher);
            var body = root.AddComponent<Rigidbody2D>();
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.useAutoMass = true;
            var shape = root.AddComponent<CircleCollider2D>();
            shape.radius = spec.Radius;
            shape.density = spec.Density;
            var visual = Visual(root.transform, _stage.Template(kind));
            visual.transform.localScale = new Vector3(2 * spec.Radius, 2 * spec.Radius, 1);
            var shot = root.AddComponent<SiegeBody>();
            shot.Setup(this, -1, body, shape, visual);
            body.linearVelocity = new Vector2(velocity.X, velocity.Y);
            Shot = shot;
            ShotKind = kind;
        }

        /// <summary>爆発：弾の位置から半径内の部材に、外向きの撃力を与えて距離を報告する。弾は消える。</summary>
        private void Detonate(TickReport report)
        {
            if (Shot == null) { return; }
            var center = Shot.Body.position;
            for (var i = 0; i < _pieces.Length; i++)
            {
                var piece = _pieces[i];
                if (piece == null) { continue; }
                var distance = Vector2.Distance(center, piece.Shape.ClosestPoint(center));
                if (distance >= _config.BlastRadius) { continue; }
                report.Blasts.Add(new BlastHit(i, distance));
                var away = piece.Body.worldCenterOfMass - center;
                var direction = away.sqrMagnitude > 1e-6f ? away.normalized : Vector2.up;
                piece.Body.WakeUp();
                piece.Body.AddForce(direction * (_config.BlastImpulse * (1 - (distance / _config.BlastRadius))), ForceMode2D.Impulse);
            }

            LastBlastTick = Ticks;
            LastBlastPoint = new V2(center.x, center.y);
            RemoveShot();
        }

        /// <summary>物理を進めた後：消滅線・弾の盤外・最大の速さ。</summary>
        private void Measure(TickReport report)
        {
            float speed = 0, angular = 0;
            for (var i = 0; i < _pieces.Length; i++)
            {
                var piece = _pieces[i];
                if (piece == null) { continue; }
                var body = piece.Body;
                speed = Mathf.Max(speed, body.linearVelocity.magnitude);
                angular = Mathf.Max(angular, Mathf.Abs(body.angularVelocity));
                if (!_outReported[i] && Field.IsOut(PebbleSiegeStage.ToField(body.position)))
                {
                    _outReported[i] = true;
                    report.FellOut.Add(i);
                }
            }

            if (Shot != null)
            {
                if (Field.IsOut(PebbleSiegeStage.ToField(Shot.Body.position)))
                {
                    report.ShotGone = true;
                    RemoveShot();
                }
                else
                {
                    speed = Mathf.Max(speed, Shot.Body.linearVelocity.magnitude);
                    angular = Mathf.Max(angular, Mathf.Abs(Shot.Body.angularVelocity));
                }
            }

            report.MaxSpeed = speed;
            report.MaxAngular = angular;
        }

        private void DropJoint(int index)
        {
            var joint = _joints[index];
            if (joint == null) { return; }
            _jointIndex.Remove(joint);
            _joints[index] = null;
            joint.enabled = false;
            Object.Destroy(joint);
        }

        private void RemoveShot()
        {
            if (Shot == null) { return; }
            Discard(Shot.gameObject);
            Shot = null;
        }

        private void AddPuff(V2 point, float size)
        {
            _puffs[_nextPuff] = (point, size, Ticks);
            _nextPuff = (_nextPuff + 1) % _puffs.Length;
        }

        private void ResetPuffs()
        {
            for (var i = 0; i < _puffs.Length; i++) { _puffs[i] = (V2.Zero, 0, -1); }
            _nextPuff = 0;
        }

        /// <summary>物理からすぐに外し（無効にする）、フレームの終わりに消す。</summary>
        private static void Discard(GameObject item)
        {
            item.SetActive(false);
            Object.Destroy(item);
        }

        private GameObject NewObject(string name)
        {
            var item = new GameObject(name);
            SceneManager.MoveGameObjectToScene(item, _scene);
            return item;
        }

        private static SpriteRenderer Visual(Transform parent, SpriteRenderer template)
        {
            var visual = Object.Instantiate(template, parent);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            visual.enabled = true;
            visual.color = Color.white;
            return visual;
        }
    }
}
