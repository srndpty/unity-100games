using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;

namespace Game.Core
{
    /// <summary>見えている敵 1 体（今の視界の中にいる敵だけ）。</summary>
    public readonly struct MonsterSighting
    {
        public MonsterSighting(int id, MonsterKind kind, Vec2i position, int hp, int maxHp, bool sleeping)
        {
            Id = id;
            Kind = kind;
            Position = position;
            Hp = hp;
            MaxHp = maxHp;
            Sleeping = sleeping;
        }

        public int Id { get; }

        public MonsterKind Kind { get; }

        public Vec2i Position { get; }

        public int Hp { get; }

        public int MaxHp { get; }

        public bool Sleeping { get; }
    }

    /// <summary>
    /// 画面（HUD・地図・メッセージ）に出してよい情報だけの窓口。Unity 層と操縦役はここだけを読む。
    /// 地形は既知マップ（一度見たマス）、品は視界の中なら今の品・外なら最後に見た記憶、敵は視界の中だけ、
    /// 出来事は見えたものだけを出す。視界の外の敵の位置・数、見ていない地形・品はどこからも取れない。
    /// </summary>
    public sealed class PlayerView
    {
        private readonly CrawlRun _run;
        private readonly List<MonsterSighting> _monsters = new();
        private readonly List<RunEvent> _events = new();
        private readonly List<ItemKind> _inventory = new();

        internal PlayerView(CrawlRun run)
        {
            _run = run;
            Monsters = _monsters.AsReadOnly();
            Events = _events.AsReadOnly();
            Inventory = _inventory.AsReadOnly();
        }

        /// <summary>写し直すたびに増える番号（描画の書き換えの判定用）。</summary>
        public int Version { get; private set; }

        public int Width => _run.Grid.Width;

        public int Height => _run.Grid.Height;

        public int Floor => _run.Floor;

        public RunState State => _run.State;

        public Vec2i Position => _run.Player.Position;

        public Direction Facing => _run.Player.Facing;

        public int Hp => _run.Player.Hp;

        public int MaxHp => _run.Player.MaxHp;

        public int Level => _run.Player.Level;

        public int Exp => _run.Player.Exp;

        /// <summary>次のレベルに要る経験値の累計。最高レベルなら -1。</summary>
        public int NextExp => _run.Player.Level < _run.Rules.MaxLevel ? _run.Rules.LevelExp[_run.Player.Level - 1] : -1;

        public int Attack => _run.Player.Attack;

        public int Defense => _run.Player.Defense;

        public int Satiety => _run.Player.Satiety;

        public int MaxSatiety => _run.Rules.MaxSatiety;

        public bool Asleep => _run.Player.SleepTurns > 0;

        /// <summary>時間を使った自機の行動の数。</summary>
        public int Turns => _run.Player.Actions;

        public int Kills => _run.Kills;

        public DeathCause DeathCause => _run.DeathCause;

        public MonsterKind Killer => _run.Killer;

        public int InventoryCapacity => _run.Rules.InventoryCapacity;

        public ReadOnlyCollection<ItemKind> Inventory { get; }

        /// <summary>今の視界の中にいる敵（番号の順）。</summary>
        public ReadOnlyCollection<MonsterSighting> Monsters { get; }

        /// <summary>直前の操作の間に起きた出来事のうち、見えたもの。</summary>
        public ReadOnlyCollection<RunEvent> Events { get; }

        /// <summary>覚えている地形。一度も見ていないマスは null。</summary>
        public Tile? TileAt(Vec2i p) => _run.Known.IsSeen(p) ? _run.Grid[p] : null;

        public bool IsKnown(Vec2i p) => _run.Known.IsSeen(p);

        public bool IsVisible(Vec2i p) => _run.IsVisible(p);

        /// <summary>品。視界の中なら今の品、外なら最後に見たときの品（古いことがある）。</summary>
        public ItemKind? ItemAt(Vec2i p) => _run.IsVisible(p) ? _run.ItemAt(p) : _run.Known.RememberedItem(p);

        /// <summary>見えている敵。いなければ null。</summary>
        public MonsterSighting? MonsterAt(Vec2i p)
        {
            foreach (var m in _monsters)
            {
                if (m.Position == p) { return m; }
            }

            return null;
        }

        /// <summary>試合の状態を写し直す（<see cref="CrawlRun"/> が操作のたびに呼ぶ）。</summary>
        internal void Refresh()
        {
            Version++;
            _monsters.Clear();
            foreach (var m in _run.Monsters)
            {
                if (_run.IsVisible(m.Position)) { _monsters.Add(new MonsterSighting(m.Id, m.Kind, m.Position, m.Hp, m.MaxHp, m.IsSleeping)); }
            }

            _monsters.Sort((a, b) => a.Id.CompareTo(b.Id));
            _events.Clear();
            foreach (var e in _run.Events)
            {
                if (e.Seen) { _events.Add(e); }
            }

            _inventory.Clear();
            _inventory.AddRange(_run.Player.Inventory);
        }
    }
}
