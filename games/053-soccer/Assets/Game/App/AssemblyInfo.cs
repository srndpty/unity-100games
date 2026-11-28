using System.Runtime.CompilerServices;

// App のテストと PlayMode のテストから、画面の流れの内部を確かめる（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G053.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
