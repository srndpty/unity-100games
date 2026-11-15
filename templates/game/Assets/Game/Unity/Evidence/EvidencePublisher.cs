using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Game.Unity.Evidence
{
    /// <summary>
    /// 証跡一式を世代ごとの不変フォルダ <c>evidence/captures/&lt;世代&gt;/</c> に置き、公開中の世代を
    /// ポインタファイル <c>evidence/current.json</c> で指す。手書きの <c>evidence/README.md</c> とは分ける。
    /// <para>
    /// 公開の手順（<see cref="Publish"/>）と、どこで止まっても成り立つこと：
    /// <list type="number">
    /// <item>撮影ごとの一時フォルダで manifest.json と画像を検証する。失敗したら何も変えない。</item>
    /// <item>一時フォルダを新しい世代フォルダへ名前を付け替える（新しいパスへの原子的な移動。公開中の世代には触れない）。</item>
    /// <item>新しいポインタを一時ファイルに書き、<c>current.json</c> と置き換える（原子的）。ここで初めて公開が切り替わる。</item>
    /// <item>どこからも指されていない世代を消す。失敗しても公開には影響しない（次回の公開で再び消す）。</item>
    /// </list>
    /// ポインタは常に「検証済みで完全な世代」を指す。強制終了しても、残るのは指されていない世代や一時ファイル（ゴミ）だけ。
    /// </para>
    /// <para>
    /// 世代名は画像の内容（ファイル名と SHA-256）から決める。同じ画像を撮り直しても世代は変わらず、README の画像リンクが保たれる。
    /// 公開の間はロックファイルを排他で開き、別の撮影の公開と重ならないようにする。
    /// </para>
    /// </summary>
    public sealed class EvidencePublisher
    {
        public const string GenerationsFolderName = "captures";
        public const string PointerFileName = "current.json";
        public const string ManifestFileName = "manifest.json";
        public const string LockFileName = ".publish.lock";

        private const string StagingPrefix = ".staging-";
        private const int GenerationIdLength = 16;

        private readonly string _evidenceDirectory;
        private readonly IEvidenceFileOps _ops;

        public EvidencePublisher(string evidenceDirectory, IEvidenceFileOps ops = null)
        {
            _evidenceDirectory = Path.GetFullPath(evidenceDirectory);
            _ops = ops ?? EvidenceFileOps.Instance;
        }

        public string GenerationsDirectory => Path.Combine(_evidenceDirectory, GenerationsFolderName);

        public string PointerPath => Path.Combine(_evidenceDirectory, PointerFileName);

        /// <summary>公開中の世代。まだ一度も公開していなければ null。</summary>
        public string CurrentGeneration =>
            File.Exists(PointerPath) ? JsonUtility.FromJson<Pointer>(File.ReadAllText(PointerPath)).Generation : null;

        public string GenerationDirectory(string generation) => Path.Combine(GenerationsDirectory, generation);

        /// <summary>この撮影専用の一時フォルダを作る。世代フォルダへの付け替えが同じボリューム内で済むよう、evidence/ の下に作る。</summary>
        public string CreateStaging()
        {
            var staging = Path.Combine(_evidenceDirectory, StagingPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            return staging;
        }

        /// <summary>
        /// 一時フォルダの一式を検証して公開する。成功すると一時フォルダは世代フォルダになる（同じ内容の世代が既にあれば一時フォルダは消す）。
        /// 失敗したら例外を投げ、公開中の世代は変わらない（一時フォルダの後始末は呼び出し側）。
        /// </summary>
        public PublishResult Publish(string staging)
        {
            var manifest = Verify(staging);
            var generation = GenerationIdOf(manifest);

            using var publishLock = AcquireLock();
            var previous = CurrentGeneration;
            Directory.CreateDirectory(GenerationsDirectory);
            var target = GenerationDirectory(generation);
            if (Directory.Exists(target))
            {
                Verify(target); // 既にある同じ内容の世代を使う。壊れていたら公開しない
                _ops.DeleteDirectory(staging);
            }
            else
            {
                _ops.MoveDirectory(staging, target);
            }

            var pointerTemp = PointerPath + ".tmp-" + Guid.NewGuid().ToString("N");
            _ops.WriteFile(pointerTemp, JsonUtility.ToJson(new Pointer { Generation = generation }, prettyPrint: true) + "\n");
            _ops.ReplaceFile(pointerTemp, PointerPath);

            return new PublishResult(generation, previous, RemoveUnreferencedGenerations(generation));
        }

        /// <summary>
        /// 一式を manifest.json の内容と照合する。画像は manifest の Shots と過不足なく一致し、ファイル名は重複・パスを含まず、
        /// SHA-256 も一致すること。サブフォルダは置かない。
        /// </summary>
        public static CaptureManifest Verify(string directory)
        {
            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException($"{ManifestFileName} がありません: {directory}");
            }

            CaptureManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<CaptureManifest>(File.ReadAllText(manifestPath));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException($"{ManifestFileName} を読めません: {exception.Message}", exception);
            }

            if (manifest?.Shots == null || manifest.Shots.Count == 0)
            {
                throw new InvalidOperationException($"{ManifestFileName} に撮影記録（Shots）がありません");
            }

            var names = manifest.Shots.Select(shot => shot.File).ToArray();
            var invalid = names.Where(name => string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || name == "." || name == ".."
                || name.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (invalid.Length > 0)
            {
                throw new InvalidOperationException($"撮影記録のファイル名が不正です: [{string.Join(", ", invalid)}]");
            }

            var duplicates = names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            if (duplicates.Length > 0)
            {
                throw new InvalidOperationException($"撮影記録のファイル名が重複しています: [{string.Join(", ", duplicates)}]");
            }

            var expected = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase) { ManifestFileName };
            var actual = Directory.GetFiles(directory).Select(Path.GetFileName).ToArray();
            var missing = expected.Where(name => !actual.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
            var unexpected = actual.Where(name => !expected.Contains(name)).ToArray();
            if (missing.Length > 0 || unexpected.Length > 0 || Directory.GetDirectories(directory).Length > 0)
            {
                throw new InvalidOperationException(
                    $"証跡一式が {ManifestFileName} と一致しません。不足: [{string.Join(", ", missing)}] 余計: [{string.Join(", ", unexpected)}]");
            }

            foreach (var shot in manifest.Shots)
            {
                var actualHash = Sha256Of(File.ReadAllBytes(Path.Combine(directory, shot.File)));
                if (!string.Equals(actualHash, shot.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"証跡のハッシュが {ManifestFileName} と一致しません: {shot.File}");
                }
            }

            return manifest;
        }

        /// <summary>世代名。画像のファイル名と SHA-256 の組（ファイル名順）から決める。</summary>
        public static string GenerationIdOf(CaptureManifest manifest)
        {
            var lines = manifest.Shots
                .OrderBy(shot => shot.File, StringComparer.Ordinal)
                .Select(shot => $"{shot.File}:{shot.Sha256.ToLowerInvariant()}\n");
            return Sha256Of(Encoding.UTF8.GetBytes(string.Concat(lines))).Substring(0, GenerationIdLength);
        }

        public static string Sha256Of(byte[] data)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }

        // ポインタが指す世代以外を消す。前回までに強制終了で残った世代やポインタの一時ファイルもここで片付く。失敗は警告にとどめる。
        private IReadOnlyList<string> RemoveUnreferencedGenerations(string current)
        {
            var warnings = new List<string>();
            foreach (var staleTemp in Directory.GetFiles(_evidenceDirectory, PointerFileName + ".tmp-*"))
            {
                try
                {
                    File.Delete(staleTemp);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    warnings.Add($"ポインタの一時ファイルを消せませんでした: {staleTemp}: {exception.Message}");
                }
            }

            foreach (var directory in Directory.GetDirectories(GenerationsDirectory))
            {
                if (string.Equals(Path.GetFileName(directory), current, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    _ops.DeleteDirectory(directory);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    warnings.Add($"使われていない世代を消せませんでした（公開は完了。次回の公開で再び消す）: {directory}: {exception.Message}");
                }
            }

            return warnings;
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

        [Serializable]
        internal sealed class Pointer
        {
            public string Generation = string.Empty;
        }
    }

    public sealed class PublishResult
    {
        public PublishResult(string generation, string previousGeneration, IReadOnlyList<string> warnings)
        {
            Generation = generation;
            PreviousGeneration = previousGeneration;
            Warnings = warnings;
        }

        public string Generation { get; }

        /// <summary>公開前に公開中だった世代。初回は null。</summary>
        public string PreviousGeneration { get; }

        /// <summary>公開は済んだが片付けられなかったものの警告。</summary>
        public IReadOnlyList<string> Warnings { get; }
    }
}
