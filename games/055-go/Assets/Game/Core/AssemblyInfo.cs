using System.Runtime.CompilerServices;

// 盤の書き換え（着手の適用）と探索の内部を Core・App のテストと PlayMode のテストにだけ公開する（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.Core.Tests")]
[assembly: InternalsVisibleTo("G055.Core.Tests")]
[assembly: InternalsVisibleTo("Game.App")]
[assembly: InternalsVisibleTo("G055.App")]
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G055.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
