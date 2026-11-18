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
    public sealed class SafeDigGame : MonoBehaviour
    {
        private const float BoardHalfHeight = 9f;
        private const float BoardHalfWidth = 16f;

        [SerializeField] private SafeDigSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private SafeDigView _view;

        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<(bool Flag, Vector2 Position)> _moves = new();
        private bool _submitRequested;
        private bool _backRequested;
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -2;
        private int _captureMatch;
        private VisualElement _root;
        private int _version = -1;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _openSound;
        private AudioClip _cascadeSound;
        private AudioClip _flagSound;
        private AudioClip _unflagSound;
        private AudioClip _explodeSound;
        private AudioClip _winSound;

        public MinesweeperFlow Flow { get; private set; }
        public SafeDigSettings Settings => _settings;
        public SafeDigView View => _view;
        public Camera ViewCamera => _camera;
        public VisualElement HudRoot => _root;

        private void Awake()
        {
            _settings.Validate();
            if (_view.Width != _settings.Board.Width || _view.Height != _settings.Board.Height)
            {
                throw new InvalidOperationException("Stage Prefab のセル数が盤面設定と一致しません。");
            }

            Flow = new MinesweeperFlow(_settings.Board, NextSeed);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _openSound = Tone("Open", 520, .05f);
            _cascadeSound = Tone("Cascade", 700, .14f);
            _flagSound = Tone("Flag", 880, .06f);
            _unflagSound = Tone("Unflag", 440, .06f);
            _explodeSound = Tone("Explode", 110, .45f);
            _winSound = Tone("Win", 980, .3f);

            Action("Reveal", "<Mouse>/leftButton").performed += context => QueueMove(false, context);
            Action("Flag", "<Mouse>/rightButton").performed += context => QueueMove(true, context);
            Action("Submit", "<Keyboard>/enter", "<Keyboard>/numpadEnter").performed += _ => { if (AcceptInput()) { _submitRequested = true; } };
            Action("Back", "<Keyboard>/escape").performed += _ => { if (AcceptInput()) { _backRequested = true; } };

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

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

        // 押した瞬間の位置で判定する。処理する Update までにカーソルが動いても別のセルを開かない。
        private void QueueMove(bool flag, InputAction.CallbackContext context)
        {
            if (AcceptInput() && context.control.device is Pointer pointer) { _moves.Add((flag, pointer.position.ReadValue())); }
        }

        private void Update()
        {
            if (AcceptInput())
            {
                if (_backRequested) { Flow.Back(); }
                else if (_submitRequested) { Flow.Submit(); }
                else if (Time.frameCount > _focusRestoredFrame + 1) { ApplyMoves(); }
            }

            ClearRequests();
            PlayEvents();
            Render();
        }

        private void ApplyMoves()
        {
            foreach (var (flag, position) in _moves)
            {
                if (!_view.TryCellAt(_camera.ScreenToWorldPoint(position), out var x, out var y)) { continue; }
                if (flag) { Flow.ToggleFlag(x, y); }
                else { Flow.Reveal(x, y); }
            }
        }

        private void ClearRequests()
        {
            _submitRequested = _backRequested = false;
            _moves.Clear();
        }

        // ウィンドウを選び直すクリックで盤面を掘らないよう、復帰直後の操作は捨てる。
        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            Flow.SetFocus(focus);
            ClearRequests();
            if (focus) { _focusRestoredFrame = Time.frameCount; }
        }

        private void PlayEvents()
        {
            var events = Flow.TakeEvents();
            if ((events & FlowEvents.Won) != 0) { _audio.PlayOneShot(_winSound); }
            else if ((events & FlowEvents.Exploded) != 0) { _audio.PlayOneShot(_explodeSound); }
            else if ((events & FlowEvents.Cascade) != 0) { _audio.PlayOneShot(_cascadeSound); }
            else if ((events & FlowEvents.Flagged) != 0) { _audio.PlayOneShot(_flagSound); }
            else if ((events & FlowEvents.Unflagged) != 0) { _audio.PlayOneShot(_unflagSound); }
            else if ((events & FlowEvents.Opened) != 0) { _audio.PlayOneShot(_openSound); }
        }

        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(BoardHalfHeight, BoardHalfWidth / _camera.aspect);
            _view.Render(Flow.Game, HoverCell());
            RenderHud();
        }

        private Vector2Int? HoverCell()
        {
            if (Flow.Screen != AppScreen.Playing || EvidenceCapture.IsRunning || Mouse.current == null) { return null; }
            var world = _camera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            return _view.TryCellAt(world, out var x, out var y) ? new Vector2Int(x, y) : null;
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            root.style.unityFont = _font;
            root.Q<Button>("submit").clicked += () => { if (AcceptInput()) { _submitRequested = true; } };
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
            _root.Q<Label>("mines").text = "のこり爆弾  " + (game?.RemainingMines ?? _settings.Board.MineCount);
            _root.Q<Label>("safe").text = "未発掘  " + (game?.SafeCellsLeft ?? _settings.Board.SafeCellCount);
            _root.Q<Label>("headline").text = Headline(screen, game);
            _root.Q<Label>("message").text = Message(screen, game);
            Show("submit", screen != AppScreen.Playing);
            _root.Q<Button>("submit").text = screen == AppScreen.Title ? "はじめる  /  Enter" : "もう一度  /  Enter";
            Show("back", screen != AppScreen.Title);
            Show("quit", screen == AppScreen.Title);
        }

        private static string Headline(AppScreen screen, MinesweeperGame game)
        {
            if (screen == AppScreen.Title) { return "SAFE DIG"; }
            if (screen == AppScreen.Playing) { return "発掘中"; }
            return game?.Phase == GamePhase.Won ? "掘りきった！" : "ドカン！";
        }

        private static string Message(AppScreen screen, MinesweeperGame game)
        {
            const string controls = "左クリック：掘る\n右クリック：旗を立てる / 外す\nEsc：タイトルへ";
            if (screen == AppScreen.Title)
            {
                return "草原に埋まった爆弾を避けて、\n安全な土をすべて掘り起こそう。\n\n"
                    + "数字は周囲8マスにある爆弾の数。\n最初のひと掘りは必ず安全です。\n\n" + controls;
            }

            if (screen == AppScreen.Playing) { return controls; }
            return game?.Phase == GamePhase.Won
                ? "爆弾をすべて避けて掘り終えました。"
                : "爆弾を掘り当ててしまいました。\n×印の旗は置き間違いです。";
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
