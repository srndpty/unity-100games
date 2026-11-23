using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>警備員の判断の状態。</summary>
    public enum GuardMode
    {
        /// <summary>巡回点を順に歩き、各点で見回す。</summary>
        Patrol,

        /// <summary>怪しい物（見えたが未発見・足音・無線）の位置へ歩き、着いたら見回す。</summary>
        Investigate,

        /// <summary>発見した侵入者を、最後に見た（聞いた）位置へ走って追う。</summary>
        Chase,

        /// <summary>見失った位置の周りを巡る。</summary>
        Search,
    }

    /// <summary>警備員の歩き方。速さは設定と警戒レベルで決まる。</summary>
    public enum Pace
    {
        Patrol,
        Investigate,
        Chase,
        Search,
    }

    /// <summary>巡回点。そのマスに着いたら <see cref="DwellTicks"/> の間、<see cref="LookAngle"/> を中心に見回す。</summary>
    public readonly struct Waypoint
    {
        public Waypoint(Cell cell, int dwellTicks, int lookAngle)
        {
            if (dwellTicks < 1) { throw new ArgumentOutOfRangeException(nameof(dwellTicks), dwellTicks, "見回す Tick 数は 1 以上にしてください。"); }
            Cell = cell;
            DwellTicks = dwellTicks;
            LookAngle = Trig.Normalize(lookAngle);
        }

        public Cell Cell { get; }

        public int DwellTicks { get; }

        public int LookAngle { get; }
    }

    /// <summary>判断が体に出す指示。</summary>
    public readonly struct Order
    {
        private Order(bool look, Cell goal, Pace pace, int lookBase, int lookElapsed, Vec2i? facePoint)
        {
            IsLook = look;
            Goal = goal;
            Pace = pace;
            LookBase = lookBase;
            LookElapsed = lookElapsed;
            FacePoint = facePoint;
        }

        /// <summary>true ならその場で見回す。false なら <see cref="Goal"/> へ歩く。</summary>
        public bool IsLook { get; }

        public Cell Goal { get; }

        public Pace Pace { get; }

        /// <summary>見回しの中心の向き。</summary>
        public int LookBase { get; }

        /// <summary>見回しを始めてからの Tick 数（0 から）。</summary>
        public int LookElapsed { get; }

        /// <summary>見えている侵入者の位置。あれば体はそちらを向く。</summary>
        public Vec2i? FacePoint { get; }

        public static Order Walk(Cell goal, Pace pace, Vec2i? facePoint) => new(false, goal, pace, 0, 0, facePoint);

        public static Order Look(int lookBase, int elapsed, Vec2i? facePoint) => new(true, default, Pace.Patrol, lookBase, elapsed, facePoint);
    }

    /// <summary>
    /// 警備員の判断（巡回 → 調査 → 追跡 → 捜索 の FSM）と気づき値。入力はセンサが作った <see cref="Percept"/> と警戒レベルだけで、
    /// 地図も視線の計算も知らない（捜索で向かうマスだけを <see cref="ISearchArea"/> に選ばせる）。
    /// <list type="bullet">
    /// <item>見えた Tick は気づき値が距離に応じて増え、<see cref="WardConfig.AwarenessMax"/> に達したら追跡。見えない Tick は減る。</item>
    /// <item>足音・無線は調査にしかならない（気づき値を増やさない）。音だけで追跡に入らない。</item>
    /// <item>追跡中は見えた位置・聞こえた位置を追い、見失ったまま最後の位置に着いたら捜索。捜索が時間切れになったら巡回へ戻る。</item>
    /// </list>
    /// </summary>
    public sealed class GuardBrain
    {
        private readonly WardConfig _config;
        private readonly ReadOnlyCollection<Waypoint> _route;
        private readonly ISearchArea _area;
        private readonly SeededRandom _random;
        private int _lookLeft;

        public GuardBrain(WardConfig config, IReadOnlyList<Waypoint> route, ISearchArea area, SeededRandom random)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (route == null) { throw new ArgumentNullException(nameof(route)); }
            if (route.Count == 0) { throw new ArgumentException("巡回点がありません。", nameof(route)); }
            var copy = new Waypoint[route.Count];
            for (var i = 0; i < copy.Length; i++) { copy[i] = route[i]; }
            _route = Array.AsReadOnly(copy);
            _area = area ?? throw new ArgumentNullException(nameof(area));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            Goal = _route[0].Cell;
        }

        private GuardBrain(GuardBrain source)
        {
            _config = source._config;
            _route = source._route;
            _area = source._area;
            _random = source._random.Clone();
            _lookLeft = source._lookLeft;
            Mode = source.Mode;
            Awareness = source.Awareness;
            Goal = source.Goal;
            IsLooking = source.IsLooking;
            LookBase = source.LookBase;
            LookElapsed = source.LookElapsed;
            RouteIndex = source.RouteIndex;
            SearchLeft = source.SearchLeft;
            SearchCenter = source.SearchCenter;
            LastNoticed = source.LastNoticed;
            StartedChase = source.StartedChase;
        }

        public GuardMode Mode { get; private set; }

        /// <summary>気づき値（0〜<see cref="WardConfig.AwarenessMax"/>）。</summary>
        public int Awareness { get; private set; }

        /// <summary>今向かっている（向かい終えた）マス。</summary>
        public Cell Goal { get; private set; }

        public bool IsLooking { get; private set; }

        public int LookBase { get; private set; }

        public int LookElapsed { get; private set; }

        public int RouteIndex { get; private set; }

        public IReadOnlyList<Waypoint> Route => _route;

        /// <summary>捜索の残り Tick 数。</summary>
        public int SearchLeft { get; private set; }

        /// <summary>捜索の中心（見失ったマス）。</summary>
        public Cell SearchCenter { get; private set; }

        /// <summary>最後に見た（触れた）侵入者の位置。</summary>
        public Vec2i? LastNoticed { get; private set; }

        /// <summary>直前の <see cref="Think"/> で追跡に入ったか。</summary>
        public bool StartedChase { get; private set; }

        public GuardBrain Clone() => new(this);

        /// <summary>1 Tick の判断。<paramref name="visionRange"/> はその Tick の視程（気づき値の増え方に使う）。</summary>
        public Order Think(Percept percept, int visionRange)
        {
            if (visionRange <= 0) { throw new ArgumentOutOfRangeException(nameof(visionRange), visionRange, "視程は正の値にしてください。"); }
            StartedChase = false;
            UpdateAwareness(percept, visionRange);

            var noticed = percept.Seen ?? percept.Touched;
            if (noticed != null && (Mode == GuardMode.Chase || Awareness >= WardConfig.AwarenessMax))
            {
                StartedChase = Mode != GuardMode.Chase;
                Mode = GuardMode.Chase;
                SetGoal(Cell.Of(noticed.Value));
            }
            else if (Mode == GuardMode.Chase)
            {
                if (percept.Heard != null) { SetGoal(percept.Heard.Value); }
            }
            else
            {
                var lead = noticed != null ? Cell.Of(noticed.Value) : percept.Heard ?? percept.Radio;
                if (lead != null)
                {
                    Mode = GuardMode.Investigate;
                    SetGoal(lead.Value);
                }
            }

            return Mode switch
            {
                GuardMode.Patrol => Patrol(percept),
                GuardMode.Investigate => Investigate(percept),
                GuardMode.Chase => Chase(percept),
                GuardMode.Search => Search(percept),
                _ => throw new InvalidOperationException($"未定義の状態 {Mode} です。"),
            };
        }

        /// <summary>見えた Tick に増える気づき値。距離 0 で <see cref="WardConfig.AwarenessNearGain"/>、視程で <see cref="WardConfig.AwarenessFarGain"/>、間は線形（切り捨て）。</summary>
        public static int GainAt(WardConfig config, int distance, int visionRange)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var d = Math.Min(Math.Max(distance, 0), visionRange);
            return config.AwarenessFarGain + (int)((long)(config.AwarenessNearGain - config.AwarenessFarGain) * (visionRange - d) / visionRange);
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix((int)Mode);
            hash.Mix(Awareness);
            hash.Mix(Goal.Column);
            hash.Mix(Goal.Row);
            hash.Mix(IsLooking);
            hash.Mix(LookBase);
            hash.Mix(LookElapsed);
            hash.Mix(_lookLeft);
            hash.Mix(RouteIndex);
            hash.Mix(SearchLeft);
            hash.Mix(SearchCenter.Column);
            hash.Mix(SearchCenter.Row);
            hash.Mix(LastNoticed != null);
            hash.Mix(LastNoticed?.X ?? 0);
            hash.Mix(LastNoticed?.Y ?? 0);
            hash.Mix(StartedChase);
            hash.Mix(_random.State);
        }

        private void UpdateAwareness(Percept percept, int visionRange)
        {
            if (percept.Touched != null)
            {
                Awareness = WardConfig.AwarenessMax;
                LastNoticed = percept.Touched;
            }

            if (percept.Seen != null)
            {
                Awareness = Math.Min(WardConfig.AwarenessMax, Awareness + GainAt(_config, percept.SeenDistance, visionRange));
                LastNoticed = percept.Seen;
            }
            else if (percept.Touched == null)
            {
                Awareness = Math.Max(0, Awareness - _config.AwarenessDecay);
            }
        }

        private Order Patrol(Percept percept)
        {
            var waypoint = _route[RouteIndex];
            if (!IsLooking)
            {
                if (percept.StandingOn != waypoint.Cell)
                {
                    Goal = waypoint.Cell;
                    return Order.Walk(waypoint.Cell, Pace.Patrol, null);
                }

                StartLook(waypoint.LookAngle, waypoint.DwellTicks);
            }

            var order = Order.Look(LookBase, LookElapsed, null);
            if (AdvanceLook()) { RouteIndex = (RouteIndex + 1) % _route.Count; }
            return order;
        }

        private Order Investigate(Percept percept)
        {
            if (!IsLooking)
            {
                if (percept.StandingOn != Goal) { return Order.Walk(Goal, Pace.Investigate, percept.Seen); }
                StartLook(percept.Facing, _config.InvestigateLookTicks);
            }

            var order = Order.Look(LookBase, LookElapsed, percept.Seen);
            if (AdvanceLook()) { Mode = GuardMode.Patrol; }
            return order;
        }

        private Order Chase(Percept percept)
        {
            if (percept.Seen != null || percept.StandingOn != Goal) { return Order.Walk(Goal, Pace.Chase, percept.Seen); }

            // 見失ったまま最後の位置に着いた。
            Mode = GuardMode.Search;
            SearchCenter = Goal;
            SearchLeft = _config.SearchTicks;
            SetGoal(_area.Pick(SearchCenter, _random));
            return Search(percept);
        }

        private Order Search(Percept percept)
        {
            SearchLeft--;
            if (SearchLeft <= 0)
            {
                SearchLeft = 0;
                Mode = GuardMode.Patrol;
                IsLooking = false;
                return Patrol(percept);
            }

            if (!IsLooking)
            {
                if (percept.StandingOn != Goal) { return Order.Walk(Goal, Pace.Search, percept.Seen); }
                StartLook(percept.Facing, _config.SearchLookTicks);
            }

            var order = Order.Look(LookBase, LookElapsed, percept.Seen);
            if (AdvanceLook()) { SetGoal(_area.Pick(SearchCenter, _random)); }
            return order;
        }

        private void SetGoal(Cell goal)
        {
            Goal = goal;
            IsLooking = false;
        }

        private void StartLook(int lookBase, int ticks)
        {
            IsLooking = true;
            LookBase = lookBase;
            LookElapsed = 0;
            _lookLeft = ticks;
        }

        /// <summary>見回しを 1 Tick 進める。終わったら true。</summary>
        private bool AdvanceLook()
        {
            LookElapsed++;
            _lookLeft--;
            if (_lookLeft > 0) { return false; }
            IsLooking = false;
            return true;
        }
    }
}
