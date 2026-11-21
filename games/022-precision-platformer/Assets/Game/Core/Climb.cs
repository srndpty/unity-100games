using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>
    /// 部屋を順に抜ける 1 回の挑戦。残機はなく、死亡は回数を数えるだけ。
    /// 部屋の出口に触れた Tick に次の部屋の挑戦を作り、次の Tick からその開始位置で進む。最後の部屋を抜けたら勝利。
    /// 勝利の後の Tick と入力は受け付けない。
    /// </summary>
    public sealed class Climb
    {
        private readonly PrecisionConfig _config;
        private readonly Room[] _rooms;
        private RunEventKind _events;

        public Climb(PrecisionConfig config, IReadOnlyList<Room> rooms)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (rooms == null) { throw new ArgumentNullException(nameof(rooms)); }
            if (rooms.Count == 0) { throw new ArgumentException("部屋が 1 つもありません。", nameof(rooms)); }
            config.Validate();
            _config = config.Copy();
            _rooms = new Room[rooms.Count];
            for (var i = 0; i < _rooms.Length; i++)
            {
                _rooms[i] = rooms[i] ?? throw new ArgumentException($"{i} 番目の部屋が null です。", nameof(rooms));
            }

            Rooms = Array.AsReadOnly(_rooms);
            Run = new RoomRun(_config, _rooms[0]);
        }

        public ReadOnlyCollection<Room> Rooms { get; }

        public int RoomIndex { get; private set; }

        public RoomRun Run { get; private set; }

        /// <summary>これまでの部屋を含めた死亡回数。</summary>
        public int Deaths { get; private set; }

        /// <summary>挑戦を始めてから進めた Tick 数。</summary>
        public long ElapsedTicks { get; private set; }

        public bool IsWon { get; private set; }

        /// <summary>1 Tick 進める。勝利の後は何もせず false。</summary>
        public bool Tick(PlayerInput input)
        {
            if (IsWon)
            {
                return false;
            }

            Run.Tick(input);
            ElapsedTicks++;
            var events = Run.TakeEvents();
            _events |= events;
            if ((events & RunEventKind.Died) != 0)
            {
                Deaths++;
            }

            if (Run.IsCleared)
            {
                if (RoomIndex == _rooms.Length - 1)
                {
                    IsWon = true;
                }
                else
                {
                    RoomIndex++;
                    Run = new RoomRun(_config, _rooms[RoomIndex]);
                }
            }

            return true;
        }

        /// <summary>前回取り出してから起きた出来事。</summary>
        public RunEventKind TakeEvents()
        {
            var events = _events;
            _events = RunEventKind.None;
            return events;
        }

        /// <summary>状態のハッシュ。今の部屋の挑戦の全状態と、進行（部屋・死亡回数・経過 Tick・勝利）を含む。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(RoomIndex);
            hash.Mix(Deaths);
            hash.Mix(ElapsedTicks);
            hash.Mix(IsWon);
            return Run.Mix(hash).Value;
        }
    }
}
