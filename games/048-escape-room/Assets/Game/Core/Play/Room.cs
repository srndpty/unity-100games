using System;
using System.Collections.ObjectModel;
using Game.Core.Mechanisms;
using Game.Core.Rooms;
using IntGeometry;

namespace Game.Core.Play
{
    /// <summary>
    /// 部屋の定義と、試合のシードから作った仕掛け（答えを持つ）の組。1 試合 = 1 シードで、再戦は新しいシードで作り直す。
    /// 仕掛けごとに試合のシード・仕掛けの番号・用途から派生した乱数系列を使うので、ある仕掛けの答えの作り方を変えても
    /// ほかの仕掛けの答えは変わらない。
    /// </summary>
    public sealed class Room
    {
        /// <summary>答えを作る乱数の用途の番号。</summary>
        public const int AnswerPurpose = 1;

        public Room(RoomDef def, int seed)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Seed = seed;
            var mechanisms = new IMechanism[def.Mechanisms.Count];
            foreach (var m in def.Mechanisms)
            {
                mechanisms[m.Index] = MechanismFactory.Create(m.Kind, RandomSeries.Derive(seed, m.Index, AnswerPurpose));
            }

            Mechanisms = Array.AsReadOnly(mechanisms);
        }

        public RoomDef Def { get; }
        public int Seed { get; }
        public ReadOnlyCollection<IMechanism> Mechanisms { get; }
    }
}
