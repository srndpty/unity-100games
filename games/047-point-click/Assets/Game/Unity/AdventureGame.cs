#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Core.Rules;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// シーンの進行役。ルール表をコンパイルして <see cref="AdventureFlow"/> を作り、キーとマウスの押下を
    /// <see cref="Intent"/> にして渡す。Stage・HUD・効果音はその Tick の Flow の状態を写すだけ。
    /// </summary>
    public sealed class AdventureGame : MonoBehaviour
    {
        [SerializeField] private AdventureSettings? _settings;
        [SerializeField] private TextAsset? _rules;
        [SerializeField] private PanelRenderer? _hudRenderer;
        [SerializeField] private Camera? _camera;
        [SerializeField] private RoomStage? _stage;

        private readonly List<InputAction> _actions = new();
        private AdventureAudio? _audio;
        private AdventureHud? _hud;
        private Font? _font;
        private int _hudVersion = -1;
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private bool _quitCalled;

        /// <summary>終了の処理（PlayMode テストでは差し替えてエディタを閉じない）。</summary>
        public static Action QuitHandler { get; set; } = Application.Quit;

        public AdventureFlow Flow { get; private set; } = null!;

        public RuleBook Book { get; private set; } = null!;

        public AdventureSettings Settings => _settings!;

        public AdventureHud? Hud => _hud;

        public RoomStage Stage => _stage!;

        public Camera ViewCamera => _camera!;

        /// <summary>Flow を作り直す（撮影・テスト）。</summary>
        public void Reinitialize()
        {
            Flow = new AdventureFlow(Book, _settings!.ToWorldSettings(), _settings.ToFlowSettings());
            _audio?.SyncBaseline(Flow.Cues);
            Render();
        }

        /// <summary>画面の座標（左下が原点）を HUD のパネルの座標にする。HUD がまだなければ null。</summary>
        public Vector2? PanelPoint(Vector2 screen)
        {
            var panel = _hud?.Root.panel;
            if (panel == null) { return null; }
            return RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
        }

        /// <summary>画面の座標を部屋の座標にする。</summary>
        public Vector2Int RoomPoint(Vector2 screen) => StageLayout.RoomPoint(_camera!.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f)));

        /// <summary>部屋の座標を画面の座標（左下が原点）にする。</summary>
        public Vector2 ScreenPoint(Vector2Int room) =>
            _camera!.WorldToScreenPoint(new Vector3(room.x * StageLayout.UnitsPerRoomPixel, room.y * StageLayout.UnitsPerRoomPixel, 0f));

        private void Awake()
        {
            if (_settings == null || _rules == null || _hudRenderer == null || _camera == null || _stage == null)
            {
                throw new InvalidOperationException("AdventureGame の SerializeField が未設定です。");
            }

            _settings.Validate();
            var compiled = Adventure.Compile(_rules.text);
            Book = compiled.Book ?? throw new InvalidOperationException("ルール表の検証に失敗しました\n" + string.Join("\n", compiled.Errors));
            var stageError = _stage.Mismatch(Book);
            if (stageError != null) { throw new InvalidOperationException("Stage がルール表と合いません：" + stageError); }
            Flow = new AdventureFlow(Book, _settings.ToWorldSettings(), _settings.ToFlowSettings());

            var source = GetComponent<AudioSource>();
            if (source == null) { source = gameObject.AddComponent<AudioSource>(); }
            _audio = new AdventureAudio(source);
            _audio.SyncBaseline(Flow.Cues);

            Bind(IntentKind.Up, -1, "<Keyboard>/upArrow");
            Bind(IntentKind.Down, -1, "<Keyboard>/downArrow");
            Bind(IntentKind.Confirm, -1, "<Keyboard>/enter");
            Bind(IntentKind.Confirm, -1, "<Keyboard>/space");
            Bind(IntentKind.Confirm, -1, "<Keyboard>/numpadEnter");
            Bind(IntentKind.Pause, -1, "<Keyboard>/escape");
            for (int i = 0; i < AdventureFlow.Verbs.Count; i++)
            {
                Bind(IntentKind.VerbKey, i, $"<Keyboard>/{i + 1}");
                Bind(IntentKind.VerbKey, i, $"<Keyboard>/numpad{i + 1}");
            }

            foreach (var a in _actions) { a.Enable(); }

            _font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Arial" }, 32);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private void OnDestroy()
        {
            foreach (var a in _actions) { a.Dispose(); }
            _actions.Clear();
            _audio?.Dispose();
            if (_font != null)
            {
                Destroy(_font);
                _font = null;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 撮影中はフォーカスで止めない（038）。
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            _hasFocus = hasFocus;
            if (hasFocus) { _focusRestoredFrame = Time.frameCount; }
            Flow.OnFocusChanged(hasFocus);
        }

        private void Update()
        {
            if (!EvidenceCapture.IsRunning)
            {
                ReadMouse();
                Flow.UpdateFrame(Time.timeScale > 0f ? Time.unscaledDeltaTime : 0);
            }

            if (Flow.QuitRequested && !_quitCalled)
            {
                _quitCalled = true;
                QuitHandler();
            }

            Render();
        }

        /// <summary>今の Flow の状態を Stage・HUD・音・カメラへ反映する。</summary>
        public void Render()
        {
            if (Flow == null || _stage == null || _camera == null) { return; }
            var aspect = _camera.aspect > 0.1f ? _camera.aspect : 16f / 9f;

            // 部屋（19.2 × 10.8）が横長の画面で切れないように収める。
            _camera.orthographicSize = Mathf.Max(StageLayout.RoomHeight * 0.5f, StageLayout.RoomWidth * 0.5f / aspect);
            _camera.transform.position = new Vector3(StageLayout.RoomWidth * 0.5f, StageLayout.RoomHeight * 0.5f, -10f);
            _stage.Show(Flow);
            _hud?.Render(Flow);
            _audio?.PlayNew(Flow.Cues, _settings!.Volume);
        }

        /// <summary>
        /// 左ボタンの押下を、押したときの画面の版を付けたクリックにする（押したときに効かせる、033）。右ボタンは選択を外す。
        /// ウィンドウを選び直したクリックは捨てる（010）。マウスの下の物は命令文のために毎フレーム渡す。
        /// </summary>
        private void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null || _hud == null || !_hasFocus) { return; }
            var screen = mouse.position.ReadValue();
            var p = PanelPoint(screen);
            if (p == null) { return; }
            UpdateHover(screen, p.Value);
            var justFocused = Time.frameCount <= _focusRestoredFrame + 1;
            if (mouse.rightButton.wasPressedThisFrame && !justFocused) { Flow.Press(IntentKind.Deselect); }
            if (!mouse.leftButton.wasPressedThisFrame || justFocused) { return; }
            switch (Flow.Screen)
            {
                case FlowScreen.Title:
                case FlowScreen.Paused:
                    Flow.Click(ClickTarget.Menu, _hud.MenuIndexAt(Flow.Screen, p.Value));
                    break;
                case FlowScreen.HowTo:
                case FlowScreen.Result:
                    Flow.Click(ClickTarget.Background, -1);
                    break;
                default:
                    PlayingClick(screen, p.Value);
                    break;
            }
        }

        private void PlayingClick(Vector2 screen, Vector2 panel)
        {
            if (Flow.CurrentLine != null)
            {
                Flow.Click(ClickTarget.Background, -1);
                return;
            }

            if (Flow.ShowingTopics)
            {
                Flow.Click(ClickTarget.Topic, _hud!.TopicIndexAt(panel));
                return;
            }

            if (_hud!.IsOverBar(panel))
            {
                var verb = _hud.VerbIndexAt(panel);
                if (verb >= 0) { Flow.Click(ClickTarget.Verb, verb); }
                var slot = _hud.SlotIndexAt(panel);
                if (slot >= 0) { Flow.Click(ClickTarget.Item, slot); }
                return;
            }

            var room = RoomPoint(screen);
            Flow.ClickWorld(room.x, room.y);
        }

        private void UpdateHover(Vector2 screen, Vector2 panel)
        {
            if (Flow.Screen != FlowScreen.Playing)
            {
                Flow.SetHover(-1, -1);
                return;
            }

            if (_hud!.IsOverBar(panel))
            {
                var slot = _hud.SlotIndexAt(panel);
                var inventory = Flow.Inventory;
                Flow.SetHover(-1, slot >= 0 && slot < inventory.Count ? inventory[slot] : -1);
                return;
            }

            var room = RoomPoint(screen);
            Flow.SetHover(Flow.HotspotAt(room.x, room.y), -1);
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_hud != null && _hudVersion == version) { return; }
            _hudVersion = version;
            _hud = new AdventureHud(root, _font, id => _stage!.ItemIcon(id));

            // 作った直後に一度描き、UXML の初期状態を見せない。
            _hud.Render(Flow);
        }

        /// <summary>キーごとのアクション。押下だけを意図にする（PassThrough で前のキーを押したままでも次の押下が届く、032）。</summary>
        private void Bind(IntentKind kind, int index, string path)
        {
            var action = new InputAction($"{kind}_{index}_{path}", InputActionType.PassThrough, path);
            action.performed += ctx =>
            {
                if (Flow == null || EvidenceCapture.IsRunning) { return; }
                if (ctx.control is KeyControl key && key.isPressed) { Flow.Press(kind, index); }
            };
            _actions.Add(action);
        }
    }
}
