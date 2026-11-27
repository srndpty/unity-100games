using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum Screen { Title, Help, Battle, Result }

    public sealed class BattleFlow
    {
        private readonly IReadOnlyList<Stats> _stats;
        private uint _seed;
        private int _enemy;
        private bool _focused = true;
        private bool _waiting;
        private double _elapsed;

        public BattleFlow(uint seed, IReadOnlyList<Stats> stats)
        {
            _seed = seed == 0 ? 1U : seed;
            var snapshot = new Stats[stats.Count];
            for (var i = 0; i < snapshot.Length; i++)
            {
                var unit = stats[i];
                snapshot[i] = new Stats(unit.Hp, unit.Move, unit.Power, unit.Accuracy, unit.Range);
            }

            _stats = snapshot;
            Battle = new Battle(_seed, _stats);
        }

        public Battle Battle { get; private set; }
        public Screen Screen { get; private set; }
        public int Selected { get; private set; } = -1;
        public string Notice { get; private set; } = string.Empty;
        public bool Ready => _focused && !_waiting;
        public Routes? Movement => Battle.Movement(Selected, Side.Ally);

        public void SetFocus(bool focused)
        {
            _focused = focused;
            _waiting = true;
        }

        public void PointerIdle()
        {
            if (_focused) { _waiting = false; }
        }

        public bool Help()
        {
            if (!Ready || Screen != Screen.Title) { return false; }
            Screen = Screen.Help;
            return true;
        }

        public bool Title()
        {
            if (!Ready || Screen == Screen.Title) { return false; }
            Screen = Screen.Title;
            return true;
        }

        public bool Start()
        {
            if (!Ready || Screen == Screen.Battle) { return false; }
            Battle = new Battle(_seed, _stats);
            _seed = _seed == uint.MaxValue ? 1 : _seed + 1U;
            Selected = -1;
            Notice = "味方を選び、移動先や攻撃相手をクリック。";
            Screen = Screen.Battle;
            return true;
        }

        public bool ClickCell(int cell)
        {
            if (!Ready || Screen != Screen.Battle || Battle.Turn != Side.Ally || !Battle.ValidCell(cell)) { return false; }
            var occupant = Battle.Occupant(cell);
            if (occupant >= 0 && Battle.Team(occupant) == Side.Ally)
            {
                Selected = occupant;
                Notice = Battle.Units[occupant].Done ? "このユニットは行動済みです。" : "移動先、または射程内の敵を選択。";
                return true;
            }

            if (occupant >= 0)
            {
                var strike = Battle.Attack(Selected, occupant, Side.Ally);
                if (strike == Strike.Rejected) { return false; }
                Notice = strike == Strike.Hit ? "命中" : "回避された";
                CheckResult();
                return true;
            }

            if (!Battle.Move(Selected, cell, Side.Ally)) { return false; }
            Notice = "移動しました。射程内の敵を攻撃できます。";
            return true;
        }

        public bool EndTurn()
        {
            if (!Ready || Screen != Screen.Battle || !Battle.EndTurn(Side.Ally)) { return false; }
            Selected = -1;
            _enemy = Battle.AllyCount;
            _elapsed = 0;
            Notice = "敵が行動中。";
            return true;
        }

        public bool Advance(double seconds, double interval)
        {
            if (!_focused || Screen != Screen.Battle || Battle.Turn != Side.Enemy) { return false; }
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds) ||
                interval <= 0 || double.IsNaN(interval) || double.IsInfinity(interval))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            _elapsed = Math.Min(interval, _elapsed + seconds);
            if (_elapsed < interval) { return false; }
            _elapsed = 0;
            return StepEnemy();
        }

        // 撮影とテストでは実時間とウィンドウの状態に依存せず1手ずつ進める。
        public bool StepEnemy()
        {
            if (Screen != Screen.Battle || Battle.Turn != Side.Enemy) { return false; }
            if (_enemy < Battle.Units.Count)
            {
                var from = Battle.Units[_enemy].Cell;
                var action = Tactics.Act(Battle, _enemy);
                if (action.Strike != Strike.Rejected)
                {
                    var encounter = $"{NameOf(_enemy)} → {NameOf(action.Order.Target)}";
                    Notice = action.Strike == Strike.Hit
                        ? $"{encounter}：{action.Damage}ダメージ" + (Battle.Units[action.Order.Target].Hp == 0 ? "・撃破" : "")
                        : $"{encounter}：回避";
                }
                else if (action.Order.Cell >= 0)
                {
                    Notice = action.Order.Cell == from ? $"{NameOf(_enemy)}は待機" : $"{NameOf(_enemy)}が移動";
                }

                _enemy++;
                CheckResult();
            }
            else
            {
                Battle.EndTurn(Side.Enemy);
                Notice = "味方のターン。";
            }

            return true;
        }

        public static string NameOf(int id) => id switch
        {
            0 => "前衛",
            1 => "盾兵",
            2 => "斥候",
            3 => "弓兵",
            4 => "指揮官",
            7 => "敵弓",
            _ => "護衛",
        };

        private void CheckResult()
        {
            if (Battle.Result != Result.Playing) { Screen = Screen.Result; }
        }
    }
}
