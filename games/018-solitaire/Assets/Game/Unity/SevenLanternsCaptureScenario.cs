using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、Tick とポインタの操作（押す・動かす・離す）を手順どおりに Flow へ渡して撮る。
    /// </summary>
    public sealed class SevenLanternsCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int AutopilotMoves = 30;
        private const int TicksPerMove = 3 * SolitaireFlow.TickRate;

        private readonly List<Command> _moves = new();
        private SevenLanternsGame _game;
        private double _clock;

        private SolitaireFlow Flow => _game.Flow;
        private SolitaireGame Game => Flow.Game;
        private IReadOnlyTable Table => Game.Table;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<SevenLanternsGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            var normalGame = Flow.GameFactory;

            _game.Render(0);
            await capture.ShootAsync("01-title", "タイトル");

            Flow.Submit();
            capture.Record("seed", Flow.MatchSeed.ToString());
            await Shoot(capture, "02-deal");

            var pilot = new Autopilot(Flow.MatchSeed);
            var move = FindTableauMove();
            for (var i = 0; move == null && i < AutopilotMoves; i++)
            {
                PlayAutopilot(pilot);
                move = FindTableauMove();
            }

            var shown = move ?? throw new InvalidOperationException("撮影場面を作れませんでした: 場札どうしの移動");
            var grip = Grip(shown.From, Table.CountOf(shown.From) - shown.Count);
            Click(grip);
            Require(Flow.Input.Selected is PileRun run && run.Pile == shown.From && run.Count == shown.Count, "選択");
            await Shoot(capture, "03-selected");

            var drop = DropPoint(shown, grip);
            Tick();
            Flow.PointerDown(grip, _clock);
            Flow.PointerMove(Lerp(grip, drop, .55));
            Require(Flow.Input.Dragging != null, "ドラッグ");
            await Shoot(capture, "04-dragging");

            var before = Game.Moves;
            Flow.PointerMove(drop);
            Flow.PointerUp(drop, _clock + .3);
            Require(Game.Moves == before + 1, "ドロップ");
            await Shoot(capture, "05-dropped");

            var illegal = FindIllegalTableauMove();
            Perform(illegal);
            Require(Flow.Notice == Notice.Rejected, "置けない場所");
            await Shoot(capture, "06-rejected");

            for (var i = 0; i < AutopilotMoves && Flow.Screen == AppScreen.Playing; i++) { PlayAutopilot(pilot); }
            Require(Flow.Screen == AppScreen.Playing, "進行中");
            await Shoot(capture, "07-progress");

            Flow.Undo();
            Require(Flow.Notice == Notice.Undone, "1 手戻す");
            await Shoot(capture, "08-undo");

            Flow.TogglePause();
            Require(Flow.Screen == AppScreen.Paused, "一時停止");
            await Shoot(capture, "09-paused");

            Flow.GameFactory = seed => new SolitaireGame(Flow.Config, Deal.Create(seed, 3));
            Flow.Restart();
            Click(TableLayout.SlotCenter(PileId.Stock));
            Click(TableLayout.SlotCenter(PileId.Stock));
            Require(Table.DrawCount == 3 && Table.CountOf(PileId.Waste) == 6, "3 枚めくり");
            await Shoot(capture, "10-draw-three");

            Flow.GameFactory = _ => new SolitaireGame(Flow.Config, DemoTables.OneMoveFromStuck(1));
            Flow.TogglePause();
            Flow.Restart();
            Perform(DemoTables.StuckMove);
            Require(Flow.Screen == AppScreen.Result && Game.Phase == GamePhase.Stuck, "手詰まり");
            await Shoot(capture, "11-stuck");

            Flow.GameFactory = _ => new SolitaireGame(Flow.Config, DemoTables.OneMoveFromWin());
            Flow.Restart();
            var king = TableLayout.CardCenter(Table, PileId.Tableau(2), 0);
            DoubleClick(king);
            Require(Flow.Screen == AppScreen.Result && Game.Phase == GamePhase.Won, "勝利");
            await Shoot(capture, "12-won");

            Flow.GameFactory = _ => new SolitaireGame(Flow.Config, DemoTables.AutoCompleteReady());
            Flow.Restart();
            Require(Flow.CanStartAutoComplete, "自動で仕上げられる");
            await Shoot(capture, "13-auto-ready");

            Flow.StartAutoComplete();
            Steps(Flow.AutoMoveTicks * 12);
            Require(Flow.IsAutoCompleting && Table.FoundationTotal == 26, "自動で仕上げている途中");
            await Shoot(capture, "14-auto-completing");
            Flow.GameFactory = normalGame;
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }

        private static TablePoint Lerp(TablePoint a, TablePoint b, double t) => new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));

        // 操作ごとに時計を 1 秒進め、続けたクリックがダブルクリックにならないようにする。
        private void Tick() => _clock += 1;

        private void DoubleClick(TablePoint point)
        {
            Click(point);
            Flow.PointerDown(point, _clock + .1);
            Flow.PointerUp(point, _clock + .12);
        }

        private void Steps(int ticks)
        {
            for (var i = 0; i < ticks; i++) { Flow.StepTick(); }
            _clock += 10;
        }

        private void Click(TablePoint point)
        {
            Tick();
            Flow.PointerDown(point, _clock);
            Flow.PointerUp(point, _clock + .02);
        }

        /// <summary>札を掴む位置。一番上の札なら中心、下の札なら上の札に隠れていない上端の帯。</summary>
        private TablePoint Grip(PileId pile, int index)
        {
            var center = TableLayout.CardCenter(Table, pile, index);
            return index == Table.CountOf(pile) - 1 ? center : center + new TablePoint(0, (TableLayout.CardHeight / 2) - .06);
        }

        /// <summary>掴んだ札の底が置き先にぴったり重なるときのポインタの位置。</summary>
        private TablePoint DropPoint(Command move, TablePoint grip)
        {
            var baseCenter = TableLayout.CardCenter(Table, move.From, Table.CountOf(move.From) - move.Count);
            var count = Table.CountOf(move.To);
            TablePoint target;
            if (count == 0) { target = TableLayout.SlotCenter(move.To); }
            else if (move.To.Kind == PileKind.Tableau) { target = TableLayout.CardCenter(Table, move.To, count - 1) + new TablePoint(0, -TableLayout.FaceUpStep); }
            else { target = TableLayout.CardCenter(Table, move.To, count - 1); }
            return target + (grip - baseCenter);
        }

        /// <summary>コマンドをポインタの操作で行う。めくり・戻しは山札のクリック、移動はドラッグ。</summary>
        private void Perform(Command move)
        {
            if (move.Kind != CommandKind.Move)
            {
                Click(TableLayout.SlotCenter(PileId.Stock));
                return;
            }

            var grip = Grip(move.From, Table.CountOf(move.From) - move.Count);
            var drop = DropPoint(move, grip);
            Tick();
            Flow.PointerDown(grip, _clock);
            Flow.PointerMove(Lerp(grip, drop, .5));
            Flow.PointerMove(drop);
            Flow.PointerUp(drop, _clock + .3);
        }

        private void PlayAutopilot(Autopilot pilot)
        {
            Steps(TicksPerMove);
            var before = Game.Moves;
            Perform(pilot.Next(Table) ?? throw new InvalidOperationException("合法手がありません。"));
            Require(Game.Moves == before + 1, "自動操縦の手");
        }

        private Command? FindTableauMove()
        {
            MoveRules.LegalMoves(Table, _moves);
            foreach (var move in _moves)
            {
                if (move.Kind == CommandKind.Move && move.From.Kind == PileKind.Tableau && move.To.Kind == PileKind.Tableau && Table.CountOf(move.To) > 0)
                {
                    return move;
                }
            }

            return null;
        }

        // 場札の一番上の札を、置けない別の列へ動かすコマンド（色かランクが合わない）。
        private Command FindIllegalTableauMove()
        {
            for (var from = 0; from < PileId.TableauCount; from++)
            {
                for (var to = 0; to < PileId.TableauCount; to++)
                {
                    var command = Command.Move(PileId.Tableau(from), PileId.Tableau(to));
                    var error = MoveRules.Check(Table, command);
                    if (error == MoveError.SameColor || error == MoveError.RankMismatch) { return command; }
                }
            }

            throw new InvalidOperationException("置けない移動が見つかりません。");
        }

        private async Awaitable Shoot(CaptureContext capture, string name)
        {
            _game.Render(0);
            await capture.ShootAsync(name, $"Tick {Flow.ElapsedTicks}、{Flow.Screen}、{Game.Phase}、手数 {Game.Moves}、組札 {Table.FoundationTotal}、{Flow.Notice} {Flow.LastError}");
        }
    }
}
