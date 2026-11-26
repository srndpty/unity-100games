namespace Game.App
{
    public enum SaveReadStatus
    {
        /// <summary>記録がない。</summary>
        Missing,

        /// <summary>読み込めた（中身が正しいかはコーデックが判定する）。</summary>
        Loaded,

        /// <summary>保存領域の読み込み自体に失敗した。</summary>
        Failed,
    }

    /// <summary>保存領域（041）。I/O の失敗は例外ではなく結果と理由で返し、App が画面に伝える。</summary>
    public interface ISaveStore
    {
        SaveReadStatus TryRead(out string content, out string failure);

        bool TryWrite(string content, out string failure);
    }

    /// <summary>メモリ上の保存領域（テスト用）。</summary>
    public sealed class MemorySaveStore : ISaveStore
    {
        public string? Content { get; set; }

        /// <summary>読み込みの失敗を起こす。</summary>
        public string? ReadFailure { get; set; }

        /// <summary>書き込みの失敗を起こす。</summary>
        public string? WriteFailure { get; set; }

        public int Writes { get; private set; }

        public SaveReadStatus TryRead(out string content, out string failure)
        {
            content = "";
            failure = "";
            if (ReadFailure != null)
            {
                failure = ReadFailure;
                return SaveReadStatus.Failed;
            }

            if (Content == null) { return SaveReadStatus.Missing; }
            content = Content;
            return SaveReadStatus.Loaded;
        }

        public bool TryWrite(string content, out string failure)
        {
            if (WriteFailure != null)
            {
                failure = WriteFailure;
                return false;
            }

            Content = content;
            Writes++;
            failure = "";
            return true;
        }
    }
}
