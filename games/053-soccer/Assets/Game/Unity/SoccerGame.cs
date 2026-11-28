#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// シーンの進行役。設定から <see cref="SoccerFlow"/> を作り、物理キーごとの押下・解放とマウスの押下を <see cref="Intent"/> にして渡す。
    /// Stage・HUD・効果音はその Tick の Flow の状態を写すだけ（補間しない）。
    /// </summary>
    public sealed class SoccerGame : MonoBehaviour
    {
        private static int? _seedOverride;
        private static SoccerSettings? _settingsOverride;

        [SerializeField] private SoccerSettings? _settings;
        [SerializeField] private PanelRenderer? _hudRenderer;
        [SerializeField] private Camera? _camera;
        [SerializeField] private SoccerStage? _stage;

        private readonly List<InputAction> _actions = new();
        private SoccerAudio? _audio;
        private SoccerHud? _hud;
        private Font? _font;
        private int _hudVersion = -1;
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private bool _quitCalled;

        /// <summary>終了の処理（PlayMode テストでは差し替えてエディタを閉じない）。</summary>
        public static Action QuitHandler { get; set; } = Application.Quit;

        public SoccerFlow Flow { get; private set; } = null!;

        public SoccerSettings Settings => _settingsOverride ?? _settings!;

        public SoccerHud? Hud => _hud;

        public SoccerStage Stage => _stage!;

        public Camera ViewCamera => _camera!;

        /// <summary>PlayMode テスト用：シーンを読み直しても同じ最初のシードと設定を使う。</summary>
        public static void SetOverridesForTests(int? firstSeed, SoccerSettings? settings)
        {
            _seedOverride = firstSeed;
            _settingsOverride = settings;
        }

        /// <summary>最初のシードを差し替えて Flow を作り直す（撮影・テスト）。</summary>
        public void Reinitialize(int firstSeed)
        {
            Flow = new SoccerFlow(Settings.ToConfig(), firstSeed, Settings.ToFlowSettings());
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
            if (_settings == null || _hudRenderer == null || _camera == null || _stage == null)
            {
                throw new InvalidOperationException("SoccerGame の SerializeField が未設定です。");
            }

            Settings.Validate();
            var stageError = _stage.Mismatch();
            if (stageError != null) { throw new InvalidOperationException("Stage が合いません：" + stageError); }

            // 撮影中は固定のシード。ふだんは起動時の値から試合ごとに進める（010）。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : _seedOverride ?? Environment.TickCount;
            Flow = new SoccerFlow(Settings.ToConfig(), seed, Settings.ToFlowSettings());

            var source = GetComponent<AudioSource>();
            if (source == null) { source = gameObject.AddComponent<AudioSource>(); }
            _audio = new SoccerAudio(source);
            _audio.SyncBaseline(Flow.Cues);

            Bind(InputButton.Up, 0, "<Keyboard>/w");
            Bind(InputButton.Up, 1, "<Keyboard>/upArrow");
            Bind(InputButton.Down, 0, "<Keyboard>/s");
            Bind(InputButton.Down, 1, "<Keyboard>/downArrow");
            Bind(InputButton.Left, 0, "<Keyboard>/a");
            Bind(InputButton.Left, 1, "<Keyboard>/leftArrow");
            Bind(InputButton.Right, 0, "<Keyboard>/d");
            Bind(InputButton.Right, 1, "<Keyboard>/rightArrow");
            Bind(InputButton.Pass, 0, "<Keyboard>/j");
            Bind(InputButton.Shoot, 0, "<Keyboard>/k");
            Bind(InputButton.Lob, 0, "<Keyboard>/l");
            Bind(InputButton.Switch, 0, "<Keyboard>/space");
            Bind(InputButton.Confirm, 0, "<Keyboard>/enter");
            Bind(InputButton.Confirm, 1, "<Keyboard>/numpadEnter");
            Bind(InputButton.Cancel, 0, "<Keyboard>/escape");
            var roles = new InputAction("Roles", InputActionType.Button, "<Keyboard>/f1");
            roles.performed += _ =>
            {
                if (!EvidenceCapture.IsRunning) { _stage.ShowRoles = !_stage.ShowRoles; }
            };
            _actions.Add(roles);
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

            // コート（32 × 20 m）と余白・上の帯が横長の画面で切れないように収める。
            var aspect = _camera.aspect > 0.1f ? _camera.aspect : 16f / 9f;
            _camera.orthographicSize = Mathf.Max(12.6f, 18.6f / aspect);
            _hud?.Render(Flow, _camera, _stage.ShowRoles);
            _audio?.PlayNew(Flow.Cues, Settings.Volume);
        }

        /// <summary>左ボタンの押下を、押したときの画面の版を付けたクリックにする（033）。ウィンドウを選び直したクリックは捨てる（010）。</summary>
        private void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null || !_hasFocus || !mouse.leftButton.wasPressedThisFrame) { return; }
            if (Time.frameCount <= _focusRestoredFrame + 1) { return; }
            if (Flow.Screen == FlowScreen.Playing) { return; }
            var p = PanelPoint(mouse.position.ReadValue());
            if (p == null || _hud == null) { return; }
            Flow.Enqueue(Intent.Click(_hud.MenuIndexAt(Flow, p.Value), Flow.Version));
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_hud != null && _hudVersion == version) { return; }
            _hudVersion = version;
            _hud = new SoccerHud(root, _font);

            // 作った直後に一度描き、UXML の初期状態を見せない。
            _hud.Render(Flow, _camera!, _stage!.ShowRoles);
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
