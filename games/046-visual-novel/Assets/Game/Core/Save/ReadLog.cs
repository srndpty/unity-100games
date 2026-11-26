using System;
using System.Collections.Generic;
using Game.Core.Runtime;

namespace Game.Core.Save
{
    /// <summary>
    /// 既読の文と到達した結末。周回をまたいで持ち、スキップは既読の文だけを送る。
    /// 文の ID はスクリプトにあるものだけを受け付ける。
    /// </summary>
    public sealed class ReadLog
    {
        private readonly HashSet<string> _known;
        private readonly HashSet<string> _read = new(StringComparer.Ordinal);
        private readonly bool[] _endings;

        public ReadLog(CompiledScript script)
        {
            Script = script ?? throw new ArgumentNullException(nameof(script));
            _known = new HashSet<string>(script.TextIds, StringComparer.Ordinal);
            _endings = new bool[script.Endings.Count];
        }

        public CompiledScript Script { get; }

        public int ReadCount => _read.Count;

        /// <summary>文の総数に対する既読の数（同じ ID は 1 回）。</summary>
        public int TextCount => _known.Count;

        public bool IsRead(string textId) => _read.Contains(textId);

        /// <summary>既読にする。初めてなら true。</summary>
        public bool MarkRead(string textId)
        {
            if (!_known.Contains(textId)) { throw new ArgumentException($"文 {textId} はスクリプトにありません", nameof(textId)); }
            return _read.Add(textId);
        }

        public bool Reached(int ending) => _endings[ending];

        public void MarkEnding(int ending) => _endings[ending] = true;

        public int EndingsReached
        {
            get
            {
                var n = 0;
                foreach (var e in _endings)
                {
                    if (e) { n++; }
                }

                return n;
            }
        }

        /// <summary>既読の ID をスクリプトに出てくる順に返す（保存の順序を決めるため）。</summary>
        public IReadOnlyList<string> ReadIdsInOrder()
        {
            var list = new List<string>();
            foreach (var id in Script.TextIds)
            {
                if (_read.Contains(id) && !list.Contains(id)) { list.Add(id); }
            }

            return list.AsReadOnly();
        }
    }
}
