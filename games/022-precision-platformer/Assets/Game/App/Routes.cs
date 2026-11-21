using System;
using System.Collections.ObjectModel;

namespace Game.App
{
    /// <summary>
    /// 既定の設定で各部屋を死亡せずに抜ける入力の列（<see cref="RouteScript"/> の書式）。撮影台本とテストが使う。
    /// App テストの RouteFinder（普段は実行しない）で探して貼り付けた。設定の既定値や部屋を変えたら作り直す。
    /// </summary>
    public static class Routes
    {
        private static readonly string[] _texts =
        {
            "R48 J4 LJ4 R76 RUJX1 RUJ3 L8 R36 RJ76 RX1 R3",
            "R44 RUX1 RU3 L8 R40 RJ68 RUX1 RU3 L8 R32 RJ4 R36 RX1 R3",
            "L4 LJ16 R4 LJ16 R16 J4 LJ24 L4 LJ16 RJ16 R4 LJ28 R4 LJ16 R8 L4 UX1 U3 LJ4 R52 RJ100 RDX1 RD3",
            "R40 RJ44 RUX1 RU3 L8 R36 RJ44 RUJX1 RUJ3 L8 R56 RDX1 RD3",
            "R4 RJ36 R4 LJ8 RJ8 UX1 U3 L4 R16 RJ16 R12 L12 J4 LJ12 R8 RUX1 RU3 L8 R16 RJ36 RUX1 RU3 L8 R56 RX1 R3",
            "R4 RJ12 R8 J4 L8 RJ4 LJ16 R4 L4 RUX1 RU3 L8 R8 RJ28 L12 J4 LJ12 R12 RUX1 RU3 L8 R12 RJ36 RUJX1 RUJ3 L8 R40 RX1 R3",
            "R28 RJ36 RJX1 RJ3 L8 R8 LJ4 L8 LJ16 R8 L12 J4 LJ16 R8 LJ8 L4 LJ16 R40 RJ36 RX1 R3 L8 R8 RX1 R2",
            "L4 LJ8 R4 LJ12 R4 LJ16 -4 LJ16 R4 LJ8 RUX1 RU3 L8 R52 RUX1 RU3 L8 R40 RJ40 RJX1 RJ3 L8 R44 RX1 R3",
            "R4 RJ32 RUJX1 RUJ3 L4 R4 L4 J4 L8 LJ16 L4 LJ16 R12 UX1 U3 L4 R52 RUJX1 RUJ3 L8 R52 RUJX1 RUJ3 L8 R24 RJ44 RX1 R2",
            "R4 RJ12 J4 L4 LJ12 L4 J4 LJ16 R4 LJ8 RUX1 RU3 L4 -4 R28 RJ44 RUX1 RU3 L4 -20 RJ8 R4 RJ28 RUX1 RU3 L8 R44 RUX1 RU3 L8 R24 RX1 R3",
        };

        /// <summary>全部屋の列を解析して返す（呼ぶたびに作り直す）。</summary>
        public static ReadOnlyCollection<RouteScript> All() => Array.AsReadOnly(Array.ConvertAll(_texts, RouteScript.Parse));
    }
}
