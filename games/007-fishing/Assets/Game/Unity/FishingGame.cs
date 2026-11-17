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
        private readonly List<Sprite> _sprites = new();
        private readonly List<Texture2D> _textures = new();
        private SpriteRenderer _fish, _tail, _eye, _float, _line, _underLine;
        private InputAction _space;
        private VisualElement _root;
        private Font _font;
        private AudioSource _audio;
        private AudioClip _biteSound, _catchSound, _loseSound;
        private bool _submit, _pause, _back, _cast, _pressed;
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
            var pixel = MakeSprite(false); var oval = MakeSprite(true);
            Place(Create("Sky", pixel, new Color(.55f, .67f, .61f), -10), 0, 3, 28, 10);
            Place(Create("FarBank", oval, new Color(.25f, .39f, .32f), -9), 5, 1.8f, 24, 3);
            Place(Create("Water", pixel, new Color(.045f, .24f, .28f), -8), 0, -3, 28, 8);
            Place(Create("DeepWater", pixel, new Color(.035f, .17f, .22f), -7), 3, -5.8f, 23, 2.5f);
            Place(Create("Surface", pixel, new Color(.65f, .79f, .64f), -6), 0, 1, 28, .06f);
            Place(Create("Bank", pixel, new Color(.34f, .32f, .22f), -5), -11, -2.5f, 4, 7);
            Place(Create("Grass", pixel, new Color(.42f, .49f, .27f), -4), -11, 1, 4, .25f);
            for (var i = 0; i < 12; i++)
            {
                var reed = Create("Reed", pixel, new Color(.33f, .47f, .28f), -3);
                Place(reed, -10.5f + i * .18f, 1.5f + i % 3 * .12f, .07f, 1.1f + i % 3 * .24f);
                var stone = Create("Stone", oval, new Color(.17f, .30f, .29f), -6);
                Place(stone, -7 + i * 1.7f, -6.4f + i % 3 * .1f, .8f, .3f);
            }
            var rod = Create("Rod", pixel, new Color(.86f, .69f, .43f), 1);
            Place(rod, -8.9f, 2.1f, .09f, 2.5f); rod.transform.localRotation = Quaternion.Euler(0, 0, -35);
            _fish = Create("Fish", oval, Color.white, 2); _tail = Create("Tail", oval, Color.white, 1);
            _eye = Create("Eye", oval, new Color(.05f, .12f, .14f), 3);
            _float = Create("Float", oval, new Color(.96f, .57f, .27f), 4);
            _line = Create("Line", pixel, new Color(.75f, .85f, .72f, .65f), 0);
            _underLine = Create("UnderwaterLine", pixel, new Color(.75f, .85f, .72f, .4f), 0);
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
                    if (_point >= 0) { Flow.Select((FishKind)_point); }
                    if (_shift != 0) { Flow.Shift(_shift); }
                    if (Flow.Screen == AppScreen.Select && (_cast || _pressed)) { Flow.Cast(); }
                    else { Flow.SetInput(_pressed, _space.IsPressed()); }
                    if (!ManualTicks) { Flow.Advance(Time.deltaTime); }
                }
            }
            _submit = _pause = _back = _cast = _pressed = false; _shift = 0; _point = -1;
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
            if (!focus) { _submit = _pause = _back = _cast = _pressed = false; _shift = 0; _point = -1; }
        }
        public void Render()
        {
            if (Flow == null) { return; }
            _camera.orthographicSize = Mathf.Max(8, 14f / _camera.aspect); _camera.transform.position = new Vector3(0, 0, -10);
            var game = Flow.Game; var kind = game?.Kind ?? Flow.Selected;
            var distance = (float)(game?.Distance ?? Array.Find(_settings.Game.Fish, fish => fish.Kind == kind).Distance);
            var tick = game?.TickCount ?? 0;
            var x = -8 + distance * .48f; var y = -1.8f - (int)kind * 1.25f;
            var fighting = game?.Phase == FishingPhase.Fighting;
            if (fighting && game.Action == FishAction.Run) { y += Mathf.Sin(tick * .18f) * .28f; }
            else { y += Mathf.Sin(tick * .04f) * .08f; }
            if (game?.Phase == FishingPhase.Landing || game?.Phase == FishingPhase.Caught) { x = -8; y = .3f; }
            var color = kind == FishKind.Crucian ? new Color(.82f, .77f, .51f) : kind == FishKind.Bass ? new Color(.58f, .76f, .67f) : new Color(.90f, .61f, .39f);
            var size = .85f + (int)kind * .2f;
            _fish.color = color; _tail.color = color * .8f;
            var bodyWidth = kind == FishKind.Bass ? 2f : kind == FishKind.Carp ? 1.5f : 1.7f;
            var bodyHeight = kind == FishKind.Carp ? .85f : .65f;
            Place(_fish, x, y, size * bodyWidth, size * bodyHeight); Place(_tail, x + size * bodyWidth * .5f, y, size * .6f, size * .8f);
            Place(_eye, x - size * .45f, y + size * .1f, .10f, .10f);
            _fish.enabled = _tail.enabled = _eye.enabled = game?.Phase != FishingPhase.Escaped;
            var floatX = game == null ? x : game.Phase == FishingPhase.Casting ? Mathf.Lerp(-8, x, 1 - game.RemainingTicks / (float)FishingConfig.Ticks(_settings.Game.CastSeconds)) : x;
            var floatY = game?.Phase == FishingPhase.Biting ? .72f : 1;
            Place(_float, floatX, floatY, .17f, .4f);
            Segment(_line, new Vector2(-8.2f, 3.1f), new Vector2(floatX, floatY));
            _underLine.enabled = fighting;
            Segment(_underLine, new Vector2(floatX, floatY), new Vector2(x, y));
            RenderHud();
        }
        private void OnHud(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _version == version) { return; }
            _version = version; _root = root; _root.style.unityFontDefinition = FontDefinition.FromFont(_font);
            _root.Q<Button>("submit").clicked += () => { if (AcceptInput()) { _submit = true; } };
            _root.Q<Button>("cast").clicked += () => { if (AcceptInput()) { _cast = true; } };
            _root.Q<Button>("resume").clicked += () => { if (AcceptInput()) { _pause = true; } };
            _root.Q<Button>("back").clicked += () => { if (AcceptInput()) { _back = true; } };
            _root.Q<Button>("quit").clicked += () => { if (AcceptInput()) { Application.Quit(); } };
            for (var i = 0; i < 3; i++)
            { var index = i; _root.Q<Button>("point" + i).clicked += () => { if (AcceptInput()) { _point = index; } }; }
            _root.Query<Button>().ForEach(button => button.focusable = false); RenderHud();
        }
        private void Show(string name, bool visible) => _root.Q(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        private void Text(string name, string text) => _root.Q<Label>(name).text = text;
        private void RenderHud()
        {
            if (_root == null) { return; }
            var screen = Flow.Screen; var game = Flow.Game; var kind = game?.Kind ?? Flow.Selected;
            var counts = $"コブナ {Flow.CatchCount(FishKind.Crucian)}   スズキ {Flow.CatchCount(FishKind.Bass)}   コイ {Flow.CatchCount(FishKind.Carp)}";
            Text("collection", $"{Flow.SpeciesCount} / 3 種   |   {Flow.Throws} 投   |   {counts}");
            Show("overlay", screen != AppScreen.Playing); Show("points", screen == AppScreen.Select);
            Show("cast", screen == AppScreen.Select); Show("submit", screen == AppScreen.Title || screen == AppScreen.Result || screen == AppScreen.Complete);
            Show("resume", screen == AppScreen.Paused); Show("back", screen != AppScreen.Title && screen != AppScreen.Playing); Show("quit", screen == AppScreen.Title);
            Show("instruments", game != null && screen == AppScreen.Playing);
            Text("headline", screen == AppScreen.Title ? "STILLWATER" : screen == AppScreen.Select ? "釣り場を選ぶ" : screen == AppScreen.Paused ? "ひと休み" : screen == AppScreen.Complete ? "3種、釣り上げた！" : game?.Phase == FishingPhase.Caught ? "釣れた！" : "逃げられた…");
            var selected = FishName(Flow.Selected);
            Text("message", screen == AppScreen.Title ? "魚の走りを見て、巻く・緩める。\n同じ釣り場で3種の捕獲を目指そう。\n\nSPACEで投げる → アタリでSPACEを押す\n長押しで巻く・離すと緩める。P / ESCで停止。"
                : screen == AppScreen.Select ? $"← → で選択 / {selected}\n" + (Flow.Selected == FishKind.Crucian ? "浅場：休みが長く、巻きやすい" : Flow.Selected == FishKind.Bass ? "中層：引きと休みのバランス型" : "深場：長く走る、力強い魚")
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
        private SpriteRenderer Create(string name, Sprite sprite, Color color, int order)
        {
            var item = new GameObject(name); item.transform.SetParent(transform, false);
            var renderer = item.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = order; return renderer;
        }
        private static void Place(SpriteRenderer item, float x, float y, float width, float height)
        { item.transform.localPosition = new Vector3(x, y); item.transform.localScale = new Vector3(width, height, 1); }
        private static void Segment(SpriteRenderer item, Vector2 from, Vector2 to)
        {
            var delta = to - from; Place(item, (from.x + to.x) / 2, (from.y + to.y) / 2, delta.magnitude, .025f);
            item.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
        private Sprite MakeSprite(bool oval)
        {
            var size = oval ? 64 : 1; var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (var y = 0; y < size; y++) { for (var x = 0; x < size; x++) { texture.SetPixel(x, y, !oval || new Vector2(x - 31.5f, y - 31.5f).sqrMagnitude <= 32 * 32 ? Color.white : Color.clear); } }
            texture.Apply(); _textures.Add(texture); var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size); _sprites.Add(sprite); return sprite;
        }
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
            foreach (var sprite in _sprites) { Destroy(sprite); }
            foreach (var texture in _textures) { Destroy(texture); }
            Destroy(_font); Destroy(_biteSound); Destroy(_catchSound); Destroy(_loseSound);
        }
    }
}
