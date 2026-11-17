using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Unity.Evidence;
using UnityEngine;

// Unity CLIから実行する既存証跡の形式移行。画像の再撮影ではなく、元の撮影情報を保持した再エンコード。
public static class ConvertEvidenceToJpeg
{
    public static object Main()
    {
        var repository = Directory.GetParent(Application.dataPath).Parent.Parent.FullName;
        var games = Path.GetFullPath(Path.Combine(repository, "games"));
        var results = new List<object>();
        foreach (var game in Directory.GetDirectories(games).OrderBy(path => path, StringComparer.Ordinal))
        {
            var evidence = Path.GetFullPath(Path.Combine(game, "evidence"));
            if (!evidence.StartsWith(games + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || (File.GetAttributes(game) & FileAttributes.ReparsePoint) != 0)
            { throw new InvalidOperationException("証跡がgames配下の通常フォルダではありません。"); }
            if (!File.Exists(Path.Combine(evidence, EvidencePublisher.PointerFileName))) { continue; }
            var publisher = new EvidencePublisher(evidence);
            var manifest = publisher.VerifyCurrent();
            var previous = publisher.Current;
            if (manifest.Shots.All(shot => shot.File.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))) { continue; }
            var sourceShots = manifest.Shots.ToArray();
            var sourceDirectory = publisher.GenerationDirectory(previous.Generation);
            var before = sourceShots.Sum(shot => new FileInfo(Path.Combine(sourceDirectory, shot.File)).Length);
            var staging = publisher.CreateStaging();
            manifest.Shots.Clear();
            var context = new CaptureContext(staging, manifest);
            context.Record("imageConversion", "PNG to JPEG; source generation=" + previous.Generation + "; source run=" + previous.Run
                + "; convertedAtUtc=" + DateTime.UtcNow.ToString("O") + "; original capture/build metadata retained");
            foreach (var shot in sourceShots)
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    if (!texture.LoadImage(File.ReadAllBytes(Path.Combine(sourceDirectory, shot.File))))
                    { throw new InvalidOperationException("画像を読み込めません: " + shot.File); }
                    context.SaveFrame(texture, Path.GetFileNameWithoutExtension(shot.File), shot.Note);
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
            File.WriteAllText(Path.Combine(staging, EvidencePublisher.ManifestFileName), JsonUtility.ToJson(manifest, true) + "\n");
            var published = publisher.Publish(staging);
            publisher.VerifyCurrent();
            var after = Directory.GetFiles(publisher.GenerationDirectory(published.Generation)).Sum(file => new FileInfo(file).Length);
            var readme = Path.Combine(evidence, "README.md");
            if (File.Exists(readme))
            {
                var text = File.ReadAllText(readme).Replace("captures/" + previous.Generation + "/", "captures/" + published.Generation + "/")
                    .Replace("runs/" + previous.Run + "/", "runs/" + published.Run + "/");
                foreach (var shot in sourceShots) { text = text.Replace(shot.File, Path.GetFileNameWithoutExtension(shot.File) + ".jpg"); }
                File.WriteAllText(readme, text);
            }
            results.Add(new { game = Path.GetFileName(game), generation = published.Generation, run = published.Run, images = sourceShots.Length, beforeBytes = before, afterBytes = after });
        }
        return results;
    }
}
