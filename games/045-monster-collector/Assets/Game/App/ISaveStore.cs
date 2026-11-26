namespace Game.App
{
    public enum SaveReadStatus
    {
        /// <summary>記録がない。</summary>
        Missing,

        /// <summary>読み込めた（中身が正しいかは SaveCodec が判定する）。</summary>
        Loaded,

        /// <summary>保存領域の読み込み自体に失敗した。</summary>
        Failed,
    }

    /// <summary>
    /// セーブの保存領域。I/O の失敗は例外ではなく結果と理由で返し、App が画面に伝える。
    /// </summary>
    public interface ISaveStore
    {
        SaveReadStatus TryRead(out string content, out string failure);

        bool TryWrite(string content, out string failure);
    }

    public sealed class MemorySaveStore : ISaveStore
    {
        private string? _content;

        /// <summary>テスト用：読み込みの失敗を起こす。</summary>
        public string? ReadFailure { get; set; }

        /// <summary>テスト用：書き込みの失敗を起こす。</summary>
        public string? WriteFailure { get; set; }

        public string? Content => _content;

        public SaveReadStatus TryRead(out string content, out string failure)
        {
            content = string.Empty;
            if (ReadFailure != null)
            {
                failure = ReadFailure;
                return SaveReadStatus.Failed;
            }

            failure = string.Empty;
            if (_content == null)
            {
                return SaveReadStatus.Missing;
            }

            content = _content;
            return SaveReadStatus.Loaded;
        }

        public bool TryWrite(string content, out string failure)
        {
            if (WriteFailure != null)
            {
                failure = WriteFailure;
                return false;
            }

            _content = content ?? string.Empty;
            failure = string.Empty;
            return true;
        }
    }
}
