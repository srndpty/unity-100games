using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core.Data;
using IntGeometry;
using TileMotion;

namespace Game.Core.Play
{
    public enum Phase
    {
        /// <summary>朝：行動を選ぶ。</summary>
        Action,

        /// <summary>イベントの会話（選択肢があれば選ぶ、なければ送る）。</summary>
        Scene,

        /// <summary>最終日が終わり、ED が決まった。</summary>
        Ended,
    }

    public enum Refusal
    {
        None,

        /// <summary>今の段では受け付けない（朝でないのに行動・会話でないのに選ぶ など）。</summary>
        NotNow,

        /// <summary>今日は選べない行動（曜日・疲労で強制の休み）。</summary>
        Unavailable,

        /// <summary>番号が範囲外。</summary>
        OutOfRange,

        /// <summary>夏休みが終わった後。</summary>
        Ended,
    }

    /// <summary>命令の結果。拒否なら状態（ハッシュ）は何も変わっていない。</summary>
    public readonly struct Outcome
    {
        private Outcome(Refusal reason) => Reason = reason;

        public Refusal Reason { get; }
        public bool Accepted => Reason == Refusal.None;

        public static Outcome Ok => default;

        public static Outcome Refuse(Refusal reason) => new(reason);

        public override string ToString() => Accepted ? "Ok" : Reason.ToString();
    }

    public enum SummerEventKind
    {
        /// <summary>新しい日が始まった（Index：日）。</summary>
        DayStarted,

        /// <summary>行動した（Index：行動、Value：結果の段階）。</summary>
        ActionDone,

        /// <summary>能力値が動いた（Index：能力値、Value：実際の増減）。</summary>
        StatChanged,

        /// <summary>イベントが始まった（Index：イベント）。</summary>
        SceneStarted,

        /// <summary>選択肢を選んだ（Index：イベント、Value：選択肢）。</summary>
        ChoiceMade,

        /// <summary>夏休みが終わり ED が決まった（Index：ED）。</summary>
        Ending,
    }

    /// <summary>命令で起きた出来事（表示と音は出来事から作る）。</summary>
    public readonly struct SummerEvent
    {
        public SummerEvent(SummerEventKind kind, int index, int value = 0)
        {
            Kind = kind;
            Index = index;
            Value = value;
        }

        public SummerEventKind Kind { get; }
        public int Index { get; }
        public int Value { get; }

        public override string ToString() => $"{Kind}({Index}, {Value})";
    }

    public enum LogKind
    {
        Action,
        Event,
        Choice,
    }

    /// <summary>夏休みの記録の 1 件（何日目に何をしたか・何が起きたか）。</summary>
    public readonly struct LogEntry : IEquatable<LogEntry>
    {
        public LogEntry(int day, LogKind kind, int index, int value)
        {
            Day = day;
            Kind = kind;
            Index = index;
            Value = value;
        }

        public int Day { get; }
        public LogKind Kind { get; }

        /// <summary>行動・イベントの番号。</summary>
        public int Index { get; }

        /// <summary>行動の結果の段階・選んだ選択肢（イベントは 0）。</summary>
        public int Value { get; }

        public bool Equals(LogEntry other) => Day == other.Day && Kind == other.Kind && Index == other.Index && Value == other.Value;

        public override bool Equals(object? obj) => obj is LogEntry other && Equals(other);

        public override int GetHashCode() => unchecked((((Day * 397) ^ (int)Kind) * 397 ^ Index) * 397 ^ Value);

        public static bool operator ==(LogEntry left, LogEntry right) => left.Equals(right);

        public static bool operator !=(LogEntry left, LogEntry right) => !left.Equals(right);

        public override string ToString() => $"{Day}:{Kind}:{Index}:{Value}";
    }

    /// <summary>
    /// 1 周（1 試合 = 1 シード）の夏休み。能力値・フラグ・1 回だけのイベントの済み・記録を持ち、命令（行動・送る・選ぶ）だけで変える。
    /// 受け付けない命令は理由を返し、何も変えない。
    /// 乱数は用途ごとに別の系列（試合のシード・日・用途から派生）を使うので、行動の結果で引く回数が変わってもイベントの抽選は変わらない。
    /// </summary>
    public sealed class Summer : IConditionState
    {
        /// <summary>乱数の用途の番号。</summary>
        public const int ActionPurpose = 1;
        public const int EventPurpose = 2;

        private readonly SummerBook _book;
        private readonly int[] _stats;
        private readonly bool[] _flags;
        private readonly bool[] _done;
        private readonly List<int> _firedToday = new();
        private readonly List<LogEntry> _log = new();
        private readonly ReadOnlyCollection<LogEntry> _logView;
        private readonly List<SummerEvent> _events = new();
        private readonly ReadOnlyCollection<SummerEvent> _eventsView;
        private RandomSeries _eventRng;

        public Summer(SummerBook book, int seed)
        {
            _book = book ?? throw new ArgumentNullException(nameof(book));
            Seed = seed;
            _stats = new int[book.Stats.Count];
            foreach (var s in book.Stats) { _stats[s.Index] = s.Initial; }
            _flags = new bool[book.Flags.Count];
            _done = new bool[book.Events.Count];
            _logView = _log.AsReadOnly();
            _eventsView = _events.AsReadOnly();
            Day = 1;
            _eventRng = RandomSeries.Derive(seed, Day, EventPurpose);
            StartDay();
        }

        private Summer(Summer other)
        {
            _book = other._book;
            Seed = other.Seed;
            _stats = (int[])other._stats.Clone();
            _flags = (bool[])other._flags.Clone();
            _done = (bool[])other._done.Clone();
            _firedToday.AddRange(other._firedToday);
            _log.AddRange(other._log);
            _logView = _log.AsReadOnly();
            _eventsView = _events.AsReadOnly();
            _eventRng = other._eventRng.Clone();
            Day = other.Day;
            Phase = other.Phase;
            Action = other.Action;
            OutcomeIndex = other.OutcomeIndex;
            Forced = other.Forced;
            Scene = other.Scene;
            Ending = other.Ending;
        }

        public SummerBook Book => _book;
        public int Seed { get; }

        /// <summary>今日（1〜Days）。</summary>
        public int Day { get; private set; }

        public Weekday Weekday => _book.WeekdayOf(Day);

        public Phase Phase { get; private set; }

        /// <summary>今日選んだ行動（朝はまだ -1）。</summary>
        public int Action { get; private set; }

        /// <summary>今日の行動の結果の段階（朝は -1）。</summary>
        public int OutcomeIndex { get; private set; }

        /// <summary>今朝は強制の行動しか選べないか。</summary>
        public bool Forced { get; private set; }

        /// <summary>今出しているイベント（なければ -1）。</summary>
        public int Scene { get; private set; } = -1;

        /// <summary>決まった ED（まだなら -1）。</summary>
        public int Ending { get; private set; } = -1;

        /// <summary>夏休みの記録（行動・イベント・選択の順）。</summary>
        public IReadOnlyList<LogEntry> Log => _logView;

        /// <summary>最後の命令で起きた出来事（拒否なら空）。</summary>
        public IReadOnlyList<SummerEvent> Events => _eventsView;

        /// <summary>今日出たイベント。</summary>
        public IReadOnlyList<int> FiredToday => _firedToday.AsReadOnly();

        public int Stat(int index) => _stats[index];

        public bool Flag(int index) => _flags[index];

        /// <summary>1 回だけのイベントが済んだか。</summary>
        public bool Done(int ev) => _done[ev];

        public Summer Clone() => new(this);

        /// <summary>今朝選べる行動か。</summary>
        public bool CanChoose(int action)
        {
            if (Phase != Phase.Action || action < 0 || action >= _book.Actions.Count) { return false; }
            if (Forced) { return action == _book.ForcedAction; }
            return _book.Actions[action].AvailableOn(Weekday);
        }

        /// <summary>行動を選ぶ。結果の段階と効果を当て、その日の「行動の後」→「夜」のイベントへ進む。</summary>
        public Outcome ChooseAction(int action)
        {
            _events.Clear();
            if (Phase == Phase.Ended) { return Outcome.Refuse(Refusal.Ended); }
            if (action < 0 || action >= _book.Actions.Count) { return Outcome.Refuse(Refusal.OutOfRange); }
            if (Phase != Phase.Action) { return Outcome.Refuse(Refusal.NotNow); }
            if (!CanChoose(action)) { return Outcome.Refuse(Refusal.Unavailable); }

            Action = action;
            var def = _book.Actions[action];
            var rng = RandomSeries.Derive(Seed, Day, ActionPurpose);
            OutcomeIndex = RollOutcome(rng);
            var percent = _book.Outcomes[OutcomeIndex].Percent;
            _events.Add(new SummerEvent(SummerEventKind.ActionDone, action, OutcomeIndex));
            _log.Add(new LogEntry(Day, LogKind.Action, action, OutcomeIndex));
            foreach (var e in def.Effects)
            {
                // 幅は毎回引く（幅のない効果でも 1 回引き、引く回数を効果の数だけに固定する）。
                var amount = rng.Range(e.Min, e.Max);
                if (e.Kind == EffectKind.Add && amount > 0 && !_book.Stats[e.Target].Cost) { amount = amount * percent / 100; }
                Apply(e, amount);
            }

            NextScene(Slot.After);
            return Outcome.Ok;
        }

        /// <summary>選択肢のない会話を送る。</summary>
        public Outcome Continue()
        {
            _events.Clear();
            if (Phase == Phase.Ended) { return Outcome.Refuse(Refusal.Ended); }
            if (Phase != Phase.Scene || _book.Events[Scene].Choices.Count > 0) { return Outcome.Refuse(Refusal.NotNow); }
            FinishScene();
            return Outcome.Ok;
        }

        /// <summary>選択肢を選ぶ。</summary>
        public Outcome Choose(int option)
        {
            _events.Clear();
            if (Phase == Phase.Ended) { return Outcome.Refuse(Refusal.Ended); }
            if (Phase != Phase.Scene || _book.Events[Scene].Choices.Count == 0) { return Outcome.Refuse(Refusal.NotNow); }
            var choices = _book.Events[Scene].Choices;
            if (option < 0 || option >= choices.Count) { return Outcome.Refuse(Refusal.OutOfRange); }
            _events.Add(new SummerEvent(SummerEventKind.ChoiceMade, Scene, option));
            _log.Add(new LogEntry(Day, LogKind.Choice, Scene, option));
            foreach (var e in choices[option].Effects) { Apply(e, e.Min); }
            FinishScene();
            return Outcome.Ok;
        }

        /// <summary>今の状態で選ばれる ED（夏休みが終わったときの判定と同じ。優先度の高い順に最初に成り立つもの）。</summary>
        public int EndingNow()
        {
            foreach (var e in _book.EndingsByPriority)
            {
                if (e.When == null || e.When.Eval(this)) { return e.Index; }
            }

            throw new InvalidOperationException("既定の ED がありません");
        }

        /// <summary>状態のハッシュ。データの指紋・シード・日・段・能力値・フラグ・済み・今日の出来事・イベントの乱数・記録まで混ぜる。</summary>
        public uint Hash()
        {
            var h = StateHash.Start();
            h.Mix(_book.Fingerprint);
            h.Mix(Seed);
            h.Mix(Day);
            h.Mix((int)Phase);
            h.Mix(Action);
            h.Mix(OutcomeIndex);
            h.Mix(Forced);
            h.Mix(Scene);
            h.Mix(Ending);
            foreach (var s in _stats) { h.Mix(s); }
            foreach (var f in _flags) { h.Mix(f); }
            foreach (var d in _done) { h.Mix(d); }
            h.Mix(_firedToday.Count);
            foreach (var f in _firedToday) { h.Mix(f); }
            h.Mix(_eventRng.State);
            h.Mix(_log.Count);
            foreach (var e in _log)
            {
                h.Mix(e.Day);
                h.Mix((int)e.Kind);
                h.Mix(e.Index);
                h.Mix(e.Value);
            }

            return h.Value;
        }

        // ---- 日の始めの写し（セーブ） ----

        /// <summary>日の始め（朝）の状態の写し。朝でなければ例外。</summary>
        public SummerSnapshot Snapshot()
        {
            if (Phase != Phase.Action || Action >= 0) { throw new InvalidOperationException("写しは朝だけ取れます"); }
            return new SummerSnapshot(Seed, Day, (int[])_stats.Clone(), (bool[])_flags.Clone(), (bool[])_done.Clone(), _log.ToArray());
        }

        /// <summary>
        /// 写しから朝の状態を作り直す。シードと記録の行動・選択を 1 日目から再生し、再生した状態（日・能力値・フラグ・済み・記録）が
        /// 写しと全て一致するときだけ返す。範囲外の値、記録にない行動・選択、記録と食い違う状態は理由を返して null。
        /// </summary>
        public static Summer? Restore(SummerBook book, SummerSnapshot snapshot, out string? reason)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            reason = Check(book, snapshot);
            if (reason != null) { return null; }
            var s = Replay(book, snapshot, out reason);
            if (s == null) { return null; }
            s._events.Clear();
            return s;
        }

        /// <summary>記録の行動と選択だけを入力にして再生し、写しと照合する。</summary>
        private static Summer? Replay(SummerBook book, SummerSnapshot snapshot, out string? reason)
        {
            var s = new Summer(book, snapshot.Seed);
            var log = snapshot.Log;
            var next = 0;

            // 入力は行動と選択だけ（イベントの記録は再生で同じものが出るかを照合する）。
            int NextInput()
            {
                while (next < log.Count && log[next].Kind == LogKind.Event) { next++; }
                return next < log.Count ? next++ : -1;
            }

            for (int guard = 0; guard < (book.Days * 8) + 8; guard++)
            {
                if (s.Phase == Phase.Ended) { break; }
                if (s.Phase == Phase.Action && s.Day == snapshot.Day) { break; }
                Outcome r;
                if (s.Phase == Phase.Action)
                {
                    var i = NextInput();
                    if (i < 0 || log[i].Kind != LogKind.Action || log[i].Day != s.Day)
                    {
                        reason = $"記録に {s.Day} 日目の行動がありません";
                        return null;
                    }

                    r = s.ChooseAction(log[i].Index);
                }
                else if (book.Events[s.Scene].Choices.Count > 0)
                {
                    var i = NextInput();
                    if (i < 0 || log[i].Kind != LogKind.Choice || log[i].Index != s.Scene)
                    {
                        reason = $"記録に {s.Day} 日目のイベント {book.Events[s.Scene].Id} の選択がありません";
                        return null;
                    }

                    r = s.Choose(log[i].Value);
                }
                else
                {
                    r = s.Continue();
                }

                if (!r.Accepted)
                {
                    reason = $"記録の {s.Day} 日目の命令を受け付けません（{r}）";
                    return null;
                }
            }

            if (s.Phase != Phase.Action || s.Day != snapshot.Day || !Same(s, snapshot))
            {
                reason = "記録を再生した状態がセーブと一致しません";
                return null;
            }

            reason = null;
            return s;
        }

        private static bool Same(Summer s, SummerSnapshot snapshot)
        {
            if (s._log.Count != snapshot.Log.Count) { return false; }
            for (int i = 0; i < s._log.Count; i++)
            {
                if (s._log[i] != snapshot.Log[i]) { return false; }
            }

            for (int i = 0; i < s._stats.Length; i++)
            {
                if (s._stats[i] != snapshot.Stats[i]) { return false; }
            }

            for (int i = 0; i < s._flags.Length; i++)
            {
                if (s._flags[i] != snapshot.Flags[i]) { return false; }
            }

            for (int i = 0; i < s._done.Length; i++)
            {
                if (s._done[i] != snapshot.Done[i]) { return false; }
            }

            return true;
        }

        private static string? Check(SummerBook book, SummerSnapshot snapshot)
        {
            if (snapshot.Day < 1 || snapshot.Day > book.Days) { return $"日 {snapshot.Day} が範囲外です"; }
            if (snapshot.Stats.Count != book.Stats.Count || snapshot.Flags.Count != book.Flags.Count || snapshot.Done.Count != book.Events.Count)
            {
                return "能力値・フラグ・イベントの数がデータと合いません";
            }

            for (int i = 0; i < book.Stats.Count; i++)
            {
                var v = snapshot.Stats[i];
                if (v < book.Stats[i].Min || v > book.Stats[i].Max) { return $"能力値 {book.Stats[i].Id} の値 {v} が範囲外です"; }
            }

            foreach (var e in snapshot.Log)
            {
                if (e.Day < 1 || e.Day >= snapshot.Day) { return "記録の日が範囲外です"; }
                var count = e.Kind == LogKind.Action ? book.Actions.Count : book.Events.Count;
                if (e.Index < 0 || e.Index >= count) { return "記録の番号が範囲外です"; }
                var values = e.Kind switch
                {
                    LogKind.Action => book.Outcomes.Count,
                    LogKind.Choice => book.Events[e.Index].Choices.Count,
                    _ => 1,
                };
                if (e.Value < 0 || e.Value >= values) { return "記録の値が範囲外です"; }
            }

            return null;
        }

        // ---- 内部 ----

        int IConditionState.Stat(int index) => _stats[index];

        bool IConditionState.Flag(int index) => _flags[index];

        int IConditionState.Day => Day;

        Weekday IConditionState.Weekday => Weekday;

        int IConditionState.Action => Action;

        private void StartDay()
        {
            Phase = Phase.Action;
            Action = -1;
            OutcomeIndex = -1;
            Scene = -1;
            _firedToday.Clear();
            _eventRng = RandomSeries.Derive(Seed, Day, EventPurpose);
            Forced = _book.ForcedAction >= 0 && _book.ForcedWhen!.Eval(this);
            _events.Add(new SummerEvent(SummerEventKind.DayStarted, Day));
        }

        private int RollOutcome(RandomSeries rng)
        {
            var weights = new int[_book.Outcomes.Count];
            for (int i = 0; i < weights.Length; i++)
            {
                var o = _book.Outcomes[i];
                weights[i] = o.Weight;
                foreach (var (when, weight) in o.Overrides)
                {
                    if (when.Eval(this))
                    {
                        weights[i] = weight;
                        break;
                    }
                }
            }

            return rng.Weighted(weights);
        }

        private void Apply(Effect e, int amount)
        {
            switch (e.Kind)
            {
                case EffectKind.Set:
                    _flags[e.Target] = true;
                    return;
                case EffectKind.Clear:
                    _flags[e.Target] = false;
                    return;
                default:
                    {
                        // 宣言の範囲で飽和させる（能力値は範囲の外に出ない）。
                        var stat = _book.Stats[e.Target];
                        var before = _stats[e.Target];
                        var after = (int)Math.Max(stat.Min, Math.Min(stat.Max, (long)before + amount));
                        _stats[e.Target] = after;
                        if (after != before) { _events.Add(new SummerEvent(SummerEventKind.StatChanged, e.Target, after - before)); }
                        return;
                    }
            }
        }

        /// <summary>
        /// 枠のイベントを選ぶ：期間・曜日・1 回だけ・排他・条件を満たす候補を、優先度の高い順（同じなら定義の順）に並べ、
        /// 先頭から確率を引いて最初に当たった 1 本。確率 100 は引かない。なければ -1。
        /// </summary>
        internal int Select(Slot slot)
        {
            var candidates = new List<EventDef>();
            foreach (var e in _book.Events)
            {
                if (e.Slot != slot || !e.OpenOn(Day, Weekday) || (e.Once && _done[e.Index])) { continue; }
                if (_firedToday.Exists(f => _book.Exclusive(f, e.Index))) { continue; }
                if (e.When != null && !e.When.Eval(this)) { continue; }
                candidates.Add(e);
            }

            candidates.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority) : a.Index.CompareTo(b.Index));
            foreach (var e in candidates)
            {
                if (e.Chance >= 100 || _eventRng.Next(100) < e.Chance) { return e.Index; }
            }

            return -1;
        }

        private void NextScene(Slot from)
        {
            for (var slot = from; slot <= Slot.Night; slot++)
            {
                var e = Select(slot);
                if (e < 0) { continue; }
                Fire(e);
                return;
            }

            EndDay();
        }

        private void Fire(int index)
        {
            var e = _book.Events[index];
            if (e.Once) { _done[index] = true; }
            _firedToday.Add(index);
            Scene = index;
            Phase = Phase.Scene;
            _events.Add(new SummerEvent(SummerEventKind.SceneStarted, index));
            _log.Add(new LogEntry(Day, LogKind.Event, index, 0));
            foreach (var effect in e.Effects) { Apply(effect, effect.Min); }
        }

        private void FinishScene()
        {
            var slot = _book.Events[Scene].Slot;
            Scene = -1;
            if (slot == Slot.After) { NextScene(Slot.Night); }
            else { EndDay(); }
        }

        private void EndDay()
        {
            if (Day >= _book.Days)
            {
                Phase = Phase.Ended;
                Scene = -1;
                Ending = EndingNow();
                _events.Add(new SummerEvent(SummerEventKind.Ending, Ending));
                return;
            }

            Day++;
            StartDay();
        }
    }

    /// <summary>日の始め（朝）の状態の写し（セーブ）。</summary>
    public sealed class SummerSnapshot
    {
        public SummerSnapshot(int seed, int day, int[] stats, bool[] flags, bool[] done, LogEntry[] log)
        {
            Seed = seed;
            Day = day;
            Stats = Array.AsReadOnly(stats);
            Flags = Array.AsReadOnly(flags);
            Done = Array.AsReadOnly(done);
            Log = Array.AsReadOnly(log);
        }

        public int Seed { get; }
        public int Day { get; }
        public ReadOnlyCollection<int> Stats { get; }
        public ReadOnlyCollection<bool> Flags { get; }
        public ReadOnlyCollection<bool> Done { get; }
        public ReadOnlyCollection<LogEntry> Log { get; }
    }
}
