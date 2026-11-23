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
            var previousOffset = PlayerPrefs.GetInt("InputOffsetMs", int.MinValue);
            using var keyboard = new TestKeyboard();
            try
            {
                PlayerPrefs.SetInt("InputOffsetMs", 0);
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
                var target = start + (double)note.Sample / Song.SampleRate;
                yield return WaitForDsp(target - .025);
                yield return keyboard.Tap(note.Strike == Strike.Pulse ? Key.F : Key.J);
                yield return WaitForDsp(target + .16);
                Assert.That(game.Session.Score.Good + game.Session.Score.Fair, Is.EqualTo(1));
                Assert.That(audioSource.isPlaying, Is.True);
                Assert.That(audioSource.timeSamples, Is.GreaterThan(0));
                AudioSettings.GetDSPBufferSize(out var bufferSize, out var bufferCount);
                var tolerance = bufferSize * bufferCount * (double)Song.SampleRate / AudioSettings.outputSampleRate;
                Assert.That(audioSource.timeSamples, Is.EqualTo(game.Session.Position).Within(tolerance));

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
                if (previousOffset == int.MinValue) { PlayerPrefs.DeleteKey("InputOffsetMs"); }
                else { PlayerPrefs.SetInt("InputOffsetMs", previousOffset); }
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
