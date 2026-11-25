using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// ミサイルの煙。粒はミッションの Tick で歳を取り、Tick ごとに飛んでいるミサイルの位置から出す（037 の煙と同じ方式）。
    /// 実時間を使わないので、同じ Tick の並びで描けば同じ絵になる。描画の間に何 Tick も進んだときは、その分を今の位置から出す。
    /// 粒の置き場は起動時に作り、描画中に割り当てない。
    /// </summary>
    public sealed class HalcyonTrails : MonoBehaviour
    {
        public const int Capacity = 480;

        /// <summary>粒の寿命（Tick）。</summary>
        public const int Life = 60;

        /// <summary>何 Tick ごとに 1 粒出すか。</summary>
        public const int Interval = 2;

        [SerializeField] private GameObject _puffTemplate;

        private readonly Puff[] _puffs = new Puff[Capacity];
        private Transform[] _views;
        private Mission _mission;
        private int _tick;
        private int _next;

        /// <summary>生きている粒の数（テスト用）。</summary>
        public int Alive
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _puffs.Length; i++) { if (_puffs[i].Alive) { count++; } }
                return count;
            }
        }

        public GameObject Template => _puffTemplate;

        private void Awake() => EnsureViews();

        /// <summary>ミッションの Tick まで粒を進めて描く。別のミッションや巻き戻しなら全て消してから始める。</summary>
        public void Show(Mission mission)
        {
            EnsureViews();
            if (mission != _mission || mission.Tick < _tick)
            {
                Clear();
                _mission = mission;
                _tick = mission.Tick;
            }

            var elapsed = Mathf.Min(mission.Tick - _tick, Life);
            for (var step = 0; step < elapsed; step++)
            {
                var tick = mission.Tick - elapsed + step + 1;
                Age();
                if (tick % Interval == 0) { Emit(mission); }
            }

            _tick = mission.Tick;
            Draw();
        }

        public void Clear()
        {
            for (var i = 0; i < _puffs.Length; i++) { _puffs[i].Alive = false; }
            _mission = null;
            _tick = 0;
            Draw();
        }

        private void EnsureViews()
        {
            if (_views != null || _puffTemplate == null) { return; }
            _puffTemplate.SetActive(false);
            _views = new Transform[Capacity];
            for (var i = 0; i < Capacity; i++)
            {
                var view = Instantiate(_puffTemplate, transform);
                view.name = "Puff-" + i;
                _views[i] = view.transform;
            }
        }

        private void Age()
        {
            for (var i = 0; i < _puffs.Length; i++)
            {
                if (!_puffs[i].Alive) { continue; }
                _puffs[i].Age++;
                if (_puffs[i].Age >= Life) { _puffs[i].Alive = false; }
            }
        }

        private void Emit(Mission mission)
        {
            var missiles = mission.Missiles;
            for (var i = 0; i < missiles.Count; i++)
            {
                var missile = missiles[i];
                if (!missile.Active) { continue; }
                ref var puff = ref _puffs[_next];
                _next = (_next + 1) % _puffs.Length;
                puff.Position = HalcyonSpace.Position(missile.Position);
                puff.Age = 0;
                puff.Alive = true;
            }
        }

        private void Draw()
        {
            if (_views == null) { return; }
            for (var i = 0; i < _puffs.Length; i++)
            {
                var view = _views[i];
                var puff = _puffs[i];
                var active = view.gameObject.activeSelf;
                if (!puff.Alive)
                {
                    if (active) { view.gameObject.SetActive(false); }
                    continue;
                }

                if (!active) { view.gameObject.SetActive(true); }
                var t = puff.Age / (float)Life;
                var size = Mathf.Lerp(1.2f, 4.5f, t) * (1f - (t * t));
                view.localPosition = puff.Position;
                view.localScale = new Vector3(size, size, size);
            }
        }

        private struct Puff
        {
            public Vector3 Position;
            public int Age;
            public bool Alive;
        }
    }
}
