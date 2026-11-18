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
    public sealed class MazeGame : MonoBehaviour
    {
        [SerializeField] private MazeSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private MazeView _view;

        private readonly List<InputAction> _actions = new();
        private readonly List<InputAction> _moves = new();
        private bool _submitRequested;
        private bool _pauseRequested;
        private bool _backRequested;
        private bool _targetsRequested;
        private bool _hasFocus = true;
        private bool _blocked;
        private int _captureMatch;
        private Direction _moveRequested = Direction.None;
        private VisualElement _root;
        private int _version = -1;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _dotSound;
        private AudioClip _powerSound;
        private AudioClip _captureSound;
        private AudioClip _missSound;
        private AudioClip _winSound;
        private AudioClip _overSound;

        public MazeFlow Flow { get; private set; }
        public MazeSettings Settings => _settings;
        public MazeView View => _view;
        public VisualElement HudRoot => _root;
        public bool ManualTicks { get; set; }
        public bool ShowTargets { get; set; }

        private void Awake()
        {
            _settings.Validate();
            UseConfig(_settings.Game);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _dotSound = Tone("Dot", 720, .03f);
            _powerSound = Tone("Power", 420, .2f);
            _captureSound = Tone("Capture", 900, .12f);
            _missSound = Tone("Miss", 150, .3f);
            _winSound = Tone("Win", 980, .25f);
            _overSound = Tone("Over", 120, .4f);

            Move(Direction.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            Move(Direction.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            Move(Direction.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            Move(Direction.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Action("Submit", "<Keyboard>/enter").performed += _ => { if (AcceptInput()) { _submitRequested = true; } };
            Action("Pause", "<Keyboard>/p", "<Keyboard>/escape").performed += _ => { if (AcceptInput()) { _pauseRequested = true; } };
            Action("Targets", "<Keyboard>/tab").performed += _ => { if (AcceptInput()) { _targetsRequested = true; } };

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        /// <summary>設定を差し替えてタイトルへ戻す。撮影台本がクリア場面を作るときにも使う。</summary>
        public void UseConfig(MazeGameConfig config) => Flow = new MazeFlow(config, NextSeed);

        // 撮影中は試合ごとに決まったシードを使い、同じビルドなら同じ画像になるようにする。
        private int NextSeed() => EvidenceCapture.IsRunning ? EvidenceCapture.Seed + _captureMatch++ : Environment.TickCount;

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private InputAction Action(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.Enable();
            _actions.Add(action);
            return action;
        }

        private void Move(Direction direction, params string[] bindings)
        {
            var action = Action(direction.ToString(), bindings);
            _moves.Add(action);
            action.performed += _ =>
            {
                if (AcceptInput() && !_blocked && (_moveRequested == Direction.None || direction < _moveRequested)) { _moveRequested = direction; }
            };
        }

        private void Update()
        {
            if (AcceptInput())
            {
                if (_targetsRequested) { ShowTargets = !ShowTargets; }
                if (_backRequested) { Flow.Back(); BlockInput(); }
                else if (_pauseRequested) { Flow.TogglePause(); BlockInput(); }
                else if (_submitRequested) { Flow.Submit(); BlockInput(); }
                else
                {
                    if (!_blocked) { Flow.RequestDirection(_moveRequested); }
                    if (_blocked && _moves.TrueForAll(action => !action.IsPressed())) { _blocked = false; }
                    if (!ManualTicks)
                    {
                        Flow.Advance(Time.deltaTime);
                        PlayEvents();
                    }
                }
            }

            _submitRequested = _pauseRequested = _backRequested = _targetsRequested = false;
            _moveRequested = Direction.None;
            Render();
        }

        private void BlockInput()
        {
            _blocked = true;
            _moveRequested = Direction.None;
        }

        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            Flow.SetFocus(focus);
            if (!focus)
            {
                _submitRequested = _pauseRequested = _backRequested = _targetsRequested = false;
                BlockInput();
            }
        }

        /// <summary>テストと撮影台本が1 Tick ずつ進めるための入口。</summary>
        public void Step(Direction direction = Direction.None)
        {
            Flow.RequestDirection(direction);
            Flow.StepOneTick();
            PlayEvents();
        }

        private void PlayEvents()
        {
            var events = Flow.TakeEvents();
            if (events == GameEvents.None) { return; }
            if ((events & GameEvents.Won) != 0) { _audio.PlayOneShot(_winSound); }
            else if ((events & GameEvents.GameOver) != 0) { _audio.PlayOneShot(_overSound); }
            else if ((events & GameEvents.PlayerCaught) != 0) { _audio.PlayOneShot(_missSound); }
            else if ((events & GameEvents.CatCaptured) != 0) { _audio.PlayOneShot(_captureSound); }
            else if ((events & GameEvents.PowerEaten) != 0) { _audio.PlayOneShot(_powerSound); }
            else if ((events & GameEvents.DotEaten) != 0) { _audio.PlayOneShot(_dotSound, .5f); }
        }

        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(8.6f, 15f / _camera.aspect);
            _camera.transform.position = new Vector3(0, .9f, -10);
            _view.Render(Flow.Game, _settings.Game.DyingTicks, ShowTargets);
            RenderHud();
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            root.style.unityFont = _font;
            root.Q<Button>("submit").clicked += () => { if (AcceptInput()) { _submitRequested = true; } };
            root.Q<Button>("resume").clicked += () => { if (AcceptInput()) { _pauseRequested = true; } };
            root.Q<Button>("back").clicked += () => { if (AcceptInput()) { _backRequested = true; } };
            root.Q<Button>("quit").clicked += () => { if (AcceptInput()) { Application.Quit(); } };
            root.Query<Button>().ForEach(button => button.focusable = false);
            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null || Flow == null) { return; }
            var screen = Flow.Screen;
            var game = Flow.Game;
            _root.Q<Label>("score").text = "とくてん  " + (game?.Score ?? 0);
            _root.Q<Label>("lives").text = "のこり  " + (game?.Lives ?? _settings.Game.Lives);
            _root.Q<Label>("pellets").text = "えさ  " + (game?.PelletsRemaining ?? _view.PelletCount);
            _root.Q<Label>("status").text = StatusText(game);
            _root.Q<VisualElement>("overlay").style.display = screen == AppScreen.Playing ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<Label>("headline").text = Headline(screen, game);
            _root.Q<Label>("message").text = Message(screen, game);
            Show("submit", screen == AppScreen.Title || screen == AppScreen.Result);
            _root.Q<Button>("submit").text = screen == AppScreen.Title ? "はじめる  /  Enter" : "もう一度  /  Enter";
            Show("resume", screen == AppScreen.Paused);
            Show("back", screen == AppScreen.Paused || screen == AppScreen.Result);
            Show("quit", screen == AppScreen.Title);
        }

        private static string StatusText(MazeGameState game)
        {
            if (game == null || game.IsOver) { return string.Empty; }
            if (game.Phase == GamePhase.Ready) { return "よーい…"; }
            if (game.Phase == GamePhase.Dying) { return "つかまった！"; }
            if (game.PowerTicksLeft > 0) { return "マタタビ  " + Mathf.CeilToInt(game.PowerTicksLeft / (float)MazeGameConfig.TickRate) + "秒"; }
            return game.IsChase ? "猫が追ってくる" : "猫は見回り中";
        }

        private static string Headline(AppScreen screen, MazeGameState game)
        {
            if (screen == AppScreen.Title) { return "NIBBLE MAZE"; }
            if (screen == AppScreen.Paused) { return "ひと休み"; }
            return game?.Phase == GamePhase.Won ? "ごちそうさま！" : "つかまった…";
        }

        private static string Message(AppScreen screen, MazeGameState game)
        {
            if (screen == AppScreen.Title)
            {
                return "夜の食料庫でパン屑をぜんぶ集めよう。\nマタタビを取ると猫がふらふら逃げ、触れると寝床へ帰る。\n\n"
                    + "トラ：まっすぐ追う　ミケ：先回り\nクロ：挟み撃ち　シロ：近づくと離れる\n\n"
                    + "方向キー / WASD：移動　P / Esc：停止　Tab：猫の狙いを表示";
            }

            if (screen == AppScreen.Paused) { return "P / Esc またはボタンで再開"; }
            var score = "とくてん  " + (game?.Score ?? 0);
            return game?.Phase == GamePhase.Won ? "パン屑をぜんぶ集めました。\n" + score : "残りのネズミがいなくなりました。\n" + score;
        }

        private void Show(string name, bool visible) =>
            _root.Q<Button>(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        private static AudioClip Tone(string name, int frequency, float seconds)
        {
            var samples = new float[(int)(44100 * seconds)];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = .05f * Mathf.Sin(2 * Mathf.PI * frequency * i / 44100f) * (1f - ((float)i / samples.Length));
            }

            var clip = AudioClip.Create(name, samples.Length, 1, 44100, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            if (_hudRenderer != null) { _hudRenderer.UnregisterUIReloadCallback(OnHudReloaded); }
            Destroy(_font);
            Destroy(_dotSound);
            Destroy(_powerSound);
            Destroy(_captureSound);
            Destroy(_missSound);
            Destroy(_winSound);
            Destroy(_overSound);
        }
    }
}
