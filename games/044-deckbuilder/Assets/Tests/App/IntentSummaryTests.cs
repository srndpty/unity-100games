using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>敵の意図の見積もりが、実際の解決で受ける攻撃と一致する（効果列を書いた順に評価する）。</summary>
    public sealed class IntentSummaryTests
    {
        private static readonly int[] _threeThenEight = { 3, 8 };

        /// <summary>番兵（1 体だけの遭遇 gate）の行動を差し替えたデータ。行動は書いた順に巡回する。</summary>
        private static GameData WardenWith(string moves)
        {
            var enemies = AppLab.DataText("Enemies.txt");
            var start = enemies.IndexOf("enemy warden", StringComparison.Ordinal);
            var end = enemies.IndexOf("enemy moth", StringComparison.Ordinal);
            var warden = "enemy warden 鉄の番兵\nhp 38\norder cycle\n" + moves.Replace(";", "\n", StringComparison.Ordinal) + "\n\n";
            return GameData.Load(
                AppLab.DataText("Cards.txt"),
                enemies.Remove(start, end - start).Insert(start, warden),
                AppLab.DataText("Encounters.txt"),
                new DeckConfig());
        }

        /// <summary>
        /// ターン終了だけを続け、敵の番ごとに「プレイヤーの番の途中」と「敵が行動する直前（ターン終了と脱力・弱体の減少の後）」の
        /// 2 時点で卓から見積もり、続く行動の実際の攻撃（防がれた分を含む）と照合する。最後の番の見積もりを返す。
        /// </summary>
        private static IntentSummary CheckTurns(string moves, int turns)
        {
            var data = WardenWith(moves);
            var config = new DeckConfig();
            var deck = data.StarterDeck.Select((d, i) => new CardRef(i + 1, d)).ToArray();
            var encounter = data.Encounters.First(e => e.Id == "gate");
            var battle = new Battle(data, config, deck, 70, 70, encounter, 1, 0);
            var table = new TableModel(battle, data);
            var applied = 0;
            void ApplyUntil(Func<BattleEvent, bool> stop)
            {
                while (applied < battle.Events.Count && !stop(battle.Events[applied])) { table.Apply(battle.Events[applied++]); }
            }

            IntentSummary last = default;
            for (int turn = 0; turn < turns && !battle.IsOver; turn++)
            {
                ApplyUntil(_ => false);
                Assert.That(table.PlayerTurnOpen, Is.True);
                var seat = table.Seats[1];
                var during = IntentSummary.Of(seat, table, config);
                var from = battle.Events.Count;
                battle.EndTurn();
                ApplyUntil(e => e.Kind == EventKind.EnemyActed);
                Assert.That(table.PlayerTurnOpen, Is.False);
                var before = IntentSummary.Of(seat, table, config);
                var actual = new List<int>();
                for (int i = from; i < battle.Events.Count && battle.Events[i].Kind != EventKind.TurnStarted; i++)
                {
                    var e = battle.Events[i];
                    if (e.Kind == EventKind.Damaged && e.Source == seat.Id) { actual.Add(e.Amount + e.Detail); }
                }

                Assert.That(during.Hits, Is.EqualTo(actual), $"ターン {turn + 1}：プレイヤーの番の途中の見積もり");
                Assert.That(before.Hits, Is.EqualTo(actual), $"ターン {turn + 1}：行動の直前の見積もり");
                last = during;
            }

            return last;
        }

        [Test]
        public void AttackByBlockIsShownAsAnAttackOfTheBlockGainedInTheSameMove()
        {
            var s = CheckTurns("move bash 盾の反撃;do block 8 self;do damage_block player", 3);
            Assert.That(s.Icon, Is.EqualTo(IntentIcon.Attack));
            Assert.That(s.Text, Is.EqualTo("8"));
            var doubled = CheckTurns("move bash 盾の反撃;do block 4 self;do double_block self;do damage_block player", 3);
            Assert.That(doubled.Text, Is.EqualTo("8"));
        }

        [Test]
        public void AttacksOfDifferentAmountsAreListedInOrder()
        {
            var s = CheckTurns("move combo 連打;do damage 3 player;do damage 8 player", 2);
            Assert.That(s.Hits, Is.EqualTo(_threeThenEight));
            Assert.That(s.Text, Is.EqualTo("3+8"));
            Assert.That(s.Total, Is.EqualTo(11));
            Assert.That(CheckTurns("move twin 二連;do damage 4 player;do damage 4 player", 2).Text, Is.EqualTo("4×2"));
        }

        [Test]
        public void StatusesAppliedEarlierInTheMoveAffectLaterAttacks()
        {
            Assert.That(CheckTurns("move mark 刻印;do status vulnerable 2 player;do damage 6 player", 3).Text, Is.EqualTo("9"));
            Assert.That(CheckTurns("move rage 怒り;do damage 4 player;do status strength 2 self;do damage 4 player", 3).Text, Is.EqualTo("8+10"));
            Assert.That(CheckTurns("move tire 疲れ;do status weak 1 self;do damage 8 player", 2).Text, Is.EqualTo("6"));
        }

        [Test]
        public void PlayersVulnerableThatDecaysAtTheTurnEndIsNotCounted()
        {
            // 1 手目で弱体 1 を与え、2 手目の攻撃の時点ではプレイヤーのターン終了で 0 に戻っている。
            var s = CheckTurns("move mark 刻印;do status vulnerable 1 player;move hit 打撃;do damage 10 player", 2);
            Assert.That(s.Text, Is.EqualTo("10"));
            // 弱体 2 なら 1 残って効く。
            Assert.That(CheckTurns("move mark 刻印;do status vulnerable 2 player;move hit 打撃;do damage 10 player", 2).Text, Is.EqualTo("15"));
        }

        [Test]
        public void ShippedEnemiesMatchTheirEstimatesInLongRuns()
        {
            // 敵ごとに、プレイヤーの番の途中と行動の直前の 2 時点の見積もりを、実際の攻撃と照合する。
            // 先に行動する敵の弱体の付与（swarm の蛾の凝視の後の小鬼など）も、プレイヤーの番の途中の見積もりに入っていること。
            var boosted = 0;
            foreach (var seed in Enumerable.Range(1, 40))
            {
                var data = AppLab.Data;
                var config = new DeckConfig();
                var deck = data.StarterDeck.Select((d, i) => new CardRef(i + 1, d)).ToArray();
                var battle = new Battle(data, config, deck, 70, 70, data.Encounters[seed % data.Encounters.Count], seed, 0);
                var table = new TableModel(battle, data);
                var applied = 0;
                for (int turn = 0; turn < 8 && !battle.IsOver; turn++)
                {
                    for (; applied < battle.Events.Count; applied++) { table.Apply(battle.Events[applied]); }
                    var during = new Dictionary<int, IReadOnlyList<int>>();
                    foreach (var seat in table.Seats.Where(x => x.Def != null && x.Alive && x.Intent != null))
                    {
                        during[seat.Id] = IntentSummary.Of(seat, table, config).Hits;
                        var earlierMarks = table.Seats.Any(x => x.Def != null && x.Id < seat.Id && x.Alive && x.Intent != null
                            && x.Intent.Effects.Any(f => f.Kind == EffectKind.Status && f.Target == TargetMode.Player && f.Status == StatusKind.Vulnerable));
                        if (earlierMarks && during[seat.Id].Count > 0) { boosted++; }
                    }

                    var from = battle.Events.Count;
                    battle.EndTurn();
                    var before = new Dictionary<int, IReadOnlyList<int>>();
                    var actual = new Dictionary<int, List<int>>();
                    var cut = false;
                    for (; applied < battle.Events.Count; applied++)
                    {
                        var e = battle.Events[applied];
                        if (e.Kind == EventKind.TurnStarted) { break; }
                        if (e.Kind is EventKind.Defeat or EventKind.Victory) { cut = true; }
                        if (e.Kind == EventKind.EnemyActed)
                        {
                            before[e.Source] = IntentSummary.Of(table.SeatById(e.Source)!, table, config).Hits;
                            actual[e.Source] = new List<int>();
                        }

                        if (e.Kind == EventKind.Damaged && actual.TryGetValue(e.Source, out var hits)) { hits.Add(e.Amount + e.Detail); }
                        table.Apply(e);
                    }

                    if (cut) { break; }
                    foreach (var (id, hit) in actual)
                    {
                        Assert.That(before[id], Is.EqualTo(hit), $"シード {seed} ターン {turn + 1} 敵 {id}：行動の直前");
                        Assert.That(during[id], Is.EqualTo(hit), $"シード {seed} ターン {turn + 1} 敵 {id}：プレイヤーの番の途中");
                    }
                }
            }

            Assert.That(boosted, Is.GreaterThan(0), "先に行動する敵の弱体で増える場面を通っていない");
        }
    }
}
