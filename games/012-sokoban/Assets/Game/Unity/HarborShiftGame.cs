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
    public sealed class HarborShiftGame : MonoBehaviour
    {
        private const float ViewHalfHeight = 9f;
        private const float ViewHalfWidth = 16f;

        [SerializeField] private HarborShiftSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private HarborShiftView _view;

        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();

        // 移動と取り消しの順序が入れ替わらないよう、すべての操作を押した順に1本の列で処理する。
        private readonly List<Intent> _intents = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private SokobanGame _shownGame;
        private int _shownRevision;
        private int _shownPreview = -1;
        private VisualElement _root;
        private int _version = -1;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _stepSound;
        private AudioClip _pushSound;
        private AudioClip _placeSound;
        private AudioClip _undoSound;
        private AudioClip _cursorSound;
        private AudioClip _clearSound;

        private enum IntentKind
        {
            Press,
            Release,
            Undo,
            Restart,
            Submit,
            Back,
            Select,
        }

        public HarborFlow Flow { get; private set; }
        public HarborShiftSettings Settings => _settings;
        public HarborShiftView View => _view;
        public Camera ViewCamera => _camera;
        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        private void Awake()
        {
            _settings.Validate();
            Flow = new HarborFlow(LevelCatalog.Maps, _settings.RepeatDelay, _settings.RepeatInterval);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _stepSound = Tone("Step", 180, .03f, .5f);
            _pushSound = Tone("Push", 120, .07f, 1f);
            _placeSound = Tone("Place", 660, .12f, 1f);
            _undoSound = Tone("Undo", 300, .05f, .7f);
            _cursorSound = Tone("Cursor", 440, .03f, .6f);
            _clearSound = Tone("Clear", 880, .4f, 1f);

            DirectionAction("Up", Direction.Up, "<Keyboard>/upArrow", "<Keyboard>/w");
            DirectionAction("Down", Direction.Down, "<Keyboard>/downArrow", "<Keyboard>/s");
            DirectionAction("Left", Direction.Left, "<Keyboard>/leftArrow", "<Keyboard>/a");
            DirectionAction("Right", Direction.Right, "<Keyboard>/rightArrow", "<Keyboard>/d");
            Action("Undo", "<Keyboard>/z", "<Keyboard>/backspace").performed += _ => Request(new Intent(IntentKind.Undo));
            Action("Restart", "<Keyboard>/r").performed += _ => Request(new Intent(IntentKind.Restart));
            Action("Submit", "<Keyboard>/enter", "<Keyboard>/numpadEnter").performed += _ => Request(new Intent(IntentKind.Submit));
            Action("Back", "<Keyboard>/escape").performed += _ => Request(new Intent(IntentKind.Back));

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Request(Intent intent)
        {
            if (AcceptInput()) { _intents.Add(intent); }
        }

        private void DirectionAction(string name, Direction direction, params string[] bindings)
        {
            var action = Action(name, bindings);
            action.performed += _ => Request(new Intent(IntentKind.Press, direction));
            action.canceled += _ => Request(new Intent(IntentKind.Release, direction));
        }

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
            // ウィンドウを選び直すクリックで操作しないよう、フォーカス復帰の直後は入力を捨てる。
            if (AcceptInput() && Time.frameCount > _focusRestoredFrame + 1)
            {
                foreach (var intent in _intents) { Apply(intent); }
                Flow.Tick(Time.deltaTime);
            }

            _intents.Clear();
            PlayEvents();
            FrameCamera();
            SyncView();
            if (EvidenceCapture.IsRunning) { _view.Snap(); }
            else { _view.Tick(Time.deltaTime); }
            RenderHud();
        }

        private void Apply(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.Press: Flow.PressDirection(intent.Direction); break;
                case IntentKind.Release: Flow.ReleaseDirection(intent.Direction); break;
                case IntentKind.Undo: Flow.Undo(); break;
                case IntentKind.Restart: Flow.Restart(); break;
                case IntentKind.Submit: Flow.Submit(); break;
                case IntentKind.Back: Flow.Back(); break;
                case IntentKind.Select: Flow.SelectLevel(intent.Level); break;
            }
        }

        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            Flow.SetFocus(focus);
            _intents.Clear();
            if (focus) { _focusRestoredFrame = Time.frameCount; }
        }

        private void PlayEvents()
        {
            var events = Flow.TakeEvents();
            if ((events & FlowEvents.Cleared) != 0) { _audio.PlayOneShot(_clearSound); }
            else if ((events & FlowEvents.BoxPlaced) != 0) { _audio.PlayOneShot(_placeSound); }
            else if ((events & FlowEvents.Pushed) != 0) { _audio.PlayOneShot(_pushSound); }
            else if ((events & (FlowEvents.Undone | FlowEvents.Restarted)) != 0) { _audio.PlayOneShot(_undoSound); }
            else if ((events & FlowEvents.Moved) != 0) { _audio.PlayOneShot(_stepSound); }
            else if ((events & FlowEvents.CursorMoved) != 0) { _audio.PlayOneShot(_cursorSound); }
        }

        // 同じフレームに複数の手が進んだときは演出せず、最終状態を描く。
        private void SyncView()
        {
            var game = Flow.Game;
            if (game == null)
            {
                _shownGame = null;
                if (_shownPreview == Flow.SelectedLevel) { return; }
                _shownPreview = Flow.SelectedLevel;
                var preview = LevelCatalog.Maps[_shownPreview];
                _view.Show(preview, preview.Initial, Direction.Down);
                return;
            }

            _shownPreview = -1;
            if (game != _shownGame)
            {
                _shownGame = game;
                _shownRevision = game.Revision;
                _view.Show(game.Map, game.Board, game.Facing);
                return;
            }

            if (game.Revision == _shownRevision) { return; }
            var singleStep = game.Revision == _shownRevision + 1;
            _shownRevision = game.Revision;
            if (singleStep && game.LastRecord != null)
            {
                _view.Play(game.LastRecord, game.LastAction == GameAction.Undo, game.Board, game.Facing, _settings.SlideSeconds);
            }
            else
            {
                _view.Show(game.Map, game.Board, game.Facing);
            }
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
            root.Q<Button>("submit").clicked += () => Request(new Intent(IntentKind.Submit));
            root.Q<Button>("undo").clicked += () => Request(new Intent(IntentKind.Undo));
            root.Q<Button>("restart").clicked += () => Request(new Intent(IntentKind.Restart));
            root.Q<Button>("back").clicked += () => Request(new Intent(IntentKind.Back));
            root.Q<Button>("quit").clicked += () => { if (AcceptInput()) { QuitHandler(); } };
            for (var i = 0; i < Flow.LevelCount; i++)
            {
                var level = i;
                root.Q<Button>("level-" + level).clicked += () => Request(new Intent(IntentKind.Select, level));
            }

            root.Query<Button>().ForEach(button => button.focusable = false);
            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null || Flow == null) { return; }
            var screen = Flow.Screen;
            var game = Flow.Game;
            var level = Flow.SelectedLevel;
            var best = Flow.Records.BestMoves(level);
            _root.Q<Label>("headline").text = Headline(screen);
            _root.Q<Label>("level").text = screen == AppScreen.Title ? string.Empty : $"第{level + 1}問  {LevelCatalog.Names[level]}";
            _root.Q<Label>("moves").text = "手数  " + (game?.MoveCount ?? 0);
            _root.Q<Label>("pushes").text = "押した数  " + (game?.PushCount ?? 0);
            _root.Q<Label>("best").text = "最少  " + (best?.ToString() ?? "-");
            _root.Q<Label>("message").text = Message(screen, game);

            _root.Q("stats").style.display = Display(screen != AppScreen.Title);
            _root.Q("levels").style.display = Display(screen == AppScreen.LevelSelect);
            for (var i = 0; i < Flow.LevelCount; i++)
            {
                var button = _root.Q<Button>("level-" + i);
                var cleared = Flow.Records.IsCleared(i);
                button.text = cleared ? (i + 1) + " 済" : (i + 1).ToString();
                button.EnableInClassList("level--selected", i == level);
                button.EnableInClassList("level--cleared", cleared);
            }

            var submit = _root.Q<Button>("submit");
            submit.style.display = Display(screen != AppScreen.Playing);
            submit.text = SubmitText(screen);
            _root.Q<Button>("undo").style.display = Display(screen == AppScreen.Playing);
            var restart = _root.Q<Button>("restart");
            restart.style.display = Display(screen == AppScreen.Playing || screen == AppScreen.Cleared);
            restart.text = screen == AppScreen.Cleared ? "もう一度  /  R" : "やり直し  /  R";
            var back = _root.Q<Button>("back");
            // 最終問のクリア後は決定ボタンが面選択へ戻るので、同じ行き先のボタンを並べない。
            back.style.display = Display(screen != AppScreen.Title && (screen != AppScreen.Cleared || Flow.HasNextLevel));
            back.text = screen == AppScreen.LevelSelect ? "タイトルへ  /  Esc" : "面選択へ  /  Esc";
            _root.Q<Button>("quit").style.display = Display(screen == AppScreen.Title);
        }

        private string Headline(AppScreen screen) => screen switch
        {
            AppScreen.Title => "HARBOR SHIFT",
            AppScreen.LevelSelect => "面を選ぶ",
            AppScreen.Playing => "荷役中",
            _ => Flow.Records.IsAllCleared ? "全問クリア！" : "荷おろし完了！",
        };

        private string SubmitText(AppScreen screen) => screen switch
        {
            AppScreen.Title => "はじめる  /  Enter",
            AppScreen.LevelSelect => "この面で遊ぶ  /  Enter",
            _ => Flow.HasNextLevel ? "次の面へ  /  Enter" : "面選択へ  /  Enter",
        };

        private string Message(AppScreen screen, SokobanGame game)
        {
            const string controls = "矢印キー / WASD：移動して箱を押す\nZ / Backspace：1手戻す\nR：やり直し\nEsc：面選択へ";
            switch (screen)
            {
                case AppScreen.Title:
                    return "港の倉庫で、荷箱をすべて黄色い印の上へ運ぼう。\n\n箱は押せますが、引けません。\n2個まとめても押せません。\n\n" + controls;
                case AppScreen.LevelSelect:
                    return $"クリア済み  {Flow.Records.ClearedCount} / {Flow.LevelCount}\n\n矢印キーで選び、Enter で始めます。";
                case AppScreen.Playing:
                    return controls;
                default:
                    return Flow.Records.IsAllCleared
                        ? $"全{Flow.LevelCount}問の荷おろしが終わりました。\nお疲れさまでした。\n\n{game?.MoveCount}手でクリア"
                        : $"{game?.MoveCount}手でクリアしました。";
            }
        }

        private static StyleEnum<DisplayStyle> Display(bool visible) => visible ? DisplayStyle.Flex : DisplayStyle.None;

        private AudioClip Tone(string name, int frequency, float seconds, float level)
        {
            var samples = new float[(int)(44100 * seconds)];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = .05f * level * Mathf.Sin(2 * Mathf.PI * frequency * i / 44100f) * (1f - ((float)i / samples.Length));
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

        private readonly struct Intent
        {
            public Intent(IntentKind kind, Direction direction = default, int level = 0)
            {
                Kind = kind;
                Direction = direction;
                Level = level;
            }

            public Intent(IntentKind kind, int level)
                : this(kind, default, level)
            {
            }

            public IntentKind Kind { get; }
            public Direction Direction { get; }
            public int Level { get; }
        }
    }
}
