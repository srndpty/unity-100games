#nullable enable
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Unity.Tests
{
    /// <summary>
    /// PlayMode テスト用の仮想マウス。位置とボタンを状態イベントで送る。
    /// フォーカスの扱いは <see cref="TestKeyboard"/> が入力の設定を変えている間に使う。
    /// </summary>
    public sealed class TestMouse : IDisposable
    {
        private readonly Mouse _mouse;

        public TestMouse()
        {
            _mouse = InputSystem.AddDevice<Mouse>();
            _mouse.MakeCurrent();
        }

        /// <summary>画面の座標（左下が原点）で左ボタンを押した状態にする。</summary>
        public void Press(Vector2 screen) =>
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));

        /// <summary>左ボタンを離した状態にする。</summary>
        public void Release(Vector2 screen) =>
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen });

        public void Dispose() => InputSystem.RemoveDevice(_mouse);
    }
}
