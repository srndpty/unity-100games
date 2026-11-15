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
    public sealed class InvadersGame : MonoBehaviour
    {
        [SerializeField] private InvadersSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        private readonly List<InputAction> _actions = new();
        private readonly List<SpriteRenderer> _enemies = new();
        private readonly List<SpriteRenderer> _shields = new();
        private readonly List<SpriteRenderer> _bullets = new();
        private readonly List<Sprite> _sprites = new();
        private readonly List<Texture2D> _textures = new();
        private InputAction _left;
        private InputAction _right;
        private InputAction _fire;
        private SpriteRenderer _player;
        private Sprite _pixel;
        private Sprite _enemySprite;
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
        public InvadersFlow Flow { get; private set; }
        public InvadersSettings Settings => _settings;
        public VisualElement HudRoot => _root;
        public bool ManualTicks { get; set; }

        private void Awake()
        {
            _settings.Validate();
            Flow = new InvadersFlow(_settings.Game, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _pixel = MakeSprite("1");
            _enemySprite = MakeSprite("00100100", "00011000", "00111100", "01111110", "11011011", "11111111", "00100100", "01000010");
            var ship = MakeSprite("00011000", "00011000", "00111100", "11111111", "11111111", "11111111", "11000011", "10000001");
            _player = CreateSprite("Player", ship, new Color(.55f, 1f, .75f), 2);
            var line = CreateSprite("InvasionLine", _pixel, new Color(.7f, .3f, .2f, .6f), -1);
            line.transform.localPosition = new Vector3(0, InvadersConfig.InvasionY, 0); line.transform.localScale = new Vector3(20, .025f, 1);
            var board = CreateSprite("Board", _pixel, new Color(.025f, .045f, .08f), -3);
            board.transform.localScale = new Vector3(20.6f, 15.4f, 1);
            for (var i = 0; i < 55; i++)
            {
                var star = CreateSprite("Star" + i, _pixel, new Color(.2f, .3f, .4f), -2);
                star.transform.localPosition = new Vector3((i * 47 % 197) / 10f - 9.8f, (i * 31 % 143) / 10f - 7, 0);
                star.transform.localScale = Vector3.one * .035f;
            }
            _audio = GetComponent<AudioSource>(); _audio.playOnAwake = false; _audio.mute = AudioPolicy.ShouldMute; _audio.volume = _settings.Volume;
            _hit = Tone("Hit", 480, .06f); _lose = Tone("Lose", 140, .2f); _win = Tone("Win", 880, .2f);
            _left = Action("Left", "<Keyboard>/leftArrow", "<Keyboard>/a");
            _right = Action("Right", "<Keyboard>/rightArrow", "<Keyboard>/d");
            _fire = Action("Fire", "<Keyboard>/space");
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
                    if (_inputBlocked && !_left.IsPressed() && !_right.IsPressed() && !_fire.IsPressed()) { _inputBlocked = false; }
                    if (!_inputBlocked) { Flow.SetInput(_left.IsPressed(), _right.IsPressed(), _fire.IsPressed()); }
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
        private void BlockInput() { _inputBlocked = true; Flow.SetInput(false, false, false); }
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
            Flow.SetInput(command.Move < 0, command.Move > 0, command.Fire); Flow.StepOneTick(); PlayEvents(score, lives, screen);
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
            _camera.orthographicSize = Mathf.Max(8.6f, 11f / _camera.aspect);
            _camera.transform.position = new Vector3(0, .3f, -10);
            var game = Flow.Game;
            Ensure(_enemies, game?.EnemyCapacity ?? 0, "Enemy", _enemySprite, Color.white);
            Ensure(_shields, game?.ShieldCapacity ?? 0, "Shield", _pixel, new Color(.3f, .8f, .85f));
            Ensure(_bullets, game?.BulletCount ?? 0, "Bullet", _pixel, Color.white);
            for (var i = 0; i < _enemies.Count; i++)
            {
                var visible = game != null && i < game.EnemyCapacity && game.EnemyAlive(i);
                _enemies[i].enabled = visible;
                if (visible)
                {
                    Place(_enemies[i], game.EnemyAt(i), 1f, .7f);
                    _enemies[i].color = i / _settings.Game.Columns % 2 == 0 ? new Color(1, .65f, .35f) : new Color(.8f, .55f, 1);
                }
            }
            for (var i = 0; i < _shields.Count; i++)
            {
                _shields[i].enabled = game != null && game.ShieldAlive(i);
                if (_shields[i].enabled) { Place(_shields[i], InvadersState.ShieldAt(i), .235f, .235f); }
            }
            for (var i = 0; i < _bullets.Count; i++)
            {
                _bullets[i].enabled = game != null && i < game.BulletCount;
                if (_bullets[i].enabled)
                {
                    var bullet = game.BulletAt(i); Place(_bullets[i], bullet.Position, .12f, .3f);
                    _bullets[i].color = bullet.Owner == BulletOwner.Player ? new Color(.7f, 1, .5f) : new Color(1, .35f, .4f);
                }
            }
            _player.enabled = game != null && (game.InvincibleTicks == 0 || game.TickCount % 10 < 5);
            if (game != null) { Place(_player, new Point(game.PlayerX, InvadersConfig.PlayerY), 1f, .6f); }
            RenderHud();
        }
        private void Ensure(List<SpriteRenderer> list, int count, string name, Sprite sprite, Color color)
        {
            while (list.Count < count) { list.Add(CreateSprite(name + list.Count, sprite, color, 1)); }
        }
        private static void Place(SpriteRenderer renderer, Point p, float width, float height)
        {
            renderer.transform.localPosition = new Vector3(p.X, p.Y, 0); renderer.transform.localScale = new Vector3(width, height, 1);
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
            _root.Q<Label>("score").text = "SCORE  " + (game?.Score ?? 0) + "     LIVES  " + (game?.Lives ?? 3) + "     WAVE  " + (game?.Wave ?? 1) + " / 3";
            _root.Q<VisualElement>("overlay").style.display = screen == AppScreen.Playing ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<Label>("headline").text = screen == AppScreen.Title ? "STAR GUARD" : screen == AppScreen.Paused ? "PAUSED" : game?.Phase == GamePhase.Won ? "YOU WIN" : "GAME OVER";
            _root.Q<Label>("message").text = screen == AppScreen.Title ? "Defeat 3 waves. Protect your 3 lives.\nBoth sides can destroy the shields.\n\nA D / Left Right : move    Space : fire\nEnter : start / replay    P / Esc : pause"
                : screen == AppScreen.Paused ? "P / Esc : resume" : "SCORE  " + (game?.Score ?? 0) + "\nEnter : play again";
            Show("submit", screen == AppScreen.Title || screen == AppScreen.Result);
            _root.Q<Button>("submit").text = screen == AppScreen.Title ? "START" : "PLAY AGAIN";
            Show("resume", screen == AppScreen.Paused); Show("back", screen == AppScreen.Paused || screen == AppScreen.Result); Show("quit", screen == AppScreen.Title);
            var status = _root.Q<Label>("status");
            if (status != null) { status.text = screen != AppScreen.Playing ? "" : game?.Phase == GamePhase.WaveTransition ? "WAVE CLEAR" : game?.Phase == GamePhase.Respawning ? "GET READY" : ""; }
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
