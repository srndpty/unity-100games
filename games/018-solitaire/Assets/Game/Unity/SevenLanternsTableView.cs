using Game.App;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Unity
{
    /// <summary>
    /// 卓の描画。Prefab に配置済みの 52 枚の札（札ごとに 1 つの SortingGroup）と 13 の枠を使い回す。
    /// 盤（Core）とドラッグの状態（App）を読むだけで書き戻さない。置き場所は <see cref="TableLayout"/> と同じ計算を使う。
    /// 札は新しい位置へ実時間で寄っていく。ドラッグ中の札だけはポインタに付いて即座に動く。
    /// </summary>
    public sealed class SevenLanternsTableView : MonoBehaviour
    {
        private const int MovingOrder = 300;
        private const int DraggingOrder = 600;
        private const float SnapDistance = .002f;
        private const float MovingDistance = .05f;

        [SerializeField] private Transform[] _cards;
        [SerializeField] private SortingGroup[] _groups;
        [SerializeField] private SpriteRenderer[] _backs;
        [SerializeField] private SpriteRenderer[] _highlights;
        [SerializeField] private SpriteRenderer[] _slots;

        private bool _placed;

        /// <summary>札の今の位置（卓の座標）。</summary>
        public Vector3 CardPosition(Card card) => _cards[card.Id].localPosition;

        public bool IsFaceUp(Card card) => !_backs[card.Id].enabled;

        public bool IsHighlighted(Card card) => _highlights[card.Id].enabled;

        public int SortingOrderOf(Card card) => _groups[card.Id].sortingOrder;

        /// <summary>卓の中身が届きうる範囲（<see cref="TableLayout"/> の端）を、卓の Transform で変換したワールド座標の範囲。</summary>
        public Bounds ContentWorldBounds()
        {
            var min = transform.TransformPoint(new Vector3((float)TableLayout.ContentLeft, (float)TableLayout.ContentBottom, 0));
            var max = transform.TransformPoint(new Vector3((float)TableLayout.ContentRight, (float)TableLayout.ContentTop, 0));
            var bounds = new Bounds(min, Vector3.zero);
            bounds.Encapsulate(max);
            return bounds;
        }

        /// <summary>空の山の枠を置く。札はまだ動かさない。</summary>
        public void Arrange()
        {
            foreach (var pile in PileId.All) { _slots[pile.Index].transform.localPosition = ToVector(TableLayout.SlotCenter(pile)); }
        }

        /// <summary>
        /// 盤を描く。sharpness が 0 なら札を目標の位置へ即座に置く（撮影中と最初の描画）。
        /// </summary>
        public void Render(IReadOnlyTable table, PileRun? selected, PileRun? dragging, TablePoint dragBase, float deltaTime, float sharpness)
        {
            var blend = !_placed || sharpness <= 0 ? 1f : 1f - Mathf.Exp(-sharpness * Mathf.Max(deltaTime, 0f));
            _placed = true;
            foreach (var pile in PileId.All)
            {
                var count = table.CountOf(pile);
                var faceDown = table.FaceDownCountOf(pile);
                for (var index = 0; index < count; index++)
                {
                    var card = table.CardAt(pile, index);
                    var id = card.Id;
                    var target = ToVector(TableLayout.CardCenter(table, pile, index));
                    var order = index + 1;
                    var held = dragging is PileRun drag && drag.Pile == pile && index >= count - drag.Count;
                    var cardTransform = _cards[id];
                    if (held)
                    {
                        var baseIndex = count - dragging!.Value.Count;
                        target = ToVector(dragBase) + new Vector3(0, (float)(-(index - baseIndex) * TableLayout.FaceUpStep), 0);
                        cardTransform.localPosition = target;
                        order += DraggingOrder;
                    }
                    else
                    {
                        var position = Vector3.Lerp(cardTransform.localPosition, target, blend);
                        if ((position - target).sqrMagnitude < SnapDistance * SnapDistance) { position = target; }
                        cardTransform.localPosition = position;
                        if ((position - target).sqrMagnitude > MovingDistance * MovingDistance) { order += MovingOrder; }
                    }

                    if (_groups[id].sortingOrder != order) { _groups[id].sortingOrder = order; }
                    var faceUp = index >= faceDown;
                    if (_backs[id].enabled == faceUp) { _backs[id].enabled = !faceUp; }
                    var highlighted = selected is PileRun run && run.Pile == pile && index >= count - run.Count;
                    if (_highlights[id].enabled != highlighted) { _highlights[id].enabled = highlighted; }
                }
            }
        }

        private static Vector3 ToVector(TablePoint point) => new((float)point.X, (float)point.Y, 0);
    }
}
