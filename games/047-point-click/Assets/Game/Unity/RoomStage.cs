#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core.Rules;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>部屋の座標（1920 × 1080 の整数）とワールドの座標（1 単位 = 100）の対応。Stage・シーン生成・照合で共有する。</summary>
    public static class StageLayout
    {
        public const float UnitsPerRoomPixel = 0.01f;

        /// <summary>床（足もと）の高さ（部屋の座標）。</summary>
        public const int FloorY = 230;

        /// <summary>部屋の 1 画面の幅と高さ（ワールドの単位）。</summary>
        public const float RoomWidth = 19.2f;

        public const float RoomHeight = 10.8f;

        public static Vector3 Center(IntGeometry.Rect r) =>
            new((r.MinX + r.MaxX) * 0.5f * UnitsPerRoomPixel, (r.MinY + r.MaxY) * 0.5f * UnitsPerRoomPixel, 0f);

        public static Vector2 Size(IntGeometry.Rect r) => new(r.Width * UnitsPerRoomPixel, r.Height * UnitsPerRoomPixel);

        public static Vector3 Feet(int x) => new(x * UnitsPerRoomPixel, FloorY * UnitsPerRoomPixel, 0f);

        /// <summary>ワールドの点を部屋の座標にする（四捨五入）。</summary>
        public static Vector2Int RoomPoint(Vector3 world) =>
            new(Mathf.RoundToInt(world.x / UnitsPerRoomPixel), Mathf.RoundToInt(world.y / UnitsPerRoomPixel));

        /// <summary>スプライトを矩形にちょうど合わせる大きさ（localScale）。</summary>
        public static Vector3 FitScale(Sprite sprite, IntGeometry.Rect r)
        {
            var size = Size(r);
            var b = sprite.bounds.size;
            return new Vector3(size.x / b.x, size.y / b.y, 1f);
        }
    }

    /// <summary>
    /// 3 部屋の Stage（Prefab）：部屋ごとの背景と、ルール表の物・人物・出口ごとのスプライト、主人公、灯ったランプ、持ち物の絵。
    /// Flow の状態を写すだけで、自分では状態を持たない。物の位置と大きさはルール表の矩形と照合する（<see cref="Mismatch"/>、023）。
    /// </summary>
    public sealed class RoomStage : MonoBehaviour
    {
        public const int PlayerOrder = 100;

        /// <summary>歩きの絵を切り替える Tick。</summary>
        public const int WalkFrameTicks = 8;

        [SerializeField] private string[] _roomIds = Array.Empty<string>();
        [SerializeField] private Transform[] _roomRoots = Array.Empty<Transform>();
        [SerializeField] private SpriteRenderer[] _backgrounds = Array.Empty<SpriteRenderer>();
        [SerializeField] private string[] _hotspotIds = Array.Empty<string>();
        [SerializeField] private SpriteRenderer[] _hotspots = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer? _player;
        [SerializeField] private Sprite[] _playerFrames = Array.Empty<Sprite>();
        [SerializeField] private SpriteRenderer? _lampLit;
        [SerializeField] private string[] _itemIds = Array.Empty<string>();
        [SerializeField] private Sprite[] _itemIcons = Array.Empty<Sprite>();

        public SpriteRenderer Player => _player!;

        public SpriteRenderer LampLit => _lampLit!;

        public IReadOnlyList<SpriteRenderer> Hotspots => _hotspots;

        public IReadOnlyList<Transform> RoomRoots => _roomRoots;

        public Sprite? ItemIcon(string id)
        {
            for (int i = 0; i < _itemIds.Length; i++)
            {
                if (_itemIds[i] == id) { return _itemIcons[i]; }
            }

            return null;
        }

        /// <summary>灯ったランプを出す謎（脱出の謎）。</summary>
        public static int LampPuzzle(RuleBook book) => book?.Goal ?? throw new ArgumentNullException(nameof(book));

        public void Show(AdventureFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var room = flow.Screen is FlowScreen.Title or FlowScreen.HowTo ? flow.Book.StartRoom : flow.Room;
            for (int i = 0; i < _roomRoots.Length; i++)
            {
                var active = i == room;
                if (_roomRoots[i].gameObject.activeSelf != active) { _roomRoots[i].gameObject.SetActive(active); }
            }

            for (int i = 0; i < _hotspots.Length; i++) { _hotspots[i].enabled = flow.Visible(i); }
            _lampLit!.enabled = flow.Solved(LampPuzzle(flow.Book));

            var playing = flow.Screen is FlowScreen.Playing or FlowScreen.Paused or FlowScreen.Result;
            _player!.enabled = playing;
            if (!playing) { return; }
            _player.transform.localPosition = StageLayout.Feet(flow.PlayerX);
            _player.flipX = flow.Facing < 0;
            _player.sprite = flow.IsWalking ? _playerFrames[1 + (int)(flow.WorldTick / WalkFrameTicks % 2)] : _playerFrames[0];
        }

        /// <summary>Prefab がルール表と合わなければ理由を返す（部屋・物の ID と並び、物の位置と大きさ、描画順、持ち物の絵）。</summary>
        public string? Mismatch(RuleBook book)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            if (_roomIds.Length != book.Rooms.Count || _roomRoots.Length != book.Rooms.Count || _backgrounds.Length != book.Rooms.Count)
            {
                return $"部屋の数が {_roomIds.Length}/{_roomRoots.Length}/{_backgrounds.Length}（{book.Rooms.Count}）";
            }

            for (int i = 0; i < book.Rooms.Count; i++)
            {
                if (_roomIds[i] != book.Rooms[i].Id) { return $"部屋 {i} の ID が {_roomIds[i]}（{book.Rooms[i].Id}）"; }
                if (_roomRoots[i] == null || _backgrounds[i] == null || _backgrounds[i].sprite == null) { return $"部屋 {_roomIds[i]} の背景がない"; }
                if (_backgrounds[i].transform.parent != _roomRoots[i]) { return $"部屋 {_roomIds[i]} の背景が部屋の下にない"; }
            }

            if (_hotspotIds.Length != book.Hotspots.Count || _hotspots.Length != book.Hotspots.Count)
            {
                return $"物の数が {_hotspotIds.Length}/{_hotspots.Length}（{book.Hotspots.Count}）";
            }

            for (int i = 0; i < book.Hotspots.Count; i++)
            {
                var h = book.Hotspots[i];
                var r = _hotspots[i];
                if (_hotspotIds[i] != h.Id) { return $"物 {i} の ID が {_hotspotIds[i]}（{h.Id}）"; }
                if (r == null || r.sprite == null) { return $"物 {h.Id} の絵がない"; }
                if (r.transform.parent != _roomRoots[h.Room]) { return $"物 {h.Id} が部屋 {book.Rooms[h.Room].Id} の下にない"; }
                if ((r.transform.localPosition - StageLayout.Center(h.Area)).sqrMagnitude > 1e-6f) { return $"物 {h.Id} の位置が {r.transform.localPosition}（{StageLayout.Center(h.Area)}）"; }
                var size = Vector3.Scale(r.sprite.bounds.size, r.transform.localScale);
                var want = StageLayout.Size(h.Area);
                if (Mathf.Abs(size.x - want.x) > 1e-3f || Mathf.Abs(size.y - want.y) > 1e-3f) { return $"物 {h.Id} の大きさが {size}（{want}）"; }
                if (r.sortingOrder <= _backgrounds[h.Room].sortingOrder) { return $"物 {h.Id} が背景より奥に描かれる"; }
                if (i > 0 && book.Hotspots[i - 1].Room == h.Room && r.sortingOrder <= _hotspots[i - 1].sortingOrder) { return $"物 {h.Id} が前の物より奥に描かれる（後に書いた物が手前）"; }
                if (r.sortingOrder >= PlayerOrder) { return $"物 {h.Id} が主人公より手前に描かれる"; }
            }

            if (_player == null || _player.sortingOrder != PlayerOrder) { return "主人公がない、または描画順が違う"; }
            if (_playerFrames.Length != 3 || Array.Exists(_playerFrames, f => f == null)) { return "主人公の絵が 3 枚（立ち・歩き 2）でない"; }
            var lamp = book.Hotspots[book.HotspotIndex("lighthouse.lamp")];
            if (_lampLit == null || _lampLit.transform.parent != _roomRoots[lamp.Room]) { return "灯ったランプが灯室の下にない"; }
            if ((_lampLit.transform.localPosition - StageLayout.Center(lamp.Area)).sqrMagnitude > 1e-6f) { return "灯ったランプの位置がランプと違う"; }
            if (_itemIds.Length != book.Items.Count || _itemIcons.Length != book.Items.Count) { return $"持ち物の絵の数が {_itemIds.Length}/{_itemIcons.Length}（{book.Items.Count}）"; }
            for (int i = 0; i < book.Items.Count; i++)
            {
                if (_itemIds[i] != book.Items[i].Id || _itemIcons[i] == null) { return $"持ち物 {book.Items[i].Id} の絵が違う"; }
            }

            return null;
        }
    }
}
