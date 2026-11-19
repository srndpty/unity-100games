using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using Grid = Game.Core.Grid;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class NineLatticeGame : MonoBehaviour
    {
        private const float ViewHalfHeight = 5.1f;
        private const float ViewHalfWidth = 9.0f;
        private const int TitleSeed = 0;
        private const string Controls = "マウス・矢印・WASD：マスを選ぶ\n1〜9：数字を入れる ／ Space：数字と候補の切り替え\nDel・BS・0：消す ／ H：ヒント ／ Esc・P：一時停止";

        [SerializeField] private NineLatticeSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private NineLatticeBoardView _view;

        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();

        // 押した順序が入れ替わらないよう、マウス・キー・ボタンのすべての操作を 1 本の列で処理する。
        private readonly List<Intent> _intents = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private Grid _titleGivens;
        private VisualElement _root;
        private int _version = -1;
        private VisualElement _stats;
        private VisualElement _overlay;
        private Label _timeLabel;
        private Label _hintsLabel;
        private Label _modeLabel;
        private Label _statusLabel;
        private Label _headlineLabel;
        private Label _messageLabel;
        private Button _noteButton;
        private Button _submitButton;
        private Button _restartButton;
        private Button _titleButton;
        private Button _quitButton;
        private VisualElement _options;
        private Label _difficultyLabel;
        private Button[] _difficultyButtons;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _placeSound;
        private AudioClip _noteSound;
        private AudioClip _eraseSound;
        private AudioClip _rejectSound;
        private AudioClip _hintSound;
        private AudioClip _wrongSound;
        private AudioClip _solvedSound;

        private enum IntentKind
        {
            PointerDown,
            Navigate,
            Digit,
            Erase,
            NoteMode,
            Hint,
            Submit,
            Pause,
            Restart,
            Title,
            Quit,
            Difficulty,
        }

        public SudokuFlow Flow { get; private set; }
        public NineLatticeSettings Settings => _settings;
        public NineLatticeBoardView View => _view;
        public Camera ViewCamera => _camera;
        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        /// <summary>ヒントで指したマスを光らせている間はそのマス、なければ -1。</summary>
        public int GlowingHintCell =>
            Flow.LastHint is Hint hint && Flow.ElapsedTicks - Flow.HintTick < _settings.HintGlowTicks ? hint.Cell : -1;

        private void Awake()
        {
            _settings.Validate();

            // 撮影中は決まったシードから始め、同じビルドなら同じ画像になるようにする。再戦は Flow が試合ごとにシードを進める。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            Flow = new SudokuFlow(_settings.Rules, seed);
            _titleGivens = PuzzleGenerator.Generate(TitleSeed, _settings.Rules, Difficulty.Normal).Givens;
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _placeSound = Tone("Place", 660, .06f, .7f);
            _noteSound = Tone("Note", 990, .04f, .4f);
            _eraseSound = Tone("Erase", 330, .06f, .6f);
            _rejectSound = Tone("Reject", 140, .14f, .8f);
            _hintSound = Tone("Hint", 784, .18f, .6f);
            _wrongSound = Tone("Wrong", 220, .3f, .8f);
            _solvedSound = Tone("Solved", 1046, .5f, 1f);

            Action("Press", "<Mouse>/leftButton").performed += PointerPressed;
            BindNavigate("Left", -1, 0, "<Keyboard>/leftArrow", "<Keyboard>/a");
            BindNavigate("Right", 1, 0, "<Keyboard>/rightArrow", "<Keyboard>/d");
            BindNavigate("Up", 0, -1, "<Keyboard>/upArrow", "<Keyboard>/w");
            BindNavigate("Down", 0, 1, "<Keyboard>/downArrow", "<Keyboard>/s");
            for (var digit = 1; digit <= Board.Size; digit++)
            {
                var value = digit;
                Action("Digit" + digit, $"<Keyboard>/{digit}", $"<Keyboard>/numpad{digit}").performed += _ => Request(new Intent(IntentKind.Digit, value));
            }

            Action("Erase", "<Keyboard>/delete", "<Keyboard>/backspace", "<Keyboard>/0", "<Keyboard>/numpad0").performed += _ => Request(new Intent(IntentKind.Erase));
            Action("NoteMode", "<Keyboard>/space").performed += _ => Request(new Intent(IntentKind.NoteMode));
            Action("Hint", "<Keyboard>/h").performed += _ => Request(new Intent(IntentKind.Hint));
            Action("Submit", "<Keyboard>/enter", "<Keyboard>/numpadEnter").performed += _ => Request(new Intent(IntentKind.Submit));
            Action("Pause", "<Keyboard>/escape", "<Keyboard>/p").performed += _ => Request(new Intent(IntentKind.Pause));
            Action("Restart", "<Keyboard>/r").performed += _ => Request(new Intent(IntentKind.Restart));
            Action("Title", "<Keyboard>/t").performed += _ => Request(new Intent(IntentKind.Title));

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Request(Intent intent)
        {
            if (AcceptInput()) { _intents.Add(intent); }
        }

        // 押した瞬間の位置で判定する。処理する Update までにカーソルが動いても別のマスを選ばない。
        private void PointerPressed(InputAction.CallbackContext context)
        {
            if (context.control.device is Pointer pointer) { Request(new Intent(IntentKind.PointerDown, 0, pointer.position.ReadValue())); }
        }

        private void BindNavigate(string name, int dx, int dy, params string[] bindings) =>
            Action(name, bindings).performed += _ => Request(new Intent(IntentKind.Navigate, 0, new Vector2(dx, dy)));

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
                Flow.Advance(Time.deltaTime);
            }

            _intents.Clear();
            PlayEvents();
            Render();
        }

        private void Apply(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.PointerDown:
                    var board = ScreenToBoard(intent.Point);
                    Flow.PointerDown(board.x, board.y);
                    break;
                case IntentKind.Navigate: Flow.Navigate((int)intent.Point.x, (int)intent.Point.y); break;
                case IntentKind.Difficulty: Flow.SelectDifficulty((Difficulty)intent.Value); break;
                case IntentKind.Digit: Flow.InputDigit(intent.Value); break;
                case IntentKind.Erase: Flow.Erase(); break;
                case IntentKind.NoteMode: Flow.ToggleNoteMode(); break;
                case IntentKind.Hint: Flow.RequestHint(); break;
                case IntentKind.Submit: Flow.Submit(); break;
                case IntentKind.Pause: Flow.TogglePause(); break;
                case IntentKind.Restart: Flow.Restart(); break;
                case IntentKind.Title: Flow.BackToTitle(); break;
                case IntentKind.Quit when Flow.Screen == AppScreen.Title: QuitHandler(); break;
            }
        }

        private Vector2 ScreenToBoard(Vector2 screen) => _view.ToBoard(_camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z)));

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
            if ((events & FlowEvents.Solved) != 0) { _audio.PlayOneShot(_solvedSound); }
            else if ((events & FlowEvents.FullButWrong) != 0) { _audio.PlayOneShot(_wrongSound); }
            else if ((events & FlowEvents.Hinted) != 0) { _audio.PlayOneShot(_hintSound); }
            else if ((events & FlowEvents.Rejected) != 0) { _audio.PlayOneShot(_rejectSound); }
            else if ((events & FlowEvents.Placed) != 0) { _audio.PlayOneShot(_placeSound); }
            else if ((events & FlowEvents.Noted) != 0) { _audio.PlayOneShot(_noteSound); }
            else if ((events & FlowEvents.Erased) != 0) { _audio.PlayOneShot(_eraseSound); }
        }

        /// <summary>現在の状態を描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(ViewHalfHeight, ViewHalfWidth / _camera.aspect);
            var game = Flow.Game;
            if (game == null) { _view.RenderStill(_titleGivens); }
            else
            {
                var playing = Flow.Screen == AppScreen.Playing;
                _view.Render(game, playing ? Flow.Cursor.Cell : (int?)null, playing ? Flow.ConflictCell : -1, playing ? GlowingHintCell : -1);
            }

            RenderHud();
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _root = root;
            _version = version;
            root.style.unityFont = _font;
            _stats = root.Q("stats");
            _overlay = root.Q("overlay");
            _timeLabel = root.Q<Label>("time");
            _hintsLabel = root.Q<Label>("hints");
            _modeLabel = root.Q<Label>("mode");
            _statusLabel = root.Q<Label>("status");
            root.Q<Label>("controls").text = Controls;
            _headlineLabel = root.Q<Label>("headline");
            _messageLabel = root.Q<Label>("message");
            _noteButton = root.Q<Button>("pad-note");
            _submitButton = root.Q<Button>("submit");
            _restartButton = root.Q<Button>("restart");
            _titleButton = root.Q<Button>("title");
            _quitButton = root.Q<Button>("quit");
            _options = root.Q("options");
            _difficultyLabel = root.Q<Label>("difficulty");
            _difficultyButtons = new[] { root.Q<Button>("difficulty-easy"), root.Q<Button>("difficulty-normal"), root.Q<Button>("difficulty-hard") };
            for (var i = 0; i < _difficultyButtons.Length; i++)
            {
                var difficulty = i;
                _difficultyButtons[i].clicked += () => Request(new Intent(IntentKind.Difficulty, difficulty));
            }

            for (var digit = 1; digit <= Board.Size; digit++)
            {
                var value = digit;
                root.Q<Button>("pad-" + digit).clicked += () => Request(new Intent(IntentKind.Digit, value));
            }

            root.Q<Button>("pad-erase").clicked += () => Request(new Intent(IntentKind.Erase));
            root.Q<Button>("pad-hint").clicked += () => Request(new Intent(IntentKind.Hint));
            _noteButton.clicked += () => Request(new Intent(IntentKind.NoteMode));
            _submitButton.clicked += () => Request(new Intent(IntentKind.Submit));
            _restartButton.clicked += () => Request(new Intent(IntentKind.Restart));
            _titleButton.clicked += () => Request(new Intent(IntentKind.Title));
            _quitButton.clicked += () => Request(new Intent(IntentKind.Quit));

            root.Query<Button>().ForEach(button => button.focusable = false);
            RenderHud();
        }

        private void RenderHud()
        {
            if (_root == null) { return; }
            var screen = Flow.Screen;
            var game = Flow.Game;

            // パネルの裏に数字が透けないよう、プレイ中だけ表示する。結果は案内の中に書く。
            Show(_stats, screen == AppScreen.Playing);
            SetText(_timeLabel, "時間  " + Clock(Flow.ElapsedTicks));
            SetText(_hintsLabel, $"ヒント  {game?.HintsUsed ?? 0} 回");
            SetText(_difficultyLabel, "難しさ：" + DifficultyName(Flow.MatchDifficulty));
            SetText(_modeLabel, Flow.NoteMode ? "入力：候補（メモ）" : "入力：数字");
            if (_noteButton.ClassListContains("pad--on") != Flow.NoteMode) { _noteButton.EnableInClassList("pad--on", Flow.NoteMode); }
            SetText(_statusLabel, game == null ? string.Empty : Status(game));

            Show(_overlay, screen != AppScreen.Playing);
            Show(_options, screen == AppScreen.Title || screen == AppScreen.Result);
            for (var i = 0; i < _difficultyButtons.Length; i++)
            {
                var selected = (int)Flow.Difficulty == i;
                if (_difficultyButtons[i].ClassListContains("option--selected") != selected) { _difficultyButtons[i].EnableInClassList("option--selected", selected); }
            }

            SetText(_headlineLabel, Headline(screen));
            SetText(_messageLabel, Message(screen, game));
            SetText(_submitButton, screen == AppScreen.Title ? "はじめる  /  Enter" : screen == AppScreen.Paused ? "再開  /  Enter" : "次の問題  /  Enter");
            Show(_restartButton, screen == AppScreen.Paused);
            Show(_titleButton, screen == AppScreen.Paused || screen == AppScreen.Result);
            Show(_quitButton, screen == AppScreen.Title);
        }

        /// <summary>プレイ中の案内。直前の操作の結果を説明する。</summary>
        public string Status(SudokuGame game)
        {
            switch (Flow.Notice)
            {
                case Notice.Conflict:
                    var conflict = Flow.ConflictCell;
                    return $"同じ{UnitName(Flow.Cursor.Cell, conflict)}に {game.ValueAt(conflict)} があります。";
                case Notice.Given:
                    return "初めから入っている数字は変えられません。";
                case Notice.Filled:
                    return "数字の入ったマスには候補を書けません。";
                case Notice.FullButWrong:
                    return "全部埋まりましたが、どこかが違います。\nH で間違いの場所を出せます。";
                case Notice.HintMistake:
                    return $"ここの {Flow.LastHint!.Value.Digit} は正解と違います。\n消して入れ直してください。";
                case Notice.HintDeduced:
                    var deduction = Flow.LastHint!.Value.Deduction!.Value;
                    return deduction.Reason == DeductionReason.NakedSingle
                        ? $"このマスに入るのは {deduction.Digit} だけです。"
                        : $"この{KindName(Board.KindOf(deduction.Unit))}で {deduction.Digit} が入るのはここだけです。";
                case Notice.HintRevealed:
                    return "理詰めでは決まらないので、\n正解を 1 つ入れました。";
                default:
                    return Flow.NoteMode ? "1〜9 で候補を付け外しします。" : "マスを選んで 1〜9 を入れてください。";
            }
        }

        public static string DifficultyName(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => "やさしい",
            Difficulty.Normal => "ふつう",
            _ => "むずかしい",
        };

        private static string UnitName(int a, int b) =>
            Board.Row(a) == Board.Row(b) ? "行" : Board.Column(a) == Board.Column(b) ? "列" : "ブロック";

        private static string KindName(UnitKind kind) => kind switch
        {
            UnitKind.Row => "行",
            UnitKind.Column => "列",
            _ => "ブロック",
        };

        private static string Headline(AppScreen screen) => screen switch
        {
            AppScreen.Title => "NINE LATTICE",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => "完成！",
            _ => string.Empty,
        };

        private string Message(AppScreen screen, SudokuGame game)
        {
            switch (screen)
            {
                case AppScreen.Title:
                    return "どの行・列・3×3 のブロックにも 1〜9 が 1 つずつ入るように、空いたマスを埋めてください。答えはどの問題も 1 通りです。\nやさしい・ふつうは、仮置きせずに理詰めだけで解けます。\n\n" + Controls;
                case AppScreen.Paused:
                    return "Enter：再開\nR：別の問題\nT：タイトルへ";
                case AppScreen.Result:
                    return $"難しさ  {DifficultyName(Flow.MatchDifficulty)}\n時間  {Clock(Flow.ElapsedTicks)}\nヒント  {game.HintsUsed} 回\n\n次の問題は別の盤になります。";
                default:
                    return string.Empty;
            }
        }

        /// <summary>Tick 数を「分:秒」（1 時間以上は「時:分:秒」）にする。</summary>
        public static string Clock(int ticks)
        {
            var seconds = ticks / SudokuFlow.TickRate;
            return seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
        }

        private static void SetText(TextElement element, string text)
        {
            if (element.text != text) { element.text = text; }
        }

        private static void Show(VisualElement element, bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display) { element.style.display = display; }
        }

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
            public Intent(IntentKind kind, int value = 0, Vector2 point = default)
            {
                Kind = kind;
                Value = value;
                Point = point;
            }

            public IntentKind Kind { get; }
            public int Value { get; }
            public Vector2 Point { get; }
        }
    }
}
