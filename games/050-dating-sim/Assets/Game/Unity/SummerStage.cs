#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core.Data;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 背景と立ち絵 1 枠の Stage（Prefab）。画面の流れの表示状態を写すだけで、自分では状態を持たない。
    /// 素材の並びと名前は台本の背景・人物・表情の表と照合する（<see cref="Mismatch"/>）。
    /// </summary>
    public sealed class SummerStage : MonoBehaviour
    {
        /// <summary>立ち絵の位置（テキスト枠の上に胸から上が見える）。</summary>
        public static readonly Vector3 PortraitPosition = new(0f, -0.6f, 0f);

        public const float PortraitScale = 1.25f;

        /// <summary>タイトル・操作説明で見せる背景。</summary>
        public const string TitleBackground = "roof";

        [SerializeField] private SpriteRenderer? _background;
        [SerializeField] private SpriteRenderer? _portrait;
        [SerializeField] private string[] _backgroundIds = Array.Empty<string>();
        [SerializeField] private Sprite[] _backgrounds = Array.Empty<Sprite>();
        [SerializeField] private string[] _portraitIds = Array.Empty<string>();
        [SerializeField] private Sprite[] _portraits = Array.Empty<Sprite>();

        public SpriteRenderer Background => _background!;

        public SpriteRenderer Portrait => _portrait!;

        public static string PortraitId(string character, string face) => character + "_" + face;

        /// <summary>台本の立ち絵の表（人物 × 表情、宣言の順）。</summary>
        public static IReadOnlyList<(string Character, string Face)> PortraitsOf(SummerBook book)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            var list = new List<(string, string)>();
            foreach (var c in book.Characters)
            {
                foreach (var f in c.Faces) { list.Add((c.Id, f)); }
            }

            return list;
        }

        public void Show(SummerFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var bg = flow.Screen is FlowScreen.Title or FlowScreen.HowTo ? TitleBackground : flow.Background;
            _background!.sprite = Find(_backgroundIds, _backgrounds, bg);
            _background.enabled = _background.sprite != null;
            var speaker = flow.InText ? flow.SpeakerId : "";
            var sprite = speaker.Length == 0 ? null : Find(_portraitIds, _portraits, PortraitId(speaker, flow.Face));
            _portrait!.sprite = sprite;
            _portrait.enabled = sprite != null;
        }

        /// <summary>Prefab が台本の素材の表と合わなければ理由を返す。</summary>
        public string? Mismatch(SummerBook book)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            if (_background == null) { return "背景の SpriteRenderer がない"; }
            if (_portrait == null) { return "立ち絵の SpriteRenderer がない"; }
            if ((_portrait.transform.localPosition - PortraitPosition).sqrMagnitude > 1e-6f) { return $"立ち絵の位置が {_portrait.transform.localPosition}"; }
            if (_portrait.sortingOrder <= _background.sortingOrder) { return "立ち絵が背景より奥に描かれる"; }

            var bgs = book.Backgrounds;
            if (_backgroundIds.Length != bgs.Count || _backgrounds.Length != bgs.Count) { return $"背景の数が {_backgroundIds.Length}/{_backgrounds.Length}（{bgs.Count}）"; }
            for (int i = 0; i < bgs.Count; i++)
            {
                if (_backgroundIds[i] != bgs[i].Id) { return $"背景 {i} の ID が {_backgroundIds[i]}（{bgs[i].Id}）"; }
                if (_backgrounds[i] == null || _backgrounds[i].name != "BG_" + bgs[i].Id) { return $"背景 {bgs[i].Id} の絵が違う"; }
            }

            var ps = PortraitsOf(book);
            if (_portraitIds.Length != ps.Count || _portraits.Length != ps.Count) { return $"立ち絵の数が {_portraitIds.Length}/{_portraits.Length}（{ps.Count}）"; }
            for (int i = 0; i < ps.Count; i++)
            {
                var id = PortraitId(ps[i].Character, ps[i].Face);
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
