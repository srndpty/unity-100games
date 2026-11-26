using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core.Script
{
    /// <summary>
    /// スクリプトから使える素材の ID（背景・人物ごとの表情）。Unity 側の素材はこの表と照合する。
    /// </summary>
    public sealed class ScriptCatalog
    {
        private readonly HashSet<string> _backgrounds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _expressions = new(StringComparer.Ordinal);

        public ScriptCatalog(IEnumerable<string> backgrounds, IEnumerable<KeyValuePair<string, string>> portraits)
        {
            if (backgrounds == null) { throw new ArgumentNullException(nameof(backgrounds)); }
            if (portraits == null) { throw new ArgumentNullException(nameof(portraits)); }
            var bgs = new List<string>();
            foreach (var b in backgrounds)
            {
                if (_backgrounds.Add(b)) { bgs.Add(b); }
            }

            var list = new List<KeyValuePair<string, string>>();
            foreach (var p in portraits)
            {
                if (!_expressions.TryGetValue(p.Key, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    _expressions[p.Key] = set;
                }

                if (set.Add(p.Value)) { list.Add(p); }
            }

            Backgrounds = bgs.AsReadOnly();
            Portraits = new ReadOnlyCollection<KeyValuePair<string, string>>(list);
        }

        public IReadOnlyList<string> Backgrounds { get; }

        /// <summary>（人物の ID, 表情の ID）の組。</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Portraits { get; }

        public bool HasBackground(string id) => _backgrounds.Contains(id);

        public bool HasCharacter(string id) => _expressions.ContainsKey(id);

        public bool HasExpression(string character, string expression) =>
            _expressions.TryGetValue(character, out var set) && set.Contains(expression);
    }
}
