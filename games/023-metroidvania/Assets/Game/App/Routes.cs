using System;
using System.Collections.ObjectModel;

namespace Game.App
{
    /// <summary>
    /// 既定の設定で、新しい試合を被弾せずに進める部屋の滞在ごとの入力の列（<see cref="RouteScript"/> の書式）。撮影台本とテストが使う。
    /// 回廊 → 洞穴（二段ジャンプ）→ 回廊 → 拠点 → 塔（破壊ショット → セーブ像 → ひびの壁）→ ボスの部屋の入口まで。ボス戦は <see cref="BossPilot"/> が行う。
    /// App テストの RouteFinder（普段は実行しない）で探して貼り付けた。設定の既定値や部屋を変えたら作り直す。
    /// </summary>
    public static class Routes
    {
        private static readonly string[] _texts =
        {
            "R244 L3",
            "RJ40 -4 RJ76 L4 LJ4 R8 J4 -8 J4 R8 LJ4 -4 L4 -4 RJ4 R16 RJ48 R4 J8 RJ20 R4 J16 R40 L1",
            "R108 RJ4 R84 L2 LJ124 -4 LJ12 L4 LJ92 L2",
            "L80 J4 -4 J8 L4 RJ4 L12 LJ144 -4 LJ4 L12",
            "LJ16 L4 LJ32 L4 J4 L12 LJ20 L4 J16 RJ8 R4 RJ4 R12 LJ20 J8 R4 RJ12 J4 R4 RJ20 R4 LJ4",
            "L4 LJ92 -4 J4 RJ12 L4 LJ36 L2 -4 R48 RJ4 R20 L14 -8 R76 RJ24 RS1 R31 L2",
        };

        /// <summary>全滞在の列を解析して返す（呼ぶたびに作り直す）。</summary>
        public static ReadOnlyCollection<RouteScript> All() => Array.AsReadOnly(Array.ConvertAll(_texts, RouteScript.Parse));

        /// <summary>列の後のボス戦を <see cref="BossPilot"/> に任せる操縦役。</summary>
        public static RoutePilot Pilot() => new(All(), new BossPilot());
    }
}
