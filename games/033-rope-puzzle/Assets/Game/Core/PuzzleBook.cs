using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 問題集（<c>Assets/Game/Data/Puzzles.txt</c>）。行の書式（座標は 1/16 マス、<see cref="Board.DataUnit"/> 単位）：
    /// <code>
    /// puzzle 名前
    /// hint 案内文
    /// par 目安の手数
    /// body x y
    /// rope 支点x 支点y 全長 節の数
    /// wall x0 y0 x1 y1
    /// spike x y 半径
    /// goal x y 半径
    /// end
    /// </code>
    /// <c>#</c> から行末と空行は無視する。
    /// </summary>
    public sealed class PuzzleBook
    {
        /// <summary>本番の問題集の問の数。</summary>
        public const int ProductionCount = 10;

        private static readonly char[] _separators = { ' ', '\t' };

        private PuzzleBook(IList<Puzzle> puzzles) => Puzzles = new ReadOnlyCollection<Puzzle>(puzzles);

        public ReadOnlyCollection<Puzzle> Puzzles { get; }

        /// <summary>本番の問題集：<see cref="ProductionCount"/> 問ちょうどで、全ての問が設定に照らして正しい。</summary>
        public static PuzzleBook Production(string text, RopeConfig config)
        {
            var book = Parse(text);
            if (book.Puzzles.Count != ProductionCount) { throw new FormatException($"問の数は {ProductionCount} にしてください（{book.Puzzles.Count} 問）。"); }
            book.Validate(config);
            return book;
        }

        /// <summary>全ての問を検証する。</summary>
        public void Validate(RopeConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            foreach (var puzzle in Puzzles) { puzzle.Validate(config); }
        }

        /// <summary>書式だけを読む（値の妥当性は <see cref="Validate"/>）。書式の誤りは行番号付きの例外。</summary>
        public static PuzzleBook Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var puzzles = new List<Puzzle>();
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
                    if (key == "puzzle")
                    {
                        if (draft != null) { throw new FormatException("前の問が end で閉じていません。"); }
                        draft = new Draft { Name = rest };
                        continue;
                    }

                    if (draft == null) { throw new FormatException($"puzzle の外に「{key}」があります。"); }
                    switch (key)
                    {
                        case "hint":
                            draft.Hint = rest;
                            break;
                        case "par":
                            draft.Par = Numbers(rest, 1)[0];
                            break;
                        case "body":
                            if (draft.Body != null) { throw new FormatException("body が 2 つあります。"); }
                            var b = Numbers(rest, 2);
                            draft.Body = Point(b[0], b[1]);
                            break;
                        case "rope":
                            var r = Numbers(rest, 4);
                            draft.Ropes.Add(new RopeSpec(Point(r[0], r[1]), r[2] * Board.DataUnit, r[3]));
                            break;
                        case "wall":
                            var w = Numbers(rest, 4);
                            if (w[2] <= w[0] || w[3] <= w[1]) { throw new FormatException("壁は x0 < x1、y0 < y1 にしてください。"); }
                            draft.Walls.Add(new Rect(w[0] * Board.DataUnit, w[1] * Board.DataUnit, w[2] * Board.DataUnit, w[3] * Board.DataUnit));
                            break;
                        case "spike":
                            draft.Spikes.Add(CircleOf(Numbers(rest, 3)));
                            break;
                        case "goal":
                            if (draft.Goal != null) { throw new FormatException("goal が 2 つあります。"); }
                            draft.Goal = CircleOf(Numbers(rest, 3));
                            break;
                        case "end":
                            puzzles.Add(draft.Build());
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
                catch (ArgumentException error)
                {
                    throw new FormatException($"{i + 1} 行目：{error.Message}", error);
                }
            }

            if (draft != null) { throw new FormatException("最後の問が end で閉じていません。"); }
            return new PuzzleBook(puzzles);
        }

        private static Vec2i Point(int x, int y) => new(x * Board.DataUnit, y * Board.DataUnit);

        private static Circle CircleOf(int[] values)
        {
            if (values[2] <= 0) { throw new FormatException("半径は正にしてください。"); }
            return new Circle(Point(values[0], values[1]), values[2] * Board.DataUnit);
        }

        /// <summary>空白区切りの整数をちょうど <paramref name="count"/> 個読む。範囲は盤の大きさの数倍までに限る（桁あふれを防ぐ）。</summary>
        private static int[] Numbers(string text, int count)
        {
            var parts = text.Split(_separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != count) { throw new FormatException($"数を {count} 個書いてください（{parts.Length} 個）。"); }
            var values = new int[count];
            for (var i = 0; i < count; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out values[i]) || values[i] < -1024 || values[i] > 1024)
                {
                    throw new FormatException($"「{parts[i]}」は -1024〜1024 の整数ではありません。");
                }
            }

            return values;
        }

        private sealed class Draft
        {
            public string Name = string.Empty;
            public string Hint = string.Empty;
            public int Par;
            public Vec2i? Body;
            public readonly List<RopeSpec> Ropes = new();
            public readonly List<Rect> Walls = new();
            public readonly List<Circle> Spikes = new();
            public Circle? Goal;

            public Puzzle Build()
            {
                if (Body == null) { throw new FormatException("body がありません。"); }
                if (Goal == null) { throw new FormatException("goal がありません。"); }
                return new Puzzle(Name, Hint, Par, Body.Value, Ropes, Walls, Spikes, Goal.Value);
            }
        }
    }
}
