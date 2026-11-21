using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    public enum RunStatus
    {
        Playing,
        Cleared,
        Missed,
    }

    public enum MissReason
    {
        None,
        Enemy,
        Pit,
        TimeUp,
    }

    [Flags]
    public enum RunEventKind
    {
        None = 0,
        Jumped = 1 << 0,
        Stomped = 1 << 1,
        Cleared = 1 << 2,
        Missed = 1 << 3,
    }

    /// <summary>前回取り出してから起きた出来事。</summary>
    public readonly struct RunEvents
    {
        public RunEvents(RunEventKind kinds, int stomps)
        {
            Kinds = kinds;
            Stomps = stomps;
        }

        public RunEventKind Kinds { get; }

        /// <summary>踏みつけた敵の数。</summary>
        public int Stomps { get; }

        public bool Has(RunEventKind kind) => (Kinds & kind) == kind && kind != RunEventKind.None;
    }

    /// <summary>
    /// 1 面の 1 回の挑戦。ミスかゴールで終わり、終わった後の Tick と入力は受け付けない（状態を変えない）。
    /// 1 Tick の順序は「自機の移動（横→縦→接地）→ ゴール判定 → 敵の移動 → 接触判定 → 穴 → 時間切れ」。
    /// 乱数は使わないので、面と入力の列だけで結果が決まる。
    /// </summary>
    public sealed class LevelRun
    {
        private readonly PlatformerConfig _config;
        private readonly Enemy[] _enemies;
        private readonly Box[] _enemyStarts;
        private RunEventKind _events;
        private int _eventStomps;

        private LevelRun(LevelRun source)
        {
            _config = source._config;
            Level = source.Level;
            Player = source.Player.Clone();
            _enemies = new Enemy[source._enemies.Length];
            for (var i = 0; i < _enemies.Length; i++)
            {
                _enemies[i] = source._enemies[i].Clone();
            }

            _enemyStarts = new Box[_enemies.Length];
            Enemies = Array.AsReadOnly(_enemies);
            Status = source.Status;
            MissReason = source.MissReason;
            Ticks = source.Ticks;
            Stomps = source.Stomps;
        }

        public LevelRun(PlatformerConfig config, Level level)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            Level = level ?? throw new ArgumentNullException(nameof(level));
            Player = new Player(_config, Level.PlaceOnTile(level.StartColumn, level.StartRow, Player.Width, Player.Height));
            _enemies = new Enemy[level.Enemies.Count];
            for (var i = 0; i < _enemies.Length; i++)
            {
                var spawn = level.Enemies[i];
                var size = Enemy.SizeOf(spawn.Kind);
                _enemies[i] = new Enemy(_config, spawn.Kind, Level.PlaceOnTile(spawn.Column, spawn.Row, size.Width, size.Height));
            }

            _enemyStarts = new Box[_enemies.Length];
            Enemies = Array.AsReadOnly(_enemies);
        }

        public Level Level { get; }

        public TileMap Map => Level.Map;

        public Player Player { get; }

        public ReadOnlyCollection<Enemy> Enemies { get; }

        public RunStatus Status { get; private set; }

        public MissReason MissReason { get; private set; }

        public bool IsOver => Status != RunStatus.Playing;

        public long Ticks { get; private set; }

        public int RemainingTicks => (int)Math.Max(0, _config.TimeLimitTicks - Ticks);

        /// <summary>残り時間（秒、切り上げ）。表示用。</summary>
        public int RemainingSeconds => (RemainingTicks + Units.TickRate - 1) / Units.TickRate;

        /// <summary>この挑戦で踏みつけた敵の数。</summary>
        public int Stomps { get; private set; }

        /// <summary>
        /// 同じ状態の複製。元とは独立に進められる（自動操縦の先読み用）。取り出していない出来事は複製しない。
        /// 面のデータと設定は変更されないので共有する。
        /// </summary>
        public LevelRun Clone() => new(this);

        /// <summary>1 Tick 進める。終わった後は何もせず false。</summary>
        public bool Tick(PlayerInput input)
        {
            if (IsOver)
            {
                return false;
            }

            Ticks++;
            var playerStart = Player.Body;
            if (Player.Step(Map, input))
            {
                _events |= RunEventKind.Jumped;
            }

            if (Map.Touches(Player.Body, TileKind.Goal))
            {
                Status = RunStatus.Cleared;
                _events |= RunEventKind.Cleared;
                return true;
            }

            for (var i = 0; i < _enemies.Length; i++)
            {
                _enemyStarts[i] = _enemies[i].Body;
                _enemies[i].Step(Map, Player.Body, Ticks);
            }

            ResolveContacts(playerStart, input.Jump);
            if (IsOver)
            {
                return true;
            }

            if (Player.Body.Top <= 0)
            {
                Miss(MissReason.Pit);
            }
            else if (Ticks >= _config.TimeLimitTicks)
            {
                Miss(MissReason.TimeUp);
            }

            return true;
        }

        public RunEvents TakeEvents()
        {
            var events = new RunEvents(_events, _eventStomps);
            _events = RunEventKind.None;
            _eventStomps = 0;
            return events;
        }

        /// <summary>状態のハッシュ（FNV-1a）。Mono と .NET で同じ結果になることの確認用。</summary>
        public uint Hash()
        {
            var hash = 2166136261u;
            void Mix(long value)
            {
                unchecked
                {
                    hash = (hash ^ (uint)value) * 16777619u;
                    hash = (hash ^ (uint)(value >> 32)) * 16777619u;
                }
            }

            Mix(Ticks);
            Mix((int)Status);
            Mix((int)MissReason);
            Mix(Stomps);
            Mix(Player.Body.X);
            Mix(Player.Body.Y);
            Mix(Player.VelocityX);
            Mix(Player.VelocityY);
            foreach (var enemy in _enemies)
            {
                Mix((int)enemy.State);
                Mix(enemy.Body.X);
                Mix(enemy.Body.Y);
                Mix(enemy.Facing);
            }

            return hash;
        }

        /// <summary>
        /// 最初に触れた時刻の接触だけを扱う。その時刻に横衝突が 1 つでもあればミス、踏みつけだけなら全て倒して跳ねる。
        /// 踏みつけより後の接触は、跳ね返りで軌跡が変わるので捨てる（まだ重なっていれば次の Tick で判定される）。
        /// </summary>
        private void ResolveContacts(Box playerStart, bool jumpHeld)
        {
            var first = Contact.None;
            var hurtFirst = false;
            List<Enemy>? stomped = null;
            for (var i = 0; i < _enemies.Length; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.IsAlive)
                {
                    continue;
                }

                var contact = EnemyContact.Classify(playerStart, Player.Body, _enemyStarts[i], enemy.Body, enemy.Stompable);
                if (contact.Kind == ContactKind.None)
                {
                    continue;
                }

                var order = first.Kind == ContactKind.None ? -1 : contact.CompareTime(first);
                if (order < 0)
                {
                    first = contact;
                    hurtFirst = false;
                    stomped?.Clear();
                }

                if (order <= 0)
                {
                    if (contact.Kind == ContactKind.Hurt)
                    {
                        hurtFirst = true;
                    }
                    else
                    {
                        stomped ??= new List<Enemy>();
                        stomped.Add(enemy);
                    }
                }
            }

            if (first.Kind == ContactKind.None)
            {
                return;
            }

            if (hurtFirst)
            {
                Miss(MissReason.Enemy);
                return;
            }

            foreach (var enemy in stomped!)
            {
                enemy.Stomp(Ticks);
            }

            Stomps += stomped.Count;
            _eventStomps += stomped.Count;
            _events |= RunEventKind.Stomped;
            Player.Bounce(jumpHeld);
        }

        private void Miss(MissReason reason)
        {
            Status = RunStatus.Missed;
            MissReason = reason;
            _events |= RunEventKind.Missed;
        }
    }
}
