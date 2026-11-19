using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、Tick と操作を手順どおりに進めて撮る。</summary>
    public sealed class NineLatticeCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int OpeningMoves = 14;
        private const int SecondsPerMove = 7;

        private NineLatticeGame _game;

        private SudokuFlow Flow => _game.Flow;
        private SudokuGame Game => Flow.Game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<NineLatticeGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            var normalGame = Flow.GameFactory;

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル");

            Flow.Submit();
            capture.Record("seed", Flow.MatchSeed.ToString());
            await Shoot(capture, "02-start");

            for (var i = 0; i < OpeningMoves; i++) { PlayAutopilotMove(); }

            // 空きマスに候補を書き込む。
            Flow.ToggleNoteMode();
            var noted = 0;
            for (var cell = 0; cell < Board.CellCount && noted < 3; cell++)
            {
                if (Game.ValueAt(cell) != 0) { continue; }
                Select(cell);
                var solution = Game.Puzzle.Solution[cell];
                Flow.InputDigit(solution);
                Flow.InputDigit((solution % Board.Size) + 1);
                noted++;
            }

            Require(Flow.Notice == Notice.Noted, "候補");
            await Shoot(capture, "03-notes");

            // 候補モードのまま選び直し、数字モードに戻して、ピアにある数字を置こうとして拒否される。
            Flow.ToggleNoteMode();
            var (conflictCell, conflictDigit) = FindConflict();
            Select(conflictCell);
            Flow.InputDigit(conflictDigit);
            Require(Flow.Notice == Notice.Conflict, "重複の拒否");
            await Shoot(capture, "04-conflict");

            Flow.RequestHint();
            Require(Flow.Notice == Notice.HintDeduced, "伝播のヒント");
            Steps(20);
            await Shoot(capture, "05-hint");

            var (wrongCell, wrongDigit) = FindWrongButAllowed();
            Select(wrongCell);
            Flow.InputDigit(wrongDigit);
            Require(Flow.Notice == Notice.Placed, "誤った数字");
            Steps(SecondsPerMove * SudokuFlow.TickRate);
            Flow.RequestHint();
            Require(Flow.Notice == Notice.HintMistake, "誤りの指摘");
            await Shoot(capture, "06-mistake");

            Flow.TogglePause();
            Require(Flow.Screen == AppScreen.Paused, "一時停止");
            await Shoot(capture, "07-paused");

            var (puzzle, cells, swapped) = DemoPuzzles.SwappableRectangle(Flow.MatchSeed, Flow.Config);
            Flow.GameFactory = (_, _) => new SudokuGame(puzzle);
            Flow.Restart();
            for (var i = 0; i < cells.Length; i++)
            {
                Steps(SecondsPerMove * SudokuFlow.TickRate);
                Select(cells[i]);
                Flow.InputDigit(swapped[i]);
            }

            Require(Flow.Notice == Notice.FullButWrong, "誤りを含む全埋め");
            await Shoot(capture, "08-full-but-wrong");

            Flow.GameFactory = normalGame;
            Flow.TogglePause();
            Flow.Restart();
            Flow.RequestHint();
            for (var i = 0; i < Board.CellCount * 2 && Flow.Screen == AppScreen.Playing; i++) { PlayAutopilotMove(); }
            Require(Flow.Screen == AppScreen.Result, "完成");
            await Shoot(capture, "09-solved");
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }

        // マスの中心をクリックした操作として Flow へ渡す。
        private void Select(int cell) => Flow.PointerDown(Board.Column(cell) + .5, Board.Row(cell) + .5);

        private void Steps(int ticks)
        {
            for (var i = 0; i < ticks; i++) { Flow.StepTick(); }
        }

        private void PlayAutopilotMove()
        {
            Steps(SecondsPerMove * SudokuFlow.TickRate);
            var (cell, digit) = Autopilot.Next(Game) ?? throw new InvalidOperationException("完成しています。");
            Select(cell);
            if (digit == 0) { Flow.Erase(); }
            else { Flow.InputDigit(digit); }
        }

        // 空きマスと、そのピアに既にある数字。
        private (int Cell, int Digit) FindConflict()
        {
            for (var cell = Board.CellCount / 2; cell < Board.CellCount; cell++)
            {
                if (Game.ValueAt(cell) != 0) { continue; }
                for (var digit = 1; digit <= Board.Size; digit++)
                {
                    if (Game.Current.ConflictWith(cell, digit) >= 0) { return (cell, digit); }
                }
            }

            throw new InvalidOperationException("重複する数字を置ける空きマスがありません。");
        }

        // 重複はしないが正解と違う数字を置ける空きマス。
        private (int Cell, int Digit) FindWrongButAllowed()
        {
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                if (Game.ValueAt(cell) != 0) { continue; }
                for (var digit = 1; digit <= Board.Size; digit++)
                {
                    if (digit != Game.Puzzle.Solution[cell] && Game.Current.ConflictWith(cell, digit) < 0) { return (cell, digit); }
                }
            }

            throw new InvalidOperationException("誤った数字を置ける空きマスがありません。");
        }

        private async Awaitable Shoot(CaptureContext capture, string name)
        {
            _game.Render();
            await capture.ShootAsync(name, $"Tick {Flow.ElapsedTicks}、{Flow.Screen}、埋まったマス {Game.Current.FilledCount}、ヒント {Game.HintsUsed}、{Flow.Notice}");
        }
    }
}
