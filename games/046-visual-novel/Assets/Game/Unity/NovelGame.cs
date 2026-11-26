#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Core.Runtime;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// シーンの進行役。シナリオをコンパイルして <see cref="NovelFlow"/> を作り、物理キーごとの入力とマウスの押下を
    /// <see cref="Intent"/> にして渡す。Stage・HUD・効果音はその Tick の Flow の状態を写すだけ。
    /// </summary>
    public sealed class NovelGame : MonoBehaviour
    {
        public const string QuickFileName = "lantern_quick.txt";
        public const string ReadFileName = "lantern_read.txt";

        private static (ISaveStore Quick, ISaveStore Read)? _storeOverride;

        [SerializeField] private NovelSettings? _settings;
        [SerializeField] private TextAsset? _scenario;
        [SerializeField] private PanelRenderer? _hudRenderer;
        [SerializeField] private Camera? _camera;
        [SerializeField] private NovelStage? _stage;

        private readonly List<InputAction> _actions = new();
        private NovelAudio? _audio;
        private NovelHud? _hud;
        private Font? _font;
        private int _hudVersion = -1;
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private bool _quitCalled;

        /// <summary>終了の処理（PlayMode テストでは差し替えてエディタを閉じない）。</summary>
        public static Action QuitHandler { get; set; } = Application.Quit;

        public NovelFlow Flow { get; private set; } = null!;

        /// <summary>コンパイルの結果（全ての選択の列と結末を含む）。</summary>
        public Game.Core.Script.CompileResult Compiled { get; private set; } = null!;

        public CompiledScript Script => Compiled.Script!;

        public NovelSettings Settings => _settings!;

        public NovelHud? Hud => _hud;

        public NovelStage Stage => _stage!;

        public Camera ViewCamera => _camera!;

        public ISaveStore QuickStore { get; private set; } = null!;

        public ISaveStore ReadStore { get; private set; } = null!;

        /// <summary>PlayMode テスト用：シーンを読み直しても同じ保存領域を使う。</summary>
        public static void SetStoresForTests(ISaveStore? quick, ISaveStore? read) =>
            _storeOverride = quick == null || read == null ? null : (quick, read);

        /// <summary>保存領域を差し替えて Flow を作り直す（撮影・テスト）。</summary>
        public void Reinitialize(ISaveStore quick, ISaveStore read)
        {
            QuickStore = quick ?? throw new ArgumentNullException(nameof(quick));
            ReadStore = read ?? throw new ArgumentNullException(nameof(read));
            Flow = new NovelFlow(Script, QuickStore, ReadStore, _settings!.ToFlowSettings());
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

        private void Awake()
        {
            if (_settings == null || _scenario == null || _hudRenderer == null || _camera == null || _stage == null)
            {
                throw new InvalidOperationException("NovelGame の SerializeField が未設定です。");
            }

            _settings.Validate();
            Compiled = Scenario.Compile(_scenario.text, _settings.MaxTextLength);
            if (!Compiled.Ok) { throw new InvalidOperationException("シナリオの検証に失敗しました\n" + Compiled.Report()); }
            var stageError = _stage.Mismatch();
            if (stageError != null) { throw new InvalidOperationException("Stage がシナリオの素材と合いません：" + stageError); }

            QuickStore = _storeOverride?.Quick ?? FileSaveStore.CreateDefault(QuickFileName);
            ReadStore = _storeOverride?.Read ?? FileSaveStore.CreateDefault(ReadFileName);
            Flow = new NovelFlow(Script, QuickStore, ReadStore, _settings.ToFlowSettings());

            var source = GetComponent<AudioSource>();
            if (source == null) { source = gameObject.AddComponent<AudioSource>(); }
            _audio = new NovelAudio(source);
            _audio.SyncBaseline(Flow.Cues);

            Bind(InputButton.Up, 0, "<Keyboard>/upArrow");
            Bind(InputButton.Down, 0, "<Keyboard>/downArrow");
            Bind(InputButton.Confirm, 0, "<Keyboard>/enter");
            Bind(InputButton.Confirm, 1, "<Keyboard>/space");
            Bind(InputButton.Confirm, 2, "<Keyboard>/numpadEnter");
            Bind(InputButton.Confirm, 3, "<Keyboard>/z");
            Bind(InputButton.Cancel, 0, "<Keyboard>/escape");
            Bind(InputButton.SkipHold, 1, "<Keyboard>/leftCtrl");
            Bind(InputButton.SkipHold, 2, "<Keyboard>/rightCtrl");
            Bind(InputButton.SkipToggle, 0, "<Keyboard>/s");
            Bind(InputButton.QuickSave, 0, "<Keyboard>/f5");
            Bind(InputButton.QuickLoad, 0, "<Keyboard>/f9");
            Bind(InputButton.HideWindow, 0, "<Keyboard>/h");
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

        private void OnApplicationQuit()
        {
            Flow?.SaveReadLog();
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
            _stage.Show(Flow);
            var aspect = _camera.aspect > 0.1f ? _camera.aspect : 16f / 9f;

            // 背景（20 × 11.25）が横長の画面で切れないように収める。
            _camera.orthographicSize = Mathf.Max(5.625f, 10f / aspect);
            _hud?.Render(Flow);
            _audio?.PlayNew(Flow.Cues, _settings!.Volume);
        }

        /// <summary>
        /// 左ボタンの押下を、押したときの画面の版を付けたクリックにする（離したときではなく押したときに効かせる、033）。
        /// ウィンドウを選び直したクリックは捨てる（010）。
        /// </summary>
        private void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null || !_hasFocus || !mouse.leftButton.wasPressedThisFrame) { return; }
            if (Time.frameCount <= _focusRestoredFrame + 1) { return; }
            var p = PanelPoint(mouse.position.ReadValue());
            if (p == null || _hud == null) { return; }
            var screen = Flow.Screen;
            var index = screen switch
            {
                FlowScreen.Title or FlowScreen.Paused => _hud.MenuIndexAt(screen, p.Value),
                FlowScreen.Reading when Flow.IsChoice => _hud.OptionIndexAt(p.Value),
                _ => -1,
            };
            Flow.Enqueue(Intent.Click(index, Flow.Version));
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_hud != null && _hudVersion == version) { return; }
            _hudVersion = version;
            _hud = new NovelHud(root, _font);

            // 作った直後に一度描き、UXML の初期状態を見せない。
            _hud.Render(Flow);
        }

        /// <summary>物理キーごとのアクション（035・037・042）。押下と解放を別に送る。</summary>
        private void Bind(InputButton button, int slot, string path)
        {
            var action = new InputAction($"{button}_{slot}", InputActionType.PassThrough, path);
            action.performed += ctx =>
            {
                if (Flow == null || EvidenceCapture.IsRunning) { return; }
                if (ctx.control is KeyControl key) { Flow.Enqueue(new Intent(button, slot, key.isPressed)); }
            };
            _actions.Add(action);
        }
    }
}
