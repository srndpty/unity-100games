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
    /// <summary>
    /// 入力・HUD・場・音をつなぐ。キーとクリックは起きた順に 1 本の意図の列にして App（<see cref="DeckFlow"/>）へ渡し、
    /// 描画は App の卓（出来事を再生した結果）から行う。音も再生した出来事から鳴らす。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class LedgerGame : MonoBehaviour
    {
        [SerializeField] private LedgerSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private LedgerStage _stage;
        [SerializeField] private Sprite[] _cardIcons = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _intentIcons = Array.Empty<Sprite>();

        // クリックは押した時点の画面を覚え、画面が変わっていたら捨てる（033）。
        private readonly List<(Intent Intent, bool Pointer, AppScreen Screen, bool Paused)> _intents = new();
        private readonly List<InputAction> _actions = new();
        private readonly List<AudioClip> _clips = new();
        private readonly List<(Func<BattleEvent, bool> Match, AudioClip Clip)> _sounds = new();
        private bool _hasFocus = true;
        private bool _quitCalled;
        private int _focusRestoredFrame = -2;
        private int _version = -1;
        private int _heardBattle;
        private int _heardCursor;
        private DeckConfig _rules;
        private GameData _data;
        private Font _font;
        private AudioSource _audio;

        public DeckFlow Flow { get; private set; }

        public LedgerSettings Settings => _settings;

        public LedgerStage Stage => _stage;

        public Camera ViewCamera => _camera;

        public LedgerHud Hud { get; private set; }

        public VisualElement HudRoot => Hud?.Root;

        public DeckConfig Rules => _rules;

        public GameData Data => _data;

        /// <summary>音を鳴らした回数（テスト用）。</summary>
        public int SoundsPlayed { get; private set; }

        /// <summary>直前に鳴らした音の名前（テスト用）。</summary>
        public string LastSound { get; private set; } = string.Empty;

        /// <summary>終了の処理。エディタでは Application.Quit が働かないので、テストが差し替えて確かめる。</summary>
        public Action QuitHandler { get; set; } = Application.Quit;

        public AudioSource Audio => _audio;

        private void Awake()
        {
            _data = _settings.Validate();
            _rules = _settings.Rules.Copy();
            var mismatch = _stage.Mismatch(_data) ?? IconMismatch();
            if (mismatch != null) { throw new InvalidOperationException("Prefab・絵がデータと一致しません：" + mismatch); }

            // 再戦は新しいシード（起動時の値から挑戦ごとに 1 進める。010）。撮影中は固定のシード。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            Flow = new DeckFlow(_data, _rules, _settings.Timing, seed);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = _settings.Volume;
            _audio.mute = AudioPolicy.ShouldMute;

            // 1 フレームに複数の出来事を再生したら、この順で最初の 1 つだけ鳴らす。
            Sound(e => e.Kind == EventKind.Victory, "Victory", 440, 1320, .9f);
            Sound(e => e.Kind == EventKind.Defeat, "Defeat", 400, 60, .9f);
            Sound(e => e.Kind == EventKind.Died, "Died", 520, 140, .3f);
            Sound(e => e.Kind == EventKind.Damaged && e.Target == 0 && e.Amount > 0, "Hurt", 300, 110, .18f);
            Sound(e => e.Kind == EventKind.Damaged && e.Amount > 0, "Hit", 260, 180, .08f);
            Sound(e => e.Kind == EventKind.Damaged, "Blocked", 900, 700, .06f);
            Sound(e => e.Kind == EventKind.PoisonTick, "Poison", 180, 140, .12f);
            Sound(e => e.Kind == EventKind.Summoned, "Summon", 300, 900, .3f);
            Sound(e => e.Kind == EventKind.Exhausted, "Exhaust", 1400, 300, .16f);
            Sound(e => e.Kind == EventKind.BlockGained, "Block", 700, 900, .08f);
            Sound(e => e.Kind == EventKind.StatusApplied && e.Amount > 0, "Status", 600, 400, .1f);
            Sound(e => e.Kind == EventKind.Shuffled, "Shuffle", 1000, 1300, .12f);
            Sound(e => e.Kind == EventKind.TurnStarted, "Turn", 660, 880, .14f);
            Sound(e => e.Kind == EventKind.CardPlayed, "Card", 800, 1000, .05f);
            Sound(e => e.Kind == EventKind.Fizzled, "Fizzle", 300, 280, .08f);

            Bind(IntentKind.Confirm, 0, "<Keyboard>/enter", "<Keyboard>/numpadEnter");
            Bind(IntentKind.Cancel, 0, "<Keyboard>/escape", "<Mouse>/rightButton");
            Bind(IntentKind.HowTo, 0, "<Keyboard>/h");
            Bind(IntentKind.ToTitle, 0, "<Keyboard>/t");
            Bind(IntentKind.EndTurn, 0, "<Keyboard>/e");
            Bind(IntentKind.CycleTarget, 1, "<Keyboard>/rightArrow", "<Keyboard>/tab");
            Bind(IntentKind.CycleTarget, -1, "<Keyboard>/leftArrow");
            for (int i = 0; i < 10; i++) { Bind(IntentKind.Number, i, $"<Keyboard>/{(i + 1) % 10}"); }

            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            Render();
        }

        private string IconMismatch()
        {
            if (_cardIcons.Length != _data.Cards.Count) { return $"カードの絵が {_cardIcons.Length} 枚（データは {_data.Cards.Count} 種）"; }
            for (int i = 0; i < _cardIcons.Length; i++)
            {
                if (_cardIcons[i] == null || _cardIcons[i].name != "Card_" + _data.Cards[i].Id) { return $"カードの絵 {i} が {_data.Cards[i].Id} ではない"; }
            }

            if (_intentIcons.Length != Enum.GetValues(typeof(IntentIcon)).Length) { return "意図の印の数が違う"; }
            return null;
        }

        private bool AcceptInput() => _hasFocus && !EvidenceCapture.IsRunning;

        /// <summary>押した瞬間だけ効くキー。キーごとに別のアクションにする（035・037・042）。</summary>
        private void Bind(IntentKind kind, int value, params string[] bindings)
        {
            foreach (var binding in bindings)
            {
                var action = new InputAction($"{kind}{value}{binding}", InputActionType.Button);
                action.AddBinding(binding);
                action.performed += _ => Queue(new Intent(kind, value), false);
                action.Enable();
                _actions.Add(action);
            }
        }

        /// <summary>意図を列に積む（HUD のクリックは pointer）。</summary>
        public void Queue(Intent intent, bool pointer)
        {
            if (AcceptInput() && Flow != null) { _intents.Add((intent, pointer, Flow.Screen, Flow.Paused)); }
        }

        private void Update()
        {
            if (AcceptInput())
            {
                var justRestored = Time.frameCount <= _focusRestoredFrame + 1;
                foreach (var (intent, pointer, screen, paused) in _intents)
                {
                    // ウィンドウを選び直すクリックで操作しない。押した後に画面が変わったクリックも捨てる。
                    if (pointer && (justRestored || screen != Flow.Screen || paused != Flow.Paused)) { continue; }
                    Flow.Submit(intent);
                }
            }

            _intents.Clear();

            // 撮影中は台本が Tick を進める。演出は Time.deltaTime で進むので、テストは timeScale = 0 で止められる。
            if (!EvidenceCapture.IsRunning) { Flow.Advance(Time.deltaTime); }
            if (Flow.QuitRequested && !_quitCalled)
            {
                _quitCalled = true;
                QuitHandler();
            }
            ConsumeEvents();
            Render();
        }

        private void OnApplicationFocus(bool focus)
        {
            _hasFocus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            _intents.Clear();
            if (focus) { _focusRestoredFrame = Time.frameCount; }
        }

        /// <summary>前回から再生した出来事のうち、優先の高い 1 つの音を鳴らす。撮影台本も Tick を進めた後に呼ぶ。</summary>
        public void ConsumeEvents()
        {
            var playback = Flow.Playback;
            if (playback == null) { return; }
            if (_heardBattle != Flow.BattleNumber)
            {
                _heardBattle = Flow.BattleNumber;
                _heardCursor = 0;
            }

            var from = _heardCursor;
            _heardCursor = playback.Cursor;
            if (from >= _heardCursor) { return; }
            foreach (var (match, clip) in _sounds)
            {
                for (int i = from; i < _heardCursor; i++)
                {
                    if (!match(playback.Events[i])) { continue; }
                    _audio.PlayOneShot(clip);
                    SoundsPlayed++;
                    LastSound = clip.name;
                    return;
                }
            }
        }

        /// <summary>今の状態を描く。</summary>
        public void Render()
        {
            if (Flow == null) { return; }
            if (Flow.Screen == AppScreen.Battle && Flow.Playback != null)
            {
                _stage.Render(Flow.Playback, _settings.Timing, Flow.Targeting.HasCard ? Flow.Targeting.Cursor : null);
            }
            else
            {
                _stage.RenderIdle();
            }

            Hud?.Render(Flow, _stage, _camera);
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (Hud != null && _version == version) { return; }
            _version = version;
            root.style.unityFont = _font;
            Hud = new LedgerHud(root, _rules, _cardIcons, _intentIcons, intent => Queue(intent, true));
            Render();
        }

        // 音程を from から to へ動かす矩形波。振幅は控えめ。
        private void Sound(Func<BattleEvent, bool> match, string name, int from, int to, float seconds)
        {
            var samples = new float[(int)(44100 * seconds)];
            var phase = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = (float)i / samples.Length;
                phase += Mathf.Lerp(from, to, t) / 44100f;
                samples[i] = (phase % 1f < .5f ? .05f : -.05f) * (1f - t);
            }

            var clip = AudioClip.Create(name, samples.Length, 1, 44100, false);
            clip.SetData(samples, 0);
            _clips.Add(clip);
            _sounds.Add((match, clip));
        }

        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            if (_hudRenderer != null) { _hudRenderer.UnregisterUIReloadCallback(OnHudReloaded); }
            Destroy(_font);
            foreach (var clip in _clips) { Destroy(clip); }
        }
    }
}
