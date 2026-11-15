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
    public sealed class AsteroidsGame : MonoBehaviour
    {
        [SerializeField] private AsteroidsSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        private readonly List<InputAction> _actions = new();
        private readonly List<SpriteRenderer> _rocks = new();
        private readonly List<SpriteRenderer> _ships = new();
        private readonly List<SpriteRenderer> _flames = new();
        private readonly List<SpriteRenderer> _bullets = new();
        private readonly List<Sprite> _sprites = new();
        private readonly List<Texture2D> _textures = new();
        private InputAction _left;
        private InputAction _right;
        private InputAction _fire;
        private Sprite _shipSprite;
        private Sprite _flameSprite;
        private Sprite _pixel;
        private Sprite _rockSprite;
        private InputAction _thrust;
        private AudioSource _audio;
        private AudioClip _hit;
        private AudioClip _lose;
        private AudioClip _win;
        private VisualElement _root;
        private int _version = -1;
        private bool _submitRequested;
        private bool _pauseRequested;
        private bool _backRequested;
        private bool _hasFocus = true;
        private bool _inputBlocked;
        public AsteroidsFlow Flow { get; private set; }
        public AsteroidsSettings Settings => _settings;
        public VisualElement HudRoot => _root;
        public bool ManualTicks { get; set; }

        private void Awake()
        {
            _settings.Validate();
            Flow = new AsteroidsFlow(_settings.Game, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _pixel = MakeSprite("1");
            _shipSprite = Outline(new[] { new Vector2(0, 1), new Vector2(-.8f, -.7f), new Vector2(0, -.3f), new Vector2(.8f, -.7f), new Vector2(0, 1) });
            _flameSprite = Outline(new[] { new Vector2(-.35f, -.5f), new Vector2(0, -1), new Vector2(.35f, -.5f) });
            var points = new Vector2[13];
            for (var i = 0; i <= 12; i++)
            {
                var angle = i % 12 * Mathf.PI / 6; var radius = i % 3 == 0 ? .8f : 1f;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            _rockSprite = Outline(points);
            var board = CreateSprite("Board", _pixel, new Color(.025f, .045f, .08f), -3);
            board.transform.localScale = new Vector3(32, 18, 1);
            for (var i = 0; i < 100; i++)
            {
                var star = CreateSprite("Star" + i, _pixel, new Color(.2f, .3f, .4f), -2);
                star.transform.localPosition = new Vector3((i * 47 % 317) / 10f - 15.8f, (i * 31 % 177) / 10f - 8.8f, 0);
                star.transform.localScale = Vector3.one * .025f;
            }
            _audio = GetComponent<AudioSource>(); _audio.playOnAwake = false; _audio.mute = AudioPolicy.ShouldMute; _audio.volume = _settings.Volume;
            _hit = Tone("Hit", 480, .06f); _lose = Tone("Lose", 140, .2f); _win = Tone("Win", 880, .2f);
            _left = Action("Left", "<Keyboard>/leftArrow", "<Keyboard>/a");
            _right = Action("Right", "<Keyboard>/rightArrow", "<Keyboard>/d");
            _fire = Action("Fire", "<Keyboard>/space");
            _thrust = Action("Thrust", "<Keyboard>/upArrow", "<Keyboard>/w");
            Button("Submit", () => _submitRequested = true, "<Keyboard>/enter");
            Button("Pause", () => _pauseRequested = true, "<Keyboard>/p", "<Keyboard>/escape");
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }
        private InputAction Action(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.Enable(); _actions.Add(action); return action;
        }
        private void Button(string name, System.Action callback, params string[] bindings)
        {
            Action(name, bindings).performed += _ => { if (_hasFocus && !EvidenceCapture.IsRunning) { callback(); } };
        }
        private void Update()
        {
            if (_hasFocus && !EvidenceCapture.IsRunning)
            {
                if (_backRequested) { Flow.Back(); BlockInput(); }
                else if (_pauseRequested) { Flow.TogglePause(); BlockInput(); }
                else if (_submitRequested) { Flow.Submit(); BlockInput(); }
                else
                {
                    if (_inputBlocked && !_left.IsPressed() && !_right.IsPressed() && !_thrust.IsPressed() && !_fire.IsPressed()) { _inputBlocked = false; }
                    if (!_inputBlocked) { Flow.SetInput(_left.IsPressed(), _right.IsPressed(), _thrust.IsPressed(), _fire.IsPressed()); }
                    if (!ManualTicks)
                    {
                        var score = Flow.Game?.Score ?? 0; var lives = Flow.Game?.Lives ?? 0; var screen = Flow.Screen;
                        Flow.Advance(Time.deltaTime); PlayEvents(score, lives, screen);
                    }
                }
            }
            _submitRequested = _pauseRequested = _backRequested = false;
            Render();
        }
        private void BlockInput() { _inputBlocked = true; Flow.SetInput(false, false, false, false); }
        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (!focus)
            {
                _submitRequested = _pauseRequested = _backRequested = false;
                if (Flow != null && !EvidenceCapture.IsRunning) { Flow.SetFocus(false); BlockInput(); }
            }
            else if (Flow != null) { Flow.SetFocus(true); }
        }
        public void Step(PlayerCommand command)
        {
            var score = Flow.Game?.Score ?? 0; var lives = Flow.Game?.Lives ?? 0; var screen = Flow.Screen;
            Flow.SetInput(command.Turn > 0, command.Turn < 0, command.Thrust, command.Fire); Flow.StepOneTick(); PlayEvents(score, lives, screen);
        }
        private void PlayEvents(int score, int lives, AppScreen screen)
        {
            if (Flow.Game == null) { return; }
            if (Flow.Screen == AppScreen.Result && screen != AppScreen.Result) { _audio.PlayOneShot(Flow.Game.Phase == GamePhase.Won ? _win : _lose); }
            else if (Flow.Game.Lives < lives) { _audio.PlayOneShot(_lose); }
            else if (Flow.Game.Score > score) { _audio.PlayOneShot(_hit); }
        }
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographic = true;
            _camera.orthographicSize = Mathf.Max(10.5f, 17f / _camera.aspect);
            _camera.transform.position = new Vector3(0, .3f, -10);
            var game = Flow.Game;
            var playing = game != null;
            Ensure(_rocks, (game?.AsteroidCount ?? 0) * 9, "Rock", _rockSprite, new Color(.65f, .8f, 1));
            Ensure(_bullets, (game?.BulletCount ?? 0) * 9, "Bullet", _pixel, new Color(1, .9f, .5f));
            Ensure(_ships, 9, "Ship", _shipSprite, new Color(.55f, 1, .75f));
            Ensure(_flames, 9, "Flame", _flameSprite, new Color(1, .6f, .3f));
            foreach (var renderer in _rocks) { renderer.enabled = false; }
            foreach (var renderer in _bullets) { renderer.enabled = false; }
            foreach (var renderer in _ships) { renderer.enabled = false; }
            foreach (var renderer in _flames) { renderer.enabled = false; }
            if (playing)
            {
                for (var i = 0; i < game.AsteroidCount; i++)
                {
                    var rock = game.AsteroidAt(i);
                    Periodic(_rocks, i * 9, rock.Position, _settings.Game.Radius(rock.Size), rock.Angle);
                }
                for (var i = 0; i < game.BulletCount; i++) { Periodic(_bullets, i * 9, game.BulletAt(i).Position, AsteroidsConfig.BulletRadius, 0); }
                if (game.Phase != GamePhase.Respawning && (game.InvincibleTicks == 0 || game.TickCount % 10 < 5))
                {
                    Periodic(_ships, 0, game.ShipPosition, .55f, game.ShipAngle);
                    if (Flow.Input.Thrust && game.Phase == GamePhase.Playing) { Periodic(_flames, 0, game.ShipPosition, .7f, game.ShipAngle); }
                }
            }
            RenderHud();
        }
        private void Ensure(List<SpriteRenderer> list, int count, string name, Sprite sprite, Color color)
        {
            while (list.Count < count) { list.Add(CreateSprite(name + list.Count, sprite, color, 1)); }
        }
        private static void Periodic(List<SpriteRenderer> renderers, int first, Point p, float radius, float angle)
        {
            var index = first;
            for (var y = -1; y <= 1; y++)
            {
                for (var x = -1; x <= 1; x++)
                {
                    var px = p.X + x * AsteroidsConfig.Width; var py = p.Y + y * AsteroidsConfig.Height;
                    var renderer = renderers[index++];
                    renderer.enabled = px + radius >= -16 && px - radius <= 16 && py + radius >= -9 && py - radius <= 9;
                    renderer.transform.localPosition = new Vector3(px, py, 0);
                    renderer.transform.localScale = new Vector3(radius * 2, radius * 2, 1);
                    renderer.transform.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
                }
            }
        }
        private Sprite Outline(Vector2[] points)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var colors = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var p = new Vector2((x - 31.5f) / 30, (y - 31.5f) / 30);
                    for (var i = 1; i < points.Length; i++)
                    {
                        var line = points[i] - points[i - 1];
                        var t = Mathf.Clamp01(Vector2.Dot(p - points[i - 1], line) / line.sqrMagnitude);
                        if ((p - points[i - 1] - line * t).sqrMagnitude < .0015f) { colors[y * size + x] = Color.white; break; }
                    }
                }
            }
            texture.SetPixels(colors); texture.Apply(); _textures.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
            _sprites.Add(sprite); return sprite;
        }
        private SpriteRenderer CreateSprite(string name, Sprite sprite, Color color, int order)
        {
            var item = new GameObject(name); item.transform.SetParent(transform, false);
            var renderer = item.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = order; return renderer;
        }
        private Sprite MakeSprite(params string[] rows)
        {
            var texture = new Texture2D(rows[0].Length, rows.Length, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            for (var y = 0; y < rows.Length; y++)
            {
                for (var x = 0; x < rows[0].Length; x++) { texture.SetPixel(x, rows.Length - 1 - y, rows[y][x] == '1' ? Color.white : Color.clear); }
            }
            texture.Apply(); _textures.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), texture.width);
            _sprites.Add(sprite); return sprite;
        }
        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_version == version && _root != null) { return; }
            _version = version; _root = root;
            _root.Q<Button>("submit").clicked += () => _submitRequested = true;
            _root.Q<Button>("resume").clicked += () => _pauseRequested = true;
            _root.Q<Button>("back").clicked += () => _backRequested = true;
            _root.Q<Button>("quit").clicked += () => { if (!EvidenceCapture.IsRunning) { Application.Quit(); } };
            _root.Query<Button>().ForEach(button => button.focusable = false);
            RenderHud();
        }
        private void RenderHud()
        {
            if (_root == null || Flow == null) { return; }
            var screen = Flow.Screen; var game = Flow.Game;
            _root.Q<Label>("score").text = "SCORE  " + (game?.Score ?? 0) + "     LIVES  " + (game?.Lives ?? _settings.Game.Lives) + "     WAVE  " + (game?.Wave ?? 1) + " / " + _settings.Game.WaveCount;
            _root.Q<VisualElement>("overlay").style.display = screen == AppScreen.Playing ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<Label>("headline").text = screen == AppScreen.Title ? "ORBIT BREAKER" : screen == AppScreen.Paused ? "PAUSED" : game?.Phase == GamePhase.Won ? "YOU WIN" : "GAME OVER";
            _root.Q<Label>("message").text = screen == AppScreen.Title ? $"Clear {_settings.Game.WaveCount} waves. Protect your {_settings.Game.Lives} lives.\nLarge rocks split into medium, then small.\n\nA D / Left Right : turn    W / Up : thrust\nSpace : fire    Enter : start / replay\nP / Esc : pause"
                : screen == AppScreen.Paused ? "P / Esc : resume" : "SCORE  " + (game?.Score ?? 0) + "\nEnter : play again";
            Show("submit", screen == AppScreen.Title || screen == AppScreen.Result);
            _root.Q<Button>("submit").text = screen == AppScreen.Title ? "START" : "PLAY AGAIN";
            Show("resume", screen == AppScreen.Paused); Show("back", screen == AppScreen.Paused || screen == AppScreen.Result); Show("quit", screen == AppScreen.Title);
            var status = _root.Q<Label>("status");
            if (status != null) { status.text = screen != AppScreen.Playing ? "" : game?.Phase == GamePhase.WaveTransition ? "WAVE CLEAR" : game?.Phase == GamePhase.Respawning ? "GET READY" : game?.InvincibleTicks > 0 ? "SHIELD ACTIVE" : ""; }
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
            Destroy(_hit); Destroy(_lose); Destroy(_win);
        }
    }
}
