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
    /// 証跡の公開。ポインタ（current.json）は、どの操作の間で失敗・中断しても「検証済みで完全な画像世代と撮影記録の組」を指す。
    /// 画像は内容で世代を使い回し、撮影記録（ビルドなど）は撮影ごとに残す。manifest.json の内容と画像が一致しない一式は公開しない。
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
        public void Publish_SwitchesPointerToNewGeneration_AndRemovesOldOnes()
        {
            var first = _publisher.Publish(Stage(_publisher, SetA, "build-A"));
            var second = _publisher.Publish(Stage(_publisher, SetB, "build-B"));

            Assert.That(second.Previous.Generation, Is.EqualTo(first.Generation));
            Assert.That(_publisher.Current.Generation, Is.EqualTo(second.Generation));
            Assert.That(_publisher.Current.Run, Is.EqualTo(second.Run));
            Assert.That(_publisher.VerifyCurrent().BuildGuid, Is.EqualTo("build-B"));
            Assert.That(CurrentImages(_publisher), Is.EquivalentTo(new[] { "result.png", "title.png" }), "前の世代だけにあった画像は見えない");
            Assert.That(ReadCurrent(_publisher, "title.png"), Is.EqualTo("B title"));
            Assert.That(Names(Directory.GetDirectories(_publisher.GenerationsDirectory)), Is.EqualTo(new[] { second.Generation }), "使われていない世代は消える");
            Assert.That(Names(Directory.GetDirectories(_publisher.RunsDirectory)), Is.EqualTo(new[] { second.Run }), "使われていない撮影記録は消える");
            Assert.That(Names(Directory.GetDirectories(_evidence)), Is.EquivalentTo(new[] { "captures", "runs" }), "一時フォルダが残らない");
        }

        [Test]
        public void SameImagesFromDifferentBuild_ReuseGeneration_ButRecordTheNewRun()
        {
            var first = _publisher.Publish(Stage(_publisher, SetA, "build-A", "2026-11-15T00:00:00Z"));
            var staging = Stage(_publisher, SetA, "build-B", "2026-11-16T00:00:00Z");

            var second = _publisher.Publish(staging);

            Assert.That(second.Generation, Is.EqualTo(first.Generation), "画像世代は同じ（README の画像リンクが保たれる）");
            Assert.That(second.Run, Is.Not.EqualTo(first.Run), "撮影記録は別");
            var manifest = _publisher.VerifyCurrent();
            Assert.That(manifest.BuildGuid, Is.EqualTo("build-B"), "公開中の記録は新しい撮影のもの");
            Assert.That(manifest.CapturedAtUtc, Is.EqualTo("2026-11-16T00:00:00Z"));
            Assert.That(Directory.Exists(staging), Is.False);
        }

        [Test]
        public void JpegFrame_IsPublishedWithEncodedHash_AndDecodesAtOriginalSize()
        {
            var staging = _publisher.CreateStaging();
            var manifest = new CaptureManifest();
            var context = new CaptureContext(staging, manifest);
            var image = new Texture2D(128, 64, TextureFormat.RGB24, false);
            var decoded = new Texture2D(2, 2);
            try
            {
                var pixels = Enumerable.Range(0, 128 * 64).Select(i => i % 128 < 64 ? Color.red : Color.blue).ToArray();
                image.SetPixels(pixels); image.Apply(); context.SaveFrame(image, "frame", "Tick 10");
                var shot = manifest.Shots.Single(); var bytes = File.ReadAllBytes(Path.Combine(staging, shot.File));
                Assert.That(shot.File, Is.EqualTo("frame.jpg"));
                Assert.That(bytes[0], Is.EqualTo(0xff)); Assert.That(bytes[1], Is.EqualTo(0xd8));
                Assert.That(shot.Sha256, Is.EqualTo(EvidencePublisher.Sha256Of(bytes)));
                Assert.That(manifest.Info.Single(entry => entry.Key == "imageEncoding").Value, Is.EqualTo("JPEG (quality 85)"));
                Assert.That(decoded.LoadImage(bytes), Is.True);
                Assert.That(decoded.width, Is.EqualTo(128)); Assert.That(decoded.height, Is.EqualTo(64));
                Assert.That(decoded.GetPixel(16, 32).r, Is.GreaterThan(.9f));
                Assert.That(decoded.GetPixel(112, 32).b, Is.GreaterThan(.9f));
                File.WriteAllText(Path.Combine(staging, EvidencePublisher.ManifestFileName), JsonUtility.ToJson(manifest));
                _publisher.Publish(staging); Assert.DoesNotThrow(() => _publisher.VerifyCurrent());
            }
            finally { UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(decoded); }
        }

        [Test]
        public void ExistingGenerationWhoseContentDoesNotMatchItsName_IsNotReused()
        {
            // 手作業のコピーなどで、B の世代名のフォルダに別の画像（A）が入ってしまった状態。
            var a = _publisher.Publish(Stage(_publisher, SetA, "build-A"));
            var generationB = EvidencePublisher.GenerationIdOf(ManifestOf(SetB));
            CopyDirectory(_publisher.GenerationDirectory(a.Generation), _publisher.GenerationDirectory(generationB));

            Assert.Throws<InvalidOperationException>(() => _publisher.Publish(Stage(_publisher, SetB, "build-B")));
            Assert.That(_publisher.Current.Generation, Is.EqualTo(a.Generation));
            Assert.DoesNotThrow(() => _publisher.VerifyCurrent());
        }

        [Test]
        public void SetThatDoesNotMatchManifest_IsRejected_AndPointerIsUnchanged()
        {
            var published = _publisher.Publish(Stage(_publisher, SetA, "build-A"));

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

            Assert.That(_publisher.Current.Run, Is.EqualTo(published.Run));
            Assert.DoesNotThrow(() => _publisher.VerifyCurrent());

            void AssertRejected(Action<string> corrupt, string reason)
            {
                var staging = Stage(_publisher, SetB, "build-B");
                corrupt(staging);
                Assert.Throws<InvalidOperationException>(() => _publisher.Publish(staging), reason);
                Directory.Delete(staging, recursive: true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailureAtAnyFileOperation_LeavesPointerOnACompletePair(bool sameImages)
        {
            // A（build-A）が公開中のところに、新しい撮影（build-B）を公開する。sameImages なら画像は A と同じ（世代を使い回す経路）。
            // 状態を変える操作（撮影フォルダ作成、記録の移動、画像の移動または一時フォルダ削除、ポインタの書き込み・置き換え、
            // 古い世代・撮影記録の削除）のそれぞれの直前で失敗させる。例外のまま後始末をしないので、その時点で止まったのと同じ状態になる。
            var newSet = sameImages ? SetA : SetB;
            var operations = CountOperations(newSet);
            Assert.That(operations, Is.GreaterThanOrEqualTo(sameImages ? 6 : 7));
            for (var failAt = 0; failAt < operations; failAt++)
            {
                var evidence = NewEvidenceDirectory();
                try
                {
                    var a = Publish(new EvidencePublisher(evidence), SetA, "build-A");
                    var faulty = new FaultyOps(failAt);
                    var publisher = new EvidencePublisher(evidence, faulty);
                    PublishResult b = null;
                    try
                    {
                        b = publisher.Publish(Stage(publisher, newSet, "build-B"));
                    }
                    catch (IOException exception) when (exception.Message == FaultyOps.Message)
                    {
                    }

                    Assert.That(faulty.Failed, Is.True, $"{failAt} 回目の操作で失敗させた");
                    var expectedRun = b?.Run ?? a.Run;
                    Assert.That(publisher.Current.Run, Is.EqualTo(expectedRun), $"{failAt} 回目で失敗: ポインタは A か B のどちらか");
                    Assert.That(publisher.VerifyCurrent().BuildGuid, Is.EqualTo(b == null ? "build-A" : "build-B"), $"{failAt} 回目で失敗: 指している組は完全");

                    // 次の公開で、残ったゴミ（指されていない世代・撮影記録・一時ファイル）も片付く。
                    var clean = new EvidencePublisher(evidence);
                    var republished = Publish(clean, newSet, "build-C");
                    Assert.That(Names(Directory.GetDirectories(clean.GenerationsDirectory)), Is.EqualTo(new[] { republished.Generation }));
                    Assert.That(Names(Directory.GetDirectories(clean.RunsDirectory)), Is.EqualTo(new[] { republished.Run }));
                    Assert.That(Names(Directory.GetFiles(evidence)), Is.EqualTo(new[] { EvidencePublisher.PointerFileName }));
                }
                finally
                {
                    Directory.Delete(evidence, recursive: true);
                }
            }
        }

        [Test]
        public void StaleStagingFromKilledCapture_IsRemoved_ButFreshStagingIsKept()
        {
            var stale = Stage(_publisher, SetB, "killed");
            Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow - EvidencePublisher.StaleStagingAge - TimeSpan.FromMinutes(1));
            var fresh = _publisher.CreateStaging(); // 撮影中の別プロセスのもの

            _publisher.Publish(Stage(_publisher, SetA, "build-A"));

            Assert.That(Directory.Exists(stale), Is.False);
            Assert.That(Directory.Exists(fresh), Is.True);
        }

        [Test]
        public void ConcurrentCaptures_UseSeparateStaging_AndDoNotPublishAtTheSameTime()
        {
            var published = _publisher.Publish(Stage(_publisher, SetA, "build-A"));
            Assert.That(_publisher.CreateStaging(), Is.Not.EqualTo(_publisher.CreateStaging()));

            var staging = Stage(_publisher, SetB, "build-B");
            var lockPath = Path.Combine(_evidence, EvidencePublisher.LockFileName);
            using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                Assert.Throws<IOException>(() => _publisher.Publish(staging));
            }

            Assert.That(_publisher.Current.Run, Is.EqualTo(published.Run));
        }

        [Test]
        public void Migration_SuccessUpdatesReadmeBeforeRemovingOldGeneration()
        {
            var old = StartPngEvidence(_evidence);
            var migrated = EvidenceJpegMigration.Convert(_evidence);
            Assert.That(_publisher.VerifyCurrent().BuildGuid, Is.EqualTo("original-build"));
            Assert.That(_publisher.VerifyCurrent().CapturedAtUtc, Is.EqualTo("2026-11-15T00:00:00Z"));
            _publisher.VerifyReadmeLinks();
            Assert.That(File.ReadAllText(Path.Combine(_evidence, "README.md")), Does.Contain("title.jpg"));
            Assert.That(Directory.Exists(_publisher.GenerationDirectory(old.Generation)), Is.False);
            Assert.That(Names(Directory.GetDirectories(_publisher.RunsDirectory)), Is.EqualTo(new[] { migrated.Run }));
            Assert.That(EvidenceJpegMigration.Convert(_evidence), Is.Null, "JPEGを繰り返し再圧縮しない");
            AssertNoMigrationTemps(_evidence);
        }

        [Test]
        public void Migration_EncoderFailureCleansPartialStagingAndKeepsOldLinks()
        {
            var old = StartPngEvidence(_evidence);
            Assert.Throws<IOException>(() => EvidenceJpegMigration.Convert(_evidence, convertFrame: (context, source, shot) =>
            {
                var texture = new Texture2D(2, 2);
                try { texture.LoadImage(File.ReadAllBytes(source)); context.SaveFrame(texture, "partial", shot.Note); }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
                throw new IOException("encode failure");
            }));
            Assert.That(_publisher.Current.Run, Is.EqualTo(old.Run));
            _publisher.VerifyCurrent(); _publisher.VerifyReadmeLinks(); AssertNoMigrationTemps(_evidence);
        }

        [TestCase("images")]
        [TestCase("README.md")]
        [TestCase("current.json")]
        public void Migration_FailureKeepsCurrentAndAllReadmeLinks_AndRetryFinishes(string target)
        {
            var old = StartPngEvidence(_evidence);
            Assert.Throws<IOException>(() => EvidenceJpegMigration.Convert(_evidence, new FailingMigrationOps(target)));
            Assert.That(_publisher.Current.Run, Is.EqualTo(old.Run));
            _publisher.VerifyCurrent(); _publisher.VerifyReadmeLinks(); AssertNoMigrationTemps(_evidence);
            if (target == "README.md")
            { Assert.That(Directory.GetDirectories(_publisher.GenerationsDirectory).Length, Is.EqualTo(2), "JPEG配置後のREADME置換に失敗した"); }
            var migrated = EvidenceJpegMigration.Convert(_evidence);
            _publisher.VerifyCurrent(); _publisher.VerifyReadmeLinks(); AssertNoMigrationTemps(_evidence);
            Assert.That(Names(Directory.GetDirectories(_publisher.GenerationsDirectory)), Is.EqualTo(new[] { migrated.Generation }));
            Assert.That(Names(Directory.GetDirectories(_publisher.RunsDirectory)), Is.EqualTo(new[] { migrated.Run }));
        }

        [Test]
        public void InterruptedMigration_ReadmeReferencesAreProtectedByNextOrdinaryPublish()
        {
            StartPngEvidence(_evidence);
            Assert.Throws<IOException>(() => EvidenceJpegMigration.Convert(_evidence, new FailingMigrationOps("current.json")));
            var readme = File.ReadAllText(Path.Combine(_evidence, "README.md"));
            Publish(_publisher, SetB, "later-capture");
            Assert.That(File.ReadAllText(Path.Combine(_evidence, "README.md")), Is.EqualTo(readme));
            _publisher.VerifyCurrent(); _publisher.VerifyReadmeLinks();
        }

        [Test]
        public void Migration_FailureAtEveryPublishingOperation_KeepsCurrentAndReadmeComplete()
        {
            StartPngEvidence(_evidence);
            var counter = new FaultyOps(int.MaxValue);
            EvidenceJpegMigration.Convert(_evidence, counter);
            Assert.That(counter.Calls, Is.GreaterThanOrEqualTo(9));
            for (var failAt = 0; failAt < counter.Calls; failAt++)
            {
                var evidence = NewEvidenceDirectory();
                try
                {
                    StartPngEvidence(evidence);
                    var faulty = new FaultyOps(failAt);
                    try { EvidenceJpegMigration.Convert(evidence, faulty); }
                    catch (IOException exception) when (exception.Message == FaultyOps.Message) { }
                    Assert.That(faulty.Failed, Is.True, "操作 " + failAt);
                    var publisher = new EvidencePublisher(evidence);
                    publisher.VerifyCurrent(); publisher.VerifyReadmeLinks(); AssertNoMigrationTemps(evidence);
                }
                finally { Directory.Delete(evidence, recursive: true); }
            }
        }

        private static PublishResult StartPngEvidence(string evidence)
        {
            var publisher = new EvidencePublisher(evidence); var staging = publisher.CreateStaging();
            var image = new Texture2D(16, 16, TextureFormat.RGB24, false);
            byte[] bytes;
            try { image.SetPixels(Enumerable.Repeat(Color.green, 256).ToArray()); image.Apply(); bytes = image.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            File.WriteAllBytes(Path.Combine(staging, "title.png"), bytes);
            var manifest = new CaptureManifest { BuildGuid = "original-build", CapturedAtUtc = "2026-11-15T00:00:00Z" };
            manifest.Shots.Add(new CaptureManifest.Shot { File = "title.png", Sha256 = EvidencePublisher.Sha256Of(bytes), Note = "Tick 10" });
            WriteManifest(staging, manifest); var published = publisher.Publish(staging);
            File.WriteAllText(Path.Combine(evidence, "README.md"), $"[画面](captures/{published.Generation}/title.png)\n[記録](runs/{published.Run}/manifest.json)\n");
            return published;
        }

        private static void AssertNoMigrationTemps(string evidence)
        {
            Assert.That(Directory.GetDirectories(evidence, ".staging-*"), Is.Empty);
            Assert.That(Directory.GetFiles(evidence, "*.tmp-*"), Is.Empty);
        }

        private sealed class FailingMigrationOps : IEvidenceFileOps
        {
            private readonly string _target;
            public FailingMigrationOps(string target) { _target = target; }
            public void CreateDirectory(string path) => EvidenceFileOps.Instance.CreateDirectory(path);
            public void MoveDirectory(string source, string destination)
            {
                if (_target == "images") { throw new IOException("publish failure"); }
                EvidenceFileOps.Instance.MoveDirectory(source, destination);
            }
            public void MoveFile(string source, string destination) => EvidenceFileOps.Instance.MoveFile(source, destination);
            public void WriteFile(string path, string contents) => EvidenceFileOps.Instance.WriteFile(path, contents);
            public void ReplaceFile(string source, string destination)
            {
                if (Path.GetFileName(destination) == _target) { throw new IOException("replace failure: " + _target); }
                EvidenceFileOps.Instance.ReplaceFile(source, destination);
            }
            public void DeleteDirectory(string path) => EvidenceFileOps.Instance.DeleteDirectory(path);
        }

        private static int CountOperations((string File, string Content)[] newSet)
        {
            var evidence = NewEvidenceDirectory();
            try
            {
                Publish(new EvidencePublisher(evidence), SetA, "build-A");
                var counter = new FaultyOps(int.MaxValue);
                var publisher = new EvidencePublisher(evidence, counter);
                publisher.Publish(Stage(publisher, newSet, "build-B"));
                return counter.Calls;
            }
            finally
            {
                Directory.Delete(evidence, recursive: true);
            }
        }

        private static PublishResult Publish(EvidencePublisher publisher, (string File, string Content)[] files, string build) =>
            publisher.Publish(Stage(publisher, files, build));

        private static string NewEvidenceDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "evidence-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        // 撮影と同じく、一時フォルダに画像と、それを記録した manifest.json を書く。
        private static string Stage(EvidencePublisher publisher, (string File, string Content)[] files, string build, string capturedAt = "2026-11-15T00:00:00Z")
        {
            var staging = publisher.CreateStaging();
            foreach (var (file, content) in files)
            {
                File.WriteAllText(Path.Combine(staging, file), content);
            }

            var manifest = ManifestOf(files);
            manifest.BuildGuid = build;
            manifest.CapturedAtUtc = capturedAt;
            WriteManifest(staging, manifest);
            return staging;
        }

        private static CaptureManifest ManifestOf((string File, string Content)[] files)
        {
            var manifest = new CaptureManifest();
            manifest.Shots.AddRange(files.Select(entry => Shot(entry.File, entry.Content)));
            return manifest;
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

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }

        private static string[] Names(string[] paths) => paths.Select(Path.GetFileName).ToArray();

        private static string[] CurrentImages(EvidencePublisher publisher) =>
            Names(Directory.GetFiles(publisher.GenerationDirectory(publisher.Current.Generation)));

        private static string ReadCurrent(EvidencePublisher publisher, string file) =>
            File.ReadAllText(Path.Combine(publisher.GenerationDirectory(publisher.Current.Generation), file));

        // 実際のファイル操作をしつつ、指定した回数目の操作だけ実行せずに失敗させる。
        private sealed class FaultyOps : IEvidenceFileOps
        {
            public const string Message = "injected fault";

            private readonly int _failAt;

            public FaultyOps(int failAt)
            {
                _failAt = failAt;
            }

            public int Calls { get; private set; }

            public bool Failed { get; private set; }

            public void CreateDirectory(string path) => Run(() => EvidenceFileOps.Instance.CreateDirectory(path));

            public void MoveDirectory(string source, string destination) => Run(() => EvidenceFileOps.Instance.MoveDirectory(source, destination));

            public void MoveFile(string source, string destination) => Run(() => EvidenceFileOps.Instance.MoveFile(source, destination));

            public void WriteFile(string path, string contents) => Run(() => EvidenceFileOps.Instance.WriteFile(path, contents));

            public void ReplaceFile(string source, string destination) => Run(() => EvidenceFileOps.Instance.ReplaceFile(source, destination));

            public void DeleteDirectory(string path) => Run(() => EvidenceFileOps.Instance.DeleteDirectory(path));

            private void Run(Action operation)
            {
                if (Calls++ == _failAt)
                {
                    Failed = true;
                    throw new IOException(Message);
                }

                operation();
            }
        }
    }
}
