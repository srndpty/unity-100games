using System.Runtime.CompilerServices;

// テストから試合を直接読むため、内部を App のテストと PlayMode のテストにだけ公開する（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G044.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
