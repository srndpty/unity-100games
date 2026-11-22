using System;

namespace Game.Core
{
    /// <summary>
    /// 1 Tick の選手の操作。移動は -1・0・1、振りは押しているか（押し始めの Tick だけ振る）。
    /// 振った Tick の横の移動（左右）は、打つときの狙いにもなる。
    /// </summary>
    public readonly struct AthleteCommand : IEquatable<AthleteCommand>
    {
        public AthleteCommand(int moveX, int moveY, bool stroke, bool lob)
        {
            if (moveX < -1 || moveX > 1) { throw new ArgumentOutOfRangeException(nameof(moveX), moveX, "-1・0・1 のどれかにしてください。"); }
            if (moveY < -1 || moveY > 1) { throw new ArgumentOutOfRangeException(nameof(moveY), moveY, "-1・0・1 のどれかにしてください。"); }
            MoveX = moveX;
            MoveY = moveY;
            Stroke = stroke;
            Lob = lob;
        }

        /// <summary>画面の左 -1・右 1（x の向き）。</summary>
        public int MoveX { get; }

        /// <summary>手前 -1・奥 1（y の向き）。</summary>
        public int MoveY { get; }

        /// <summary>ストローク（サーブ）のキーを押している。</summary>
        public bool Stroke { get; }

        /// <summary>ロブのキーを押している。</summary>
        public bool Lob { get; }

        public static AthleteCommand Idle => default;

        public bool Equals(AthleteCommand other) => MoveX == other.MoveX && MoveY == other.MoveY && Stroke == other.Stroke && Lob == other.Lob;

        public override bool Equals(object? obj) => obj is AthleteCommand other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(MoveX, MoveY, Stroke, Lob);

        public static bool operator ==(AthleteCommand left, AthleteCommand right) => left.Equals(right);

        public static bool operator !=(AthleteCommand left, AthleteCommand right) => !left.Equals(right);
    }

    /// <summary>
    /// 選手。位置（地面）、振りの進み具合、直前に押していたキー。
    /// 振りは押し始めの Tick を 0 として <see cref="TennisConfig.ImpactTicks"/> Tick 目にラケットが球の位置を通り（打点）、
    /// <see cref="TennisConfig.SwingTicks"/> Tick で終わる。振りの途中は次の振りを始められず、移動も遅くなる。
    /// </summary>
    public sealed class Athlete
    {
        public Athlete(Side side, Vec3 position)
        {
            Side = side;
            Position = position;
        }

        public Side Side { get; }

        public Vec3 Position { get; private set; }

        /// <summary>振り始めてからの Tick 数。振っていなければ -1。</summary>
        public int SwingTick { get; private set; } = -1;

        public bool IsSwinging => SwingTick >= 0;

        public ShotKind SwingKind { get; private set; }

        /// <summary>振ったときの狙い（左右の入力、-1・0・1）。</summary>
        public int SwingAim { get; private set; }

        /// <summary>直前の Tick に押していたキー（押し始めの判定用）。</summary>
        public bool StrokeHeld { get; private set; }

        public bool LobHeld { get; private set; }

        /// <summary>最後に動いた向き（描画用、-1・0・1）。</summary>
        public int Facing { get; private set; }

        public Athlete Clone() => (Athlete)MemberwiseClone();

        /// <summary>ポイントの始めの位置に置き直す。振りは止め、押しているキーはそのまま（押し直すまで振らない）。</summary>
        public void Reset(Vec3 position)
        {
            Position = position;
            SwingTick = -1;
            Facing = 0;
        }

        /// <summary>押し始めを調べてキーの記録を更新する。返り値は押し始めたショットの種類（なければ null）。ストロークとロブを同時に押し始めたらストローク。</summary>
        public ShotKind? TakePress(AthleteCommand command)
        {
            var stroke = command.Stroke && !StrokeHeld;
            var lob = command.Lob && !LobHeld;
            StrokeHeld = command.Stroke;
            LobHeld = command.Lob;
            if (stroke) { return ShotKind.Stroke; }
            if (lob) { return ShotKind.Lob; }
            return null;
        }

        /// <summary>振り始める。振っている途中なら何もしない（false）。</summary>
        public bool StartSwing(ShotKind kind, int aim)
        {
            if (IsSwinging) { return false; }
            SwingTick = 0;
            SwingKind = kind;
            SwingAim = aim;
            return true;
        }

        /// <summary>振りを 1 Tick 進める。終わったら振っていない状態に戻る。</summary>
        public void AdvanceSwing(int swingTicks)
        {
            if (!IsSwinging) { return; }
            SwingTick++;
            if (SwingTick >= swingTicks) { SwingTick = -1; }
        }

        public void StopSwing() => SwingTick = -1;

        /// <summary>
        /// 8 方向に動く。斜めは各成分を 181/256 倍（最低 1）。自分の側の範囲（ネットから <see cref="TennisConfig.NetGap"/> 以上離れ、壁の線の内側）に収める。
        /// </summary>
        public void Move(int moveX, int moveY, int speed, TennisConfig config)
        {
            if (moveX == 0 && moveY == 0) { return; }
            var step = moveX != 0 && moveY != 0 ? Math.Max(1, speed * 181 / 256) : speed;
            var x = Position.X + (moveX * step);
            var y = Position.Y + (moveY * step);
            Position = Clamp(new Vec3(x, y, 0), config);
            if (moveX != 0) { Facing = moveX; }
        }

        /// <summary>自分の側で立てる範囲に収める。</summary>
        public Vec3 Clamp(Vec3 position, TennisConfig config)
        {
            var limitX = Court.FenceX - config.AthleteMargin;
            var x = Math.Max(-limitX, Math.Min(limitX, position.X));
            var depth = -position.Y * Court.Forward(Side);
            depth = Math.Max(config.NetGap, Math.Min(Court.FenceY - config.AthleteMargin, depth));
            return new Vec3(x, -depth * Court.Forward(Side), 0);
        }
    }
}
