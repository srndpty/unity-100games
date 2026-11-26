#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// シーンの進行役。物理キーごとの入力を <see cref="Intent"/> にして <see cref="KeeperFlow"/> に渡し、
    /// 地図の Stage・戦闘の Stage・HUD・効果音をその Tick の状態に合わせる。
    /// </summary>
    public sealed class KeeperGame : MonoBehaviour
    {
        public const string SaveFileName = "thornwild_save.txt";
        public const string RecordFileName = "thornwild_records.txt";

        private static int _bootSeed;
        private static bool _bootSeedReady;
        private static (ISaveStore Save, ISaveStore Records)? _storeOverride;

        [SerializeField] private KeeperSettings? _settings;
        [SerializeField] private TextAsset? _types;
        [SerializeField] private TextAsset? _statuses;
        [SerializeField] private TextAsset? _moves;
        [SerializeField] private TextAsset? _species;
        [SerializeField] private TextAsset? _world;
        [SerializeField] private PanelRenderer? _hudRenderer;
        [SerializeField] private Camera? _camera;
        [SerializeField] private KeeperMapStage[] _mapStages = Array.Empty<KeeperMapStage>();
        [SerializeField] private KeeperBattleStage? _battleStage;

        private readonly List<InputAction> _actions = new();
        private KeeperAudio? _audio;
        private KeeperHud? _hud;
        private Font? _font;
        private int _hudVersion = -1;
        private Autopilot? _autoplay;
        private double _autoplayTicks;
        private InputAction? _autoplayToggle;

        /// <summary>1 フレームに進める自動プレイの Tick の上限（処理落ちで一度に進めすぎない）。</summary>
        public const int MaxAutoplayTicksPerFrame = 40;

        public KeeperFlow Flow { get; private set; } = null!;

        public MonsterData Data { get; private set; } = null!;

        public KeeperSettings Settings => _settings!;

        public KeeperHud? Hud => _hud;

        public Camera ViewCamera => _camera!;

        public IReadOnlyList<KeeperMapStage> MapStages => _mapStages;

        public KeeperBattleStage BattleStage => _battleStage!;

        public ISaveStore SaveStore { get; private set; } = null!;

        public ISaveStore RecordStore { get; private set; } = null!;

        /// <summary>眺める自動プレイの最中（操縦役が人の間合いで操作し、<see cref="KeeperSettings.AutoplaySpeed"/> 倍で進む）。</summary>
        public bool IsAutoplaying => _autoplay != null;

        /// <summary>眺める自動プレイを始める・止める（F9）。止めたらキー操作に戻る。</summary>
        public void SetAutoplay(bool on)
        {
            if (on == IsAutoplaying) { return; }
            _autoplay = on ? new Autopilot { HumanPace = true, SaveAtCenter = false } : null;
            _autoplayTicks = 0;

            // フォーカスを失って止まっていたら、自動プレイを始めた時点で再開する。
            if (on) { Flow.Resume(); }
            Render();
        }

        /// <summary>PlayMode テスト用：シーンを読み直しても同じ保存領域を使う。</summary>
        public static void SetStoresForTests(ISaveStore? save, ISaveStore? records) =>
            _storeOverride = save == null || records == null ? null : (save, records);

        /// <summary>保存領域とシードを差し替えて Flow を作り直す（撮影・テスト）。</summary>
        public void Reinitialize(ISaveStore save, ISaveStore records, int seed)
        {
            SaveStore = save ?? throw new ArgumentNullException(nameof(save));
            RecordStore = records ?? throw new ArgumentNullException(nameof(records));
            Flow = new KeeperFlow(Data, SaveStore, RecordStore, seed, _settings!.ToFlowSettings());
            _audio?.SyncBaseline(Flow.Cues);
            Render();
        }

        private void Awake()
        {
            if (_settings == null || _types == null || _statuses == null || _moves == null || _species == null || _world == null ||
                _hudRenderer == null || _camera == null || _battleStage == null)
            {
                throw new InvalidOperationException("KeeperGame の SerializeField が未設定です。");
            }

            _settings.Validate();
            Data = MonsterData.Load(_types.text, _statuses.text, _moves.text, _species.text, _world.text);
            if (!_bootSeedReady)
            {
                _bootSeed = (Environment.TickCount & 0x7FFFFFFF) | 1;
                _bootSeedReady = true;
            }

            // 再戦のたびに時刻を読まず、起動時の値から進める（010）。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : _bootSeed;
            _bootSeed = unchecked(_bootSeed * 1664525 + 1013904223) & 0x7FFFFFFF;
            SaveStore = _storeOverride?.Save ?? FileSaveStore.CreateDefault(SaveFileName);
            RecordStore = _storeOverride?.Records ?? FileSaveStore.CreateDefault(RecordFileName);
            Flow = new KeeperFlow(Data, SaveStore, RecordStore, seed, _settings.ToFlowSettings());

            var source = GetComponent<AudioSource>();
            if (source == null) { source = gameObject.AddComponent<AudioSource>(); }
            _audio = new KeeperAudio(source);
            _audio.SyncBaseline(Flow.Cues);

            Bind(InputButton.Up, 0, "<Keyboard>/upArrow");
            Bind(InputButton.Up, 1, "<Keyboard>/w");
            Bind(InputButton.Down, 0, "<Keyboard>/downArrow");
            Bind(InputButton.Down, 1, "<Keyboard>/s");
            Bind(InputButton.Left, 0, "<Keyboard>/leftArrow");
            Bind(InputButton.Left, 1, "<Keyboard>/a");
            Bind(InputButton.Right, 0, "<Keyboard>/rightArrow");
            Bind(InputButton.Right, 1, "<Keyboard>/d");
            Bind(InputButton.Confirm, 0, "<Keyboard>/z");
            Bind(InputButton.Confirm, 1, "<Keyboard>/enter");
            Bind(InputButton.Confirm, 2, "<Keyboard>/space");
            Bind(InputButton.Confirm, 3, "<Keyboard>/numpadEnter");
            Bind(InputButton.Cancel, 0, "<Keyboard>/x");
            Bind(InputButton.Cancel, 1, "<Keyboard>/escape");
            Bind(InputButton.Cancel, 2, "<Keyboard>/backspace");
            Bind(InputButton.Pause, 0, "<Keyboard>/p");
            foreach (var a in _actions) { a.Enable(); }
            _autoplayToggle = new InputAction("Autoplay", InputActionType.Button, "<Keyboard>/f9");
            _autoplayToggle.performed += _ =>
            {
                if (!EvidenceCapture.IsRunning) { SetAutoplay(!IsAutoplaying); }
            };
            _autoplayToggle.Enable();
            if (!EvidenceCapture.IsRunning && Array.IndexOf(Environment.GetCommandLineArgs(), "-autoplay") >= 0) { SetAutoplay(true); }

            _font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Arial" }, 28);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private void OnDestroy()
        {
            foreach (var a in _actions) { a.Dispose(); }
            _actions.Clear();
            _autoplayToggle?.Dispose();
            _audio?.Dispose();
            if (_font != null)
            {
                Destroy(_font);
                _font = null;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // 撮影中と眺める自動プレイの間は、フォーカスで止めない（038。自動プレイは人のキーで再開できないので止めない）。
            if (Flow == null || EvidenceCapture.IsRunning || IsAutoplaying) { return; }
            Flow.OnFocusChanged(hasFocus);
        }

        private void Update()
        {
            if (_autoplay != null && !EvidenceCapture.IsRunning)
            {
                StepAutoplay(Time.timeScale > 0f ? Time.unscaledDeltaTime : 0);
            }
            else if (!EvidenceCapture.IsRunning)
            {
                // 遊んだ時間の計測は maximumDeltaTime で切られない実時間で（017）。
                Flow.UpdateFrame(Time.timeScale > 0f ? Time.unscaledDeltaTime : 0);
            }

            Render();
        }

        /// <summary>
        /// 自動プレイを実時間 × 速さぶん進める。1 Tick ごとに操縦役が 1 回まで操作する。キー操作は捨てる（F9 だけ効く）。
        /// </summary>
        public void StepAutoplay(double deltaSeconds)
        {
            if (_autoplay == null) { return; }

            // 人の P で一時停止・P か決定で再開できる。止めている間は自動でも進めない。
            Flow.ApplyPauseInputOnly();
            if (Flow.IsPaused)
            {
                _autoplayTicks = 0;
                return;
            }

            _autoplayTicks += Math.Max(0, deltaSeconds) * 60.0 * _settings!.AutoplaySpeed;
            var ticks = Math.Min(MaxAutoplayTicksPerFrame, (int)Math.Floor(_autoplayTicks));
            _autoplayTicks = Math.Min(_autoplayTicks - ticks, 1.0);
            for (int i = 0; i < ticks; i++)
            {
                _autoplay.Step(Flow);
                Flow.StepTicks(1);
            }
        }

        /// <summary>今の Flow の状態を Stage・HUD・音へ反映する。</summary>
        public void Render()
        {
            if (Flow == null || _camera == null || _battleStage == null) { return; }
            var screen = Flow.Screen;
            var world = Flow.World;
            var inBattle = screen == FlowScreen.Battle && Flow.Presenter != null;
            _battleStage.gameObject.SetActive(inBattle);
            if (inBattle)
            {
                foreach (var s in _mapStages) { s.gameObject.SetActive(false); }
                _battleStage.Show(Flow.Presenter!);
                _camera.orthographicSize = OrthoSize(5.2f, 9.4f);
                _camera.transform.position = new Vector3(0f, 0f, -10f);
            }
            else
            {
                var playing = world != null && screen != FlowScreen.Title && screen != FlowScreen.HowTo && screen != FlowScreen.Result && screen != FlowScreen.StarterPick;
                var mapId = playing ? world!.Explorer.MapId : Data.Rules.StartMapId;
                KeeperMapStage? active = null;
                foreach (var s in _mapStages)
                {
                    var match = string.Equals(s.MapId, mapId, StringComparison.Ordinal);
                    s.gameObject.SetActive(match);
                    if (match) { active = s; }

                    // タイトルでは見えていない Stage も初期の姿に戻す（前の試合の倒したトレーナーを残さない、021）。
                    if (!playing) { s.ShowPreview(Data); }
                }

                if (active != null)
                {
                    var map = Data.Map(mapId);
                    Vector3 focus;
                    if (playing)
                    {
                        active.Show(world!, out focus);
                    }
                    else
                    {
                        // タイトルでは初期の姿に戻す（021）。
                        active.ShowPreview(Data);
                        focus = active.transform.TransformPoint(new Vector3(map.Width * 0.5f, map.Height * 0.5f, 0f));
                    }

                    var half = OrthoSize(5.6f, 9.6f);
                    _camera.orthographicSize = half;
                    var halfW = half * _camera.aspect;
                    _camera.transform.position = new Vector3(Clamp(focus.x, map.Width, halfW), Clamp(focus.y, map.Height, half), -10f);
                }
            }

            _hud?.Render(Flow, Data, IsAutoplaying ? _settings!.AutoplaySpeed : 0f);
            _audio?.PlayNew(Flow.Cues, _settings!.Volume);
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_hud != null && _hudVersion == version) { return; }
            _hudVersion = version;
            _hud = new KeeperHud(root, _font);

            // 作った直後に一度描き、UXML の初期状態を見せない。
            _hud.Render(Flow, Data, IsAutoplaying ? _settings!.AutoplaySpeed : 0f);
        }

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

        private float OrthoSize(float minHalfHeight, float minHalfWidth)
        {
            var aspect = _camera != null && _camera.aspect > 0.1f ? _camera.aspect : 16f / 9f;
            return Mathf.Max(minHalfHeight, minHalfWidth / aspect);
        }

        private static float Clamp(float target, float span, float half) =>
            span <= half * 2f ? span * 0.5f : Mathf.Clamp(target, half, span - half);
    }
}
