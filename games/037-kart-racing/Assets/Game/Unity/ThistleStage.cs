using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>コースと 4 台の車。位置は <see cref="ThistleLayout"/> と同じ式で Prefab と照合する。</summary>
    public sealed class ThistleStage : MonoBehaviour
    {
        private Transform[] _karts;
        private SpriteRenderer[] _sprites;

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
                var color = _sprites[i];
                if (color == null) { continue; }
                var tint = color.color;
                tint.a = kart.Finished ? 0.45f : 1f;
                color.color = tint;
            }
        }

        private void EnsureViews()
        {
            if (_karts != null) { return; }
            _karts = new Transform[KartConfig.KartCount];
            _sprites = new SpriteRenderer[KartConfig.KartCount];
            for (var i = 0; i < KartConfig.KartCount; i++)
            {
                _karts[i] = transform.Find("Kart-" + i);
                if (_karts[i] != null) { _sprites[i] = _karts[i].GetComponent<SpriteRenderer>(); }
            }
        }

        /// <summary>点を 1 つ動かした Prefab は不一致になる。一致するときは null。</summary>
        public string Mismatch(Track track)
        {
            if (track == null) { return "コースがありません。"; }
            for (var i = 0; i < track.StationCount; i++)
            {
                var marker = transform.Find("Marker-" + i);
                if (marker == null) { return "Marker-" + i + " がありません。"; }
                var expected = ThistleLayout.At(track.StationPosition(i));
                var delta = marker.localPosition - expected;
                if (delta.sqrMagnitude > 0.0001f) { return "Marker-" + i + " の位置が違います。"; }
            }

            for (var i = 0; i < KartConfig.KartCount; i++)
            {
                if (transform.Find("Kart-" + i) == null) { return "Kart-" + i + " がありません。"; }
            }

            return null;
        }
    }
}
