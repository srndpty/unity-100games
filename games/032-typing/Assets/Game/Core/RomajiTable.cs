using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// かなの単位（1 かな、または拗音などの 2 かな）と、そのローマ字の綴りの表。
    /// <para>
    /// 各単位の綴りは先頭が案内に使う既定の綴り。「ん」の単独の <c>n</c> と「っ」の子音の重ねは、
    /// 次の単位の綴りによって決まるので、この表ではなく <see cref="Reading"/> が組み立てる。
    /// </para>
    /// </summary>
    public static class RomajiTable
    {
        /// <summary>受け付けるキー（小文字の英字と長音の <c>-</c>）。</summary>
        public const string Keys = "abcdefghijklmnopqrstuvwxyz-";

        private static readonly Dictionary<string, string[]> _units = Build();

        /// <summary>受け付けるキーか。</summary>
        public static bool IsKey(char key) => Keys.Contains(key);

        /// <summary>かなの単位の綴り（先頭が既定）。表にない単位なら空。</summary>
        public static IReadOnlyList<string> SpellingsOf(string unit)
        {
            if (unit == null) { throw new ArgumentNullException(nameof(unit)); }
            return _units.TryGetValue(unit, out var spellings) ? Array.AsReadOnly(spellings) : Array.Empty<string>();
        }

        /// <summary>表にある単位か。</summary>
        public static bool IsUnit(string unit) => unit != null && _units.ContainsKey(unit);

        private static Dictionary<string, string[]> Build()
        {
            var table = new Dictionary<string, string[]>(StringComparer.Ordinal);

            void Add(string kana, params string[] spellings) => table.Add(kana, spellings);

            Add("あ", "a"); Add("い", "i", "yi"); Add("う", "u", "wu", "whu"); Add("え", "e"); Add("お", "o");
            Add("か", "ka", "ca"); Add("き", "ki"); Add("く", "ku", "cu", "qu"); Add("け", "ke"); Add("こ", "ko", "co");
            Add("さ", "sa"); Add("し", "shi", "si", "ci"); Add("す", "su"); Add("せ", "se", "ce"); Add("そ", "so");
            Add("た", "ta"); Add("ち", "chi", "ti"); Add("つ", "tsu", "tu"); Add("て", "te"); Add("と", "to");
            Add("な", "na"); Add("に", "ni"); Add("ぬ", "nu"); Add("ね", "ne"); Add("の", "no");
            Add("は", "ha"); Add("ひ", "hi"); Add("ふ", "fu", "hu"); Add("へ", "he"); Add("ほ", "ho");
            Add("ま", "ma"); Add("み", "mi"); Add("む", "mu"); Add("め", "me"); Add("も", "mo");
            Add("や", "ya"); Add("ゆ", "yu"); Add("よ", "yo");
            Add("ら", "ra"); Add("り", "ri"); Add("る", "ru"); Add("れ", "re"); Add("ろ", "ro");
            Add("わ", "wa"); Add("を", "wo");
            Add("が", "ga"); Add("ぎ", "gi"); Add("ぐ", "gu"); Add("げ", "ge"); Add("ご", "go");
            Add("ざ", "za"); Add("じ", "ji", "zi"); Add("ず", "zu"); Add("ぜ", "ze"); Add("ぞ", "zo");
            Add("だ", "da"); Add("ぢ", "di"); Add("づ", "du"); Add("で", "de"); Add("ど", "do");
            Add("ば", "ba"); Add("び", "bi"); Add("ぶ", "bu"); Add("べ", "be"); Add("ぼ", "bo");
            Add("ぱ", "pa"); Add("ぴ", "pi"); Add("ぷ", "pu"); Add("ぺ", "pe"); Add("ぽ", "po");

            // 小書き・撥音・促音・長音。「ん」の単独の n と「っ」の子音の重ねは Reading が次の単位から作る。
            Add("ぁ", "xa", "la"); Add("ぃ", "xi", "li"); Add("ぅ", "xu", "lu"); Add("ぇ", "xe", "le"); Add("ぉ", "xo", "lo");
            Add("ゃ", "xya", "lya"); Add("ゅ", "xyu", "lyu"); Add("ょ", "xyo", "lyo"); Add("ゎ", "xwa", "lwa");
            Add("ん", "nn", "xn");
            Add("っ", "xtu", "ltu", "xtsu", "ltsu");
            Add("ー", "-");

            // 拗音（子音＋ゃゅょ）。
            void Yoon(string i, string a, string u, string o, params string[] heads)
            {
                Add(i + "ゃ", Spell(heads, a));
                Add(i + "ゅ", Spell(heads, u));
                Add(i + "ょ", Spell(heads, o));
            }

            Yoon("き", "a", "u", "o", "ky");
            Yoon("ぎ", "a", "u", "o", "gy");
            Yoon("し", "a", "u", "o", "sh", "sy");
            Yoon("じ", "a", "u", "o", "j", "zy", "jy");
            Yoon("ち", "a", "u", "o", "ch", "ty", "cy");
            Yoon("ぢ", "a", "u", "o", "dy");
            Yoon("に", "a", "u", "o", "ny");
            Yoon("ひ", "a", "u", "o", "hy");
            Yoon("び", "a", "u", "o", "by");
            Yoon("ぴ", "a", "u", "o", "py");
            Yoon("み", "a", "u", "o", "my");
            Yoon("り", "a", "u", "o", "ry");
            Add("しぇ", "she", "sye"); Add("じぇ", "je", "zye", "jye"); Add("ちぇ", "che", "tye", "cye");
            Add("ふぁ", "fa"); Add("ふぃ", "fi"); Add("ふぇ", "fe"); Add("ふぉ", "fo");
            Add("てぃ", "thi"); Add("でぃ", "dhi"); Add("うぃ", "wi"); Add("うぇ", "we");
            return table;
        }

        private static string[] Spell(string[] heads, string vowel)
        {
            var spellings = new string[heads.Length];
            for (var i = 0; i < heads.Length; i++) { spellings[i] = heads[i] + vowel; }
            return spellings;
        }
    }
}
