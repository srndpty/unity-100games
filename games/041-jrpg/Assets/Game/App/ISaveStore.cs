namespace Game.App
{
    public interface ISaveStore
    {
        bool Exists { get; }
        string? Read();
        void Write(string content);
        void Delete();
    }

    public sealed class MemorySaveStore : ISaveStore
    {
        private string? _content;

        public bool Exists => _content != null;

        public string? Read() => _content;

        public void Write(string content)
        {
            _content = content ?? string.Empty;
        }

        public void Delete()
        {
            _content = null;
        }
    }
}
