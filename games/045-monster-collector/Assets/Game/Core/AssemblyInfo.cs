using System.Runtime.CompilerServices;

// テストから場面（体力・状態異常・手持ちなど）を直接作るため、内部の書き換えを Core・App のテストと PlayMode のテストにだけ公開する（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.Core.Tests")]
[assembly: InternalsVisibleTo("G045.Core.Tests")]
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G045.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
