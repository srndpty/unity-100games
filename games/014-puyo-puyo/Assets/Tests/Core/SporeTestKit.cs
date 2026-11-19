using System;

namespace Game.Core.Tests
{
    internal static class SporeTestKit
    {
        public const int MaxSteps = 2000;

        public static readonly Pair EmberPair = new(Cell.Ember, Cell.Ember);
        public static readonly Pair MossPair = new(Cell.Moss, Cell.Moss);
        public static readonly Pair TidePair = new(Cell.Tide, Cell.Tide);

        /// <summary>自然落下せず、演出の待ち時間もない設定。</summary>
        public static SporeConfig InstantConfig() => new()
        {
            GravityTicks = new[] { 3600 },
            SoftDropTicks = 1,
            SettleTicksPerRow = 0,
            PopTicks = 0,
            SpawnDelayTicks = 0,
        };

        /// <summary>自然落下しないが、演出の待ち時間は既定値のままの設定。</summary>
        public static SporeConfig TimedConfig() => new()
        {
            GravityTicks = new[] { 3600 },
            SoftDropTicks = 1,
        };

        /// <summary>
        /// Ember のペアを縦のまま2列目へ落とすと2連鎖する盤面。
        /// 1連鎖目で E が4個消え、落ちた M が最下段の M とつながる。
        /// </summary>
        public static Board TwoChainBoard() => Parse(
            "M.....",
            "M.....",
            "M.....",
            "E.....",
            "E.....",
            "MT....");

        /// <summary>文字列から盤面を作る。最後の行が最下段。E M T P は色、A は枯れ胞子、. は空き。</summary>
        public static Board Parse(params string[] rows)
        {
            var board = Board.Empty;
            for (var r = 0; r < rows.Length; r++)
            {
                var y = rows.Length - 1 - r;
                for (var x = 0; x < rows[r].Length; x++) { board = board.With(x, y, ToCell(rows[r][x])); }
            }

            return board;
        }

        public static PlayerField NewField(SporeConfig config, Board board, params Pair[] pairs) => new(config, new FixedPairs(pairs), 1, board);

        public static PlayerField NewField(Board board, params Pair[] pairs) => NewField(InstantConfig(), board, pairs);

        public static void Ticks(PlayerField field, int count)
        {
            for (var i = 0; i < count; i++) { field.Tick(); }
        }

        /// <summary>今のペアをソフトドロップで置き、次のペアが出るか負けるまで進める。</summary>
        public static void Drop(PlayerField field)
        {
            field.Apply(GameCommand.SoftDropOn);
            for (var i = 0; i < MaxSteps && (field.TakeEvents() & FieldEvents.Locked) == 0; i++) { field.Tick(); }
            RunUntilControl(field);
        }

        /// <summary>操作できるようになるか負けるまで進める。</summary>
        public static void RunUntilControl(PlayerField field)
        {
            for (var i = 0; i < MaxSteps && field.Phase != FieldPhase.Falling && field.Phase != FieldPhase.Defeated; i++) { field.Tick(); }
        }

        /// <summary>指定のフェーズに入るまで進める。</summary>
        public static void RunUntil(PlayerField field, FieldPhase phase)
        {
            for (var i = 0; i < MaxSteps && field.Phase != phase; i++) { field.Tick(); }
        }

        private static Cell ToCell(char c) => c switch
        {
            '.' => Cell.Empty,
            'E' => Cell.Ember,
            'M' => Cell.Moss,
            'T' => Cell.Tide,
            'P' => Cell.Pollen,
            'A' => Cell.Ash,
            _ => throw new ArgumentOutOfRangeException(nameof(c), c, "未定義の文字です。"),
        };
    }
}
