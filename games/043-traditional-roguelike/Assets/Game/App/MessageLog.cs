using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core;

namespace Game.App
{
    /// <summary>敵と品の名前（作品のオリジナル）。</summary>
    public static class Names
    {
        public static string Of(MonsterKind kind) => kind switch
        {
            MonsterKind.Skitter => "ちょろネズミ",
            MonsterKind.Golem => "岩人形",
            MonsterKind.Archer => "葦の射手",
            MonsterKind.Thief => "くすね鳥",
            MonsterKind.Serpent => "ねむり大蛇",
            _ => kind.ToString(),
        };

        public static string Of(ItemKind kind) => kind switch
        {
            ItemKind.HealPotion => "回復薬",
            ItemKind.PoisonPotion => "毒薬",
            ItemKind.SleepPotion => "眠り薬",
            ItemKind.Bread => "パン",
            ItemKind.BigBread => "大きなパン",
            ItemKind.Stone => "石",
            ItemKind.MapScroll => "地図の巻物",
            ItemKind.WarpScroll => "跳躍の巻物",
            _ => kind.ToString(),
        };

        /// <summary>持ち物の画面の説明。</summary>
        public static string Describe(ItemKind kind, CrawlConfig c) => kind switch
        {
            ItemKind.HealPotion => $"飲むと HP が {c.HealAmount} 回復する。投げると敵が回復する。",
            ItemKind.PoisonPotion => $"飲むと HP が {c.PoisonSelfDamage} 減る（1 は残る）。投げると敵に {c.PoisonThrowDamage} のダメージ。",
            ItemKind.SleepPotion => $"飲むと {c.SleepTurns} 行動眠ってしまう。投げると敵を {c.SleepTurns} 行動眠らせる。",
            ItemKind.Bread => $"食べると満腹度が {c.BreadSatiety} 回復する。",
            ItemKind.BigBread => $"食べると満腹度が {c.BigBreadSatiety} 回復する。",
            ItemKind.Stone => $"使えない。投げると敵に {c.StoneDamage} のダメージ。",
            ItemKind.MapScroll => "読むとこの階の地形と階段が分かる（品と敵は分からない）。",
            ItemKind.WarpScroll => "読むとこの階のどこかへ飛ぶ。",
            _ => string.Empty,
        };

        /// <summary>使うときの動詞。</summary>
        public static string UseVerb(ItemKind kind) => kind switch
        {
            ItemKind.HealPotion or ItemKind.PoisonPotion or ItemKind.SleepPotion => "飲んだ",
            ItemKind.Bread or ItemKind.BigBread => "食べた",
            ItemKind.MapScroll or ItemKind.WarpScroll => "読んだ",
            _ => "使った",
        };

        public static string Reason(RejectReason reason) => reason switch
        {
            RejectReason.Blocked => "そちらへは進めない。",
            RejectReason.NothingToPickUp => "足元には何もない。",
            RejectReason.InventoryFull => "持ち物がいっぱいだ。",
            RejectReason.EmptySlot => "その品はない。",
            RejectReason.CannotUse => "それは使えない（投げる物だ）。",
            RejectReason.CannotDrop => "ここには置けない。",
            RejectReason.NotOnStairs => "ここに階段はない。",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// メッセージ欄。<see cref="PlayerView.Events"/>（見えた出来事だけ）から文を作るので、視界の外の出来事は文にならない。
    /// 行には通し番号を付け、描画は番号の変化で書き換えを決める。
    /// </summary>
    public sealed class MessageLog
    {
        public const int Capacity = 50;

        private readonly List<string> _lines = new();

        public MessageLog()
        {
            Lines = _lines.AsReadOnly();
        }

        public ReadOnlyCollection<string> Lines { get; }

        /// <summary>これまでに足した行の数（書き換えの判定用）。</summary>
        public int Version { get; private set; }

        public void Clear()
        {
            _lines.Clear();
            Version++;
        }

        public void Add(string line)
        {
            if (string.IsNullOrEmpty(line)) { return; }
            _lines.Add(line);
            if (_lines.Count > Capacity) { _lines.RemoveAt(0); }
            Version++;
        }

        /// <summary>直前の操作で見えた出来事を文にして足す。</summary>
        public void AddEvents(PlayerView view)
        {
            foreach (var e in view.Events) { Add(Text(e)); }
        }

        public static string Text(RunEvent e)
        {
            var monster = Names.Of(e.Monster);
            var item = Names.Of(e.Item);
            switch (e.Kind)
            {
                case EventKind.Attack when e.Actor == 0:
                    return e.Value < 0 ? $"攻撃は{monster}に外れた。" : $"{monster}に {e.Value} のダメージ。";
                case EventKind.Attack:
                    return e.Value < 0 ? $"{monster}の攻撃は外れた。" : $"{monster}の攻撃。{e.Value} のダメージを受けた。";
                case EventKind.Shot:
                    return e.Value < 0 ? $"{monster}の矢は外れた。" : $"{monster}の矢。{e.Value} のダメージを受けた。";
                case EventKind.Killed: return $"{monster}を倒した。";
                case EventKind.LevelUp: return $"レベル {e.Value} に上がった！";
                case EventKind.PickedUp: return $"{item}を拾った。";
                case EventKind.FullCannotPickUp: return $"持ち物がいっぱいで{item}を拾えない。";
                case EventKind.Dropped when e.Actor == 0: return $"{item}を足元に置いた。";
                case EventKind.Dropped: return $"{item}が落ちた。";
                case EventKind.Used: return $"{item}を{Names.UseVerb(e.Item)}。";
                case EventKind.Healed: return $"HP が {e.Value} 回復した。";
                case EventKind.Poisoned: return $"毒で {e.Value} のダメージを受けた。";
                case EventKind.FellAsleep: return "眠ってしまった……";
                case EventKind.WokeUp: return "目が覚めた。";
                case EventKind.Ate: return $"満腹度が {e.Value} 回復した。";
                case EventKind.MapRevealed: return "この階の地形が分かった。";
                case EventKind.Warped: return "どこかへ飛ばされた。";
                case EventKind.Thrown: return $"{item}を投げた。";
                case EventKind.ThrowHit when e.Value > 0: return $"{item}が{monster}に当たった。{e.Value} のダメージ。";
                case EventKind.ThrowHit: return $"{item}が{monster}に当たった。";
                case EventKind.ThrowLanded: return $"{item}は床に落ちた。";
                case EventKind.ItemLost: return $"{item}は消えてしまった。";
                case EventKind.Stolen: return $"{monster}に{item}を盗まれた！";
                case EventKind.Vanished: return $"{monster}は姿を消した。";
                case EventKind.MonsterWoke: return $"{monster}が目を覚ました。";
                case EventKind.MonsterSlept: return $"{monster}は眠った。";
                case EventKind.Hungry: return "おなかが減ってきた。";
                case EventKind.Starving: return "空腹で倒れそうだ！ HP が減っていく。";
                case EventKind.Starve: return string.Empty;
                case EventKind.Descended: return $"{e.Value} 階に降りた。";
                case EventKind.Died: return "力尽きた……";
                case EventKind.Won: return "地上の光が見えた！";
                default: return string.Empty;
            }
        }
    }
}
