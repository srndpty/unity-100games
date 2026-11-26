#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Core.Runtime;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 背景と立ち絵 3 枠の Stage（Prefab）。実行器の表示状態を写すだけで、自分では状態を持たない。
    /// 素材の並びと名前は <see cref="Scenario"/> の表と照合する（<see cref="Mismatch"/>）。
    /// </summary>
    public sealed class NovelStage : MonoBehaviour
    {
        /// <summary>立ち絵の枠の位置（left・center・right）。</summary>
        public static readonly Vector3[] SlotPositions = { new(-5.6f, -1.4f, 0f), new(0f, -1.4f, 0f), new(5.6f, -1.4f, 0f) };

        public const float PortraitScale = 1.2f;

        /// <summary>タイトル・操作説明で見せる背景。</summary>
        public const string TitleBackground = "lighthouse";

        /// <summary>結果画面で見せる背景。</summary>
        public const string ResultBackground = "dawn_sea";

        [SerializeField] private SpriteRenderer? _background;
        [SerializeField] private SpriteRenderer[] _slots = Array.Empty<SpriteRenderer>();
        [SerializeField] private string[] _backgroundIds = Array.Empty<string>();
        [SerializeField] private Sprite[] _backgrounds = Array.Empty<Sprite>();
        [SerializeField] private string[] _portraitIds = Array.Empty<string>();
        [SerializeField] private Sprite[] _portraits = Array.Empty<Sprite>();

        public SpriteRenderer Background => _background!;

        public IReadOnlyList<SpriteRenderer> Slots => _slots;

        public static string PortraitId(string character, string expression) => character + "_" + expression;

        public void Show(NovelFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var bg = flow.Screen switch
            {
                FlowScreen.Title or FlowScreen.HowTo => TitleBackground,
                FlowScreen.Result => ResultBackground,
                _ => flow.Background,
            };
            _background!.sprite = Find(_backgroundIds, _backgrounds, bg);
            _background.enabled = _background.sprite != null;
            for (int i = 0; i < _slots.Length; i++)
            {
                var (character, expression) = flow.Portrait((StageSlot)i);
                var sprite = character.Length == 0 ? null : Find(_portraitIds, _portraits, PortraitId(character, expression));
                _slots[i].sprite = sprite;
                _slots[i].enabled = sprite != null;
            }
        }

        /// <summary>Prefab がシナリオの素材の表と合わなければ理由を返す。</summary>
        public string? Mismatch()
        {
            if (_background == null) { return "背景の SpriteRenderer がない"; }
            if (_slots.Length != ScriptRunner.SlotCount) { return $"立ち絵の枠が {_slots.Length}（{ScriptRunner.SlotCount}）"; }
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null) { return $"立ち絵の枠 {i} がない"; }
                if ((_slots[i].transform.localPosition - SlotPositions[i]).sqrMagnitude > 1e-6f) { return $"立ち絵の枠 {i} の位置が {_slots[i].transform.localPosition}"; }
                if (_slots[i].sortingOrder <= _background.sortingOrder) { return $"立ち絵の枠 {i} が背景より奥に描かれる"; }
            }

            var bgs = Scenario.Backgrounds;
            if (_backgroundIds.Length != bgs.Count || _backgrounds.Length != bgs.Count) { return $"背景の数が {_backgroundIds.Length}/{_backgrounds.Length}（{bgs.Count}）"; }
            for (int i = 0; i < bgs.Count; i++)
            {
                if (_backgroundIds[i] != bgs[i]) { return $"背景 {i} の ID が {_backgroundIds[i]}（{bgs[i]}）"; }
                if (_backgrounds[i] == null || _backgrounds[i].name != "BG_" + bgs[i]) { return $"背景 {bgs[i]} の絵が違う"; }
            }

            var ps = Scenario.Portraits;
            if (_portraitIds.Length != ps.Count || _portraits.Length != ps.Count) { return $"立ち絵の数が {_portraitIds.Length}/{_portraits.Length}（{ps.Count}）"; }
            for (int i = 0; i < ps.Count; i++)
            {
                var id = PortraitId(ps[i].Key, ps[i].Value);
                if (_portraitIds[i] != id) { return $"立ち絵 {i} の ID が {_portraitIds[i]}（{id}）"; }
                if (_portraits[i] == null || _portraits[i].name != "Portrait_" + id) { return $"立ち絵 {id} の絵が違う"; }
            }

            return null;
        }

        private static Sprite? Find(string[] ids, Sprite[] sprites, string id)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == id) { return sprites[i]; }
            }

            return null;
        }
    }
}
