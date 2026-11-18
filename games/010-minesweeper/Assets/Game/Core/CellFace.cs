namespace Game.Core
{
    /// <summary>プレイヤーに見せるセルの面。進行中は爆弾の有無を含まない。</summary>
    public enum CellFace : byte
    {
        Hidden,
        Flagged,
        Open0,
        Open1,
        Open2,
        Open3,
        Open4,
        Open5,
        Open6,
        Open7,
        Open8,

        /// <summary>敗北時に見せる、踏まなかった爆弾。</summary>
        Mine,

        /// <summary>敗北の原因になった爆弾。</summary>
        Exploded,

        /// <summary>敗北時に見せる、爆弾のないセルに立てた旗。</summary>
        WrongFlag,
    }

    public static class CellFaces
    {
        public static CellFace Open(int adjacent) => (CellFace)((int)CellFace.Open0 + adjacent);

        public static bool IsOpen(this CellFace face) => face >= CellFace.Open0 && face <= CellFace.Open8;
    }
}
