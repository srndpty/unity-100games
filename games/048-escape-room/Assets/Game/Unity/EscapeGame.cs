#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core.Rooms;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// シーンの進行役。本番の部屋（<see cref="StudyRoom"/>）で <see cref="EscapeFlow"/> を作り、キーとマウスの押下・ドラッグを
    /// <see cref="Intent"/> にして渡す。カメラ・Stage・HUD・効果音はその Tick の Flow の状態を写すだけ。
    /// </summary>
    public sealed class EscapeGame : MonoBehaviour
    {
        [SerializeField] private EscapeSettings? _settings;
        [SerializeField] private PanelRenderer? _hudRenderer;
        [SerializeField] private Camera? _camera;
        [SerializeField] private StudyStage? _stage;
        [SerializeField] private Transform? _heldBackdrop;

        /// <summary>手に取った物の後ろの暗幕の距離（m）。近景で一番近い家具より手前、物（0.55 m）より奥。</summary>
        public const float BackdropDistance = 0.7f;

        private readonly List<InputAction> _actions = new();
        private EscapeAudio? _audio;
        private EscapeHud? _hud;
        private Font? _font;
        private int _hudVersion = -1;
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private bool _dragging;
        private Vector2 _dragFrom;
        private int _dragVersion;
        private bool _quitCalled;

        /// <summary>終了の処理（PlayMode テストでは差し替えてエディタを閉じない）。</summary>
        public static Action QuitHandler { get; set; } = Application.Quit;

        public EscapeFlow Flow { get; private set; } = null!;

        public RoomDef Def { get; private set; } = null!;

        public EscapeSettings Settings => _settings!;

        public EscapeHud? Hud => _hud;

        public StudyStage Stage => _stage!;

        public Camera ViewCamera => _camera!;

        /// <summary>Flow を作り直す（撮影・テスト）。</summary>
        public void Reinitialize(int baseSeed)
        {
            Flow = new EscapeFlow(Def, baseSeed, _settings!.ToFlowSettings());
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

        /// <summary>画面のピクセル（左下が原点）を Core の画面の整数座標にする。</summary>
        public Vector2Int CorePoint(Vector2 pixel) => StageMath.ToCore(pixel, _camera!.pixelRect);

        /// <summary>Core の画面の整数座標をピクセル（左下が原点）にする。</summary>
        public Vector2 PixelPoint(Vector2Int core) => StageMath.ToPixel(core, _camera!.pixelRect);

        /// <summary>
        /// 画面の比率を対応する範囲（16:10〜21:9）に収め（外は黒い帯）、部屋のクリックが届く範囲を Flow に渡す。
        /// 対応する範囲の画面では、届く範囲が部屋の定義の検証で使う範囲（ScreenRegion.Guaranteed）を必ず含む。
        /// </summary>
        public void UpdateViewport()
        {
            if (_camera == null || Flow == null || Screen.height <= 0) { return; }
            var rect = StageMath.Viewport(Screen.width, Screen.height);
            if (_camera.rect != rect) { _camera.rect = rect; }
            Flow.SetRegion(StageMath.Region(_camera.pixelRect, Screen.height));
        }

        private void Awake()
        {
            if (_settings == null || _hudRenderer == null || _camera == null || _stage == null)
            {
                throw new InvalidOperationException("EscapeGame の SerializeField が未設定です。");
            }

            _settings.Validate();
            Def = StudyRoom.Create();
            var stageError = _stage.Mismatch(Def);
            if (stageError != null) { throw new InvalidOperationException("Stage が部屋の定義と合いません：" + stageError); }

            // 1 試合 = 1 シード。撮影中は決まったシード、ふだんは起動時の値から試合ごとに派生する（010）。
            Flow = new EscapeFlow(Def, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount, _settings.ToFlowSettings());

            var source = GetComponent<AudioSource>();
            if (source == null) { source = gameObject.AddComponent<AudioSource>(); }
            _audio = new EscapeAudio(source);
            _audio.SyncBaseline(Flow.Cues);

            Bind(IntentKind.Up, 0, "<Keyboard>/upArrow");
            Bind(IntentKind.Up, 0, "<Keyboard>/w");
            Bind(IntentKind.Down, 0, "<Keyboard>/downArrow");
            Bind(IntentKind.Down, 0, "<Keyboard>/s");
            Bind(IntentKind.Turn, -1, "<Keyboard>/leftArrow");
            Bind(IntentKind.Turn, -1, "<Keyboard>/a");
            Bind(IntentKind.Turn, 1, "<Keyboard>/rightArrow");
            Bind(IntentKind.Turn, 1, "<Keyboard>/d");
            Bind(IntentKind.Confirm, 0, "<Keyboard>/enter");
            Bind(IntentKind.Confirm, 0, "<Keyboard>/space");
            Bind(IntentKind.Confirm, 0, "<Keyboard>/numpadEnter");
            Bind(IntentKind.Pause, 0, "<Keyboard>/escape");
            Bind(IntentKind.Hint, 0, "<Keyboard>/h");
            Bind(IntentKind.Reset, 0, "<Keyboard>/r");
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
            _dragging = false;
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

        /// <summary>今の Flow の状態をカメラ・Stage・HUD・音へ反映する。</summary>
        public void Render()
        {
            if (Flow == null || _stage == null || _camera == null) { return; }
            UpdateViewport();
            var views = Def.Views;
            if (Flow.InTransition) { StageMath.Blend(_camera, views[Flow.PreviousView].Pose, views[Flow.View].Pose, (float)Flow.TransitionProgress); }
            else { StageMath.Apply(_camera, views[Flow.View].Pose); }

            _stage.Show(Flow, _camera);
            if (_heldBackdrop != null)
            {
                var held = Flow.InGame && Flow.Held >= 0;
                if (_heldBackdrop.gameObject.activeSelf != held) { _heldBackdrop.gameObject.SetActive(held); }
                var cam = _camera.transform;
                _heldBackdrop.SetPositionAndRotation(cam.position + (cam.forward * BackdropDistance), cam.rotation);
            }

            if (_hud != null)
            {
                var mouse = Mouse.current;
                _hud.HoverName = mouse != null && !EvidenceCapture.IsRunning ? HoverName(mouse.position.ReadValue()) : "";
                _hud.Render(Flow);
            }

            _audio?.PlayNew(Flow.Cues, _settings!.Volume);
        }

        private string HoverName(Vector2 screen)
        {
            if (Flow.Screen != FlowScreen.Playing) { return ""; }
            var panel = PanelPoint(screen);
            if (panel == null || _hud!.IsOverBar(panel.Value)) { return ""; }
            var p = CorePoint(screen);
            var part = Flow.PartAt(p.x, p.y);
            if (part < 0) { return ""; }
            var def = Def.Parts[part];
            return Def.Views[Flow.View].Kind == ViewKind.Wall ? Def.Views[def.View].Name : def.Name;
        }

        /// <summary>
        /// 左ボタンの押下を、押したときの画面の版を付けたクリックにする（押したときに効かせる、033）。押したまま動かすとドラッグ。
        /// 右ボタンは一段戻る。ウィンドウを選び直したクリックは捨てる（010）。
        /// </summary>
        private void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null || _hud == null || !_hasFocus) { return; }
            var screen = mouse.position.ReadValue();
            var justFocused = Time.frameCount <= _focusRestoredFrame + 1;

            // ドラッグは左ボタンを押した画面の版の間だけ続ける（物を置く・寄る・画面が変わると終わる）。
            if (_dragging && (!mouse.leftButton.isPressed || Flow.Version != _dragVersion)) { _dragging = false; }
            if (_dragging && screen != _dragFrom)
            {
                var a = CorePoint(_dragFrom);
                var b = CorePoint(screen);
                if (b != a) { Flow.Enqueue(Intent.Drag(b.x - a.x, b.y - a.y, _dragVersion)); }
                _dragFrom = screen;
            }

            if (mouse.rightButton.wasPressedThisFrame && !justFocused) { Flow.Press(IntentKind.Back); }
            if (!mouse.leftButton.wasPressedThisFrame || justFocused) { return; }
            var p = PanelPoint(screen);
            if (p == null) { return; }
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
                    if (_hud.IsOverBar(p.Value))
                    {
                        var slot = _hud.SlotIndexAt(p.Value);
                        if (slot >= 0) { Flow.Click(ClickTarget.Item, slot); }
                        return;
                    }

                    var core = CorePoint(screen);
                    _dragVersion = Flow.Version;
                    Flow.ClickWorld(core.x, core.y);
                    _dragging = true;
                    _dragFrom = screen;
                    break;
            }
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_hud != null && _hudVersion == version) { return; }
            _hudVersion = version;
            _hud = new EscapeHud(root, _font, item => _stage!.Icon(item));

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
