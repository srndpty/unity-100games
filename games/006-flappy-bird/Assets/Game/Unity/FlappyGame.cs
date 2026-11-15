using System;
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
    public sealed class FlappyGame : MonoBehaviour
    {
        [SerializeField] private FlappySettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        private readonly List<InputAction> _actions = new();
        private readonly List<Sprite> _sprites = new();
        private readonly List<Texture2D> _textures = new();
        private SpriteRenderer[] _pipes;
        private SpriteRenderer _bird;
        private SpriteRenderer _eye;
        private SpriteRenderer _wing;
        private SpriteRenderer _beak;
        private InputAction _flapKey;
        private InputAction _flapMouse;
        private AudioSource _audio;
        private AudioClip _flapSound;
        private AudioClip _pointSound;
        private AudioClip _loseSound;
        private VisualElement _root;
        private int _version = -1;
        private bool _submitRequested;
        private bool _pauseRequested;
        private bool _backRequested;
        private bool _flapRequested;
        private bool _mouseRequested;
        private bool _hasFocus = true;
        private bool _inputBlocked;
        public FlappyFlow Flow { get; private set; }
        public FlappySettings Settings => _settings;
        public VisualElement HudRoot => _root;
        public bool ManualTicks { get; set; }
        public int PipeRendererCount => _pipes.Length;

        private void Awake()
        {
            _settings.Validate();
            Flow = new FlappyFlow(_settings.Game, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            var pixel = MakeSprite(false); var circle = MakeSprite(true);
            var sky = CreateSprite("Sky", pixel, new Color(.055f, .16f, .22f), -5);
            Place(sky, 0, 0, 24, 14);
            for (var i = 0; i < 18; i++)
            {
                var cloud = CreateSprite("Cloud" + i, circle, new Color(.12f, .25f, .3f), -4);
                Place(cloud, i * 3.1f % 26 - 13, i * 1.7f % 10 - 5, 2.5f, .65f);
            }
            foreach (var side in new[] { -1, 1 })
            {
                var wall = CreateSprite("Wall", pixel, new Color(.27f, .57f, .56f), 2); Place(wall, 0, side * 7.15f, 24, .3f);
                var rim = CreateSprite("WallRim", pixel, new Color(.64f, .86f, .73f), 3); Place(rim, 0, side * 7.025f, 24, .05f);
            }
            _pipes = new SpriteRenderer[FlappyConfig.MaxCapacity * 4];
            for (var i = 0; i < _pipes.Length; i++)
            { _pipes[i] = CreateSprite("Pipe" + i, pixel, i % 4 < 2 ? new Color(.16f, .48f, .47f) : new Color(.39f, .76f, .63f), 0); }
            _bird = CreateSprite("Bird", circle, new Color(1, .77f, .29f), 5);
            _wing = CreateSprite("Wing", circle, new Color(.94f, .53f, .19f), 6);
            _eye = CreateSprite("Eye", circle, new Color(.05f, .12f, .17f), 7);
            _beak = CreateSprite("Beak", pixel, new Color(1, .48f, .22f), 6);
            _audio = GetComponent<AudioSource>(); _audio.playOnAwake = false; _audio.mute = AudioPolicy.ShouldMute; _audio.volume = _settings.Volume;
            _flapSound = Tone("Flap", 640, .045f); _pointSound = Tone("Point", 960, .08f); _loseSound = Tone("Lose", 140, .18f);
            _flapKey = Action("FlapKey", "<Keyboard>/space");
            _flapMouse = Action("FlapMouse", "<Mouse>/leftButton");
            _flapKey.performed += _ => { if (AcceptInput()) { _flapRequested = true; } };
            _flapMouse.performed += _ => { if (AcceptInput()) { _mouseRequested = true; } };
            Button("Submit", () => _submitRequested = true, "<Keyboard>/enter");
            Button("Pause", () => _pauseRequested = true, "<Keyboard>/p", "<Keyboard>/escape");
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded); Render();
        }
        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;
        private InputAction Action(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.Enable(); _actions.Add(action); return action;
        }
        private void Button(string name, System.Action callback, params string[] bindings)
        { Action(name, bindings).performed += _ => { if (AcceptInput()) { callback(); } }; }
        private void Update()
        {
            if (AcceptInput())
            {
                if (_backRequested) { Flow.Back(); BlockInput(); }
                else if (_pauseRequested) { Flow.TogglePause(); BlockInput(); }
                else if (_submitRequested) { Flow.Submit(); BlockInput(); }
                else
                {
                    if (!_inputBlocked && (_flapRequested || (_mouseRequested && !PointerOverUi()))) { Flow.RequestFlap(); }
                    if (_inputBlocked && !_flapKey.IsPressed() && !_flapMouse.IsPressed()) { _inputBlocked = false; }
                    if (!ManualTicks)
                    {
                        var score = Flow.Game?.Score ?? 0; var screen = Flow.Screen; var flap = Flow.PendingFlap;
                        Flow.Advance(Time.deltaTime); PlayEvents(score, screen, flap && !Flow.PendingFlap);
                    }
                }
            }
            _submitRequested = _pauseRequested = _backRequested = _flapRequested = _mouseRequested = false;
            Render();
        }
        private bool PointerOverUi()
        {
            if (_root?.panel == null || Mouse.current == null) { return false; }
            var point = Mouse.current.position.ReadValue(); point.y = Screen.height - point.y;
            var element = _root.panel.Pick(RuntimePanelUtils.ScreenToPanel(_root.panel, point));
            for (; element != null; element = element.parent)
            { if (element is Button || element.name == "overlay") { return true; } }
            return false;
        }
        private void BlockInput() { _inputBlocked = true; _flapRequested = _mouseRequested = false; }
        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            Flow.SetFocus(focus);
            if (!focus) { _submitRequested = _pauseRequested = _backRequested = false; BlockInput(); }
        }
        public void Step(FlappyCommand command)
        {
            var score = Flow.Game?.Score ?? 0; var screen = Flow.Screen;
            if (command.Flap) { Flow.RequestFlap(); }
            Flow.StepOneTick(); PlayEvents(score, screen, command.Flap);
        }
        private void PlayEvents(long score, AppScreen screen, bool flap)
        {
            if (Flow.Screen == AppScreen.Result && screen != AppScreen.Result) { _audio.PlayOneShot(_loseSound); }
            else if ((Flow.Game?.Score ?? 0) > score) { _audio.PlayOneShot(_pointSound); }
            else if (flap && Flow.Screen == AppScreen.Playing) { _audio.PlayOneShot(_flapSound); }
        }
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographic = true; _camera.orthographicSize = Mathf.Max(8.5f, 13f / _camera.aspect);
            _camera.transform.position = new Vector3(0, .35f, -10);
            var game = Flow.Game; var config = _settings.Game;
            for (var i = 0; i < _pipes.Length; i++) { _pipes[i].enabled = game != null && i < game.PipeCount * 4; }
            if (game != null)
            {
                for (var i = 0; i < game.PipeCount; i++)
                {
                    var p = game.PipeAt(i); var lower = p.Center - p.Gap / 2; var upper = p.Center + p.Gap / 2;
                    var left = Mathf.Max(-12, p.X - config.PipeWidth / 2);
                    var right = Mathf.Min(12, p.X + config.PipeWidth / 2);
                    var width = Mathf.Max(0, right - left); var x = (left + right) / 2;
                    for (var part = 0; part < 4; part++) { _pipes[i * 4 + part].enabled = width > 0; }
                    Place(_pipes[i * 4], x, (-7 + lower) / 2, width, lower + 7);
                    Place(_pipes[i * 4 + 1], x, (7 + upper) / 2, width, 7 - upper);
                    // 装飾は当たり判定の矩形内に収める。
                    Place(_pipes[i * 4 + 2], x, lower - .13f, width, .26f);
                    Place(_pipes[i * 4 + 3], x, upper + .13f, width, .26f);
                }
            }
            var y = game?.BirdY ?? 0; var r = config.BirdRadius;
            Place(_bird, FlappyConfig.BirdX, y, r * 2, r * 2);
            Place(_wing, FlappyConfig.BirdX - r * .3f, y - r * .2f, r * .85f, r * .6f);
            Place(_eye, FlappyConfig.BirdX + r * .3f, y + r * .28f, r * .25f, r * .25f);
            Place(_beak, FlappyConfig.BirdX + r * .7f, y - r * .1f, r * .4f, r * .25f);
            RenderHud();
        }
        private static void Place(SpriteRenderer renderer, float x, float y, float width, float height)
        { renderer.transform.localPosition = new Vector3(x, y, 0); renderer.transform.localScale = new Vector3(width, height, 1); }
        private SpriteRenderer CreateSprite(string name, Sprite sprite, Color color, int order)
        {
            var item = new GameObject(name); item.transform.SetParent(transform, false);
            var renderer = item.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = order; return renderer;
        }
        private Sprite MakeSprite(bool circle)
        {
            var size = circle ? 64 : 1; var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (var y = 0; y < size; y++)
            { for (var x = 0; x < size; x++) { texture.SetPixel(x, y, !circle || new Vector2(x - 31.5f, y - 31.5f).sqrMagnitude <= 32 * 32 ? Color.white : Color.clear); } }
            texture.Apply(); _textures.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size); _sprites.Add(sprite); return sprite;
        }
        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_version == version && _root != null) { return; }
            _version = version; _root = root;
            _root.Q<Button>("submit").clicked += () => _submitRequested = true;
            _root.Q<Button>("resume").clicked += () => _pauseRequested = true;
            _root.Q<Button>("back").clicked += () => _backRequested = true;
            _root.Q<Button>("quit").clicked += () => { if (!EvidenceCapture.IsRunning) { Application.Quit(); } };
            _root.Query<Button>().ForEach(button => button.focusable = false); RenderHud();
        }
        private void RenderHud()
        {
            if (_root == null || Flow == null) { return; }
            var screen = Flow.Screen; var game = Flow.Game;
            _root.Q<Label>("score").text = "SCORE  " + (game?.Score ?? 0) + "     LEVEL  " + (game?.Level ?? 0) + " / " + _settings.Game.MaxLevel;
            _root.Q<VisualElement>("overlay").style.display = screen == AppScreen.Playing || screen == AppScreen.Ready ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<Label>("headline").text = screen == AppScreen.Title ? "SKY HOPPER" : screen == AppScreen.Paused ? "PAUSED" : "GAME OVER";
            _root.Q<Label>("message").text = screen == AppScreen.Title ? "Fly through the gaps. Each pair earns one point.\nThe sky gets faster as your score grows.\n\nSpace / left click : flap\nEnter : start / replay    P / Esc : pause"
                : screen == AppScreen.Paused ? "P / Esc : resume" : "SCORE  " + (game?.Score ?? 0) + "\nEnter : play again";
            Show("submit", screen == AppScreen.Title || screen == AppScreen.Result);
            _root.Q<Button>("submit").text = screen == AppScreen.Title ? "START" : "PLAY AGAIN";
            Show("resume", screen == AppScreen.Paused); Show("back", screen == AppScreen.Paused || screen == AppScreen.Result); Show("quit", screen == AppScreen.Title);
            _root.Q<Label>("status").text = screen == AppScreen.Ready ? "READY\nSPACE / CLICK TO FLY" : "";
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
            foreach (var sprite in _sprites) { Destroy(sprite); }
            foreach (var texture in _textures) { Destroy(texture); }
            Destroy(_flapSound); Destroy(_pointSound); Destroy(_loseSound);
        }
    }
}
