using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// ゲートの横断と周回。車両の速さや壁は見ない。渡された移動（壁で止めた後の経路）だけを見る。
    /// 次のゲートを、手前から奥へ横切ったときだけ印が進む。線の上、逆向き、次以外のゲートでは変えない。
    /// </summary>
    public static class Progress
    {
        /// <summary>
        /// 1 本の移動で次のゲートを横切ったら印を進める。0 番を前向きに横切ると周回が増え、
        /// <see cref="KartConfig.LapsToFinish"/> 周に達したらその Tick で完走して速さを 0 にする。
        /// </summary>
        public static bool Cross(Track track, ref Kart kart, Vec2i from, Vec2i to, int tick)
        {
            if (kart.Finished) { return false; }
            if (!Track.ForwardCross(track.Gates[kart.NextGate], from, to)) { return false; }
            if (kart.NextGate == 0)
            {
                kart.Laps++;
                if (kart.Laps >= KartConfig.LapsToFinish)
                {
                    kart.Finished = true;
                    kart.FinishTick = tick;
                    kart.Speed = 0;
                }
            }

            kart.NextGate = (kart.NextGate + 1) % track.CheckpointCount;
            return true;
        }
    }
}
