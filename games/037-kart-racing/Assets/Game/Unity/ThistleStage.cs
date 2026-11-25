using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// コースの地面（Circuit）、4 台の車、煙。車の位置は <see cref="ThistleLayout"/> の式で置き、
    /// 地面は <see cref="ThistleTrackMesh"/> が作るメッシュと Prefab に保存したメッシュを照合する。
    /// </summary>
    public sealed class ThistleStage : MonoBehaviour
    {
        public const string CircuitName = "Circuit";

        public const string SmokeName = "Smoke";

        private Transform[] _karts;
        private SpriteRenderer[] _sprites;
        private ThistleSmoke _smoke;

        public ThistleSmoke Smoke
        {
            get
            {
                EnsureViews();
                return _smoke;
            }
        }

        public static string KartName(int index) => "Kart-" + index;

        public void Show(Race race)
        {
            EnsureViews();
            for (var i = 0; i < KartConfig.KartCount; i++)
            {
                var view = _karts[i];
                if (view == null) { continue; }
                var kart = race.KartAt(i);
                view.localPosition = ThistleLayout.At(kart.Position);
                view.localRotation = Quaternion.Euler(0f, 0f, ThistleLayout.Degrees(kart.Heading));
                var sprite = _sprites[i];
                if (sprite == null) { continue; }
                var tint = sprite.color;
                tint.a = kart.Finished ? 0.45f : 1f;
                sprite.color = tint;
            }

            if (_smoke != null) { _smoke.Show(race); }
        }

        /// <summary>煙を消す（タイトルへ戻ったとき）。</summary>
        public void ClearEffects()
        {
            EnsureViews();
            if (_smoke != null) { _smoke.Clear(); }
        }

        /// <summary>地面のメッシュを Track から作り直した結果と比べる。1 点でもずれていれば理由を返し、一致すれば null。</summary>
        public string Mismatch(Track track)
        {
            if (track == null) { return "コースがありません。"; }
            var circuit = transform.Find(CircuitName);
            var filter = circuit != null ? circuit.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null) { return CircuitName + " のメッシュがありません。"; }

            var expected = new List<Vector3>();
            ThistleTrackMesh.Write(track, expected, new List<Color>(), new List<Vector2>(), new List<int>());
            var actual = new List<Vector3>();
            filter.sharedMesh.GetVertices(actual);
            if (actual.Count != expected.Count) { return $"{CircuitName} の頂点の数が違います（{actual.Count} と {expected.Count}）。"; }
            for (var i = 0; i < expected.Count; i++)
            {
                if ((actual[i] - expected[i]).sqrMagnitude > 1e-6f) { return $"{CircuitName} の頂点 {i} の位置が違います。"; }
            }

            for (var i = 0; i < KartConfig.KartCount; i++)
            {
                var kart = transform.Find(KartName(i));
                if (kart == null || kart.GetComponent<SpriteRenderer>() == null) { return KartName(i) + " がありません。"; }
            }

            var smoke = transform.Find(SmokeName);
            if (smoke == null || smoke.GetComponent<ThistleSmoke>() == null) { return SmokeName + " がありません。"; }
            return null;
        }

        private void EnsureViews()
        {
            if (_karts != null) { return; }
            _karts = new Transform[KartConfig.KartCount];
            _sprites = new SpriteRenderer[KartConfig.KartCount];
            for (var i = 0; i < KartConfig.KartCount; i++)
            {
                _karts[i] = transform.Find(KartName(i));
                if (_karts[i] != null) { _sprites[i] = _karts[i].GetComponent<SpriteRenderer>(); }
            }

            var smoke = transform.Find(SmokeName);
            if (smoke != null) { _smoke = smoke.GetComponent<ThistleSmoke>(); }
        }
    }
}
