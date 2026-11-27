using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;

namespace Game.Core.Mechanisms
{
    /// <summary>仕掛けの状態。Locked（前提の持ち物が入っていない）→ Active（操作できる）→ Solved（解けて開いた・以後は不変）。</summary>
    public enum Phase
    {
        Locked,
        Active,
        Solved,
    }

    public enum MechanismKind
    {
        /// <summary>数字ダイヤル錠（3 桁、0〜9 で循環）。</summary>
        Dial,

        /// <summary>鍵穴（鍵を差すと開く）。</summary>
        KeyLock,

        /// <summary>天球儀のリング合わせ（3 本、45° ずつ 8 段）。</summary>
        Rings,

        /// <summary>柱時計の針合わせ（短針・長針、12 段）。</summary>
        Clock,

        /// <summary>順番ボタン（4 つの宝石を答えの順に押す）。</summary>
        Sequence,
    }

    /// <summary>仕掛けへの操作の種類（表の列）。</summary>
    public enum MechanismOp
    {
        /// <summary>持ち物を鍵穴・差し込み口に入れる。</summary>
        Install,

        /// <summary>桁・リング・針・宝石を 1 段動かす。</summary>
        Turn,

        /// <summary>持ち物を選ばずに鍵穴・差し込み口を触る。</summary>
        Probe,
    }

    public enum TransitionKind
    {
        Reject,

        /// <summary>持ち物を入れて <see cref="IMechanism.InstalledPhase"/> へ。</summary>
        Install,

        /// <summary>値を動かし、答えになったら Solved へ。</summary>
        Turn,
    }

    /// <summary>表の 1 マス（状態 × 操作 → 遷移または拒否の理由）。</summary>
    public readonly struct Transition
    {
        public Transition(TransitionKind kind, Play.Refusal refusal)
        {
            Kind = kind;
            Refusal = refusal;
        }

        public TransitionKind Kind { get; }
        public Play.Refusal Refusal { get; }

        public static Transition Reject(Play.Refusal refusal) => new(TransitionKind.Reject, refusal);
    }

    /// <summary>
    /// 全ての仕掛けで共有する遷移の表。種類ごとの違い（持ち物を入れた後の状態・値の動かし方・答え）は
    /// <see cref="IMechanism"/> の実装が持ち、どの状態で何を受け付けるかはこの表だけで決める。
    /// </summary>
    public static class MechanismTable
    {
        private static readonly Transition[,] _table =
        {
            // Locked
            {
                new(TransitionKind.Install, Play.Refusal.None),
                Transition.Reject(Play.Refusal.Locked),
                Transition.Reject(Play.Refusal.Locked),
            },

            // Active
            {
                Transition.Reject(Play.Refusal.AlreadyInstalled),
                new(TransitionKind.Turn, Play.Refusal.None),
                Transition.Reject(Play.Refusal.NotAllowed),
            },

            // Solved
            {
                Transition.Reject(Play.Refusal.AlreadySolved),
                Transition.Reject(Play.Refusal.AlreadySolved),
                Transition.Reject(Play.Refusal.AlreadySolved),
            },
        };

        public static Transition Lookup(Phase phase, MechanismOp op) => _table[(int)phase, (int)op];
    }

    /// <summary>
    /// 仕掛けの種類ごとの規則。答えは試合のシードから作って持ち、操作の値（桁・リング・針・押した数）は世界が持つ
    /// （複製とハッシュを世界の側で 1 か所にするため）。
    /// </summary>
    public interface IMechanism
    {
        MechanismKind Kind { get; }

        /// <summary>試合の始めの状態（ダイヤルは Active、ほかは持ち物を待つ Locked）。</summary>
        Phase InitialPhase { get; }

        /// <summary>持ち物を入れた後の状態（鍵穴は Solved、ほかは Active）。</summary>
        Phase InstalledPhase { get; }

        /// <summary>動かせる部品（桁・リング・針・宝石）の数。</summary>
        int Slots { get; }

        /// <summary>世界が持つ値の数。</summary>
        int ValueCount { get; }

        /// <summary>答え（種類ごとの意味：桁・星座の番号・時と分の段・宝石の順）。</summary>
        IReadOnlyList<int> Answer { get; }

        /// <summary>答えの文（ヒントの最後の段階）。</summary>
        string AnswerText { get; }

        /// <summary>試合の始めの値。</summary>
        void Reset(int[] values);

        /// <summary>部品を 1 段動かす。間違えて最初からになったら mistake。</summary>
        void Turn(int[] values, int slot, out bool mistake);

        bool IsSolved(int[] values);

        /// <summary>今の値から答えまでに動かす部品の列（その順に Turn すると解ける）。</summary>
        IReadOnlyList<int> SolutionFrom(int[] values);
    }

    /// <summary>仕掛けを作る（答えは仕掛けごとに派生した乱数系列から作る）。</summary>
    public static class MechanismFactory
    {
        public static IMechanism Create(MechanismKind kind, RandomSeries random)
        {
            if (random == null) { throw new ArgumentNullException(nameof(random)); }
            return kind switch
            {
                MechanismKind.Dial => new DialLock(random),
                MechanismKind.KeyLock => new KeyLock(),
                MechanismKind.Rings => new RingLock(random),
                MechanismKind.Clock => new ClockHands(random),
                MechanismKind.Sequence => new SequenceLock(random),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }

        /// <summary>種類ごとの動かせる部品の数（部屋の定義の検証で使う）。</summary>
        public static int SlotsOf(MechanismKind kind) => kind switch
        {
            MechanismKind.Dial => DialLock.Digits,
            MechanismKind.KeyLock => 0,
            MechanismKind.Rings => RingLock.RingCount,
            MechanismKind.Clock => 2,
            MechanismKind.Sequence => SequenceLock.GemCount,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>持ち物を入れて Locked を外す種類か（ダイヤルは最初から Active）。</summary>
        public static bool NeedsItem(MechanismKind kind) => kind != MechanismKind.Dial;
    }

    /// <summary>桁を 1 つずつ循環させて答えに合わせる仕掛けの共通部分（ダイヤル・リング・針）。</summary>
    public abstract class CyclicLock : IMechanism
    {
        private readonly int[] _answer;

        protected CyclicLock(RandomSeries random, int slots, int positions)
        {
            Positions = positions;
            _answer = new int[slots];

            // 始めの値（全て 0）と同じ答えは作らない（何もせずに解けないように）。
            do
            {
                for (int i = 0; i < slots; i++) { _answer[i] = random.Next(positions); }
            }
            while (Array.TrueForAll(_answer, v => v == 0));
            Answer = Array.AsReadOnly(_answer);
        }

        public abstract MechanismKind Kind { get; }
        public Phase InitialPhase => Kind == MechanismKind.Dial ? Phase.Active : Phase.Locked;
        public Phase InstalledPhase => Phase.Active;
        public int Slots => _answer.Length;
        public int ValueCount => _answer.Length;

        /// <summary>1 つの部品の段の数。</summary>
        public int Positions { get; }

        public IReadOnlyList<int> Answer { get; }
        public abstract string AnswerText { get; }

        public void Reset(int[] values) => Array.Clear(values, 0, values.Length);

        public void Turn(int[] values, int slot, out bool mistake)
        {
            mistake = false;
            values[slot] = (values[slot] + 1) % Positions;
        }

        public bool IsSolved(int[] values)
        {
            for (int i = 0; i < _answer.Length; i++)
            {
                if (values[i] != _answer[i]) { return false; }
            }

            return true;
        }

        public IReadOnlyList<int> SolutionFrom(int[] values)
        {
            var result = new List<int>();
            for (int i = 0; i < _answer.Length; i++)
            {
                var steps = ((_answer[i] - values[i]) % Positions + Positions) % Positions;
                for (int k = 0; k < steps; k++) { result.Add(i); }
            }

            return result.AsReadOnly();
        }
    }

    /// <summary>引き出しの 3 桁の数字ダイヤル錠。</summary>
    public sealed class DialLock : CyclicLock
    {
        public const int Digits = 3;

        public DialLock(RandomSeries random)
            : base(random, Digits, 10)
        {
        }

        public override MechanismKind Kind => MechanismKind.Dial;

        public override string AnswerText => $"{Answer[0]}・{Answer[1]}・{Answer[2]}";
    }

    /// <summary>天球儀のリング（上・中・下の 3 本）。値は正面に来る星座の番号。</summary>
    public sealed class RingLock : CyclicLock
    {
        public const int RingCount = 3;

        private static readonly ReadOnlyCollection<string> _constellations =
            Array.AsReadOnly(new[] { "獅子", "白鳥", "蠍", "琴", "鷲", "冠", "竜", "魚" });

        public RingLock(RandomSeries random)
            : base(random, RingCount, 8)
        {
        }

        /// <summary>リングの 8 つの星座の名前（番号順）。</summary>
        public static IReadOnlyList<string> Constellations => _constellations;

        public override MechanismKind Kind => MechanismKind.Rings;

        public override string AnswerText => $"上から {Constellations[Answer[0]]}・{Constellations[Answer[1]]}・{Constellations[Answer[2]]}";
    }

    /// <summary>柱時計の針（0 短針・1 長針）。値は 12 段（短針は時、長針は 5 分ずつ）。0 は 12 時ちょうど。</summary>
    public sealed class ClockHands : CyclicLock
    {
        public ClockHands(RandomSeries random)
            : base(random, 2, 12)
        {
        }

        public override MechanismKind Kind => MechanismKind.Clock;

        public override string AnswerText => TimeText(Answer[0], Answer[1]);

        public static string TimeText(int hour, int minute) => $"{(hour == 0 ? 12 : hour)} 時 {minute * 5:00} 分";
    }

    /// <summary>鍵穴。合う鍵を入れると開く（動かす部品はない）。</summary>
    public sealed class KeyLock : IMechanism
    {
        private static readonly ReadOnlyCollection<int> _none = Array.AsReadOnly(Array.Empty<int>());

        public MechanismKind Kind => MechanismKind.KeyLock;
        public Phase InitialPhase => Phase.Locked;
        public Phase InstalledPhase => Phase.Solved;
        public int Slots => 0;
        public int ValueCount => 0;
        public IReadOnlyList<int> Answer => _none;
        public string AnswerText => "鍵を差す";

        public void Reset(int[] values)
        {
        }

        public void Turn(int[] values, int slot, out bool mistake) => throw new InvalidOperationException("鍵穴に動かす部品はありません。");

        public bool IsSolved(int[] values) => false;

        public IReadOnlyList<int> SolutionFrom(int[] values) => _none;
    }

    /// <summary>
    /// 金庫の順番ボタン（4 つの宝石を答えの順に 1 回ずつ押す）。値は正しく押した数。
    /// 間違えると最初から。ただし押した宝石が答えの最初なら、それを 1 つ目として数える
    /// （答えの順に全部押せば、途中の状態に依らず必ず開く）。
    /// </summary>
    public sealed class SequenceLock : IMechanism
    {
        public const int GemCount = 4;

        private static readonly ReadOnlyCollection<string> _gems = Array.AsReadOnly(new[] { "紅玉", "青玉", "翠玉", "黄玉" });
        private static readonly string[] _colors = { "赤", "青", "緑", "黄" };

        private readonly int[] _answer;

        public SequenceLock(RandomSeries random)
        {
            _answer = new int[GemCount];
            for (int i = 0; i < GemCount; i++) { _answer[i] = i; }
            for (int i = GemCount - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (_answer[i], _answer[j]) = (_answer[j], _answer[i]);
            }

            Answer = Array.AsReadOnly(_answer);
        }

        /// <summary>宝石の名前（番号順、0 赤・1 青・2 緑・3 黄）。</summary>
        public static IReadOnlyList<string> Gems => _gems;

        public MechanismKind Kind => MechanismKind.Sequence;
        public Phase InitialPhase => Phase.Locked;
        public Phase InstalledPhase => Phase.Active;
        public int Slots => GemCount;
        public int ValueCount => 1;
        public IReadOnlyList<int> Answer { get; }

        public string AnswerText => $"{Gem(_answer[0])} → {Gem(_answer[1])} → {Gem(_answer[2])} → {Gem(_answer[3])}";

        /// <summary>宝石の名前と色（「紅玉（赤）」）。</summary>
        public static string Gem(int gem) => $"{_gems[gem]}（{_colors[gem]}）";

        public void Reset(int[] values) => values[0] = 0;

        public void Turn(int[] values, int slot, out bool mistake)
        {
            if (slot == _answer[values[0]])
            {
                mistake = false;
                values[0]++;
                return;
            }

            mistake = true;
            values[0] = slot == _answer[0] ? 1 : 0;
        }

        public bool IsSolved(int[] values) => values[0] >= GemCount;

        public IReadOnlyList<int> SolutionFrom(int[] values) => Answer;
    }
}
