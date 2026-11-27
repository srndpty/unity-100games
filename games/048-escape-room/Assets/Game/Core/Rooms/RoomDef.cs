using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core.Geometry;
using Game.Core.Mechanisms;

namespace Game.Core.Rooms
{
    public enum ViewKind
    {
        /// <summary>部屋の中央から 4 面の壁の 1 つを見る。</summary>
        Wall,

        /// <summary>仕掛けの近景（壁の視点からクリックで寄る）。</summary>
        CloseUp,
    }

    public enum PartKind
    {
        /// <summary>家具の本体・扉・前板など。遮るだけで操作はない（壁の視点では寄る先になる）。</summary>
        Body,

        /// <summary>仕掛けの動かせる部品（桁・リング・針・宝石）。</summary>
        Slot,

        /// <summary>鍵穴（鍵を差すと開く）。</summary>
        Keyhole,

        /// <summary>持ち物の差し込み口（円盤の台座・歯車の軸・ハンドルの穴）。</summary>
        Socket,

        /// <summary>拾える物。拾うと持ち物になる。</summary>
        Pickup,

        /// <summary>手に取って回して見る物（手がかり）。</summary>
        Examinable,
    }

    /// <summary>箱があるか（遮る・当たる）を決める条件。状態だけから決め、ヒットテストは状態から作った箱の列で行う。</summary>
    public enum Presence
    {
        Always,

        /// <summary>仕掛けが解けていない間（閉じた扉・前板）。</summary>
        WhileClosed,

        /// <summary>仕掛けが解けた後（開いた扉・引き出した箱）。</summary>
        WhileOpen,

        /// <summary>仕掛けに持ち物が入った後（はめた円盤など）。</summary>
        WhileInstalled,

        /// <summary>拾うまで（拾える物）。</summary>
        UntilTaken,
    }

    public sealed class ItemDef
    {
        internal ItemDef(int index, string id, string name, int examinable, string where)
        {
            Index = index;
            Id = id;
            Name = name;
            Examinable = examinable;
            Where = where;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }

        /// <summary>持ち物から手に取って回せるなら、その物の番号（なければ -1）。</summary>
        public int Examinable { get; }

        /// <summary>置いてある場所の言い方（ヒントの文）。</summary>
        public string Where { get; }
    }

    public sealed class ExaminableDef
    {
        internal ExaminableDef(int index, string id, string name, Vec3i clueFace, Vec3i clueUp, int reveals)
        {
            Index = index;
            Id = id;
            Name = name;
            ClueFace = clueFace;
            ClueUp = clueUp;
            Reveals = reveals;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }

        /// <summary>手がかりが書いてある面の法線（物の座標、軸に平行な単位ベクトル）。</summary>
        public Vec3i ClueFace { get; }

        /// <summary>手がかりの「上」の向き（物の座標、軸に平行な単位ベクトル、面に平行）。手がかりは正立しているときだけ読める。</summary>
        public Vec3i ClueUp { get; }

        /// <summary>手がかりが答えを示す仕掛けの番号。</summary>
        public int Reveals { get; }
    }

    public sealed class MechanismDef
    {
        internal MechanismDef(int index, string id, string name, MechanismKind kind, int item, string direction, string clue)
        {
            Index = index;
            Id = id;
            Name = name;
            Kind = kind;
            Item = item;
            Direction = direction;
            Clue = clue;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public MechanismKind Kind { get; }

        /// <summary>Locked を外す持ち物（鍵・円盤・歯車・ハンドル）。ダイヤルは -1。</summary>
        public int Item { get; }

        /// <summary>ヒントの 1 段目（方向付け）。</summary>
        public string Direction { get; }

        /// <summary>ヒントの 2 段目（具体的な手がかり）。</summary>
        public string Clue { get; }
    }

    public sealed class PartDef
    {
        internal PartDef(int index, string id, string name, PartKind kind, int view, Box3 box, Presence presence, int presenceOf, int container, int mechanism, int slot, int item, int examinable)
        {
            Index = index;
            Id = id;
            Name = name;
            Kind = kind;
            View = view;
            Box = box;
            Presence = presence;
            PresenceOf = presenceOf;
            Container = container;
            Mechanism = mechanism;
            Slot = slot;
            Item = item;
            Examinable = examinable;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public PartKind Kind { get; }

        /// <summary>操作できる近景の番号（壁の視点でこの箱に当たると、この近景へ寄る）。</summary>
        public int View { get; }

        public Box3 Box { get; }
        public Presence Presence { get; }

        /// <summary>Presence が仕掛けを見るときの仕掛けの番号（UntilTaken・Always では -1）。</summary>
        public int PresenceOf { get; }

        /// <summary>中に入っている仕掛け（解けるまで触れない）。なければ -1。</summary>
        public int Container { get; }

        /// <summary>Slot・Keyhole・Socket の仕掛け（ほかは -1）。</summary>
        public int Mechanism { get; }

        /// <summary>Slot の番号（ほかは -1）。</summary>
        public int Slot { get; }

        /// <summary>Pickup の持ち物（ほかは -1）。</summary>
        public int Item { get; }

        /// <summary>Examinable の物（ほかは -1）。</summary>
        public int Examinable { get; }
    }

    public sealed class ViewDef
    {
        internal ViewDef(int index, string id, string name, ViewKind kind, CameraPose pose, int reveals)
        {
            Index = index;
            Id = id;
            Name = name;
            Kind = kind;
            Pose = pose;
            Reveals = reveals;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public ViewKind Kind { get; }
        public CameraPose Pose { get; }
        public int Wall => Pose.Wall;

        /// <summary>のぞくだけで答えが見える仕掛け（望遠鏡の星の瞬き）。なければ -1。</summary>
        public int Reveals { get; }
    }

    /// <summary>部屋の定義の誤り（定義を作るときに検証し、理由付きで拒否する）。</summary>
    public sealed class RoomDefinitionException : Exception
    {
        public RoomDefinitionException()
        {
        }

        public RoomDefinitionException(string message)
            : base(message)
        {
        }

        public RoomDefinitionException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// 部屋の定義（持ち物・手に取る物・仕掛け・部品の箱・視点・出口）。<see cref="RoomBuilder"/> で組み立て、作るときに検証する。
    /// 答え（シード）は持たない。
    /// </summary>
    public sealed class RoomDef
    {
        internal RoomDef(string name, Box3 bounds, List<ItemDef> items, List<ExaminableDef> examinables, List<MechanismDef> mechanisms, List<PartDef> parts, List<ViewDef> views, int exit)
        {
            Name = name;
            Bounds = bounds;
            Items = items.AsReadOnly();
            Examinables = examinables.AsReadOnly();
            Mechanisms = mechanisms.AsReadOnly();
            Parts = parts.AsReadOnly();
            Views = views.AsReadOnly();
            Exit = exit;
            var walls = new int[4];
            foreach (var v in views)
            {
                if (v.Kind == ViewKind.Wall) { walls[v.Wall] = v.Index; }
            }

            WallViews = Array.AsReadOnly(walls);
            Validate();
            Fingerprint = ComputeFingerprint();
        }

        public string Name { get; }

        /// <summary>部屋の内側の範囲（mm）。</summary>
        public Box3 Bounds { get; }

        public ReadOnlyCollection<ItemDef> Items { get; }
        public ReadOnlyCollection<ExaminableDef> Examinables { get; }
        public ReadOnlyCollection<MechanismDef> Mechanisms { get; }
        public ReadOnlyCollection<PartDef> Parts { get; }
        public ReadOnlyCollection<ViewDef> Views { get; }

        /// <summary>壁ごとの視点の番号。</summary>
        public ReadOnlyCollection<int> WallViews { get; }

        /// <summary>出口の扉の仕掛け（解けたら脱出）。</summary>
        public int Exit { get; }

        /// <summary>定義の指紋（状態のハッシュに混ぜる）。</summary>
        public uint Fingerprint { get; }

        /// <summary>この仕掛けの Locked を外す持ち物を差す部品（Keyhole・Socket）。なければ -1。</summary>
        public int SocketOf(int mechanism)
        {
            foreach (var p in Parts)
            {
                if ((p.Kind == PartKind.Keyhole || p.Kind == PartKind.Socket) && p.Mechanism == mechanism) { return p.Index; }
            }

            return -1;
        }

        /// <summary>この持ち物を拾う部品。</summary>
        public int PickupOf(int item)
        {
            foreach (var p in Parts)
            {
                if (p.Kind == PartKind.Pickup && p.Item == item) { return p.Index; }
            }

            return -1;
        }

        /// <summary>この仕掛けの Slot の部品（番号順）。</summary>
        public int SlotPart(int mechanism, int slot)
        {
            foreach (var p in Parts)
            {
                if (p.Kind == PartKind.Slot && p.Mechanism == mechanism && p.Slot == slot) { return p.Index; }
            }

            return -1;
        }

        /// <summary>仕掛けを操作する近景（部品の近景）。</summary>
        public int ViewOf(int mechanism)
        {
            foreach (var p in Parts)
            {
                if (p.Mechanism == mechanism) { return p.View; }
            }

            return -1;
        }

        private void Validate()
        {
            var errors = new List<string>();
            void Unique<T>(IEnumerable<T> list, Func<T, string> id, string what)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var x in list)
                {
                    if (string.IsNullOrEmpty(id(x))) { errors.Add($"{what}の ID が空です"); }
                    else if (!seen.Add(id(x))) { errors.Add($"{what}の ID {id(x)} が重複しています"); }
                }
            }

            Unique(Items, x => x.Id, "持ち物");
            Unique(Examinables, x => x.Id, "手に取る物");
            Unique(Mechanisms, x => x.Id, "仕掛け");
            Unique(Parts, x => x.Id, "部品");
            Unique(Views, x => x.Id, "視点");
            if (!Bounds.IsValid) { errors.Add("部屋の範囲が空です"); }

            var wallCount = new int[4];
            foreach (var v in Views)
            {
                if (v.Kind == ViewKind.Wall) { wallCount[v.Wall]++; }
                if (!Bounds.Contains(v.Pose.Eye)) { errors.Add($"視点 {v.Id} の目が部屋の外です"); }
                if (v.Reveals >= Mechanisms.Count || v.Reveals < -1) { errors.Add($"視点 {v.Id} の答えを示す仕掛けの番号が範囲外です"); }
            }

            for (int w = 0; w < 4; w++)
            {
                if (wallCount[w] != 1) { errors.Add($"壁 {w} の視点が {wallCount[w]} 個です（1 個）"); }
            }

            if (Exit < 0 || Exit >= Mechanisms.Count) { errors.Add("出口の仕掛けの番号が範囲外です"); }
            else if (Mechanisms[Exit].Kind != MechanismKind.KeyLock) { errors.Add("出口は鍵穴の仕掛けです"); }

            foreach (var e in Examinables)
            {
                var n = e.ClueFace;
                if (Math.Abs(n.X) + Math.Abs(n.Y) + Math.Abs(n.Z) != 1) { errors.Add($"手に取る物 {e.Id} の手がかりの面は軸に平行な単位ベクトルです"); }
                var u = e.ClueUp;
                if (Math.Abs(u.X) + Math.Abs(u.Y) + Math.Abs(u.Z) != 1 || Vec3i.Dot(n, u) != 0) { errors.Add($"手に取る物 {e.Id} の手がかりの上は、面に平行な軸の単位ベクトルです"); }
                if (e.Reveals < 0 || e.Reveals >= Mechanisms.Count) { errors.Add($"手に取る物 {e.Id} の答えを示す仕掛けの番号が範囲外です"); }
            }

            foreach (var item in Items)
            {
                if (item.Examinable < -1 || item.Examinable >= Examinables.Count) { errors.Add($"持ち物 {item.Id} の手に取る物の番号が範囲外です"); }
                int pickups = 0;
                foreach (var p in Parts)
                {
                    if (p.Kind == PartKind.Pickup && p.Item == item.Index) { pickups++; }
                }

                if (pickups != 1) { errors.Add($"持ち物 {item.Id} を拾う部品が {pickups} 個です（1 個）"); }
                int uses = 0;
                foreach (var m in Mechanisms)
                {
                    if (m.Item == item.Index) { uses++; }
                }

                if (uses != 1) { errors.Add($"持ち物 {item.Id} を使う仕掛けが {uses} 個です（1 個）"); }
            }

            foreach (var m in Mechanisms)
            {
                var needs = MechanismFactory.NeedsItem(m.Kind);
                if (needs != (m.Item >= 0)) { errors.Add($"仕掛け {m.Id} の持ち物の有無が種類と合いません"); }
                if (m.Item >= Items.Count) { errors.Add($"仕掛け {m.Id} の持ち物の番号が範囲外です"); }
                for (int s = 0; s < MechanismFactory.SlotsOf(m.Kind); s++)
                {
                    int n = 0;
                    foreach (var p in Parts)
                    {
                        if (p.Kind == PartKind.Slot && p.Mechanism == m.Index && p.Slot == s) { n++; }
                    }

                    if (n != 1) { errors.Add($"仕掛け {m.Id} の部品 {s} が {n} 個です（1 個）"); }
                }

                int sockets = 0;
                foreach (var p in Parts)
                {
                    if ((p.Kind == PartKind.Keyhole || p.Kind == PartKind.Socket) && p.Mechanism == m.Index) { sockets++; }
                }

                if (sockets != (needs ? 1 : 0)) { errors.Add($"仕掛け {m.Id} の差し込み口が {sockets} 個です"); }
            }

            foreach (var p in Parts)
            {
                var where = $"部品 {p.Id}";
                if (!p.Box.IsValid) { errors.Add($"{where} の箱が空です"); }
                else if (!p.Box.Inside(Bounds)) { errors.Add($"{where} の箱が部屋の外にはみ出します"); }
                if (p.View < 0 || p.View >= Views.Count || Views[p.View].Kind != ViewKind.CloseUp) { errors.Add($"{where} の近景が近景の視点ではありません"); }
                if (p.Container < -1 || p.Container >= Mechanisms.Count) { errors.Add($"{where} の入れ物の番号が範囲外です"); }
                var needsMechanism = p.Presence == Presence.WhileClosed || p.Presence == Presence.WhileOpen || p.Presence == Presence.WhileInstalled;
                if (needsMechanism != (p.PresenceOf >= 0) || p.PresenceOf >= Mechanisms.Count) { errors.Add($"{where} の現れる条件の仕掛けが合いません"); }
                if ((p.Presence == Presence.UntilTaken) != (p.Kind == PartKind.Pickup)) { errors.Add($"{where}：拾うまである（UntilTaken）のは拾える物だけです"); }
                switch (p.Kind)
                {
                    case PartKind.Slot:
                        if (p.Mechanism < 0 || p.Mechanism >= Mechanisms.Count) { errors.Add($"{where} の仕掛けの番号が範囲外です"); }
                        else if (p.Slot < 0 || p.Slot >= MechanismFactory.SlotsOf(Mechanisms[p.Mechanism].Kind)) { errors.Add($"{where} の部品の番号が範囲外です"); }
                        break;
                    case PartKind.Keyhole:
                    case PartKind.Socket:
                        if (p.Mechanism < 0 || p.Mechanism >= Mechanisms.Count) { errors.Add($"{where} の仕掛けの番号が範囲外です"); }
                        else if ((Mechanisms[p.Mechanism].Kind == MechanismKind.KeyLock) != (p.Kind == PartKind.Keyhole)) { errors.Add($"{where}：鍵穴は鍵穴の仕掛けだけ、差し込み口はほかの仕掛けだけです"); }
                        break;
                    case PartKind.Pickup:
                        if (p.Item < 0 || p.Item >= Items.Count) { errors.Add($"{where} の持ち物の番号が範囲外です"); }
                        break;
                    case PartKind.Examinable:
                        if (p.Examinable < 0 || p.Examinable >= Examinables.Count) { errors.Add($"{where} の手に取る物の番号が範囲外です"); }
                        break;
                }

                foreach (var v in Views)
                {
                    if (p.Box.Contains(v.Pose.Eye)) { errors.Add($"視点 {v.Id} の目が {where} の箱の中です"); }
                }
            }

            if (errors.Count > 0) { throw new RoomDefinitionException($"部屋 {Name} の定義の誤り：" + string.Join("／", errors)); }
        }

        private uint ComputeFingerprint()
        {
            var h = TileMotion.StateHash.Start();
            h.Mix(Items.Count);
            h.Mix(Examinables.Count);
            foreach (var e in Examinables)
            {
                h.Mix(e.ClueFace.X);
                h.Mix(e.ClueFace.Y);
                h.Mix(e.ClueFace.Z);
                h.Mix(e.ClueUp.X);
                h.Mix(e.ClueUp.Y);
                h.Mix(e.ClueUp.Z);
                h.Mix(e.Reveals);
            }

            foreach (var m in Mechanisms)
            {
                h.Mix((int)m.Kind);
                h.Mix(m.Item);
            }

            foreach (var p in Parts)
            {
                h.Mix((int)p.Kind);
                h.Mix(p.View);
                MixBox(ref h, p.Box);
                h.Mix((int)p.Presence);
                h.Mix(p.PresenceOf);
                h.Mix(p.Container);
                h.Mix(p.Mechanism);
                h.Mix(p.Slot);
                h.Mix(p.Item);
                h.Mix(p.Examinable);
            }

            foreach (var v in Views)
            {
                h.Mix((int)v.Kind);
                h.Mix(v.Pose.Eye.X);
                h.Mix(v.Pose.Eye.Y);
                h.Mix(v.Pose.Eye.Z);
                h.Mix(v.Pose.Wall);
                h.Mix(v.Pose.Pitch);
                h.Mix(v.Pose.Focal);
                h.Mix(v.Reveals);
            }

            h.Mix(Exit);
            return h.Value;
        }

        private static void MixBox(ref TileMotion.StateHash h, Box3 b)
        {
            h.Mix(b.Min.X);
            h.Mix(b.Min.Y);
            h.Mix(b.Min.Z);
            h.Mix(b.Max.X);
            h.Mix(b.Max.Y);
            h.Mix(b.Max.Z);
        }
    }

    /// <summary>部屋の定義を組み立てる。追加した順が番号になる（部品は後に足したものが同じ距離で手前として当たる）。</summary>
    public sealed class RoomBuilder
    {
        private readonly string _name;
        private readonly Box3 _bounds;
        private readonly List<ItemDef> _items = new();
        private readonly List<ExaminableDef> _examinables = new();
        private readonly List<MechanismDef> _mechanisms = new();
        private readonly List<PartDef> _parts = new();
        private readonly List<ViewDef> _views = new();

        public RoomBuilder(string name, Box3 bounds)
        {
            _name = name;
            _bounds = bounds;
        }

        public int Item(string id, string name, string where, int examinable = -1)
        {
            _items.Add(new ItemDef(_items.Count, id, name, examinable, where));
            return _items.Count - 1;
        }

        public int Examinable(string id, string name, Vec3i clueFace, Vec3i clueUp, int reveals)
        {
            _examinables.Add(new ExaminableDef(_examinables.Count, id, name, clueFace, clueUp, reveals));
            return _examinables.Count - 1;
        }

        public int Mechanism(string id, string name, MechanismKind kind, int item, string direction, string clue)
        {
            _mechanisms.Add(new MechanismDef(_mechanisms.Count, id, name, kind, item, direction, clue));
            return _mechanisms.Count - 1;
        }

        public int WallView(string id, string name, CameraPose pose)
        {
            _views.Add(new ViewDef(_views.Count, id, name, ViewKind.Wall, pose, -1));
            return _views.Count - 1;
        }

        public int CloseUp(string id, string name, CameraPose pose, int reveals = -1)
        {
            _views.Add(new ViewDef(_views.Count, id, name, ViewKind.CloseUp, pose, reveals));
            return _views.Count - 1;
        }

        public int Body(string id, string name, int view, Box3 box, Presence presence = Presence.Always, int presenceOf = -1) =>
            Add(id, name, PartKind.Body, view, box, presence, presenceOf, -1, -1, -1, -1, -1);

        public int Slot(string id, string name, int view, Box3 box, int mechanism, int slot) =>
            Add(id, name, PartKind.Slot, view, box, Presence.Always, -1, -1, mechanism, slot, -1, -1);

        public int Keyhole(string id, string name, int view, Box3 box, int mechanism) =>
            Add(id, name, PartKind.Keyhole, view, box, Presence.Always, -1, -1, mechanism, -1, -1, -1);

        public int Socket(string id, string name, int view, Box3 box, int mechanism) =>
            Add(id, name, PartKind.Socket, view, box, Presence.Always, -1, -1, mechanism, -1, -1, -1);

        public int Pickup(string id, string name, int view, Box3 box, int item, int container) =>
            Add(id, name, PartKind.Pickup, view, box, Presence.UntilTaken, -1, container, -1, -1, item, -1);

        public int Examine(string id, string name, int view, Box3 box, int examinable, Presence presence = Presence.Always, int presenceOf = -1) =>
            Add(id, name, PartKind.Examinable, view, box, presence, presenceOf, -1, -1, -1, -1, examinable);

        public RoomDef Build(int exit) => new(_name, _bounds, new List<ItemDef>(_items), new List<ExaminableDef>(_examinables), new List<MechanismDef>(_mechanisms), new List<PartDef>(_parts), new List<ViewDef>(_views), exit);

        private int Add(string id, string name, PartKind kind, int view, Box3 box, Presence presence, int presenceOf, int container, int mechanism, int slot, int item, int examinable)
        {
            _parts.Add(new PartDef(_parts.Count, id, name, kind, view, box, presence, presenceOf, container, mechanism, slot, item, examinable));
            return _parts.Count - 1;
        }
    }
}
