using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Game.Unity.Editor
{
    /// <summary>
    /// Unity のコンパイラは各アセンブリの csc.rsp（<c>-nullable:enable</c>）を読むが、IDE 用に生成される csproj には反映されない。
    /// そのままだと IDE だけが CS8632（nullable 注釈は #nullable 文脈でのみ使える）を出すため、
    /// 生成時に csc.rsp で Nullable を有効にしているアセンブリへ <c>&lt;Nullable&gt;enable&lt;/Nullable&gt;</c> を足す。
    /// </summary>
    public sealed class CsprojNullableSync : AssetPostprocessor
    {
        private const string NullableOption = "-nullable:enable";
        private static readonly Regex RspInclude = new(@"<None Include=""([^""]*csc\.rsp)""\s*/>");

        private static string OnGeneratedCSProject(string path, string content)
        {
            var match = RspInclude.Match(content);
            if (!match.Success)
            {
                return content;
            }

            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
            var rspPath = Path.Combine(projectDirectory, match.Groups[1].Value);
            if (!File.ReadAllText(rspPath).Contains(NullableOption))
            {
                return content;
            }

            return content.Replace("<LangVersion>", "<Nullable>enable</Nullable>\n    <LangVersion>");
        }
    }
}
