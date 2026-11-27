using System.Runtime.CompilerServices;

// App のテストと PlayMode のテストから、試合を直接確かめる（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G050.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
