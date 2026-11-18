using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class FroggerGame : MonoBehaviour
    {
        [SerializeField] private FroggerSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private FroggerView _view;
        private readonly List<InputAction> _actions = new();
        private readonly List<InputAction> _moves = new();
        private bool _submitRequested;
        private bool _pauseRequested;
        private bool _backRequested;
        private bool _hasFocus = true;
        private bool _blocked;
        private MoveDirection _moveRequested;
        private VisualElement _root;
        private int _version = -1;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _hopSound;
        private AudioClip _winSound;
        private AudioClip _loseSound;
        public FroggerFlow Flow { get; private set; }
        public FroggerSettings Settings => _settings;
        public VisualElement HudRoot => _root;
        public FroggerView View => _view;
        public bool ManualTicks { get; set; }

        private void Awake()
        {
            _settings.Validate(); Flow = new FroggerFlow(_settings.Game);
            _audio = GetComponent<AudioSource>(); _audio.playOnAwake = false;
            _audio.volume = _settings.Volume; _audio.mute = AudioPolicy.ShouldMute;
            _hopSound = Tone("Hop", 560, .05f); _winSound = Tone("Win", 980, .18f); _loseSound = Tone("Lose", 160, .16f);
            Move(MoveDirection.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            Move(MoveDirection.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            Move(MoveDirection.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            Move(MoveDirection.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Action("Submit", "<Keyboard>/enter").performed += _ => { if (AcceptInput()) { _submitRequested = true; } };
            Action("Pause", "<Keyboard>/p", "<Keyboard>/escape").performed += _ => { if (AcceptInput()) { _pauseRequested = true; } };
            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded); Render();
        }
        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;
        private InputAction Action(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.Enable(); _actions.Add(action); return action;
        }
        private void Move(MoveDirection direction, params string[] bindings)
        {
            var action = Action(direction.ToString(), bindings); _moves.Add(action);
            action.performed += _ =>
            {
                if (AcceptInput() && !_blocked && (_moveRequested == MoveDirection.None || direction < _moveRequested)) { _moveRequested = direction; }
            };
        }
        private void Update()
        {
            if (AcceptInput())
            {
                if (_backRequested) { Flow.Back(); BlockInput(); }
                else if (_pauseRequested) { Flow.TogglePause(); BlockInput(); }
                else if (_submitRequested) { Flow.Submit(); BlockInput(); }
                else
                {
                    if (!_blocked) { Flow.RequestMove(_moveRequested); }
                    if (_blocked && _moves.TrueForAll(action => !action.IsPressed())) { _blocked = false; }
                    if (!ManualTicks)
                    {
                        var row = Flow.Game?.Row ?? 0; var x = Flow.Game?.X ?? 5.5; var screen = Flow.Screen;
                        Flow.Advance(Time.deltaTime); PlayEvents(row, x, screen);
                    }
                }
            }
            _submitRequested = _pauseRequested = _backRequested = false; _moveRequested = MoveDirection.None; Render();
        }
        private void BlockInput() { _blocked = true; _moveRequested = MoveDirection.None; }
        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            Flow.SetFocus(focus);
            if (!focus) { _submitRequested = _pauseRequested = _backRequested = false; BlockInput(); }
        }
        public void Step(MoveDirection direction = MoveDirection.None)
        {
            var row = Flow.Game?.Row ?? 0; var x = Flow.Game?.X ?? 5.5; var screen = Flow.Screen;
            Flow.RequestMove(direction); Flow.StepOneTick(); PlayEvents(row, x, screen);
        }
        private void PlayEvents(int row, double x, AppScreen previous)
        {
            if (previous != AppScreen.Result && Flow.Screen == AppScreen.Result)
            { _audio.PlayOneShot(Flow.Game.Result == RoundResult.Won ? _winSound : _loseSound); }
            else if (Flow.Game != null && (Flow.Game.Row != row || System.Math.Abs(Flow.Game.X - x) > .5)) { _audio.PlayOneShot(_hopSound); }
        }
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(5.35f, 10.5f / _camera.aspect);
            _camera.transform.position = new Vector3(0, .15f, -10);
            _view.Render(Flow.Game, _settings.Game); RenderHud();
        }
        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root; _version = version; root.style.unityFont = _font;
            root.Q<Button>("submit").clicked += () => { if (AcceptInput()) { _submitRequested = true; } };
            root.Q<Button>("resume").clicked += () => { if (AcceptInput()) { _pauseRequested = true; } };
            root.Q<Button>("back").clicked += () => { if (AcceptInput()) { _backRequested = true; } };
            root.Q<Button>("quit").clicked += () => { if (AcceptInput()) { Application.Quit(); } };
            root.Query<Button>().ForEach(button => button.focusable = false); RenderHud();
        }
        private void RenderHud()
        {
            if (_root == null || Flow == null) { return; }
            var screen = Flow.Screen; var result = Flow.Game?.Result ?? RoundResult.Playing;
            _root.Q<Label>("progress").text = "岸まで  " + (8 - (Flow.Game?.Row ?? 0)) + " マス";
            _root.Q<VisualElement>("overlay").style.display = screen == AppScreen.Playing ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<Label>("headline").text = screen == AppScreen.Title ? "向こう岸へ" : screen == AppScreen.Paused ? "ひと休み" : result == RoundResult.Won ? "渡れた！" : "もう一度！";
            _root.Q<Label>("message").text = screen == AppScreen.Title ? "車をよけて、丸太に乗って。\n向こう岸に着いたらクリア。\n\n方向キー / WASD：1押しで1マス\nP / Esc：停止　 Enter：開始・再挑戦"
                : screen == AppScreen.Paused ? "P / Esc またはボタンで再開" : result == RoundResult.Won ? "無事に向こう岸へ到着しました。" : result == RoundResult.CarHit ? "車にぶつかりました。\nすき間を見て渡ろう。" : result == RoundResult.Drowned ? "川に落ちました。\n丸太の上に着地しよう。" : "丸太に運ばれて画面の外へ。\n端に着く前に飛び移ろう。";
            Show("submit", screen == AppScreen.Title || screen == AppScreen.Result);
            _root.Q<Button>("submit").text = screen == AppScreen.Title ? "はじめる  /  Enter" : "もう一度  /  Enter";
            Show("resume", screen == AppScreen.Paused); Show("back", screen == AppScreen.Paused || screen == AppScreen.Result); Show("quit", screen == AppScreen.Title);
        }
        private void Show(string name, bool visible) => _root.Q<Button>(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        private static AudioClip Tone(string name, int frequency, float seconds)
        {
            var samples = new float[(int)(44100 * seconds)];
            for (var i = 0; i < samples.Length; i++) { samples[i] = .05f * Mathf.Sin(2 * Mathf.PI * frequency * i / 44100f) * (1f - (float)i / samples.Length); }
            var clip = AudioClip.Create(name, samples.Length, 1, 44100, false); clip.SetData(samples, 0); return clip;
        }
        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            if (_hudRenderer != null) { _hudRenderer.UnregisterUIReloadCallback(OnHudReloaded); }
            Destroy(_font); Destroy(_hopSound); Destroy(_winSound); Destroy(_loseSound);
        }
    }
}
