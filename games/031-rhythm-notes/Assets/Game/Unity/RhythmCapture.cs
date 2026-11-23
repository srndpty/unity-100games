using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class RhythmCapture : MonoBehaviour, ICaptureScenario
    {
        private const double Confirm = (RhythmSession.InputGraceMs + 5) / 1000.0;
        private const double FairLate = .05;
        private const double BurstAge = .08;
        private const double CalibrationLate = .15;
        private const double FullComboAge = .35;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<RhythmGame>();
            await capture.WaitUntilAsync(() => game.HudReady, 10);
            await capture.ShootAsync("select", "曲選択と音ずれ補正");
            var song = game.BeginCapture();
            capture.Record("song", song.Name);
            capture.Record("inputOffsetMs", "0");

            // 7個目は 50 ms 遅らせて「可」（SLOW）、8個目はぴったりで「良」。それぞれ判定の 80 ms 後の破壊演出を撮る。
            for (var i = 0; i < song.Notes.Count; i++)
            {
                var note = song.Notes[i];
                var seconds = (double)note.Sample / Song.SampleRate + (i == 6 ? FairLate : 0);
                game.Session.StrikeAt(note.Strike, seconds);
                game.CaptureAt(seconds + Confirm);
                if (i is 6 or 7)
                {
                    game.CaptureAt(seconds + Confirm + BurstAge);
                    await capture.ShootAsync(i == 6 ? "burst-fair" : "burst-good", $"曲内サンプル {game.Session.Position}");
                }
            }

            // 最後のノーツの判定でフルコンボ（ミスなし）が決まる。曲の余韻の間に演出を撮る。
            var last = (double)song.Notes[song.Notes.Count - 1].Sample / Song.SampleRate;
            game.CaptureAt(last + Confirm + FullComboAge);
            await capture.ShootAsync("full-combo", $"曲内サンプル {game.Session.Position}");

            game.CaptureAt((double)song.Length / Song.SampleRate);
            await capture.ShootAsync("result", $"曲内サンプル {game.Session.Position}");

            // 補正テスト：全拍を 150 ms 遅れて打ち、補正値が +150 ms になる結果を撮る。
            var calibration = game.BeginCalibrationCapture();
            capture.Record("calibrationLateMs", "150");
            for (var i = 0; i < calibration.Notes.Count; i++)
            {
                var seconds = (double)calibration.Notes[i].Sample / Song.SampleRate + CalibrationLate;
                game.Session.StrikeAt(Strike.Pulse, seconds);
                game.CaptureAt(seconds + Confirm);
                if (i == 11) { await capture.ShootAsync("calibration", $"曲内サンプル {game.Session.Position}"); }
            }

            game.CaptureAt((double)calibration.Length / Song.SampleRate);
            await capture.ShootAsync("calibration-result", $"補正 {game.Session.Offset} ms");
        }
    }
}
