using System;

namespace Game.App
{
    /// <summary>
    /// 横スクロールのカメラ。自機が中央の遊び（デッドゾーン）を出たぶんだけ追い、面の左右の端で止める。
    /// 描画だけに使い、Core の判定（接地・接触・敵の起動）には関わらない。位置は Core と同じ整数の単位。
    /// </summary>
    public static class CameraFollow
    {
        /// <param name="cameraCenter">前のカメラの中心。</param>
        /// <param name="target">追う位置（自機の中心）。</param>
        /// <param name="deadZone">中心から左右にこれだけ離れるまでは動かない。</param>
        /// <param name="viewWidth">画面に映る幅。</param>
        /// <param name="levelWidth">面の幅。</param>
        public static int Follow(int cameraCenter, int target, int deadZone, int viewWidth, int levelWidth)
        {
            if (deadZone < 0) { throw new ArgumentOutOfRangeException(nameof(deadZone), deadZone, "0 以上にしてください。"); }
            if (viewWidth <= 0) { throw new ArgumentOutOfRangeException(nameof(viewWidth), viewWidth, "正の値にしてください。"); }
            if (levelWidth <= 0) { throw new ArgumentOutOfRangeException(nameof(levelWidth), levelWidth, "正の値にしてください。"); }

            var center = cameraCenter;
            if (target > center + deadZone)
            {
                center = target - deadZone;
            }
            else if (target < center - deadZone)
            {
                center = target + deadZone;
            }

            return Clamp(center, viewWidth, levelWidth);
        }

        /// <summary>面の端より外が映らない位置に収める。面が画面より狭ければ面の中央。</summary>
        public static int Clamp(int center, int viewWidth, int levelWidth)
        {
            if (levelWidth <= viewWidth)
            {
                return levelWidth / 2;
            }

            var half = viewWidth / 2;
            return Math.Max(half, Math.Min(levelWidth - (viewWidth - half), center));
        }
    }
}
