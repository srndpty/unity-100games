using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Screen = Game.App.Screen;

namespace Game.Unity
{
    [RequireComponent(typeof(AudioSource), typeof(PanelRenderer))]
    public sealed class RhythmGame : MonoBehaviour
    {
        public const string OffsetKey = "InputOffsetMs";
        [SerializeField] private RhythmSettings _settings;

        private readonly List<InputAction> _actions = new();
        private IReadOnlyList<Arrangement> _tracks;
        private AudioSource _audio;
        private PanelRenderer _panel;
        private RhythmHud _hud;
        private Font _font;
        private int _version = -1;
        private int _track;
        private int _savedOffset;
        private double _acceptSince;
        private bool _focused = true;
        private int _focusRestoredFrame = -1;
        private bool _listenerPaused;

        public RhythmSession Session { get; private set; }
        public double StartDsp { get; private set; }

        // 予約した音が実際に聞こえる DSP 時刻。ミキサーの出力バッファぶん dspTime より遅れて鳴る。
        public double HeardStartDsp => StartDsp + OutputLatency;

        // dspTime はミキサーが書き込んだ位置なので、出力キューに積まれたバッファぶん先行している。
        public static double OutputLatency
        {
            get
            {
                AudioSettings.GetDSPBufferSize(out var length, out var count);
                return (double)length * count / AudioSettings.outputSampleRate;
            }
        }

        public AudioClip CalibrationClip => _settings.CalibrationClip;
        public int VisibleBursts => _hud?.VisibleBursts ?? 0;
        internal bool HudReady => _hud != null;

        private void Awake()
        {
            _settings.Validate();
            _tracks = Arrangement.All;
            if (_settings.Clips.Length != _tracks.Count) { throw new InvalidOperationException("3曲の音源が必要です。"); }
            for (var i = 0; i < _tracks.Count; i++)
            {
                Verify(_settings.Clips[i], _tracks[i].Song);
            }

            Verify(_settings.CalibrationClip, Arrangement.Calibration.Song);

            _savedOffset = EvidenceCapture.IsRunning ? 0 : PlayerPrefs.GetInt(OffsetKey);
            if (!Calibration.ValidOffset(_savedOffset)) { _savedOffset = 0; }
            Session = new RhythmSession(_settings.Rules, _savedOffset);
            _audio = GetComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.loop = false;
            _audio.mute = AudioPolicy.ShouldMute;
            _audio.volume = _settings.Volume;
            _panel = GetComponent<PanelRenderer>();
            _font = Font.CreateDynamicFontFromOSFont("Yu Gothic", 24);
            _panel.RegisterUIReloadCallback(OnReload);

            Bind("f", time => Hit(Strike.Pulse, time));
            Bind("j", time => Hit(Strike.Spark, time));
            Bind("escape", _ => TogglePause());
            Bind("enter", _ => Submit());
            Bind("c", _ => Calibrate());
            Bind("upArrow", _ => MoveSelection(-1));
            Bind("downArrow", _ => MoveSelection(1));
            Bind("leftArrow", _ => ChangeOffset(-Calibration.StepMs));
            Bind("rightArrow", _ => ChangeOffset(Calibration.StepMs));
            Bind("t", _ => Select());
        }

        private void Verify(AudioClip clip, Song song)
        {
            if (clip == null || clip.frequency != Song.SampleRate || clip.samples != song.Length)
            {
                throw new InvalidOperationException("譜面と音源の長さまたは周波数が一致しません。");
            }

            _ = new Scorecard(song, _settings.Rules);
        }

        private void Bind(string key, Action<double> action)
        {
            var input = new InputAction(key, InputActionType.Button, "<Keyboard>/" + key);
            input.performed += context =>
            {
                if (Interactive) { action(context.time); }
            };
            _actions.Add(input);
            input.Enable();
        }

        private void Hit(Strike strike, double eventTime)
        {
            if (eventTime < _acceptSince) { return; }

            var realtime = Time.realtimeSinceStartupAsDouble;
            var dsp = AudioSettings.dspTime;
            Session.StrikeAt(strike, RhythmSession.ToDspTime(eventTime, realtime, dsp));
        }

        private void OnReload(PanelRenderer renderer, VisualElement root, int version)
        {
            if (version == _version) { return; }

            _version = version;
            _hud = new RhythmHud(root, _font, _tracks, Choose, ChangeOffset, Submit, Calibrate, TogglePause, Select);
            Render();
        }

        private void Update()
        {
            if (EvidenceCapture.IsRunning) { return; }

            Session.Advance(AudioSettings.dspTime);
            if (Session.Offset != _savedOffset) { SaveOffset(); }
            Render();
        }

        private void Render() => _hud?.Render(Session, _track, _settings.LeadSeconds);

        private void Choose(int index)
        {
            if (Interactive && Session.State == Screen.Select) { _track = index; }
        }

        private void MoveSelection(int delta) => Choose((_track + delta + _tracks.Count) % _tracks.Count);

        private void ChangeOffset(int delta)
        {
            if (Interactive && Session.ChangeOffset(delta)) { SaveOffset(); }
        }

        private void SaveOffset()
        {
            _savedOffset = Session.Offset;
            PlayerPrefs.SetInt(OffsetKey, _savedOffset);
            PlayerPrefs.Save();
        }

        private bool Interactive => _focused && Time.frameCount != _focusRestoredFrame && !EvidenceCapture.IsRunning;

        private void Submit()
        {
            if (!Interactive || Session.State is not (Screen.Select or Screen.Result)) { return; }

            // 結果画面からの「もう一度」は直前と同じ種類（曲か補正テスト）をやり直す。
            Play(Session.State == Screen.Result && Session.Calibrating);
        }

        private void Calibrate()
        {
            if (Interactive && Session.State == Screen.Select) { Play(true); }
        }

        private void Play(bool calibrating)
        {
            _audio.Stop();
            _audio.clip = calibrating ? _settings.CalibrationClip : _settings.Clips[_track];
            _acceptSince = Time.realtimeSinceStartupAsDouble;
            StartDsp = AudioSettings.dspTime + 1;
            if (calibrating) { Session.StartCalibration(Arrangement.Calibration.Song, HeardStartDsp); }
            else { Session.Start(_tracks[_track].Song, HeardStartDsp); }
            _audio.PlayScheduled(StartDsp);
        }

        private void TogglePause()
        {
            if (!Interactive || Session.State is not (Screen.Play or Screen.Paused)) { return; }

            if (Session.State == Screen.Play)
            {
                Pause();
            }
            else
            {
                _acceptSince = Time.realtimeSinceStartupAsDouble;
                ReleasePause();
                Session.TogglePause();
            }
        }

        private void Pause()
        {
            AudioListener.pause = true;
            Session.TogglePause();
            _listenerPaused = true;
        }

        private void ReleasePause()
        {
            if (!_listenerPaused) { return; }

            AudioListener.pause = false;
            _listenerPaused = false;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!_focused && focused) { _focusRestoredFrame = Time.frameCount; }
            _focused = focused;
            if (!focused && Session?.State == Screen.Play && !EvidenceCapture.IsRunning) { Pause(); }
        }

        private void Select()
        {
            if (!Interactive || Session.State is not (Screen.Paused or Screen.Result)) { return; }

            _audio.Stop();
            ReleasePause();
            Session.Select();
        }

        internal Song BeginCapture()
        {
            Session.Start(_tracks[_track].Song, 0);
            return _tracks[_track].Song;
        }

        internal Song BeginCalibrationCapture()
        {
            Session.StartCalibration(Arrangement.Calibration.Song, 0);
            return Arrangement.Calibration.Song;
        }

        internal void CaptureAt(double seconds)
        {
            Session.Advance(seconds);
            Render();
        }

        private void OnDestroy()
        {
            foreach (var action in _actions) { action.Dispose(); }
            if (_panel != null) { _panel.UnregisterUIReloadCallback(OnReload); }
            if (_audio != null) { _audio.Stop(); }
            ReleasePause();
            Destroy(_font);
        }
    }
}
