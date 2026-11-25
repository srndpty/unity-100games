using System;
using System.Collections;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Unity.Tests
{
    /// <summary>
    /// PlayMode テスト用の仮想キーボード。プロジェクト全体の Input Actions をそのまま使えるよう、
    /// InputTestFixture（入力システムを初期化し直し、InputSystem.actions が null になる）は使わずにデバイスを追加する。
    /// バッチ実行ではゲームビューにフォーカスがなく入力が捨てられるため、使っている間だけフォーカスを無視させる。
    /// SetUp で作り、TearDown で Dispose する。
    /// </summary>
    public sealed class TestKeyboard : IDisposable
    {
        private readonly Keyboard _keyboard;
        private readonly InputSettings.BackgroundBehavior _backgroundBehavior;
        private readonly InputSettings.EditorInputBehaviorInPlayMode _editorInputBehavior;

        public TestKeyboard()
        {
            var settings = InputSystem.settings;
            _backgroundBehavior = settings.backgroundBehavior;
            _editorInputBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        /// <summary>指定したキーだけが押されている状態にする（引数なしで全キーを離す）。</summary>
        public void Hold(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        }

        /// <summary>指定したキーだけが押されている状態を、入力の時刻 <paramref name="time"/>（<c>Time.realtimeSinceStartupAsDouble</c> と同じ時計）で送る。</summary>
        public void HoldAt(double time, params Key[] keys)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys), time);
        }

        /// <summary>1 フレーム押して離す。<c>yield return keyboard.Tap(Key.Enter);</c> のように使う。</summary>
        public IEnumerator Tap(Key key)
        {
            Hold(key);
            yield return null;
            Hold();
            yield return null;
        }

        public void Dispose()
        {
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.settings.backgroundBehavior = _backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBehavior;
        }
    }
}
