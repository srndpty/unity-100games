using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using IntGeometry;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>キーを時刻つきの意図にして Flow へ渡し、その Tick の状態をそのまま描く。</summary>
    public sealed class ThistleGame : MonoBehaviour
    {
        public const float ViewMeters = 28f;

        [SerializeField] private ThistleSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private ThistleStage _stage;

        private RaceFlow _flow;
        private ThistleAudio _audio;
        private VisualElement _root;
        private Label _body;
        private int _hudVersion = -1;
        private int _held;
        private bool _focused = true;

        public RaceFlow Flow => _flow;

        public ThistleSettings Settings => _settings;

        public ThistleStage Stage => _stage;

        public VisualElement HudRoot => _root;

        private void Awake()
        {
            var rules = _settings.Validate();
            var track = Track.Parse(_settings.Track.text, rules);
            _flow = new RaceFlow(rules, track);
            _audio = gameObject.AddComponent<ThistleAudio>();
            _audio.Bind(_settings.Volume);
        }

        private void OnEnable()
        {
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
        }

        private void Update()
        {
            if (!EvidenceCapture.IsRunning) { ReadKeys(); }
            Render();
        }

        private void OnApplicationFocus(bool focus)
        {
            _focused = focus;
            _flow?.SetFocus(focus);
            if (!focus) { _held = 0; }
        }

        public void Render()
        {
            if (_flow == null) { return; }
            PresentWorld();
            _audio.Play(_flow.Race, _flow.TakeMoments());
            DrawHud();
        }

        /// <summary>車とカメラだけを、その Tick の状態で置く。HUD の文字列は作らない。</summary>
        public void PresentWorld()
        {
            if (_flow == null) { return; }
            _stage.Show(_flow.Race);
            var kart = _flow.Race.KartAt(0);
            var forward = new Vector3(Trig.Cos(kart.Heading) / (float)Trig.One, Trig.Sin(kart.Heading) / (float)Trig.One, 0f);
            _camera.orthographic = true;
            _camera.orthographicSize = ViewMeters * 0.5f;
            _camera.transform.rotation = ThistleLayout.CameraRotation(kart.Heading);
            var look = ThistleLayout.At(kart.Position) + (forward * 3f);
            _camera.transform.position = new Vector3(look.x, look.y, -20f);
        }

        private void ReadKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !_focused) { return; }
            var now = Time.realtimeSinceStartupAsDouble;
            Edge(keyboard.aKey, keyboard.leftArrowKey, now, IntentKind.SteerLeftDown, IntentKind.SteerLeftUp, 0);
            Edge(keyboard.dKey, keyboard.rightArrowKey, now, IntentKind.SteerRightDown, IntentKind.SteerRightUp, 2);
            Edge(keyboard.wKey, keyboard.upArrowKey, now, IntentKind.AccelDown, IntentKind.AccelUp, 4);
            Edge(keyboard.sKey, keyboard.downArrowKey, now, IntentKind.BrakeDown, IntentKind.BrakeUp, 6);
            Edge(keyboard.spaceKey, null, now, IntentKind.DriftDown, IntentKind.DriftUp, 8);
            if (Pressed(keyboard.enterKey) || Pressed(keyboard.numpadEnterKey)) { _flow.Apply(new Intent(IntentKind.Submit, now)); }
            if (Pressed(keyboard.escapeKey)) { _flow.Apply(new Intent(IntentKind.Cancel, now)); }
            if (Pressed(keyboard.rKey)) { _flow.Apply(new Intent(IntentKind.Retry, now)); }
            if (Pressed(keyboard.backspaceKey)) { _flow.Apply(new Intent(IntentKind.Back, now)); }
            _flow.Advance(now);
        }

        private void Edge(KeyControl first, KeyControl second, double now, IntentKind down, IntentKind up, int bit)
        {
            var pressed = (first != null && first.isPressed) || (second != null && second.isPressed);
            var was = (_held & (1 << bit)) != 0;
            if (pressed == was) { return; }
            if (pressed) { _held |= 1 << bit; }
            else { _held &= ~(1 << bit); }
            _flow.Apply(new Intent(pressed ? down : up, now));
        }

        private static bool Pressed(KeyControl key) => key != null && key.wasPressedThisFrame;

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _hudVersion == version) { return; }
            _root = root;
            _hudVersion = version;
            root.Clear();
            root.style.paddingTop = 16;
            root.style.paddingLeft = 24;
            _body = new Label { name = "body" };
            _body.style.color = Color.white;
            _body.style.fontSize = 28;
            _body.style.whiteSpace = WhiteSpace.Normal;
            _body.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(_body);
            DrawHud();
        }

        private void DrawHud()
        {
            if (_body == null || _flow == null) { return; }
            var race = _flow.Race;
            var player = race.KartAt(0);
            _body.text = _flow.Screen switch
            {
                AppScreen.Title => "THISTLE CIRCUIT\nEnter でスタート\nW 加速  A D 旋回  Space ドリフト",
                AppScreen.Paused => "一時停止\nEnter で再開   R でやり直し   Backspace でタイトル",
                AppScreen.Result => ResultText(race),
                _ => $"周回 {player.Laps + 1}/3   {race.PlaceOf(0)} 位   {player.Speed * 60 / 1000f:0} m/s\n" +
                     Charge(player) + Order(race),
            };
        }

        private static string Charge(Kart kart)
        {
            if (kart.BoostLeft > 0) { return "ブースト"; }
            if (kart.DriftTicks > 0) { return "ドリフト " + kart.DriftTicks; }
            return " ";
        }

        private static string Order(Race race)
        {
            var line = "\n";
            for (var place = 1; place <= KartConfig.KartCount; place++)
            {
                for (var i = 0; i < KartConfig.KartCount; i++)
                {
                    if (race.PlaceOf(i) != place) { continue; }
                    line += place + " " + (i == 0 ? "YOU" : "CPU " + i) + "  ";
                }
            }

            return line;
        }

        private string ResultText(Race race)
        {
            var title = _flow.Won ? "勝利" : "敗北";
            var lines = title + "\n";
            for (var place = 1; place <= KartConfig.KartCount; place++)
            {
                for (var i = 0; i < KartConfig.KartCount; i++)
                {
                    if (race.PlaceOf(i) != place) { continue; }
                    var kart = race.KartAt(i);
                    var name = i == 0 ? "YOU" : "CPU " + i;
                    var time = kart.Finished ? (kart.FinishTick / (float)RaceFlow.TickRate).ToString("0.00") + " 秒" : "時間切れ";
                    lines += place + " " + name + "  " + time + "\n";
                }
            }

            return lines + "Enter で再戦   Esc でタイトル";
        }
    }
}
