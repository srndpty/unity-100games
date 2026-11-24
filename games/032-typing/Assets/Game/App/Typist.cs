using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>キーの代わりに Tick ごとに 1 打を決める操縦役（撮影・テスト用）。</summary>
    public interface IPilot
    {
        /// <summary>新しい試合の前に呼ぶ。前の試合の判断の状態を捨てる。</summary>
        void Reset(int seed);

        /// <summary>この Tick の前に打つキー。打たなければ null、Backspace は <see cref="Typist.BackspaceKey"/>。</summary>
        char? KeyFor(TypingRun run);
    }

    /// <summary>
    /// 一定の間隔で打つ操縦役。狙いの敵（なければ最も手前の敵）の案内の先頭を打つ。
    /// <see cref="MistakePermil"/> の割合で、その敵が受け付けない文字（狙いがなければどの敵も受け付けない文字）をわざと打つ。
    /// </summary>
    public sealed class Typist : IPilot
    {
        /// <summary>Backspace を表す文字。</summary>
        public const char BackspaceKey = '\b';

        private readonly List<char> _wrong = new();
        private SeededRandom _random = new(1);
        private int _cooldown;

        /// <param name="intervalTicks">打鍵の間隔（Tick）。1 以上。</param>
        /// <param name="mistakePermil">誤字の割合（‰）。0〜1000。</param>
        public Typist(int intervalTicks, int mistakePermil = 0)
        {
            if (intervalTicks < 1) { throw new ArgumentOutOfRangeException(nameof(intervalTicks), intervalTicks, "1 以上にしてください。"); }
            if (mistakePermil < 0 || mistakePermil > 1000) { throw new ArgumentOutOfRangeException(nameof(mistakePermil), mistakePermil, "0〜1000 にしてください。"); }
            IntervalTicks = intervalTicks;
            MistakePermil = mistakePermil;
        }

        public int IntervalTicks { get; }

        public int MistakePermil { get; }

        public void Reset(int seed)
        {
            _random = new SeededRandom(seed);
            _cooldown = 0;
        }

        public char? KeyFor(TypingRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (_cooldown > 0) { _cooldown--; }
            if (_cooldown > 0) { return null; }
            var target = run.Target ?? Nearest(run);
            if (target == null) { return null; }
            _cooldown = IntervalTicks;
            if (MistakePermil > 0 && _random.Next(1000) < MistakePermil)
            {
                var wrong = WrongKey(run);
                if (wrong != null) { return wrong; }
            }

            return target.Matcher.Guide[0];
        }

        /// <summary>最も手前（出現からの Tick が多い）、同じなら出現順の早い敵。狙いの規則と同じ順序。</summary>
        public static Enemy? Nearest(TypingRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            Enemy? best = null;
            foreach (var enemy in run.Enemies)
            {
                if (best == null || enemy.Age > best.Age) { best = enemy; }
            }

            return best;
        }

        private char? WrongKey(TypingRun run)
        {
            _wrong.Clear();
            foreach (var key in RomajiTable.Keys)
            {
                var accepted = run.Target != null ? run.Target.Matcher.Accepts(key) : AnyAccepts(run, key);
                if (!accepted) { _wrong.Add(key); }
            }

            return _wrong.Count == 0 ? null : _wrong[_random.Next(_wrong.Count)];
        }

        private static bool AnyAccepts(TypingRun run, char key)
        {
            foreach (var enemy in run.Enemies)
            {
                if (enemy.Matcher.Accepts(key)) { return true; }
            }

            return false;
        }
    }
}
