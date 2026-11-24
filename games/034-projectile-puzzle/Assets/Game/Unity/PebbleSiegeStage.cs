using System;
using Game.App;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 盤の描画の置き場（Stage Prefab <c>PebbleSiegeStage.prefab</c>）。背景・地面・発射台・構えた弾・ゴム・弾道の点・爆発と破壊の演出、
    /// 部材と弾の見本（<see cref="PebbleSiegeWorld"/> が面ごとに複製して物理を付ける）を持つ。
    /// 座標はワールド座標と同じ（1 = 1 m、Stage は原点に置く）。補間せず、その Tick の状態をそのまま描く。
    /// </summary>
    public sealed class PebbleSiegeStage : MonoBehaviour
    {
        /// <summary>弾道の点の数の上限。</summary>
        public const int MaxDots = 32;

        /// <summary>破壊の演出（土ぼこり）の数の上限。</summary>
        public const int MaxPuffs = 12;

        /// <summary>二股の先（発射台からの位置）。ゴムの両端。</summary>
        public static readonly Vector3 ForkLeft = new(-.55f, .05f, 0);

        public static readonly Vector3 ForkRight = new(.55f, .05f, 0);

        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private SpriteRenderer _ground;
        [SerializeField] private SpriteRenderer _slingshot;
        [SerializeField] private SpriteRenderer _loaded;
        [SerializeField] private LineRenderer _band;
        [SerializeField] private SpriteRenderer _blast;
        [SerializeField] private SpriteRenderer[] _dots = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] _puffs = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer _wood;
        [SerializeField] private SpriteRenderer _stone;
        [SerializeField] private SpriteRenderer _ice;
        [SerializeField] private SpriteRenderer _lantern;
        [SerializeField] private SpriteRenderer _normal;
        [SerializeField] private SpriteRenderer _heavy;
        [SerializeField] private SpriteRenderer _bomb;

        private V2[] _trajectory = Array.Empty<V2>();
        private readonly Vector3[] _bandPoints = new Vector3[3];

        public SpriteRenderer Slingshot => _slingshot;

        public SpriteRenderer Loaded => _loaded;

        public LineRenderer Band => _band;

        public SpriteRenderer Blast => _blast;

        public SpriteRenderer Dot(int index) => _dots[index];

        public SpriteRenderer Puff(int index) => _puffs[index];

        /// <summary>部材の見本（タイル表示、灯籠は 1 m 角の絵）。</summary>
        public SpriteRenderer Template(Core.Material material) => material switch
        {
            Core.Material.Wood => _wood,
            Core.Material.Stone => _stone,
            Core.Material.Ice => _ice,
            Core.Material.Lantern => _lantern,
            _ => throw new ArgumentOutOfRangeException(nameof(material), material, "未定義の素材です。"),
        };

        /// <summary>弾の見本（直径 1 m の絵）。</summary>
        public SpriteRenderer Template(ShotKind kind) => kind switch
        {
            ShotKind.Normal => _normal,
            ShotKind.Heavy => _heavy,
            ShotKind.Bomb => _bomb,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の弾です。"),
        };

        public static Vector3 ToWorld(V2 point) => new(point.X, point.Y, 0);

        public static V2 ToField(Vector3 point) => new(point.x, point.y);

        /// <summary>
        /// 発射台・構えた弾・ゴム・弾道の点を描く。構えていなければ弾は発射台に置き、点は隠す。
        /// 撃てない（面の外・飛行中・終わった後）なら弾を隠す。
        /// </summary>
        public void RenderLauncher(SiegeFlow flow, SiegeConfig config)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var run = flow.Run;
            var stage = run?.Stage ?? flow.Book.Stages[flow.Cursor];
            var launcher = ToWorld(stage.Launcher);
            _slingshot.transform.localPosition = launcher + new Vector3(0, -1.2f, 0);

            var ready = run != null && flow.Screen == AppScreen.Playing && run.Phase == RunPhase.Aiming && run.NextShot != null;
            var aim = ready ? flow.Aim : null;
            var holder = aim is { } pull ? launcher + ToWorld(pull) : launcher;
            _loaded.enabled = ready;
            if (ready)
            {
                var template = Template(run!.NextShot!.Value);
                var diameter = 2 * config.Spec(run.NextShot.Value).Radius;
                _loaded.sprite = template.sprite;
                _loaded.transform.localPosition = holder;
                _loaded.transform.localScale = new Vector3(diameter, diameter, 1);
            }

            _bandPoints[0] = launcher + ForkLeft;
            _bandPoints[1] = ready ? holder : launcher;
            _bandPoints[2] = launcher + ForkRight;
            _band.positionCount = 3;
            _band.SetPositions(_bandPoints);

            var shown = 0;
            if (ready && flow.AimVelocity is { } velocity)
            {
                var ticks = config.PreviewTicks;
                if (_trajectory.Length != ticks) { _trajectory = new V2[ticks]; }
                Trajectory.Fill(stage.Launcher, velocity, config.Gravity, _trajectory);
                for (var t = config.PreviewStride - 1; t < ticks && shown < _dots.Length; t += config.PreviewStride)
                {
                    var dot = _dots[shown];
                    dot.enabled = true;
                    dot.transform.localPosition = ToWorld(_trajectory[t]);
                    var fade = 1f - ((float)t / ticks * .7f);
                    dot.color = new Color(1, 1, 1, fade);
                    shown++;
                }
            }

            for (var i = shown; i < _dots.Length; i++) { _dots[i].enabled = false; }
        }

        /// <summary>爆発と破壊の演出を、世界の Tick から決めて描く。</summary>
        public void RenderEffects(PebbleSiegeWorld world, SiegeConfig config, int effectTicks)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var since = world.LastBlastTick < 0 ? int.MaxValue : world.Ticks - world.LastBlastTick;
            _blast.enabled = since < effectTicks;
            if (_blast.enabled)
            {
                var t = (float)since / effectTicks;
                var diameter = 2 * config.BlastRadius * (.5f + (.5f * t));
                _blast.transform.localPosition = ToWorld(world.LastBlastPoint);
                _blast.transform.localScale = new Vector3(diameter, diameter, 1);
                _blast.color = new Color(1, 1, 1, 1 - t);
            }

            for (var i = 0; i < _puffs.Length; i++)
            {
                var puff = world.PuffAt(i);
                var age = puff.Tick < 0 ? int.MaxValue : world.Ticks - puff.Tick;
                var puffRenderer = _puffs[i];
                puffRenderer.enabled = age < effectTicks;
                if (!puffRenderer.enabled) { continue; }
                var t = (float)age / effectTicks;
                puffRenderer.transform.localPosition = ToWorld(puff.Point) + new Vector3(0, .4f * t, 0);
                var size = puff.Size * (.8f + (.6f * t));
                puffRenderer.transform.localScale = new Vector3(size, size, 1);
                puffRenderer.color = new Color(1, 1, 1, 1 - t);
            }
        }

        /// <summary>Stage Prefab が描画に必要な部品と絵をそろえているか。そろっていれば null、欠けていれば理由。</summary>
        public string Mismatch()
        {
            var single = Sprite("背景", _background) ?? Sprite("地面", _ground) ?? Sprite("発射台", _slingshot) ?? Sprite("構えた弾", _loaded)
                ?? Sprite("爆発", _blast) ?? Sprite("木の見本", _wood) ?? Sprite("石の見本", _stone) ?? Sprite("氷の見本", _ice)
                ?? Sprite("灯籠の見本", _lantern) ?? Sprite("通常の弾の見本", _normal) ?? Sprite("重い弾の見本", _heavy) ?? Sprite("爆発の弾の見本", _bomb);
            if (single != null) { return single; }
            if (_band == null || _band.sharedMaterial == null || _band.useWorldSpace) { return "ゴム（ローカル座標の LineRenderer）がありません。"; }
            if (!Identity(_band.transform)) { return "ゴムの位置・回転・大きさが Stage と違います。"; }
            foreach (var tiled in new[] { _ground, _wood, _stone, _ice })
            {
                if (tiled.drawMode != SpriteDrawMode.Tiled) { return $"{tiled.name} はタイル表示にしてください。"; }
            }

            foreach (var template in new[] { _wood, _stone, _ice, _lantern, _normal, _heavy, _bomb })
            {
                if (template.transform.parent != transform) { return $"見本 {template.name} は Stage の直下に置いてください。"; }
            }

            return Parts("弾道の点", _dots, MaxDots) ?? Parts("土ぼこり", _puffs, MaxPuffs);
        }

        /// <summary>面と設定を描けるか（弾道の点の数）。描けなければ理由。</summary>
        public static string Capacity(StageBook book, SiegeConfig config)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var dots = config.PreviewTicks / config.PreviewStride;
            if (dots > MaxDots) { return $"弾道の点が {dots} 個になります（描けるのは {MaxDots} 個まで）。PreviewTicks か PreviewStride を変えてください。"; }
            return null;
        }

        private static bool Identity(Transform part) =>
            part.localPosition == Vector3.zero && part.localRotation == Quaternion.identity && part.localScale == Vector3.one;

        private static string Sprite(string name, SpriteRenderer renderer) =>
            renderer == null || renderer.sprite == null ? $"{name}の絵がありません。" : null;

        private static string Parts(string name, SpriteRenderer[] parts, int count)
        {
            if (parts.Length != count) { return $"{name}の置き場が {count} 個ではありません（{parts.Length} 個）。"; }
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null || parts[i].sprite == null) { return $"{name}の置き場 {i} がありません。"; }
            }

            return null;
        }
    }
}
