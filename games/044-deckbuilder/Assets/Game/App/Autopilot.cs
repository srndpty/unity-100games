using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>キーやマウスの代わりに意図を 1 つずつ決める操縦役（撮影・テスト用）。</summary>
    public interface IPilot
    {
        /// <summary>次の意図。待つ（演出中など）なら null。</summary>
        Intent? Decide(DeckFlow flow);
    }

    /// <summary>
    /// 操縦役。戦闘はそのターンに使う手順を、戦闘の複製（<see cref="Battle.Clone"/>）で先読みして選ぶ。
    /// 先読みは複製の乱数で無作為の効果まで見るので、人より少し有利（テストと撮影の駆動役）。
    /// <see cref="Depth"/> を 1 にし <see cref="MistakePercent"/> を与えると、1 枚ずつ貪欲に選び時々悪手を打つ人寄りの操縦役になる。
    /// </summary>
    public sealed class Autopilot : IPilot
    {
        private static readonly string[] _removeOrder = { "strike", "guard" };

        private static readonly Dictionary<string, int> _cardValue = new(StringComparer.Ordinal)
        {
            ["finisher"] = 9,
            ["temper"] = 9,
            ["bulwark"] = 8,
            ["fumes"] = 8,
            ["smog"] = 7,
            ["venom"] = 7,
            ["sweep"] = 7,
            ["quickcut"] = 6,
            ["crush"] = 6,
            ["twin"] = 5,
            ["brace"] = 5,
            ["fortress"] = 5,
            ["sap"] = 5,
            ["shieldbash"] = 4,
            ["sparks"] = 4,
            ["kindle"] = 4,
            ["purge"] = 3,
            ["glance"] = 2,
        };

        private readonly GameRng _rng;
        private readonly Dictionary<uint, double> _memo = new();
        private int _budget;

        /// <param name="depth">1 ターンに先読みするカードの枚数（大きいほど強い）。</param>
        /// <param name="mistakePercent">払えるカードを無作為に使う確率（人寄りの操縦役）。</param>
        public Autopilot(int depth = 10, int mistakePercent = 0, int seed = 1)
        {
            if (depth < 1) { throw new ArgumentOutOfRangeException(nameof(depth)); }
            if (mistakePercent < 0 || mistakePercent > 100) { throw new ArgumentOutOfRangeException(nameof(mistakePercent)); }
            Depth = depth;
            MistakePercent = mistakePercent;
            _rng = new GameRng(seed);
        }

        public int Depth { get; }

        public int MistakePercent { get; }

        /// <summary>1 回の判断で調べる局面の上限。</summary>
        public int NodeBudget { get; set; } = 3000;

        public Intent? Decide(DeckFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            if (flow.Paused) { return new Intent(IntentKind.Cancel); }
            var run = flow.Run;
            switch (flow.Screen)
            {
                case AppScreen.Title:
                    return new Intent(IntentKind.Confirm);
                case AppScreen.HowTo:
                    return new Intent(IntentKind.Cancel);
                case AppScreen.Route:
                    return new Intent(IntentKind.Option, ChooseRoute(run!));
                case AppScreen.Rest:
                    return new Intent(IntentKind.Option, run!.Hp * 100 < run.MaxHp * 75 ? 0 : 1);
                case AppScreen.Reward:
                    return Reward(flow, run!);
                case AppScreen.Battle:
                    return flow.AcceptsBattleInput ? Battle(flow, run!.Battle!) : null;
                default:
                    return null;
            }
        }

        private static int ChooseRoute(Run run)
        {
            if (run.Options.Count == 1) { return 0; }
            return run.Hp * 100 >= run.MaxHp * 60 ? 1 : 0;
        }

        private static Intent Reward(DeckFlow flow, Run run)
        {
            if (!run.RewardTaken)
            {
                var best = 0;
                for (int i = 1; i < run.RewardOptions.Count; i++)
                {
                    if (Value(run.RewardOptions[i]) > Value(run.RewardOptions[best])) { best = i; }
                }

                return new Intent(IntentKind.Option, best);
            }

            if (run.CanRemove)
            {
                if (flow.RemovalCandidate != null) { return new Intent(IntentKind.ConfirmRemoval); }
                foreach (var id in _removeOrder)
                {
                    foreach (var c in run.Deck)
                    {
                        if (c.Def.Id == id) { return new Intent(IntentKind.PickRemoval, c.Id); }
                    }
                }
            }

            return new Intent(IntentKind.Leave);
        }

        private static int Value(CardDef card) => _cardValue.TryGetValue(card.Id, out var v) ? v : 0;

        private Intent Battle(DeckFlow flow, Battle battle)
        {
            var (cardId, target) = Plan(battle);
            if (cardId < 0) { return new Intent(IntentKind.EndTurn); }
            var card = Find(battle, cardId);
            if (!card.Def.NeedsTarget) { return new Intent(IntentKind.SelectCard, cardId); }
            if (flow.Targeting.CardId == cardId) { return new Intent(IntentKind.ClickEnemy, target!.Value); }
            return new Intent(IntentKind.SelectCard, cardId);
        }

        /// <summary>次に使うカードと対象。ターンを終えるなら (-1, null)。</summary>
        public (int CardId, int? Target) Plan(Battle battle)
        {
            if (battle == null) { throw new ArgumentNullException(nameof(battle)); }
            var moves = Moves(battle);
            if (moves.Count == 0) { return (-1, null); }
            if (MistakePercent > 0 && _rng.Chance(MistakePercent)) { return moves[_rng.Next(moves.Count)]; }
            _memo.Clear();
            _budget = NodeBudget;
            var bestScore = Evaluate(battle);
            (int, int?) best = (-1, null);
            foreach (var m in moves)
            {
                var copy = battle.Clone();
                copy.Play(m.CardId, m.Target);
                var score = Search(copy, Depth - 1);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = m;
                }
            }

            return best;
        }

        private double Search(Battle battle, int depth)
        {
            var key = battle.Hash();
            if (_memo.TryGetValue(key, out var known)) { return known; }
            var best = Evaluate(battle);
            if (depth > 0 && !battle.IsOver && _budget > 0)
            {
                foreach (var m in Moves(battle))
                {
                    if (--_budget <= 0) { break; }
                    var copy = battle.Clone();
                    copy.Play(m.CardId, m.Target);
                    best = Math.Max(best, Search(copy, depth - 1));
                }
            }

            _memo[key] = best;
            return best;
        }

        /// <summary>使える手（同じ種類のカードは 1 枚だけ、対象は生きている敵ごと）。</summary>
        private static List<(int CardId, int? Target)> Moves(Battle battle)
        {
            var list = new List<(int, int?)>();
            if (battle.IsOver) { return list; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in battle.Hand)
            {
                if (!battle.Affordable(c) || !seen.Add(c.Def.Id)) { continue; }
                if (!c.Def.NeedsTarget)
                {
                    list.Add((c.Id, null));
                    continue;
                }

                foreach (var e in battle.Enemies)
                {
                    if (e.Alive) { list.Add((c.Id, e.Id)); }
                }
            }

            return list;
        }

        /// <summary>ターンをここで終えたときの評価。勝ちは最大、負けは最小。敵の次の攻撃からブロックを引いた分を失う HP とみなす。</summary>
        private static double Evaluate(Battle battle)
        {
            if (battle.Phase == BattlePhase.Won) { return 1e6 + battle.Player.Hp; }
            if (battle.Phase == BattlePhase.Lost) { return -1e6; }
            var player = battle.Player;
            double score = 0;
            var incoming = 0;
            foreach (var e in battle.Enemies)
            {
                if (!e.Alive) { continue; }
                score -= (e.Hp * 10) + 25;
                score += e.Status(StatusKind.Poison) * 6;
                score += e.Status(StatusKind.Vulnerable) * 6;
                score += e.Status(StatusKind.Weak) * 5;
                if (e.Intent == null) { continue; }
                foreach (var effect in e.Intent.Effects)
                {
                    if (effect.Kind == EffectKind.Damage) { incoming += battle.PreviewAttack(e, player, effect.Amount); }
                }
            }

            var loss = Math.Max(0, incoming - player.Block) + player.Status(StatusKind.Poison);
            if (loss >= player.Hp) { score -= 5e5; }
            score -= loss * 14;
            score += player.Status(StatusKind.Strength) * 30;
            score += player.Status(StatusKind.Bulwark) * 28;
            score += player.Status(StatusKind.Miasma) * 30;
            return score;
        }

        private static CardRef Find(Battle battle, int cardId)
        {
            foreach (var c in battle.Hand)
            {
                if (c.Id == cardId) { return c; }
            }

            throw new InvalidOperationException($"手札にカード {cardId} がありません。");
        }
    }
}
