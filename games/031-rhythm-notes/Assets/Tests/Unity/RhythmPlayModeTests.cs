using System.Collections;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Screen = Game.App.Screen;

namespace Game.Unity.Tests
{
    public sealed class RhythmPlayModeTests
    {
        [UnityTest]
        public IEnumerator ScheduledPlaybackPausesBeforeAndAfterStartAndAcceptsARealKey()
        {
            var previousOffset = PlayerPrefs.GetInt(RhythmGame.OffsetKey, int.MinValue);
            using var keyboard = new TestKeyboard();
            try
            {
                PlayerPrefs.SetInt(RhythmGame.OffsetKey, 0);
                yield return SceneManager.LoadSceneAsync("Rhythm");
                var game = Object.FindAnyObjectByType<RhythmGame>();
                Assert.That(game, Is.Not.Null);
                yield return keyboard.Tap(Key.Enter);
                Assert.That(game.Session.State, Is.EqualTo(Screen.Play));

                var audioSource = game.GetComponent<AudioSource>();
                var start = game.StartDsp;
                yield return VerifyPause(keyboard, game, audioSource);
                Assert.That(AudioSettings.dspTime, Is.LessThan(start));

                var note = game.Session.Score.Song.Notes[0];
                var target = game.HeardStartDsp + (double)note.Sample / Song.SampleRate;
                yield return WaitForDsp(target - .025);
                yield return keyboard.Tap(note.Strike == Strike.Pulse ? Key.F : Key.J);
                yield return WaitForDsp(target + .16);
                Assert.That(game.Session.Score.Good + game.Session.Score.Fair, Is.EqualTo(1));
                Assert.That(game.VisibleBursts, Is.EqualTo(1), "打ったノーツの破壊演出");
                Assert.That(audioSource.isPlaying, Is.True);
                Assert.That(audioSource.timeSamples, Is.GreaterThan(0));
                AudioSettings.GetDSPBufferSize(out var bufferSize, out var bufferCount);
                var tolerance = bufferSize * bufferCount * (double)Song.SampleRate / AudioSettings.outputSampleRate;
                var latency = RhythmGame.OutputLatency * Song.SampleRate;
                Assert.That(audioSource.timeSamples - latency, Is.EqualTo(game.Session.Position).Within(tolerance));

                yield return VerifyPause(keyboard, game, audioSource);
                Assert.That(game.StartDsp, Is.EqualTo(start));
                yield return keyboard.Tap(Key.Escape);
                yield return keyboard.Tap(Key.T);
                Assert.That(game.Session.State, Is.EqualTo(Screen.Select));
                Assert.That(AudioListener.pause, Is.False);
            }
            finally
            {
                AudioListener.pause = false;
                if (previousOffset == int.MinValue) { PlayerPrefs.DeleteKey(RhythmGame.OffsetKey); }
                else { PlayerPrefs.SetInt(RhythmGame.OffsetKey, previousOffset); }
            }
        }

        [UnityTest]
        public IEnumerator OffsetKeysSaveAndCalibrationPlaysTheClickTrack()
        {
            var previousOffset = PlayerPrefs.GetInt(RhythmGame.OffsetKey, int.MinValue);
            using var keyboard = new TestKeyboard();
            try
            {
                PlayerPrefs.SetInt(RhythmGame.OffsetKey, 100);
                yield return SceneManager.LoadSceneAsync("Rhythm");
                var game = Object.FindAnyObjectByType<RhythmGame>();
                Assert.That(game.Session.Offset, Is.EqualTo(100));
                yield return keyboard.Tap(Key.RightArrow);
                Assert.That(game.Session.Offset, Is.EqualTo(105));
                Assert.That(PlayerPrefs.GetInt(RhythmGame.OffsetKey), Is.EqualTo(105));

                yield return keyboard.Tap(Key.C);
                Assert.That(game.Session.State, Is.EqualTo(Screen.Play));
                Assert.That(game.Session.Calibrating, Is.True);
                var audioSource = game.GetComponent<AudioSource>();
                Assert.That(audioSource.clip, Is.SameAs(game.CalibrationClip));
                yield return keyboard.Tap(Key.LeftArrow);
                Assert.That(game.Session.Offset, Is.EqualTo(105), "演奏中は補正を変えない");

                yield return keyboard.Tap(Key.Escape);
                yield return keyboard.Tap(Key.T);
                Assert.That(game.Session.State, Is.EqualTo(Screen.Select));
                Assert.That(audioSource.isPlaying, Is.False);
            }
            finally
            {
                AudioListener.pause = false;
                if (previousOffset == int.MinValue) { PlayerPrefs.DeleteKey(RhythmGame.OffsetKey); }
                else { PlayerPrefs.SetInt(RhythmGame.OffsetKey, previousOffset); }
            }
        }

        private static IEnumerator VerifyPause(TestKeyboard keyboard, RhythmGame game, AudioSource audioSource)
        {
            yield return keyboard.Tap(Key.Escape);
            Assert.That(game.Session.State, Is.EqualTo(Screen.Paused));
            var dsp = AudioSettings.dspTime;
            var position = game.Session.Position;
            var playback = audioSource.timeSamples;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(AudioSettings.dspTime, Is.EqualTo(dsp));
            Assert.That(game.Session.Position, Is.EqualTo(position));
            Assert.That(audioSource.timeSamples, Is.EqualTo(playback));
            yield return keyboard.Tap(Key.Escape);
            Assert.That(game.Session.State, Is.EqualTo(Screen.Play));
            Assert.That(AudioListener.pause, Is.False);
        }

        private static IEnumerator WaitForDsp(double target)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (AudioSettings.dspTime < target)
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "DSP 時計が進んでいません。");
                yield return null;
            }
        }
    }
}
