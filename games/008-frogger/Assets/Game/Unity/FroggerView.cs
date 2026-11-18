using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    public sealed class FroggerView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _frog;
        [SerializeField] private SpriteRenderer[] _objects;
        public int ObjectCount => _objects.Length;
        public Vector3 FrogPosition => _frog.transform.localPosition;
        public void Render(FroggerState game, FroggerConfig config)
        {
            var state = game ?? new FroggerState(config);
            _frog.transform.localPosition = new Vector3((float)state.X - 5.5f, state.Row - 4, 0);
            _frog.color = state.Result == RoundResult.CarHit || state.Result == RoundResult.Drowned || state.Result == RoundResult.CarriedOut
                ? new Color(1, .5f, .5f) : Color.white;
            for (var lane = 0; lane < 6; lane++)
            {
                for (var id = 0; id < 4; id++)
                {
                    var renderer = _objects[lane * 4 + id];
                    renderer.transform.localPosition = new Vector3((float)state.ObjectX(lane, id) - 5.5f, FroggerConfig.LaneRow(lane) - 4, 0);
                    renderer.transform.localScale = new Vector3(config.Lanes[lane].Width / renderer.sprite.bounds.size.x, .72f / renderer.sprite.bounds.size.y, 1);
                    renderer.flipX = config.Lanes[lane].Speed < 0;
                }
            }
        }
    }
}
