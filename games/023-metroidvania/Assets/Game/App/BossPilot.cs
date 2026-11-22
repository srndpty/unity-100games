using System;
using Game.Core;
using TileMotion;

namespace Game.App
{
    /// <summary>
    /// ボスの部屋の操縦役（撮影・テスト用）。<see cref="Decision"/> Tick ごとに 6 通りの手（左右 × ジャンプ）を、
    /// 試合の複製（<see cref="Expedition.Clone"/>）で「その手 → 単純な方針」を <see cref="Horizon"/> Tick 続けて先読みし、
    /// 被弾せず・ボスを多く削る手を選ぶ。ショットは 2 Tick ごとに押し直す。乱数は使わない。
    /// 単純な方針：ボスとの間を 6〜11 タイルに保ちながらボスのほうを向いて撃ち、突進が近づくか弾が迫れば跳ぶ。段差の手前でも跳ぶ。
    /// </summary>
    public sealed class BossPilot : IPilot
    {
        public const int Decision = 4;
        public const int Horizon = 72;

        private static readonly (int Horizontal, bool Jump)[] _options =
        {
            (0, false), (1, false), (-1, false), (0, true), (1, true), (-1, true),
        };

        private (int Horizontal, bool Jump) _current;
        private int _left;
        private Expedition? _expedition;
        private int _entry = -1;

        public PlayerInput InputFor(Expedition expedition)
        {
            if (expedition == null) { throw new ArgumentNullException(nameof(expedition)); }

            // 判断の状態（選んだ手と残り Tick）は 1 回の部屋の滞在の中だけで使う。別の試合・入り直した部屋では捨てて選び直す
            // （同じ操縦役で再戦しても、前の試合の計画を持ち越さない）。
            if (!ReferenceEquals(expedition, _expedition) || expedition.RoomEntries != _entry)
            {
                _expedition = expedition;
                _entry = expedition.RoomEntries;
                _current = default;
                _left = 0;
            }

            if (expedition.Status != ExpeditionStatus.Playing) { return PlayerInput.None; }
            if (expedition.Run.Boss == null) { return Policy(expedition); }
            if (_left == 0)
            {
                _current = Choose(expedition);
                _left = Decision;
            }

            _left--;
            return new PlayerInput(_current.Horizontal, _current.Jump, Shoots(expedition));
        }

        private static bool Shoots(Expedition expedition) => expedition.RoomTicks % 2 == 0;

        private static (int, bool) Choose(Expedition source)
        {
            var best = _options[0];
            var bestScore = long.MinValue;
            foreach (var option in _options)
            {
                var score = Evaluate(source, option);
                if (score > bestScore)
                {
                    best = option;
                    bestScore = score;
                }
            }

            return best;
        }

        private static long Evaluate(Expedition source, (int Horizontal, bool Jump) option)
        {
            var e = source.Clone();
            var health = e.Player.Health;
            var boss = e.Run.Boss!.Health;
            var room = e.RoomIndex;
            for (var tick = 0; tick < Horizon; tick++)
            {
                var input = tick < Decision ? new PlayerInput(option.Horizontal, option.Jump, Shoots(e)) : Policy(e);
                e.Tick(input);
                if (e.IsWon) { return long.MaxValue - tick; }
                if (e.Status == ExpeditionStatus.Down) { return long.MinValue + tick + 1; }

                // 扉から出るとボスは初めからになるので、出る手は倒れるのに次いで悪い。
                if (e.RoomIndex != room) { return (long.MinValue / 2) + tick; }
            }

            var lost = health - e.Player.Health;
            var dealt = boss - (e.Run.Boss?.Health ?? 0);
            return (dealt * 1000L) - (lost * 100_000L) - Math.Abs(Distance(e) - (8 * Units.Tile));
        }

        /// <summary>先読みの後半に続ける単純な方針。</summary>
        private static PlayerInput Policy(Expedition e)
        {
            var player = e.Player;
            var boss = e.Run.Boss;
            if (boss == null || !boss.IsAlive) { return new PlayerInput(1, WallAhead(e, 1), false); }
            var toward = boss.Body.CenterX >= player.Body.CenterX ? 1 : -1;
            var distance = Distance(e);
            var horizontal = distance > 11 * Units.Tile ? toward : distance < 6 * Units.Tile ? -toward : 0;
            if (horizontal == 0 && player.Facing != toward) { horizontal = toward; }

            // 左右の端（扉・壁）へは下がらない。
            if (horizontal == -toward && (player.Body.Left < 3 * Units.Tile || player.Body.Right > (e.Run.Room.Map.Width - 3) * Units.Tile)) { horizontal = 0; }
            var jump = (boss.Phase == BossPhase.Charge && boss.Facing == -toward && distance < 7 * Units.Tile)
                || BulletNear(e)
                || (horizontal != 0 && WallAhead(e, horizontal));
            if (player.JumpHeld && player.Grounded) { jump = false; }
            return new PlayerInput(horizontal, jump, Shoots(e));
        }

        private static int Distance(Expedition e)
        {
            var boss = e.Run.Boss;
            return boss == null ? 0 : Math.Abs(boss.Body.CenterX - e.Player.Body.CenterX);
        }

        private static bool BulletNear(Expedition e)
        {
            var body = e.Player.Body;
            foreach (var bullet in e.Run.Bullets)
            {
                var dx = bullet.Body.CenterX - body.CenterX;
                if (Math.Abs(dx) < 4 * Units.Tile && Math.Sign(dx) == -Math.Sign(bullet.VelocityX) && bullet.Body.Bottom < body.Top + Units.Tile)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WallAhead(Expedition e, int direction)
        {
            var body = e.Player.Body;
            var column = Units.FloorDiv(direction > 0 ? body.Right + (Units.Tile / 2) : body.Left - (Units.Tile / 2), Units.Tile);
            return e.Run.Terrain.IsSolid(column, Units.FloorDiv(body.Bottom, Units.Tile));
        }
    }
}
