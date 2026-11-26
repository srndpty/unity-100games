using System;
using Game.App;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 戦いの場（背景・自機・敵の立ち位置 3 つ・対象の印）。Prefab が配置の正で、シーン生成と照合（<see cref="Mismatch"/>）は同じ位置の計算を使う。
    /// 描くのは App の卓（<see cref="TableModel"/>）と演出の再生位置だけで、Core の戦闘は読まない。
    /// </summary>
    public sealed class LedgerStage : MonoBehaviour
    {
        public const int SlotCount = 3;

        /// <summary>立ち位置の高さ（足元）。下の手札の帯より上に立つ。</summary>
        public const float GroundY = -1.2f;

        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private SpriteRenderer _player;
        [SerializeField] private SpriteRenderer _marker;
        [SerializeField] private SpriteRenderer[] _slots = Array.Empty<SpriteRenderer>();
        [SerializeField] private Sprite[] _enemySprites = Array.Empty<Sprite>();

        private readonly Seat[] _seats = new Seat[SlotCount];

        public static Vector3 PlayerPosition => new(-5.4f, GroundY, 0);

        public SpriteRenderer Player => _player;

        public SpriteRenderer Marker => _marker;

        public SpriteRenderer Background => _background;

        /// <summary>敵の立ち位置（足元）。</summary>
        public static Vector3 SlotPosition(int slot) => new(2.0f + (slot * 2.75f), GroundY, 0);

        public SpriteRenderer Slot(int slot) => _slots[slot];

        /// <summary>今の描画で立ち位置 slot にいる席（なければ null）。</summary>
        public Seat SeatAt(int slot) => _seats[slot];

        public Sprite EnemySprite(EnemyDef def) => _enemySprites[def.Index];

        /// <summary>Prefab がデータと配置に合っているか。合っていなければ理由。</summary>
        public string Mismatch(GameData data)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            if (_background == null || _player == null || _marker == null) { return "背景・自機・印のどれかがない"; }
            if (_slots.Length != SlotCount) { return $"立ち位置が {_slots.Length} 個（{SlotCount} 個のはず）"; }
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i] == null) { return $"立ち位置 {i} がない"; }
                if ((_slots[i].transform.localPosition - SlotPosition(i)).sqrMagnitude > 1e-6f) { return $"立ち位置 {i} の位置が {_slots[i].transform.localPosition}"; }
            }

            if ((_player.transform.localPosition - PlayerPosition).sqrMagnitude > 1e-6f) { return $"自機の位置が {_player.transform.localPosition}"; }
            if (_enemySprites.Length != data.Enemies.Count) { return $"敵の絵が {_enemySprites.Length} 枚（データは {data.Enemies.Count} 種）"; }
            for (int i = 0; i < data.Enemies.Count; i++)
            {
                if (_enemySprites[i] == null || _enemySprites[i].name != data.Enemies[i].Id) { return $"敵の絵 {i} が {data.Enemies[i].Id} ではない"; }
            }

            return null;
        }

        /// <summary>
        /// 席を立ち位置へ割り当てる。席は番号の位置（1 番 → 0）に立ち、そこが埋まっていれば（呼ばれた敵）倒れた敵の位置か空いた位置の若い方へ。
        /// 倒れた敵の位置に後ろの敵が詰めて動くことはない。卓だけから決まるので、HUD の重ね表示も同じ割り当てを使う。
        /// </summary>
        public static void Assign(TableModel table, Seat[] slots)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            Array.Clear(slots, 0, slots.Length);
            for (int i = 1; i < table.Seats.Count; i++)
            {
                var seat = table.Seats[i];
                var slot = i - 1 < slots.Length && slots[i - 1] == null ? i - 1 : -1;
                for (int s = 0; s < slots.Length && slot < 0; s++)
                {
                    if (slots[s] == null || !slots[s].Alive) { slot = s; }
                }

                if (slot >= 0) { slots[slot] = seat; }
            }
        }

        /// <summary>タイトル・ルートなど戦闘の外：自機だけを立たせる（前の戦闘の姿を残さない）。</summary>
        public void RenderIdle()
        {
            Array.Clear(_seats, 0, _seats.Length);
            _player.transform.localPosition = PlayerPosition;
            _player.color = Color.white;
            _player.enabled = true;
            _marker.enabled = false;
            foreach (var s in _slots)
            {
                s.enabled = false;
                s.transform.localPosition = SlotPosition(Array.IndexOf(_slots, s));
            }
        }

        /// <summary>戦闘：卓と演出の再生位置から描く。</summary>
        public void Render(EventPlayback playback, PlaybackTiming timing, int? target)
        {
            if (playback == null) { throw new ArgumentNullException(nameof(playback)); }
            if (timing == null) { throw new ArgumentNullException(nameof(timing)); }
            var table = playback.Table;
            Assign(table, _seats);
            var current = playback.Current;
            var age = playback.Age;
            _player.enabled = true;
            _player.transform.localPosition = PlayerPosition + Offset(table.Player, current, age, timing, +1);
            _player.color = Tint(table.Player, current, age, timing);
            _marker.enabled = false;
            for (int i = 0; i < SlotCount; i++)
            {
                var renderer = _slots[i];
                var seat = _seats[i];
                var dying = seat != null && current is { Kind: EventKind.Died } d && d.Target == seat.Id;
                if (seat == null || (!seat.Alive && !dying))
                {
                    renderer.enabled = false;
                    continue;
                }

                renderer.enabled = true;
                renderer.sprite = EnemySprite(seat.Def);
                renderer.transform.localPosition = SlotPosition(i) + Offset(seat, current, age, timing, -1);
                var color = Tint(seat, current, age, timing);
                if (dying) { color.a = 1f - Mathf.Clamp01(age / (float)Mathf.Max(1, timing.TicksOf(current.Value))); }
                if (current is { Kind: EventKind.Summoned } s && s.Target == seat.Id) { color.a = Mathf.Clamp01(age / (float)Mathf.Max(1, timing.TicksOf(s))); }
                renderer.color = color;
                if (target == seat.Id && seat.Alive)
                {
                    _marker.enabled = true;
                    _marker.transform.localPosition = SlotPosition(i) + new Vector3(0, -.1f, 0);
                }
            }
        }

        /// <summary>行動の踏み込み（出した者）と被弾の揺れ（受けた者）。direction は踏み込む向き。</summary>
        private static Vector3 Offset(Seat seat, BattleEvent? current, int age, PlaybackTiming timing, int direction)
        {
            if (current == null) { return Vector3.zero; }
            var e = current.Value;
            var length = Mathf.Max(1, timing.TicksOf(e));
            var t = Mathf.Clamp01(age / (float)length);
            if ((e.Kind == EventKind.EnemyActed && e.Source == seat.Id) || (e.Kind == EventKind.CardPlayed && seat.Id == 0))
            {
                return new Vector3(direction * .45f * Mathf.Sin(t * Mathf.PI), 0, 0);
            }

            if ((e.Kind is EventKind.Damaged or EventKind.PoisonTick) && e.Target == seat.Id && e.Amount > 0 && age < 10)
            {
                return new Vector3((age % 2 == 0 ? .08f : -.08f) * (1f - t), 0, 0);
            }

            return Vector3.zero;
        }

        private static Color Tint(Seat seat, BattleEvent? current, int age, PlaybackTiming timing)
        {
            if (current == null) { return Color.white; }
            var e = current.Value;
            var length = Mathf.Max(1, timing.TicksOf(e));
            var t = Mathf.Clamp01(age / (float)length);
            if (e.Kind == EventKind.Damaged && e.Target == seat.Id && e.Amount > 0) { return Color.Lerp(new Color(1f, .35f, .3f), Color.white, t); }
            if (e.Kind == EventKind.PoisonTick && e.Target == seat.Id) { return Color.Lerp(new Color(.5f, 1f, .4f), Color.white, t); }
            if (e.Kind == EventKind.BlockGained && e.Target == seat.Id) { return Color.Lerp(new Color(.55f, .75f, 1f), Color.white, t); }
            return Color.white;
        }
    }
}
