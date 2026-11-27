#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core.Data;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// シーンの進行役。台本をコンパイルして <see cref="SummerFlow"/> を作り、物理キーごとの押下とマウスの押下を
    /// <see cref="Intent"/> にして渡す。Stage・HUD・効果音はその Tick の Flow の状態を写すだけ。
    /// </summary>
    public sealed class SummerGame : MonoBehaviour
    {
        public const string SaveFileName = "meteor_summer_save.txt";

        private static ISaveStore? _storeOverride;
        private static int? _seedOverride;

        [SerializeField] private SummerSettings? _settings;
        [SerializeField] private TextAsset? _book;
        [SerializeField] private PanelRenderer? _hudRenderer;
        [SerializeField] private Camera? _camera;
        [SerializeField] private SummerStage? _stage;

        private readonly List<InputAction> _actions = new();
        private SummerAudio? _audio;
        private SummerHud? _hud;
        private Font? _font;
        private int _hudVersion = -1;
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private bool _quitCalled;

        /// <summary>終了の処理（PlayMode テストでは差し替えてエディタを閉じない）。</summary>
        public static Action QuitHandler { get; set; } = Application.Quit;

        public SummerFlow Flow { get; private set; } = null!;

        public SummerBook Book { get; private set; } = null!;

        public SummerSettings Settings => _settings!;

        public SummerHud? Hud => _hud;

        public SummerStage Stage => _stage!;

        public Camera ViewCamera => _camera!;

        public ISaveStore Store { get; private set; } = null!;

        /// <summary>PlayMode テスト用：シーンを読み直しても同じ保存領域と最初のシードを使う。</summary>
        public static void SetOverridesForTests(ISaveStore? store, int? firstSeed)
        {
            _storeOverride = store;
            _seedOverride = firstSeed;
        }

        /// <summary>保存領域と最初のシードを差し替えて Flow を作り直す（撮影・テスト）。</summary>
        public void Reinitialize(ISaveStore store, int firstSeed)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Flow = new SummerFlow(Book, Store, firstSeed, _settings!.ToFlowSettings());
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
            if (_settings == null || _book == null || _hudRenderer == null || _camera == null || _stage == null)
            {
                throw new InvalidOperationException("SummerGame の SerializeField が未設定です。");
            }

            _settings.Validate();
            var compiled = SummerCompiler.Compile(_book.text);
            if (!compiled.Ok) { throw new InvalidOperationException("台本の検証に失敗しました\n" + string.Join("\n", compiled.Errors)); }
            Book = compiled.Book!;
            var stageError = _stage.Mismatch(Book);
            if (stageError != null) { throw new InvalidOperationException("Stage が台本の素材と合いません：" + stageError); }

            Store = _storeOverride ?? FileSaveStore.CreateDefault(SaveFileName);

            // 撮影中は固定のシード。ふだんは起動時の値から試合ごとに進める（010）。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : _seedOverride ?? Environment.TickCount;
            Flow = new SummerFlow(Book, Store, seed, _settings.ToFlowSettings());

            var source = GetComponent<AudioSource>();
            if (source == null) { source = gameObject.AddComponent<AudioSource>(); }
            _audio = new SummerAudio(source);
            _audio.SyncBaseline(Flow.Cues);

            Bind(InputButton.Up, "<Keyboard>/upArrow");
            Bind(InputButton.Up, "<Keyboard>/w");
            Bind(InputButton.Down, "<Keyboard>/downArrow");
            Bind(InputButton.Down, "<Keyboard>/s");
            Bind(InputButton.Confirm, "<Keyboard>/enter");
            Bind(InputButton.Confirm, "<Keyboard>/space");
            Bind(InputButton.Confirm, "<Keyboard>/numpadEnter");
            Bind(InputButton.Confirm, "<Keyboard>/z");
            Bind(InputButton.Cancel, "<Keyboard>/escape");
            Bind(InputButton.Detail, "<Keyboard>/tab");
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
            Flow.Enqueue(Intent.Click(_hud.MenuIndexAt(Flow, p.Value), Flow.Version));
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_hud != null && _hudVersion == version) { return; }
            _hudVersion = version;
            _hud = new SummerHud(root, _font);

            // 作った直後に一度描き、UXML の初期状態を見せない。
            _hud.Render(Flow);
        }

        /// <summary>物理キーごとのアクション（035・037・042）。押下だけを拾う（押し続けて効く操作はない）。</summary>
        private void Bind(InputButton button, string path)
        {
            var action = new InputAction($"{button}_{_actions.Count}", InputActionType.PassThrough, path);
            action.performed += ctx =>
            {
                if (Flow == null || EvidenceCapture.IsRunning) { return; }
                if (ctx.control is KeyControl key && key.isPressed) { Flow.Press(button); }
            };
            _actions.Add(action);
        }
    }
}
