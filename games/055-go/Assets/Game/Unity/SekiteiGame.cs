#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Game.App;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// シーンの進行役。設定から <see cref="GoFlow"/> を作り、キーの押下とマウスの押下を <see cref="Intent"/> にして渡す。
    /// 探索（CPU・死石の推定）は 1 フレームの予算（入力と Tick を含めて <see cref="SekiteiSettings.ThinkMillisecondsPerFrame"/>）の残りで回す（<see cref="GoFlow.Think"/>）。
    /// Tick では回さないので、処理落ちで Tick がたまっても探索の量は増えない。撮影中だけは Tick の分だけで回す（<see cref="GoFlow.SearchInTicks"/>）。
    /// Stage・HUD・効果音はその Tick の状態を写すだけ。
    /// </summary>
    public sealed class SekiteiGame : MonoBehaviour
    {
        /// <summary>フレームの予算の残りで 1 回に回すプレイアウトの数（時間を見る間隔）。</summary>
        public const int ThinkChunk = 8;

        private static int? _seedOverride;
        private static SekiteiSettings? _settingsOverride;

        [SerializeField] private SekiteiSettings? _settings;
        [SerializeField] private PanelRenderer? _hudRenderer;
        [SerializeField] private Camera? _camera;
        [SerializeField] private SekiteiStage? _stage;

        private readonly List<InputAction> _actions = new();
        private readonly Stopwatch _thinkWatch = new();
        private SekiteiAudio? _audio;
        private SekiteiHud? _hud;
        private Font? _font;
        private int _hudVersion = -1;
        private bool _hasFocus = true;
        private int _focusRestoredFrame = -10;
        private bool _quitCalled;
        private int _hover = -1;

        /// <summary>終了の処理（PlayMode テストでは差し替えてエディタを閉じない）。</summary>
        public static Action QuitHandler { get; set; } = Application.Quit;

        public GoFlow Flow { get; private set; } = null!;

        public SekiteiSettings Settings => _settingsOverride ?? _settings!;

        public SekiteiHud? Hud => _hud;

        public SekiteiStage Stage => _stage!;

        public Camera ViewCamera => _camera!;

        /// <summary>PlayMode テスト用：シーンを読み直しても同じ最初のシードと設定を使う。</summary>
        public static void SetOverridesForTests(int? firstSeed, SekiteiSettings? settings)
        {
            _seedOverride = firstSeed;
            _settingsOverride = settings;
        }

        /// <summary>最初のシードを差し替えて Flow を作り直す（撮影・テスト）。</summary>
        public void Reinitialize(int firstSeed)
        {
            Flow = new GoFlow(Settings.ToConfig(), Settings.ToMcts(), firstSeed, Settings.ToFlowSettings()) { SearchInTicks = EvidenceCapture.IsRunning };
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

        /// <summary>画面の座標の下の交点（なければ -1）。</summary>
        public int BoardPointAt(Vector2 screen)
        {
            var world = _camera!.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -_camera.transform.position.z));
            return BoardProjection.PointAt(world);
        }

        /// <summary>交点の画面の座標（左下が原点）。PlayMode テストのクリックに使う。</summary>
        public Vector2 ScreenPointOf(int point) => _camera!.WorldToScreenPoint(BoardProjection.World(point));

        private void Awake()
        {
            if (_settings == null || _hudRenderer == null || _camera == null || _stage == null)
            {
                throw new InvalidOperationException("SekiteiGame の SerializeField が未設定です。");
            }

            Settings.Validate();
            var stageError = _stage.Mismatch();
            if (stageError != null) { throw new InvalidOperationException("Stage が合いません：" + stageError); }

            // 撮影中は固定のシード。ふだんは起動時の値から試合ごとに進める（010）。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : _seedOverride ?? Environment.TickCount;
            Flow = new GoFlow(Settings.ToConfig(), Settings.ToMcts(), seed, Settings.ToFlowSettings()) { SearchInTicks = EvidenceCapture.IsRunning };

            var source = GetComponent<AudioSource>();
            if (source == null) { source = gameObject.AddComponent<AudioSource>(); }
            _audio = new SekiteiAudio(source);
            _audio.SyncBaseline(Flow.Cues);

            Bind(InputButton.Up, "<Keyboard>/w", "<Keyboard>/upArrow");
            Bind(InputButton.Down, "<Keyboard>/s", "<Keyboard>/downArrow");
            Bind(InputButton.Left, "<Keyboard>/a", "<Keyboard>/leftArrow");
            Bind(InputButton.Right, "<Keyboard>/d", "<Keyboard>/rightArrow");
            Bind(InputButton.Confirm, "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space");
            Bind(InputButton.Cancel, "<Keyboard>/escape");
            Bind(InputButton.Pass, "<Keyboard>/p");
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
                _thinkWatch.Restart();
                ReadMouse();
                Flow.UpdateFrame(Time.timeScale > 0f ? Time.unscaledDeltaTime : 0);
                Think();
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
            _camera.orthographicSize = BoardProjection.CameraSizeFor(aspect);
            _camera.transform.position = BoardProjection.CameraPosition;
            _stage.Show(Flow, EvidenceCapture.IsRunning ? -1 : _hover);
            _hud?.Render(Flow, _camera);
            _audio?.PlayNew(Flow.Cues, Settings.Volume);
        }

        /// <summary>
        /// フレームの予算の残りで探索を進める（入力と Tick にかかった時間も予算に含める）。手と推定は総数だけで決まるので、時間で切っても結果は変わらない。
        /// 進み続けるよう、1 フレームに少なくとも 1 回（<see cref="ThinkChunk"/>）は回す。
        /// </summary>
        private void Think()
        {
            var budget = Settings.ThinkMillisecondsPerFrame;
            do
            {
                if (Flow.Think(ThinkChunk) == 0) { break; }
            }
            while (_thinkWatch.Elapsed.TotalMilliseconds < budget);
        }

        /// <summary>
        /// 左ボタンの押下を、押したときの画面の版を付けたクリックにする（044）。ウィンドウを選び直したクリックは捨てる（010）。
        /// HUD の項目・ボタンを先に見て、なければ盤の交点。マウスの下の交点は置く前の影に使う。
        /// </summary>
        private void ReadMouse()
        {
            var mouse = Mouse.current;
            _hover = -1;
            if (mouse == null || !_hasFocus) { return; }
            var screen = mouse.position.ReadValue();
            var panel = PanelPoint(screen);
            var overPanel = panel != null && _hud != null && _hud.IsOverPanel(panel.Value);
            if (!overPanel) { _hover = BoardPointAt(screen); }
            if (!mouse.leftButton.wasPressedThisFrame || Time.frameCount <= _focusRestoredFrame + 1) { return; }
            if (panel == null || _hud == null) { return; }
            var index = _hud.ClickIndexAt(Flow, panel.Value);
            if (index < 0 && !overPanel && Flow.Screen is FlowScreen.Playing or FlowScreen.Scoring) { index = _hover; }
            if (index >= 0) { Flow.Enqueue(Intent.Click(index, Flow.Version)); }
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_hud != null && _hudVersion == version) { return; }
            _hudVersion = version;
            _hud = new SekiteiHud(root, _font);

            // 作った直後に一度描き、UXML の初期状態を見せない。
            _hud.Render(Flow, _camera!);
        }

        /// <summary>キーごとのアクション（035・037・042）。押下だけを送る。</summary>
        private void Bind(InputButton button, params string[] paths)
        {
            foreach (var path in paths)
            {
                var action = new InputAction($"{button}_{path}", InputActionType.Button, path);
                action.performed += _ =>
                {
                    if (Flow == null || EvidenceCapture.IsRunning) { return; }
                    Flow.Press(button);
                };
                _actions.Add(action);
            }
        }
    }
}
