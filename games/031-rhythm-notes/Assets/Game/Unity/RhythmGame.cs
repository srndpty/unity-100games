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
        private const string OffsetKey = "InputOffsetMs";
        [SerializeField] private RhythmSettings _settings;

        private readonly List<InputAction> _actions = new();
        private IReadOnlyList<Arrangement> _tracks;
        private AudioSource _audio;
        private PanelRenderer _panel;
        private RhythmHud _hud;
        private Font _font;
        private int _version = -1;
        private int _track;
        private int _offset;
        private double _acceptSince;
        private bool _focused = true;
        private int _focusRestoredFrame = -1;
        private bool _listenerPaused;

        public RhythmSession Session { get; private set; }
        public double StartDsp { get; private set; }
        internal bool HudReady => _hud != null;

        private void Awake()
        {
            _settings.Validate();
            _tracks = Arrangement.All;
            if (_settings.Clips.Length != _tracks.Count) { throw new InvalidOperationException("3曲の音源が必要です。"); }
            for (var i = 0; i < _tracks.Count; i++)
            {
                var song = _tracks[i].Song;
                var clip = _settings.Clips[i];
                if (clip == null || clip.frequency != Song.SampleRate || clip.samples != song.Length)
                {
                    throw new InvalidOperationException("譜面と音源の長さまたは周波数が一致しません。");
                }

                _ = new Scorecard(song, _settings.Rules);
            }

            _offset = EvidenceCapture.IsRunning ? 0 : PlayerPrefs.GetInt(OffsetKey);
            if (!RhythmSession.ValidOffset(_offset)) { _offset = 0; }
            Session = new RhythmSession(_settings.Rules);
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
            Bind("upArrow", _ => MoveSelection(-1));
            Bind("downArrow", _ => MoveSelection(1));
            Bind("leftArrow", _ => ChangeOffset(-5));
            Bind("rightArrow", _ => ChangeOffset(5));
            Bind("t", _ => Select());
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
            _hud = new RhythmHud(root, _font, _tracks, Choose, ChangeOffset, Submit, TogglePause, Select);
            Render();
        }

        private void Update()
        {
            if (!EvidenceCapture.IsRunning)
            {
                Session.Advance(AudioSettings.dspTime);
                Render();
            }
        }

        private void Render() => _hud?.Render(Session, _track, _offset, _settings.LeadSeconds);

        private void Choose(int index)
        {
            if (Interactive && Session.State == Screen.Select) { _track = index; }
        }

        private void MoveSelection(int delta) => Choose((_track + delta + _tracks.Count) % _tracks.Count);

        private void ChangeOffset(int delta)
        {
            if (!Interactive || Session.State != Screen.Select) { return; }
            _offset = Math.Clamp(_offset + delta, -100, 100);
            PlayerPrefs.SetInt(OffsetKey, _offset);
            PlayerPrefs.Save();
        }

        private bool Interactive => _focused && Time.frameCount != _focusRestoredFrame && !EvidenceCapture.IsRunning;

        private void Submit()
        {
            if (!Interactive || Session.State is not (Screen.Select or Screen.Result)) { return; }

            _audio.Stop();
            _audio.clip = _settings.Clips[_track];
            _acceptSince = Time.realtimeSinceStartupAsDouble;
            StartDsp = AudioSettings.dspTime + 1;
            Session.Start(_tracks[_track].Song, _offset, StartDsp);
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
            Session.Start(_tracks[_track].Song, _offset, 0);
            return _tracks[_track].Song;
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
