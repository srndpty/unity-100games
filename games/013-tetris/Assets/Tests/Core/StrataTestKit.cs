using System;

namespace Game.Core.Tests
{
    /// <summary>決まった順にピースを配る供給元。列の最後まで来たら先頭へ戻る。</summary>
    internal sealed class ScriptedPieces : IPieceSource
    {
        private readonly PieceKind[] _kinds;
        private int _position;

        public ScriptedPieces(params PieceKind[] kinds)
        {
            if (kinds.Length == 0) { throw new ArgumentException("ピースがありません。", nameof(kinds)); }
            _kinds = kinds;
        }

        public PieceKind Peek(int index) => _kinds[(_position + index) % _kinds.Length];

        public PieceKind Take()
        {
            var kind = Peek(0);
            _position++;
            return kind;
        }
    }

    internal static class StrataTestKit
    {
        /// <summary>自然落下せず、ソフトドロップは1 Tick で1段進む設定。</summary>
        public static StrataConfig Config() => new()
        {
            GravityTicks = new[] { 600 },
            SoftDropTicks = 1,
        };

        public static StrataGame NewGame(Board board, params PieceKind[] kinds) => new(Config(), new ScriptedPieces(kinds), board);

        public static StrataGame NewGame(params PieceKind[] kinds) => NewGame(Board.Empty, kinds);

        /// <summary>指定の段を、除外する列以外すべて埋める。</summary>
        public static Board FillRow(Board board, int y, params int[] gaps)
        {
            for (var x = 0; x < Board.Width; x++)
            {
                if (Array.IndexOf(gaps, x) < 0) { board = board.With(x, y, PieceKind.J); }
            }

            return board;
        }

        public static void Ticks(StrataGame game, int count)
        {
            for (var i = 0; i < count; i++) { game.Tick(); }
        }

        /// <summary>ソフトドロップで接地させ、ソフトドロップを解除する。</summary>
        public static void Land(StrataGame game)
        {
            game.Apply(GameCommand.SoftDropOn);
            var distance = game.Active!.Value.Y - game.Ghost!.Value.Y;
            Ticks(game, distance);
            game.Apply(GameCommand.SoftDropOff);
        }
    }
}
