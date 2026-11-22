using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Game.Core;
using TileMotion;

namespace Game.App
{
    /// <summary>
    /// 1 回の部屋の滞在ぶんの入力の列。空白区切りの区間で書く。区間は「押しているキー」と「Tick 数」で、
    /// キーは <c>L</c> 左・<c>R</c> 右・<c>J</c> ジャンプ・<c>S</c> ショット、何も押さないときは <c>-</c>。例：<c>R20 RJ8 S1 -1</c>。
    /// </summary>
    public sealed class RouteScript
    {
        /// <summary>1 回の滞在の入力の上限（120 秒）。</summary>
        public const int MaxTicks = 120 * Units.TickRate;

        private static readonly char[] _separators = { ' ' };

        private readonly PlayerInput[] _inputs;

        private RouteScript(PlayerInput[] inputs)
        {
            _inputs = inputs;
            Inputs = Array.AsReadOnly(inputs);
        }

        public ReadOnlyCollection<PlayerInput> Inputs { get; }

        public int Length => _inputs.Length;

        /// <summary>部屋に入ってから <paramref name="tick"/> Tick 目（0 始まり）の入力。列の後は何も押さない。</summary>
        public PlayerInput InputAt(long tick) => tick >= 0 && tick < _inputs.Length ? _inputs[tick] : PlayerInput.None;

        public static RouteScript Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var inputs = new List<PlayerInput>();
            foreach (var token in text.Split(_separators, StringSplitOptions.RemoveEmptyEntries))
            {
                var digits = token.Length;
                while (digits > 0 && char.IsDigit(token[digits - 1])) { digits--; }
                if (digits == 0 || digits == token.Length)
                {
                    throw new FormatException($"区間 '{token}' はキーと Tick 数で書いてください。");
                }

                var keys = token.Substring(0, digits);
                if (!int.TryParse(token.AsSpan(digits), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks < 1 || inputs.Count + ticks > MaxTicks)
                {
                    throw new FormatException($"区間 '{token}' の Tick 数が範囲外です（合計 {MaxTicks} まで）。");
                }

                var input = ParseKeys(keys, token);
                for (var i = 0; i < ticks; i++) { inputs.Add(input); }
            }

            return new RouteScript(inputs.ToArray());
        }

        /// <summary>入力の列を区間の書式にする（同じ入力が続く間を 1 区間にまとめる）。</summary>
        public static string Format(IReadOnlyList<PlayerInput> inputs)
        {
            if (inputs == null) { throw new ArgumentNullException(nameof(inputs)); }
            var parts = new List<string>();
            var i = 0;
            while (i < inputs.Count)
            {
                var j = i;
                while (j < inputs.Count && inputs[j] == inputs[i]) { j++; }
                parts.Add(Keys(inputs[i]) + (j - i).ToString(CultureInfo.InvariantCulture));
                i = j;
            }

            return string.Join(" ", parts);
        }

        private static string Keys(PlayerInput input)
        {
            var keys = (input.Horizontal < 0 ? "L" : input.Horizontal > 0 ? "R" : string.Empty)
                + (input.Jump ? "J" : string.Empty)
                + (input.Shoot ? "S" : string.Empty);
            return keys.Length == 0 ? "-" : keys;
        }

        private static PlayerInput ParseKeys(string keys, string token)
        {
            if (keys == "-") { return PlayerInput.None; }
            var horizontal = 0;
            bool jump = false, shoot = false;
            foreach (var key in keys)
            {
                switch (key)
                {
                    case 'L' when horizontal == 0: horizontal = -1; break;
                    case 'R' when horizontal == 0: horizontal = 1; break;
                    case 'J' when !jump: jump = true; break;
                    case 'S' when !shoot: shoot = true; break;
                    default: throw new FormatException($"区間 '{token}' のキー '{key}' は使えないか、重複しています。");
                }
            }

            return new PlayerInput(horizontal, jump, shoot);
        }
    }

    /// <summary>
    /// 部屋に入るたびの入力の列で操作する操縦役（撮影・テスト用）。何回目の滞在か（<see cref="Expedition.RoomEntries"/>）と、
    /// 部屋に入ってからの Tick 数だけで入力が決まる。列を使い切った後の滞在は <see cref="Fallback"/> に任せる（なければ何も押さない）。
    /// 被弾で押し戻されると列とずれるので、被弾しない列であることをテストで確かめる。
    /// </summary>
    public sealed class RoutePilot : IPilot
    {
        private readonly RouteScript[] _routes;

        public RoutePilot(IReadOnlyList<RouteScript> routes, IPilot? fallback = null)
        {
            if (routes == null) { throw new ArgumentNullException(nameof(routes)); }
            _routes = new RouteScript[routes.Count];
            for (var i = 0; i < _routes.Length; i++)
            {
                _routes[i] = routes[i] ?? throw new ArgumentException($"{i} 番目の列が null です。", nameof(routes));
            }

            Fallback = fallback;
        }

        public int Count => _routes.Length;

        public IPilot? Fallback { get; }

        public PlayerInput InputFor(Expedition expedition)
        {
            if (expedition == null) { throw new ArgumentNullException(nameof(expedition)); }
            var visit = expedition.RoomEntries - 1;
            if (visit < _routes.Length) { return _routes[visit].InputAt(expedition.RoomTicks); }
            return Fallback?.InputFor(expedition) ?? PlayerInput.None;
        }
    }
}
