using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>現在 Tick の状態をそのまま描く。補間はしない。</summary>
    public sealed class MazeView : MonoBehaviour
    {
        private const int FlashStartTicks = 120;
        private const int FlashPeriodTicks = 10;

        [SerializeField] private SpriteRenderer _mouse;
        [SerializeField] private SpriteRenderer _mouseWrap;
        [SerializeField] private SpriteRenderer[] _cats;
        [SerializeField] private SpriteRenderer[] _catWraps;
        [SerializeField] private SpriteRenderer[] _markers;
        [SerializeField] private SpriteRenderer[] _pellets;
        [SerializeField] private int[] _pelletCells;
        [SerializeField] private Sprite[] _catFaces;
        [SerializeField] private Sprite _dazedFace;
        [SerializeField] private Sprite _returningFace;

        private MazeGraph _graph;

        public int PelletCount => _pellets.Length;
        public int VisiblePelletCount
        {
            get
            {
                var count = 0;
                foreach (var pellet in _pellets) { if (pellet.enabled) { count++; } }
                return count;
            }
        }

        public Vector3 MousePosition => _mouse.transform.localPosition;
        public Sprite CatSprite(int index) => _cats[index].sprite;
        public Sprite CatFace(int index) => _catFaces[index];
        public Sprite DazedFace => _dazedFace;
        public bool IsMarkerVisible(int index) => _markers[index].enabled;

        public void Render(MazeGameState game, int dyingTicks, bool showTargets)
        {
            _graph ??= new MazeGraph(MazeLayout.CreateDefault());
            RenderPellets(game);
            RenderMouse(game, dyingTicks);
            for (var i = 0; i < _cats.Length; i++)
            {
                RenderCat(game, i);
                RenderMarker(game, i, showTargets);
            }
        }

        private void RenderPellets(MazeGameState game)
        {
            for (var i = 0; i < _pellets.Length; i++)
            {
                var visible = game == null || game.PelletAt(_pelletCells[i]) != Pellet.None;
                if (_pellets[i].enabled != visible) { _pellets[i].enabled = visible; }
            }
        }

        private void RenderMouse(MazeGameState game, int dyingTicks)
        {
            var position = game != null ? ActorPosition(game.Player) : CellPosition(_graph.PlayerCell);
            var heading = game?.Player.Heading ?? Direction.Left;
            var dying = game != null && game.Phase == GamePhase.Dying;
            var scale = dying ? Mathf.Clamp01((float)game.PhaseTicksLeft / dyingTicks) : 1f;
            var caught = dying || game?.Phase == GamePhase.GameOver;
            foreach (var renderer in new[] { _mouse, _mouseWrap })
            {
                renderer.flipX = heading == Direction.Left;
                renderer.transform.localRotation = Quaternion.Euler(0, 0, heading == Direction.Up ? 90 : heading == Direction.Down ? -90 : 0);
                renderer.transform.localScale = new Vector3(scale, scale, 1);
                renderer.color = caught ? new Color(1, .55f, .55f) : Color.white;
            }

            Place(_mouse, _mouseWrap, position);
        }

        private void RenderCat(MazeGameState game, int index)
        {
            var cat = game?.Cat(index);
            var position = cat != null ? ActorPosition(cat.Actor) : CellPosition(MazeGameState.CatSpawnCell(_graph, index));
            var sprite = _catFaces[index];
            if (cat?.Mode == CatMode.Returning) { sprite = _returningFace; }
            else if (cat?.Mode == CatMode.Frightened && !IsFlashing(game)) { sprite = _dazedFace; }

            _cats[index].sprite = sprite;
            _catWraps[index].sprite = sprite;
            Place(_cats[index], _catWraps[index], position);
        }

        // マタタビが切れる直前は元の顔と交互に見せて知らせる。
        private static bool IsFlashing(MazeGameState game) =>
            game.PowerTicksLeft < FlashStartTicks && game.PowerTicksLeft / FlashPeriodTicks % 2 == 1;

        private void RenderMarker(MazeGameState game, int index, bool showTargets)
        {
            var target = showTargets ? game?.TargetOf(index) : null;
            _markers[index].enabled = target.HasValue;
            if (!target.HasValue) { return; }
            var x = Mathf.Clamp(target.Value.X, 0, _graph.Width - 1);
            var y = Mathf.Clamp(target.Value.Y, 0, _graph.Height - 1);
            _markers[index].transform.localPosition = CellPosition(_graph.CellId(x, y));
        }

        // 周回通路の端では、反対側の複製を重ねて表示をつなぐ。盤面の外はマスクで隠れる。
        private void Place(SpriteRenderer main, SpriteRenderer wrap, Vector3 position)
        {
            main.transform.localPosition = position;
            wrap.transform.localPosition = position + new Vector3(position.x < 0 ? _graph.Width : -_graph.Width, 0, 0);
        }

        private Vector3 ActorPosition(MazeActor actor)
        {
            var x = actor.WorldX(_graph) / (float)MazeActor.UnitsPerCell;
            if (x > _graph.Width - .5f) { x -= _graph.Width; }
            return ToLocal(x, actor.WorldY(_graph) / (float)MazeActor.UnitsPerCell);
        }

        private Vector3 CellPosition(int cell)
        {
            var point = _graph.Point(cell);
            return ToLocal(point.X, point.Y);
        }

        private Vector3 ToLocal(float x, float y) => new(x - ((_graph.Width - 1) / 2f), ((_graph.Height - 1) / 2f) - y, 0);
    }
}
