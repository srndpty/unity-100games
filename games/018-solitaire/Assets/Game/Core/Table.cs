using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>盤を読むだけの窓口。描画と入力の判定はこれだけを見る。</summary>
    public interface IReadOnlyTable
    {
        /// <summary>山札から 1 回にめくる枚数（1 または 3）。</summary>
        int DrawCount { get; }

        /// <summary>山の枚数。</summary>
        int CountOf(PileId pile);

        /// <summary>山の index 番目の札（0 が一番下）。</summary>
        Card CardAt(PileId pile, int index);

        /// <summary>山の下から数えた伏せ札の枚数。山札は全部、捨て札と組札は 0。</summary>
        int FaceDownCountOf(PileId pile);

        /// <summary>組札に入っている札の合計。</summary>
        int FoundationTotal { get; }
    }

    /// <summary>
    /// 52 枚の札が 13 の山のどこにあるか。コマンドの適用（<see cref="Execute"/>）と取り消し（<see cref="Undo"/>）だけで変わる。
    /// どのコマンドの後も <see cref="ValidateInvariants"/> の条件（全札がちょうど 1 つの山にある など）を保つ。
    /// </summary>
    public sealed class Table : IReadOnlyTable
    {
        private readonly List<Card>[] _piles;
        private readonly int[] _faceDown;

        private Table(int drawCount)
        {
            DrawCount = drawCount;
            _piles = new List<Card>[PileId.Count];
            for (var i = 0; i < _piles.Length; i++) { _piles[i] = new List<Card>(Card.DeckSize); }
            _faceDown = new int[PileId.Count];
        }

        public int DrawCount { get; }

        public int FoundationTotal
        {
            get
            {
                var total = 0;
                for (var i = 0; i < PileId.FoundationCount; i++) { total += _piles[PileId.Foundation(i).Index].Count; }
                return total;
            }
        }

        /// <summary>
        /// 山ごとの札（下から上の順）と場札の伏せ札の枚数から盤を作る。不変条件を満たさなければ例外。
        /// </summary>
        /// <param name="piles">13 山分。<see cref="PileId.All"/> の順。</param>
        /// <param name="tableauFaceDown">場札 7 列それぞれの伏せ札の枚数。</param>
        public static Table Create(int drawCount, IReadOnlyList<IReadOnlyList<Card>> piles, IReadOnlyList<int> tableauFaceDown)
        {
            if (piles == null) { throw new ArgumentNullException(nameof(piles)); }
            if (tableauFaceDown == null) { throw new ArgumentNullException(nameof(tableauFaceDown)); }
            if (piles.Count != PileId.Count) { throw new ArgumentException($"山は {PileId.Count} 個必要です。", nameof(piles)); }
            if (tableauFaceDown.Count != PileId.TableauCount)
            {
                throw new ArgumentException($"伏せ札の枚数は {PileId.TableauCount} 列分必要です。", nameof(tableauFaceDown));
            }

            var table = new Table(SolitaireConfig.RequireDrawCount(drawCount));
            for (var i = 0; i < PileId.Count; i++) { table._piles[i].AddRange(piles[i]); }
            table._faceDown[PileId.Stock.Index] = table._piles[PileId.Stock.Index].Count;
            for (var i = 0; i < PileId.TableauCount; i++) { table._faceDown[PileId.Tableau(i).Index] = tableauFaceDown[i]; }
            table.ValidateInvariants();
            return table;
        }

        /// <summary>別の盤の複製。探索で盤を書き換えるときに使う。</summary>
        public static Table CopyOf(IReadOnlyTable source)
        {
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            var table = new Table(source.DrawCount);
            foreach (var pile in PileId.All)
            {
                var list = table._piles[pile.Index];
                var count = source.CountOf(pile);
                for (var i = 0; i < count; i++) { list.Add(source.CardAt(pile, i)); }
                table._faceDown[pile.Index] = source.FaceDownCountOf(pile);
            }

            return table;
        }

        public int CountOf(PileId pile) => _piles[pile.Index].Count;

        public Card CardAt(PileId pile, int index) => _piles[pile.Index][index];

        public int FaceDownCountOf(PileId pile) => _faceDown[pile.Index];

        /// <summary>コマンドを適用する。合法でなければ例外（盤は変わらない）。</summary>
        public MoveRecord Execute(Command command)
        {
            var error = MoveRules.Check(this, command);
            if (error != MoveError.None)
            {
                throw new InvalidOperationException($"{command} は適用できません：{error}");
            }

            switch (command.Kind)
            {
                case CommandKind.Draw:
                    {
                        var n = Math.Min(DrawCount, CountOf(PileId.Stock));
                        Transfer(PileId.Stock, PileId.Waste, n);
                        return new MoveRecord(command, n, false);
                    }

                case CommandKind.Recycle:
                    {
                        var n = CountOf(PileId.Waste);
                        Transfer(PileId.Waste, PileId.Stock, n);
                        return new MoveRecord(command, n, false);
                    }

                default:
                    {
                        var from = _piles[command.From.Index];
                        var start = from.Count - command.Count;
                        _piles[command.To.Index].AddRange(from.GetRange(start, command.Count));
                        from.RemoveRange(start, command.Count);
                        var flipped = from.Count > 0 && _faceDown[command.From.Index] == from.Count;
                        if (flipped) { _faceDown[command.From.Index]--; }
                        return new MoveRecord(command, command.Count, flipped);
                    }
            }
        }

        /// <summary>直前に適用したコマンドの記録を渡して取り消す。記録は適用と逆の順に渡す。</summary>
        public void Undo(MoveRecord record)
        {
            var command = record.Command;
            switch (command.Kind)
            {
                case CommandKind.Draw:
                    RequireCount(PileId.Waste, record.Cards);
                    Transfer(PileId.Waste, PileId.Stock, record.Cards);
                    break;

                case CommandKind.Recycle:
                    RequireCount(PileId.Stock, record.Cards);
                    Transfer(PileId.Stock, PileId.Waste, record.Cards);
                    break;

                case CommandKind.Move:
                    {
                        RequireCount(command.To, record.Cards);
                        var from = _piles[command.From.Index];
                        if (record.Flipped)
                        {
                            if (_faceDown[command.From.Index] != from.Count - 1)
                            {
                                throw new InvalidOperationException("自動で表にした札が一番上にありません。");
                            }

                            _faceDown[command.From.Index]++;
                        }

                        var to = _piles[command.To.Index];
                        var start = to.Count - record.Cards;
                        from.AddRange(to.GetRange(start, record.Cards));
                        to.RemoveRange(start, record.Cards);
                        break;
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(record), command.Kind, "未定義のコマンドです。");
            }
        }

        /// <summary>
        /// 不変条件を検査する。破れていれば例外。
        /// 52 枚がちょうど 1 つの山にある／山札は全部伏せ・捨て札と組札は全部表／場札の伏せ札は下側にあり、札のある列の一番上は表／
        /// 組札は A から同じスートで 1 つずつ上がる／場札の表の札は色違いで 1 つずつ下がる。
        /// </summary>
        public void ValidateInvariants()
        {
            var seen = new bool[Card.DeckSize];
            var total = 0;
            foreach (var pile in PileId.All)
            {
                foreach (var card in _piles[pile.Index])
                {
                    if (seen[card.Id]) { throw new InvalidOperationException($"{card} が 2 か所にあります。"); }
                    seen[card.Id] = true;
                    total++;
                }
            }

            if (total != Card.DeckSize) { throw new InvalidOperationException($"札が {total} 枚です（{Card.DeckSize} 枚必要）。"); }

            if (_faceDown[PileId.Stock.Index] != CountOf(PileId.Stock)) { throw new InvalidOperationException("山札に表の札があります。"); }
            if (_faceDown[PileId.Waste.Index] != 0) { throw new InvalidOperationException("捨て札に伏せ札があります。"); }

            for (var slot = 0; slot < PileId.FoundationCount; slot++)
            {
                var pile = PileId.Foundation(slot);
                if (_faceDown[pile.Index] != 0) { throw new InvalidOperationException($"{pile} に伏せ札があります。"); }
                var cards = _piles[pile.Index];
                for (var i = 0; i < cards.Count; i++)
                {
                    if (cards[i].Rank != i + 1 || cards[i].Suit != cards[0].Suit)
                    {
                        throw new InvalidOperationException($"{pile} の並びが A から同じスートで上がっていません。");
                    }
                }
            }

            for (var slot = 0; slot < PileId.TableauCount; slot++)
            {
                var pile = PileId.Tableau(slot);
                var cards = _piles[pile.Index];
                var down = _faceDown[pile.Index];
                if (down < 0 || down > cards.Count || (cards.Count > 0 && down == cards.Count))
                {
                    throw new InvalidOperationException($"{pile} の伏せ札の枚数 {down} が不正です（札 {cards.Count} 枚）。");
                }

                for (var i = down + 1; i < cards.Count; i++)
                {
                    if (!MoveRules.Stacks(cards[i], cards[i - 1]))
                    {
                        throw new InvalidOperationException($"{pile} の表の札が色違いで 1 つずつ下がっていません。");
                    }
                }
            }
        }

        private void Transfer(PileId from, PileId to, int count)
        {
            var source = _piles[from.Index];
            var target = _piles[to.Index];
            for (var i = 0; i < count; i++)
            {
                var last = source.Count - 1;
                target.Add(source[last]);
                source.RemoveAt(last);
            }

            _faceDown[PileId.Stock.Index] = _piles[PileId.Stock.Index].Count;
        }

        private void RequireCount(PileId pile, int count)
        {
            if (count < 0 || CountOf(pile) < count)
            {
                throw new InvalidOperationException($"{pile} に戻す札が {count} 枚ありません。");
            }
        }
    }
}
