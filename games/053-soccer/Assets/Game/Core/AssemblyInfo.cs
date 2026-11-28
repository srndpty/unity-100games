using System.Runtime.CompilerServices;

// 試合の内部（場面を直接作る口）を Core・App のテストと PlayMode のテストにだけ公開する（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.Core.Tests")]
[assembly: InternalsVisibleTo("G053.Core.Tests")]
[assembly: InternalsVisibleTo("Game.App")]
[assembly: InternalsVisibleTo("G053.App")]
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G053.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
