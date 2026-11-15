using System;
using System.IO;
using System.Linq;
using System.Text;
using Game.Unity.Evidence;
using NUnit.Framework;
using UnityEngine;

namespace Game.Unity.Tests
{
    /// <summary>
    /// 証跡の公開。ポインタ（current.json）は、どの時点で失敗・中断しても「検証済みで完全な世代」を指す。
    /// manifest.json の内容と画像が一致しない一式は公開しない。
    /// </summary>
    public sealed class EvidencePublisherTests
    {
        private static readonly (string File, string Content)[] SetA = { ("title.png", "A title"), ("stale.png", "only in A") };
        private static readonly (string File, string Content)[] SetB = { ("title.png", "B title"), ("result.png", "B result") };

        private string _evidence;
        private EvidencePublisher _publisher;

        [SetUp]
        public void SetUp()
        {
            _evidence = NewEvidenceDirectory();
            _publisher = new EvidencePublisher(_evidence);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_evidence, recursive: true);
        }

        [Test]
        public void Publish_SwitchesPointerToNewGeneration_AndRemovesOldGeneration()
        {
            var first = _publisher.Publish(Stage(_publisher, SetA));
            var second = _publisher.Publish(Stage(_publisher, SetB));

            Assert.That(second.PreviousGeneration, Is.EqualTo(first.Generation));
            Assert.That(_publisher.CurrentGeneration, Is.EqualTo(second.Generation));
            Assert.That(CurrentFiles(_publisher), Is.EquivalentTo(new[] { "manifest.json", "result.png", "title.png" }), "前の世代だけにあったファイルは見えない");
            Assert.That(ReadCurrent(_publisher, "title.png"), Is.EqualTo("B title"));
            Assert.That(Directory.GetDirectories(_publisher.GenerationsDirectory).Select(Path.GetFileName), Is.EqualTo(new[] { second.Generation }), "使われていない世代は消える");
            Assert.That(Directory.GetDirectories(_evidence).Select(Path.GetFileName), Is.EqualTo(new[] { EvidencePublisher.GenerationsFolderName }), "一時フォルダが残らない");
        }

        [Test]
        public void SameImages_KeepTheSameGeneration()
        {
            var first = _publisher.Publish(Stage(_publisher, SetA));
            var staging = Stage(_publisher, SetA);

            var second = _publisher.Publish(staging);

            Assert.That(second.Generation, Is.EqualTo(first.Generation), "README の画像リンクが撮り直しで切れない");
            Assert.That(Directory.Exists(staging), Is.False);
            Assert.That(ReadCurrent(_publisher, "title.png"), Is.EqualTo("A title"));
        }

        [Test]
        public void SetThatDoesNotMatchManifest_IsRejected_AndPointerIsUnchanged()
        {
            var published = _publisher.Publish(Stage(_publisher, SetA)).Generation;

            AssertRejected(staging => File.WriteAllText(Path.Combine(staging, "manifest.json"), "{}"), "撮影記録が空");
            AssertRejected(staging => File.WriteAllText(Path.Combine(staging, "manifest.json"), "not json"), "JSON として読めない");
            AssertRejected(staging => File.Delete(Path.Combine(staging, "result.png")), "画像が欠けている");
            AssertRejected(staging => File.WriteAllText(Path.Combine(staging, "result.png"), "tampered"), "ハッシュが違う");
            AssertRejected(staging => File.WriteAllText(Path.Combine(staging, "extra.png"), "x"), "記録にない画像がある");
            AssertRejected(staging => Directory.CreateDirectory(Path.Combine(staging, "sub")), "サブフォルダがある");
            AssertRejected(staging => RewriteShots(staging, shots => shots.Concat(new[] { shots[0] }).ToArray()), "ファイル名が重複");
            AssertRejected(staging => RewriteShots(staging, shots => new[] { Shot("../escape.png", "x") }), "上の階層を指す");
            AssertRejected(staging => RewriteShots(staging, shots => new[] { Shot("sub/x.png", "x") }), "パスを含む");
            AssertRejected(staging => RewriteShots(staging, shots => new[] { Shot("manifest.json", "x") }), "manifest.json を画像として記録");

            Assert.That(_publisher.CurrentGeneration, Is.EqualTo(published));
            Assert.DoesNotThrow(() => EvidencePublisher.Verify(_publisher.GenerationDirectory(published)));

            void AssertRejected(Action<string> corrupt, string reason)
            {
                var staging = Stage(_publisher, SetB);
                corrupt(staging);
                Assert.Throws<InvalidOperationException>(() => _publisher.Publish(staging), reason);
                Directory.Delete(staging, recursive: true);
            }
        }

        [Test]
        public void FailureAtAnyFileOperation_LeavesPointerOnACompleteGeneration()
        {
            // 世代 A が公開中のところに B を公開する。状態を変える操作は 4 回
            // （一時フォルダ → 世代フォルダ、ポインタの一時ファイル書き込み、ポインタの置き換え、A の削除）。
            // それぞれの直前で失敗させる。例外のまま後始末をしないので、その時点での強制終了と同じ状態になる。
            for (var failAt = 0; failAt < 4; failAt++)
            {
                var evidence = NewEvidenceDirectory();
                try
                {
                    var generationA = new EvidencePublisher(evidence).Publish(Stage(new EvidencePublisher(evidence), SetA)).Generation;
                    var faulty = new FaultyOps(failAt);
                    var publisher = new EvidencePublisher(evidence, faulty);
                    var staging = Stage(publisher, SetB);
                    string generationB = null;
                    try
                    {
                        generationB = publisher.Publish(staging).Generation;
                    }
                    catch (IOException exception) when (exception.Message == FaultyOps.Message)
                    {
                    }

                    Assert.That(faulty.Failed, Is.True, $"{failAt} 回目の操作で失敗させた");
                    var current = publisher.CurrentGeneration;
                    Assert.That(current, Is.EqualTo(generationB ?? generationA), $"{failAt} 回目で失敗: ポインタは A か B のどちらか");
                    Assert.DoesNotThrow(() => EvidencePublisher.Verify(publisher.GenerationDirectory(current)), $"{failAt} 回目で失敗: 指している世代は完全");

                    // 次の公開で、残ったゴミ（指されていない世代・一時ファイル）も片付く。
                    var clean = new EvidencePublisher(evidence);
                    var republished = clean.Publish(Stage(clean, SetB)).Generation;
                    Assert.That(Directory.GetDirectories(clean.GenerationsDirectory).Select(Path.GetFileName), Is.EqualTo(new[] { republished }));
                    Assert.That(Directory.GetFiles(evidence).Select(Path.GetFileName), Is.EqualTo(new[] { EvidencePublisher.PointerFileName }));
                }
                finally
                {
                    Directory.Delete(evidence, recursive: true);
                }
            }
        }

        [Test]
        public void ConcurrentCaptures_UseSeparateStaging_AndDoNotPublishAtTheSameTime()
        {
            var published = _publisher.Publish(Stage(_publisher, SetA)).Generation;
            Assert.That(_publisher.CreateStaging(), Is.Not.EqualTo(_publisher.CreateStaging()));

            var staging = Stage(_publisher, SetB);
            var lockPath = Path.Combine(_evidence, EvidencePublisher.LockFileName);
            using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                Assert.Throws<IOException>(() => _publisher.Publish(staging));
            }

            Assert.That(_publisher.CurrentGeneration, Is.EqualTo(published));
        }

        private static string NewEvidenceDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "evidence-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        // 撮影と同じく、一時フォルダに画像と、それを記録した manifest.json を書く。
        private static string Stage(EvidencePublisher publisher, (string File, string Content)[] files)
        {
            var staging = publisher.CreateStaging();
            var manifest = new CaptureManifest();
            foreach (var (file, content) in files)
            {
                File.WriteAllText(Path.Combine(staging, file), content);
                manifest.Shots.Add(Shot(file, content));
            }

            WriteManifest(staging, manifest);
            return staging;
        }

        private static void RewriteShots(string staging, Func<CaptureManifest.Shot[], CaptureManifest.Shot[]> rewrite)
        {
            var manifest = JsonUtility.FromJson<CaptureManifest>(File.ReadAllText(Path.Combine(staging, "manifest.json")));
            manifest.Shots = rewrite(manifest.Shots.ToArray()).ToList();
            WriteManifest(staging, manifest);
        }

        private static void WriteManifest(string staging, CaptureManifest manifest) =>
            File.WriteAllText(Path.Combine(staging, EvidencePublisher.ManifestFileName), JsonUtility.ToJson(manifest));

        private static CaptureManifest.Shot Shot(string file, string content) =>
            new() { File = file, Sha256 = EvidencePublisher.Sha256Of(Encoding.UTF8.GetBytes(content)) };

        private static string[] CurrentFiles(EvidencePublisher publisher) =>
            Directory.GetFiles(publisher.GenerationDirectory(publisher.CurrentGeneration)).Select(Path.GetFileName).ToArray();

        private static string ReadCurrent(EvidencePublisher publisher, string file) =>
            File.ReadAllText(Path.Combine(publisher.GenerationDirectory(publisher.CurrentGeneration), file));

        // 実際のファイル操作をしつつ、指定した回数目の操作だけ実行せずに失敗させる。
        private sealed class FaultyOps : IEvidenceFileOps
        {
            public const string Message = "injected fault";

            private readonly int _failAt;
            private int _calls;

            public FaultyOps(int failAt)
            {
                _failAt = failAt;
            }

            public bool Failed { get; private set; }

            public void MoveDirectory(string source, string destination) => Run(() => EvidenceFileOps.Instance.MoveDirectory(source, destination));

            public void WriteFile(string path, string contents) => Run(() => EvidenceFileOps.Instance.WriteFile(path, contents));

            public void ReplaceFile(string source, string destination) => Run(() => EvidenceFileOps.Instance.ReplaceFile(source, destination));

            public void DeleteDirectory(string path) => Run(() => EvidenceFileOps.Instance.DeleteDirectory(path));

            private void Run(Action operation)
            {
                if (_calls++ == _failAt)
                {
                    Failed = true;
                    throw new IOException(Message);
                }

                operation();
            }
        }
    }
}
