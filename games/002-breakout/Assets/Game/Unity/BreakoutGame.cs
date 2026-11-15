using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// シーンの起点。プロジェクト全体の Input Actions から入力を読み、<see cref="BreakoutFlow"/> を進めて
    /// その結果をフィールド・HUD・効果音へ反映する。
    /// 証跡撮影中は実時間でもプレイヤー入力でも進めず、撮影台本が <see cref="AdvanceUntil"/> で Tick 単位に進める。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class BreakoutGame : MonoBehaviour
    {
        [SerializeField] private BreakoutSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;

        private BreakoutFlow _flow;
        private FieldView _field;
        private BreakoutHud _hud;
        private VisualElement _hudRoot;
        private int _hudVersion;
        private BreakoutAudio _audio;
        private InputAction _move;
        private InputAction _submit;
        private InputAction _jump;
        private InputAction _cancel;

        public BreakoutFlow Flow => _flow;

        public BreakoutSettings Settings => _settings;

        /// <summary>HUD がつながっているルート要素。UXML を読み込むまでは null。</summary>
        public VisualElement HudRoot => _hudRoot;

        private void Awake()
        {
            // 撮影は毎回同じゲームになるよう固定シードを使う。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            _flow = new BreakoutFlow(_settings.Game, seed);
            _field = new FieldView(_settings.Game, transform);
            _audio = new BreakoutAudio(GetComponent<AudioSource>());
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);

            var actions = InputSystem.actions;
            _move = actions.FindAction("Player/Move", throwIfNotFound: true);
            _jump = actions.FindAction("Player/Jump", throwIfNotFound: true);
            _submit = actions.FindAction("UI/Submit", throwIfNotFound: true);
            _cancel = actions.FindAction("UI/Cancel", throwIfNotFound: true);
        }

        private void Update()
        {
            if (EvidenceCapture.IsRunning)
            {
                Render(_flow.Current, 1f);
                return;
            }

            var move = ReadPlayerInput();
            Advance(Time.deltaTime, move);
            Render(BreakoutSnapshot.Interpolate(_flow.Previous, _flow.Current, _flow.Alpha), _flow.Alpha);
        }

        private void OnDestroy()
        {
            _hudRenderer.UnregisterUIReloadCallback(OnHudReloaded);
            _field.Dispose();
            _audio.Dispose();
        }

        /// <summary>
        /// 撮影台本とテストから使う。<paramref name="until"/> が成り立つか <paramref name="maxTicks"/> に達するまで、1 Tick ずつ進めて描画する。
        /// パドルは <paramref name="move"/> に従い、発射待ちならすぐ発射する。成り立ったら true。
        /// 証跡は「その Tick の状態」を写すため、Tick 間の補間はせず最新の Tick を描く。
        /// </summary>
        public bool AdvanceUntil(Func<BreakoutState, bool> until, int maxTicks, Func<BreakoutState, float> move)
        {
            var reached = false;
            for (var i = 0; i < maxTicks && _flow.Screen == AppScreen.Playing && !reached; i++)
            {
                var game = _flow.Game;
                if (game.Phase == GamePhase.Serving)
                {
                    _flow.Submit();
                }

                Advance(_settings.Game.TickDuration, move(game));
                reached = until(game);
            }

            Render(_flow.Current, 1f);
            return reached;
        }

        private void Advance(double deltaSeconds, float move)
        {
            _flow.Advance(deltaSeconds, move);
            foreach (var gameEvent in _flow.FrameEvents)
            {
                _audio.Play(gameEvent.Kind);
            }
        }

        private void Render(BreakoutSnapshot snapshot, float alpha)
        {
            FieldFraming.Fit(_camera, _settings.Game);
            _field.Render(_flow.Game, snapshot, alpha);
            _hud?.Render(_flow);
        }

        // UXML を読み込むたび（エディタで編集したときのライブリロードを含む）に、HUD を新しいルート要素へつなぎ直す。
        // UI が変わっていない（版が同じ）通知は無視する。
        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_hud != null && version == _hudVersion)
            {
                return;
            }

            _hudVersion = version;
            _hudRoot = root;
            _hud = new BreakoutHud(root, _settings.Game);
        }

        // 画面遷移と発射の操作を処理し、パドルの左右入力を返す。
        private float ReadPlayerInput()
        {
            if (_submit.WasPressedThisFrame() || _jump.WasPressedThisFrame())
            {
                _flow.Submit();
            }

            if (_cancel.WasPressedThisFrame())
            {
                if (_flow.Screen == AppScreen.Title)
                {
                    Application.Quit();
                }
                else
                {
                    _flow.Back();
                }
            }

            return _move.ReadValue<Vector2>().x;
        }
    }
}
