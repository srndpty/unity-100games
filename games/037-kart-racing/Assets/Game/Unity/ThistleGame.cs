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
        /// <summary>レース中に画面の縦に入る長さ（m）。</summary>
        public const float ViewMeters = 20f;

        /// <summary>カメラが自機より前を見る距離（m）。</summary>
        public const float LookAhead = 4.5f;

        /// <summary>タイトルでコースの全景を置く、画面の右側の割合（左はタイトルの札）。</summary>
        public const float TitleViewShare = 0.57f;

        [SerializeField] private ThistleSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private ThistleStage _stage;

        private RaceFlow _flow;
        private Track _track;
        private KartConfig _rules;
        private ThistleAudio _audio;
        private ThistleHud _hud;
        private VisualElement _root;
        private int _hudVersion = -1;
        private int _held;
        private bool _focused = true;
        private Vector2 _trackMin;
        private Vector2 _trackMax;

        public RaceFlow Flow => _flow;

        public ThistleSettings Settings => _settings;

        public ThistleStage Stage => _stage;

        public VisualElement HudRoot => _root;

        public ThistleHud Hud => _hud;

        public Camera View => _camera;

        private void Awake()
        {
            _rules = _settings.Validate();
            _track = Track.Parse(_settings.Track.text, _rules);
            _flow = new RaceFlow(_rules, _track);
            ThistleTrackMesh.Bounds(_track, out _trackMin, out _trackMax);
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
            _hud?.Show(_flow);
        }

        /// <summary>車・煙・カメラだけを、その Tick の状態で置く。HUD の文字列は作らない。</summary>
        public void PresentWorld()
        {
            if (_flow == null) { return; }
            if (_flow.Screen == AppScreen.Title)
            {
                _stage.ClearEffects();
                _stage.Show(_flow.Race);
                FrameCircuit();
                return;
            }

            _stage.Show(_flow.Race);
            var kart = _flow.Race.KartAt(0);
            var forward = new Vector3(Trig.Cos(kart.Heading) / (float)Trig.One, Trig.Sin(kart.Heading) / (float)Trig.One, 0f);
            _camera.orthographic = true;
            _camera.orthographicSize = ViewMeters * 0.5f;
            _camera.transform.rotation = ThistleLayout.CameraRotation(kart.Heading);
            var look = ThistleLayout.At(kart.Position) + (forward * LookAhead);
            _camera.transform.position = new Vector3(look.x, look.y, -20f);
        }

        /// <summary>タイトル：北を上にコース全体を画面の右側へ収める。</summary>
        private void FrameCircuit()
        {
            var aspect = Mathf.Max(1f, _camera.aspect);
            var size = _trackMax - _trackMin;
            var half = Mathf.Max(size.y * 0.6f, size.x * 0.55f / (TitleViewShare * aspect));
            _camera.orthographic = true;
            _camera.orthographicSize = half;
            _camera.transform.rotation = Quaternion.identity;
            var center = (_trackMin + _trackMax) * 0.5f;
            var width = 2f * half * aspect;
            var shift = ((1f - TitleViewShare) + (TitleViewShare * 0.5f) - 0.5f) * width;
            _camera.transform.position = new Vector3(center.x - shift, center.y, -20f);
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
            _hud = new ThistleHud(root, _track, _rules);
            if (_flow != null) { _hud.Show(_flow); }
        }
    }
}
