using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;
using TileMotion;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Unity
{
    /// <summary>
    /// 試合の描画。3 階の <see cref="AshenFloorView"/> と、自機・照準・攻撃の弧・薙ぎ払いの輪・突進の筋・飛び道具・地面の品の見本を持つ。
    /// 1 タイル = 1 ワールド単位で、Stage のローカル座標は Core の単位を <see cref="Units.Tile"/> で割った値。
    /// Tick の状態をそのまま描き、補間しない。点滅も Tick で決める。
    /// </summary>
    public sealed class AshenStage : MonoBehaviour
    {
        /// <summary>絵に描いた体の半径（Core の単位。1 ピクセル = 16 単位）。描画は設定の半径 ÷ 絵の半径で拡縮する。</summary>
        public const int HeroArtRadius = 96;

        public const int BlinkTicks = 4;

        /// <summary>攻撃を受けた敵を赤くする Tick 数。</summary>
        public const int FlashTicks = 6;

        [SerializeField] private AshenFloorView[] _floors = Array.Empty<AshenFloorView>();
        [SerializeField] private SpriteRenderer _hero;
        [SerializeField] private SpriteRenderer _crosshair;
        [SerializeField] private SpriteRenderer _swing;
        [SerializeField] private SpriteRenderer _spin;
        [SerializeField] private SpriteRenderer _laneTemplate;
        [SerializeField] private SpriteRenderer _projectileTemplate;
        [SerializeField] private SpriteRenderer _groundTemplate;
        [SerializeField] private Sprite _heroStand;
        [SerializeField] private Sprite _heroDown;
        [SerializeField] private Sprite _grunt;
        [SerializeField] private Sprite _archer;
        [SerializeField] private Sprite _charger;
        [SerializeField] private Sprite _boss;
        [SerializeField] private Sprite _arrow;
        [SerializeField] private Sprite _bolt;
        [SerializeField] private Sprite _potion;
        [SerializeField] private Sprite[] _slotIcons = Array.Empty<Sprite>();

        private readonly List<SpriteRenderer> _lanes = new();
        private readonly List<SpriteRenderer> _projectiles = new();
        private readonly List<SpriteRenderer> _groundItems = new();

        public SpriteRenderer Hero => _hero;

        public SpriteRenderer Crosshair => _crosshair;

        public SpriteRenderer Swing => _swing;

        public SpriteRenderer Spin => _spin;

        public IReadOnlyList<AshenFloorView> FloorViews => _floors;

        /// <summary>今表示している描画物の数（テスト・負荷の記録用）。</summary>
        public int VisibleProjectiles { get; private set; }

        public int VisibleGround { get; private set; }

        public int VisibleFoes { get; private set; }

        public int VisibleLanes { get; private set; }

        /// <summary>今まで作った描画物の数（見本を含む）。</summary>
        public int PooledRenderers => _lanes.Count + _projectiles.Count + _groundItems.Count;

        public static Vector3 Local(Vec2i point) => new((float)point.X / Units.Tile, (float)point.Y / Units.Tile, 0);

        public static Vec2i ToCore(Vector3 local) => new(Mathf.RoundToInt(local.x * Units.Tile), Mathf.RoundToInt(local.y * Units.Tile));

        public static float Angle(Vec2i direction) => direction.IsZero ? 0 : Mathf.Atan2(direction.Y, direction.X) * Mathf.Rad2Deg;

        public static Vector3 ScaleFor(int radius, int artRadius)
        {
            var scale = (float)radius / artRadius;
            return new Vector3(scale, scale, 1);
        }

        /// <summary>種類ごとの絵の半径（既定の当たりの半径で描いてある）。</summary>
        public static int ArtRadius(FoeKind kind) => kind switch
        {
            FoeKind.Grunt => 96,
            FoeKind.Archer => 80,
            FoeKind.Charger => 112,
            FoeKind.Boss => 192,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>希少度の色（名前札・地面の品・持ち物の画面で共通）。</summary>
        public static Color RarityColor(Rarity rarity) => rarity switch
        {
            Rarity.Magic => new Color(.45f, .65f, 1f),
            Rarity.Rare => new Color(1f, .85f, .3f),
            _ => new Color(.88f, .88f, .88f),
        };

        public Sprite FoeSprite(FoeKind kind) => kind switch
        {
            FoeKind.Grunt => _grunt,
            FoeKind.Archer => _archer,
            FoeKind.Charger => _charger,
            FoeKind.Boss => _boss,
            _ => null,
        };

        public Sprite SlotIcon(Slot slot) => _slotIcons.Length > (int)slot ? _slotIcons[(int)slot] : null;

        /// <summary>Prefab が地図と一致するか。違えば理由、一致すれば null。</summary>
        public string Mismatch(FloorSet floors)
        {
            if (floors == null) { throw new ArgumentNullException(nameof(floors)); }
            if (_hero == null || _crosshair == null || _swing == null || _spin == null || _laneTemplate == null || _projectileTemplate == null || _groundTemplate == null)
            {
                return "描画物の参照が欠けています。";
            }

            if (_slotIcons.Length != SlotInfo.Count) { return "枠の絵が足りません。"; }
            if (_floors.Length != floors.Floors.Count) { return $"階の描画が {_floors.Length} つです（地図は {floors.Floors.Count} 階）。"; }
            for (int i = 0; i < _floors.Length; i++)
            {
                if (_floors[i] == null) { return $"{i + 1} 階の描画がありません。"; }
                var reason = _floors[i].Mismatch(floors.Floors[i], FoeSprite);
                if (reason != null) { return reason; }
            }

            return null;
        }

        /// <summary>試合の前の表示：1 階・入口の自機・初めの位置の敵。</summary>
        public void RenderInitial(FloorSet floors, DelveConfig config)
        {
            if (floors == null) { throw new ArgumentNullException(nameof(floors)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            ShowFloor(1);
            var map = floors[1];
            _hero.enabled = true;
            _hero.sprite = _heroStand;
            _hero.color = Color.white;
            _hero.flipX = false;
            _hero.transform.localPosition = Local(map.Entrance);
            _hero.transform.localRotation = Quaternion.identity;
            _hero.transform.localScale = ScaleFor(config.HeroRadius, HeroArtRadius);
            _crosshair.enabled = false;
            _swing.enabled = false;
            _spin.enabled = false;
            var view = _floors[0];
            for (int i = 0; i < view.Foes.Count; i++)
            {
                var spawn = map.Spawns[i];
                var foe = view.Foes[i];
                foe.enabled = true;
                foe.color = Color.white;
                foe.flipX = false;
                foe.transform.localPosition = Local(spawn.Center);
                foe.transform.localScale = ScaleFor(config.Foe(spawn.Kind).Radius, ArtRadius(spawn.Kind));
            }

            Hide(_lanes, _laneTemplate, 0);
            Hide(_projectiles, _projectileTemplate, 0);
            Hide(_groundItems, _groundTemplate, 0);
            VisibleProjectiles = VisibleGround = VisibleLanes = 0;
            VisibleFoes = view.Foes.Count;
        }

        /// <summary>試合の状態を描く。<paramref name="aim"/> は照準の点（Core の単位）。</summary>
        public void Render(DelveRun run, DelveConfig config, Vec2i aim, bool showCrosshair)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            ShowFloor(run.FloorNumber);
            long tick = run.Tick;
            var hero = run.Hero;
            _hero.enabled = true;
            _hero.sprite = hero.Dead ? _heroDown : _heroStand;
            _hero.flipX = hero.Facing.X < 0;
            _hero.transform.localPosition = Local(hero.Position);
            _hero.transform.localScale = ScaleFor(config.HeroRadius, HeroArtRadius);
            bool dim = !hero.Dead && hero.Invuln > 0 && (hero.Invuln / BlinkTicks) % 2 == 1;
            _hero.color = hero.Dead ? new Color(.6f, .6f, .6f, 1f) : dim ? new Color(1f, 1f, 1f, .35f) : Color.white;
            _crosshair.enabled = showCrosshair && !run.IsOver && !hero.Dead;
            _crosshair.transform.localPosition = Local(aim);

            // 基本攻撃の弧：振り始めから当たった後の数 Tick まで、振った向きに出す。
            bool swinging = !hero.Dead && hero.SwingStartTick >= 0 && tick >= hero.SwingStartTick && tick <= hero.SwingHitTick + 4;
            _swing.enabled = swinging;
            if (swinging)
            {
                var direction = IntMath.Scale(hero.SwingDirection, config.AttackReach);
                _swing.transform.localPosition = Local(hero.Position + direction);
                _swing.transform.localRotation = Quaternion.Euler(0, 0, Angle(hero.SwingDirection));
                float size = (float)config.AttackRadius * 2 / Units.Tile;
                _swing.transform.localScale = new Vector3(size, size, 1);
                _swing.color = new Color(1f, 1f, 1f, tick >= hero.SwingHitTick ? .9f : .35f);
            }

            bool spinning = !hero.Dead && hero.SpinStartTick >= 0 && tick >= hero.SpinStartTick && tick <= hero.SpinHitTick + 6;
            _spin.enabled = spinning;
            if (spinning)
            {
                _spin.transform.localPosition = Local(hero.Position);
                float size = (float)config.SkillRadius * 2 / Units.Tile;
                _spin.transform.localScale = new Vector3(size, size, 1);
                _spin.transform.localRotation = Quaternion.Euler(0, 0, -(tick - hero.SpinStartTick) * 24);
                _spin.color = new Color(1f, 1f, 1f, tick >= hero.SpinHitTick ? .9f : .3f);
            }

            var view = _floors[run.FloorNumber - 1];
            int visible = 0;
            int lanes = 0;
            for (int i = 0; i < view.Foes.Count; i++)
            {
                var renderer = view.Foes[i];
                var foe = run.Foes[i];
                renderer.enabled = !foe.Dead;
                if (foe.Dead) { continue; }
                visible++;
                renderer.transform.localPosition = Local(foe.Position);
                renderer.transform.localScale = ScaleFor(config.Foe(foe.Kind).Radius, ArtRadius(foe.Kind));
                renderer.flipX = hero.Position.X < foe.Position.X;
                renderer.color = FoeTint(foe, tick);
                if (foe.State == FoeState.ChargeWindup)
                {
                    var stats = config.Foe(foe.Kind);
                    var lane = Take(_lanes, _laneTemplate, lanes++);
                    float length = (float)stats.ChargeSpeed * stats.ChargeTicks / Units.Tile;
                    var direction = new Vector3(foe.Direction.X, foe.Direction.Y, 0).normalized;
                    lane.transform.localPosition = Local(foe.Position) + (direction * (length / 2));
                    lane.transform.localRotation = Quaternion.Euler(0, 0, Angle(foe.Direction));
                    lane.transform.localScale = new Vector3(length, (float)stats.Radius * 2 / Units.Tile, 1);
                }
            }

            int projectiles = 0;
            foreach (var p in run.Projectiles)
            {
                var item = Take(_projectiles, _projectileTemplate, projectiles++);
                item.sprite = p.Element == Element.Fire ? _bolt : _arrow;
                item.transform.localPosition = Local(p.Position);
                item.transform.localRotation = Quaternion.Euler(0, 0, Angle(p.Velocity));
            }

            int ground = 0;
            foreach (var g in run.Ground)
            {
                var item = Take(_groundItems, _groundTemplate, ground++);
                item.sprite = g.IsPotion ? _potion : SlotIcon(g.Item.Slot);
                item.color = g.IsPotion ? Color.white : RarityColor(g.Item.Rarity);
                item.transform.localPosition = Local(g.Position);
            }

            Hide(_lanes, _laneTemplate, lanes);
            Hide(_projectiles, _projectileTemplate, projectiles);
            Hide(_groundItems, _groundTemplate, ground);
            VisibleFoes = visible;
            VisibleLanes = lanes;
            VisibleProjectiles = projectiles;
            VisibleGround = ground;
        }

        /// <summary>敵の色：受けた直後は赤、構え（攻撃の予告）は黄、それ以外は白。</summary>
        public static Color FoeTint(Foe foe, long tick)
        {
            if (foe == null) { throw new ArgumentNullException(nameof(foe)); }
            if (tick - foe.LastHitTick < FlashTicks) { return new Color(1f, .35f, .35f, 1f); }
            if (foe.State == FoeState.Windup || foe.State == FoeState.ChargeWindup) { return new Color(1f, .9f, .45f, 1f); }
            return Color.white;
        }

        private void ShowFloor(int number)
        {
            for (int i = 0; i < _floors.Length; i++)
            {
                bool active = i == number - 1;
                if (_floors[i].gameObject.activeSelf != active) { _floors[i].gameObject.SetActive(active); }
            }
        }

        private static SpriteRenderer Take(List<SpriteRenderer> pool, SpriteRenderer template, int index)
        {
            if (pool.Count == 0) { pool.Add(template); }
            while (pool.Count <= index) { pool.Add(Instantiate(template, template.transform.parent)); }
            var item = pool[index];
            item.enabled = true;
            return item;
        }

        private static void Hide(List<SpriteRenderer> pool, SpriteRenderer template, int from)
        {
            if (pool.Count == 0) { template.enabled = false; }
            for (int i = from; i < pool.Count; i++) { pool[i].enabled = false; }
        }
    }
}
