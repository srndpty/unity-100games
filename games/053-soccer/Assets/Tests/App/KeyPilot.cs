using System;
using Game.Core;
using IntGeometry;

namespace Game.App.Tests
{
    /// <summary>
    /// キー操作の人間役。実際の <see cref="HumanControl"/> に、方向キー（8 方向）・J・K・Space の押下と解放を送る。
    /// 判断は <see cref="ReactionTicks"/> ごと（人の反応の遅れ）で、その間は押しているキーのまま。
    /// 球を持てばゴールへ運んで 8 m 以内で K、相手が持っていれば球へ走って届けば J、球から遠く味方の方がずっと近ければ Space。
    /// Core の素朴な人間役（毎 Tick 一番近い選手へ切り替え、行動を毎 Tick 命じる）より実際の操作に近い。
    /// </summary>
    internal sealed class KeyPilot
    {
        public const int ReactionTicks = 6;

        private readonly bool[] _held = new bool[4];
        private int _lastSwitch = int.MinValue;

        /// <summary>1 試合をこの人間役（青）と CPU（赤）で行い、集計を返す。</summary>
        public static MatchStats Play(Match match, AiSkill redSkill, Tactics red)
        {
            var human = new HumanControl();
            var blue = new TeamAi(Team.Blue, Tactics.Balanced);
            var redAi = new TeamAi(Team.Red, red, redSkill);
            var pilot = new KeyPilot();
            var stats = new MatchStats();
            var commands = new PlayerCommand[Match.PlayerCount];
            human.Begin(match);
            while (match.Phase != MatchPhase.Ended)
            {
                if (match.Clock % ReactionTicks == 0) { pilot.Think(match, human); }
                Array.Clear(commands, 0, commands.Length);
                commands[human.Controlled] = human.Command(match);
                blue.Decide(match, human.Controlled, commands);
                redAi.Decide(match, -1, commands);
                match.Step(commands);
                stats.Observe(match);
                human.AfterStep(match);
            }

            return stats;
        }

        private void Think(Match m, HumanControl human)
        {
            var me = m.Players[human.Controlled];
            var owner = m.Owner;
            var mine = owner == human.Controlled || m.Taker == human.Controlled;
            var target = mine ? new Vec2i(Pitch.HalfLength, 0) : m.Ball.Position;
            var d = target - me.Position;
            Hold(human, m, InputButton.Right, d.X > 300);
            Hold(human, m, InputButton.Left, d.X < -300);
            Hold(human, m, InputButton.Up, d.Y > 300);
            Hold(human, m, InputButton.Down, d.Y < -300);

            if (mine)
            {
                if (m.Phase == MatchPhase.Kickoff) { Tap(human, m, InputButton.Pass); }
                else if (me.Position.X > Pitch.HalfLength - 8000) { Tap(human, m, InputButton.Shoot); }
                return;
            }

            var toBall = Vec2i.DistanceSquared(me.Position, m.Ball.Position);
            if (owner >= 0 && Match.TeamOf(owner) == Team.Red && toBall < 800L * 800)
            {
                Tap(human, m, InputButton.Pass);
                return;
            }

            // 球から遠く、味方の誰かが半分以下の距離にいれば切り替える（連打しない）。
            if (m.Clock - _lastSwitch < 60) { return; }
            for (int slot = 0; slot < Match.TeamSize; slot++)
            {
                var i = Match.PlayerOf(Team.Blue, slot);
                if (i == human.Controlled) { continue; }
                if (Vec2i.DistanceSquared(m.Players[i].Position, m.Ball.Position) * 4 < toBall && toBall > 4000L * 4000)
                {
                    Tap(human, m, InputButton.Switch);
                    _lastSwitch = m.Clock;
                    return;
                }
            }
        }

        private void Hold(HumanControl human, Match m, InputButton direction, bool pressed)
        {
            var k = (int)direction;
            if (_held[k] == pressed) { return; }
            _held[k] = pressed;
            human.Apply(direction, 0, pressed, m);
        }

        private static void Tap(HumanControl human, Match m, InputButton button)
        {
            human.Apply(button, 0, true, m);
            human.Apply(button, 0, false, m);
        }
    }
}
