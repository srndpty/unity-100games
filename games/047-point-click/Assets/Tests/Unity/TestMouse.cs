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

        /// <summary>画面の座標（左下が原点）へ動かす（ボタンは離したまま）。</summary>
        public void Move(Vector2 screen) =>
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen });

        /// <summary>画面の座標で左ボタンを押した状態にする。</summary>
        public void Press(Vector2 screen) =>
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen }.WithButton(MouseButton.Left));

        /// <summary>画面の座標で右ボタンを押した状態にする。</summary>
        public void PressRight(Vector2 screen) =>
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen }.WithButton(MouseButton.Right));

        /// <summary>ボタンを全て離した状態にする。</summary>
        public void Release(Vector2 screen) => Move(screen);

        public void Dispose() => InputSystem.RemoveDevice(_mouse);
    }
}
