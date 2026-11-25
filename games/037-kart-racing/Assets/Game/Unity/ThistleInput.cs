using System;
using System.Collections.Generic;
using Game.App;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game.Unity
{
    /// <summary>
    /// キーの押下・解放を、Input System のコールバックで起きた時刻つきの意図にして溜める。
    /// フレームの間に押して離したキーも 2 つの意図として残り、時刻はフレームの時刻ではなく入力の時刻なので、
    /// 同じ操作ならフレームレートに依らず同じ Tick に効く（022 の知見）。
    /// 1 つの操作の 2 本のキーはキーごとに番号を付ける。押しているかは Flow がキーごとに持つ（035 の知見）。
    /// </summary>
    public sealed class ThistleInput : IDisposable
    {
        private readonly List<InputAction> _actions = new();
        private readonly List<Intent> _pending = new();

        public ThistleInput()
        {
            Track(IntentKind.SteerLeftDown, IntentKind.SteerLeftUp, (Key.A, "a", 0), (Key.LeftArrow, "leftArrow", 1));
            Track(IntentKind.SteerRightDown, IntentKind.SteerRightUp, (Key.D, "d", 2), (Key.RightArrow, "rightArrow", 3));
            Track(IntentKind.AccelDown, IntentKind.AccelUp, (Key.W, "w", 4), (Key.UpArrow, "upArrow", 5));
            Track(IntentKind.BrakeDown, IntentKind.BrakeUp, (Key.S, "s", 6), (Key.DownArrow, "downArrow", 7));
            Track(IntentKind.DriftDown, IntentKind.DriftUp, (Key.Space, "space", 8));
            Bind(IntentKind.Submit, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.Cancel, "<Keyboard>/escape");
            Bind(IntentKind.Retry, "<Keyboard>/r");
            Bind(IntentKind.Back, "<Keyboard>/backspace");
        }

        /// <summary>入力と時刻の時計（秒）。Input System のイベントの時刻と同じ時計。</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>まだ Flow へ渡していない意図（起きた順）。</summary>
        public IReadOnlyList<Intent> Pending => _pending;

        public void Enable()
        {
            foreach (var action in _actions) { action.Enable(); }
        }

        public void Disable()
        {
            foreach (var action in _actions) { action.Disable(); }
            _pending.Clear();
        }

        /// <summary>溜めた意図を起きた順に Flow へ渡して空にする。</summary>
        public void ApplyTo(RaceFlow flow)
        {
            for (var i = 0; i < _pending.Count; i++) { flow.Apply(_pending[i]); }
            _pending.Clear();
        }

        /// <summary>溜めた意図を捨てる（フォーカス喪失・撮影中）。</summary>
        public void Clear() => _pending.Clear();

        public void Dispose()
        {
            foreach (var action in _actions) { action.Dispose(); }
            _actions.Clear();
            _pending.Clear();
        }

        private void Track(IntentKind down, IntentKind up, params (Key Key, string Path, int Bit)[] keys)
        {
            var action = new InputAction(down.ToString(), InputActionType.PassThrough);
            foreach (var key in keys) { action.AddBinding("<Keyboard>/" + key.Path); }
            action.performed += context =>
            {
                if (context.control is not KeyControl control) { return; }
                foreach (var key in keys)
                {
                    if (key.Key != control.keyCode) { continue; }
                    _pending.Add(new Intent(control.isPressed ? down : up, EventTime(context), key.Bit));
                    return;
                }
            };
            _actions.Add(action);
        }

        private void Bind(IntentKind kind, params string[] bindings)
        {
            var action = new InputAction(kind.ToString(), InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.performed += context => _pending.Add(new Intent(kind, EventTime(context)));
            _actions.Add(action);
        }

        /// <summary>入力イベントが起きた時刻。今より後にはしない。</summary>
        private static double EventTime(InputAction.CallbackContext context) => Math.Min(context.time, Now);
    }
}
