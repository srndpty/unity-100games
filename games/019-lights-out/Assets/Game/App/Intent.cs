using Game.Core;

namespace Game.App
{
    public enum IntentKind
    {
        /// <summary>決定。タイトル・結果では試合を始め、一時停止中は再開する。</summary>
        Submit,

        TogglePause,

        CursorUp,
        CursorDown,
        CursorLeft,
        CursorRight,

        /// <summary>カーソルのマスを押す。</summary>
        PressCursor,

        /// <summary>指定のマスを押す（クリック）。カーソルもそのマスへ移す。</summary>
        PressCell,

        /// <summary>カーソルを指定のマスへ移す（マウスを動かした）。</summary>
        PointCell,

        /// <summary>出題時の盤からやり直す（一時停止中）。</summary>
        Retry,

        /// <summary>新しい問題（一時停止中・結果画面）。</summary>
        NewPuzzle,

        /// <summary>あきらめて解答を見る（一時停止中）。</summary>
        GiveUp,

        /// <summary>タイトルへ戻る（一時停止中・結果画面）。</summary>
        BackToTitle,

        /// <summary>終了（タイトル）。アプリの終了は Unity 層が行う。</summary>
        Quit,
    }

    /// <summary>入力を変換したプレイヤーの意図。種類の違う操作も 1 本の列に並べ、押した順に処理する。</summary>
    public readonly struct Intent
    {
        private Intent(IntentKind kind, int cell)
        {
            Kind = kind;
            Cell = cell;
        }

        public IntentKind Kind { get; }

        /// <summary>PressCell と PointCell のマス。それ以外は -1。</summary>
        public int Cell { get; }

        public static Intent Of(IntentKind kind) => new(kind, -1);

        public static Intent PressCell(int cell) => new(IntentKind.PressCell, LightsBoard.RequireIndex(cell));

        public static Intent PointCell(int cell) => new(IntentKind.PointCell, LightsBoard.RequireIndex(cell));

        public override string ToString() => Cell < 0 ? Kind.ToString() : $"{Kind}({Cell})";
    }
}
