using System.Runtime.CompilerServices;

// イベントの選択の内部をテストから確かめるため、内部を Core・App のテストと PlayMode のテストにだけ公開する（Unity と dotnet でアセンブリ名が違う）。
[assembly: InternalsVisibleTo("Game.Core.Tests")]
[assembly: InternalsVisibleTo("G050.Core.Tests")]
[assembly: InternalsVisibleTo("Game.App.Tests")]
[assembly: InternalsVisibleTo("G050.App.Tests")]
[assembly: InternalsVisibleTo("Game.Unity.Tests")]
