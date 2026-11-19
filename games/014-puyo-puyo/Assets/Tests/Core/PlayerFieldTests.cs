using System;
using System.Collections.Generic;
using NUnit.Framework;
using static Game.Core.Tests.SporeTestKit;

namespace Game.Core.Tests
{
    public sealed class PlayerFieldTests
    {
        private static readonly int[] _slowingGravity = { 1, 2 };

        private static readonly GameCommand[] _allCommands =
        {
            GameCommand.MoveLeft,
            GameCommand.MoveRight,
            GameCommand.RotateClockwise,
            GameCommand.RotateCounterClockwise,
            GameCommand.SoftDropOn,
            GameCommand.SoftDropOff,
        };

        [Test]
        public void ペアは3列目の可視最上段に出る()
        {
            var field = NewField(Board.Empty, EmberPair);
            Assert.That(field.Phase, Is.EqualTo(FieldPhase.Falling));
            Assert.That(field.Active, Is.EqualTo(ActivePair.Spawn(EmberPair)));
        }

        [Test]
        public void 壁の外へは動けない()
        {
            var field = NewField(Board.Empty, EmberPair);
            Assert.That(field.Apply(GameCommand.MoveLeft), Is.True);
            Assert.That(field.Apply(GameCommand.MoveLeft), Is.True);
            Assert.That(field.Apply(GameCommand.MoveLeft), Is.False);
            Assert.That(field.Active!.Value.X, Is.EqualTo(0));
        }

        [Test]
        public void 横向きのペアは着地後に片方だけ分離して落ちる()
        {
            var field = NewField(Parse("..E..."), MossPair, TidePair);
            field.Apply(GameCommand.RotateClockwise);
            Drop(field);
            Assert.That(field.Board.At(2, 1), Is.EqualTo(Cell.Moss));
            Assert.That(field.Board.At(3, 0), Is.EqualTo(Cell.Moss), "支えのない子は床まで落ちる");
        }

        [Test]
        public void 接地して猶予が切れると確定する()
        {
            var config = InstantConfig();
            config.GravityTicks = new[] { 1 };
            config.LockDelayTicks = 5;
            var field = NewField(config, Board.Empty, EmberPair, MossPair);
            Ticks(field, Board.SpawnY);
            Assert.That(field.Active!.Value.Y, Is.EqualTo(0));

            Ticks(field, config.LockDelayTicks - 1);
            Assert.That(field.Active!.Value.Pair, Is.EqualTo(EmberPair), "猶予の間は確定しない");

            field.Tick();
            Assert.That(field.Active!.Value.Pair, Is.EqualTo(MossPair));
            Assert.That(field.Board.At(Board.SpawnX, 0), Is.EqualTo(Cell.Ember));
        }

        [Test]
        public void 接地したまま操作し続けても必ず確定する()
        {
            var config = InstantConfig();
            config.GravityTicks = new[] { 1 };
            config.LockDelayTicks = 5;
            config.MaxLockResets = 3;
            var field = NewField(config, Board.Empty, EmberPair, MossPair);
            Ticks(field, Board.SpawnY + 1);

            var locked = false;
            var maxResets = 0;
            var limit = (config.MaxLockResets + 2) * config.LockDelayTicks;
            for (var i = 0; i < limit && !locked; i++)
            {
                field.Apply(i % 2 == 0 ? GameCommand.MoveLeft : GameCommand.MoveRight);
                maxResets = Math.Max(maxResets, field.LockResets);
                field.Tick();
                locked = (field.TakeEvents() & FieldEvents.Locked) != 0;
            }

            Assert.That(locked, Is.True);
            Assert.That(maxResets, Is.EqualTo(config.MaxLockResets));
        }

        [Test]
        public void ソフトドロップは次のペアへ持ち越さない()
        {
            var field = NewField(Board.Empty, EmberPair, MossPair);
            Drop(field);
            var y = field.Active!.Value.Y;
            field.Tick();
            Assert.That(field.Active!.Value.Y, Is.EqualTo(y), "押し直すまで速く落ちない");
        }

        [Test]
        public void 四個そろえると消えて得点が入る()
        {
            var field = NewField(Parse("EE...."), EmberPair, MossPair);
            field.Apply(GameCommand.RotateClockwise);
            Drop(field);
            Assert.That(field.Board, Is.EqualTo(Board.Empty));
            Assert.That(field.Score, Is.EqualTo(40));
            Assert.That(field.MaxChain, Is.EqualTo(1));
        }

        [Test]
        public void 意図的に組んだ2連鎖が成立する()
        {
            var field = NewField(TwoChainBoard(), EmberPair, TidePair);
            field.Apply(GameCommand.MoveLeft);
            Drop(field);
            Assert.That(field.MaxChain, Is.EqualTo(2));
            Assert.That(field.Score, Is.EqualTo(200));
            Assert.That(field.Board, Is.EqualTo(Parse(".T....")));
        }

        [Test]
        public void 解決はフェーズを順に通る()
        {
            var field = NewField(TimedConfig(), Parse("M.....", "EE...."), EmberPair, TidePair);
            field.Apply(GameCommand.RotateClockwise);
            field.Apply(GameCommand.SoftDropOn);

            var phases = new List<FieldPhase> { field.Phase };
            for (var i = 0; i < MaxSteps && (phases.Count == 1 || field.Phase != FieldPhase.Falling); i++)
            {
                field.Tick();
                if (phases[phases.Count - 1] != field.Phase) { phases.Add(field.Phase); }
            }

            Assert.That(phases, Is.EqualTo(new[]
            {
                FieldPhase.Falling,
                FieldPhase.Popping,
                FieldPhase.Settling,
                FieldPhase.Spawning,
                FieldPhase.Falling,
            }));
            Assert.That(field.Board, Is.EqualTo(Parse("M.....")));
        }

        [Test]
        public void 消去の演出中は盤面にまだ残っている()
        {
            var field = NewField(TimedConfig(), Parse("EE...."), EmberPair, MossPair);
            field.Apply(GameCommand.RotateClockwise);
            field.Apply(GameCommand.SoftDropOn);
            RunUntil(field, FieldPhase.Popping);
            Assert.That(field.PoppingGroups, Has.Count.EqualTo(1));
            Assert.That(field.Board.At(0, 0), Is.EqualTo(Cell.Ember));
        }

        [Test]
        public void 演出時間を変えても解決の結果は同じ()
        {
            var instant = PlayTwoChain(InstantConfig());
            var slow = InstantConfig();
            slow.SettleTicksPerRow = 3;
            slow.PopTicks = 17;
            slow.SpawnDelayTicks = 5;
            var timed = PlayTwoChain(slow);

            Assert.That(timed.TickCount, Is.GreaterThan(instant.TickCount), "演出の分だけ時間はかかる");
            Assert.That(timed.Board, Is.EqualTo(instant.Board));
            Assert.That(timed.Score, Is.EqualTo(instant.Score));
            Assert.That(timed.MaxChain, Is.EqualTo(instant.MaxChain));
        }

        [Test]
        public void 段階的な解決は一括の解決と一致する()
        {
            var field = PlayTwoChain(TimedConfig());
            var landed = new ActivePair(EmberPair, 1, 1, PairOrientation.Up);
            var outcome = ChainResolver.ResolveAll(TimedConfig(), TwoChainBoard().Place(landed));
            Assert.That(field.Board, Is.EqualTo(outcome.Board));
            Assert.That(field.Score, Is.EqualTo(outcome.Score));
            Assert.That(field.MaxChain, Is.EqualTo(outcome.Chains));
        }

        [Test]
        public void 予告の枯れ胞子は手番の終わりに着弾する()
        {
            var field = NewField(Board.Empty, TidePair, MossPair);
            field.ReceiveGarbage(6);
            Assert.That(field.Board, Is.EqualTo(Board.Empty), "受けた時点では降らない");
            Drop(field);
            Assert.That(field.PendingGarbage, Is.EqualTo(0));
            Assert.That(field.Board.At(0, 0), Is.EqualTo(Cell.Ash));
            Assert.That(field.Board.At(Board.SpawnX, 2), Is.EqualTo(Cell.Ash), "積んだ胞子の上に降る");
        }

        [Test]
        public void 連鎖が続く間は着弾しない()
        {
            var field = NewField(TimedConfig(), Parse("EE...."), EmberPair, MossPair);
            field.Apply(GameCommand.RotateClockwise);
            field.Apply(GameCommand.SoftDropOn);
            RunUntil(field, FieldPhase.Popping);
            field.ReceiveGarbage(6);
            Assert.That(field.Board.At(5, 0), Is.EqualTo(Cell.Empty));
            Assert.That(field.PendingGarbage, Is.EqualTo(6));

            RunUntilControl(field);
            Assert.That(field.Board.At(5, 0), Is.EqualTo(Cell.Ash));
            Assert.That(field.PendingGarbage, Is.EqualTo(0));
        }

        [Test]
        public void 出現位置が塞がると負ける()
        {
            var field = NewField(Column(Board.VisibleHeight - 2), EmberPair);
            Drop(field);
            Assert.That(field.Phase, Is.EqualTo(FieldPhase.Defeated));
            Assert.That(field.Active, Is.Null);
        }

        [Test]
        public void 出現位置の1段下まで積んでも負けない()
        {
            var field = NewField(Column(Board.VisibleHeight - 1), EmberPair);
            Assert.That(field.Phase, Is.EqualTo(FieldPhase.Falling));
        }

        [Test]
        public void 負けた後の操作は受け付けず時間も進まない()
        {
            var field = NewField(Column(Board.VisibleHeight), EmberPair);
            Assert.That(field.Phase, Is.EqualTo(FieldPhase.Defeated));
            foreach (var command in _allCommands) { Assert.That(field.Apply(command), Is.False); }
            field.Tick();
            Assert.That(field.TickCount, Is.EqualTo(0));
        }

        [Test]
        public void 操作できないフェーズの入力を拒否する()
        {
            var field = NewField(TimedConfig(), Parse("EE...."), EmberPair, MossPair);
            field.Apply(GameCommand.RotateClockwise);
            field.Apply(GameCommand.SoftDropOn);
            RunUntil(field, FieldPhase.Popping);
            Assert.That(field.Apply(GameCommand.MoveLeft), Is.False);
            Assert.That(field.Apply(GameCommand.RotateClockwise), Is.False);
            Assert.That(field.Apply(GameCommand.SoftDropOn), Is.False);
        }

        [Test]
        public void 設定の範囲外を拒否する()
        {
            Assert.Throws<ArgumentException>(() => new SporeConfig { GravityTicks = Array.Empty<int>() }.Validate());
            Assert.Throws<ArgumentException>(() => new SporeConfig { GravityTicks = _slowingGravity }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new SporeConfig { GarbageRate = 0 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new SporeConfig { LockDelayTicks = 0 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new SporeConfig { PopTicks = -1 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new SporeConfig { MaxGarbagePerDrop = 0 }.Validate());
        }

        // 出現列に、連結しないよう色を交互にして積む。
        private static Board Column(int height)
        {
            var board = Board.Empty;
            for (var y = 0; y < height; y++) { board = board.With(Board.SpawnX, y, y % 2 == 0 ? Cell.Moss : Cell.Tide); }
            return board;
        }

        private static PlayerField PlayTwoChain(SporeConfig config)
        {
            var field = NewField(config, TwoChainBoard(), EmberPair, TidePair);
            field.Apply(GameCommand.MoveLeft);
            Drop(field);
            return field;
        }
    }
}
