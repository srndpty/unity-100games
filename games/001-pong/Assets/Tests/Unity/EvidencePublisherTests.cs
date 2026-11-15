using System;
using System.IO;
using System.Linq;
using System.Text;
using Game.Unity.Evidence;
using NUnit.Framework;

namespace Game.Unity.Tests
{
    /// <summary>証跡の公開は一式単位で、途中で失敗しても公開中の一式が新旧混ざらない。</summary>
    public sealed class EvidencePublisherTests
    {
        private string _evidence;
        private EvidencePublisher _publisher;

        [SetUp]
        public void SetUp()
        {
            _evidence = Path.Combine(Path.GetTempPath(), "evidence-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_evidence);
            _publisher = new EvidencePublisher(_evidence);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_evidence, recursive: true);
        }

        [Test]
        public void Publish_ReplacesWholeSet_AndRemovesFilesOnlyInPreviousSet()
        {
            PublishSet(("title.png", "old title"), ("stale.png", "only in old set"));

            PublishSet(("title.png", "new title"), ("result.png", "new result"));

            Assert.That(PublishedFiles(), Is.EquivalentTo(new[] { "manifest.json", "result.png", "title.png" }));
            Assert.That(ReadPublished("title.png"), Is.EqualTo("new title"));
            Assert.That(Directory.GetDirectories(_evidence), Is.EqualTo(new[] { _publisher.PublishedDirectory }), "一時フォルダ・退避フォルダが残らない");
        }

        [Test]
        public void Publish_RejectsSetThatDoesNotMatchRecord_AndKeepsPublishedSet()
        {
            PublishSet(("title.png", "old title"));

            // 記録にあるファイルが欠けている
            var missing = Stage(("title.png", "new"));
            File.Delete(Path.Combine(missing, "title.png"));
            Assert.Throws<InvalidOperationException>(() => _publisher.Publish(missing, Record(("title.png", "new"))));

            // 中身が記録と違う
            var tampered = Stage(("title.png", "new"));
            File.WriteAllText(Path.Combine(tampered, "title.png"), "tampered");
            Assert.Throws<InvalidOperationException>(() => _publisher.Publish(tampered, Record(("title.png", "new"))));

            // 記録にないファイルがある
            var extra = Stage(("title.png", "new"), ("extra.png", "x"));
            Assert.Throws<InvalidOperationException>(() => _publisher.Publish(extra, Record(("title.png", "new"))));

            Assert.That(ReadPublished("title.png"), Is.EqualTo("old title"));
            Assert.That(PublishedFiles(), Is.EquivalentTo(new[] { "manifest.json", "title.png" }));
        }

        [Test]
        public void Publish_WhenSwapFailsMidway_RestoresPreviousSet()
        {
            PublishSet(("title.png", "old title"), ("result.png", "old result"));
            var staging = Stage(("title.png", "new title"), ("result.png", "new result"));

            // 新しい一式のファイルを開いたままにすると、公開フォルダを退避した後の「一時フォルダ → 公開フォルダ」の付け替えが失敗する。
            using (new FileStream(Path.Combine(staging, "result.png"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.That(
                    () => _publisher.Publish(staging, Record(("title.png", "new title"), ("result.png", "new result"))),
                    Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            }

            Assert.That(ReadPublished("title.png"), Is.EqualTo("old title"));
            Assert.That(ReadPublished("result.png"), Is.EqualTo("old result"));
            Assert.That(Directory.Exists(staging), Is.True, "一時フォルダの後始末は呼び出し側に任せる");
        }

        [Test]
        public void ConcurrentCaptures_UseSeparateStaging_AndDoNotPublishAtTheSameTime()
        {
            PublishSet(("title.png", "old title"));
            var first = _publisher.CreateStaging();
            var second = _publisher.CreateStaging();
            Assert.That(first, Is.Not.EqualTo(second));
            Directory.Delete(first);

            var staging = Stage(("title.png", "new title"));
            var lockPath = Path.Combine(_evidence, EvidencePublisher.LockFileName);
            using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                Assert.Throws<IOException>(() => _publisher.Publish(staging, Record(("title.png", "new title"))));
            }

            Assert.That(ReadPublished("title.png"), Is.EqualTo("old title"));
        }

        private void PublishSet(params (string File, string Content)[] files)
        {
            var staging = Stage(files);
            _publisher.Publish(staging, Record(files));
        }

        // 撮影と同じく、一時フォルダに画像と manifest.json を書く。
        private string Stage(params (string File, string Content)[] files)
        {
            var staging = _publisher.CreateStaging();
            foreach (var (file, content) in files)
            {
                File.WriteAllText(Path.Combine(staging, file), content);
            }

            File.WriteAllText(Path.Combine(staging, EvidencePublisher.ManifestFileName), "{}");
            return staging;
        }

        private static (string File, string Sha256)[] Record(params (string File, string Content)[] files) =>
            files.Select(entry => (entry.File, EvidencePublisher.Sha256Of(Encoding.UTF8.GetBytes(entry.Content)))).ToArray();

        private string[] PublishedFiles() =>
            Directory.GetFiles(_publisher.PublishedDirectory).Select(Path.GetFileName).ToArray();

        private string ReadPublished(string file) =>
            File.ReadAllText(Path.Combine(_publisher.PublishedDirectory, file));
    }
}
