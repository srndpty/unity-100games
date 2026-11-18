using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum AppScreen
    {
        Title,
        LevelSelect,
        Playing,
        Cleared,
    }

    [Flags]
    public enum FlowEvents
    {
        None = 0,
        Moved = 1 << 0,
        Pushed = 1 << 1,
        BoxPlaced = 1 << 2,
        Undone = 1 << 3,
        Restarted = 1 << 4,
        Cleared = 1 << 5,
        CursorMoved = 1 << 6,
    }

    /// <summary>画面遷移と、プレイヤーの操作を Core へ渡す窓口。</summary>
    public sealed class HarborFlow
    {
        /// <summary>面選択の1行あたりの面数。</summary>
        public const int SelectColumns = 5;

        private readonly IReadOnlyList<LevelMap> _levels;
        private readonly MoveRepeater _repeater;
        private bool _hasFocus = true;
        private FlowEvents _events;

        public HarborFlow(IReadOnlyList<LevelMap> levels, float repeatDelay, float repeatInterval)
        {
            _levels = levels ?? throw new ArgumentNullException(nameof(levels));
            if (levels.Count == 0) { throw new ArgumentException("面がありません。", nameof(levels)); }
            _repeater = new MoveRepeater(repeatDelay, repeatInterval);
            Records = new SessionRecords(levels.Count);
        }

        public AppScreen Screen { get; private set; }
        public SokobanGame? Game { get; private set; }
        public SessionRecords Records { get; }

        /// <summary>面選択のカーソル。プレイ中は遊んでいる面の番号（0 始まり）。</summary>
        public int SelectedLevel { get; private set; }

        public int LevelCount => _levels.Count;
        public bool HasNextLevel => SelectedLevel + 1 < _levels.Count;

        /// <summary>決定。タイトル→面選択→プレイ、クリア後は次の面（最終問なら面選択）へ進む。</summary>
        public void Submit()
        {
            if (!_hasFocus) { return; }
            switch (Screen)
            {
                case AppScreen.Title:
                    Show(AppScreen.LevelSelect);
                    break;
                case AppScreen.LevelSelect:
                    Begin(SelectedLevel);
                    break;
                case AppScreen.Cleared when HasNextLevel:
                    Begin(SelectedLevel + 1);
                    break;
                case AppScreen.Cleared:
                    Game = null;
                    Show(AppScreen.LevelSelect);
                    break;
            }
        }

        /// <summary>面選択から指定の面を始める。範囲外の番号は無視する。</summary>
        public bool SelectLevel(int level)
        {
            if (!_hasFocus || Screen != AppScreen.LevelSelect || level < 0 || level >= _levels.Count) { return false; }
            Begin(level);
            return true;
        }

        /// <summary>1つ前の画面へ戻る。遊んでいた面の途中経過は捨てる。</summary>
        public void Back()
        {
            if (!_hasFocus) { return; }
            switch (Screen)
            {
                case AppScreen.LevelSelect:
                    Show(AppScreen.Title);
                    break;
                case AppScreen.Playing:
                case AppScreen.Cleared:
                    Game = null;
                    Show(AppScreen.LevelSelect);
                    break;
            }
        }

        /// <summary>方向キーの押下。プレイ中は移動、面選択ではカーソル移動になる。</summary>
        public void PressDirection(Direction direction)
        {
            if (!_hasFocus || !MoveRules.IsDirection(direction)) { return; }
            _repeater.Press(direction);
            Navigate(direction);
        }

        public void ReleaseDirection(Direction direction) => _repeater.Release(direction);

        /// <summary>長押しのリピートを進める。</summary>
        public void Tick(float deltaSeconds)
        {
            if (!_hasFocus) { return; }
            var direction = _repeater.Tick(deltaSeconds);
            if (direction != null) { Navigate(direction.Value); }
        }

        public MoveResult Move(Direction direction)
        {
            if (!_hasFocus || Screen != AppScreen.Playing) { return MoveResult.Rejected; }
            var result = Game!.Move(direction);
            if (result == MoveResult.Rejected) { return result; }

            _events |= FlowEvents.Moved;
            var record = Game.LastRecord!;
            if (record.IsPush) { _events |= FlowEvents.Pushed; }
            if (record.BoxTo is Cell boxTo && Game.Map.IsGoal(boxTo)) { _events |= FlowEvents.BoxPlaced; }
            if (result == MoveResult.Cleared)
            {
                _events |= FlowEvents.Cleared;
                Records.Record(SelectedLevel, Game.MoveCount);
                Show(AppScreen.Cleared);
            }

            return result;
        }

        public bool Undo()
        {
            if (!_hasFocus || Screen != AppScreen.Playing || !Game!.Undo()) { return false; }
            _events |= FlowEvents.Undone;
            return true;
        }

        /// <summary>今の面を初期盤面へ戻す。クリア後はもう一度遊ぶ操作になる。</summary>
        public bool Restart()
        {
            if (!_hasFocus || (Screen != AppScreen.Playing && Screen != AppScreen.Cleared)) { return false; }
            Game!.Restart();
            Show(AppScreen.Playing);
            _events |= FlowEvents.Restarted;
            return true;
        }

        /// <summary>フォーカスを失っている間は操作を受け付けず、長押しも解除する。</summary>
        public void SetFocus(bool focus)
        {
            _hasFocus = focus;
            if (!focus) { _repeater.Clear(); }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FlowEvents TakeEvents()
        {
            var events = _events;
            _events = FlowEvents.None;
            return events;
        }

        private void Navigate(Direction direction)
        {
            if (Screen == AppScreen.Playing) { Move(direction); }
            else if (Screen == AppScreen.LevelSelect) { MoveCursor(direction); }
        }

        private void MoveCursor(Direction direction)
        {
            var next = direction switch
            {
                Direction.Left when SelectedLevel % SelectColumns > 0 => SelectedLevel - 1,
                Direction.Right when SelectedLevel % SelectColumns < SelectColumns - 1 => SelectedLevel + 1,
                Direction.Up => SelectedLevel - SelectColumns,
                Direction.Down => SelectedLevel + SelectColumns,
                _ => SelectedLevel,
            };
            if (next < 0 || next >= _levels.Count || next == SelectedLevel) { return; }
            SelectedLevel = next;
            _events |= FlowEvents.CursorMoved;
        }

        private void Begin(int level)
        {
            SelectedLevel = level;
            Game = new SokobanGame(_levels[level]);
            Show(AppScreen.Playing);
            _events = FlowEvents.None;
        }

        // 画面が変わったら、押し続けているキーで次の画面を操作しない。
        private void Show(AppScreen screen)
        {
            if (Screen != screen) { _repeater.Clear(); }
            Screen = screen;
        }
    }
}
