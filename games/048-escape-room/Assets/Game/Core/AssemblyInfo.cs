using System.Runtime.CompilerServices;

// 探索の抽象状態の鍵をテストから確かめるため、内部を Core・App のテストと PlayMode のテストにだけ公開する（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.Core.Tests")]
[assembly: InternalsVisibleTo("G048.Core.Tests")]
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G048.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
