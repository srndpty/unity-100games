using System.Runtime.CompilerServices;

// App のテストと PlayMode のテストから、実行器と既読の記録を直接確かめる（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G046.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
