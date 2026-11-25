#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Data;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// シーンのルート進行役。物理キー入力をタイムスタンプ付き <see cref="Intent"/> に変換して <see cref="QuestFlow"/> に渡し、
    /// フィールド Stage・戦闘 Stage・UI Toolkit HUD・効果音を同期する。
    /// </summary>
    public sealed class EmberlockGame : MonoBehaviour
    {
        private static int _bootSeed;
        private static bool _bootSeedInitialized;
        private static ISaveStore? _saveStoreOverride;

        [SerializeField] private EmberlockSettings? _settings;
        [SerializeField] private GameDatabase? _database;
        [SerializeField] private PanelRenderer? _hudRenderer;
        [SerializeField] private Camera? _camera;
        [SerializeField] private EmberlockMapStage[] _mapStages = Array.Empty<EmberlockMapStage>();
        [SerializeField] private EmberlockBattleStage? _battleStage;

        private readonly List<InputAction> _actions = new();
        private EmberlockAudio? _audio;
        private EmberlockHudView? _hudView;
        private Font? _font;
        private VisualElement? _hudRoot;
        private int _hudVersion = -1;
        private bool _hasFocus = true;

        public QuestFlow Flow { get; private set; } = null!;
        public GameData Data { get; private set; } = null!;
        public ISaveStore SaveStore { get; private set; } = null!;
        public EmberlockSettings Settings => _settings!;
        public GameDatabase Database => _database!;
        public VisualElement? HudRoot => _hudRoot;
        public Camera ViewCamera => _camera!;
        public IReadOnlyList<EmberlockMapStage> MapStages => _mapStages;
        public EmberlockBattleStage BattleStage => _battleStage!;

        /// <summary>PlayMode テストでシーン再読込をまたいで隔離セーブ領域を注入するために使う。</summary>
        public static void SetSaveStoreOverrideForTests(ISaveStore? store)
        {
            _saveStoreOverride = store;
        }

        public void ReinitializeWithSaveStore(ISaveStore saveStore, int initialSeed)
        {
            SaveStore = saveStore ?? throw new ArgumentNullException(nameof(saveStore));
            Flow = new QuestFlow(Data, SaveStore, initialSeed, _settings!.EventDurationTicks);
            _audio?.SyncBaseline(Flow.AudioCues);
            Render();
        }

        private void Awake()
        {
            if (_settings == null || _database == null || _hudRenderer == null || _camera == null || _battleStage == null)
            {
                throw new InvalidOperationException("EmberlockGame の必須 SerializeField が未設定です。");
            }

            _settings.Validate();
            Data = _database.BuildValidated(_settings);

            if (!_bootSeedInitialized)
            {
                _bootSeed = Environment.TickCount & 0x7FFFFFFF;
                if (_bootSeed == 0)
                {
                    _bootSeed = 1;
                }

                _bootSeedInitialized = true;
            }

            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : _bootSeed;
            _bootSeed = unchecked((_bootSeed * 1664525) + 1013904223) & 0x7FFFFFFF;
            if (_bootSeed == 0)
            {
                _bootSeed = 1;
            }

            SaveStore = _saveStoreOverride ?? FileSaveStore.CreateDefault();
            Flow = new QuestFlow(Data, SaveStore, seed, _settings.EventDurationTicks);

            var audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audio = new EmberlockAudio(audioSource);
            _audio.SyncBaseline(Flow.AudioCues);

            BindPhysicalKey(InputButton.Up, 0, "<Keyboard>/w");
            BindPhysicalKey(InputButton.Up, 1, "<Keyboard>/upArrow");
            BindPhysicalKey(InputButton.Down, 0, "<Keyboard>/s");
            BindPhysicalKey(InputButton.Down, 1, "<Keyboard>/downArrow");
            BindPhysicalKey(InputButton.Left, 0, "<Keyboard>/a");
            BindPhysicalKey(InputButton.Left, 1, "<Keyboard>/leftArrow");
            BindPhysicalKey(InputButton.Right, 0, "<Keyboard>/d");
            BindPhysicalKey(InputButton.Right, 1, "<Keyboard>/rightArrow");

            BindPhysicalKey(InputButton.Confirm, 0, "<Keyboard>/enter");
            BindPhysicalKey(InputButton.Confirm, 1, "<Keyboard>/numpadEnter");
            BindPhysicalKey(InputButton.Confirm, 2, "<Keyboard>/space");
            BindPhysicalKey(InputButton.Confirm, 3, "<Keyboard>/z");

            BindPhysicalKey(InputButton.Cancel, 0, "<Keyboard>/escape");
            BindPhysicalKey(InputButton.Cancel, 1, "<Keyboard>/backspace");
            BindPhysicalKey(InputButton.Cancel, 2, "<Keyboard>/x");

            BindPhysicalKey(InputButton.Menu, 0, "<Keyboard>/tab");
            BindPhysicalKey(InputButton.Menu, 1, "<Keyboard>/c");
            BindPhysicalKey(InputButton.Menu, 2, "<Keyboard>/m");

            BindPhysicalKey(InputButton.Pause, 0, "<Keyboard>/p");
            BindPhysicalKey(InputButton.Pause, 1, "<Keyboard>/f1");

            for (var i = 0; i < _actions.Count; i++)
            {
                _actions[i].Enable();
            }

            _font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Arial" }, 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private void OnDestroy()
        {
            for (var i = 0; i < _actions.Count; i++)
            {
                _actions[i].Dispose();
            }

            _actions.Clear();
            _audio?.Dispose();
            if (_font != null)
            {
                Destroy(_font);
                _font = null;
            }
        }

        public void SetFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
            if (Flow == null || EvidenceCapture.IsRunning)
            {
                return;
            }

            Flow.OnFocusChanged(hasFocus);
        }

        private void OnApplicationFocus(bool hasFocus) => SetFocus(hasFocus);

        private void Update()
        {
            if (!EvidenceCapture.IsRunning)
            {
                if (Time.timeScale > 0f)
                {
                    Flow.UpdateFrame(Time.realtimeSinceStartupAsDouble, Time.unscaledDeltaTime);
                }
                else
                {
                    Flow.FlushPendingIntents();
                }
            }

            Render();
        }

        public void Render()
        {
            if (Flow == null || _camera == null || _battleStage == null)
            {
                return;
            }

            var screen = Flow.Screen;
            var world = Flow.World;
            var inBattle = screen == QuestScreen.Battle && Flow.ActiveBattle != null;

            if (inBattle)
            {
                for (var i = 0; i < _mapStages.Length; i++)
                {
                    if (_mapStages[i] != null)
                    {
                        _mapStages[i].gameObject.SetActive(false);
                    }
                }

                _battleStage.gameObject.SetActive(true);
                _battleStage.Show(Flow.ActiveBattle!, _database!, Flow.ActiveBattle!.Battle.IsBossBattle);

                _camera.orthographic = true;
                _camera.orthographicSize = ComputeOrthographicSize(5.4f, 9.6f);
                var shakeX = 0f;
                var shakeY = 0f;
                var shakeTicks = Flow.ActiveBattle.ScreenShakeTicks;
                if (shakeTicks > 0)
                {
                    shakeX = ((shakeTicks % 2 == 0) ? 1f : -1f) * 0.14f;
                    shakeY = ((shakeTicks % 3 == 0) ? 1f : -1f) * 0.08f;
                }

                _camera.transform.position = new Vector3(shakeX, shakeY, -10f);
            }
            else
            {
                _battleStage.gameObject.SetActive(false);
                var activeMapId = world != null &&
                                  screen != QuestScreen.Title &&
                                  screen != QuestScreen.HowTo &&
                                  screen != QuestScreen.Ending &&
                                  screen != QuestScreen.Result
                    ? world.Explorer.MapId
                    : Data.Rules.StartMapId;

                EmberlockMapStage? activeStage = null;
                for (var i = 0; i < _mapStages.Length; i++)
                {
                    var st = _mapStages[i];
                    if (st == null)
                    {
                        continue;
                    }

                    var match = string.Equals(st.MapId, activeMapId, StringComparison.Ordinal);
                    st.gameObject.SetActive(match);
                    if (match)
                    {
                        activeStage = st;
                    }
                }

                if (activeStage != null)
                {
                    var map = Data.GetMap(activeMapId);
                    Vector3 focusPos;
                    if (world != null &&
                        screen != QuestScreen.Title &&
                        screen != QuestScreen.HowTo &&
                        screen != QuestScreen.Ending &&
                        screen != QuestScreen.Result)
                    {
                        activeStage.Show(world, out focusPos);
                    }
                    else
                    {
                        // タイトル・遊び方・エンディング・リザルトでは初期配置に戻す（021 の知見）
                        activeStage.ShowStaticPreview(Data);
                        focusPos = new Vector3(map.Width * 0.5f, map.Height * 0.5f, 0f);
                    }

                    _camera.orthographic = true;
                    var halfH = ComputeOrthographicSize(6.6f, 10.5f);
                    _camera.orthographicSize = halfH;

                    var halfW = halfH * Mathf.Max(1.2f, _camera.aspect);
                    var camX = ClampCameraAxis(focusPos.x, map.Width, halfW - 1.2f);
                    var camY = ClampCameraAxis(focusPos.y, map.Height, halfH - 1.4f);
                    _camera.transform.position = new Vector3(camX, camY, -10f);
                }
            }

            _hudView?.Render(Flow);
            _audio?.PlayNewCues(Flow.AudioCues, _settings!.Volume);
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_hudRoot != null && _hudVersion == version)
            {
                return;
            }

            _hudRoot = root;
            _hudVersion = version;
            _hudView = new EmberlockHudView(root, _font);
            _hudView.Render(Flow);
        }

        private void BindPhysicalKey(InputButton button, int keySlot, string bindingPath)
        {
            var action = new InputAction($"{button}_{keySlot}", InputActionType.PassThrough, bindingPath);
            action.performed += ctx =>
            {
                if (!_hasFocus || EvidenceCapture.IsRunning || Flow == null)
                {
                    return;
                }

                if (ctx.control is KeyControl keyControl)
                {
                    var eventTime = Math.Min(ctx.time, Time.realtimeSinceStartupAsDouble);
                    Flow.EnqueueIntent(new Intent(button, keySlot, keyControl.isPressed, eventTime));
                }
            };
            _actions.Add(action);
        }

        private float ComputeOrthographicSize(float minHalfHeight, float minHalfWidth)
        {
            var aspect = _camera != null && _camera.aspect > 0.1f ? _camera.aspect : (16f / 9f);
            return Mathf.Max(minHalfHeight, minHalfWidth / aspect);
        }

        private static float ClampCameraAxis(float target, float mapSpan, float viewHalfSpan)
        {
            if (mapSpan <= viewHalfSpan * 2f)
            {
                return mapSpan * 0.5f;
            }

            return Mathf.Clamp(target, viewHalfSpan, mapSpan - viewHalfSpan);
        }
    }
}
