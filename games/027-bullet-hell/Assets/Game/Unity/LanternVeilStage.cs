using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// プレイ領域の描画。背景・枠・自機・当たり判定の点・ボス・体力のバー・ボムの輪・ショットの見本は SpriteRenderer、
    /// 敵弾は <see cref="BulletLayer"/> の Mesh 1 つで、どれも生成スクリプトが Prefab に置く（プレイ前から Scene ビューで見える）。
    /// 1 px（Core の 256 単位）= 1/16 ワールド単位。Stage のローカル座標の原点がプレイ領域の左下。
    /// Tick の状態をそのまま描き、補間しない。点滅も Tick で決める。
    /// </summary>
    public sealed class LanternVeilStage : MonoBehaviour
    {
        /// <summary>1 ワールド単位のピクセル数（絵の 1 ピクセル = 領域の 1 px）。</summary>
        public const int PixelsPerUnit = 16;

        /// <summary>無敵中の点滅の 1 段の Tick 数。</summary>
        public const int BlinkTicks = 4;

        /// <summary>敵弾の Mesh の描画順。</summary>
        public const int BulletOrder = 20;

        /// <summary>領域の外を覆う幕の描画順（弾より手前、体力のバーより奥）。</summary>
        public const int CurtainOrder = 28;

        /// <summary>体力のバーの描画順。</summary>
        public const int HpBarOrder = 30;

        /// <summary>幕が領域の外へ伸びる長さ（ワールド単位）。どの横長の画面でも、領域の外の余白（弾が生きている 32 px = 2 単位）を覆いきる。</summary>
        public const float CurtainReach = 60f;

        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private SpriteRenderer _player;
        [SerializeField] private SpriteRenderer _hitbox;
        [SerializeField] private SpriteRenderer _boss;
        [SerializeField] private SpriteRenderer _hpBar;
        [SerializeField] private SpriteRenderer _bombRing;
        [SerializeField] private SpriteRenderer _shotTemplate;
        [SerializeField] private BulletLayer _bullets;
        [SerializeField] private Sprite[] _bossSprites;

        /// <summary>プレイ領域の外を覆う幕（左・右・下・上）。余白にいる弾を隠す。弾より手前、体力のバーより奥に描く。</summary>
        [SerializeField] private SpriteRenderer[] _curtains;

        private readonly List<SpriteRenderer> _shots = new();

        public SpriteRenderer Player => _player;

        public SpriteRenderer Hitbox => _hitbox;

        public SpriteRenderer Boss => _boss;

        public SpriteRenderer HpBar => _hpBar;

        public SpriteRenderer BombRing => _bombRing;

        public BulletLayer Bullets => _bullets;

        /// <summary>領域の外を覆う幕（左・右・下・上。テストで配置の検査を確かめるのに使う）。</summary>
        public IReadOnlyList<SpriteRenderer> Curtains => Array.AsReadOnly(_curtains);

        /// <summary>ショットの描画物の数（見本を含む。増やすだけで減らさない。設定の検証で <see cref="VeilConfig.MaxShots"/> 以下に収まる）。</summary>
        public int PooledShots => _shots.Count;

        /// <summary>今表示しているショットの数。</summary>
        public int VisibleShots { get; private set; }

        /// <summary>プレイ領域の大きさ（ワールド単位）。</summary>
        public static Vector2 FieldSize => new((float)Field.WidthPx / PixelsPerUnit, (float)Field.HeightPx / PixelsPerUnit);

        /// <summary>
        /// 幕 4 枚（左・右・下・上）の名前・位置・大きさ。幕の絵は左下を基準にした 1 単位の四角で、大きさはそのまま拡大の倍率になる。
        /// 生成スクリプトと <see cref="Mismatch"/> が同じこの値を使う（Prefab の幕をずらすと、余白の弾が見える）。
        /// </summary>
        public static (string Name, Vector3 Position, Vector2 Size)[] CurtainLayout()
        {
            var field = FieldSize;
            return new[]
            {
                ("CurtainLeft", new Vector3(-CurtainReach, -CurtainReach, 0), new Vector2(CurtainReach, field.y + (2 * CurtainReach))),
                ("CurtainRight", new Vector3(field.x, -CurtainReach, 0), new Vector2(CurtainReach, field.y + (2 * CurtainReach))),
                ("CurtainBottom", new Vector3(0, -CurtainReach, 0), new Vector2(field.x, CurtainReach)),
                ("CurtainTop", new Vector3(0, field.y, 0), new Vector2(field.x, CurtainReach)),
            };
        }

        /// <summary>Core の単位の位置を Stage のローカル座標へ。</summary>
        public static Vector3 Local(Vec2i point) => new((float)point.X / (Field.Unit * PixelsPerUnit), (float)point.Y / (Field.Unit * PixelsPerUnit), 0);

        /// <summary>Prefab の描画物がそろい、背景がプレイ領域の大きさか。違えば理由、一致すれば null。</summary>
        public string Mismatch(VeilConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (_background == null || _player == null || _hitbox == null || _boss == null || _hpBar == null || _bombRing == null || _shotTemplate == null || _bullets == null)
            {
                return "描画物の参照が欠けています。";
            }

            if (_curtains == null || _curtains.Length != 4 || Array.Exists(_curtains, c => c == null)) { return "領域の外を覆う幕が 4 枚ありません。"; }
            if (_bossSprites == null || _bossSprites.Length != VeilConfig.BossCount || Array.Exists(_bossSprites, s => s == null))
            {
                return $"ボスの絵が {VeilConfig.BossCount} 枚ありません。";
            }

            var curtain = CurtainMismatch();
            if (curtain != null) { return curtain; }

            var bulletRenderer = _bullets.GetComponent<MeshRenderer>();
            if (bulletRenderer == null || bulletRenderer.sortingOrder != BulletOrder) { return $"弾の Mesh の描画順が {BulletOrder} ではありません。"; }
            if (_hpBar.sortingOrder != HpBarOrder) { return $"体力のバーの描画順が {HpBarOrder} ではありません。"; }

            var size = (Vector2)_background.sprite.bounds.size;
            if (Vector2.Distance(size, FieldSize) > .001f) { return $"背景の大きさ {size} がプレイ領域 {FieldSize} と違います。"; }
            if (_background.transform.localPosition != (Vector3)(FieldSize / 2)) { return "背景がプレイ領域の中央にありません。"; }
            return null;
        }

        /// <summary>幕の位置・大きさ・向き・描画順・絵・不透明さが <see cref="CurtainLayout"/> どおりか。違えば理由、一致すれば null。</summary>
        private string CurtainMismatch()
        {
            var layout = CurtainLayout();
            var bulletLayer = _bullets.GetComponent<Renderer>().sortingLayerID;
            for (var i = 0; i < layout.Length; i++)
            {
                var (name, position, size) = layout[i];
                var curtain = _curtains[i];
                var t = curtain.transform;
                if (t.parent != transform) { return $"幕 {name} が Stage の直下にありません。"; }
                if (!curtain.enabled || !curtain.gameObject.activeSelf) { return $"幕 {name} が表示されていません。"; }
                if (Vector3.Distance(t.localPosition, position) > 1e-4f) { return $"幕 {name} の位置 {t.localPosition} が {position} と違います。"; }
                if (Vector3.Distance(t.localScale, new Vector3(size.x, size.y, 1)) > 1e-4f) { return $"幕 {name} の大きさ {t.localScale} が {size} と違います。"; }
                if (t.localRotation != Quaternion.identity) { return $"幕 {name} が回っています。"; }
                if (curtain.sortingOrder != CurtainOrder || curtain.sortingLayerID != bulletLayer) { return $"幕 {name} の描画順が弾より手前（{CurtainOrder}）ではありません。"; }
                if (curtain.color.a < 1) { return $"幕 {name} が不透明ではありません。"; }
                var bounds = curtain.sprite == null ? default : curtain.sprite.bounds;
                if (curtain.sprite == null || Vector2.Distance(bounds.min, Vector2.zero) > 1e-4f || Vector2.Distance(bounds.size, Vector2.one) > 1e-4f)
                {
                    return $"幕 {name} の絵が左下を基準にした 1 単位の四角ではありません。";
                }
            }

            return null;
        }

        /// <summary>弾の Mesh を設定の容量で用意する。</summary>
        public void Prepare(VeilConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _bullets.Allocate(config.MaxBullets);
        }

        /// <summary>試合の前の表示（自機とボス 1 体目を始めの位置に置き、弾・ショット・体力のバーを出さない）。</summary>
        public void RenderInitial(VeilConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _player.enabled = true;
            _player.transform.localPosition = Local(config.PlayerStart);
            _player.color = Color.white;
            _hitbox.enabled = false;
            _boss.enabled = true;
            _boss.sprite = _bossSprites[0];
            _boss.transform.localPosition = Local(config.BossHome);
            _boss.color = Color.white;
            _hpBar.enabled = false;
            _bombRing.enabled = false;
            HideShots(0);
            VisibleShots = 0;
            _bullets.Render(null);
        }

        /// <summary>試合の状態を描く。</summary>
        public void Render(VeilRun run, VeilConfig config)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }

            _player.enabled = run.Status != RunStatus.Lost;
            _player.transform.localPosition = Local(run.Player);
            var blink = run.InvincibleRemaining > 0 && (run.InvincibleRemaining / BlinkTicks) % 2 == 1;
            _player.color = blink ? new Color(1f, 1f, 1f, .3f) : Color.white;
            _hitbox.enabled = _player.enabled && run.Slow;
            _hitbox.transform.localPosition = Local(run.Player);

            _boss.enabled = run.Status != RunStatus.Won;
            _boss.sprite = _bossSprites[run.BossIndex];
            _boss.transform.localPosition = Local(run.Boss);

            // 弾幕の間はボスを薄くし、ショットが効かないことを見せる。
            _boss.color = run.Phase == RunPhase.Pattern ? Color.white : new Color(1f, 1f, 1f, .55f);

            // 体力のバーは領域の上端に、残りの割合の長さで描く（弾幕の間は次の弾幕の満タン）。
            _hpBar.enabled = !run.IsOver;
            var ratio = (float)run.BossHp / run.CurrentPattern.Hp;
            _hpBar.transform.localScale = new Vector3(FieldSize.x * ratio, _hpBar.transform.localScale.y, 1);

            // ボムの輪は、使ってからの Tick に合わせて広がる。
            _bombRing.enabled = run.BombRemaining > 0;
            if (_bombRing.enabled)
            {
                var progress = 1f - ((float)run.BombRemaining / config.BombTicks);
                var scale = 1f + (progress * 8f);
                _bombRing.transform.localPosition = Local(run.Player);
                _bombRing.transform.localScale = new Vector3(scale, scale, 1);
                _bombRing.color = new Color(1f, 1f, 1f, 1f - (progress * .8f));
            }

            // ReadOnlyCollection を foreach で回すと列挙子が箱に入って割り当てが起きるので、番号で回す。
            var shots = run.Shots;
            for (var i = 0; i < shots.Count; i++) { Take(i).transform.localPosition = Local(shots[i].Position); }

            HideShots(shots.Count);
            VisibleShots = shots.Count;
            _bullets.Render(run.Bullets);
        }

        /// <summary><paramref name="index"/> 番目のショットの描画物（なければ見本から増やす。この Stage が持ち、壊すときに一緒に消える）。</summary>
        private SpriteRenderer Take(int index)
        {
            if (_shots.Count == 0) { _shots.Add(_shotTemplate); }
            while (_shots.Count <= index) { _shots.Add(Instantiate(_shotTemplate, _shotTemplate.transform.parent)); }
            var item = _shots[index];
            item.enabled = true;
            return item;
        }

        private void HideShots(int from)
        {
            if (_shots.Count == 0) { _shotTemplate.enabled = false; }
            for (var i = from; i < _shots.Count; i++) { _shots[i].enabled = false; }
        }
    }
}
