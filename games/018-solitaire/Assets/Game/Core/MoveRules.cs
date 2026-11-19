using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// コマンドの合法性を 1 か所で決める。盤は読むだけで変えない。
    /// 入力・ダブルクリック・合法手の列挙・手詰まりの探索はすべてここを通す。
    /// </summary>
    public static class MoveRules
    {
        /// <summary>upper を lower の上に場札として重ねられるか（色違いで 1 つ下）。</summary>
        public static bool Stacks(Card upper, Card lower) => upper.IsRed != lower.IsRed && upper.Rank + 1 == lower.Rank;

        /// <summary>コマンドが合法なら None、そうでなければ理由。</summary>
        public static MoveError Check(IReadOnlyTable table, Command command)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            return command.Kind switch
            {
                CommandKind.Draw => table.CountOf(PileId.Stock) == 0 ? MoveError.StockEmpty : MoveError.None,
                CommandKind.Recycle => CheckRecycle(table),
                CommandKind.Move => CheckMove(table, command),
                _ => MoveError.UnknownCommand,
            };
        }

        /// <summary>山札をクリックしたときのコマンド。山札が残っていればめくり、空なら戻す。</summary>
        public static Command StockCommand(IReadOnlyTable table)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            return table.CountOf(PileId.Stock) > 0 ? Command.Draw : Command.Recycle;
        }

        /// <summary>
        /// 山の一番上の札を組札へ送るコマンド。同じスートが積まれた組札を優先し、なければ左の空いた組札。置けなければ null。
        /// </summary>
        public static Command? FoundationMoveFrom(IReadOnlyTable table, PileId from)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (from.Kind != PileKind.Waste && from.Kind != PileKind.Tableau) { return null; }
            Command? empty = null;
            for (var slot = 0; slot < PileId.FoundationCount; slot++)
            {
                var to = PileId.Foundation(slot);
                var command = Command.Move(from, to);
                if (Check(table, command) != MoveError.None) { continue; }
                if (table.CountOf(to) > 0) { return command; }
                empty ??= command;
            }

            return empty;
        }

        /// <summary>合法なコマンドをすべて buffer に入れる（先に空にする）。順序は決まっている。</summary>
        public static void LegalMoves(IReadOnlyTable table, List<Command> buffer)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (buffer == null) { throw new ArgumentNullException(nameof(buffer)); }
            buffer.Clear();
            var stock = StockCommand(table);
            if (Check(table, stock) == MoveError.None) { buffer.Add(stock); }

            // 置き先ごとに、底になれる札は決まっている（組札は一番上の 1 枚、空の列は K、札のある列は 1 つ下のランク）。
            // 動かす元の表の札からその札を探して候補にし、Check で確かめる。手詰まりの探索で局面ごとに呼ぶので、全枚数を総当たりしない。
            foreach (var from in PileId.All)
            {
                if (from.Kind == PileKind.Stock) { continue; }
                var count = table.CountOf(from);
                if (count == 0) { continue; }
                var firstMovable = from.Kind == PileKind.Tableau ? table.FaceDownCountOf(from) : count - 1;
                foreach (var to in PileId.All)
                {
                    if (to == from) { continue; }
                    if (to.Kind == PileKind.Foundation)
                    {
                        AddIfLegal(table, Command.Move(from, to), buffer);
                    }
                    else if (to.Kind == PileKind.Tableau)
                    {
                        var targetCount = table.CountOf(to);
                        var baseRank = targetCount == 0 ? Card.MaxRank : table.CardAt(to, targetCount - 1).Rank - 1;
                        for (var i = firstMovable; i < count; i++)
                        {
                            if (table.CardAt(from, i).Rank != baseRank) { continue; }
                            AddIfLegal(table, Command.Move(from, to, count - i), buffer);
                            break;
                        }
                    }
                }
            }
        }

        private static void AddIfLegal(IReadOnlyTable table, Command command, List<Command> buffer)
        {
            if (Check(table, command) == MoveError.None) { buffer.Add(command); }
        }

        private static MoveError CheckRecycle(IReadOnlyTable table)
        {
            if (table.CountOf(PileId.Stock) > 0) { return MoveError.StockNotEmpty; }
            return table.CountOf(PileId.Waste) == 0 ? MoveError.WasteEmpty : MoveError.None;
        }

        private static MoveError CheckMove(IReadOnlyTable table, Command command)
        {
            var from = command.From;
            var to = command.To;
            if (from == to) { return MoveError.SamePile; }
            if (command.Count < 1) { return MoveError.InvalidCount; }
            if (from.Kind == PileKind.Stock) { return MoveError.FromStock; }
            if (to.Kind == PileKind.Stock || to.Kind == PileKind.Waste) { return MoveError.InvalidDestination; }
            if (from.Kind == PileKind.Foundation && to.Kind == PileKind.Foundation) { return MoveError.InvalidDestination; }

            var sourceCount = table.CountOf(from);
            if (sourceCount == 0) { return MoveError.SourceEmpty; }
            if (from.Kind != PileKind.Tableau && command.Count != 1) { return MoveError.NotSingleCard; }
            if (command.Count > sourceCount - table.FaceDownCountOf(from)) { return MoveError.FaceDown; }

            var baseIndex = sourceCount - command.Count;
            for (var i = baseIndex + 1; i < sourceCount; i++)
            {
                if (!Stacks(table.CardAt(from, i), table.CardAt(from, i - 1))) { return MoveError.BrokenRun; }
            }

            var moving = table.CardAt(from, baseIndex);
            return to.Kind == PileKind.Foundation
                ? CheckFoundation(table, to, moving, command.Count)
                : CheckTableau(table, to, moving);
        }

        private static MoveError CheckFoundation(IReadOnlyTable table, PileId to, Card moving, int count)
        {
            if (count != 1) { return MoveError.NotSingleCard; }
            var targetCount = table.CountOf(to);
            if (targetCount == 0) { return moving.Rank == Card.MinRank ? MoveError.None : MoveError.FoundationNeedsAce; }
            var top = table.CardAt(to, targetCount - 1);
            if (top.Suit != moving.Suit) { return MoveError.FoundationSuitMismatch; }
            return top.Rank + 1 == moving.Rank ? MoveError.None : MoveError.FoundationRankMismatch;
        }

        private static MoveError CheckTableau(IReadOnlyTable table, PileId to, Card moving)
        {
            var targetCount = table.CountOf(to);
            if (targetCount == 0) { return moving.Rank == Card.MaxRank ? MoveError.None : MoveError.EmptyColumnNeedsKing; }
            var top = table.CardAt(to, targetCount - 1);
            if (top.IsRed == moving.IsRed) { return MoveError.SameColor; }
            return top.Rank == moving.Rank + 1 ? MoveError.None : MoveError.RankMismatch;
        }
    }
}
