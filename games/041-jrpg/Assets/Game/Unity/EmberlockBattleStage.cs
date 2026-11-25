#nullable enable
using System;
using Game.App;
using Game.Core;
using Game.Unity.Data;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>フロントビュー戦闘画面の背景・敵スプライト・ターゲット矢印・被弾フラッシュを描画する。</summary>
    public sealed class EmberlockBattleStage : MonoBehaviour
    {
        public const int MaxEnemySlots = 4;

        [SerializeField] private SpriteRenderer? _backdrop;
        [SerializeField] private SpriteRenderer[] _enemySlots = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer? _targetArrow;

        public string? Mismatch()
        {
            if (_backdrop == null)
            {
                return "戦闘背景 SpriteRenderer が未設定です。";
            }

            if (_enemySlots == null || _enemySlots.Length != MaxEnemySlots)
            {
                return $"敵スロット数が {MaxEnemySlots} ではありません。";
            }

            for (var i = 0; i < _enemySlots.Length; i++)
            {
                if (_enemySlots[i] == null)
                {
                    return $"敵スロット {i} が null です。";
                }
            }

            if (_targetArrow == null)
            {
                return "ターゲット矢印 SpriteRenderer が未設定です。";
            }

            return null;
        }

        public void Show(BattlePresenter presenter, GameDatabase database, bool isBossBattle)
        {
            if (presenter == null || database == null)
            {
                return;
            }

            if (_backdrop != null)
            {
                _backdrop.color = isBossBattle
                    ? new Color(0.55f, 0.22f, 0.20f, 1f)
                    : new Color(0.28f, 0.32f, 0.45f, 1f);
            }

            var snap = presenter.DisplaySnapshot;
            var total = Math.Min(snap.Enemies.Count, _enemySlots.Length);
            var spacing = total <= 1 ? 0f : (total == 2 ? 3.2f : 2.6f);
            var startX = -0.5f * (total - 1) * spacing;

            for (var i = 0; i < _enemySlots.Length; i++)
            {
                var sr = _enemySlots[i];
                if (sr == null)
                {
                    continue;
                }

                if (i >= total || !snap.Enemies[i].IsAlive)
                {
                    sr.enabled = false;
                    continue;
                }

                var enemySnap = snap.Enemies[i];
                sr.enabled = true;
                sr.sprite = database.FindEnemySprite(enemySnap.EnemyId);
                var yOffset = isBossBattle ? 0.55f : 0.35f;
                sr.transform.localPosition = new Vector3(startX + (i * spacing), yOffset, 0f);

                var flashing = presenter.HitFlashTicks > 0 &&
                               presenter.LastPopupSide == BattleSide.Enemy &&
                               presenter.LastPopupTargetIndex == i &&
                               ((presenter.HitFlashTicks / 2) % 2 == 1);
                sr.color = flashing ? new Color(1f, 0.35f, 0.35f, 1f) : Color.white;
            }

            if (_targetArrow != null)
            {
                var showTarget = presenter.Step == BattleInputStep.EnemyTarget &&
                                 presenter.TargetCursor >= 0 &&
                                 presenter.TargetCursor < total &&
                                 snap.Enemies[presenter.TargetCursor].IsAlive;
                _targetArrow.enabled = showTarget;
                if (showTarget)
                {
                    var targetPos = _enemySlots[presenter.TargetCursor].transform.localPosition;
                    var topOffset = isBossBattle ? 2.15f : 1.45f;
                    _targetArrow.transform.localPosition = new Vector3(targetPos.x, targetPos.y + topOffset, 0f);
                }
            }
        }
    }
}
