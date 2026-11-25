using Game.App;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// Hud.uxml の要素を画面ごとに出し分け、その Tick の状態を書く。文字は値が変わったときだけ作り直す。
    /// 飛行中は、機首（十字）と速度の向き（翼つきの円）を画面に投影して<b>機首と速度方向のずれ</b>を見せる。
    /// </summary>
    public sealed class HalcyonHud
    {
        /// <summary>発射の拒否を表示する Tick 数。</summary>
        public const int RejectTicks = 45;

        /// <summary>撃墜の表示を出す Tick 数。</summary>
        public const int KillTicks = 120;

        /// <summary>被弾で画面を赤くする Tick 数。</summary>
        public const int FlashTicks = 12;

        /// <summary>レーダーの半径（m）。</summary>
        public const float RadarRange = 6000f;

        /// <summary>敵のミサイルを警告する距離（µm）。</summary>
        public static readonly long MissileWarningRange = 3000 * Units.PerMeter;

        /// <summary>地面への接近を警告する高度（µm）。</summary>
        public static readonly long PullUpAltitude = 250 * Units.PerMeter;

        private readonly MissionConfig _config;
        private readonly FlightConfig _flight;
        private readonly VisualElement _root;
        private readonly VisualElement _flightLayer;
        private readonly VisualElement _titleLayer;
        private readonly VisualElement _pauseLayer;
        private readonly VisualElement _resultLayer;
        private readonly VisualElement _hitFlash;
        private readonly VisualElement _boresight;
        private readonly VisualElement _fpm;
        private readonly VisualElement[] _targets = new VisualElement[MissionConfig.EnemyCount + 1];
        private readonly Label[] _targetLabels = new Label[MissionConfig.EnemyCount + 1];
        private readonly VisualElement[] _radarDots = new VisualElement[MissionConfig.EnemyCount + 1];
        private readonly VisualElement _radarRunway;
        private readonly Label _speed;
        private readonly Label _altitude;
        private readonly Label _throttle;
        private readonly Label _alpha;
        private readonly Label _missiles;
        private readonly Label _enemies;
        private readonly Label _time;
        private readonly Label _health;
        private readonly Label _warning;
        private readonly Label _lockState;
        private readonly Label _kill;
        private readonly Label _resultTitle;
        private readonly Label _resultSub;
        private readonly int[] _targetDistances = { -1, -1, -1, -1 };
        private readonly int[] _targetStates = { -1, -1, -1, -1 };
        private int _shownSpeed = int.MinValue;
        private int _shownAltitude = int.MinValue;
        private int _shownThrottle = int.MinValue;
        private int _shownAlpha = int.MinValue;
        private int _shownMissiles = int.MinValue;
        private int _shownEnemies = int.MinValue;
        private int _shownTime = int.MinValue;
        private int _shownHealth = int.MinValue;
        private Warning _shownWarning = (Warning)(-1);
        private int _shownWarningValue = int.MinValue;
        private int _shownLock = int.MinValue;
        private Mission _shownResult;
        private int _shownKill = -1;

        public HalcyonHud(VisualElement root, FlightConfig flight, MissionConfig config)
        {
            _root = root;
            _flight = flight;
            _config = config;
            _flightLayer = root.Q("flight-hud");
            _titleLayer = root.Q("title-screen");
            _pauseLayer = root.Q("pause-screen");
            _resultLayer = root.Q("result-screen");
            _hitFlash = root.Q("hit-flash");
            _boresight = root.Q("boresight");
            _fpm = root.Q("fpm");
            for (var i = 1; i <= MissionConfig.EnemyCount; i++)
            {
                _targets[i] = root.Q("target-" + i);
                _targetLabels[i] = root.Q<Label>("target-label-" + i);
            }

            for (var i = 0; i <= MissionConfig.EnemyCount; i++) { _radarDots[i] = root.Q("radar-dot-" + i); }
            _radarRunway = root.Q("radar-runway");
            _speed = root.Q<Label>("speed");
            _altitude = root.Q<Label>("altitude");
            _throttle = root.Q<Label>("throttle");
            _alpha = root.Q<Label>("alpha");
            _missiles = root.Q<Label>("missiles");
            _enemies = root.Q<Label>("enemies");
            _time = root.Q<Label>("time");
            _health = root.Q<Label>("health");
            _warning = root.Q<Label>("warning");
            _lockState = root.Q<Label>("lock-state");
            _kill = root.Q<Label>("kill");
            _resultTitle = root.Q<Label>("result-title");
            _resultSub = root.Q<Label>("result-sub");
        }

        /// <summary>画面に出している警告（テスト用）。</summary>
        public string WarningText => _warning.text;

        /// <summary>ロックの表示（テスト用）。</summary>
        public string LockText => _lockState.text;

        /// <summary>撃墜の表示（テスト用）。</summary>
        public string KillText => _kill.text;

        public VisualElement TargetBox(int index) => _targets[index];

        public VisualElement Boresight => _boresight;

        public VisualElement FlightPathMarker => _fpm;

        public static string Clock(int ticks)
        {
            var seconds = Mathf.Max(0, ticks) / Units.TickRate;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        public static string Reason(Outcome outcome) => outcome switch
        {
            Outcome.Won => "敵機 3 機を撃墜した",
            Outcome.ShotDown => "撃墜された",
            Outcome.Crashed => "墜落した",
            Outcome.OutOfArea => "作戦区域を離れた",
            Outcome.TimeUp => "時間切れ",
            _ => " ",
        };

        public void Show(SortieFlow flow, Camera camera)
        {
            var screen = flow.Screen;
            Display(_titleLayer, screen == AppScreen.Title);
            Display(_flightLayer, screen != AppScreen.Title);
            Display(_pauseLayer, screen == AppScreen.Paused);
            Display(_resultLayer, screen == AppScreen.Result);
            if (screen == AppScreen.Title) { return; }

            var mission = flow.Mission;
            ShowStats(mission);
            ShowMarkers(mission, camera);
            ShowTargets(mission, camera);
            ShowRadar(mission);
            ShowWarning(mission);
            ShowLock(flow);
            ShowKill(mission);
            var player = mission.Player;
            Display(_hitFlash, player.HitTick >= 0 && mission.Tick - player.HitTick < FlashTicks);
            if (screen == AppScreen.Result) { ShowResult(mission); }
        }

        private void ShowStats(Mission mission)
        {
            var player = mission.Player;
            var view = player.View;
            Set(_speed, ref _shownSpeed, HalcyonSpace.KilometersPerHour(view.Air.Speed), v => $"SPD {v} km/h");
            Set(_altitude, ref _shownAltitude, (int)((view.Position.Y - _flight.GearHeight) / Units.PerMeter), v => $"ALT {v} m");
            Set(_throttle, ref _shownThrottle, view.Throttle / 10, v => $"THR {v}%");
            Set(_alpha, ref _shownAlpha, (int)Mathf.Round(view.Air.Alpha * 360f / Units.FullTurn), v => $"AoA {v}°");
            Set(_missiles, ref _shownMissiles, player.Ammo, v => $"MSL {v}");
            Set(_enemies, ref _shownEnemies, mission.EnemiesLeft, v => $"ENEMY {v}");
            Set(_time, ref _shownTime, (_config.MissionTicks - mission.Tick) / Units.TickRate, v => "TIME " + Clock(v * Units.TickRate));
            if (player.Health != _shownHealth)
            {
                _shownHealth = player.Health;
                _health.text = "HP " + new string('■', Mathf.Max(0, _shownHealth)) + new string('□', Mathf.Max(0, _config.PlayerHealth - _shownHealth));
            }
        }

        /// <summary>機首の 1 km 先と、速度の向きの 1 km 先を画面に置く。</summary>
        private void ShowMarkers(Mission mission, Camera camera)
        {
            var view = mission.Player.View;
            if (mission.Player.Down)
            {
                Display(_boresight, false);
                Display(_fpm, false);
                return;
            }

            var position = HalcyonSpace.Position(view.Position);
            var nose = HalcyonSpace.Rotation(view.Attitude) * Vector3.forward;
            Place(_boresight, camera, position + (nose * 1000f));
            var velocity = HalcyonSpace.Velocity(view.Velocity);
            if (velocity.sqrMagnitude < 1f) { Display(_fpm, false); }
            else { Place(_fpm, camera, position + (velocity.normalized * 1000f)); }
        }

        private void ShowTargets(Mission mission, Camera camera)
        {
            var player = mission.Player;
            var locked = mission.PlayerLocked;
            for (var i = 1; i <= MissionConfig.EnemyCount; i++)
            {
                var box = _targets[i];
                var enemy = mission.Fighters[i];
                if (enemy.Down)
                {
                    Display(box, false);
                    continue;
                }

                if (!Place(box, camera, HalcyonSpace.Position(enemy.View.Position))) { continue; }
                var state = player.LockTarget != i ? 0 : locked ? 2 : 1;
                if (state != _targetStates[i])
                {
                    _targetStates[i] = state;
                    box.EnableInClassList("target-locking", state == 1);
                    box.EnableInClassList("target-locked", state == 2);
                }

                var distance = (int)((enemy.View.Position - player.View.Position).Length / (100 * Units.PerMeter));
                if (distance != _targetDistances[i])
                {
                    _targetDistances[i] = distance;
                    _targetLabels[i].text = $"{distance / 10}.{distance % 10} km";
                }
            }
        }

        /// <summary>機首を上にした、自機中心のレーダー。</summary>
        private void ShowRadar(Mission mission)
        {
            var view = mission.Player.View;
            var nose = HalcyonSpace.Rotation(view.Attitude) * Vector3.forward;
            var heading = Mathf.Atan2(nose.x, nose.z);
            var center = HalcyonSpace.Position(view.Position);
            for (var i = 1; i <= MissionConfig.EnemyCount; i++)
            {
                var enemy = mission.Fighters[i];
                Display(_radarDots[i], !enemy.Down);
                if (!enemy.Down) { RadarPlace(_radarDots[i], HalcyonSpace.Position(enemy.View.Position) - center, heading); }
            }

            var runway = mission.Runway;
            RadarPlace(_radarRunway, HalcyonStage.RunwayCenter(runway, 0f) - center, heading);
            _radarRunway.style.rotate = new Rotate(new Angle(-heading * Mathf.Rad2Deg, AngleUnit.Degree));
        }

        private static void RadarPlace(VisualElement dot, Vector3 offset, float heading)
        {
            var cos = Mathf.Cos(heading);
            var sin = Mathf.Sin(heading);
            var right = (offset.x * cos) - (offset.z * sin);
            var ahead = (offset.x * sin) + (offset.z * cos);
            var radar = new Vector2(right, ahead) / RadarRange;
            if (radar.sqrMagnitude > 1f) { radar = radar.normalized; }
            dot.style.left = 120f + (radar.x * 114f);
            dot.style.top = 120f - (radar.y * 114f);
        }

        private void ShowWarning(Mission mission)
        {
            var player = mission.Player;
            var view = player.View;
            var warning = Warning.None;
            var value = 0;
            if (!view.Grounded && !player.Down && view.Position.Y < PullUpAltitude && view.Velocity.Y < 0) { warning = Warning.PullUp; }
            else if (MissileIncoming(mission)) { warning = Warning.Missile; }
            else if (LockProgress(mission) >= 100) { warning = Warning.Locked; }
            else if (view.Air.Stalled && !player.Down) { warning = Warning.Stall; }
            else if (mission.OutOfArea > 0)
            {
                warning = Warning.Area;
                value = (_config.OutOfAreaTicks - mission.OutOfArea + Units.TickRate - 1) / Units.TickRate;
            }
            else if (LockProgress(mission) > 0)
            {
                warning = Warning.Targeted;
                value = LockProgress(mission);
            }

            if (warning == _shownWarning && value == _shownWarningValue) { return; }
            _shownWarning = warning;
            _shownWarningValue = value;
            _warning.text = warning switch
            {
                Warning.PullUp => "PULL UP",
                Warning.Missile => "MISSILE",
                Warning.Stall => "STALL",
                Warning.Area => $"区域に戻れ {value}",
                Warning.Locked => "ロックされた",
                Warning.Targeted => $"狙われている {value}%",
                _ => " ",
            };
        }

        private static bool MissileIncoming(Mission mission)
        {
            var position = mission.Player.View.Position;
            foreach (var missile in mission.Missiles)
            {
                if (missile.Active && missile.Tracking && missile.Target == 0 && (missile.Position - position).Length < MissileWarningRange) { return true; }
            }

            return false;
        }

        /// <summary>敵のロックの進み（%、最も進んだ敵）。円錐に入っていなければ 0、LOCK で 100。</summary>
        public static int LockProgress(Mission mission)
        {
            var progress = 0;
            for (var i = 1; i < mission.Fighters.Count; i++)
            {
                var enemy = mission.Fighters[i];
                if (enemy.LockTarget != 0 || enemy.LockTicks <= 0) { continue; }
                progress = Mathf.Max(progress, enemy.LockTicks * 100 / mission.LockTicksFor(enemy));
            }

            return progress;
        }

        /// <summary>LOCK・ロック中の進み・直近の発射の拒否。</summary>
        private void ShowLock(SortieFlow flow)
        {
            var mission = flow.Mission;
            var player = mission.Player;
            int code;
            var recent = flow.LastFireTick >= 0 && mission.Tick - flow.LastFireTick < RejectTicks;
            if (recent && flow.LastFire != FireResult.Launched && flow.LastFire != FireResult.None) { code = 1000 + (int)flow.LastFire; }
            else if (mission.PlayerLocked) { code = 2000; }
            else if (player.LockTarget > 0) { code = 3000 + (player.LockTicks * 100 / mission.LockTicksFor(player)); }
            else { code = 0; }

            if (code == _shownLock) { return; }
            _shownLock = code;
            _lockState.EnableInClassList("lock-locking", code >= 3000);
            _lockState.EnableInClassList("lock-locked", code == 2000);
            _lockState.EnableInClassList("lock-rejected", code >= 1000 && code < 2000);
            _lockState.text = code switch
            {
                0 => " ",
                2000 => "LOCK　Space で発射",
                >= 3000 => $"LOCKING {code - 3000}%",
                _ => (FireResult)(code - 1000) switch
                {
                    FireResult.NotLocked => "ロックしていない",
                    FireResult.NoAmmo => "ミサイルがない",
                    FireResult.Reloading => "装填中",
                    FireResult.Grounded => "地上では撃てない",
                    _ => " ",
                },
            };
        }

        /// <summary>直近に敵を落としてから <see cref="KillTicks"/> の間、撃墜の表示を出す。</summary>
        private void ShowKill(Mission mission)
        {
            var latest = -1;
            for (var i = 1; i < mission.Fighters.Count; i++) { latest = Mathf.Max(latest, mission.Fighters[i].DownTick); }
            var count = MissionConfig.EnemyCount - mission.EnemiesLeft;
            var shown = latest >= 0 && mission.Tick - latest < KillTicks ? count : 0;
            if (shown == _shownKill) { return; }
            _shownKill = shown;
            _kill.text = shown > 0 ? $"撃墜　{shown}/{MissionConfig.EnemyCount}" : " ";
        }

        private void ShowResult(Mission mission)
        {
            // 結果は試合ごとに 1 度決まる。勝敗が同じでも別の試合なら書き直す（キャッシュの鍵は試合そのもの）。
            if (ReferenceEquals(mission, _shownResult)) { return; }
            _shownResult = mission;
            _resultTitle.text = mission.Outcome == Outcome.Won ? "MISSION COMPLETE" : "MISSION FAILED";
            var kills = MissionConfig.EnemyCount - mission.EnemiesLeft;
            _resultSub.text = $"{Reason(mission.Outcome)}　撃墜 {kills}/{MissionConfig.EnemyCount}　時間 {Clock(mission.FinishedTick)}　残弾 {mission.Player.Ammo}";
        }

        /// <summary>ワールドの点を HUD の座標に置く。カメラの後ろなら隠して false。</summary>
        private bool Place(VisualElement element, Camera camera, Vector3 world)
        {
            var viewport = camera.WorldToViewportPoint(world);
            var visible = viewport.z > 0f;
            Display(element, visible);
            if (!visible) { return false; }
            var width = _root.resolvedStyle.width;
            var height = _root.resolvedStyle.height;
            if (!float.IsFinite(width) || !float.IsFinite(height)) { return true; }
            element.style.left = viewport.x * width;
            element.style.top = (1f - viewport.y) * height;
            return true;
        }

        private static void Set(Label label, ref int shown, int value, System.Func<int, string> format)
        {
            if (value == shown) { return; }
            shown = value;
            label.text = format(value);
        }

        private static void Display(VisualElement element, bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display) { element.style.display = display; }
        }

        private enum Warning
        {
            None,
            PullUp,
            Missile,
            Stall,
            Area,
            Locked,
            Targeted,
        }
    }
}
