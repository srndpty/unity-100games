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
    /// シーンの起点。プロジェクト全体の Input Actions から入力を読み、<see cref="PongFlow"/> を進めて
    /// その結果をコート・HUD・効果音へ反映する。
    /// 証跡撮影中は実時間でもプレイヤー入力でも進めず、撮影台本が <see cref="AdvanceTicks"/> で Tick 単位に進める。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class PongGame : MonoBehaviour
    {
        [SerializeField] private PongSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;

        private PongFlow _flow;
        private CourtView _court;
        private PongHud _hud;
        private VisualElement _hudRoot;
        private int _hudVersion;
        private PongAudio _audio;
        private InputAction _move;
        private InputAction _submit;
        private InputAction _jump;
        private InputAction _cancel;

        public PongFlow Flow => _flow;

        public PongSettings Settings => _settings;

        /// <summary>HUD がつながっているルート要素。UXML を読み込むまでは null。</summary>
        public VisualElement HudRoot => _hudRoot;

        private void Awake()
        {
            // 撮影は毎回同じ試合になるよう固定シードを使う。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            _flow = new PongFlow(_settings.Match, _settings.Cpu, seed);
            _court = new CourtView(_settings.Match, transform);
            _audio = new PongAudio(GetComponent<AudioSource>());
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);

            var actions = InputSystem.actions;
            _move = actions.FindAction("Player/Move", throwIfNotFound: true);
            _jump = actions.FindAction("Player/Jump", throwIfNotFound: true);
            _submit = actions.FindAction("UI/Submit", throwIfNotFound: true);
            _cancel = actions.FindAction("UI/Cancel", throwIfNotFound: true);
        }

        private void Update()
        {
            if (!EvidenceCapture.IsRunning)
            {
                var move = ReadPlayerInput();
                Advance(Time.deltaTime, move);
            }

            Render();
        }

        private void OnDestroy()
        {
            _hudRenderer.UnregisterUIReloadCallback(OnHudReloaded);
            _court.Dispose();
            _audio.Dispose();
        }

        /// <summary>撮影台本から使う。プレイヤー入力なしで、ちょうど指定した Tick 数だけ進めて描画する。</summary>
        public void AdvanceTicks(int ticks)
        {
            for (var i = 0; i < ticks && _flow.Screen == AppScreen.Playing; i++)
            {
                Advance(_settings.Match.TickDuration, 0f);
            }

            Render();
        }

        private void Advance(double deltaSeconds, float move)
        {
            _flow.Advance(deltaSeconds, move);
            foreach (var matchEvent in _flow.FrameEvents)
            {
                _audio.Play(matchEvent.Kind);
            }
        }

        private void Render()
        {
            CourtFraming.Fit(_camera, _settings.Match);
            var snapshot = MatchSnapshot.Interpolate(_flow.Previous, _flow.Current, _flow.Alpha);
            var showBall = _flow.Match is { Phase: not MatchPhase.Finished };
            _court.Render(snapshot, showBall);
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
            _hud = new PongHud(root, _settings.Match.WinScore);
        }

        // 画面遷移の操作を処理し、パドルの上下入力を返す。
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

            return _move.ReadValue<Vector2>().y;
        }
    }
}
