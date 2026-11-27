using System;
using Game.Core;

namespace Game.App
{
    public static class BattleDemo
    {
        public static void Act(BattleFlow flow, int id, AttackPriority priority = AttackPriority.Objective)
        {
            var battle = flow.Battle;
            if (!battle.CanAct(id, Side.Ally)) { return; }
            var order = Tactics.Choose(battle, id, priority);
            if (!flow.ClickCell(battle.Units[id].Cell) ||
                (order.Cell != battle.Units[id].Cell && !flow.ClickCell(order.Cell)) ||
                (order.Target >= 0 && !flow.ClickCell(battle.Units[order.Target].Cell)))
            {
                throw new InvalidOperationException($"自動操縦の操作が拒否されました：{id}");
            }
        }
    }
}
