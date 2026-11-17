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
    public sealed class FishingGame : MonoBehaviour
    {
        [SerializeField] private FishingSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        private readonly List<InputAction> _actions = new();
        [SerializeField] private FishingView _view;
        private InputAction _space;
        private VisualElement _root;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _biteSound, _catchSound, _loseSound;
        private bool _submit, _pause, _back, _pressed;
        private int _shift;
        private int _point = -1;
        private int _version = -1;
        private bool _focus = true;
        private FishingPhase _lastPhase;
        public FishingFlow Flow { get; private set; }
        public FishingSettings Settings => _settings;
        public VisualElement HudRoot => _root;
        public bool ManualTicks { get; set; }
        public static string FishName(FishKind kind) => kind == FishKind.Crucian ? "コブナ" : kind == FishKind.Bass ? "スズキ" : "コイ";
        private void Awake()
        {
            _settings.Validate(); Flow = new FishingFlow(_settings.Game, EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount);
            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _audio = GetComponent<AudioSource>(); _audio.playOnAwake = false; _audio.volume = _settings.Volume; _audio.mute = AudioPolicy.ShouldMute;
            _biteSound = Tone("Bite", 720, .09f); _catchSound = Tone("Catch", 1000, .16f); _loseSound = Tone("Escape", 180, .16f);
            _space = Action("Fishing", "<Keyboard>/space"); _space.performed += _ => { if (AcceptInput()) { _pressed = true; } };
            Button("Submit", () => _submit = true, "<Keyboard>/enter");
            Button("Pause", () => _pause = true, "<Keyboard>/p", "<Keyboard>/escape");
            Button("Left", () => _shift = -1, "<Keyboard>/leftArrow"); Button("Right", () => _shift = 1, "<Keyboard>/rightArrow");
            _hudRenderer.RegisterUIReloadCallback(OnHud); Render();
        }
        private bool AcceptInput() => _focus && !EvidenceCapture.IsRunning;
        private InputAction Action(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var binding in bindings) { action.AddBinding(binding); }
            action.Enable(); _actions.Add(action); return action;
        }
        private void Button(string name, System.Action callback, params string[] bindings)
        { Action(name, bindings).performed += _ => { if (AcceptInput()) { callback(); } }; }
        private void Update()
        {
            if (AcceptInput())
            {
                if (_back) { Flow.Back(); }
                else if (_pause) { Flow.TogglePause(); }
                else if (_submit) { Flow.Submit(); }
                else
                {
                    if (_point >= 0) { Flow.Select((FishKind)_point); Flow.Cast(); }
                    if (_shift != 0) { Flow.Shift(_shift); }
                    if (Flow.Screen == AppScreen.Select && _pressed) { Flow.Cast(); }
                    else { Flow.SetInput(_pressed, _space.IsPressed()); }
                    if (!ManualTicks) { Flow.Advance(Time.deltaTime); }
                }
            }
            _submit = _pause = _back = _pressed = false; _shift = 0; _point = -1;
            PlayEvents(); Render();
        }
        public void Step(FishingCommand command)
        { Flow.SetInput(command.Hook, command.Reel); Flow.StepOneTick(); PlayEvents(); }
        private void PlayEvents()
        {
            var phase = Flow.Game?.Phase ?? FishingPhase.Casting;
            if (phase == _lastPhase) { return; }
            if (phase == FishingPhase.Biting) { _audio.PlayOneShot(_biteSound); }
            else if (phase == FishingPhase.Caught) { _audio.PlayOneShot(_catchSound); }
            else if (phase == FishingPhase.Escaped) { _audio.PlayOneShot(_loseSound); }
            _lastPhase = phase;
        }
        private void OnApplicationFocus(bool focus)
        {
            _focus = focus;
            if (Flow == null || EvidenceCapture.IsRunning) { return; }
            Flow.SetFocus(focus);
            if (!focus) { _submit = _pause = _back = _pressed = false; _shift = 0; _point = -1; }
        }
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(8, 14f / _camera.aspect); _camera.transform.position = new Vector3(0, 0, -10);
            _view.Render(Flow.Game, Flow.Selected, _settings.Game);
            RenderHud();
        }
        private void OnHud(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _version = version; _root = root; _root.style.unityFontDefinition = FontDefinition.FromFont(_font);
            _root.Q<Button>("submit").clicked += () => { if (AcceptInput()) { _submit = true; } };
            _root.Q<Button>("resume").clicked += () => { if (AcceptInput()) { _pause = true; } };
            _root.Q<Button>("back").clicked += () => { if (AcceptInput()) { _back = true; } };
            _root.Q<Button>("quit").clicked += () => { if (AcceptInput()) { Application.Quit(); } };
            for (var i = 0; i < 3; i++)
            { var index = i; _root.Q<Button>("point" + i).clicked += () => { if (AcceptInput()) { _point = index; } }; }
            _root.Query<Button>().ForEach(button => button.focusable = false); RenderHud();
        }
        private void Show(string name, bool visible) => _root.Q(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        private void Text(string name, string text)
        {
            _root.Q<Label>(name).text = text;
            if (name == "status") { Show(name, text.Length > 0); }
        }
        private void RenderHud()
        {
            if (_root == null) { return; }
            var screen = Flow.Screen; var game = Flow.Game; var kind = game?.Kind ?? Flow.Selected;
            var counts = $"コブナ {Flow.CatchCount(FishKind.Crucian)}   スズキ {Flow.CatchCount(FishKind.Bass)}   コイ {Flow.CatchCount(FishKind.Carp)}";
            Text("collection", $"{Flow.SpeciesCount} / 3 種   |   {Flow.Throws} 投   |   {counts}");
            Show("overlay", screen != AppScreen.Playing); Show("points", screen == AppScreen.Select);
            Show("submit", screen == AppScreen.Title || screen == AppScreen.Result || screen == AppScreen.Complete);
            Show("resume", screen == AppScreen.Paused); Show("back", screen != AppScreen.Title && screen != AppScreen.Playing); Show("quit", screen == AppScreen.Title);
            Show("instruments", game != null && screen == AppScreen.Playing);
            Text("headline", screen == AppScreen.Title ? "STILLWATER" : screen == AppScreen.Select ? "釣り場を選ぶ" : screen == AppScreen.Paused ? "ひと休み" : screen == AppScreen.Complete ? "3種、釣り上げた！" : game?.Phase == FishingPhase.Caught ? "釣れた！" : "逃げられた…");
            var selected = FishName(Flow.Selected);
            Text("message", screen == AppScreen.Title ? "魚の走りを見て、巻く・緩める。\n同じ釣り場で3種の捕獲を目指そう。\n\nSPACEで投げる → アタリでSPACEを押す\n合わせたらそのまま長押しで巻く・離すと緩める。P / ESCで停止。"
                : screen == AppScreen.Select ? $"クリックで決定 / ← → で選択・SPACEで決定\n{selected}\n" + (Flow.Selected == FishKind.Crucian ? "浅場：休みが長く、巻きやすい" : Flow.Selected == FishKind.Bass ? "中層：引きと休みのバランス型" : "深場：長く走る、力強い魚")
                : screen == AppScreen.Paused ? "時間は止まっています。再開後はSPACEを離してから操作。"
                : screen == AppScreen.Complete ? $"{Flow.Throws}投で3種を捕獲\n{counts}\nもう一度、最初から挑戦できます。"
                : game?.Phase == FishingPhase.Caught ? $"{FishName(kind)}を捕獲！\n{counts}\n次のポイントへ進もう。" : EscapeText(game?.Reason ?? EscapeReason.None));
            _root.Q<Button>("submit").text = screen == AppScreen.Title ? "はじめる / ENTER" : screen == AppScreen.Complete ? "もう一度 / ENTER" : "次の一投 / ENTER";
            for (var i = 0; i < 3; i++) { _root.Q<Button>("point" + i).EnableInClassList("selected", i == (int)Flow.Selected); }
            Text("hint", screen == AppScreen.Playing ? "SPACE 長押し：巻く / 離す：緩める    P / ESC：停止" : "ENTER：開始・次へ    ← →：ポイント選択    SPACE：投げる");
            if (game == null) { Text("status", ""); return; }
            Text("fishname", FishName(kind));
            Text("action", game.Phase == FishingPhase.Fighting ? game.Stamina <= 0 ? "力尽きた / 巻いて寄せる" : game.Action == FishAction.Run ? "走る / 張力に注意" : game.Action == FishAction.Rest ? "休む / 巻くチャンス" : "向き直る" : "魚の動きを見よう");
            Text("tension-label", $"張力  {game.Tension:F0} / 100");
            _root.Q("needle").style.left = Length.Percent((float)game.Tension * .98f);
            Text("warning", game.HighTicks > 0 ? $"糸切れまで {(FishingConfig.Ticks(_settings.Game.BreakSeconds) - game.HighTicks) / 60f:F1}秒！ 緩めよう"
                : game.LowTicks > 0 ? $"針外れまで {(FishingConfig.Ticks(_settings.Game.SlackSeconds) - game.LowTicks) / 60f:F1}秒！ 巻こう" : "張力20〜80で魚が消耗する");
            var profile = Array.Find(_settings.Game.Fish, fish => fish.Kind == kind);
            Text("stamina-label", $"スタミナ  {game.Stamina:F0} / {profile.Stamina:F0}");
            var stamina = _root.Q<ProgressBar>("stamina"); stamina.value = (float)(game.Stamina / profile.Stamina * 100); stamina.title = "";
            Text("distance", $"岸まで  {game.Distance:F1} m");
            Text("status", screen != AppScreen.Playing ? "" : game.Phase == FishingPhase.Biting ? "アタリ！ SPACEで合わせる"
                : game.Phase == FishingPhase.Casting ? "キャスト…" : game.Phase == FishingPhase.Approaching ? "魚が近づいてきた…"
                : game.Phase == FishingPhase.Roaming ? "ウキを見て待とう" : game.Phase == FishingPhase.Landing ? "取り込み中…" : "");
        }
        private static string EscapeText(EscapeReason reason) => reason == EscapeReason.EarlyHook ? "合わせが早かった。アタリの表示を待とう。"
            : reason == EscapeReason.MissedBite ? "アタリを見逃した。表示が出たらSPACE！"
            : reason == EscapeReason.BrokenLine ? "張力が高すぎて糸が切れた。危険なときは離して緩めよう。"
            : reason == EscapeReason.LooseHook ? "糸が緩みすぎて針が外れた。低張力では巻こう。" : "魚が遠くへ逃げた。休んでいる間に巻いて寄せよう。";
        private static AudioClip Tone(string name, int frequency, float seconds)
        {
            var data = new float[(int)(44100 * seconds)];
            for (var i = 0; i < data.Length; i++) { data[i] = .05f * Mathf.Sin(2 * Mathf.PI * frequency * i / 44100f) * (1f - (float)i / data.Length); }
            var clip = AudioClip.Create(name, data.Length, 1, 44100, false); clip.SetData(data, 0); return clip;
        }
        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            if (_hudRenderer != null) { _hudRenderer.UnregisterUIReloadCallback(OnHud); }
            Destroy(_font); Destroy(_biteSound); Destroy(_catchSound); Destroy(_loseSound);
        }
    }
}
