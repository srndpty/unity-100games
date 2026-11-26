using System.Runtime.CompilerServices;

// App のテストと PlayMode のテストから内部の場面づくりを使う（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G045.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
