using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;
using TileMotion;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 平原の描画。床・自機・ボス・Orbit の刃・Pulse の輪・Lash の斬撃・Arc の稲妻は Prefab に置いた描画物、
    /// 数の多い敵・宝石・弾は <see cref="QuadLayer"/> の Mesh 1 つで描く（どれも生成スクリプトが Prefab に置き、プレイ前から Scene ビューで見える）。
    /// 1 セル = 1 ワールド単位で、Stage のローカル座標は Core の単位を <see cref="Units.Tile"/> で割った値。
    /// Tick の状態をそのまま描き、補間しない。点滅・効果の残り時間も Tick で決める。
    /// </summary>
    public sealed class ThornTideStage : MonoBehaviour
    {
        /// <summary>無敵中の点滅の 1 段の Tick 数。</summary>
        public const int BlinkTicks = 3;

        /// <summary>Pulse・Lash・Arc の効果を見せる Tick 数。</summary>
        public const int EffectTicks = 10;

        /// <summary>床の模様の 1 枚の大きさ（セル）。床はこの倍数の位置に寄せて置き、果てなく続いて見せる。</summary>
        public const int FloorTileCells = 2;

        /// <summary>アトラスの区画：宝石（小・大）と Bolt の弾。敵の区画は敵の定義アセットが持つ。</summary>
        public const int GemCell = 3;

        public const int BigGemCell = 4;

        public const int BoltCell = 5;

        /// <summary>この値以上の宝石は大きい絵で描く。</summary>
        public const int BigGemValue = 5;

        /// <summary>
        /// 区画ごとの絵に描いた半径（Core の単位。1 ピクセル = 8 単位）。生成スクリプトは既定の半径でこの大きさに描き、
        /// 描画は「設定の半径 ÷ 絵の半径」で拡縮する（設定で半径を変えても、見た目と当たりが食い違わない）。
        /// </summary>
        public static readonly int[] CellArtRadius = { 72, 56, 112, 40, 56, 40, 40, 40 };

        public const int PlayerArtRadius = 80;

        public const int BossArtRadius = 320;

        public const int BladeArtRadius = 60;

        [SerializeField] private SpriteRenderer _floor;
        [SerializeField] private SpriteRenderer _player;
        [SerializeField] private SpriteRenderer _boss;
        [SerializeField] private SpriteRenderer _bladeTemplate;
        [SerializeField] private SpriteRenderer _pulse;
        [SerializeField] private SpriteRenderer[] _lashes;
        [SerializeField] private LineRenderer _arc;
        [SerializeField] private QuadLayer _swarm;

        /// <summary>撃たれた直後の敵の色。</summary>
        [SerializeField] private Color32 _flashTint = new(255, 110, 110, 255);

        private readonly List<SpriteRenderer> _blades = new();
        private Vector3[] _arcPoints = new Vector3[TideConfig.MaxCount + 1];
        private int[] _enemyCells = Array.Empty<int>();

        public SpriteRenderer Player => _player;

        public SpriteRenderer Boss => _boss;

        public SpriteRenderer Floor => _floor;

        public SpriteRenderer Pulse => _pulse;

        public LineRenderer ArcLine => _arc;

        public QuadLayer Swarm => _swarm;

        /// <summary>今表示している刃の数。</summary>
        public int VisibleBlades { get; private set; }

        /// <summary>今表示している Lash の斬撃の数。</summary>
        public int VisibleLashes { get; private set; }

        /// <summary>刃の描画物の数（見本を含む。増やすだけで減らさない。上限は <see cref="TideConfig.MaxCount"/>）。</summary>
        public int PooledBlades => _blades.Count;

        /// <summary>Core の単位の位置を Stage のローカル座標へ。</summary>
        public static Vector3 Local(Vec2i point) => new((float)point.X / Units.Tile, (float)point.Y / Units.Tile, 0);

        /// <summary>当たりの半径 <paramref name="radius"/> を、絵の半径 <paramref name="artRadius"/> の絵で描く倍率。</summary>
        public static Vector3 ScaleFor(int radius, int artRadius)
        {
            var scale = (float)radius / artRadius;
            return new Vector3(scale, scale, 1);
        }

        /// <summary>区画 <paramref name="cell"/> の絵を半径 <paramref name="radius"/> で描く四角形の半分の大きさ（ワールド単位）。</summary>
        public static float HalfSizeFor(int cell, int radius) => QuadLayer.CellPixels / 2f / ThornTideArt.PixelsPerUnit * radius / CellArtRadius[cell];

        /// <summary>Prefab の描画物がそろっているか。違えば理由、一致すれば null。</summary>
        public string Mismatch()
        {
            if (_floor == null || _player == null || _boss == null || _bladeTemplate == null || _pulse == null || _arc == null || _swarm == null)
            {
                return "描画物の参照が欠けています。";
            }

            if (_lashes == null || _lashes.Length != 2 || Array.Exists(_lashes, l => l == null)) { return "Lash の斬撃が 2 つありません。"; }
            if (_floor.drawMode != SpriteDrawMode.Tiled) { return "床が並べて描く（Tiled）設定ではありません。"; }
            return null;
        }

        /// <summary>描く量を設定から用意する（Mesh の容量・敵の絵の区画）。</summary>
        public void Prepare(TideConfig rules, int[] enemyCells)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            _enemyCells = enemyCells ?? throw new ArgumentNullException(nameof(enemyCells));
            _swarm.Allocate(rules.MaxEnemies + rules.MaxGems + rules.MaxProjectiles);
        }

        /// <summary>試合の前の表示（自機だけを原点に置き、敵・効果を出さない）。</summary>
        public void RenderInitial(TideConfig rules, Vector3 viewCenter)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            PlaceFloor(viewCenter, rules);
            _player.enabled = true;
            _player.transform.localPosition = Vector3.zero;
            _player.transform.localScale = ScaleFor(rules.PlayerRadius, PlayerArtRadius);
            _player.flipX = false;
            _player.color = Color.white;
            _boss.enabled = false;
            _pulse.enabled = false;
            _arc.enabled = false;
            foreach (var lash in _lashes) { lash.enabled = false; }
            VisibleLashes = 0;
            HideBlades(0);
            VisibleBlades = 0;
            _swarm.Begin();
            _swarm.End();
        }

        /// <summary>試合の状態を描く。</summary>
        public void Render(TideRun run, TideConfig rules, Vector3 viewCenter)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            PlaceFloor(viewCenter, rules);

            _player.enabled = !(run.IsOver && run.End == RunEnd.Died);
            _player.transform.localPosition = Local(run.Player);
            _player.transform.localScale = ScaleFor(rules.PlayerRadius, PlayerArtRadius);
            _player.flipX = run.FacingX < 0;

            // 無敵の間は半透明で点滅する（消すと見失う。026 の知見）。
            var blink = run.IsInvincible && (run.Elapsed / BlinkTicks) % 2 == 0;
            _player.color = blink ? new Color(1f, .6f, .6f, .45f) : Color.white;

            RenderSwarm(run, rules);
            RenderBoss(run, rules);
            RenderBlades(run, rules);
            RenderEffects(run);
        }

        private void RenderSwarm(TideRun run, TideConfig rules)
        {
            _swarm.Begin();
            var white = new Color32(255, 255, 255, 255);
            var gems = run.Gems;
            for (var i = 0; i < gems.Count; i++)
            {
                var cell = gems.ValueOf(i) >= BigGemValue ? BigGemCell : GemCell;
                var radius = cell == BigGemCell ? rules.GemRadius * CellArtRadius[BigGemCell] / CellArtRadius[GemCell] : rules.GemRadius;
                _swarm.Add(Local(gems.Position(i)), cell, HalfSizeFor(cell, radius), white);
            }

            var enemies = run.Enemies;
            var boss = run.BossIndex();
            for (var i = 0; i < enemies.Count; i++)
            {
                if (i == boss) { continue; }
                var cell = _enemyCells[enemies.Def(i)];
                _swarm.Add(Local(enemies.Position(i)), cell, HalfSizeFor(cell, run.EnemyRadius(i)), enemies.IsFlashing(i) ? _flashTint : white);
            }

            var shots = run.Projectiles;
            for (var i = 0; i < shots.Count; i++) { _swarm.Add(Local(shots.Position(i)), BoltCell, HalfSizeFor(BoltCell, shots.RadiusOf(i)), white); }
            _swarm.End();
        }

        private void RenderBoss(TideRun run, TideConfig rules)
        {
            var boss = run.BossIndex();
            _boss.enabled = boss >= 0;
            if (boss < 0) { return; }
            _boss.transform.localPosition = Local(run.Enemies.Position(boss));
            _boss.transform.localScale = ScaleFor(rules.Enemies[run.Enemies.Def(boss)].Radius, BossArtRadius);
            _boss.color = run.Enemies.IsFlashing(boss) ? (Color)_flashTint : Color.white;
            _boss.flipX = run.Enemies.Position(boss).X > run.Player.X;
        }

        private void RenderBlades(TideRun run, TideConfig rules)
        {
            var shown = 0;
            for (var w = 0; w < rules.Weapons.Length; w++)
            {
                if (rules.Weapons[w].Kind != WeaponKind.Orbit) { continue; }
                var count = run.WeaponCountOf(w);
                for (var b = 0; b < count; b++)
                {
                    var blade = Take(shown++);
                    blade.transform.localPosition = Local(run.OrbitBlade(w, b));
                    blade.transform.localScale = ScaleFor(run.WeaponSizeOf(w), BladeArtRadius);
                    blade.transform.localRotation = Quaternion.Euler(0, 0, -run.Elapsed * 12f);
                }
            }

            HideBlades(shown);
            VisibleBlades = shown;
        }

        private void RenderEffects(TideRun run)
        {
            var effects = run.Effects;
            var age = run.Elapsed - effects.PulseTick;
            _pulse.enabled = effects.PulseTick >= 0 && age < EffectTicks;
            if (_pulse.enabled)
            {
                // 輪の絵は半径 1 単位（直径 2 単位）。発動から広がりながら薄くなる。
                var radius = (float)effects.PulseRange / Units.Tile;
                var grow = .85f + (.15f * age / EffectTicks);
                _pulse.transform.localPosition = Local(effects.PulseCenter);
                _pulse.transform.localScale = new Vector3(radius * grow, radius * grow, 1);
                _pulse.color = new Color(1f, 1f, 1f, 1f - ((float)age / EffectTicks));
            }

            age = run.Elapsed - effects.LashTick;
            var lashing = effects.LashTick >= 0 && age < EffectTicks;
            VisibleLashes = 0;
            for (var i = 0; i < _lashes.Length; i++)
            {
                var lash = _lashes[i];
                lash.enabled = lashing && i < effects.LashCount;
                if (!lash.enabled) { continue; }
                VisibleLashes++;
                var rect = effects.LashRect(i);

                // 斬撃の絵は 1×1 単位で中央が基準。矩形の大きさに引き伸ばす。
                lash.transform.localPosition = Local(new Vec2i(rect.MinX + (rect.Width / 2), rect.MinY + (rect.Height / 2)));
                lash.transform.localScale = new Vector3((float)rect.Width / Units.Tile, (float)rect.Height / Units.Tile, 1);
                lash.flipX = rect.MinX < run.Player.X;
                lash.color = new Color(1f, 1f, 1f, 1f - ((float)age / EffectTicks));
            }

            age = run.Elapsed - effects.ArcTick;
            _arc.enabled = effects.ArcTick >= 0 && age < EffectTicks;
            if (_arc.enabled)
            {
                for (var i = 0; i < effects.ArcCount; i++) { _arcPoints[i] = Local(effects.ArcPoint(i)); }
                _arc.positionCount = effects.ArcCount;
                _arc.SetPositions(_arcPoints);
                var alpha = 1f - ((float)age / EffectTicks);
                _arc.startColor = _arc.endColor = new Color(.75f, .9f, 1f, alpha);
            }
        }

        /// <summary>床を視野の中心の近く（模様の 1 枚の倍数の位置）に置く。模様がずれないので、果てなく続いて見える。</summary>
        private void PlaceFloor(Vector3 viewCenter, TideConfig rules)
        {
            var local = transform.InverseTransformPoint(viewCenter);
            var x = Mathf.Floor(local.x / FloorTileCells) * FloorTileCells;
            var y = Mathf.Floor(local.y / FloorTileCells) * FloorTileCells;
            _floor.transform.localPosition = new Vector3(x, y, 0);

            // 視野より模様 2 枚ぶん大きく覆う。
            _floor.size = new Vector2(rules.ViewWidthCells + (4 * FloorTileCells), rules.ViewHeightCells + (4 * FloorTileCells) + 8);
        }

        /// <summary><paramref name="index"/> 番目の刃（なければ見本から増やす。この Stage が持ち、壊すときに一緒に消える）。</summary>
        private SpriteRenderer Take(int index)
        {
            if (_blades.Count == 0) { _blades.Add(_bladeTemplate); }
            while (_blades.Count <= index) { _blades.Add(Instantiate(_bladeTemplate, _bladeTemplate.transform.parent)); }
            var blade = _blades[index];
            blade.enabled = true;
            return blade;
        }

        private void HideBlades(int from)
        {
            if (_blades.Count == 0) { _bladeTemplate.enabled = false; }
            for (var i = from; i < _blades.Count; i++) { _blades[i].enabled = false; }
        }
    }
}
