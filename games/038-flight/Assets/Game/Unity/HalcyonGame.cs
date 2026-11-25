using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// キーを時刻つきの意図にして Flow へ渡し、その Tick の状態をそのまま描く（補間しない）。
    /// カメラは機体に固定した後方の位置から機首の先を見る（機体と一緒に傾く）。タイトルは滑走路の自機を斜め前から見る。
    /// </summary>
    public sealed class HalcyonGame : MonoBehaviour
    {
        /// <summary>機体の座標で見たカメラの位置（m）。</summary>
        public static readonly Vector3 ChaseOffset = new(0f, 5f, -30f);

        /// <summary>カメラが見る、機体の座標の点（m）。機体が画面の中央より下に来るよう、機首の線より上を見る。</summary>
        public static readonly Vector3 ChaseLook = new(0f, 6.5f, 60f);

        /// <summary>撃墜・墜落の後に、最後の位置を見るカメラの後ろへの距離と高さ（m）。</summary>
        public static readonly Vector2 DownView = new(90f, 35f);

        /// <summary>タイトルのカメラの位置（自機から見た m、北が +z）。</summary>
        public static readonly Vector3 TitleOffset = new(-13f, 4.5f, 21f);

        [SerializeField] private HalcyonSettings _settings;
        [SerializeField] private PanelRenderer _hudRenderer;
        [SerializeField] private Camera _camera;
        [SerializeField] private HalcyonStage _stage;

        private SortieFlow _flow;
        private FlightConfig _flight;
        private MissionConfig _mission;
        private HalcyonAudio _audio;
        private HalcyonHud _hud;
        private VisualElement _root;
        private int _hudVersion = -1;
        private HalcyonInput _input;
        private bool _focused = true;

        public SortieFlow Flow => _flow;

        public HalcyonSettings Settings => _settings;

        public HalcyonStage Stage => _stage;

        public VisualElement HudRoot => _root;

        public HalcyonHud Hud => _hud;

        public Camera View => _camera;

        private void Awake()
        {
            (_flight, _mission) = _settings.Validate();

            // 再挑戦のシードは起動時の値から 1 ずつ進める（010）。撮影中は決まったシード。
            var seed = EvidenceCapture.IsRunning ? EvidenceCapture.Seed : Environment.TickCount;
            _flow = new SortieFlow(_flight, _mission, seed);
            _stage.Initialize(_mission);
            _audio = gameObject.AddComponent<HalcyonAudio>();
            _audio.Bind(_settings.Volume);
            _input = new HalcyonInput();
        }

        private void OnEnable()
        {
            _hudRenderer.RegisterUIReloadCallback(OnHudReloaded);
            _input.Enable();
        }

        private void OnDisable()
        {
            _input.Disable();
        }

        private void OnDestroy()
        {
            _input?.Dispose();
        }

        private void Update()
        {
            if (!EvidenceCapture.IsRunning)
            {
                if (_focused) { _input.ApplyTo(_flow); }
                _flow.Advance(HalcyonInput.Now);
            }

            _input.Clear();
            Render();
        }

        private void OnApplicationFocus(bool focus)
        {
            // 撮影は台本が Tick を進めるので、ウィンドウのフォーカスで一時停止しない（別のウィンドウを触っても撮影が止まらない）。
            if (EvidenceCapture.IsRunning) { return; }
            _focused = focus;
            _flow?.SetFocus(focus);
            if (!focus) { _input?.Clear(); }
        }

        public void Render()
        {
            if (_flow == null) { return; }
            PresentWorld();
            _audio.Play(_flow, _flow.TakeMoments());
            _hud?.Show(_flow, _camera);
        }

        /// <summary>機体・ミサイル・煙・カメラだけを、その Tick の状態で置く。</summary>
        public void PresentWorld()
        {
            if (_flow == null) { return; }
            var mission = _flow.Mission;
            _stage.Show(mission);
            var plane = mission.Player.View;
            var position = HalcyonSpace.Position(plane.Position);
            if (_flow.Screen == AppScreen.Title)
            {
                var eye = position + TitleOffset;
                _camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(position + new Vector3(0f, 1.5f, 0f) - eye));
                return;
            }

            if (mission.Player.Down)
            {
                // 撃墜・墜落の後は、最後の位置を後ろ上から水平に見る（機体の傾きに合わせない）。
                var back = HalcyonSpace.Velocity(plane.Velocity);
                back.y = 0f;
                back = back.sqrMagnitude > 1f ? back.normalized : Vector3.forward;
                var eye = position - (back * DownView.x) + (Vector3.up * DownView.y);
                if (eye.y < 5f) { eye.y = 5f; }
                _camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(position - eye, Vector3.up));
                return;
            }

            var rotation = HalcyonSpace.Rotation(plane.Attitude);
            var camera = position + (rotation * ChaseOffset);

            // 地面より下へは潜らない。
            if (camera.y < 1f) { camera.y = 1f; }
            var look = position + (rotation * ChaseLook);
            _camera.transform.SetPositionAndRotation(camera, Quaternion.LookRotation(look - camera, rotation * Vector3.up));
        }

        private void OnHudReloaded(PanelRenderer renderer, VisualElement root, int version)
        {
            if (_root != null && _hudVersion == version) { return; }
            _root = root;
            _hudVersion = version;
            _hud = new HalcyonHud(root, _flight, _mission);
            if (_flow != null) { _hud.Show(_flow, _camera); }
        }
    }
}
