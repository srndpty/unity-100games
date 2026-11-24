using System.IO;

namespace Game.Unity.Evidence
{
    /// <summary>
    /// 証跡の公開で状態を変えるファイル操作。テストでは任意の操作の直前で失敗させ（fault injection）、
    /// どこで止まっても公開中の証跡が壊れないことを確かめる。
    /// これで確かめられるのは「操作と操作の間で止まった場合」の整合性で、電源断を含む記憶装置への永続性までは保証しない。
    /// </summary>
    public interface IEvidenceFileOps
    {
        void CreateDirectory(string path);

        /// <summary>フォルダ名の付け替え（同じボリューム内なので原子的）。</summary>
        void MoveDirectory(string source, string destination);

        void MoveFile(string source, string destination);

        void WriteFile(string path, string contents);

        /// <summary><paramref name="source"/> で <paramref name="destination"/> を置き換える（あれば File.Replace、なければ File.Move）。</summary>
        void ReplaceFile(string source, string destination);

        void DeleteDirectory(string path);
    }

    public sealed class EvidenceFileOps : IEvidenceFileOps
    {
        public static readonly EvidenceFileOps Instance = new();

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);

        public void MoveFile(string source, string destination) => File.Move(source, destination);

        public void WriteFile(string path, string contents)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            using var writer = new StreamWriter(stream);
            writer.Write(contents);
        }

        public void ReplaceFile(string source, string destination)
        {
            if (File.Exists(destination))
            {
                File.Replace(source, destination, destinationBackupFileName: null);
            }
            else
            {
                File.Move(source, destination);
            }
        }

        public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);
    }
}
