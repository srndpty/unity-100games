using System;
using System.Collections.Generic;
using Game.Core.Mechanisms;
using Game.Core.Play;
using Game.Core.Rooms;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>テストの道具。本番の部屋（書斎）を使い、視点の移動と次の一手の再生を助ける。</summary>
    internal static class Lab
    {
        private static RoomDef? _study;

        public static RoomDef Study => _study ??= StudyRoom.Create();

        public static World NewWorld(int seed = 1) => new(new Room(Study, seed));

        public static int Part(string id) => Find(Study.Parts, p => p.Id == id, id).Index;

        public static int Item(string id) => Find(Study.Items, i => i.Id == id, id).Index;

        public static int Mechanism(string id) => Find(Study.Mechanisms, m => m.Id == id, id).Index;

        public static int View(string id) => Find(Study.Views, v => v.Id == id, id).Index;

        /// <summary>命令を当てはめ、受け付けたことを確かめる。</summary>
        public static void Do(World w, Command c)
        {
            var o = w.Apply(c);
            Assert.That(o.Accepted, Is.True, $"{c} が {o.Reason} で拒否されました");
        }

        /// <summary>視点へ移る（受け付けたことを確かめる）。</summary>
        public static void GoTo(World w, int view)
        {
            foreach (var c in Navigation.PathTo(w, view)) { Do(w, c); }
            Assert.That(w.View, Is.EqualTo(view));
        }

        /// <summary>次の一手（ヒントの答え）を 1 つ行う。</summary>
        public static HintStep Advance(World w)
        {
            var step = HintEngine.Next(w);
            GoTo(w, step.View(w.Def));
            foreach (var c in step.Answer(w)) { Do(w, c); }
            return step;
        }

        /// <summary>次の一手を繰り返して脱出する（命令の数の上限付き）。</summary>
        public static int Escape(World w, int maxSteps = 64)
        {
            int steps = 0;
            while (!w.Escaped)
            {
                Assert.That(steps++, Is.LessThan(maxSteps), "次の一手を繰り返しても脱出しません");
                Advance(w);
            }

            return steps;
        }

        /// <summary>仕掛けを解く直前の状態まで次の一手を進める。</summary>
        public static void AdvanceUntil(World w, Func<World, bool> done, int maxSteps = 64)
        {
            int steps = 0;
            while (!done(w))
            {
                Assert.That(steps++, Is.LessThan(maxSteps));
                Advance(w);
            }
        }

        public static bool Active(World w, string mechanism) => w.PhaseOf(Mechanism(mechanism)) == Phase.Active;

        private static T Find<T>(IEnumerable<T> list, Func<T, bool> match, string id)
        {
            foreach (var x in list)
            {
                if (match(x)) { return x; }
            }

            throw new ArgumentException($"{id} がありません");
        }
    }
}
