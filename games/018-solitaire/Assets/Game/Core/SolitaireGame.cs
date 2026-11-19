using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>試合の状態。</summary>
    public enum GamePhase
    {
        Playing,

        /// <summary>52 枚がすべて組札に入った。</summary>
        Won,

        /// <summary>どの手順でも前進できない（<see cref="DeadlockDetector"/>）。</summary>
        Stuck,

        /// <summary>あきらめた。</summary>
        Resigned,
    }

    /// <summary>
    /// 1 試合：盤・手数・取り消しの履歴・勝敗。時間は持たない。
    /// コマンドは <see cref="MoveRules"/> で検証してから適用し、適用のたびに勝利と手詰まりを判定する。終わった試合はコマンドも取り消しも受け付けない。
    /// </summary>
    public sealed class SolitaireGame
    {
        private readonly Table _table;
        private readonly SolitaireConfig _config;
        private readonly Stack<MoveRecord> _history = new();

        public SolitaireGame(SolitaireConfig config, int seed)
            : this(config, Deal.Create(seed, RequireConfig(config).DrawCount))
        {
        }

        /// <summary>決まった盤から始める（テストと撮影の場面）。盤は複製し、めくる枚数は盤のものを使う。</summary>
        public SolitaireGame(SolitaireConfig config, IReadOnlyTable table)
        {
            _config = RequireConfig(config).Copy();
            _table = Game.Core.Table.CopyOf(table ?? throw new ArgumentNullException(nameof(table)));
            _table.ValidateInvariants();
            Evaluate();
        }

        public IReadOnlyTable Table => _table;

        public GamePhase Phase { get; private set; }

        public bool IsOver => Phase != GamePhase.Playing;

        /// <summary>成立したコマンドの数。取り消すと 1 減る。</summary>
        public int Moves => _history.Count;

        public bool CanUndo => !IsOver && _history.Count > 0;

        /// <summary>残りを自動で組札へ送れる（<see cref="AutoComplete"/>）。</summary>
        public bool CanAutoComplete => !IsOver && AutoComplete.IsAvailable(_table);

        /// <summary>直前に適用したコマンドの記録。描画が動きを知るために使う。</summary>
        public MoveRecord? LastMove => _history.Count > 0 ? _history.Peek() : null;

        /// <summary>コマンドを検証して適用する。None 以外では何も変わらない。</summary>
        public MoveError Apply(Command command)
        {
            if (IsOver) { return MoveError.GameOver; }
            var error = MoveRules.Check(_table, command);
            if (error != MoveError.None) { return error; }
            _history.Push(_table.Execute(command));
            Evaluate();
            return MoveError.None;
        }

        /// <summary>直前のコマンドを取り消す。自動で表にした札も伏せ直す。取り消せなければ false。</summary>
        public bool Undo()
        {
            if (!CanUndo) { return false; }
            _table.Undo(_history.Pop());
            return true;
        }

        /// <summary>あきらめる。進行中でなければ false。</summary>
        public bool Resign()
        {
            if (IsOver) { return false; }
            Phase = GamePhase.Resigned;
            return true;
        }

        /// <summary>山札をクリックしたときのコマンドを適用する。</summary>
        public MoveError ClickStock() => Apply(MoveRules.StockCommand(_table));

        /// <summary>山の一番上の札を組札へ送る（ダブルクリック）。置けなければ拒否の理由。</summary>
        public MoveError SendToFoundation(PileId from)
        {
            if (IsOver) { return MoveError.GameOver; }
            return MoveRules.FoundationMoveFrom(_table, from) is Command command
                ? Apply(command)
                : MoveError.InvalidDestination;
        }

        private static SolitaireConfig RequireConfig(SolitaireConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            return config;
        }

        private void Evaluate()
        {
            if (_table.FoundationTotal == Card.DeckSize)
            {
                Phase = GamePhase.Won;
            }
            else if (DeadlockDetector.Check(_table, _config.DeadlockSearchStates) == DeadlockVerdict.Stuck)
            {
                Phase = GamePhase.Stuck;
            }
        }
    }
}
