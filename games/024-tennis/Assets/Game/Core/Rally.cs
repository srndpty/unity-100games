using System;

namespace Game.Core
{
    public enum RallyPhase
    {
        /// <summary>サーバーが球を持って待っている。</summary>
        Ready,

        /// <summary>サーブのトスが上がっている。</summary>
        Toss,

        /// <summary>打ち合いの球が飛んでいる（サーブを含む）。</summary>
        InPlay,

        /// <summary>ポイントが決まった。</summary>
        Over,
    }

    /// <summary>判定（審判のコール）。</summary>
    public enum Verdict
    {
        None,

        /// <summary>サーブがサービスボックスに入らなかった（1 本目）。</summary>
        Fault,

        /// <summary>ネットにかかった。打った側の失点。</summary>
        Net,

        /// <summary>相手コートの外に落ちた。打った側の失点。</summary>
        Out,

        /// <summary>相手コートで 2 回弾んだ。受けた側の失点。</summary>
        DoubleBounce,

        /// <summary>1 回弾んだ球が壁の線を越えた（返せなかった）。受けた側の失点。</summary>
        Unreturned,

        /// <summary>サーブが 2 本続けて入らなかった。サーバーの失点。</summary>
        DoubleFault,
    }

    /// <summary>
    /// 1 ポイントの状態機械。球の出来事（ネット・バウンド・壁の線）と打球を受けて、続くか・フォルトか・ポイントが決まったかを返す。
    /// <list type="bullet">
    /// <item>ネット：打った側の失点。サーブならフォルト。</item>
    /// <item>最初のバウンド：サーブは対角のサービスボックスの中（ライン上を含む）でなければフォルト。打ち合いは相手のシングルスのコートの中でなければアウト（打った側の失点）。打った側のコートに落ちても打った側の失点（アウト）。</item>
    /// <item>2 回目のバウンド、または 1 回弾んだ後に壁の線を越える：受けた側の失点。</item>
    /// <item>フォルト 2 本でダブルフォルト（サーバーの失点）。</item>
    /// </list>
    /// </summary>
    public sealed class Rally
    {
        public Rally(Side server, ServeCourt court)
        {
            Server = server;
            ServeCourt = court;
            ServeNumber = 1;
            LastHitter = server;
        }

        public Side Server { get; }

        public ServeCourt ServeCourt { get; }

        public RallyPhase Phase { get; private set; }

        /// <summary>何本目のサーブか（1 か 2）。</summary>
        public int ServeNumber { get; private set; }

        public Side LastHitter { get; private set; }

        /// <summary>最後に打ってから、受ける側のコートで弾んだ回数（0 か 1）。</summary>
        public int Bounces { get; private set; }

        /// <summary>サーブがまだバウンドしていない。</summary>
        public bool ServeInFlight { get; private set; }

        /// <summary>トスを上げてからの Tick 数。</summary>
        public int TossTick { get; private set; }

        /// <summary>このトスで既に振った（空振りを含む）。</summary>
        public bool TossSwung { get; private set; }

        /// <summary>このポイントで打った回数（サーブを含む）。</summary>
        public int Shots { get; private set; }

        public Side? Winner { get; private set; }

        public Verdict LastVerdict { get; private set; }

        public Side Receiver => Court.Other(Server);

        public Rally Clone() => (Rally)MemberwiseClone();

        /// <summary><paramref name="lastHitter"/> が今打った打ち合いの球の場面（テスト・撮影用）。</summary>
        public static Rally Staged(Side server, ServeCourt court, Side lastHitter) => new(server, court)
        {
            Phase = RallyPhase.InPlay,
            LastHitter = lastHitter,
            Shots = 2,
        };

        /// <summary>球が向かっている側（最後に打った人の相手）。打ち合いでなければ null。</summary>
        public Side? Incoming => Phase == RallyPhase.InPlay ? Court.Other(LastHitter) : (Side?)null;

        public void Toss()
        {
            Require(RallyPhase.Ready);
            Phase = RallyPhase.Toss;
            TossTick = 0;
            TossSwung = false;
            LastVerdict = Verdict.None;
        }

        public void AdvanceToss()
        {
            Require(RallyPhase.Toss);
            TossTick++;
        }

        /// <summary>トスを打たずに落とした。フォルトにせず、持ち直す。</summary>
        public void DropToss()
        {
            Require(RallyPhase.Toss);
            Phase = RallyPhase.Ready;
        }

        /// <summary>トスを振った。当たればサーブとして飛ぶ。</summary>
        public void SwingAtToss(bool hit)
        {
            Require(RallyPhase.Toss);
            if (TossSwung) { throw new InvalidOperationException("このトスは既に振りました。"); }
            TossSwung = true;
            if (!hit) { return; }
            Phase = RallyPhase.InPlay;
            ServeInFlight = true;
            LastHitter = Server;
            Bounces = 0;
            Shots++;
        }

        /// <summary>打ち合いの球を打った（<paramref name="hitter"/> は球が向かっていた側）。</summary>
        public void Hit(Side hitter)
        {
            Require(RallyPhase.InPlay);
            if (hitter == LastHitter) { throw new InvalidOperationException("同じ人が続けて打つことはできません。"); }
            if (ServeInFlight) { throw new InvalidOperationException("サーブはバウンドする前に打てません。"); }
            LastHitter = hitter;
            Bounces = 0;
            Shots++;
        }

        /// <summary>打ち合いの球の出来事を判定する。</summary>
        public Verdict OnBall(BallEvent ballEvent)
        {
            Require(RallyPhase.InPlay);
            var receiver = Court.Other(LastHitter);
            switch (ballEvent.Kind)
            {
                case BallEventKind.None:
                    return Verdict.None;
                case BallEventKind.Net:
                    return ServeInFlight ? Fault() : End(receiver, Verdict.Net);
                case BallEventKind.Bounce:
                    var p = ballEvent.Point;
                    if (ServeInFlight)
                    {
                        if (!Court.InServiceBox(p.X, p.Y, Server, ServeCourt)) { return Fault(); }
                        ServeInFlight = false;
                        Bounces = 1;
                        return Verdict.None;
                    }

                    if (Bounces >= 1) { return End(LastHitter, Verdict.DoubleBounce); }
                    if (!Court.InSingles(p.X, p.Y, receiver)) { return End(receiver, Verdict.Out); }
                    Bounces = 1;
                    return Verdict.None;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ballEvent), ballEvent.Kind, "未定義の出来事です。");
            }
        }

        /// <summary>1 回弾んだ球が壁の線を越えたら、受けた側の失点。</summary>
        public Verdict OnPosition(Vec3 position)
        {
            Require(RallyPhase.InPlay);
            if (Bounces < 1) { return Verdict.None; }
            if (Math.Abs(position.Y) <= Court.FenceY && Math.Abs(position.X) <= Court.FenceX) { return Verdict.None; }
            return End(LastHitter, Verdict.Unreturned);
        }

        private Verdict Fault()
        {
            ServeInFlight = false;
            if (ServeNumber >= 2) { return End(Receiver, Verdict.DoubleFault); }
            ServeNumber = 2;
            Phase = RallyPhase.Ready;
            LastHitter = Server;
            Bounces = 0;
            LastVerdict = Verdict.Fault;
            return Verdict.Fault;
        }

        private Verdict End(Side winner, Verdict verdict)
        {
            Phase = RallyPhase.Over;
            Winner = winner;
            LastVerdict = verdict;
            return verdict;
        }

        private void Require(RallyPhase phase)
        {
            if (Phase != phase) { throw new InvalidOperationException($"{phase} ではありません（今は {Phase}）。"); }
        }
    }
}
