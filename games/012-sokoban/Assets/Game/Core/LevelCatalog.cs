using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>手作りの全10問。難しさの順に並ぶ。</summary>
    public static class LevelCatalog
    {
        private static readonly string[] _names =
        {
            "最初の荷",
            "回り道",
            "二つの荷",
            "左右の棚",
            "三つの印",
            "細い通路",
            "十字路",
            "積み残し",
            "満潮",
            "最後の便",
        };

        private static readonly string[][] _sources =
        {
            new[]
            {
                "#######",
                "#     #",
                "# @$ .#",
                "#     #",
                "#######",
            },
            new[]
            {
                "#######",
                "#.    #",
                "### # #",
                "#   $ #",
                "# @   #",
                "#######",
            },
            new[]
            {
                "########",
                "#  .   #",
                "# $ #$ #",
                "#  @#. #",
                "########",
            },
            new[]
            {
                "#########",
                "#.  #  .#",
                "# $   $ #",
                "#   @   #",
                "#########",
            },
            new[]
            {
                "##########",
                "#   ##   #",
                "# $ .. $ #",
                "#  #@ #  #",
                "#  $ .   #",
                "##########",
            },
            new[]
            {
                " ########",
                " #   #  #",
                "##$  $  #",
                "#  .  . #",
                "# @ $ . #",
                "#########",
            },
            new[]
            {
                "##########",
                "#  .#    #",
                "# #   ## #",
                "# . $@$  #",
                "# ##$#   #",
                "#   .#   #",
                "##########",
            },
            new[]
            {
                "###########",
                "#    #    #",
                "# $$ # .. #",
                "#    $  . #",
                "#  @ #    #",
                "###########",
            },
            new[]
            {
                "  ########",
                "###  #   #",
                "#  $   $ #",
                "# #.#.#  #",
                "# $ @ $ ##",
                "## .#.   #",
                " #       #",
                " #########",
            },
            new[]
            {
                "############",
                "#    #     #",
                "# $$   ##  #",
                "#  # $   . #",
                "## #  # .. #",
                "#@   $   . #",
                "############",
            },
        };

        private static readonly LevelMap[] _maps = ParseAll();

        public static int Count => _maps.Length;
        public static IReadOnlyList<LevelMap> Maps => _maps;
        public static IReadOnlyList<string> Names => _names;

        private static LevelMap[] ParseAll()
        {
            var maps = new LevelMap[_sources.Length];
            for (var i = 0; i < maps.Length; i++) { maps[i] = LevelParser.Parse(_sources[i]); }
            return maps;
        }
    }
}
