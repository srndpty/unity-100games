using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Game.Unity.Evidence
{
    /// <summary>既存PNG証跡をJPEGへ移行する。撮影条件は保持し、公開とREADME更新はPublisherの同じロック内で行う。</summary>
    public static class EvidenceJpegMigration
    {
        public static PublishResult Convert(string directory, IEvidenceFileOps ops = null,
            Action<CaptureContext, string, CaptureManifest.Shot> convertFrame = null)
        {
            var publisher = new EvidencePublisher(directory, ops);
            var manifest = publisher.VerifyCurrent();
            publisher.VerifyReadmeLinks();
            var previous = publisher.Current;
            if (manifest.Shots.All(shot => shot.File.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))) { return null; }
            var sourceShots = manifest.Shots.ToArray();
            var sourceDirectory = publisher.GenerationDirectory(previous.Generation);
            var staging = publisher.CreateStaging();
            try
            {
                manifest.Shots.Clear();
                manifest.Info.RemoveAll(entry => entry.Key == "imageEncoding");
                var context = new CaptureContext(staging, manifest);
                context.Record("imageConversion", "PNG to JPEG; source generation=" + previous.Generation + "; source run=" + previous.Run
                    + "; convertedAtUtc=" + DateTime.UtcNow.ToString("O") + "; original capture/build metadata retained");
                foreach (var shot in sourceShots)
                { (convertFrame ?? ConvertFrame)(context, Path.Combine(sourceDirectory, shot.File), shot); }
                File.WriteAllText(Path.Combine(staging, EvidencePublisher.ManifestFileName), JsonUtility.ToJson(manifest, true) + "\n");
                return publisher.Publish(staging, next =>
                {
                    var current = publisher.Current;
                    if (current.Generation != previous.Generation || current.Run != previous.Run)
                    { throw new IOException("変換中に別の撮影が公開されました。変換を再実行してください。"); }
                    var readme = Path.Combine(directory, "README.md");
                    if (!File.Exists(readme)) { return null; }
                    var text = File.ReadAllText(readme);
                    text = Regex.Replace(text, @"captures/[a-fA-F0-9]{16}/", "captures/" + next.Generation + "/");
                    text = Regex.Replace(text, @"runs/[^/)\s]+/manifest\.json", "runs/" + next.Run + "/manifest.json");
                    foreach (var shot in sourceShots) { text = text.Replace(shot.File, Path.GetFileNameWithoutExtension(shot.File) + ".jpg"); }
                    return text;
                });
            }
            finally
            {
                try { if (Directory.Exists(staging)) { Directory.Delete(staging, recursive: true); } }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                { Debug.LogWarning("変換用一時フォルダを消せませんでした: " + staging + ": " + exception.Message); }
            }
        }

        private static void ConvertFrame(CaptureContext context, string source, CaptureManifest.Shot shot)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(source))) { throw new InvalidOperationException("画像を読み込めません: " + shot.File); }
                context.SaveFrame(texture, Path.GetFileNameWithoutExtension(shot.File), shot.Note);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
