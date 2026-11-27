using System.Runtime.CompilerServices;

// テストから構文解析の中間（RawStatement）と探索の内部を確かめるため、内部を Core・App のテストと PlayMode のテストにだけ公開する（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.Core.Tests")]
[assembly: InternalsVisibleTo("G047.Core.Tests")]
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G047.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
