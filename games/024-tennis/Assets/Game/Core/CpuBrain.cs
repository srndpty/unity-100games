using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// CPU の判断。相手が打つと、反応の遅れの後に球の軌跡を予測し（<see cref="BallPredictor"/>、本番と同じ <see cref="Ball.Step"/>）、
    /// 自分のコートで 1 回弾んだ後に打てる高さへ下りてくる Tick を打点に選ぶ。そこを理想の打点にできる位置へ動き、
    /// 打点の <see cref="TennisConfig.ImpactTicks"/> Tick 前（±ぶれ）に振る。予測でアウト・フォルトになる球は見送る。
    /// 自分が打った後はベースラインの中央へ戻る。プレイヤー側の自動操縦（App の Autopilot）も同じ判断を使う。
    /// <para>乱数は判断（ぶれ・狙い・ロブ）とサーブで系列を分ける。</para>
    /// </summary>
    public sealed class CpuBrain
    {
        private const int PredictTicks = 400;
        private const uint ServeStream = 0x5EBE0001u;

        private readonly Side _side;
        private SeededRandom _decide;
        private SeededRandom _serve;

        // サーブの計画（どのサーブ機会のものか、トスを上げる Tick、頂点からのずれ、狙い）。
        private long _serveKey = -1;
        private long _tossStep;
        private int _serveOffset;
        private int _serveAim;

        // 返球の計画（どの打球に対するものか、反応の終わり、立つ位置、振り始める Tick、ショット、狙い）。
        private long _shotKey = -1;
        private long _reactUntil;
        private bool _planned;
        private bool _letGo;
        private bool _swung;
        private Vec3 _stand;
        private long _pressStep;
        private ShotKind _kind;
        private int _aim;

        private bool _pressedLast;

        public CpuBrain(Side side, int seed)
        {
            _side = side;
            _decide = new SeededRandom(seed);
            _serve = new SeededRandom(unchecked(seed ^ (int)ServeStream));
        }

        public Side Side => _side;

        /// <summary>計画を立てたか（テスト用）。</summary>
        public bool HasPlan => _planned && !_letGo;

        /// <summary>計画した立ち位置（テスト・描画用）。</summary>
        public Vec3 PlannedStand => _stand;

        public CpuBrain Clone()
        {
            var copy = (CpuBrain)MemberwiseClone();
            copy._decide = _decide.Clone();
            copy._serve = _serve.Clone();
            return copy;
        }

        /// <summary>乱数の状態を保ったまま、計画を捨てる（場面を作り直したとき）。</summary>
        public CpuBrain Restarted() => new(_side, 0) { _decide = _decide.Clone(), _serve = _serve.Clone() };

        public AthleteCommand Decide(TennisMatch match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var command = Choose(match);

            // 押し始めでしか振れないので、続けて押すときは 1 Tick 離す。
            var press = command.Stroke || command.Lob;
            if (press && _pressedLast) { command = new AthleteCommand(command.MoveX, command.MoveY, false, false); press = false; }
            _pressedLast = press;
            return command;
        }

        public void Mix(ref StateHash h)
        {
            h.Mix(_decide.State);
            h.Mix(_serve.State);
            h.Mix(_serveKey);
            h.Mix(_tossStep);
            h.Mix(_serveOffset);
            h.Mix(_serveAim);
            h.Mix(_shotKey);
            h.Mix(_reactUntil);
            h.Mix(_planned);
            h.Mix(_letGo);
            h.Mix(_swung);
            h.Mix(_stand.X);
            h.Mix(_stand.Y);
            h.Mix(_pressStep);
            h.Mix((int)_kind);
            h.Mix(_aim);
            h.Mix(_pressedLast);
        }

        private AthleteCommand Choose(TennisMatch match)
        {
            if (match.Phase != MatchPhase.Point) { return AthleteCommand.Idle; }

            var config = match.Config;
            var rally = match.Rally;
            var me = match.AthleteOf(_side);
            var nextStep = match.Tick + 1;
            switch (rally.Phase)
            {
                case RallyPhase.Ready:
                    if (rally.Server != _side) { return AthleteCommand.Idle; }
                    var serveKey = (match.PointsPlayed * 4L) + rally.ServeNumber;
                    if (serveKey != _serveKey)
                    {
                        _serveKey = serveKey;
                        _tossStep = nextStep + config.CpuServeWaitTicks;
                        _serveOffset = _serve.Range(-config.CpuTimingJitter, config.CpuTimingJitter);
                        _serveAim = _serve.Range(-1, 1);
                    }

                    return nextStep >= _tossStep ? new AthleteCommand(0, 0, true, false) : AthleteCommand.Idle;

                case RallyPhase.Toss:
                    if (rally.Server != _side || rally.TossSwung) { return AthleteCommand.Idle; }

                    // 押した Tick の TossTick は、この後の Step で 1 つ進む前の値。
                    var hit = rally.TossTick >= config.TossApexTick + _serveOffset;
                    return new AthleteCommand(_serveAim * Court.RightOf(_side), 0, hit, false);

                case RallyPhase.InPlay:
                    return rally.Incoming == _side ? Return(match, me, config) : Recover(me, config);

                default:
                    return AthleteCommand.Idle;
            }
        }

        private AthleteCommand Return(TennisMatch match, Athlete me, TennisConfig config)
        {
            var rally = match.Rally;
            var key = (match.PointsPlayed * 1000L) + rally.Shots;
            if (key != _shotKey)
            {
                _shotKey = key;
                _reactUntil = match.Tick + config.CpuReactionTicks;
                _planned = false;
                _letGo = false;
                _swung = false;
            }

            if (match.Tick < _reactUntil) { return AthleteCommand.Idle; }
            if (!_planned) { Plan(match, me, config); }
            if (_letGo) { return Recover(me, config); }

            var move = MoveToward(me, _stand, config);
            if (_swung || me.IsSwinging || match.Tick + 1 < _pressStep) { return move; }

            _swung = true;
            var aimX = _aim * Court.RightOf(_side);
            return new AthleteCommand(aimX, move.MoveY, _kind == ShotKind.Stroke, _kind == ShotKind.Lob);
        }

        private void Plan(TennisMatch match, Athlete me, TennisConfig config)
        {
            _planned = true;
            var prediction = BallPredictor.FindContact(match.Ball, match.Rally, _side, me.Position, SpeedOf(config), config, PredictTicks);
            if (prediction == null)
            {
                _letGo = true;
                return;
            }

            var contact = prediction.Value;
            _stand = me.Clamp(new Vec3(contact.Position.X, contact.Position.Y - (config.ContactAhead * Court.Forward(_side)), 0), config);
            var jitter = _decide.Range(-config.CpuTimingJitter, config.CpuTimingJitter);
            _pressStep = match.Tick + contact.Ticks - config.ImpactTicks + jitter;
            _aim = _decide.Range(-1, 1);
            var opponent = match.AthleteOf(Court.Other(_side));
            var opponentAtNet = Math.Abs(opponent.Position.Y) < config.CpuLobTrigger;
            _kind = opponentAtNet || _decide.Next(100) < config.CpuLobPercent ? ShotKind.Lob : ShotKind.Stroke;
        }

        private AthleteCommand Recover(Athlete me, TennisConfig config)
        {
            var home = new Vec3(0, -(Court.HalfLength + config.ReceiveBackOffset) * Court.Forward(_side), 0);
            return MoveToward(me, home, config);
        }

        private AthleteCommand MoveToward(Athlete me, Vec3 target, TennisConfig config)
        {
            var deadZone = Math.Max(1, SpeedOf(config) / 2);
            var dx = target.X - me.Position.X;
            var dy = target.Y - me.Position.Y;
            var mx = Math.Abs(dx) > deadZone ? Math.Sign(dx) : 0;
            var my = Math.Abs(dy) > deadZone ? Math.Sign(dy) : 0;
            return new AthleteCommand(mx, my, false, false);
        }

        private int SpeedOf(TennisConfig config) => _side == Side.Near ? config.MoveSpeed : config.CpuMoveSpeed;
    }
}
