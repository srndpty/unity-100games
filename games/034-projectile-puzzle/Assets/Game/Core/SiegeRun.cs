using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    public enum RunPhase
    {
        /// <summary>次の弾を構えられる。</summary>
        Aiming,

        /// <summary>弾を撃ってから静止するまで。</summary>
        Flying,

        Won,

        Lost,
    }

    /// <summary>試合の出来事（描画・音に使う）。</summary>
    [Flags]
    public enum RunEvents
    {
        None = 0,
        Launched = 1,
        Hit = 1 << 1,
        Broke = 1 << 2,
        Detonated = 1 << 3,

        /// <summary>静止して次の弾を構えられるようになった。</summary>
        Ready = 1 << 4,
        Won = 1 << 5,
        Lost = 1 << 6,
    }

    /// <summary>
    /// 1 面の試合。物理（剛体の運動・衝突・接合の力）は Unity の Physics2D が進め、ここは Health・接合の台帳・得点・静止・勝敗を持つ。
    /// <para>
    /// 1 Tick の順序：<see cref="BeginTick"/> がこの Tick の命令（射出・起爆・弾の消去）を返す → Unity が命令を反映して物理を 1 Tick 進め、
    /// <see cref="TickReport"/> を書く → <see cref="EndTick"/> がダメージ・破壊・静止・勝敗を決め、消す部材と接合を
    /// <see cref="BrokenThisTick"/>・<see cref="JointsToRemove"/> に置く。操作（<see cref="Launch"/>・<see cref="Detonate"/>）は Tick の境目でだけ効く。
    /// </para>
    /// </summary>
    public sealed class SiegeRun
    {
        public const int TickRate = 60;

        private readonly SiegeConfig _config;
        private readonly int[] _health;
        private readonly bool[] _broken;
        private readonly bool[] _jointBroken;
        private readonly List<int> _brokenThisTick = new();
        private readonly List<int> _jointsToRemove = new();
        private readonly uint _fingerprint;
        private V2? _pendingLaunch;
        private bool _pendingDetonate;
        private bool _launchedOnce;
        private int _pieceScore;
        private RunEvents _events;

        public SiegeRun(SiegeConfig config, Stage stage)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            Stage = stage ?? throw new ArgumentNullException(nameof(stage));
            _config = config.Copy();
            _config.Validate();
            stage.Validate(_config);
            _health = new int[stage.Pieces.Count];
            _broken = new bool[stage.Pieces.Count];
            _jointBroken = new bool[stage.Joints.Count];
            for (var i = 0; i < _health.Length; i++) { _health[i] = _config.Spec(stage.Pieces[i].Material).Health; }
            TargetsLeft = stage.TargetCount;
            BrokenThisTick = _brokenThisTick.AsReadOnly();
            JointsToRemove = _jointsToRemove.AsReadOnly();
            _fingerprint = Fingerprint(_config, stage);
        }

        public Stage Stage { get; }

        public RunPhase Phase { get; private set; }

        /// <summary>面を始めてから進めた Tick 数。</summary>
        public int Ticks { get; private set; }

        /// <summary>撃った弾の数（次に撃つ弾の番号）。</summary>
        public int ShotsUsed { get; private set; }

        public int ShotsLeft => Stage.Shots.Count - ShotsUsed;

        /// <summary>次に構える弾（残っていなければ null）。</summary>
        public ShotKind? NextShot => ShotsUsed < Stage.Shots.Count ? Stage.Shots[ShotsUsed] : null;

        /// <summary>今飛んでいる（最後に撃った）弾。</summary>
        public ShotKind? FlyingShot => Phase == RunPhase.Flying && ShotsUsed > 0 ? Stage.Shots[ShotsUsed - 1] : null;

        /// <summary>撃った弾がまだ盤にあるか（起爆・消去・盤外で false）。</summary>
        public bool ShotAlive { get; private set; }

        /// <summary>今の弾を撃ってからの Tick 数。</summary>
        public int TicksSinceLaunch { get; private set; }

        /// <summary>爆発の弾が最初に触れてからの Tick 数（触れていなければ -1）。</summary>
        public int FuseElapsed { get; private set; } = -1;

        public bool Detonated { get; private set; }

        /// <summary>止まっている状態が続いている Tick 数。</summary>
        public int QuietTicks { get; private set; }

        public int TargetsLeft { get; private set; }

        public int BrokenCount { get; private set; }

        /// <summary>得点：壊した部材の得点の和。勝ったら残った弾の得点を足す。</summary>
        public int Score => _pieceScore + (Phase == RunPhase.Won ? ShotsLeft * _config.UnusedShotBonus : 0);

        /// <summary>星：勝ったら 1 + 残った弾の数（最大 3）、それ以外は 0。</summary>
        public int Stars => Phase == RunPhase.Won ? Math.Min(3, 1 + ShotsLeft) : 0;

        public bool IsOver => Phase == RunPhase.Won || Phase == RunPhase.Lost;

        /// <summary>直前の <see cref="EndTick"/> で壊れた部材の番号（Unity はこれだけを消す）。</summary>
        public ReadOnlyCollection<int> BrokenThisTick { get; }

        /// <summary>直前の <see cref="EndTick"/> で、壊れた部材に付いていたため外す接合の番号。</summary>
        public ReadOnlyCollection<int> JointsToRemove { get; }

        /// <summary>
        /// 直前の <see cref="EndTick"/> で静止したため、盤に残っている弾を消すか（Unity はすぐに消す）。
        /// 静止で勝敗が決まると次の Tick がないので、次の命令ではなくこの Tick の結果として返す。
        /// </summary>
        public bool ShotRemovedThisTick { get; private set; }

        public int Health(int piece) => _health[CheckPiece(piece)];

        public bool IsBroken(int piece) => _broken[CheckPiece(piece)];

        public bool IsJointBroken(int joint)
        {
            if ((uint)joint >= (uint)_jointBroken.Length) { throw new ArgumentOutOfRangeException(nameof(joint), joint, "接合の番号が範囲外です。"); }
            return _jointBroken[joint];
        }

        /// <summary>
        /// 引き <paramref name="pull"/> で次の弾を撃つ（次の Tick の境目で効く）。構えられない・取り消しの引き・既に撃つ命令があれば false。
        /// </summary>
        public bool Launch(V2 pull)
        {
            if (Phase != RunPhase.Aiming || _pendingLaunch != null || ShotsLeft <= 0) { return false; }
            var velocity = Core.Launch.Velocity(pull, _config);
            if (velocity == null) { return false; }
            _pendingLaunch = velocity;
            return true;
        }

        /// <summary>飛んでいる爆発の弾を起爆する（次の Tick の境目で効く）。起爆できなければ false。1 発で 1 回だけ。</summary>
        public bool Detonate()
        {
            if (!CanDetonate || _pendingDetonate) { return false; }
            _pendingDetonate = true;
            return true;
        }

        /// <summary>飛んでいる爆発の弾をまだ起爆していないか。</summary>
        public bool CanDetonate => FlyingShot == ShotKind.Bomb && ShotAlive && !Detonated;

        /// <summary>この Tick の命令を決める。終わった後は空の命令。</summary>
        public TickCommands BeginTick()
        {
            if (IsOver) { return default; }
            V2? launch = null;
            var kind = ShotKind.Normal;
            var detonate = false;
            var remove = false;

            if (_pendingLaunch is { } velocity && Phase == RunPhase.Aiming)
            {
                launch = velocity;
                kind = Stage.Shots[ShotsUsed];
                ShotsUsed++;
                Phase = RunPhase.Flying;
                ShotAlive = true;
                TicksSinceLaunch = 0;
                FuseElapsed = -1;
                Detonated = false;
                QuietTicks = 0;
                _launchedOnce = true;
                _events |= RunEvents.Launched;
            }
            else if (Phase == RunPhase.Flying && ShotAlive)
            {
                var fuse = FlyingShot == ShotKind.Bomb && FuseElapsed >= _config.FuseTicks;
                if ((_pendingDetonate || fuse) && CanDetonate)
                {
                    detonate = true;
                    Detonated = true;
                    ShotAlive = false;
                    _events |= RunEvents.Detonated;
                }
                else if (TicksSinceLaunch >= _config.ShotLifeTicks)
                {
                    remove = true;
                    ShotAlive = false;
                }
            }

            _pendingLaunch = null;
            _pendingDetonate = false;
            return new TickCommands(launch, kind, detonate, remove);
        }

        /// <summary>物理を 1 Tick 進めた結果を反映する。終わった後は何もしない。部材・接合の番号が範囲外なら例外。</summary>
        public void EndTick(TickReport report)
        {
            if (report == null) { throw new ArgumentNullException(nameof(report)); }
            _brokenThisTick.Clear();
            _jointsToRemove.Clear();
            ShotRemovedThisTick = false;
            if (IsOver) { return; }
            Validate(report);
            Ticks++;

            foreach (var joint in report.BrokenJoints) { _jointBroken[joint] = true; }

            // 面の始めの積み上げの落ち着きはダメージにしない（最初の弾を撃つまでの GraceTicks の間）。
            if (_launchedOnce || Ticks > _config.GraceTicks)
            {
                foreach (var impact in report.Impacts)
                {
                    if (impact.A >= 0) { Damage(impact.A, DamageOf(_config.Spec(Stage.Pieces[impact.A].Material), impact.Impulse)); }
                    if (impact.B >= 0) { Damage(impact.B, DamageOf(_config.Spec(Stage.Pieces[impact.B].Material), impact.Impulse)); }
                }
            }

            foreach (var hit in report.Blasts) { Damage(hit.Piece, BlastDamageOf(_config, hit.Distance)); }
            foreach (var piece in report.FellOut) { Break(piece); }

            // 消す順番を番号順にそろえる。報告の順番（Unity の衝突のコールバックの順番）はメモリの配置で変わり、
            // 消す順番が変わると Box2D の体の並びと計算の順番が変わって、同じ操作でも結果がわずかに分かれる。
            _brokenThisTick.Sort();
            _jointsToRemove.Sort();

            if (Phase == RunPhase.Flying)
            {
                TicksSinceLaunch++;
                if (report.ShotGone) { ShotAlive = false; }
                if (report.ShotTouched && FuseElapsed < 0 && FlyingShot == ShotKind.Bomb) { FuseElapsed = 0; }
                else if (FuseElapsed >= 0) { FuseElapsed++; }
            }

            var quiet = report.MaxSpeed < _config.RestSpeed && report.MaxAngular < _config.RestAngular;
            QuietTicks = quiet ? QuietTicks + 1 : 0;
            var rested = QuietTicks >= _config.RestTicks;

            switch (Phase)
            {
                case RunPhase.Flying when rested || TicksSinceLaunch >= _config.SettleTimeoutTicks:
                    Settle();
                    break;
                case RunPhase.Aiming when rested && TargetsLeft == 0:
                    Finish(RunPhase.Won);
                    break;
            }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public RunEvents TakeEvents()
        {
            var events = _events;
            _events = RunEvents.None;
            return events;
        }

        /// <summary>
        /// 以後の進行を決める状態のハッシュ。Health・壊れた部材・接合・弾の状態・静止の数え・Tick・結果、設定と面の指紋を含む。
        /// 物理の位置は含まない（Unity が持つ）。
        /// </summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(_fingerprint);
            hash.Mix(Ticks);
            hash.Mix((int)Phase);
            hash.Mix(ShotsUsed);
            hash.Mix(ShotAlive);
            hash.Mix(TicksSinceLaunch);
            hash.Mix(FuseElapsed);
            hash.Mix(Detonated);
            hash.Mix(QuietTicks);
            hash.Mix(_launchedOnce);
            hash.Mix(_pendingDetonate);
            hash.Mix(_pendingLaunch != null);
            if (_pendingLaunch is { } v)
            {
                hash.Mix(Bits(v.X));
                hash.Mix(Bits(v.Y));
            }

            hash.Mix(_pieceScore);
            for (var i = 0; i < _health.Length; i++)
            {
                hash.Mix(_health[i]);
                hash.Mix(_broken[i]);
            }

            foreach (var broken in _jointBroken) { hash.Mix(broken); }
            return hash.Value;
        }

        /// <summary>衝突の撃力によるダメージ：floor((撃力 − 閾値) × 係数)。閾値以下・有限でない撃力は 0。</summary>
        public static int DamageOf(MaterialSpec spec, float impulse)
        {
            if (spec == null) { throw new ArgumentNullException(nameof(spec)); }
            if (!float.IsFinite(impulse) || impulse <= spec.Threshold) { return 0; }
            var damage = Math.Floor((double)(impulse - spec.Threshold) * spec.DamagePerImpulse);
            return damage >= int.MaxValue ? int.MaxValue : (int)damage;
        }

        /// <summary>爆発のダメージ：爆心で <see cref="SiegeConfig.BlastDamage"/>、距離に比例して減り、半径以上で 0。</summary>
        public static int BlastDamageOf(SiegeConfig config, float distance)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (!float.IsFinite(distance) || distance >= config.BlastRadius) { return 0; }
            var ratio = 1.0 - Math.Max(0f, distance) / (double)config.BlastRadius;
            return (int)Math.Floor(config.BlastDamage * ratio);
        }

        private void Settle()
        {
            ShotRemovedThisTick = ShotAlive;
            ShotAlive = false;
            if (TargetsLeft == 0) { Finish(RunPhase.Won); }
            else if (ShotsLeft == 0) { Finish(RunPhase.Lost); }
            else
            {
                Phase = RunPhase.Aiming;
                _events |= RunEvents.Ready;
            }
        }

        private void Finish(RunPhase phase)
        {
            Phase = phase;
            _pendingLaunch = null;
            _pendingDetonate = false;
            _events |= phase == RunPhase.Won ? RunEvents.Won : RunEvents.Lost;
        }

        private void Damage(int piece, int amount)
        {
            if (piece < 0 || amount <= 0 || _broken[piece]) { return; }
            _health[piece] = amount >= _health[piece] ? 0 : _health[piece] - amount;
            _events |= RunEvents.Hit;
            if (_health[piece] == 0) { Break(piece); }
        }

        /// <summary>部材を壊す。既に壊れていれば何もしない（二重得点しない）。付いている接合を外す。</summary>
        private void Break(int piece)
        {
            if (_broken[piece]) { return; }
            _broken[piece] = true;
            _health[piece] = 0;
            BrokenCount++;
            _pieceScore += _config.Spec(Stage.Pieces[piece].Material).Score;
            if (Stage.Pieces[piece].IsTarget) { TargetsLeft--; }
            _brokenThisTick.Add(piece);
            for (var k = 0; k < _jointBroken.Length; k++)
            {
                var joint = Stage.Joints[k];
                if (_jointBroken[k] || (joint.A != piece && joint.B != piece)) { continue; }
                _jointBroken[k] = true;
                _jointsToRemove.Add(k);
            }

            _events |= RunEvents.Broke;
        }

        private void Validate(TickReport report)
        {
            foreach (var impact in report.Impacts)
            {
                CheckOptional(impact.A);
                CheckOptional(impact.B);
            }

            foreach (var hit in report.Blasts) { CheckPiece(hit.Piece); }
            foreach (var piece in report.FellOut) { CheckPiece(piece); }
            foreach (var joint in report.BrokenJoints)
            {
                if ((uint)joint >= (uint)_jointBroken.Length) { throw new ArgumentOutOfRangeException(nameof(report), joint, "接合の番号が範囲外です。"); }
            }
        }

        private void CheckOptional(int piece)
        {
            if (piece != -1) { CheckPiece(piece); }
        }

        private int CheckPiece(int piece)
        {
            if ((uint)piece >= (uint)_health.Length) { throw new ArgumentOutOfRangeException(nameof(piece), piece, "部材の番号が範囲外です。"); }
            return piece;
        }

        private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

        /// <summary>設定と面の、以後の進行に効く値の指紋（名前・案内は含まない）。</summary>
        private static uint Fingerprint(SiegeConfig config, Stage stage)
        {
            var hash = StateHash.Start();
            foreach (Material material in Enum.GetValues(typeof(Material)))
            {
                var spec = config.Spec(material);
                hash.Mix(spec.Health);
                hash.Mix(spec.Score);
                hash.Mix(Bits(spec.Threshold));
                hash.Mix(Bits(spec.DamagePerImpulse));
                hash.Mix(Bits(spec.Density));
            }

            foreach (ShotKind kind in Enum.GetValues(typeof(ShotKind)))
            {
                hash.Mix(Bits(config.Spec(kind).Radius));
                hash.Mix(Bits(config.Spec(kind).Density));
            }

            foreach (var value in new[] { config.Gravity, config.MaxPull, config.MinPull, config.MaxSpeed, config.ShotAngularDamping, config.BlastRadius, config.BlastImpulse, config.RestSpeed, config.RestAngular })
            {
                hash.Mix(Bits(value));
            }

            foreach (var value in new[] { config.BlastDamage, config.FuseTicks, config.RestTicks, config.SettleTimeoutTicks, config.ShotLifeTicks, config.GraceTicks, config.UnusedShotBonus })
            {
                hash.Mix(value);
            }

            hash.Mix(Bits(stage.Launcher.X));
            hash.Mix(Bits(stage.Launcher.Y));
            foreach (var shot in stage.Shots) { hash.Mix((int)shot); }
            foreach (var piece in stage.Pieces)
            {
                hash.Mix((int)piece.Material);
                hash.Mix(Bits(piece.Center.X));
                hash.Mix(Bits(piece.Center.Y));
                hash.Mix(Bits(piece.Size.X));
                hash.Mix(Bits(piece.Size.Y));
            }

            foreach (var joint in stage.Joints)
            {
                hash.Mix(joint.A);
                hash.Mix(joint.B);
                hash.Mix(Bits(joint.BreakForce));
            }

            return hash.Value;
        }
    }
}
