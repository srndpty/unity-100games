using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>戦闘画面に見せている片側（味方・相手）の姿。出来事を 1 つずつ当てて作り、Core の最終状態は直接見ない。</summary>
    public sealed class SideView
    {
        public bool Visible { get; internal set; }

        public int Slot { get; internal set; }

        public string Name { get; internal set; } = string.Empty;

        public string SpeciesId { get; internal set; } = string.Empty;

        /// <summary>種族の番号（絵を選ぶ）。</summary>
        public int SpeciesIndex { get; internal set; } = -1;

        public int Level { get; internal set; }

        /// <summary>HP の目標（出来事を当てた後の値）。</summary>
        public int Hp { get; internal set; }

        /// <summary>画面に出している HP（Tick ごとに目標へ寄せる）。</summary>
        public int ShownHp { get; internal set; }

        public int MaxHp { get; internal set; }

        /// <summary>状態異常の名前（なければ空）。</summary>
        public string Status { get; internal set; } = string.Empty;

        /// <summary>技を受けて点滅している残り Tick。</summary>
        public int FlashTicks { get; internal set; }

        internal SideView Clone() => (SideView)MemberwiseClone();
    }

    /// <summary>出来事 1 つぶんの見せ方（文・長さ・合図）。</summary>
    public sealed class Beat
    {
        internal Beat(BattleEvent source, string text, int ticks)
        {
            Source = source;
            Text = text;
            Ticks = ticks;
        }

        public BattleEvent Source { get; }

        /// <summary>メッセージ欄の文（空なら文を変えずに HP だけを動かす）。</summary>
        public string Text { get; }

        public int Ticks { get; }
    }

    /// <summary>
    /// 戦闘の出来事の列を、Tick で進む見せ方の列（<see cref="Beat"/>）に変える。状態は Core が先に最後まで決めていて、
    /// 演出はそれを時間をかけて見せるだけ（041 の BattlePresenter と同じ考え）。決定キーで次の文へ送れる。
    /// </summary>
    public sealed class BattlePresenter
    {
        private readonly List<Beat> _beats = new();
        private readonly int _textTicks;
        private readonly int _quietTicks;
        private readonly int _minTicks;
        private readonly AudioCues _cues;
        private int _index;
        private int _elapsed;
        private bool _started;

        public BattlePresenter(BattleKind kind, int textTicks, int quietTicks, int minTicks, AudioCues cues)
        {
            Kind = kind;
            _textTicks = Math.Max(1, textTicks);
            _quietTicks = Math.Max(1, quietTicks);
            _minTicks = Math.Max(0, Math.Min(minTicks, _textTicks));
            _cues = cues ?? throw new ArgumentNullException(nameof(cues));
        }

        public BattleKind Kind { get; }

        public SideView Player { get; } = new();

        public SideView Foe { get; } = new();

        /// <summary>今出している文。</summary>
        public string Text { get; private set; } = string.Empty;

        /// <summary>投げた捕獲玉の揺れ（0〜3）、捕獲の演出の Tick（0 なら出していない）。</summary>
        public int BallShakes { get; private set; }

        public int BallTicks { get; private set; }

        /// <summary>捕まえて相手が玉に入った。</summary>
        public bool FoeCaught { get; private set; }

        public Beat? Current => _index < _beats.Count ? _beats[_index] : null;

        public int ElapsedInBeat => _elapsed;

        /// <summary>見せる出来事が残っておらず、HP の動きも止まった。</summary>
        public bool IsIdle => _index >= _beats.Count && Player.ShownHp == Player.Hp && Foe.ShownHp == Foe.Hp;

        public void Enqueue(IEnumerable<BattleEvent> events)
        {
            if (events == null) { throw new ArgumentNullException(nameof(events)); }
            foreach (var e in events)
            {
                var text = TextOf(e);
                _beats.Add(new Beat(e, text, text.Length == 0 ? _quietTicks : _textTicks));
            }
        }

        /// <summary>1 Tick 進める。</summary>
        public void Tick()
        {
            AnimateHp(Player);
            AnimateHp(Foe);
            if (Player.FlashTicks > 0) { Player.FlashTicks--; }
            if (Foe.FlashTicks > 0) { Foe.FlashTicks--; }
            if (BallTicks > 0) { BallTicks++; }
            if (_index >= _beats.Count) { return; }
            if (!_started)
            {
                Apply(_beats[_index]);
                _started = true;
            }

            _elapsed++;
            if (_elapsed >= _beats[_index].Ticks) { Next(); }
        }

        /// <summary>決定キー：文が出てから少し経っていれば次の出来事へ送る。</summary>
        public bool Skip()
        {
            if (_index >= _beats.Count || !_started || _elapsed < _minTicks) { return false; }
            Next();
            return true;
        }

        private void Next()
        {
            _index++;
            _elapsed = 0;
            _started = false;
            if (_index < _beats.Count)
            {
                Apply(_beats[_index]);
                _started = true;
            }
        }

        private static void AnimateHp(SideView side)
        {
            if (side.ShownHp == side.Hp) { return; }
            var step = Math.Max(1, side.MaxHp / 24);
            side.ShownHp = side.ShownHp < side.Hp ? Math.Min(side.Hp, side.ShownHp + step) : Math.Max(side.Hp, side.ShownHp - step);
        }

        /// <summary>出来事を見せている姿に当てる。</summary>
        private void Apply(Beat beat)
        {
            var e = beat.Source;
            if (beat.Text.Length > 0) { Text = beat.Text; }
            var side = e.Player ? Player : Foe;
            switch (e.Kind)
            {
                case BattleEventKind.SentOut:
                    side.Visible = true;
                    side.Slot = e.Slot;
                    side.Name = e.Name;
                    side.SpeciesId = e.Species?.Id ?? string.Empty;
                    side.SpeciesIndex = e.Species?.Index ?? -1;
                    side.Level = e.Level;
                    side.Hp = e.HpAfter;
                    side.ShownHp = e.HpAfter;
                    side.MaxHp = e.MaxHp;
                    side.Status = string.Empty;
                    side.FlashTicks = 0;
                    if (!e.Player)
                    {
                        FoeCaught = false;
                        BallTicks = 0;
                    }

                    break;
                case BattleEventKind.Withdrew:
                    side.Visible = false;
                    break;
                case BattleEventKind.Damaged:
                    side.Hp = e.HpAfter;
                    side.MaxHp = e.MaxHp;
                    side.FlashTicks = 12;
                    if (e.Effectiveness > 100) { _cues.SuperHit++; }
                    else if (e.Effectiveness < 100) { _cues.WeakHit++; }
                    else { _cues.Hit++; }
                    break;
                case BattleEventKind.StatusDamage:
                    side.Hp = e.HpAfter;
                    side.FlashTicks = 8;
                    _cues.Status++;
                    break;
                case BattleEventKind.Healed:
                case BattleEventKind.PotionUsed:
                    if (side.Visible && side.Slot == e.Slot)
                    {
                        side.Hp = e.HpAfter;
                        side.MaxHp = e.MaxHp;
                    }

                    _cues.Heal++;
                    break;
                case BattleEventKind.StatusInflicted:
                    side.Status = e.Status!.Name;
                    _cues.Status++;
                    break;
                case BattleEventKind.StatusCured:
                    if (side.Visible && side.Slot == e.Slot) { side.Status = string.Empty; }
                    break;
                case BattleEventKind.Fainted:
                    side.Hp = 0;
                    _cues.Faint++;
                    break;
                case BattleEventKind.LeveledUp:
                    if (side.Visible && side.Slot == e.Slot)
                    {
                        side.Level = e.Level;
                        side.Hp = e.HpAfter;
                        side.ShownHp = e.HpAfter;
                        side.MaxHp = e.MaxHp;
                    }

                    _cues.LevelUp++;
                    break;
                case BattleEventKind.BallThrown:
                    BallShakes = e.Amount;
                    BallTicks = 1;
                    _cues.Ball++;
                    break;
                case BattleEventKind.Captured:
                    FoeCaught = true;
                    _cues.Capture++;
                    break;
                case BattleEventKind.BrokeFree:
                    BallTicks = 0;
                    break;
                case BattleEventKind.Started:
                    _cues.Encounter++;
                    break;
                case BattleEventKind.Won:
                    _cues.Victory++;
                    break;
                case BattleEventKind.Lost:
                    _cues.Defeat++;
                    break;
            }
        }

        private string Subject(BattleEvent e) => e.Player ? e.Name : (Kind == BattleKind.Wild ? "野生の" : "相手の") + e.Name;

        /// <summary>出来事の文。HP だけが動く出来事は空の文にする。</summary>
        private string TextOf(BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.Started:
                    return Kind == BattleKind.Wild ? $"野生の {e.Name}（Lv{e.Level}）が とびだしてきた！" : $"{e.Name}が 勝負を しかけてきた！";
                case BattleEventKind.SentOut:
                    if (e.Player) { return $"ゆけ！ {e.Name}！"; }
                    return Kind == BattleKind.Wild ? string.Empty : $"相手は {e.Name}（Lv{e.Level}）を くりだした！";
                case BattleEventKind.Withdrew: return $"もどれ、{e.Name}！";
                case BattleEventKind.UsedMove: return $"{Subject(e)}の {e.Move!.Name}！";
                case BattleEventKind.Missed: return "しかし 攻撃は はずれた！";
                case BattleEventKind.Damaged:
                    if (e.Effectiveness > 100) { return "効果は ばつぐんだ！"; }
                    if (e.Effectiveness < 100) { return "効果は いまひとつだ……"; }
                    return string.Empty;
                case BattleEventKind.Healed: return $"{Subject(e)}は 体力を 回復した！";
                case BattleEventKind.StatusInflicted: return $"{Subject(e)}は {e.Status!.Name}状態に なった！";
                case BattleEventKind.StatusFailed: return $"しかし {Subject(e)}には 効かなかった！";
                case BattleEventKind.StatusCured: return $"{Subject(e)}の {e.Status!.Name}が 治った！";
                case BattleEventKind.Asleep: return $"{Subject(e)}は {e.Status!.Name}で 動けない！";
                case BattleEventKind.StatusDamage: return $"{Subject(e)}は {e.Status!.Name}の ダメージを うけた！";
                case BattleEventKind.StatChanged: return $"{Subject(e)}の {StatName(e.Stat)}が {(e.Amount > 0 ? "あがった" : "さがった")}！";
                case BattleEventKind.StatUnchanged: return $"{Subject(e)}の {StatName(e.Stat)}は もう 変わらない！";
                case BattleEventKind.Fainted: return $"{Subject(e)}は たおれた！";
                case BattleEventKind.ExpGained: return $"{e.Name}は {e.Amount} の 経験値を もらった！";
                case BattleEventKind.LeveledUp: return $"{e.Name}は Lv{e.Level}に あがった！";
                case BattleEventKind.LearnedMove: return $"{e.Name}は {e.Move!.Name}を おぼえた！";
                case BattleEventKind.WantsToLearn: return $"{e.Name}は {e.Move!.Name}を おぼえたい……";
                case BattleEventKind.PotionUsed: return $"傷薬で {e.Name}の HPが {e.Amount} 回復した！";
                case BattleEventKind.BallThrown: return "捕獲玉を なげた！";
                case BattleEventKind.Captured: return $"やった！ {e.Name}を つかまえた！";
                case BattleEventKind.BrokeFree: return $"ああ！ {e.Name}が 出てしまった！";
                case BattleEventKind.RanAway: return "うまく にげきれた！";
                case BattleEventKind.RunFailed: return "にげられない！";
                case BattleEventKind.Won: return Kind == BattleKind.Trainer ? $"{e.Name}との 勝負に 勝った！" : string.Empty;
                case BattleEventKind.Lost: return "戦える モンスターが いない…… 目の前が まっくらに なった！";
                default: return string.Empty;
            }
        }

        public static string StatName(StatKind stat) => stat switch
        {
            StatKind.Attack => "攻撃",
            StatKind.Defense => "防御",
            _ => "素早さ",
        };
    }
}
