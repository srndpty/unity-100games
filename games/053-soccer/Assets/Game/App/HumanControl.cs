using System;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>
    /// 人間の操作（青の 3 人のうち 1 人）。押しているキーと、押した行動を、操作している選手の命令にする。
    /// <para>
    /// 切替（手動のキー・自動）は、その Tick の命令を作って試合を進めた<b>後</b>に効く。だから同じ Tick に押した行動は切替の前の選手から出る（どちらの選手か一意）。
    /// 押している移動キーは、切り替えた次の Tick から新しい選手にそのまま効く（離し直さなくてよい）。前の選手はその Tick から CPU が動かす。
    /// 押した行動は 1 回の命令で使い切り、切り替えたら捨てる（前の選手の行動が新しい選手に移らない）。
    /// </para>
    /// 手動の切替は、操作していない 2 人のうち球に近い方へ。自動の切替：自分のパスの受け手へ／味方（CPU）が球を止めたらその選手へ／
    /// 相手が球を取ったら球に一番近い選手へ／青のキックオフなら蹴る人へ。
    /// </summary>
    public sealed class HumanControl
    {
        private int _held;
        private PlayerAction _pending;
        private int _pendingFor = -1;
        private bool _switchRequested;
        private int _passReceiver = -1;

        public HumanControl(bool autoSwitchOnDefense = true)
        {
            AutoSwitchOnDefense = autoSwitchOnDefense;
        }

        /// <summary>操作している選手（青：0〜2）。</summary>
        public int Controlled { get; private set; } = Match.PlayerOf(Team.Blue, 1);

        public bool AutoSwitchOnDefense { get; }

        /// <summary>切り替えた回数（効果音）。</summary>
        public int Switches { get; private set; }

        public HumanControl Clone() => (HumanControl)MemberwiseClone();

        /// <summary>キーの押下・解放（移動キーは物理キーごとのビット）。</summary>
        public void Apply(InputButton button, int keySlot, bool pressed, Match match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var bit = MoveBit(button, keySlot);
            if (bit != 0)
            {
                _held = pressed ? _held | bit : _held & ~bit;
                return;
            }

            if (!pressed) { return; }
            switch (button)
            {
                case InputButton.Pass:
                    Queue(match.Owner == Controlled || match.Taker == Controlled ? PlayerAction.Pass : PlayerAction.Tackle);
                    break;
                case InputButton.Shoot:
                    Queue(PlayerAction.Shoot);
                    break;
                case InputButton.Lob:
                    Queue(PlayerAction.Lob);
                    break;
                case InputButton.Switch:
                    _switchRequested = true;
                    break;
                default:
                    break;
            }
        }

        /// <summary>押しているキーを全て離したことにする（一時停止・フォーカス喪失）。押した行動・切替も捨てる。</summary>
        public void ReleaseAll()
        {
            _held = 0;
            _pending = PlayerAction.None;
            _pendingFor = -1;
            _switchRequested = false;
        }

        /// <summary>操作している選手の、この Tick の命令。押した行動はここで使い切る。</summary>
        public PlayerCommand Command(Match match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var move = MoveVector();
            var action = _pendingFor == Controlled ? _pending : PlayerAction.None;
            _pending = PlayerAction.None;
            _pendingFor = -1;
            _passReceiver = -1;
            var p = match.Players[Controlled];
            switch (action)
            {
                case PlayerAction.Pass:
                case PlayerAction.Lob:
                    {
                        var cone = move.IsZero ? p.Facing : move;
                        var option = PassEvaluator.Best(match, Controlled, safeOnly: false, cone, action)
                            ?? PassEvaluator.Best(match, Controlled, safeOnly: false, null, action);
                        if (!option.HasValue) { return new PlayerCommand(move); }
                        _passReceiver = option.Value.Receiver;
                        return new PlayerCommand(move, action, option.Value.Target);
                    }

                case PlayerAction.Shoot:
                    {
                        // 上下のキーでゴールの左右の隅を狙う（押していなければ中央）。
                        var aim = Pitch.GoalHalfWidth - 400;
                        var y = move.Y > 0 ? aim : move.Y < 0 ? -aim : 0;
                        return new PlayerCommand(move, PlayerAction.Shoot, new Vec2i(Pitch.AttackGoalX(Team.Blue), y));
                    }

                case PlayerAction.Tackle:
                    return new PlayerCommand(move, PlayerAction.Tackle);
                default:
                    return new PlayerCommand(move);
            }
        }

        /// <summary>試合の始め：キックオフを蹴る青の選手を操作する。</summary>
        public void Begin(Match match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            if (match.Taker >= 0 && match.Players[match.Taker].Team == Team.Blue) { Controlled = match.Taker; }
        }

        /// <summary>試合を 1 Tick 進めた後に呼ぶ。手動・自動の切替をここで当てる。</summary>
        public void AfterStep(Match match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var next = _switchRequested ? NearestToBall(match, Controlled) : -1;
            _switchRequested = false;
            foreach (var e in match.Events)
            {
                switch (e.Kind)
                {
                    case MatchEventKind.Kick when e.Player == Controlled && _passReceiver >= 0:
                        next = _passReceiver;
                        break;
                    case MatchEventKind.Capture when e.Team == Team.Blue:
                        next = e.Player;
                        break;
                    case MatchEventKind.Capture when e.Team == Team.Red && AutoSwitchOnDefense:
                    case MatchEventKind.Tackle when e.Team == Team.Red && AutoSwitchOnDefense:
                        next = NearestToBall(match, -1);
                        break;
                    case MatchEventKind.Kickoff when e.Team == Team.Blue:
                        next = e.Player;
                        break;
                    default:
                        break;
                }
            }

            if (next >= 0 && next != Controlled) { SwitchTo(next); }
        }

        private void SwitchTo(int player)
        {
            Controlled = player;
            Switches++;

            // 押している移動キーは新しい選手にそのまま効く。押した行動は前の選手のものなので捨てる。
            _pending = PlayerAction.None;
            _pendingFor = -1;
        }

        private void Queue(PlayerAction action)
        {
            _pending = action;
            _pendingFor = Controlled;
        }

        private Vec2i MoveVector()
        {
            var x = (Any(InputButton.Right) ? 1 : 0) - (Any(InputButton.Left) ? 1 : 0);
            var y = (Any(InputButton.Up) ? 1 : 0) - (Any(InputButton.Down) ? 1 : 0);
            if (x == 0 && y == 0) { return Vec2i.Zero; }
            return IntMath.Scale(new Vec2i(x, y), 1000);
        }

        private bool Any(InputButton direction) => (_held & (0xF << ((int)direction * 4))) != 0;

        /// <summary>移動キーのビット（方向ごとに 4 つの物理キー）。移動キーでなければ 0。</summary>
        private static int MoveBit(InputButton button, int slot) =>
            button is InputButton.Up or InputButton.Down or InputButton.Left or InputButton.Right ? 1 << (((int)button * 4) + (slot & 3)) : 0;

        /// <summary>青の選手のうち球に一番近い選手（<paramref name="except"/> を除く。同じなら番号の小さい方）。</summary>
        private static int NearestToBall(Match m, int except)
        {
            var best = -1;
            var bestDistance = long.MaxValue;
            for (int slot = 0; slot < Match.TeamSize; slot++)
            {
                var i = Match.PlayerOf(Team.Blue, slot);
                if (i == except) { continue; }
                var d = Vec2i.DistanceSquared(m.Players[i].Position, m.Ball.Position);
                if (d < bestDistance)
                {
                    best = i;
                    bestDistance = d;
                }
            }

            return best;
        }
    }
}
