namespace Game.Core.Play
{
    public enum EventKind
    {
        /// <summary>文。Speaker は人物の物の番号（主人公・地の文は -1）。</summary>
        Said,
        ItemGained,
        ItemLost,
        PuzzleSolved,
        HotspotShown,
        HotspotHidden,
        ExitUnlocked,
        RoomChanged,
        TalkStarted,
        TalkEnded,
        Escaped,
    }

    /// <summary>行為の結果の出来事。表示と音は出来事から作る。</summary>
    public readonly struct AdventureEvent
    {
        public AdventureEvent(EventKind kind, int index, string text = "", int speaker = -1)
        {
            Kind = kind;
            Index = index;
            Text = text;
            Speaker = speaker;
        }

        public EventKind Kind { get; }

        /// <summary>持ち物・謎・物・部屋の番号（Said では使わない）。</summary>
        public int Index { get; }

        public string Text { get; }
        public int Speaker { get; }

        /// <summary>ルールが当たらず、動詞の既定の返事を出したか（Said のみ）。</summary>
        public bool IsDefault => Kind == EventKind.Said && Index == DefaultLines.DefaultMarker;
    }

    /// <summary>ルールが当たらないときの返事。</summary>
    public static class DefaultLines
    {
        internal const int DefaultMarker = -2;

        public const string Look = "特に変わったところはない。";
        public const string Take = "それは取れない。";
        public const string Use = "どう使えばいいのか分からない。";
        public const string UseWith = "それは役に立ちそうにない。";
        public const string Talk = "返事はない。";
        public const string Locked = "閉まっていて通れない。";
    }
}
