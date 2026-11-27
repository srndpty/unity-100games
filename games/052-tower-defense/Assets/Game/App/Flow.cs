using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum Screen { Title, Help, Play, Result }
    public enum Intent { Start, Help, Title, Slot, Kind, Build, Upgrade, Sell, Wave, Resume }

    public sealed class Flow
    {
        private readonly Queue<(Intent Action, int Value, int Revision)> _inputs = new();
        private readonly Rules _rules;
        private bool _focused = true;
        private bool _waiting;
        private double _remainder;
        private int _selectionRevision;

        public Flow(Rules rules)
        {
            _rules = rules;
            Defense = new Defense(rules);
        }

        public Defense Defense { get; private set; }
        public Screen Screen { get; private set; }
        public bool Paused { get; private set; }
        public int Slot { get; private set; } = -1;
        public TowerKind Kind { get; private set; }
        public bool AcceptsOrders => _focused && !_waiting && !Paused && Screen == Screen.Play;
        public string Notice { get; private set; } = "";

        public void Focus(bool focused)
        {
            _focused = focused;
            _waiting = true;
            _inputs.Clear();
            _remainder = 0;
            if (!focused && Screen == Screen.Play) { Paused = true; }
        }

        public void PointerIdle()
        {
            if (_focused) { _waiting = false; }
        }

        public void Push(Intent action, int value = 0)
        {
            if (_focused && !_waiting) { _inputs.Enqueue((action, value, _selectionRevision)); }
        }

        public void Advance(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0) { throw new ArgumentOutOfRangeException(nameof(seconds)); }
            Dispatch();
            if (Paused || Screen != Screen.Play || Defense.Phase != Phase.Running) { return; }
            _remainder += Math.Min(seconds, 0.25) * Defense.TickRate;
            var ticks = (int)_remainder;
            _remainder -= ticks;
            for (var i = 0; i < ticks && Defense.Phase == Phase.Running; i++) { Step(); }
        }

        public void Step()
        {
            if (Screen != Screen.Play || Paused) { return; }
            var wasRunning = Defense.Phase == Phase.Running;
            Defense.Step();
            if (wasRunning && Defense.Phase == Phase.Preparation) { Notice = "Waveを防衛。次のWaveを準備しよう。"; }
            if (Defense.Phase != Phase.Running) { _remainder = 0; }
            if (Defense.Phase is Phase.Won or Phase.Lost) { Screen = Screen.Result; }
        }

        public void Dispatch()
        {
            while (_inputs.TryDequeue(out var input))
            {
                var before = Screen;
                if (input.Action is Intent.Build or Intent.Upgrade or Intent.Sell && input.Revision != _selectionRevision)
                {
                    Notice = "選択や塔が変わりました。内容を確認して操作してください。";
                    continue;
                }

                Apply(input.Action, input.Value);
                if (before != Screen || input.Action == Intent.Resume) { _inputs.Clear(); }
            }
        }

        private void Apply(Intent action, int value)
        {
            if (action == Intent.Title)
            {
                Screen = Screen.Title;
                return;
            }

            if (action == Intent.Start && Screen != Screen.Play)
            {
                Defense = new Defense(_rules);
                Screen = Screen.Play;
                Slot = -1;
                Kind = TowerKind.Bow;
                Paused = false;
                _remainder = 0;
                Notice = "建設枠を選び、塔を建てよう。";
                return;
            }

            if (action == Intent.Help && Screen == Screen.Title) { Screen = Screen.Help; }
            if (Screen != Screen.Play) { return; }
            if (action == Intent.Resume) { Paused = false; }
            if (Paused) { return; }
            switch (action)
            {
                case Intent.Slot when value >= 0 && value < Canyon.Slots.Count:
                    if (Slot != value) { _selectionRevision++; }
                    Slot = value;
                    Notice = "";
                    break;
                case Intent.Kind when (uint)value < 4:
                    if (Kind != (TowerKind)value) { _selectionRevision++; }
                    Kind = (TowerKind)value;
                    break;
                case Intent.Build:
                    Notice = DescribeOrder(Defense.Build(Slot, Kind), "建設した");
                    break;
                case Intent.Upgrade:
                    Notice = DescribeOrder(Defense.Upgrade(Slot), "強化した");
                    break;
                case Intent.Sell:
                    Notice = DescribeOrder(Defense.Sell(Slot), "売却した");
                    break;
                case Intent.Wave:
                    if (Defense.StartWave()) { Notice = "敵が接近中"; }
                    break;
            }
        }

        private string DescribeOrder(CommandResult result, string applied)
        {
            if (result == CommandResult.Applied) { _selectionRevision++; }
            return Describe(result, applied);
        }

        private static string Describe(CommandResult result, string applied) => result switch
        {
            CommandResult.Applied => applied,
            CommandResult.Funds => "資金が足りない",
            CommandResult.Maximum => "最高レベル",
            CommandResult.Occupied => "この枠には塔がある",
            CommandResult.Empty => "空き枠",
            _ => "建設枠を選んでください",
        };
    }

    public static class Demonstration
    {
        private static readonly (int Slot, TowerKind Kind)[] _opening =
        {
            (2, TowerKind.Bow), (4, TowerKind.Bow), (7, TowerKind.Bow),
            (5, TowerKind.Cannon), (6, TowerKind.Arcane), (0, TowerKind.Frost),
            (8, TowerKind.Arcane), (1, TowerKind.Bow), (9, TowerKind.Bow), (3, TowerKind.Bow),
        };

        public static void Prepare(Flow flow)
        {
            if (!flow.AcceptsOrders || flow.Defense.Phase != Phase.Preparation)
            {
                throw new InvalidOperationException("自動操縦を準備中に実行できません。");
            }

            foreach (var entry in _opening)
            {
                Send(flow, Intent.Slot, entry.Slot);
                Send(flow, Intent.Kind, (int)entry.Kind);
                if (flow.Slot != entry.Slot || flow.Kind != entry.Kind) { throw new InvalidOperationException("塔の選択に失敗しました。"); }
                if (flow.Defense.Towers[entry.Slot] != null || flow.Defense.Money < flow.Defense.Cost(entry.Kind)) { continue; }
                Send(flow, Intent.Build);
                if (flow.Defense.Towers[entry.Slot]?.Kind != entry.Kind) { throw new InvalidOperationException("建設に失敗しました。"); }
            }

            foreach (var entry in _opening)
            {
                Send(flow, Intent.Slot, entry.Slot);
                var tower = flow.Defense.Towers[entry.Slot];
                if (tower == null) { continue; }
                while (tower.Level < 3 && flow.Defense.Money >= flow.Defense.UpgradeCost(tower))
                {
                    var before = tower.Level;
                    Send(flow, Intent.Upgrade);
                    if (tower.Level != before + 1) { throw new InvalidOperationException("強化に失敗しました。"); }
                }
            }
        }

        public static void Send(Flow flow, Intent intent, int value = 0)
        {
            flow.Push(intent, value);
            flow.Dispatch();
        }
    }
}
