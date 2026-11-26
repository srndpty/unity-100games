using System.Globalization;

namespace Game.Core.Script
{
    /// <summary>スクリプトの誤り 1 件（行番号は 1 から、行に結びつかない誤りは 0）。</summary>
    public sealed class ScriptError
    {
        public ScriptError(int line, string message)
        {
            Line = line;
            Message = message;
        }

        public int Line { get; }
        public string Message { get; }

        public override string ToString() =>
            Line > 0 ? string.Format(CultureInfo.InvariantCulture, "行 {0}：{1}", Line, Message) : Message;
    }
}
