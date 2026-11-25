using Game.App;
using Game.Core;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 後輪から出る煙と土ぼこり。粒はレースの Tick で歳を取り、Tick ごとに <see cref="TyreSmoke"/> が決めた車から出す。
    /// 実時間を使わないので、同じ Tick の並びで描けば同じ絵になる（撮影の再現）。
    /// 描画の間に何 Tick も進んだときは、その Tick 数だけ今の位置から出す（間の位置は Core が持たないため）。
    /// 粒の置き場は起動時に作り、描画中に割り当てない。
    /// </summary>
    public sealed class ThistleSmoke : MonoBehaviour
    {
        public const int Capacity = 384;

        private const int SmokeLife = 42;
        private const int DustLife = 30;
        private const float RearAxle = -0.5f;
        private const float Track = 0.4f;

        [SerializeField] private Sprite _puff;

        private readonly Puff[] _puffs = new Puff[Capacity];
        private SpriteRenderer[] _views;
        private Race _race;
        private int _tick;
        private int _next;
        private Color _smoke;
        private Color _dust;

        /// <summary>生きている粒の数（テスト用）。</summary>
        public int Alive
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _puffs.Length; i++) { if (_puffs[i].Life > 0) { count++; } }
                return count;
            }
        }

        private void Awake() => EnsureViews();

        /// <summary>レースの Tick まで粒を進め、描く。別のレースや巻き戻しなら全て消してから始める。</summary>
        public void Show(Race race)
        {
            EnsureViews();
            if (race != _race || race.Tick < _tick)
            {
                Clear();
                _race = race;
                _tick = race.Tick;
            }

            var elapsed = race.Tick - _tick;
            if (elapsed > SmokeLife) { elapsed = SmokeLife; }
            for (var step = 0; step < elapsed; step++)
            {
                Age();
                Emit(race, race.Tick - elapsed + step + 1);
            }

            _tick = race.Tick;
            Draw();
        }

        public void Clear()
        {
            for (var i = 0; i < _puffs.Length; i++) { _puffs[i].Life = 0; }
            _race = null;
            _tick = 0;
            Draw();
        }

        private void EnsureViews()
        {
            if (_views != null) { return; }
            _smoke = new Color(0.92f, 0.92f, 0.92f);
            _dust = new Color(0.58f, 0.50f, 0.34f);
            _views = new SpriteRenderer[Capacity];
            for (var i = 0; i < Capacity; i++)
            {
                var view = new GameObject("Puff-" + i);
                view.transform.SetParent(transform, false);
                var sprite = view.AddComponent<SpriteRenderer>();
                sprite.sprite = _puff;
                sprite.sortingOrder = 5;
                sprite.enabled = false;
                _views[i] = sprite;
            }
        }

        private void Age()
        {
            for (var i = 0; i < _puffs.Length; i++)
            {
                if (_puffs[i].Life <= 0) { continue; }
                _puffs[i].Age++;
                if (_puffs[i].Age >= _puffs[i].Life)
                {
                    _puffs[i].Life = 0;
                    continue;
                }

                _puffs[i].Position += _puffs[i].Velocity;
                _puffs[i].Velocity *= 0.93f;
            }
        }

        private void Emit(Race race, int tick)
        {
            for (var k = 0; k < KartConfig.KartCount; k++)
            {
                var kart = race.KartAt(k);
                var smoke = TyreSmoke.Of(kart);
                if (smoke == Smoke.None) { continue; }
                var heading = kart.Heading;
                var forward = new Vector2(Trig.Cos(heading), Trig.Sin(heading)) / Trig.One;
                var left = new Vector2(-forward.y, forward.x);
                var center = (Vector2)ThistleLayout.At(kart.Position);
                var dust = (smoke & Smoke.Dust) != 0;
                var heavy = (smoke & (Smoke.Drift | Smoke.Launch)) != 0;
                for (var wheel = -1; wheel <= 1; wheel += 2)
                {
                    var seed = Mix(tick, k, wheel);
                    var origin = center + (forward * RearAxle) + (left * (Track * wheel));
                    var spread = new Vector2(Jitter(seed, 1), Jitter(seed, 2)) * 0.03f;
                    ref var puff = ref _puffs[_next];
                    _next = (_next + 1) % _puffs.Length;
                    puff.Position = origin + (new Vector2(Jitter(seed, 3), Jitter(seed, 4)) * 0.08f);
                    puff.Velocity = (-forward * (dust ? 0.035f : 0.02f)) + (left * (wheel * 0.012f)) + spread;
                    puff.Age = 0;
                    puff.Life = dust ? DustLife : SmokeLife;
                    puff.Start = dust ? 0.3f : (heavy ? 0.32f : 0.26f);
                    puff.End = dust ? 1.0f : (heavy ? 1.35f : 0.95f);
                    puff.Alpha = dust ? 0.42f : (heavy ? 0.38f : 0.3f);
                    puff.Spin = Jitter(seed, 5) * 180f;
                    puff.Dust = dust;
                }
            }
        }

        private void Draw()
        {
            if (_views == null) { return; }
            for (var i = 0; i < _puffs.Length; i++)
            {
                var view = _views[i];
                var puff = _puffs[i];
                if (puff.Life <= 0)
                {
                    if (view.enabled) { view.enabled = false; }
                    continue;
                }

                var t = puff.Age / (float)puff.Life;
                var size = Mathf.Lerp(puff.Start, puff.End, 1f - ((1f - t) * (1f - t)));
                var color = puff.Dust ? _dust : _smoke;
                color.a = puff.Alpha * (1f - t) * (1f - t);
                view.enabled = true;
                view.color = color;
                var transformOfView = view.transform;
                transformOfView.localPosition = puff.Position;
                transformOfView.localScale = new Vector3(size, size, 1f);
                transformOfView.localRotation = Quaternion.Euler(0f, 0f, puff.Spin + (puff.Age * 2f));
            }
        }

        private static int Mix(int tick, int kart, int wheel)
        {
            unchecked
            {
                var h = (uint)tick * 0x9E3779B1u;
                h ^= (uint)(kart + 7) * 0x85EBCA77u;
                h ^= (uint)(wheel + 3) * 0xC2B2AE3Du;
                h ^= h >> 15;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        /// <summary>-1〜1 の決まった揺らぎ。</summary>
        private static float Jitter(int seed, int salt)
        {
            unchecked
            {
                var h = (uint)seed ^ ((uint)salt * 0x27D4EB2Fu);
                h ^= h >> 13;
                h *= 0x165667B1u;
                h ^= h >> 16;
                return ((h & 0xFFFF) / 32767.5f) - 1f;
            }
        }

        private struct Puff
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public int Age;
            public int Life;
            public float Start;
            public float End;
            public float Alpha;
            public float Spin;
            public bool Dust;
        }
    }
}
