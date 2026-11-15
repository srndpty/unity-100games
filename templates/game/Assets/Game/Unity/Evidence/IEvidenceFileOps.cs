using System.IO;

namespace Game.Unity.Evidence
{
    /// <summary>
    /// 証跡の公開で状態を変えるファイル操作。テストでは任意の操作の途中で失敗させ（fault injection）、
    /// どこで止まっても公開中の証跡が壊れないことを確かめる。
    /// </summary>
    public interface IEvidenceFileOps
    {
        /// <summary>フォルダ名の付け替え（同じボリューム内なので原子的）。</summary>
        void MoveDirectory(string source, string destination);

        void WriteFile(string path, string contents);

        /// <summary><paramref name="source"/> で <paramref name="destination"/> を置き換える（あれば File.Replace、なければ File.Move）。</summary>
        void ReplaceFile(string source, string destination);

        void DeleteDirectory(string path);
    }

    public sealed class EvidenceFileOps : IEvidenceFileOps
    {
        public static readonly EvidenceFileOps Instance = new();

        public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);

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
