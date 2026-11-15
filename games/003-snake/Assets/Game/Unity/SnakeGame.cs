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
    public sealed class SnakeGame : MonoBehaviour
    {
        [SerializeField] private SnakeSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        private readonly List<InputAction> _actions = new();
        private readonly List<SpriteRenderer> _segments = new();
        private SpriteRenderer _food;
        private Sprite _sprite;
        private Texture2D _texture;
        private AudioClip _eat;
        private AudioClip _lose;
        private AudioClip _win;
        private AudioSource _audio;
        private VisualElement _root;
        private int _version = -1;
        private bool _backRequested;
        private bool _pauseRequested;
        private bool _submitRequested;
        private bool _hasFocus = true;

        public SnakeFlow Flow { get; private set; }
        public SnakeSettings Settings => _settings;
        public VisualElement HudRoot => _root;
        // テストも撮影と同じ明示的な Tick 駆動を使える。
        public bool ManualTicks { get; set; }

        private void Awake()
        {
            _settings.Validate();
            Flow = new SnakeFlow(_settings.Game, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _texture = new Texture2D(1, 1);
            _texture.SetPixel(0, 0, Color.white);
            _texture.Apply();
            _sprite = Sprite.Create(_texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
            var board = CreateSprite("Board", new Color(.07f, .12f, .16f), -2);
            board.transform.localPosition = new Vector3((_settings.Game.Width - 1) / 2f, (_settings.Game.Height - 1) / 2f, 0);
            board.transform.localScale = new Vector3(_settings.Game.Width + .3f, _settings.Game.Height + .3f, 1);
            _food = CreateSprite("Food", new Color(1f, .45f, .25f), 1);
            _food.transform.localScale = Vector3.one * .65f;
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.mute = AudioPolicy.ShouldMute;
            _audio.volume = _settings.Volume;
            _eat = Tone("Eat", 660, .07f);
            _lose = Tone("Lose", 160, .2f);
            _win = Tone("Win", 880, .25f);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            AddDirection("Right", Direction.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            AddDirection("Up", Direction.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            AddDirection("Left", Direction.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            AddDirection("Down", Direction.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            AddButton("Submit", () => _submitRequested = true, "<Keyboard>/enter", "<Keyboard>/space");
            AddButton("Pause", () => _pauseRequested = true, "<Keyboard>/p", "<Keyboard>/escape");
            Render();
        }

        private void AddDirection(string name, Direction direction, params string[] bindings)
        {
            AddButton(name, () => Flow.RequestDirection(direction), bindings);
        }

        private void AddButton(string name, Action callback, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.performed += _ => { if (!EvidenceCapture.IsRunning && _hasFocus) { callback(); } };
            action.Enable();
            _actions.Add(action);
        }

        private void Update()
        {
            if (!EvidenceCapture.IsRunning && _hasFocus)
            {
                if (_backRequested) { Flow.Back(); }
                else if (_pauseRequested) { Flow.TogglePause(); }
                else if (_submitRequested) { Flow.Submit(); }
                // 開始・再開したフレームの経過時間は、開始前の時間なので加算しない。
                else if (!ManualTicks) { Advance(Time.deltaTime); }
            }
            _backRequested = _pauseRequested = _submitRequested = false;
            Render();
        }

        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (!focus)
            {
                // フォーカス喪失より前に届いた停止・開始操作を復帰へ持ち越さない。
                _backRequested = _pauseRequested = _submitRequested = false;
            }
            if (!focus && !EvidenceCapture.IsRunning && Flow != null && Flow.Screen == AppScreen.Playing)
            {
                Flow.TogglePause();
            }
        }

        private void Advance(double seconds)
        {
            var score = Flow.Game?.Score ?? 0;
            var phase = Flow.Game?.Phase ?? GamePhase.Playing;
            Flow.Advance(seconds);
            PlayEvents(score, phase);
        }

        public void Step(Direction direction)
        {
            var score = Flow.Game?.Score ?? 0;
            var phase = Flow.Game?.Phase ?? GamePhase.Playing;
            Flow.RequestDirection(direction);
            Flow.StepOneTick();
            PlayEvents(score, phase);
            Render();
        }

        private void PlayEvents(int score, GamePhase phase)
        {
            var game = Flow.Game;
            if (game == null) { return; }
            if (game.Phase != phase) { _audio.PlayOneShot(game.Phase == GamePhase.Won ? _win : _lose); }
            else if (game.Score > score) { _audio.PlayOneShot(_eat); }
        }

        public void Render()
        {
            if (Flow == null) { return; }
            var config = _settings.Game;
            _camera.orthographic = true;
            _camera.orthographicSize = Mathf.Max((config.Height + 3f) / 2f, (config.Width + 3f) / (2f * _camera.aspect));
            _camera.transform.position = new Vector3((config.Width - 1) / 2f, (config.Height - 1) / 2f + .7f, -10);
            var game = Flow.Game;
            var length = game?.Length ?? 0;
            while (_segments.Count < length)
            {
                var segment = CreateSprite("Segment" + _segments.Count, Color.white, 0);
                segment.transform.localScale = Vector3.one * .9f;
                _segments.Add(segment);
            }
            for (var i = 0; i < _segments.Count; i++)
            {
                _segments[i].enabled = i < length;
                if (i < length)
                {
                    var cell = game.SegmentAt(i);
                    _segments[i].transform.localPosition = new Vector3(cell.X, cell.Y, 0);
                    _segments[i].color = i == 0 ? new Color(.85f, 1f, .55f) : new Color(.25f, .8f, .65f);
                }
            }
            _food.enabled = game?.Food != null;
            if (_food.enabled)
            {
                var cell = game.Food.Value;
                _food.transform.localPosition = new Vector3(cell.X, cell.Y, 0);
            }
            RenderHud();
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_version == version && _root != null) { return; }
            _version = version;
            _root = root;
            _root.Q<Button>("submit").clicked += () => _submitRequested = true;
            _root.Q<Button>("resume").clicked += () => _pauseRequested = true;
            _root.Q<Button>("back").clicked += () => _backRequested = true;
            _root.Q<Button>("quit").clicked += () => { if (!EvidenceCapture.IsRunning) { Application.Quit(); } };
            // ボタンのキーボード既定操作と InputAction の二重発火を防ぐ。
            _root.Query<Button>().ForEach(button => button.focusable = false);
            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null || Flow == null) { return; }
            var screen = Flow.Screen;
            _root.Q<Label>("score").text = "SCORE  " + (Flow.Game?.Score ?? 0);
            _root.Q<VisualElement>("overlay").style.display = screen == AppScreen.Playing ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<Label>("headline").text = screen switch
            {
                AppScreen.Title => "SNAKE",
                AppScreen.Paused => "PAUSED",
                AppScreen.Result => Flow.Game.Phase == GamePhase.Won ? "YOU WIN" : "GAME OVER",
                _ => "",
            };
            _root.Q<Label>("message").text = screen == AppScreen.Title
                ? "Fill the board. Avoid walls and your body.\n\nArrow keys / WASD : turn\nEnter / Space : start or replay\nP / Esc : pause or resume"
                : screen == AppScreen.Paused ? "P / Esc : resume" : "SCORE  " + (Flow.Game?.Score ?? 0) + "\nEnter / Space : play again";
            Show("submit", screen == AppScreen.Title || screen == AppScreen.Result);
            _root.Q<Button>("submit").text = screen == AppScreen.Title ? "START" : "PLAY AGAIN";
            Show("resume", screen == AppScreen.Paused);
            Show("back", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("quit", screen == AppScreen.Title);
        }

        private void Show(string name, bool visible) => _root.Q<Button>(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        private SpriteRenderer CreateSprite(string name, Color color, int order)
        {
            var item = new GameObject(name);
            item.transform.SetParent(transform, false);
            var renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = _sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static AudioClip Tone(string name, int frequency, float seconds)
        {
            var samples = new float[(int)(44100 * seconds)];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = .05f * Mathf.Sin(2 * Mathf.PI * frequency * i / 44100f) * (1f - (float)i / samples.Length);
            }
            var clip = AudioClip.Create(name, samples.Length, 1, 44100, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            if (_hudRenderer != null) { _hudRenderer.UnregisterUIReloadCallback(OnHudReloaded); }
            Destroy(_sprite); Destroy(_texture);
            Destroy(_eat); Destroy(_lose); Destroy(_win);
        }
    }
}
