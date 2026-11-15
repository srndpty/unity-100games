using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Game.Unity.Evidence
{
    /// <summary>
    /// 証跡一式を <c>evidence/capture/</c> に公開する。手書きの <c>evidence/README.md</c> とは分け、生成物だけをこのフォルダに置く。
    /// <list type="number">
    /// <item>撮影ごとに専用の一時フォルダ（<see cref="CreateStaging"/>）へ書く。同時に撮影しても一時フォルダはぶつからない。</item>
    /// <item><see cref="Publish"/> で中身を検証し（記録したファイルがすべてあり、ハッシュが一致し、余計なファイルがない）、
    /// 公開中のフォルダと一時フォルダをフォルダ名の付け替えだけで入れ替える。</item>
    /// </list>
    /// ファイル単位のコピーをしないので、<c>capture/</c> は常に「古い一式」か「新しい一式」のどちらかで、混ざらない。
    /// 前回だけにあったファイルも一式ごと消える。入れ替えに失敗したら古い一式に戻す。
    /// 公開の間はロックファイルを排他で開き、別の撮影の公開と重ならないようにする。
    /// Unity に依存しない（ファイル操作だけ）。
    /// </summary>
    public sealed class EvidencePublisher
    {
        public const string PublishedFolderName = "capture";
        public const string ManifestFileName = "manifest.json";
        public const string LockFileName = ".publish.lock";

        private const string StagingPrefix = ".staging-";
        private const string RetiredPrefix = ".retired-";

        private readonly string _evidenceDirectory;

        public EvidencePublisher(string evidenceDirectory)
        {
            _evidenceDirectory = Path.GetFullPath(evidenceDirectory);
        }

        public string PublishedDirectory => Path.Combine(_evidenceDirectory, PublishedFolderName);

        /// <summary>この撮影専用の一時フォルダを作る。入れ替えをフォルダ名の変更で済ませるため、公開先と同じ場所（evidence/ の下）に作る。</summary>
        public string CreateStaging()
        {
            var staging = Path.Combine(_evidenceDirectory, StagingPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            return staging;
        }

        /// <summary>
        /// 一時フォルダの中身を検証して公開する。<paramref name="expectedFiles"/> はファイル名と SHA-256（16 進小文字）の組。
        /// manifest.json は必須で、ハッシュは照合しない（中に他のファイルのハッシュを持つため）。
        /// 成功すると一時フォルダは公開フォルダになる。失敗したら例外を投げ、公開中の一式は変わらない（一時フォルダの後始末は呼び出し側）。
        /// 戻り値は、公開は済んだが片付けられなかったものの警告。
        /// </summary>
        public IReadOnlyList<string> Publish(string staging, IReadOnlyCollection<(string File, string Sha256)> expectedFiles)
        {
            Verify(staging, expectedFiles);

            using var publishLock = AcquireLock();
            var published = PublishedDirectory;
            string retired = null;
            if (Directory.Exists(published))
            {
                retired = Path.Combine(_evidenceDirectory, RetiredPrefix + Guid.NewGuid().ToString("N"));
                Directory.Move(published, retired);
            }

            try
            {
                Directory.Move(staging, published);
            }
            catch
            {
                if (retired != null)
                {
                    Directory.Move(retired, published);
                }

                throw;
            }

            var warnings = new List<string>();
            if (retired != null)
            {
                try
                {
                    Directory.Delete(retired, recursive: true);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    warnings.Add($"古い証跡を消せませんでした（公開は完了）: {retired}: {exception.Message}");
                }
            }

            return warnings;
        }

        public static string Sha256Of(byte[] data)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void Verify(string staging, IReadOnlyCollection<(string File, string Sha256)> expectedFiles)
        {
            var expected = new HashSet<string>(expectedFiles.Select(entry => entry.File), StringComparer.OrdinalIgnoreCase) { ManifestFileName };
            var actual = Directory.GetFiles(staging).Select(Path.GetFileName).ToArray();
            var missing = expected.Where(file => !actual.Contains(file, StringComparer.OrdinalIgnoreCase)).ToArray();
            var unexpected = actual.Where(file => !expected.Contains(file)).ToArray();
            if (missing.Length > 0 || unexpected.Length > 0 || Directory.GetDirectories(staging).Length > 0)
            {
                throw new InvalidOperationException(
                    $"証跡一式が記録と一致しません。不足: [{string.Join(", ", missing)}] 余計: [{string.Join(", ", unexpected)}]");
            }

            foreach (var (file, sha256) in expectedFiles)
            {
                var actualHash = Sha256Of(File.ReadAllBytes(Path.Combine(staging, file)));
                if (!string.Equals(actualHash, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"証跡のハッシュが記録と一致しません: {file}");
                }
            }
        }

        private FileStream AcquireLock()
        {
            var path = Path.Combine(_evidenceDirectory, LockFileName);
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException exception)
            {
                throw new IOException($"別の撮影が証跡を公開中です: {path}", exception);
            }
        }
    }
}
