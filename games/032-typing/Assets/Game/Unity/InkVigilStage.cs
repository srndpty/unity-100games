using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 回廊と敵の描画。配置の正は Stage Prefab（<c>InkVigilStage.prefab</c>）で、敵の位置と大きさは <see cref="InkVigilPerspective"/> が決める。
    /// 敵は列ごとのスプライトの置き場に描く（同じ列に 2 体は出ない）。補間せず、その Tick の状態をそのまま描く。
    /// </summary>
    public sealed class InkVigilStage : MonoBehaviour
    {
        /// <summary>描画できる列の数。設定の列はこれ以下に限る。</summary>
        public const int MaxLanes = 5;

        /// <summary>敵の絵の高さ（拡大率 1 のとき、世界座標）。</summary>
        public const float EnemyHeight = 72f / 32f;

        /// <summary>段ごとの、絵の高さに対する頭の高さの割合（札は頭の上に出す）。</summary>
        private static readonly float[] _headRatio = { 32f / 72f, 63f / 72f, 72f / 72f };

        /// <summary>見張り（自機）の足もと。</summary>
        public static readonly Vector3 KeeperPosition = new(0, -4.05f, 0);

        [SerializeField] private SpriteRenderer _corridor;
        [SerializeField] private SpriteRenderer _nearLine;
        [SerializeField] private SpriteRenderer _keeper;
        [SerializeField] private SpriteRenderer _flash;
        [SerializeField] private SpriteRenderer _puff;
        [SerializeField] private SpriteRenderer[] _enemies = Array.Empty<SpriteRenderer>();

        /// <summary>段ごとの敵の絵（段 1 の 2 コマ、段 2 の 2 コマ、段 3 の 2 コマ）。</summary>
        [SerializeField] private Sprite[] _tierSprites = Array.Empty<Sprite>();

        /// <summary>列 <paramref name="lane"/> の敵の描画（テスト用）。</summary>
        public SpriteRenderer EnemyRenderer(int lane) => _enemies[lane];

        public SpriteRenderer Puff => _puff;

        public SpriteRenderer Flash => _flash;

        /// <summary>敵の頭の上（札を出す位置）。</summary>
        public Vector3 HeadOf(Enemy enemy, int lanes) => transform.TransformPoint(
            InkVigilPerspective.Foot(enemy.Lane, lanes, enemy.Distance) + new Vector3(0, EnemyHeight * _headRatio[enemy.Word.Tier - 1] * InkVigilPerspective.SpriteScale(enemy.Distance), 0));

        /// <summary>試合の状態を描く。<paramref name="effectTicks"/> は撃破・被弾の演出の長さ。</summary>
        public void Render(TypingRun run, int effectTicks)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            for (var lane = 0; lane < _enemies.Length; lane++) { _enemies[lane].enabled = false; }
            foreach (var enemy in run.Enemies) { Place(_enemies[enemy.Lane], enemy, run.Lanes, enemy == run.Target); }

            // 結果の画面では演出を止めて消す。
            var playing = run.Status == RunStatus.Playing;
            var sinceDefeat = run.LastDefeatedTick < 0 || !playing ? long.MaxValue : run.Ticks - run.LastDefeatedTick;
            _puff.enabled = run.LastDefeated != null && sinceDefeat < effectTicks;
            if (_puff.enabled)
            {
                var defeated = run.LastDefeated!;
                var t = (float)sinceDefeat / effectTicks;
                var scale = InkVigilPerspective.SpriteScale(defeated.Distance) * (1f + t);
                _puff.transform.localPosition = InkVigilPerspective.Foot(defeated.Lane, run.Lanes, defeated.Distance) + new Vector3(0, EnemyHeight * .45f * scale, 0);
                _puff.transform.localScale = new Vector3(scale, scale, 1);
                _puff.color = new Color(1, 1, 1, 1f - t);
                _puff.sortingOrder = InkVigilPerspective.SortingOrder(defeated.Distance) + 1;
            }

            var sinceHit = run.LastHitTick < 0 || !playing ? long.MaxValue : run.Ticks - run.LastHitTick;
            _flash.enabled = sinceHit < effectTicks;
            if (_flash.enabled) { _flash.color = new Color(1, .15f, .1f, .12f * (1f - ((float)sinceHit / effectTicks))); }
        }

        /// <summary>試合がないとき（タイトル）：敵と演出を隠す。</summary>
        public void RenderEmpty()
        {
            foreach (var enemy in _enemies) { enemy.enabled = false; }
            _puff.enabled = false;
            _flash.enabled = false;
        }

        /// <summary>被弾の直後の画面の揺れ（Tick から決める）。</summary>
        public static Vector3 Shake(TypingRun? run, int effectTicks)
        {
            if (run == null || run.LastHitTick < 0 || run.Status != RunStatus.Playing) { return Vector3.zero; }
            var since = run.Ticks - run.LastHitTick;
            if (since >= effectTicks) { return Vector3.zero; }
            var amount = .18f * (1f - ((float)since / effectTicks));
            return new Vector3(since % 2 == 0 ? amount : -amount, since % 3 == 0 ? amount * .5f : 0, 0);
        }

        /// <summary>Stage Prefab が描画に必要な部品と絵をそろえているか。そろっていれば null、欠けていれば理由。</summary>
        public string Mismatch()
        {
            if (_corridor == null || _corridor.sprite == null) { return "回廊の絵がありません。"; }
            if (_nearLine == null || _nearLine.sprite == null) { return "手前の線がありません。"; }
            if (Mathf.Abs(_nearLine.transform.localPosition.y - InkVigilPerspective.NearY) > 1e-4f) { return $"手前の線の高さが {InkVigilPerspective.NearY} と違います。"; }
            if (_keeper == null || _keeper.sprite == null) { return "見張りの絵がありません。"; }
            if ((_keeper.transform.localPosition - KeeperPosition).sqrMagnitude > 1e-8f) { return $"見張りの位置が {KeeperPosition} と違います。"; }
            if (_flash == null || _flash.sprite == null) { return "被弾の閃光がありません。"; }
            if (_puff == null || _puff.sprite == null) { return "撃破の煙がありません。"; }
            if (_enemies.Length != MaxLanes) { return $"敵の置き場が {MaxLanes} 個ではありません（{_enemies.Length} 個）。"; }
            for (var i = 0; i < _enemies.Length; i++)
            {
                if (_enemies[i] == null) { return $"敵の置き場 {i} がありません。"; }
            }

            if (_tierSprites.Length != WordBook.MaxTier * 2) { return $"敵の絵が {WordBook.MaxTier * 2} 枚ではありません（{_tierSprites.Length} 枚）。"; }
            for (var i = 0; i < _tierSprites.Length; i++)
            {
                if (_tierSprites[i] == null) { return $"敵の絵 {i} がありません。"; }
            }

            return null;
        }

        private void Place(SpriteRenderer renderer, Enemy enemy, int lanes, bool targeted)
        {
            var scale = InkVigilPerspective.SpriteScale(enemy.Distance);
            var bob = Mathf.Sin(enemy.Age * .12f) * .06f * scale;
            renderer.enabled = true;
            renderer.sprite = _tierSprites[((enemy.Word.Tier - 1) * 2) + (enemy.Age / 20 % 2)];
            renderer.transform.localPosition = InkVigilPerspective.Foot(enemy.Lane, lanes, enemy.Distance) + new Vector3(0, bob, 0);
            renderer.transform.localScale = new Vector3(scale, scale, 1);
            renderer.sortingOrder = InkVigilPerspective.SortingOrder(enemy.Distance);
            renderer.color = targeted ? Color.white : new Color(.72f, .74f, .82f, 1);
        }
    }
}
