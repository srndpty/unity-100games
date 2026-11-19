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
    public sealed class SevenLanternsGame : MonoBehaviour
    {
        private const float ViewHalfHeight = 5.4f;
        private const float ViewHalfWidth = 9.4f;
        private const int TitleSeed = 0;
        private const string Controls =
            "ドラッグ：札を重ねる ／ クリック→クリック：選んで置く\nダブルクリック：組札へ ／ 山札をクリック：めくる\nZ・BS：1 手戻す ／ Esc・P：一時停止";

        [SerializeField] private SevenLanternsSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private SevenLanternsTableView _view;

        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();

        // 押した順序が入れ替わらないよう、マウス・キー・ボタンのすべての操作を 1 本の列で処理する。
        private readonly List<Intent> _intents = new();
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private Table _titleTable;
        private VisualElement _root;
        private int _version = -1;
        private VisualElement _stats;
        private VisualElement _overlay;
        private Label _movesLabel;
        private Label _timeLabel;
        private Label _foundationLabel;
        private Label _modeLabel;
        private Label _statusLabel;
        private Label _headlineLabel;
        private Label _messageLabel;
        private Button _undoButton;
        private Button _submitButton;
        private Button _restartButton;
        private Button _resignButton;
        private Button _titleButton;
        private Button _quitButton;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _moveSound;
        private AudioClip _foundationSound;
        private AudioClip _flipSound;
        private AudioClip _drawSound;
        private AudioClip _recycleSound;
        private AudioClip _rejectSound;
        private AudioClip _undoSound;
        private AudioClip _wonSound;
        private AudioClip _stuckSound;
        private AudioClip _resignSound;

        private enum IntentKind
        {
            PointerDown,
            PointerUp,
            Undo,
            Submit,
            Pause,
            Restart,
            Resign,
            Title,
            Quit,
        }

        public SolitaireFlow Flow { get; private set; }
        public SevenLanternsSettings Settings => _settings;
        public SevenLanternsTableView View => _view;
        public Camera ViewCamera => _camera;
        public VisualElement HudRoot => _root;

        /// <summary>終了ボタンの処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        private void Awake()
        {
            _settings.Validate();

            // 撮影中は決まったシードから始め、同じビルドなら同じ画像になるようにする。再戦は Flow が試合ごとにシードを進める。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            Flow = new SolitaireFlow(_settings.Rules, seed, _settings.DoubleClickSeconds);
            _titleTable = Deal.Create(TitleSeed, _settings.Rules.DrawCount);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;
            _moveSound = Tone("Move", 523, .05f, .6f);
            _foundationSound = Tone("Foundation", 784, .1f, .7f);
            _flipSound = Tone("Flip", 659, .06f, .5f);
            _drawSound = Tone("Draw", 392, .04f, .4f);
            _recycleSound = Tone("Recycle", 262, .12f, .5f);
            _rejectSound = Tone("Reject", 140, .14f, .8f);
            _undoSound = Tone("Undo", 330, .06f, .5f);
            _wonSound = Tone("Won", 1046, .5f, 1f);
            _stuckSound = Tone("Stuck", 196, .45f, .8f);
            _resignSound = Tone("Resign", 220, .3f, .6f);

            var press = Action("Press", "<Mouse>/leftButton");
            press.performed += context => PointerChanged(context, IntentKind.PointerDown);
            press.canceled += context => PointerChanged(context, IntentKind.PointerUp);
            Action("Undo", "<Keyboard>/z", "<Keyboard>/backspace").performed += _ => Request(new Intent(IntentKind.Undo));
            Action("Submit", "<Keyboard>/enter", "<Keyboard>/numpadEnter").performed += _ => Request(new Intent(IntentKind.Submit));
            Action("Pause", "<Keyboard>/escape", "<Keyboard>/p").performed += _ => Request(new Intent(IntentKind.Pause));
            Action("Restart", "<Keyboard>/r").performed += _ => Request(new Intent(IntentKind.Restart));
            Action("Resign", "<Keyboard>/a").performed += _ => Request(new Intent(IntentKind.Resign));
            Action("Title", "<Keyboard>/t").performed += _ => Request(new Intent(IntentKind.Title));

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _view.Arrange();
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render(0);
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        private void Request(Intent intent)
        {
            if (AcceptInput()) { _intents.Add(intent); }
        }

        // 押した・離した瞬間の位置と時刻で判定する。処理する Update までにカーソルが動いても別の札を掴まない。
        private void PointerChanged(InputAction.CallbackContext context, IntentKind kind)
        {
            if (context.control.device is Pointer pointer)
            {
                Request(new Intent(kind, pointer.position.ReadValue(), Time.realtimeSinceStartupAsDouble));
            }
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
                if (Flow.Input.IsPressed && Pointer.current is Pointer pointer) { Flow.PointerMove(ScreenToTable(pointer.position.ReadValue())); }

                // 経過時間は遊んだ時間の計測なので、maximumDeltaTime で切られる deltaTime ではなく実時間を渡す。
                Flow.Advance(Time.unscaledDeltaTime);
            }

            _intents.Clear();
            PlayEvents();
            Render(Time.unscaledDeltaTime);
        }

        private void Apply(Intent intent)
        {
            switch (intent.Kind)
            {
                case IntentKind.PointerDown: Flow.PointerDown(ScreenToTable(intent.Point), intent.Time); break;
                case IntentKind.PointerUp: Flow.PointerUp(ScreenToTable(intent.Point), intent.Time); break;
                case IntentKind.Undo: Flow.Undo(); break;
                case IntentKind.Submit: Flow.Submit(); break;
                case IntentKind.Pause: Flow.TogglePause(); break;
                case IntentKind.Restart: Flow.Restart(); break;
                case IntentKind.Resign: Flow.Resign(); break;
                case IntentKind.Title: Flow.BackToTitle(); break;
                case IntentKind.Quit when Flow.Screen == AppScreen.Title: QuitHandler(); break;
            }
        }

        /// <summary>画面の座標を卓の座標に直す。</summary>
        public TablePoint ScreenToTable(Vector2 screen)
        {
            var world = _view.transform.InverseTransformPoint(_camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z)));
            return new TablePoint(world.x, world.y);
        }

        /// <summary>卓の座標を画面の座標に直す（テストと撮影でクリックの位置を作る）。</summary>
        public Vector2 TableToScreen(TablePoint point) =>
            _camera.WorldToScreenPoint(_view.transform.TransformPoint(new Vector3((float)point.X, (float)point.Y, 0)));

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
            if ((events & FlowEvents.Won) != 0) { _audio.PlayOneShot(_wonSound); }
            else if ((events & FlowEvents.Stuck) != 0) { _audio.PlayOneShot(_stuckSound); }
            else if ((events & FlowEvents.Resigned) != 0) { _audio.PlayOneShot(_resignSound); }
            else if ((events & FlowEvents.Rejected) != 0) { _audio.PlayOneShot(_rejectSound); }
            else if ((events & FlowEvents.Undone) != 0) { _audio.PlayOneShot(_undoSound); }
            else if ((events & FlowEvents.ToFoundation) != 0) { _audio.PlayOneShot(_foundationSound); }
            else if ((events & FlowEvents.Flipped) != 0) { _audio.PlayOneShot(_flipSound); }
            else if ((events & FlowEvents.Moved) != 0) { _audio.PlayOneShot(_moveSound); }
            else if ((events & FlowEvents.Recycled) != 0) { _audio.PlayOneShot(_recycleSound); }
            else if ((events & FlowEvents.Drawn) != 0) { _audio.PlayOneShot(_drawSound); }
        }

        /// <summary>現在の状態を描く。deltaTime は札を寄せる実時間（撮影中は札を即座に置く）。</summary>
        public void Render(float deltaTime)
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(ViewHalfHeight, ViewHalfWidth / _camera.aspect);
            var sharpness = EvidenceCapture.IsRunning ? 0f : _settings.SlideSharpness;
            var game = Flow.Game;
            if (game == null) { _view.Render(_titleTable, null, null, default, deltaTime, sharpness); }
            else
            {
                var input = Flow.Input;
                _view.Render(game.Table, input.Selected, input.Dragging, input.DragBaseCenter, deltaTime, sharpness);
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
            _movesLabel = root.Q<Label>("moves");
            _timeLabel = root.Q<Label>("time");
            _foundationLabel = root.Q<Label>("foundation");
            _modeLabel = root.Q<Label>("mode");
            _statusLabel = root.Q<Label>("status");
            root.Q<Label>("controls").text = Controls;
            _headlineLabel = root.Q<Label>("headline");
            _messageLabel = root.Q<Label>("message");
            _undoButton = root.Q<Button>("undo");
            _submitButton = root.Q<Button>("submit");
            _restartButton = root.Q<Button>("restart");
            _resignButton = root.Q<Button>("resign");
            _titleButton = root.Q<Button>("title");
            _quitButton = root.Q<Button>("quit");

            _undoButton.clicked += () => Request(new Intent(IntentKind.Undo));
            root.Q<Button>("pause").clicked += () => Request(new Intent(IntentKind.Pause));
            _submitButton.clicked += () => Request(new Intent(IntentKind.Submit));
            _restartButton.clicked += () => Request(new Intent(IntentKind.Restart));
            _resignButton.clicked += () => Request(new Intent(IntentKind.Resign));
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

            Show(_stats, screen == AppScreen.Playing);
            SetText(_movesLabel, $"手数  {game?.Moves ?? 0}");
            SetText(_timeLabel, "時間  " + Clock(Flow.ElapsedTicks));
            SetText(_foundationLabel, $"組札  {game?.Table.FoundationTotal ?? 0} / {Card.DeckSize}");
            SetText(_modeLabel, DrawModeName(game?.Table.DrawCount ?? Flow.Config.DrawCount));
            SetText(_statusLabel, game == null ? string.Empty : Status());
            _undoButton.SetEnabled(game?.CanUndo ?? false);

            Show(_overlay, screen != AppScreen.Playing);
            SetText(_headlineLabel, Headline(screen, game));
            SetText(_messageLabel, Message(screen, game));
            SetText(_submitButton, screen == AppScreen.Title ? "はじめる  /  Enter" : screen == AppScreen.Paused ? "再開  /  Enter" : "次の配札  /  Enter");
            Show(_restartButton, screen == AppScreen.Paused);
            Show(_resignButton, screen == AppScreen.Paused);
            Show(_titleButton, screen == AppScreen.Paused || screen == AppScreen.Result);
            Show(_quitButton, screen == AppScreen.Title);
        }

        /// <summary>プレイ中の案内。直前の操作の結果を説明する。</summary>
        public string Status() => Flow.Notice switch
        {
            Notice.Rejected => RejectMessage(Flow.LastError),
            Notice.Undone => "1 手戻しました。",
            Notice.NothingToUndo => "戻せる手がありません。",
            _ => Flow.Input.Selected != null ? "置きたい場所をクリックしてください。" : "札をドラッグして重ねます。\nダブルクリックで組札へ送ります。",
        };

        public static string RejectMessage(MoveError error) => error switch
        {
            MoveError.SameColor => "同じ色の上には置けません。\n色違いの札に重ねます。",
            MoveError.RankMismatch => "1 つ大きい数の札の上にだけ置けます。",
            MoveError.EmptyColumnNeedsKing => "空いた列には K から始まる札だけ置けます。",
            MoveError.FoundationNeedsAce => "空いた組札には A から置きます。",
            MoveError.FoundationSuitMismatch => "組札には同じ印の札を重ねます。",
            MoveError.FoundationRankMismatch => "組札には 1 つ大きい数の札を置きます。",
            MoveError.NotSingleCard => "組札へは 1 枚ずつ置きます。",
            MoveError.FaceDown => "伏せた札は動かせません。",
            MoveError.BrokenRun => "その並びはまとめて動かせません。",
            MoveError.StockEmpty or MoveError.WasteEmpty => "山札も捨て札も空です。",
            _ => "そこへは置けません。",
        };

        public static string DrawModeName(int drawCount) => drawCount == 1 ? "1 枚めくり" : $"{drawCount} 枚めくり";

        private static string Headline(AppScreen screen, SolitaireGame game) => screen switch
        {
            AppScreen.Title => "SEVEN LANTERNS",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => game.Phase switch
            {
                GamePhase.Won => "完成！",
                GamePhase.Stuck => "手詰まり",
                _ => "おしまい",
            },
            _ => string.Empty,
        };

        private string Message(AppScreen screen, SolitaireGame game)
        {
            switch (screen)
            {
                case AppScreen.Title:
                    return "4 つの印（ハート・ダイヤ・クラブ・スペード）を、それぞれ A から K まで組札に積み上げてください。\n" +
                        "場札は色違いで 1 つ小さい数を重ねられます。空いた列には K だけ置けます。\n\n" + Controls;
                case AppScreen.Paused:
                    return "Enter：再開\nR：配り直す\nA：あきらめる\nT：タイトルへ";
                case AppScreen.Result:
                    var reason = game.Phase switch
                    {
                        GamePhase.Won => "52 枚すべてを組札に積みました。",
                        GamePhase.Stuck => "どう動かしても、組札を増やすことも\n伏せ札を開くこともできなくなりました。",
                        _ => "この配札はここまでにしました。",
                    };
                    return $"{reason}\n\n組札  {game.Table.FoundationTotal} / {Card.DeckSize}\n手数  {game.Moves}\n時間  {Clock(Flow.ElapsedTicks)}\n\n次の配札は別の並びになります。";
                default:
                    return string.Empty;
            }
        }

        /// <summary>Tick 数を「分:秒」（1 時間以上は「時:分:秒」）にする。</summary>
        public static string Clock(int ticks)
        {
            var seconds = ticks / SolitaireFlow.TickRate;
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
            public Intent(IntentKind kind, Vector2 point = default, double time = 0)
            {
                Kind = kind;
                Point = point;
                Time = time;
            }

            public IntentKind Kind { get; }
            public Vector2 Point { get; }
            public double Time { get; }
        }
    }
}
