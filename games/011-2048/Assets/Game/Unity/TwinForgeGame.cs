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
    public sealed class TwinForgeGame : MonoBehaviour
    {
        private const float ViewHalfHeight = 9f;
        private const float ViewHalfWidth = 16f;

        [SerializeField] private TwinForgeSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private TwinForgeView _view;

        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<Direction> _moves = new();
        private bool _submitRequested;
        private bool _backRequested;
        private bool _hasFocus = true;
        private int _baseSeed;
        private int _matchIndex;
        private ForgeGame _shownGame;
        private int _shownMoves;
        private VisualElement _root;
        private int _version = -1;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _moveSound;
        private AudioClip _mergeSound;
        private AudioClip _winSound;
        private AudioClip _loseSound;

        public ForgeFlow Flow { get; private set; }
        public TwinForgeSettings Settings => _settings;
        public TwinForgeView View => _view;
        public Camera ViewCamera => _camera;
        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        private void Awake()
        {
            _settings.Validate();
            _baseSeed = Environment.TickCount;
            Flow = new ForgeFlow(_settings.Rules, NextSeed);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _moveSound = Tone("Move", 240, .05f);
            _mergeSound = Tone("Merge", 620, .09f);
            _winSound = Tone("Win", 980, .4f);
            _loseSound = Tone("Lose", 130, .45f);

            MoveAction("Left", Direction.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            MoveAction("Right", Direction.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            MoveAction("Up", Direction.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            MoveAction("Down", Direction.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            Action("Submit", "<Keyboard>/enter", "<Keyboard>/numpadEnter").performed += _ => RequestSubmit();
            Action("Back", "<Keyboard>/escape").performed += _ => RequestBack();

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        // 起動時のシードから試合ごとに1ずつ進め、短い間隔で再戦しても同じ盤面にしない。
        // 撮影中は決まったシードから始め、同じビルドなら同じ画像になるようにする。
        private int NextSeed() => unchecked((EvidenceCapture.IsRunning ? EvidenceCapture.Seed : _baseSeed) + _matchIndex++);

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void RequestSubmit()
        {
            if (AcceptInput()) { _submitRequested = true; }
        }

        private void RequestBack()
        {
            if (AcceptInput()) { _backRequested = true; }
        }

        private void MoveAction(string name, Direction direction, params string[] bindings) =>
            Action(name, bindings).performed += _ => { if (AcceptInput()) { _moves.Add(direction); } };

        private InputAction Action(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.Enable();
            _actions.Add(action);
            return action;
        }

        private void Update()
        {
            if (AcceptInput())
            {
                if (_backRequested) { Flow.Back(); }
                else if (_submitRequested) { Flow.Submit(); }
                else { foreach (var direction in _moves) { Flow.Move(direction); } }
            }

            ClearRequests();
            PlayEvents();
            FrameCamera();
            SyncView();
            if (EvidenceCapture.IsRunning) { _view.Snap(); }
            else { _view.Tick(Time.deltaTime); }
            RenderHud();
        }

        private void ClearRequests()
        {
            _submitRequested = _backRequested = false;
            _moves.Clear();
        }

        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            Flow.SetFocus(focus);
            ClearRequests();
        }

        private void PlayEvents()
        {
            var events = Flow.TakeEvents();
            if ((events & FlowEvents.Won) != 0) { _audio.PlayOneShot(_winSound); }
            else if ((events & FlowEvents.Lost) != 0) { _audio.PlayOneShot(_loseSound); }
            else if ((events & FlowEvents.Merged) != 0) { _audio.PlayOneShot(_mergeSound); }
            else if ((events & FlowEvents.Moved) != 0) { _audio.PlayOneShot(_moveSound); }
        }

        // 同じフレームに複数の手が進んだときは最後の手だけを再生する。盤面の状態は Core で確定している。
        private void SyncView()
        {
            var game = Flow.Game;
            if (game != _shownGame)
            {
                _shownGame = game;
                _shownMoves = game?.MoveCount ?? 0;
                _view.Show(game?.Board);
                return;
            }

            if (game == null || game.MoveCount == _shownMoves) { return; }
            _shownMoves = game.MoveCount;
            _view.Play(game.LastOutcome, game.LastSpawn, game.Board, _settings.SlideSeconds, _settings.PopSeconds);
        }

        /// <summary>現在の状態を演出なしで描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            FrameCamera();
            SyncView();
            _view.Snap();
            RenderHud();
        }

        private void FrameCamera() => _camera.orthographicSize = Mathf.Max(ViewHalfHeight, ViewHalfWidth / _camera.aspect);

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            root.style.unityFont = _font;
            root.Q<Button>("submit").clicked += RequestSubmit;
            root.Q<Button>("back").clicked += RequestBack;
            root.Q<Button>("quit").clicked += () => { if (AcceptInput()) { QuitHandler(); } };
            root.Query<Button>().ForEach(button => button.focusable = false);
            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null || Flow == null) { return; }
            var screen = Flow.Screen;
            var game = Flow.Game;
            _root.Q<Label>("score").text = "スコア  " + (game?.Score ?? 0);
            _root.Q<Label>("moves").text = "手数  " + (game?.MoveCount ?? 0);
            _root.Q<Label>("headline").text = Headline(screen, game);
            _root.Q<Label>("message").text = Message(screen, game);
            Show("submit", screen != AppScreen.Playing);
            _root.Q<Button>("submit").text = screen == AppScreen.Title ? "はじめる  /  Enter" : "もう一度  /  Enter";
            Show("back", screen != AppScreen.Title);
            Show("quit", screen == AppScreen.Title);
        }

        private static string Headline(AppScreen screen, ForgeGame game)
        {
            if (screen == AppScreen.Title) { return "TWIN FORGE"; }
            if (screen == AppScreen.Playing) { return "鍛造中"; }
            return game?.Phase == GamePhase.Won ? "2048 完成！" : "手詰まり";
        }

        private static string Message(AppScreen screen, ForgeGame game)
        {
            const string controls = "矢印キー / WASD：板を動かす\nEsc：タイトルへ";
            if (screen == AppScreen.Title)
            {
                return "同じ数字の金属板をぶつけて鍛え、\n2048 の板を完成させよう。\n\n"
                    + "1回動かすごとに新しい板が現れます。\n動かせなくなると終わりです。\n\n" + controls;
            }

            if (screen == AppScreen.Playing) { return "同じ数字の板をぶつけると\n1枚に鍛え上がります。\n\n" + controls; }
            return game?.Phase == GamePhase.Won
                ? "最高の一枚を鍛え上げました。"
                : "動かせる板がなくなりました。";
        }

        private void Show(string name, bool visible) =>
            _root.Q<Button>(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        private AudioClip Tone(string name, int frequency, float seconds)
        {
            var samples = new float[(int)(44100 * seconds)];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = .05f * Mathf.Sin(2 * Mathf.PI * frequency * i / 44100f) * (1f - ((float)i / samples.Length));
            }

            var clip = AudioClip.Create(name, samples.Length, 1, 44100, false);
            clip.SetData(samples, 0);
            _clips.Add(clip);
            return clip;
        }

        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            if (_hudRenderer != null) { _hudRenderer.UnregisterUIReloadCallback(OnHudReloaded); }
            Destroy(_font);
            foreach (var clip in _clips) { Destroy(clip); }
        }
    }
}
