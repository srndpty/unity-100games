using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>README の表がデータと一致する。</summary>
    public sealed class ReadmeTests
    {
        [Test]
        public void ReadmeHasTheCardTableBuiltFromTheData()
        {
            var table = CardText.Table(Lab.Data);
            var readme = Lab.ReadmeText().Replace("\r", string.Empty, StringComparison.Ordinal);
            Assert.That(readme.Contains(table, StringComparison.Ordinal), Is.True, "README のカード表を次に置き換える：\n" + table);
        }
    }
}
