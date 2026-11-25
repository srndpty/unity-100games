using System;

namespace Game.Core
{
    /// <summary>
    /// 敵の考えと攻撃権。攻撃に入れるのは攻撃権を持つ敵だけで、雑魚は同時に MaxAttackers 体、ボスは別枠で 1 体。
    /// 権利は待った Tick の長い順（同じなら出現の早い順）に渡し、攻撃が終わる・怯む・倒れる・掴まれる・
    /// 時間切れ（TokenTimeout）で返して TokenCooldown の間は求めない。
    /// </summary>
    public sealed partial class Brawl
    {
        /// <summary>攻撃権を持つ雑魚の数（ボスを除く）。</summary>
        public int Attackers
        {
            get
            {
                int count = 0;
                for (int i = 1; i < _actors.Length; i++)
                {
                    var a = _actors[i];
                    if (a.Active && a.HasToken && a.Kind != ActorKind.Boss) { count++; }
                }

                return count;
            }
        }

        public int TokensGranted { get; private set; }

        private void UpdateTokens()
        {
            if (!AiEnabled) { return; }
            int grunts = 0;
            bool boss = false;
            for (int i = 1; i < _actors.Length; i++)
            {
                var a = _actors[i];
                if (!a.Active || !a.HasToken) { continue; }
                a.TokenHeld++;
                bool keep = !a.Defeated && (a.State == ActorState.Idle || a.State == ActorState.Walk || a.State == ActorState.Attack);
                if (!keep || (a.State != ActorState.Attack && a.TokenHeld > _config.TokenTimeout))
                {
                    ReleaseToken(a, true);
                    continue;
                }

                if (a.Kind == ActorKind.Boss) { boss = true; }
                else { grunts++; }
            }

            bool open = PlayerOpen();
            for (int i = 1; i < _actors.Length; i++)
            {
                var a = _actors[i];
                if (Requests(a, open)) { a.Waited++; }
                else if (!a.HasToken) { a.Waited = 0; }
            }

            while (grunts < _config.MaxAttackers)
            {
                var next = Longest(open, false);
                if (next == null) { break; }
                Grant(next);
                grunts++;
            }

            if (!boss)
            {
                var next = Longest(open, true);
                if (next != null) { Grant(next); }
            }
        }

        /// <summary>プレイヤーが攻撃を受けうる状態か（倒れている間は攻撃権を配らない）。</summary>
        private bool PlayerOpen()
        {
            var p = Player;
            return p.State != ActorState.Knockdown && p.State != ActorState.Wakeup && p.State != ActorState.Dead && Outcome == Outcome.Running;
        }

        private bool Requests(Actor a, bool open) =>
            open && a.Active && a.Slot != 0 && !a.HasToken && !a.Defeated && a.Cooldown == 0
            && (a.State == ActorState.Idle || a.State == ActorState.Walk)
            && a.X >= CameraX && a.X <= CameraX + _config.ViewWidth;

        private Actor? Longest(bool open, bool boss)
        {
            Actor? best = null;
            for (int i = 1; i < _actors.Length; i++)
            {
                var a = _actors[i];
                if (!Requests(a, open) || (a.Kind == ActorKind.Boss) != boss) { continue; }
                if (best == null || a.Waited > best.Waited || (a.Waited == best.Waited && a.Serial < best.Serial)) { best = a; }
            }

            return best;
        }

        private void Grant(Actor a)
        {
            a.HasToken = true;
            a.TokenHeld = 0;
            a.Waited = 0;
            a.Tokens++;
            TokensGranted++;
        }

        private void EnemyNeutral(Actor a)
        {
            if (!AiEnabled || Outcome != Outcome.Running)
            {
                SetState(a, ActorState.Idle);
                return;
            }

            var def = a.Def!;
            var p = Player;
            int dx = p.X - a.X;
            int dz = p.Z - a.Z;
            if (dx != 0) { a.Facing = dx > 0 ? 1 : -1; }
            int side = dx >= 0 ? -1 : 1;
            int adx = Math.Abs(dx);
            int targetX;
            int targetZ;
            if (a.HasToken && PlayerOpen())
            {
                a.Mind = EnemyMind.Engage;
                var opener = def.Find(def.Opener);
                int align = Math.Max(Px.One, opener.Box.Depth + p.Def!.Body.Depth - Px.One);
                bool aligned = Math.Abs(dz) < align;
                if (def.Far.Length > 0 && adx >= def.FarRange)
                {
                    if (aligned)
                    {
                        StartMove(a, def.Find(def.Far));
                        return;
                    }

                    targetX = a.X;
                    targetZ = p.Z;
                }
                else if (adx <= AttackRange(def, p) && aligned)
                {
                    var move = def.Close.Length > 0 && adx <= def.CloseRange && RandomRange(a, 0, 2) == 0 ? def.Find(def.Close) : opener;
                    StartMove(a, move);
                    return;
                }
                else
                {
                    targetX = p.X + (side * Math.Max(def.Reach * 3 / 4, Contact(def, p)));
                    targetZ = p.Z;
                }
            }
            else
            {
                a.Mind = a.X < CameraX || a.X > CameraX + _config.ViewWidth ? EnemyMind.Approach : EnemyMind.Wait;
                targetX = WaitSpot(p.X, side, def.Keep);
                targetZ = Math.Clamp(p.Z + a.WaitOffset, _config.ZMin, _config.ZMax);
            }

            int speed = def.Walk * (a.Enraged ? def.Rage : 100) / 100;
            int speedZ = def.WalkZ * (a.Enraged ? def.Rage : 100) / 100;
            int mx = Math.Clamp(targetX - a.X, -speed, speed);
            int mz = Math.Clamp(targetZ - a.Z, -speedZ, speedZ);
            if (Math.Abs(targetX - a.X) <= Px.One) { mx = 0; }
            if (Math.Abs(targetZ - a.Z) <= Px.One) { mz = 0; }
            if (mx == 0 && mz == 0)
            {
                SetState(a, ActorState.Idle);
                return;
            }

            SetState(a, ActorState.Walk);
            a.X += mx;
            a.Z += mz;
        }

        /// <summary>
        /// 攻撃権を待つ位置。今いる側に Keep だけ離れるが、画面の外になるなら反対側に取る（画面の外の敵は権利を求めないので、
        /// 待ち位置が外にあると権利が回らない）。どちらも外なら画面の端に寄せる。
        /// </summary>
        private int WaitSpot(int playerX, int side, int keep)
        {
            int min = CameraX + _config.EdgeMargin;
            int max = CameraX + _config.ViewWidth - _config.EdgeMargin;
            int spot = playerX + (side * keep);
            if (spot >= min && spot <= max) { return spot; }
            int other = playerX - (side * keep);
            if (other >= min && other <= max) { return other; }
            return Math.Clamp(spot, min, max);
        }

        /// <summary>押し合いで止まる横の距離（奥行きがそろっていればこれより近づけない）。</summary>
        private static int Contact(ActorDef def, Actor player) => (def.PushWidth + player.Def!.PushWidth) / 2;

        /// <summary>
        /// 攻撃を始める横の距離。Reach が押し合いで止まる距離より短いと、動かないプレイヤーには永久に届かないので、
        /// 押し合いの距離 ＋ 2 px までは始める（技の攻撃箱はそこまで届く長さにしてある）。
        /// </summary>
        private static int AttackRange(ActorDef def, Actor player) => Math.Max(def.Reach, Contact(def, player) + (2 * Px.One));

        private void FinishEnemyAttack(Actor a) => ReleaseToken(a, true);

        private void ReleaseToken(Actor a, bool cooldown)
        {
            if (!a.HasToken) { return; }
            a.HasToken = false;
            a.TokenHeld = 0;
            if (cooldown) { a.Cooldown = _config.TokenCooldown; }
        }
    }
}
