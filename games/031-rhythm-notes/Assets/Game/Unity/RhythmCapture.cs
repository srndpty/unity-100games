using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class RhythmCapture : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<RhythmGame>();
            await capture.WaitUntilAsync(() => game.HudReady, 10);
            await capture.ShootAsync("select", "曲選択と入力補正");
            var song = game.BeginCapture();
            capture.Record("song", song.Name);
            capture.Record("inputOffsetMs", "0");

            for (var i = 0; i < song.Notes.Count; i++)
            {
                var note = song.Notes[i];
                var seconds = (double)note.Sample / Song.SampleRate;
                game.Session.StrikeAt(note.Strike, seconds);
                game.CaptureAt(seconds + .105);
                if (i == 7) { await capture.ShootAsync("performance", $"曲内サンプル {game.Session.Position}"); }
            }

            game.CaptureAt((double)song.Length / Song.SampleRate);
            await capture.ShootAsync("result", $"曲内サンプル {game.Session.Position}");
        }
    }
}
