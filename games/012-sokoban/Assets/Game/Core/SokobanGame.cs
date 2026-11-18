using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum GamePhase
    {
        Playing,
        Cleared,
    }

    public enum MoveResult
    {
        /// <summary>動けない手、または終了後の手。状態は変化しない。</summary>
        Rejected,
        Moved,
        Pushed,
        Cleared,
    }

    public enum GameAction
    {
        None,
        Move,
        Undo,
        Restart,
    }

    /// <summary>1面の進行。移動の判定は <see cref="MoveRules"/> に任せ、履歴と手数とクリアを管理する。</summary>
    public sealed class SokobanGame
    {
        private readonly Stack<MoveRecord> _history = new();

        public SokobanGame(LevelMap map)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Board = map.Initial;
        }

        public LevelMap Map { get; }
        public BoardState Board { get; private set; }
        public GamePhase Phase { get; private set; }
        public int MoveCount { get; private set; }
        public int PushCount { get; private set; }

        /// <summary>状態が変わるたびに増える。描画側が変化を検出するために使う。</summary>
        public int Revision { get; private set; }

        public GameAction LastAction { get; private set; }

        /// <summary>直前の移動または Undo の差分。やり直し直後は null。</summary>
        public MoveRecord? LastRecord { get; private set; }

        public int UndoDepth => _history.Count;
        public bool IsCleared => Phase == GamePhase.Cleared;

        /// <summary>プレイヤーの向き。直前の手の方向で、手がなければ下を向く。</summary>
        public Direction Facing => _history.Count > 0 ? _history.Peek().Direction : Direction.Down;

        public MoveResult Move(Direction direction)
        {
            if (IsCleared) { return MoveResult.Rejected; }
            var record = MoveRules.TryMove(Map, Board, direction);
            if (record == null) { return MoveResult.Rejected; }

            Board = MoveRules.Apply(Board, record);
            _history.Push(record);
            MoveCount++;
            if (record.IsPush) { PushCount++; }
            Changed(GameAction.Move, record);

            if (!record.IsPush) { return MoveResult.Moved; }
            if (!MoveRules.IsCleared(Map, Board)) { return MoveResult.Pushed; }
            Phase = GamePhase.Cleared;
            return MoveResult.Cleared;
        }

        /// <summary>直前の1手を取り消す。履歴がないときとクリア後は false。</summary>
        public bool Undo()
        {
            if (IsCleared || _history.Count == 0) { return false; }
            var record = _history.Pop();
            Board = MoveRules.Revert(Board, record);
            MoveCount--;
            if (record.IsPush) { PushCount--; }
            Changed(GameAction.Undo, record);
            return true;
        }

        /// <summary>初期盤面へ戻す。手数と履歴も消え、クリア後でも遊び直せる。</summary>
        public void Restart()
        {
            Board = Map.Initial;
            Phase = GamePhase.Playing;
            MoveCount = 0;
            PushCount = 0;
            _history.Clear();
            Changed(GameAction.Restart, null);
        }

        private void Changed(GameAction action, MoveRecord? record)
        {
            LastAction = action;
            LastRecord = record;
            Revision++;
        }
    }
}
