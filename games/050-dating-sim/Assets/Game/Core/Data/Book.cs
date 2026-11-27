using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core.Data
{
    /// <summary>曜日（データの 1 日目の曜日から数える）。</summary>
    public enum Weekday
    {
        Mon,
        Tue,
        Wed,
        Thu,
        Fri,
        Sat,
        Sun,
    }

    /// <summary>イベントの枠。1 日に枠ごとに高々 1 本。</summary>
    public enum Slot
    {
        /// <summary>行動の後。</summary>
        After,

        /// <summary>夜。</summary>
        Night,
    }

    /// <summary>能力値・好感度（どちらも範囲を持つ整数）。</summary>
    public sealed class StatDef
    {
        internal StatDef(int index, string id, string name, int min, int max, int initial, bool cost, int character)
        {
            Index = index;
            Id = id;
            Name = name;
            Min = min;
            Max = max;
            Initial = initial;
            Cost = cost;
            Character = character;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public int Min { get; }
        public int Max { get; }
        public int Initial { get; }

        /// <summary>行動の結果の倍率を掛けない値（疲労など）。</summary>
        public bool Cost { get; }

        /// <summary>好感度なら人物の番号、能力値なら -1。</summary>
        public int Character { get; }

        public bool IsHeart => Character >= 0;
    }

    public sealed class CharDef
    {
        internal CharDef(int index, string id, string name, uint color, IReadOnlyList<string> faces)
        {
            Index = index;
            Id = id;
            Name = name;
            Color = color;
            Faces = faces;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }

        /// <summary>名前の色（0xRRGGBB）。</summary>
        public uint Color { get; }

        /// <summary>表情の ID（先頭が既定）。</summary>
        public IReadOnlyList<string> Faces { get; }

        /// <summary>この人物の好感度の能力値の番号（なければ -1）。</summary>
        public int Heart { get; internal set; } = -1;
    }

    public sealed class BackgroundDef
    {
        internal BackgroundDef(int index, string id, string name)
        {
            Index = index;
            Id = id;
            Name = name;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
    }

    public enum EffectKind
    {
        /// <summary>能力値に足す（負なら引く）。幅（Min〜Max）は行動のときだけ乱数で引く。</summary>
        Add,

        /// <summary>フラグを立てる。</summary>
        Set,

        /// <summary>フラグを下ろす。</summary>
        Clear,
    }

    /// <summary>効果 1 つ。</summary>
    public readonly struct Effect
    {
        internal Effect(EffectKind kind, int target, int min, int max)
        {
            Kind = kind;
            Target = target;
            Min = min;
            Max = max;
        }

        public EffectKind Kind { get; }

        /// <summary>能力値またはフラグの番号。</summary>
        public int Target { get; }

        public int Min { get; }
        public int Max { get; }
    }

    public sealed class ActionDef
    {
        internal ActionDef(int index, string id, string name, int weekMask, int background, IReadOnlyList<Effect> effects, int line)
        {
            Index = index;
            Id = id;
            Name = name;
            WeekMask = weekMask;
            Background = background;
            Effects = effects;
            Line = line;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }

        /// <summary>選べる曜日のビット（Weekday の番号）。</summary>
        public int WeekMask { get; }

        /// <summary>結果を見せる背景（-1 なら既定）。</summary>
        public int Background { get; }

        public IReadOnlyList<Effect> Effects { get; }
        public int Line { get; }

        public bool AvailableOn(Weekday day) => (WeekMask & (1 << (int)day)) != 0;
    }

    /// <summary>行動の結果の段階（大成功・成功・失敗など）。</summary>
    public sealed class OutcomeDef
    {
        internal OutcomeDef(int index, string id, string name, int percent, int weight, IReadOnlyList<(Cond When, int Weight)> overrides)
        {
            Index = index;
            Id = id;
            Name = name;
            Percent = percent;
            Weight = weight;
            Overrides = overrides;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }

        /// <summary>倍率の百分率（150 なら 1.5 倍）。好ましい効果（正の値・費用でない能力値）にだけ掛ける。</summary>
        public int Percent { get; }

        public int Weight { get; }

        /// <summary>条件付きの重み（書いた順に最初に成り立つものを使う）。</summary>
        public IReadOnlyList<(Cond When, int Weight)> Overrides { get; }
    }

    /// <summary>会話の 1 行（台詞・地の文・背景の切り替え）。</summary>
    public readonly struct SceneLine
    {
        internal SceneLine(int speaker, int face, int background, string text)
        {
            Speaker = speaker;
            Face = face;
            Background = background;
            Text = text;
        }

        /// <summary>話者の人物の番号（地の文は -1）。</summary>
        public int Speaker { get; }

        /// <summary>表情の番号（話者の Faces の番号）。</summary>
        public int Face { get; }

        /// <summary>この行から切り替える背景（変えなければ -1）。</summary>
        public int Background { get; }

        public string Text { get; }
    }

    public sealed class ChoiceDef
    {
        internal ChoiceDef(string text, IReadOnlyList<Effect> effects)
        {
            Text = text;
            Effects = effects;
        }

        public string Text { get; }
        public IReadOnlyList<Effect> Effects { get; }
    }

    public sealed class EventDef
    {
        internal EventDef(int index, string id, Slot slot, int dayFrom, int dayTo, int weekMask, int priority, int chance, bool once, Cond? when,
            int background, IReadOnlyList<SceneLine> lines, IReadOnlyList<Effect> effects, IReadOnlyList<ChoiceDef> choices, int line)
        {
            Index = index;
            Id = id;
            Slot = slot;
            DayFrom = dayFrom;
            DayTo = dayTo;
            WeekMask = weekMask;
            Priority = priority;
            Chance = chance;
            Once = once;
            When = when;
            Background = background;
            Lines = lines;
            Effects = effects;
            Choices = choices;
            Line = line;
        }

        public int Index { get; }
        public string Id { get; }
        public Slot Slot { get; }
        public int DayFrom { get; }
        public int DayTo { get; }
        public int WeekMask { get; }

        /// <summary>大きいほど先に試す。同じなら定義の順。</summary>
        public int Priority { get; }

        /// <summary>出る確率（1〜100 %）。</summary>
        public int Chance { get; }

        /// <summary>1 周で 1 回だけ。</summary>
        public bool Once { get; }

        /// <summary>出る条件（なければ常に）。</summary>
        public Cond? When { get; }

        /// <summary>始めの背景（-1 なら行動の背景のまま）。</summary>
        public int Background { get; }

        public IReadOnlyList<SceneLine> Lines { get; }

        /// <summary>出たときにすぐ当てる効果。</summary>
        public IReadOnlyList<Effect> Effects { get; }

        /// <summary>最後の選択肢（なければ空）。</summary>
        public IReadOnlyList<ChoiceDef> Choices { get; }

        public int Line { get; }

        /// <summary>期間・曜日の上でこの日に出うるか（条件は見ない）。</summary>
        public bool OpenOn(int day, Weekday weekday) => day >= DayFrom && day <= DayTo && (WeekMask & (1 << (int)weekday)) != 0;
    }

    public sealed class EndingDef
    {
        internal EndingDef(int index, string id, string name, int priority, Cond? when, int background, IReadOnlyList<SceneLine> lines, int line)
        {
            Index = index;
            Id = id;
            Name = name;
            Priority = priority;
            When = when;
            Background = background;
            Lines = lines;
            Line = line;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public int Priority { get; }

        /// <summary>条件（既定の ED は null）。</summary>
        public Cond? When { get; }

        public int Background { get; }
        public IReadOnlyList<SceneLine> Lines { get; }
        public int Line { get; }

        public bool IsDefault => When == null;
    }

    /// <summary>
    /// コンパイルした夏休みのデータ（能力値・フラグ・人物・背景・行動・結果の段階・イベント・排他・ED）。
    /// 実行器（<see cref="Play.Summer"/>）はこの型だけを読む。作るときに検証を通っている。
    /// </summary>
    public sealed class SummerBook
    {
        internal SummerBook(int days, Weekday firstWeekday, List<StatDef> stats, List<string> flags, List<CharDef> chars, List<BackgroundDef> backgrounds,
            List<ActionDef> actions, List<OutcomeDef> outcomes, int forcedAction, Cond? forcedWhen, List<EventDef> events, bool[,] exclusive, List<EndingDef> endings, uint fingerprint)
        {
            Days = days;
            FirstWeekday = firstWeekday;
            Stats = stats.AsReadOnly();
            Flags = flags.AsReadOnly();
            Characters = chars.AsReadOnly();
            Backgrounds = backgrounds.AsReadOnly();
            Actions = actions.AsReadOnly();
            Outcomes = outcomes.AsReadOnly();
            ForcedAction = forcedAction;
            ForcedWhen = forcedWhen;
            Events = events.AsReadOnly();
            _exclusive = exclusive;
            var ordered = new List<EndingDef>(endings);
            ordered.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            Endings = endings.AsReadOnly();
            EndingsByPriority = ordered.AsReadOnly();
            Fingerprint = fingerprint;
        }

        private readonly bool[,] _exclusive;

        /// <summary>日数（30）。</summary>
        public int Days { get; }

        /// <summary>1 日目の曜日。</summary>
        public Weekday FirstWeekday { get; }

        public ReadOnlyCollection<StatDef> Stats { get; }
        public ReadOnlyCollection<string> Flags { get; }
        public ReadOnlyCollection<CharDef> Characters { get; }
        public ReadOnlyCollection<BackgroundDef> Backgrounds { get; }
        public ReadOnlyCollection<ActionDef> Actions { get; }
        public ReadOnlyCollection<OutcomeDef> Outcomes { get; }

        /// <summary>条件（<see cref="ForcedWhen"/>）が成り立つ朝に、これしか選べない行動（なければ -1）。</summary>
        public int ForcedAction { get; }

        public Cond? ForcedWhen { get; }

        public ReadOnlyCollection<EventDef> Events { get; }

        /// <summary>ED（定義の順）。</summary>
        public ReadOnlyCollection<EndingDef> Endings { get; }

        /// <summary>ED（優先度の高い順。既定の ED が最後）。</summary>
        public ReadOnlyCollection<EndingDef> EndingsByPriority { get; }

        /// <summary>データの指紋（状態のハッシュとセーブに混ぜる）。</summary>
        public uint Fingerprint { get; }

        public Weekday WeekdayOf(int day) => (Weekday)(((int)FirstWeekday + day - 1) % 7);

        /// <summary>2 つのイベントが同じ日に出ない組か。</summary>
        public bool Exclusive(int a, int b) => _exclusive[a, b];

        public int StatIndex(string id) => IndexOf(Stats, s => s.Id == id, id);

        public int FlagIndex(string id) => Flags.IndexOf(id) is var i && i >= 0 ? i : throw new ArgumentException($"フラグ {id} がありません");

        public int ActionIndex(string id) => IndexOf(Actions, a => a.Id == id, id);

        public int EventIndex(string id) => IndexOf(Events, e => e.Id == id, id);

        /// <summary>行動の番号（なければ -1）。</summary>
        public int FindAction(string id) => Find(Actions, a => a.Id == id);

        /// <summary>イベントの番号（なければ -1）。</summary>
        public int FindEvent(string id) => Find(Events, e => e.Id == id);

        public int EndingIndex(string id) => IndexOf(Endings, e => e.Id == id, id);

        public int CharacterIndex(string id) => IndexOf(Characters, c => c.Id == id, id);

        private static int IndexOf<T>(IReadOnlyList<T> list, Func<T, bool> match, string id) =>
            Find(list, match) is var i && i >= 0 ? i : throw new ArgumentException($"{id} がありません");

        private static int Find<T>(IReadOnlyList<T> list, Func<T, bool> match)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (match(list[i])) { return i; }
            }

            return -1;
        }
    }
}
