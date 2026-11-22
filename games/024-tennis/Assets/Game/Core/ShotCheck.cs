namespace Game.Core
{
    /// <summary>
    /// 設定の検証：ずれ 0（狙いどおりの時機）の打球が入ることを、実際に <see cref="Ball"/> を飛ばして確かめる。
    /// 式で見積もらず、本番と同じ <see cref="Shots.Plan"/> と <see cref="Ball.Step"/> を使うので、丸めや判定の順序まで含めて食い違わない。
    /// </summary>
    public static class ShotCheck
    {
        private const int FlightLimit = TennisConfig.MaxTicks;

        /// <summary>最初に見つかった失敗の説明。全て入れば null。</summary>
        public static string? FirstFailure(TennisConfig config)
        {
            foreach (var side in new[] { Side.Near, Side.Far })
            {
                var forward = Court.Forward(side);
                foreach (var depth in new[] { Court.HalfLength, Court.ServiceLine, config.NetGap })
                {
                    foreach (var x in new[] { -Court.HalfWidth, 0, Court.HalfWidth })
                    {
                        foreach (var z in new[] { config.MinHitZ, config.MaxHitZ })
                        {
                            var contact = new Vec3(x, -depth * forward, z);
                            foreach (var kind in new[] { ShotKind.Stroke, ShotKind.Lob })
                            {
                                for (var aim = -1; aim <= 1; aim++)
                                {
                                    var failure = Rally(side, kind, aim, contact, config);
                                    if (failure != null) { return failure; }
                                }
                            }
                        }
                    }
                }

                foreach (var court in new[] { ServeCourt.Deuce, ServeCourt.Ad })
                {
                    for (var aim = -1; aim <= 1; aim++)
                    {
                        var failure = Serve(side, court, aim, config);
                        if (failure != null) { return failure; }
                    }
                }
            }

            return null;
        }

        /// <summary>サーブの位置でトスを上げ、頂点の Tick に打った球の位置（トスの間に地面に落ちれば null）。</summary>
        public static Vec3? TossAtApex(Vec3 serverPosition, TennisConfig config)
        {
            var ball = new Ball(serverPosition.WithZ(config.TossZ), new Vec3(0, 0, config.TossSpeed));
            for (var tick = 1; tick <= config.TossApexTick; tick++)
            {
                if (ball.Step(config.Physics).Kind != BallEventKind.None) { return null; }
            }

            return ball.Position;
        }

        private static string? Rally(Side side, ShotKind kind, int aim, Vec3 contact, TennisConfig config)
        {
            var velocity = Shots.Plan(side, kind, aim, 0, contact, ServeCourt.Deuce, config);
            var landing = Fly(new Ball(contact, velocity), config);
            var receiver = Court.Other(side);
            if (landing == null || !Court.InSingles(landing.Value.X, landing.Value.Y, receiver))
            {
                return $"狙いどおりの{kind}（打点 {contact}、狙い {aim}）が相手コートに入りません（着地 {Describe(landing)}）。深さ・狙いの幅・ネットの余裕を見直してください。";
            }

            return null;
        }

        private static string? Serve(Side server, ServeCourt court, int aim, TennisConfig config)
        {
            var contact = TossAtApex(Positions.Serve(server, court, config), config);
            if (contact == null) { return "サーブのトスが頂点に達する前に地面に落ちます。"; }
            var velocity = Shots.Plan(server, ShotKind.Serve, aim, 0, contact.Value, court, config);
            var landing = Fly(new Ball(contact.Value, velocity), config);
            if (landing == null || !Court.InServiceBox(landing.Value.X, landing.Value.Y, server, court))
            {
                return $"狙いどおりのサーブ（{court}、狙い {aim}）がサービスボックスに入りません（着地 {Describe(landing)}）。サーブの深さ・狙い・ネットの余裕を見直してください。";
            }

            return null;
        }

        /// <summary>最初のバウンドの位置。ネットにかかるか、上限の Tick までに弾まなければ null。</summary>
        private static Vec3? Fly(Ball ball, TennisConfig config)
        {
            for (var tick = 0; tick < FlightLimit; tick++)
            {
                var e = ball.Step(config.Physics);
                if (e.Kind == BallEventKind.Net) { return null; }
                if (e.Kind == BallEventKind.Bounce) { return e.Point; }
            }

            return null;
        }

        private static string Describe(Vec3? landing) => landing.HasValue ? landing.Value.ToString() : "ネットか、弾まない";
    }
}
