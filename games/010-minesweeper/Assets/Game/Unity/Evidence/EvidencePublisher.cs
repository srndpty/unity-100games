using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Game.Unity.Evidence
{
    /// <summary>
    /// 証跡を公開する。「画像の内容」と「撮影の実行記録」を分けて置き、公開中の組をポインタで指す。手書きの <c>evidence/README.md</c> とは分ける。
    /// <list type="bullet">
    /// <item><c>evidence/captures/&lt;画像世代&gt;/</c>：画像だけを置く不変フォルダ。名前は中身（ファイル名と SHA-256）から決まり、
    /// 同じ画像なら撮り直しても同じ世代を使い回す（README の画像リンクが保たれる）。名前と中身の対応はいつでも検算できる。</item>
    /// <item><c>evidence/runs/&lt;撮影 ID&gt;/manifest.json</c>：撮影ごとの記録（ビルド、日時、シード、設定、画像のハッシュ）。撮影のたびに新しく作る。</item>
    /// <item><c>evidence/current.json</c>：公開中の画像世代と撮影 ID の組。置き換え（原子的）だけで公開を切り替える。</item>
    /// </list>
    /// <para>
    /// 公開の手順（<see cref="Publish"/>）：一時フォルダの manifest と画像を照合 → 実行記録を新しい撮影フォルダへ移す →
    /// 画像を新しい世代フォルダへ移す（同じ世代があれば検算して使い回す）→ README更新があれば参照先を検証して原子的に置き換える → ポインタを置き換える → ポインタとREADMEの双方から参照されない世代・撮影記録・残骸を消す。
    /// ポインタを置き換えるまでは公開中のものに触れず、古いものは置き換えた後に消すので、操作と操作の間のどこで止まっても
    /// ポインタは検証済みで完全な組を指す。READMEとポインタの置換は別操作なので、中断時は異なる世代を参照し得るが、双方のリンク先を保持する。
    /// 再実行で参照を揃え、参照されなくなったものは次の公開で片付く。電源断を含む永続性は保証しない。
    /// 公開の間はロックファイルを排他で開き、別の撮影の公開と重ならないようにする。
    /// </para>
    /// </summary>
    public sealed class EvidencePublisher
    {
        public const string GenerationsFolderName = "captures";
        public const string RunsFolderName = "runs";
        public const string PointerFileName = "current.json";
        public const string ManifestFileName = "manifest.json";
        public const string LockFileName = ".publish.lock";

        /// <summary>これより古い一時フォルダは、撮影が強制終了して残ったものとみなして消す（dev capture は 120 秒で打ち切る）。</summary>
        public static readonly TimeSpan StaleStagingAge = TimeSpan.FromHours(1);

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

        public string RunsDirectory => Path.Combine(_evidenceDirectory, RunsFolderName);

        public string PointerPath => Path.Combine(_evidenceDirectory, PointerFileName);

        /// <summary>公開中の画像世代と撮影 ID。まだ一度も公開していなければ null。</summary>
        public EvidencePointer Current =>
            File.Exists(PointerPath) ? JsonUtility.FromJson<EvidencePointer>(File.ReadAllText(PointerPath)) : null;

        public string GenerationDirectory(string generation) => Path.Combine(GenerationsDirectory, generation);

        public string RunManifestPath(string run) => Path.Combine(RunsDirectory, run, ManifestFileName);

        /// <summary>この撮影専用の一時フォルダを作る。世代フォルダへの付け替えが同じボリューム内で済むよう、evidence/ の下に作る。</summary>
        public string CreateStaging()
        {
            var staging = Path.Combine(_evidenceDirectory, StagingPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            return staging;
        }

        /// <summary>
        /// 一時フォルダの一式（画像と manifest.json）を検証して公開する。成功すると一時フォルダはなくなる。
        /// 失敗したら例外を投げる。ポインタ置換後の失敗では新しい組が公開済みの場合があるが、参照先の完全性を保つ。
        /// 一時フォルダが残っていれば後始末は呼び出し側。
        /// </summary>
        public PublishResult Publish(string staging) => Publish(staging, null);

        /// <summary>
        /// READMEの更新を伴う公開。新画像を配置した後、READMEを原子的に置き換えてからポインタを切り替える。
        /// 途中停止でREADMEが新世代、ポインタが旧世代になっても、双方の参照先を保持する。
        /// </summary>
        public PublishResult Publish(string staging, Func<EvidencePointer, string> prepareReadme)
        {
            var manifest = Verify(staging);
            var generation = GenerationIdOf(manifest);
            var run = $"{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            using var publishLock = AcquireLock();
            var previous = Current;
            var pointer = new EvidencePointer { Generation = generation, Run = run };
            var readme = prepareReadme?.Invoke(pointer);
            var readmePath = Path.Combine(_evidenceDirectory, "README.md");
            string readmeTemp = null;
            string pointerTemp = null;
            try
            {
                if (readme != null)
                {
                    VerifyReadmeLinks(readme, pointer, staging);
                    readmeTemp = readmePath + ".tmp-" + Guid.NewGuid().ToString("N");
                    _ops.WriteFile(readmeTemp, readme);
                }
                Directory.CreateDirectory(GenerationsDirectory);
                Directory.CreateDirectory(RunsDirectory);
                var runDirectory = Path.Combine(RunsDirectory, run);
                _ops.CreateDirectory(runDirectory);
                _ops.MoveFile(Path.Combine(staging, ManifestFileName), RunManifestPath(run));
                var target = GenerationDirectory(generation);
                if (Directory.Exists(target))
                {
                    VerifyGeneration(target);
                    _ops.DeleteDirectory(staging);
                }
                else { _ops.MoveDirectory(staging, target); }
                if (readme != null)
                {
                    VerifyReadmeLinks(readme);
                    _ops.ReplaceFile(readmeTemp, readmePath);
                }
                pointerTemp = PointerPath + ".tmp-" + Guid.NewGuid().ToString("N");
                _ops.WriteFile(pointerTemp, JsonUtility.ToJson(pointer, prettyPrint: true) + "\n");
                _ops.ReplaceFile(pointerTemp, PointerPath);
                VerifyCurrent();
                if (readme != null) { VerifyReadmeLinks(); }
                return new PublishResult(generation, run, previous, RemoveUnreferenced(generation, run));
            }
            finally { RemoveTemporaryFile(readmeTemp); RemoveTemporaryFile(pointerTemp); }
        }

        /// <summary>READMEのローカルリンクがすべて実在することを確認する。</summary>
        public void VerifyReadmeLinks()
        {
            var readme = Path.Combine(_evidenceDirectory, "README.md");
            if (File.Exists(readme)) { VerifyReadmeLinks(File.ReadAllText(readme)); }
        }

        private void VerifyReadmeLinks(string contents, EvidencePointer prepared = null, string staging = null)
        {
            foreach (var link in LocalLinks(contents))
            {
                var path = Path.GetFullPath(Path.Combine(_evidenceDirectory, link));
                if (prepared != null)
                {
                    var future = Path.GetFullPath(GenerationDirectory(prepared.Generation)) + Path.DirectorySeparatorChar;
                    if (path.StartsWith(future, StringComparison.OrdinalIgnoreCase))
                    { path = Path.Combine(staging, path.Substring(future.Length)); }
                    else if (string.Equals(path, Path.GetFullPath(RunManifestPath(prepared.Run)), StringComparison.OrdinalIgnoreCase))
                    { path = Path.Combine(staging, ManifestFileName); }
                }
                if (!File.Exists(path) && !Directory.Exists(path))
                { throw new InvalidOperationException("READMEのリンク先がありません: " + link); }
            }
        }

        private static IEnumerable<string> LocalLinks(string contents) =>
            Regex.Matches(contents, @"\]\((?!https?:|#)([^)\s#]+)").Cast<Match>().Select(match => match.Groups[1].Value);

        private static void RemoveTemporaryFile(string path)
        {
            if (path == null) { return; }
            try { if (File.Exists(path)) { File.Delete(path); } }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { Debug.LogWarning("一時ファイルを消せませんでした: " + path + ": " + exception.Message); }
        }

        /// <summary>
        /// 公開中の組を検証して、その実行記録を返す。画像世代は名前と中身が一致し、実行記録の画像ハッシュはその世代と一致すること。
        /// </summary>
        public CaptureManifest VerifyCurrent()
        {
            var current = Current ?? throw new InvalidOperationException($"{PointerFileName} がありません");
            VerifyGeneration(GenerationDirectory(current.Generation));
            var manifest = ReadManifest(RunManifestPath(current.Run));
            if (GenerationIdOf(manifest) != current.Generation)
            {
                throw new InvalidOperationException($"撮影記録 {current.Run} の画像が、公開中の世代 {current.Generation} と一致しません");
            }

            return manifest;
        }

        /// <summary>
        /// 一時フォルダの一式を manifest.json の内容と照合する。画像は manifest の Shots と過不足なく一致し、ファイル名は重複・パスを含まず、
        /// SHA-256 も一致すること。サブフォルダは置かない。
        /// </summary>
        public static CaptureManifest Verify(string directory)
        {
            var manifest = ReadManifest(Path.Combine(directory, ManifestFileName));
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

        /// <summary>画像世代のフォルダを検算する。画像だけを置き、フォルダ名が中身から計算した世代名と一致すること。</summary>
        public static void VerifyGeneration(string directory)
        {
            var files = Directory.GetFiles(directory);
            if (files.Length == 0 || Directory.GetDirectories(directory).Length > 0)
            {
                throw new InvalidOperationException($"画像世代のフォルダは画像だけを含む必要があります: {directory}");
            }

            var actual = GenerationIdOf(files.Select(file => (Path.GetFileName(file), Sha256Of(File.ReadAllBytes(file)))));
            if (actual != Path.GetFileName(directory))
            {
                throw new InvalidOperationException($"画像世代の名前と中身が一致しません: {directory}（中身から計算した世代は {actual}）");
            }
        }

        /// <summary>画像世代の名前。画像のファイル名と SHA-256 の組（ファイル名順）から決める。</summary>
        public static string GenerationIdOf(CaptureManifest manifest) =>
            GenerationIdOf(manifest.Shots.Select(shot => (shot.File, shot.Sha256)));

        public static string Sha256Of(byte[] data)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string GenerationIdOf(IEnumerable<(string File, string Sha256)> images)
        {
            var lines = images
                .OrderBy(image => image.File, StringComparer.Ordinal)
                .Select(image => $"{image.File}:{image.Sha256.ToLowerInvariant()}\n");
            return Sha256Of(Encoding.UTF8.GetBytes(string.Concat(lines))).Substring(0, GenerationIdLength);
        }

        private static CaptureManifest ReadManifest(string path)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"{ManifestFileName} がありません: {path}");
            }

            CaptureManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<CaptureManifest>(File.ReadAllText(path));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException($"{ManifestFileName} を読めません: {exception.Message}", exception);
            }

            if (manifest?.Shots == null || manifest.Shots.Count == 0)
            {
                throw new InvalidOperationException($"{ManifestFileName} に撮影記録（Shots）がありません: {path}");
            }

            return manifest;
        }

        // ポインタが指す世代・撮影記録以外を消す。前回までに強制終了で残った世代、撮影記録、ポインタの一時ファイル、
        // 古い一時フォルダもここで片付く（新しい一時フォルダは撮影中の別プロセスのものかもしれないので残す）。失敗は警告にとどめる。
        private IReadOnlyList<string> RemoveUnreferenced(string generation, string run)
        {
            var warnings = new List<string>();
            void TryRemove(string path, Action remove)
            {
                try
                {
                    remove();
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    warnings.Add($"使われていないものを消せませんでした（公開は完了。次回の公開で再び消す）: {path}: {exception.Message}");
                }
            }

            // README自体を永続的な参照として扱う。README更新後・ポインタ切り替え前に停止しても、新世代を消さない。
            var readme = Path.Combine(_evidenceDirectory, "README.md");
            string[] links;
            try { links = File.Exists(readme) ? LocalLinks(File.ReadAllText(readme)).ToArray() : Array.Empty<string>(); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                warnings.Add("READMEの参照を読めないため旧世代を保持しました: " + exception.Message);
                return warnings;
            }
            bool Linked(string path) => links.Any(link =>
            {
                var target = Path.GetFullPath(Path.Combine(_evidenceDirectory, link));
                var directory = Path.GetFullPath(path);
                return target.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(target, directory, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var directory in Directory.GetDirectories(GenerationsDirectory).Where(path => !NameIs(path, generation) && !Linked(path)))
            {
                TryRemove(directory, () => _ops.DeleteDirectory(directory));
            }

            foreach (var directory in Directory.GetDirectories(RunsDirectory).Where(path => !NameIs(path, run) && !Linked(path)))
            {
                TryRemove(directory, () => _ops.DeleteDirectory(directory));
            }

            foreach (var file in Directory.GetFiles(_evidenceDirectory, PointerFileName + ".tmp-*"))
            {
                TryRemove(file, () => File.Delete(file));
            }

            var staleBefore = DateTime.UtcNow - StaleStagingAge;
            foreach (var directory in Directory.GetDirectories(_evidenceDirectory, StagingPrefix + "*")
                .Where(path => Directory.GetLastWriteTimeUtc(path) < staleBefore))
            {
                TryRemove(directory, () => _ops.DeleteDirectory(directory));
            }

            return warnings;
        }

        private static bool NameIs(string path, string name) => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase);

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

    /// <summary>evidence/current.json の中身。公開中の画像世代と撮影 ID の組。</summary>
    [Serializable]
    public sealed class EvidencePointer
    {
        public string Generation = string.Empty;
        public string Run = string.Empty;
    }

    public sealed class PublishResult
    {
        public PublishResult(string generation, string run, EvidencePointer previous, IReadOnlyList<string> warnings)
        {
            Generation = generation;
            Run = run;
            Previous = previous;
            Warnings = warnings;
        }

        public string Generation { get; }

        public string Run { get; }

        /// <summary>公開前に公開中だった組。初回は null。</summary>
        public EvidencePointer Previous { get; }

        /// <summary>公開は済んだが片付けられなかったものの警告。</summary>
        public IReadOnlyList<string> Warnings { get; }
    }
}
