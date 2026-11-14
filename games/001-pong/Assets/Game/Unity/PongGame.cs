using System;
using Game.App;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// シーンの起点。プロジェクト全体の Input Actions から入力を読み、<see cref="PongFlow"/> を進めて
    /// その結果をコート・HUD・効果音へ反映する。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class PongGame : MonoBehaviour
    {
        [SerializeField] private PongSettings _settings;
        [SerializeField] private UIDocument _hudDocument;

        private PongFlow _flow;
        private CourtView _court;
        private PongHud _hud;
        private PongAudio _audio;
        private InputAction _move;
        private InputAction _submit;
        private InputAction _jump;
        private InputAction _cancel;

        public PongFlow Flow => _flow;

        private void Awake()
        {
            _flow = new PongFlow(_settings.Match, _settings.Cpu, Environment.TickCount);
            _court = new CourtView(_settings.Match, transform);
            _hud = new PongHud(_hudDocument.rootVisualElement, _settings.Match.WinScore);
            _audio = new PongAudio(GetComponent<AudioSource>());

            var actions = InputSystem.actions;
            _move = actions.FindAction("Player/Move", throwIfNotFound: true);
            _jump = actions.FindAction("Player/Jump", throwIfNotFound: true);
            _submit = actions.FindAction("UI/Submit", throwIfNotFound: true);
            _cancel = actions.FindAction("UI/Cancel", throwIfNotFound: true);
        }

        private void Update()
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

            _flow.Advance(Time.deltaTime, _move.ReadValue<Vector2>().y);

            foreach (var matchEvent in _flow.FrameEvents)
            {
                _audio.Play(matchEvent.Kind);
            }

            var snapshot = MatchSnapshot.Interpolate(_flow.Previous, _flow.Current, _flow.Alpha);
            var showBall = _flow.Match is { Phase: not MatchPhase.Finished };
            _court.Render(snapshot, showBall);
            _hud.Render(_flow);
        }
    }
}
