using System;

namespace Game.Core.Tests
{
    /// <summary>テスト用の面の作成と、手順文字列の再生。</summary>
    internal static class LevelText
    {
        public static LevelMap Map(params string[] rows) => LevelParser.Parse(rows);

        public static SokobanGame Game(params string[] rows) => new(Map(rows));

        public static Direction ToDirection(char symbol) => symbol switch
        {
            'U' => Direction.Up,
            'D' => Direction.Down,
            'L' => Direction.Left,
            'R' => Direction.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(symbol), symbol, "不正な手順です。"),
        };

        /// <summary>手順を再生する。拒否された手があれば例外にする。</summary>
        public static MoveResult Play(SokobanGame game, string moves)
        {
            var result = MoveResult.Rejected;
            for (var i = 0; i < moves.Length; i++)
            {
                result = game.Move(ToDirection(moves[i]));
                if (result == MoveResult.Rejected) { throw new InvalidOperationException($"{i + 1}手目 '{moves[i]}' が拒否されました。"); }
            }

            return result;
        }
    }
}
