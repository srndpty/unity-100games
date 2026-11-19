using System;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.SporeTestKit;

namespace Game.Core.Tests
{
    public sealed class GarbageTests
    {
        [TestCase(40, 0)]
        [TestCase(70, 1)]
        [TestCase(200, 2)]
        [TestCase(520, 7)]
        [TestCase(1160, 16)]
        [TestCase(2440, 34)]
        public void 得点を70で割って攻撃量にする(long score, int expected)
        {
            Assert.That(new GarbageMeter(70).Attack(score), Is.EqualTo(expected));
        }

        [Test]
        public void 端数は次の攻撃へ持ち越す()
        {
            var meter = new GarbageMeter(70);
            Assert.That(meter.Attack(40), Is.EqualTo(0));
            Assert.That(meter.Attack(40), Is.EqualTo(1));
        }

        [Test]
        public void 攻撃は先に自分の予告と相殺する()
        {
            var meter = new GarbageMeter(70);
            meter.Receive(5);
            Assert.That(meter.Attack(210), Is.EqualTo(0));
            Assert.That(meter.Pending, Is.EqualTo(2));
            Assert.That(meter.Attack(350), Is.EqualTo(3));
            Assert.That(meter.Pending, Is.EqualTo(0));
        }

        [Test]
        public void 着弾は上限までで残りは予告に残る()
        {
            var meter = new GarbageMeter(70);
            meter.Receive(34);
            Assert.That(meter.TakeDrop(30), Is.EqualTo(30));
            Assert.That(meter.Pending, Is.EqualTo(4));
            Assert.That(meter.TakeDrop(30), Is.EqualTo(4));
            Assert.That(meter.TakeDrop(30), Is.EqualTo(0));
        }

        [Test]
        public void 不正な値を拒否する()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GarbageMeter(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GarbageMeter(70).Attack(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GarbageMeter(70).Receive(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GarbageMeter(70).TakeDrop(0));
        }

        [Test]
        public void 六個は全列に一個ずつ降る()
        {
            var result = GarbageDropper.Drop(Board.Empty, 6, new SeededRandom(1));
            for (var x = 0; x < Board.Width; x++) { Assert.That(result.Board.At(x, 0), Is.EqualTo(Cell.Ash)); }
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        public void 端数は別々の列に降る(int count)
        {
            var result = GarbageDropper.Drop(Board.Empty, count, new SeededRandom(7));
            Assert.That(result.Moves.Select(move => move.X).Distinct().Count(), Is.EqualTo(count));
        }

        [Test]
        public void 同じシードなら同じ列に降る()
        {
            var first = GarbageDropper.Drop(Board.Empty, 17, new SeededRandom(42));
            var second = GarbageDropper.Drop(Board.Empty, 17, new SeededRandom(42));
            Assert.That(first.Board, Is.EqualTo(second.Board));
        }

        [Test]
        public void 満杯の列にあふれた分は消える()
        {
            var board = Board.Empty;
            for (var y = 0; y < Board.Height; y++) { board = board.With(0, y, Cell.Moss); }
            var result = GarbageDropper.Drop(board, 6, new SeededRandom(1));
            Assert.That(result.Moves, Has.Count.EqualTo(5));
            Assert.That(result.Board.At(0, Board.Height - 1), Is.EqualTo(Cell.Moss));
        }

        [Test]
        public void 全列が満杯でも破綻しない()
        {
            var cells = new Cell[Board.Width * Board.Height];
            Array.Fill(cells, Cell.Ash);
            var board = Board.FromCells(cells);
            var result = GarbageDropper.Drop(board, 30, new SeededRandom(1));
            Assert.That(result.Board, Is.EqualTo(board));
            Assert.That(result.Moves, Is.Empty);
        }

        [Test]
        public void 一列の高さを超える数でも盤面の中に収まる()
        {
            var result = GarbageDropper.Drop(Board.Empty, 90, new SeededRandom(1));
            Assert.That(result.Moves, Has.Count.EqualTo(Board.Width * Board.Height));
        }
    }
}
