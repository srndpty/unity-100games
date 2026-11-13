using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>テスト基盤の疎通確認。最初の本物のテストを書いたら削除する。</summary>
    public sealed class GameInfoTests
    {
        [Test]
        public void IdMatchesProjectFolder()
        {
            Assert.That(GameInfo.Id, Is.EqualTo("001-pong"));
        }
    }
}
