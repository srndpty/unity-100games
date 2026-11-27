using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Game.Core.Geometry;
using Game.Core.Mechanisms;
using Game.Core.Rooms;
using TileMotion;

namespace Game.Core.Play
{
    /// <summary>
    /// 1 試合の世界。仕掛けの状態と値・持ち物・視点・手に取った物・ヒントの段階・操作の回数を 1 つに持ち、
    /// <see cref="Apply"/> だけで変える。受け付けない命令は理由を返し、何も変えない（ハッシュも操作の回数も）。
    /// 視点は命令の時点で切り替わる（カメラの移動の演出は App が描く）。
    /// </summary>
    public sealed class World
    {
        private readonly Room _room;
        private readonly RoomDef _def;
        private readonly Phase[] _phase;
        private readonly int[][] _values;
        private readonly bool[] _obtained;
        private readonly List<int> _inventory;
        private readonly ReadOnlyCollection<int> _inventoryView;
        private readonly List<EscapeEvent> _events = new();
        private readonly ReadOnlyCollection<EscapeEvent> _eventsView;

        public World(Room room)
        {
            _room = room ?? throw new ArgumentNullException(nameof(room));
            _def = room.Def;
            _phase = new Phase[_def.Mechanisms.Count];
            _values = new int[_def.Mechanisms.Count][];
            for (int m = 0; m < _values.Length; m++) { _values[m] = new int[room.Mechanisms[m].ValueCount]; }
            _obtained = new bool[_def.Items.Count];
            _inventory = new List<int>(_def.Items.Count);
            _inventoryView = _inventory.AsReadOnly();
            _eventsView = _events.AsReadOnly();
            Initialize();
        }

        private World(World other)
        {
            _room = other._room;
            _def = other._def;
            _phase = (Phase[])other._phase.Clone();
            _values = new int[other._values.Length][];
            for (int m = 0; m < _values.Length; m++) { _values[m] = (int[])other._values[m].Clone(); }
            _obtained = (bool[])other._obtained.Clone();
            _inventory = new List<int>(other._inventory);
            _inventoryView = _inventory.AsReadOnly();
            _eventsView = _events.AsReadOnly();
            View = other.View;
            Held = other.Held;
            Orientation = other.Orientation;
            HintKey = other.HintKey;
            HintLevel = other.HintLevel;
            HintsUsed = other.HintsUsed;
            Actions = other.Actions;
            Escaped = other.Escaped;
        }

        public Room Room => _room;
        public RoomDef Def => _def;

        /// <summary>今の視点。</summary>
        public int View { get; private set; }

        /// <summary>手に取って見ている物（手に取る物の番号、なければ -1）。</summary>
        public int Held { get; private set; }

        /// <summary>手に取った物の向き。</summary>
        public Orientation Orientation { get; private set; }

        /// <summary>前のヒントの対象（<see cref="HintStep.Key"/>、まだなければ -1）。</summary>
        public int HintKey { get; private set; }

        /// <summary>前のヒントの段階（0 方向付け・1 手がかり・2 答え）。</summary>
        public int HintLevel { get; private set; }

        public int HintsUsed { get; private set; }

        /// <summary>操作の回数（部品の操作・持ち物を使う・拾う・手に取る。視点の移動・回す・ヒントは数えない）。</summary>
        public int Actions { get; private set; }

        public bool Escaped { get; private set; }

        public bool OnWall => _def.Views[View].Kind == ViewKind.Wall;

        /// <summary>持ち物（手に入れた順）。</summary>
        public IReadOnlyList<int> Inventory => _inventoryView;

        /// <summary>最後の <see cref="Apply"/> で起きた出来事（拒否なら空）。</summary>
        public IReadOnlyList<EscapeEvent> Events => _eventsView;

        /// <summary>手に取った物の手がかりの面がこちらを向いているか（表示も判定もこれで決める）。</summary>
        public bool ClueFacing => Held >= 0 && Orientation.Faces(_def.Examinables[Held].ClueFace);

        public Phase PhaseOf(int mechanism) => _phase[mechanism];

        public bool IsSolved(int mechanism) => _phase[mechanism] == Phase.Solved;

        /// <summary>仕掛けの今の値（複製）。</summary>
        public int[] ValuesOf(int mechanism) => (int[])_values[mechanism].Clone();

        public int ValueOf(int mechanism, int index) => _values[mechanism][index];

        public bool Has(int item) => _inventory.Contains(item);

        public bool Obtained(int item) => _obtained[item];

        public int SolvedCount
        {
            get
            {
                int n = 0;
                foreach (var p in _phase)
                {
                    if (p == Phase.Solved) { n++; }
                }

                return n;
            }
        }

        public World Clone() => new(this);

        /// <summary>部品の箱が今あるか（遮る・当たる）。状態だけから決める。</summary>
        public bool IsPresent(int part)
        {
            var p = _def.Parts[part];
            return p.Presence switch
            {
                Presence.WhileClosed => _phase[p.PresenceOf] != Phase.Solved,
                Presence.WhileOpen => _phase[p.PresenceOf] == Phase.Solved,
                Presence.WhileInstalled => _phase[p.PresenceOf] != Phase.Locked,
                Presence.UntilTaken => !_obtained[p.Item],
                _ => true,
            };
        }

        /// <summary>部品に触れられるか（あって、入れ物があれば開いている）。命令の受け付けはこれで決め、ヒットテストと一致することをテストで確かめる。</summary>
        public bool IsReachable(int part)
        {
            var p = _def.Parts[part];
            return IsPresent(part) && (p.Container < 0 || _phase[p.Container] == Phase.Solved);
        }

        /// <summary>
        /// 今の視点で画面の点（<see cref="CameraPose"/> の整数座標）に当たる部品。今ある箱の中で最も近い面のもの。
        /// 同じ距離なら後に定義した部品（手前に付けた部品）。当たらなければ -1。
        /// </summary>
        public int HitTest(int sx, int sy)
        {
            if (!_def.Views[View].Pose.TryRay(sx, sy, out var ray)) { return -1; }
            return HitTest(ray);
        }

        /// <summary>レイに当たる部品（今ある箱の中で最も近いもの）。</summary>
        public int HitTest(Ray3 ray)
        {
            int best = -1;
            var bestT = Fraction.Zero;
            for (int i = 0; i < _def.Parts.Count; i++)
            {
                if (!IsPresent(i) || !Slab.Intersect(ray, _def.Parts[i].Box, out var t)) { continue; }
                if (best < 0 || t <= bestT)
                {
                    best = i;
                    bestT = t;
                }
            }

            return best;
        }

        /// <summary>
        /// 画面の点のクリックを命令にする（選んでいる持ち物があれば「使う」）。壁の視点では当たった部品の近景へ寄る。
        /// 近景ではその近景の部品だけを扱う。命令にならなければ <see cref="Command.None"/>。受け付けるかは <see cref="Check"/> で決まる。
        /// </summary>
        public Command CommandAt(int sx, int sy, int selectedItem)
        {
            if (Escaped || Held >= 0) { return Command.None; }
            var hit = HitTest(sx, sy);
            if (hit < 0) { return Command.None; }
            var part = _def.Parts[hit];
            if (OnWall)
            {
                // 壁の端に隣の壁の家具が見えることがある。寄るのは向いている壁の近景だけ。
                return _def.Views[part.View].Wall == _def.Views[View].Wall ? Command.Focus(part.View) : Command.None;
            }

            if (part.View != View) { return Command.None; }
            if (selectedItem >= 0) { return Command.UseItem(selectedItem, hit); }
            return part.Kind switch
            {
                PartKind.Pickup => Command.Take(hit),
                PartKind.Examinable => Command.Inspect(hit),
                PartKind.Body => Command.None,
                _ => Command.Operate(hit),
            };
        }

        /// <summary>命令を受け付けられるか。受け付けないときは理由を返す（何も変えない）。</summary>
        public Outcome Check(Command command)
        {
            switch (command.Kind)
            {
                case CommandKind.Reset:
                    return Outcome.Ok;
                case CommandKind.None:
                    return Outcome.Refuse(Refusal.NotAllowed);
            }

            var range = CheckRange(command);
            if (!range.Accepted) { return range; }
            if (Escaped) { return Outcome.Refuse(Refusal.Escaped); }
            if (Held >= 0)
            {
                return command.Kind == CommandKind.Rotate || command.Kind == CommandKind.Back || command.Kind == CommandKind.Hint
                    ? Outcome.Ok
                    : Outcome.Refuse(Refusal.Holding);
            }

            switch (command.Kind)
            {
                case CommandKind.Rotate:
                    return Outcome.Refuse(Refusal.NotHolding);
                case CommandKind.Hint:
                    return Outcome.Ok;
                case CommandKind.Back:
                    return OnWall ? Outcome.Refuse(Refusal.NothingToBack) : Outcome.Ok;
                case CommandKind.Turn:
                    return OnWall ? Outcome.Ok : Outcome.Refuse(Refusal.NotOnWall);
                case CommandKind.Focus:
                    {
                        var target = _def.Views[command.A];
                        if (!OnWall || target.Kind != ViewKind.CloseUp || target.Wall != _def.Views[View].Wall) { return Outcome.Refuse(Refusal.NotOnWall); }
                        return Outcome.Ok;
                    }

                case CommandKind.InspectItem:
                    if (!Has(command.A)) { return Outcome.Refuse(Refusal.NotHeld); }
                    return _def.Items[command.A].Examinable >= 0 ? Outcome.Ok : Outcome.Refuse(Refusal.NotAllowed);
            }

            // 部品への命令（Operate・UseItem・Take・Inspect）。
            var partIndex = command.Kind == CommandKind.UseItem ? command.B : command.A;
            var part = _def.Parts[partIndex];
            if (part.View != View) { return Outcome.Refuse(Refusal.NotInView); }
            if (!IsReachable(partIndex)) { return Outcome.Refuse(Refusal.NotHere); }
            switch (command.Kind)
            {
                case CommandKind.Take:
                    return part.Kind == PartKind.Pickup ? Outcome.Ok : Outcome.Refuse(Refusal.NotAllowed);
                case CommandKind.Inspect:
                    return part.Kind == PartKind.Examinable ? Outcome.Ok : Outcome.Refuse(Refusal.NotAllowed);
                case CommandKind.UseItem:
                    {
                        if (!Has(command.A)) { return Outcome.Refuse(Refusal.NotHeld); }
                        if (part.Kind != PartKind.Keyhole && part.Kind != PartKind.Socket) { return Outcome.Refuse(Refusal.WrongItem); }
                        var t = MechanismTable.Lookup(_phase[part.Mechanism], MechanismOp.Install);
                        if (t.Kind == TransitionKind.Reject) { return Outcome.Refuse(t.Refusal); }
                        return _def.Mechanisms[part.Mechanism].Item == command.A ? Outcome.Ok : Outcome.Refuse(Refusal.WrongItem);
                    }

                default:
                    {
                        if (part.Kind == PartKind.Slot)
                        {
                            var t = MechanismTable.Lookup(_phase[part.Mechanism], MechanismOp.Turn);
                            return t.Kind == TransitionKind.Reject ? Outcome.Refuse(t.Refusal) : Outcome.Ok;
                        }

                        if (part.Kind == PartKind.Keyhole || part.Kind == PartKind.Socket)
                        {
                            // 持ち物を選ばずに鍵穴・差し込み口を触っても、表のとおり拒否する（Locked なら「鍵がかかっている」）。
                            return Outcome.Refuse(MechanismTable.Lookup(_phase[part.Mechanism], MechanismOp.Probe).Refusal);
                        }

                        return Outcome.Refuse(Refusal.NotAllowed);
                    }
            }
        }

        /// <summary>命令を当てはめる。受け付けないときは理由を返し、出来事を空にするだけで何も変えない。</summary>
        public Outcome Apply(Command command)
        {
            _events.Clear();
            var check = Check(command);
            if (!check.Accepted) { return check; }
            switch (command.Kind)
            {
                case CommandKind.Reset:
                    Initialize();
                    _events.Add(new EscapeEvent(EventKind.Reset));
                    break;
                case CommandKind.Turn:
                    View = _def.WallViews[(_def.Views[View].Wall + command.A + 4) % 4];
                    _events.Add(new EscapeEvent(EventKind.Turned, View));
                    break;
                case CommandKind.Focus:
                    View = command.A;
                    _events.Add(new EscapeEvent(EventKind.Focused, View));
                    break;
                case CommandKind.Back:
                    if (Held >= 0)
                    {
                        _events.Add(new EscapeEvent(EventKind.PutDown, Held));
                        Held = -1;
                        Orientation = default;
                    }
                    else
                    {
                        View = _def.WallViews[_def.Views[View].Wall];
                        _events.Add(new EscapeEvent(EventKind.Backed, View));
                    }

                    break;
                case CommandKind.Rotate:
                    {
                        var was = ClueFacing;
                        Orientation = Orientation.Rotated(command.A, command.B);
                        _events.Add(new EscapeEvent(EventKind.Rotated, Held));
                        if (!was && ClueFacing) { _events.Add(new EscapeEvent(EventKind.ClueShown, Held)); }
                        break;
                    }

                case CommandKind.Hint:
                    GiveHint();
                    break;
                case CommandKind.InspectItem:
                    Actions++;
                    Hold(_def.Items[command.A].Examinable);
                    break;
                case CommandKind.Inspect:
                    Actions++;
                    Hold(_def.Parts[command.A].Examinable);
                    break;
                case CommandKind.Take:
                    {
                        var item = _def.Parts[command.A].Item;
                        if (_obtained[item]) { throw new InvalidOperationException($"持ち物 {_def.Items[item].Id} を二度拾います。"); }
                        Actions++;
                        _obtained[item] = true;
                        _inventory.Add(item);
                        _events.Add(new EscapeEvent(EventKind.ItemTaken, item));
                        break;
                    }

                case CommandKind.UseItem:
                    {
                        Actions++;
                        var m = _def.Parts[command.B].Mechanism;
                        _inventory.Remove(command.A);
                        _events.Add(new EscapeEvent(EventKind.ItemUsed, command.A));
                        _events.Add(new EscapeEvent(EventKind.Installed, m));
                        Enter(m, _room.Mechanisms[m].InstalledPhase);
                        break;
                    }

                default:
                    {
                        Actions++;
                        var part = _def.Parts[command.A];
                        var m = part.Mechanism;
                        var mechanism = _room.Mechanisms[m];
                        mechanism.Turn(_values[m], part.Slot, out var mistake);
                        _events.Add(new EscapeEvent(EventKind.Operated, command.A));
                        if (mistake) { _events.Add(new EscapeEvent(EventKind.Mistake, m)); }
                        if (mechanism.IsSolved(_values[m])) { Enter(m, Phase.Solved); }
                        break;
                    }
            }

            return Outcome.Ok;
        }

        /// <summary>今の状態での次の一手（ヒントの対象）。脱出の後は null。</summary>
        public HintStep? NextStep() => Escaped ? (HintStep?)null : HintEngine.Next(this);

        /// <summary>状態のハッシュ。部屋の指紋・シード・答え・仕掛けの値・持ち物の順序・視点・向き・ヒント・操作の回数まで混ぜる。</summary>
        public uint Hash()
        {
            var h = StateHash.Start();
            h.Mix(_def.Fingerprint);
            h.Mix(_room.Seed);
            for (int m = 0; m < _phase.Length; m++)
            {
                foreach (var a in _room.Mechanisms[m].Answer) { h.Mix(a); }
                h.Mix((int)_phase[m]);
                h.Mix(_values[m].Length);
                foreach (var v in _values[m]) { h.Mix(v); }
            }

            h.Mix(_inventory.Count);
            foreach (var i in _inventory) { h.Mix(i); }
            foreach (var o in _obtained) { h.Mix(o); }
            h.Mix(View);
            h.Mix(Held);
            h.Mix(Orientation.Yaw);
            h.Mix(Orientation.Pitch);
            h.Mix(HintKey);
            h.Mix(HintLevel);
            h.Mix(HintsUsed);
            h.Mix(Actions);
            h.Mix(Escaped);
            return h.Value;
        }

        /// <summary>
        /// 探索で比べる状態の鍵：仕掛けの状態・持ち物・拾った物・脱出。仕掛けの値（ダイヤルの桁など）は「解けたか」に縮め、
        /// 視点・手に取った物・ヒント・操作の回数は除く（探索の手は視点を移す命令を含めて当てはめる）。
        /// </summary>
        internal string AbstractKey()
        {
            var sb = new StringBuilder();
            foreach (var p in _phase) { sb.Append((int)p); }
            sb.Append('|');
            for (int i = 0; i < _obtained.Length; i++) { sb.Append(Has(i) ? '2' : _obtained[i] ? '1' : '0'); }
            sb.Append('|').Append(Escaped ? '1' : '0');
            return sb.ToString();
        }

        private Outcome CheckRange(Command command)
        {
            bool Part(int i) => i >= 0 && i < _def.Parts.Count;
            bool Item(int i) => i >= 0 && i < _def.Items.Count;
            var ok = command.Kind switch
            {
                CommandKind.Turn => command.A == 1 || command.A == -1,
                CommandKind.Focus => command.A >= 0 && command.A < _def.Views.Count,
                CommandKind.Operate => Part(command.A),
                CommandKind.Take => Part(command.A),
                CommandKind.Inspect => Part(command.A),
                CommandKind.UseItem => Item(command.A) && Part(command.B),
                CommandKind.InspectItem => Item(command.A),
                CommandKind.Rotate => command.A >= -1 && command.A <= 1 && command.B >= -1 && command.B <= 1 && (command.A != 0 || command.B != 0),
                _ => true,
            };
            return ok ? Outcome.Ok : Outcome.Refuse(Refusal.OutOfRange);
        }

        private void Initialize()
        {
            for (int m = 0; m < _phase.Length; m++)
            {
                var mechanism = _room.Mechanisms[m];
                _phase[m] = mechanism.InitialPhase;
                mechanism.Reset(_values[m]);
            }

            Array.Clear(_obtained, 0, _obtained.Length);
            _inventory.Clear();
            View = _def.WallViews[0];
            Held = -1;
            Orientation = default;
            HintKey = -1;
            HintLevel = 0;
            HintsUsed = 0;
            Actions = 0;
            Escaped = false;
        }

        private void Hold(int examinable)
        {
            Held = examinable;
            Orientation = default;
            _events.Add(new EscapeEvent(EventKind.Inspected, examinable));
            if (ClueFacing) { _events.Add(new EscapeEvent(EventKind.ClueShown, examinable)); }
        }

        private void Enter(int mechanism, Phase phase)
        {
            _phase[mechanism] = phase;
            if (phase != Phase.Solved) { return; }
            _events.Add(new EscapeEvent(EventKind.Solved, mechanism));
            if (mechanism == _def.Exit)
            {
                Escaped = true;
                _events.Add(new EscapeEvent(EventKind.Escaped, mechanism));
            }
        }

        private void GiveHint()
        {
            var step = HintEngine.Next(this);
            HintLevel = step.Key == HintKey ? Math.Min(HintLevel + 1, HintEngine.MaxLevel) : 0;
            HintKey = step.Key;
            HintsUsed++;
            _events.Add(new EscapeEvent(EventKind.Hint, HintLevel, HintEngine.Text(this, step, HintLevel)));
        }
    }
}
