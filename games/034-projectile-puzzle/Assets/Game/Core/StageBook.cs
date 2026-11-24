using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Game.Core
{
    /// <summary>
    /// 面の集まり（<c>Assets/Game/Data/Stages.txt</c>）。行の書式（長さは m、小数可）：
    /// <code>
    /// stage 名前
    /// hint 案内文
    /// launcher x y
    /// shots normal heavy bomb    # 使う順
    /// block 素材 x y 幅 高さ       # 素材は wood / stone / ice / lantern、x y は中心。書いた順に 0 から番号
    /// joint a b 外れる力
    /// end
    /// </code>
    /// <c>#</c> から行末と空行は無視する。
    /// </summary>
    public sealed class StageBook
    {
        /// <summary>本番の面の数。</summary>
        public const int ProductionCount = 5;

        private static readonly char[] _separators = { ' ', '\t' };

        private StageBook(IList<Stage> stages) => Stages = Array.AsReadOnly(new List<Stage>(stages).ToArray());

        public ReadOnlyCollection<Stage> Stages { get; }

        /// <summary>本番の面：<see cref="ProductionCount"/> 面ちょうどで、全ての面が設定に照らして正しい。</summary>
        public static StageBook Production(string text, SiegeConfig config)
        {
            var book = Parse(text);
            if (book.Stages.Count != ProductionCount) { throw new FormatException($"面の数は {ProductionCount} にしてください（{book.Stages.Count} 面）。"); }
            book.Validate(config);
            return book;
        }

        /// <summary>設定と全ての面を検証する。</summary>
        public void Validate(SiegeConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            for (var i = 0; i < Stages.Count; i++)
            {
                try
                {
                    Stages[i].Validate(config);
                }
                catch (FormatException error)
                {
                    throw new FormatException($"{i + 1} 面「{Stages[i].Name}」：{error.Message}", error);
                }
            }
        }

        /// <summary>書式だけを読む（値の妥当性は <see cref="Validate"/>）。書式の誤りは行番号付きの例外。</summary>
        public static StageBook Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var stages = new List<Stage>();
            Draft? draft = null;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var comment = line.IndexOf('#');
                if (comment >= 0) { line = line.Substring(0, comment); }
                line = line.Trim();
                if (line.Length == 0) { continue; }

                var space = line.IndexOf(' ');
                var key = space < 0 ? line : line.Substring(0, space);
                var rest = space < 0 ? string.Empty : line.Substring(space + 1).Trim();
                try
                {
                    if (key == "stage")
                    {
                        if (draft != null) { throw new FormatException("前の面が end で閉じていません。"); }
                        draft = new Draft { Name = rest };
                        continue;
                    }

                    if (draft == null) { throw new FormatException($"stage の外に「{key}」があります。"); }
                    switch (key)
                    {
                        case "hint":
                            draft.Hint = rest;
                            break;
                        case "launcher":
                            if (draft.Launcher != null) { throw new FormatException("launcher が 2 つあります。"); }
                            var l = Numbers(Words(rest, 2), 0, 2);
                            draft.Launcher = new V2(l[0], l[1]);
                            break;
                        case "shots":
                            if (draft.Shots.Count > 0) { throw new FormatException("shots が 2 つあります。"); }
                            foreach (var word in rest.Split(_separators, StringSplitOptions.RemoveEmptyEntries)) { draft.Shots.Add(ShotOf(word)); }
                            break;
                        case "block":
                            var b = Words(rest, 5);
                            var v = Numbers(b, 1, 4);
                            draft.Pieces.Add(new PieceDef(MaterialOf(b[0]), new V2(v[0], v[1]), new V2(v[2], v[3])));
                            break;
                        case "joint":
                            var j = Words(rest, 3);
                            draft.Joints.Add(new JointDef(Index(j[0]), Index(j[1]), Numbers(j, 2, 1)[0]));
                            break;
                        case "end":
                            stages.Add(draft.Build());
                            draft = null;
                            break;
                        default:
                            throw new FormatException($"未知の項目「{key}」です。");
                    }
                }
                catch (FormatException error)
                {
                    throw new FormatException($"{i + 1} 行目：{error.Message}", error);
                }
            }

            if (draft != null) { throw new FormatException("最後の面が end で閉じていません。"); }
            return new StageBook(stages);
        }

        public static ShotKind ShotOf(string word) => word switch
        {
            "normal" => ShotKind.Normal,
            "heavy" => ShotKind.Heavy,
            "bomb" => ShotKind.Bomb,
            _ => throw new FormatException($"未知の弾「{word}」です。"),
        };

        public static Material MaterialOf(string word) => word switch
        {
            "wood" => Material.Wood,
            "stone" => Material.Stone,
            "ice" => Material.Ice,
            "lantern" => Material.Lantern,
            _ => throw new FormatException($"未知の素材「{word}」です。"),
        };

        private static string[] Words(string text, int count)
        {
            var parts = text.Split(_separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != count) { throw new FormatException($"値を {count} 個書いてください（{parts.Length} 個）。"); }
            return parts;
        }

        /// <summary><paramref name="words"/>[start..start+count) を -1000〜1000 の有限の小数として読む。</summary>
        private static float[] Numbers(string[] words, int start, int count)
        {
            var values = new float[count];
            for (var i = 0; i < count; i++)
            {
                var word = words[start + i];
                if (!float.TryParse(word, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out values[i])
                    || !float.IsFinite(values[i]) || values[i] < -1000f || values[i] > 1000f)
                {
                    throw new FormatException($"「{word}」は -1000〜1000 の数ではありません。");
                }
            }

            return values;
        }

        private static int Index(string word)
        {
            if (!int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) { throw new FormatException($"「{word}」は部材の番号ではありません。"); }
            return value;
        }

        private sealed class Draft
        {
            public string Name = string.Empty;
            public string Hint = string.Empty;
            public V2? Launcher;
            public readonly List<ShotKind> Shots = new();
            public readonly List<PieceDef> Pieces = new();
            public readonly List<JointDef> Joints = new();

            public Stage Build()
            {
                if (Launcher == null) { throw new FormatException("launcher がありません。"); }
                return new Stage(Name, Hint, Launcher.Value, Shots, Pieces, Joints);
            }
        }
    }
}
