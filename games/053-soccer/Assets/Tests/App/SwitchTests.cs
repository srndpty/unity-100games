using System.Linq;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>題材固有：選手切替で入力が混線しない。</summary>
    public sealed class SwitchTests
    {
        private static SoccerConfig Exact => new() { ShotSpread = 0, PassSpread = 0 };

        /// <summary>インプレーの場面：青 1 が球を持ち、青 2 は離れた所。赤は遠く。</summary>
        private static Match Scene()
        {
            var m = new Match(Exact, 1);
            m.ForcePlay();
            m.PlacePlayer(0, new Vec2i(-Pitch.HalfLength + 500, -4900));
            m.PlacePlayer(3, new Vec2i(Pitch.HalfLength - 500, 4900));
            m.PlacePlayer(4, new Vec2i(12000, -8000));
            m.PlacePlayer(5, new Vec2i(12000, 8000));
            m.PlacePlayer(1, new Vec2i(-4000, 0));
            m.PlacePlayer(2, new Vec2i(0, 5000));
            m.GiveBall(1);
            return m;
        }

        /// <summary>Flow と同じ順序で 1 Tick：人間の命令 → 味方の CPU → 試合 → 切替。</summary>
        private static void Tick(Match m, HumanControl h, TeamAi blue)
        {
            var c = new PlayerCommand[Match.PlayerCount];
            c[h.Controlled] = h.Command(m);
            blue.Decide(m, h.Controlled, c);
            m.Step(c);
            h.AfterStep(m);
        }

        [Test]
        public void HeldMoveKey_MovesTheNewPlayerAtOnce_WithoutReleasing()
        {
            var m = Scene();
            var h = new HumanControl(autoSwitchOnDefense: false);
            var blue = new TeamAi(Team.Blue, Tactics.Balanced);
            Assert.That(h.Controlled, Is.EqualTo(1));
            h.Apply(InputButton.Right, 0, true, m);
            for (int i = 0; i < 10; i++) { Tick(m, h, blue); }
            Assert.That(m.Players[1].Velocity.X, Is.GreaterThan(0), "押している間は動く");

            h.Apply(InputButton.Switch, 0, true, m);
            Tick(m, h, blue);
            Assert.That(h.Controlled, Is.EqualTo(2), "切替は Tick の後に効く");

            // 右を押したまま：次の Tick から新しい選手が右へ動く（離し直さなくてよい）。古い選手は CPU（役割を持つ）。
            Assert.That(h.Command(m).Move.X, Is.GreaterThan(0), "押している移動キーがすぐ効く");
            var x0 = m.Players[2].Position.X;
            for (int i = 0; i < 10; i++) { Tick(m, h, blue); }
            Assert.That(m.Players[2].Position.X, Is.GreaterThan(x0), "新しい選手が動く");
            Assert.That(blue.RoleOf(1), Is.Not.EqualTo(Role.None), "前の選手は CPU が動かす（役割を持つ）");
            Assert.That(blue.RoleOf(2), Is.EqualTo(Role.None), "新しい選手は人間");
        }

        [Test]
        public void PassPressedInTheSameTickAsSwitch_ComesFromTheOldPlayer_Once()
        {
            var m = Scene();
            var h = new HumanControl(autoSwitchOnDefense: false);
            var blue = new TeamAi(Team.Blue, Tactics.Balanced);
            h.Apply(InputButton.Pass, 0, true, m);
            h.Apply(InputButton.Switch, 0, true, m);
            Tick(m, h, blue);
            var kicks = m.Events.Where(e => e.Kind == MatchEventKind.Kick).ToList();
            Assert.That(kicks, Has.Count.EqualTo(1));
            Assert.That(kicks[0].Player, Is.EqualTo(1), "押した時点の選手から出る");

            // 次の Tick に同じパスが出ない（行動は 1 回で使い切る）。
            Tick(m, h, blue);
            Assert.That(m.Events.Count(e => e.Kind == MatchEventKind.Kick), Is.EqualTo(0));
        }

        [Test]
        public void SwitchThenPass_InOrder_ThePassBelongsToTheNewPlayer()
        {
            // 切替の Tick の後に押したパスは、新しい選手の行動になる（新しい選手は球を持っていないのでタックルの扱い）。
            var m = Scene();
            var h = new HumanControl(autoSwitchOnDefense: false);
            var blue = new TeamAi(Team.Blue, Tactics.Balanced);
            h.Apply(InputButton.Switch, 0, true, m);
            Tick(m, h, blue);
            Assert.That(h.Controlled, Is.EqualTo(2));
            h.Apply(InputButton.Pass, 0, true, m);
            var c = h.Command(m);
            Assert.That(c.Action, Is.EqualTo(PlayerAction.Tackle), "球を持っていない選手の J はタックル");
        }

        [Test]
        public void AfterAPass_ControlMovesToTheReceiver()
        {
            var m = Scene();
            var h = new HumanControl(autoSwitchOnDefense: false);
            var blue = new TeamAi(Team.Blue, Tactics.Balanced);
            h.Apply(InputButton.Up, 0, true, m);
            h.Apply(InputButton.Pass, 0, true, m);
            Tick(m, h, blue);
            Assert.That(m.Events.Any(e => e.Kind == MatchEventKind.Kick && e.Player == 1), Is.True);
            Assert.That(h.Controlled, Is.EqualTo(2), "受け手へ");
            Assert.That(h.Command(m).Move.Y, Is.GreaterThan(0), "押していた上キーは受け手にそのまま効く");
        }

        [Test]
        public void WhenTheCpuTeammateTraps_ControlMovesToIt_AndOnDefenseToTheNearest()
        {
            var m = Scene();
            m.PlacePlayer(1, new Vec2i(-9000, 0));
            m.PlaceBall(new Vec2i(-500, 4800), new Vec2i(0, 50));
            var h = new HumanControl(autoSwitchOnDefense: true);
            var blue = new TeamAi(Team.Blue, Tactics.Balanced);
            for (int i = 0; i < 40 && m.Owner < 0; i++) { Tick(m, h, blue); }
            Assert.That(m.Owner, Is.EqualTo(2));
            Assert.That(h.Controlled, Is.EqualTo(2), "味方の CPU が止めたらその選手へ");

            // 相手が球を取ったら、球に近い方へ。
            var d = Scene();
            d.PlacePlayer(4, new Vec2i(1000, 5000));
            d.GiveBall(4);
            d.PlacePlayer(2, new Vec2i(-1000, 5000));
            var h2 = new HumanControl(autoSwitchOnDefense: true);
            var c = new PlayerCommand[Match.PlayerCount];
            d.PlaceBall(new Vec2i(-300, 5000), new Vec2i(-10, 0));
            for (int i = 0; i < 20 && !d.Events.Any(e => e.Kind == MatchEventKind.Capture && e.Team == Team.Red); i++)
            {
                c[4] = new PlayerCommand(d.Ball.Position - d.Players[4].Position);
                d.Step(c);
                h2.AfterStep(d);
            }

            Assert.That(h2.Controlled, Is.EqualTo(2));
        }

        [Test]
        public void ReleaseAll_ClearsHeldKeysAndPendingActions()
        {
            var m = Scene();
            var h = new HumanControl();
            h.Apply(InputButton.Right, 0, true, m);
            h.Apply(InputButton.Shoot, 0, true, m);
            h.ReleaseAll();
            var c = h.Command(m);
            Assert.That(c.Move, Is.EqualTo(Vec2i.Zero));
            Assert.That(c.Action, Is.EqualTo(PlayerAction.None));
        }

        [Test]
        public void Shot_AimsAtTheCornerByUpDown()
        {
            var m = Scene();
            var h = new HumanControl();
            h.Apply(InputButton.Up, 0, true, m);
            h.Apply(InputButton.Shoot, 0, true, m);
            Assert.That(h.Command(m).Target.Y, Is.GreaterThan(0));
            h.Apply(InputButton.Up, 0, false, m);
            h.Apply(InputButton.Down, 0, true, m);
            h.Apply(InputButton.Shoot, 0, true, m);
            Assert.That(h.Command(m).Target.Y, Is.LessThan(0));
        }
    }
}
