using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 世界の描画。機体 4 機・ミサイル・爆発・煙・地面・滑走路を、その Tick の Core の状態で置く（補間しない）。
    /// 配置の正は Stage Prefab で、生成スクリプトが Core の滑走路と一致するかを <see cref="Mismatch"/> で確かめる。
    /// ミサイルと爆発の置き場は起動時に作り、描画中に割り当てない。
    /// </summary>
    public sealed class HalcyonStage : MonoBehaviour
    {
        public const int AircraftCount = MissionConfig.EnemyCount + 1;

        /// <summary>爆発を見せる Tick 数。</summary>
        public const int ExplosionTicks = 60;

        /// <summary>爆発の最大の直径（m）。</summary>
        public const float ExplosionSize = 60f;

        [SerializeField] private Transform[] _aircraft = new Transform[AircraftCount];
        [SerializeField] private GameObject _missileTemplate;
        [SerializeField] private GameObject _explosionTemplate;
        [SerializeField] private HalcyonTrails _trails;
        [SerializeField] private Transform _runway;
        [SerializeField] private Transform _ground;

        private Transform[] _missiles;
        private Transform[] _explosions;

        public HalcyonTrails Trails => _trails;

        public Transform AircraftAt(int index) => _aircraft[index];

        /// <summary>置き場のミサイル（テスト用）。</summary>
        public Transform MissileAt(int index) => _missiles[index];

        public int MissileCapacity => _missiles?.Length ?? 0;

        public Transform ExplosionAt(int index) => _explosions[index];

        /// <summary>置き場を作る（ミサイルは設定の総数）。2 回目以降は何もしない。</summary>
        public void Initialize(MissionConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var mismatch = Mismatch(config);
            if (mismatch != null) { throw new InvalidOperationException("Stage が設定と一致しません：" + mismatch); }
            if (_missiles != null) { return; }

            _missileTemplate.SetActive(false);
            _missiles = new Transform[config.MissileCapacity];
            for (var i = 0; i < _missiles.Length; i++)
            {
                var missile = Instantiate(_missileTemplate, transform);
                missile.name = "Missile-" + i;
                _missiles[i] = missile.transform;
            }

            _explosionTemplate.SetActive(false);
            _explosions = new Transform[AircraftCount];
            for (var i = 0; i < AircraftCount; i++)
            {
                var explosion = Instantiate(_explosionTemplate, transform);
                explosion.name = "Explosion-" + i;
                _explosions[i] = explosion.transform;
            }
        }

        /// <summary>その Tick の状態で置く。</summary>
        public void Show(Mission mission)
        {
            if (mission == null) { throw new ArgumentNullException(nameof(mission)); }
            for (var i = 0; i < AircraftCount; i++)
            {
                var fighter = mission.Fighters[i];
                var view = fighter.View;
                var plane = _aircraft[i];
                var visible = !fighter.Down;
                if (plane.gameObject.activeSelf != visible) { plane.gameObject.SetActive(visible); }
                plane.SetPositionAndRotation(HalcyonSpace.Position(view.Position), HalcyonSpace.Rotation(view.Attitude));
                ShowExplosion(i, fighter, mission.Tick);
            }

            var missiles = mission.Missiles;
            for (var i = 0; i < _missiles.Length; i++)
            {
                var view = _missiles[i];
                var active = i < missiles.Count && missiles[i].Active;
                if (view.gameObject.activeSelf != active) { view.gameObject.SetActive(active); }
                if (!active) { continue; }
                var missile = missiles[i];
                var velocity = HalcyonSpace.Velocity(missile.Velocity);
                var rotation = velocity.sqrMagnitude > 0f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
                view.SetPositionAndRotation(HalcyonSpace.Position(missile.Position), rotation);
            }

            _trails.Show(mission);
        }

        /// <summary>撃墜・墜落した Tick から <see cref="ExplosionTicks"/> の間、広がってから縮む火の玉。</summary>
        private void ShowExplosion(int index, Fighter fighter, int tick)
        {
            var view = _explosions[index];
            var age = fighter.DownTick < 0 ? -1 : tick - fighter.DownTick;
            var visible = age >= 0 && age < ExplosionTicks;
            if (view.gameObject.activeSelf != visible) { view.gameObject.SetActive(visible); }
            if (!visible) { return; }
            var t = age / (float)ExplosionTicks;
            var size = ExplosionSize * Mathf.Sin(Mathf.PI * Mathf.Sqrt(Mathf.Max(t, 0.02f)));
            view.position = HalcyonSpace.Position(fighter.View.Position);
            view.localScale = new Vector3(size, size, size);
        }

        /// <summary>Prefab の置き方が Core の設定と一致しなければ理由を返す（一致すれば null）。</summary>
        public string Mismatch(MissionConfig config)
        {
            if (config == null) { return "設定がありません。"; }
            if (_aircraft == null || _aircraft.Length != AircraftCount) { return $"機体は {AircraftCount} 機必要です。"; }
            for (var i = 0; i < AircraftCount; i++)
            {
                if (_aircraft[i] == null) { return $"機体 {i} がありません。"; }
                if (_aircraft[i].parent != transform) { return $"機体 {i} が Stage の直下にありません。"; }
            }

            if (_missileTemplate == null) { return "ミサイルの見本がありません。"; }
            if (_explosionTemplate == null) { return "爆発の見本がありません。"; }
            if (_trails == null || _trails.Template == null) { return "煙がありません。"; }
            if (_ground == null) { return "地面がありません。"; }
            if (_runway == null) { return "滑走路がありません。"; }
            if (transform.position != Vector3.zero || transform.rotation != Quaternion.identity || transform.lossyScale != Vector3.one)
            {
                return "Stage は原点に、回さず拡大せずに置いてください（Core の座標をそのまま使う）。";
            }

            var runway = config.Runway;
            var center = RunwayCenter(runway, _runway.localPosition.y);
            var size = new Vector3(runway.HalfWidth * 2 / (float)Units.PerMeter, _runway.localScale.y, runway.Length / (float)Units.PerMeter);
            if ((_runway.localPosition - center).sqrMagnitude > 0.01f || Mathf.Abs(_runway.localScale.x - size.x) > 0.1f || Mathf.Abs(_runway.localScale.z - size.z) > 0.1f)
            {
                return $"滑走路の位置・大きさが設定と違います（位置 {_runway.localPosition} / {center}、大きさ {_runway.localScale} / {size}）。";
            }

            return null;
        }

        /// <summary>Core の滑走路の中心（m）。生成スクリプトと照合で同じ式を使う。</summary>
        public static Vector3 RunwayCenter(Runway runway, float height) =>
            new(runway.X / (float)Units.PerMeter, height, (runway.StartZ + (runway.Length / 2)) / (float)Units.PerMeter);
    }
}
