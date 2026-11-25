using Game.App;
using Game.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// Hud.uxml の要素を画面ごとに出し分け、その Tick の状態を書く。文字は値が変わったときだけ作り直す。
    /// タイトル、レース中（順位・周回・順位表・ミニマップ・速さ・ドリフト・カウントダウン）、一時停止、結果。
    /// </summary>
    public sealed class ThistleHud
    {
        private const int GoTicks = 45;

        private readonly KartConfig _config;
        private readonly VisualElement _raceLayer;
        private readonly VisualElement _titleLayer;
        private readonly VisualElement _pauseLayer;
        private readonly VisualElement _resultLayer;
        private readonly Label _place;
        private readonly Label _placeSuffix;
        private readonly Label _lap;
        private readonly Label _speed;
        private readonly Label _charge;
        private readonly Label _countdown;
        private readonly Label _resultTitle;
        private readonly Label _resultSub;
        private readonly Row[] _order = new Row[KartConfig.KartCount];
        private readonly Row[] _results = new Row[KartConfig.KartCount];
        private int _shownPlace = -1;
        private int _shownLap = -1;
        private int _shownSpeed = -1;
        private int _shownCharge = -1;
        private int _shownCount = -2;
        private int _shownOrder = -1;
        private int _shownResult = -1;

        public ThistleHud(VisualElement root, Track track, KartConfig config)
        {
            _config = config;
            _raceLayer = root.Q("race-hud");
            _titleLayer = root.Q("title-screen");
            _pauseLayer = root.Q("pause-screen");
            _resultLayer = root.Q("result-screen");
            _place = root.Q<Label>("place");
            _placeSuffix = root.Q<Label>("place-suffix");
            _lap = root.Q<Label>("lap");
            _speed = root.Q<Label>("speed");
            _charge = root.Q<Label>("charge");
            _countdown = root.Q<Label>("countdown");
            _resultTitle = root.Q<Label>("result-title");
            _resultSub = root.Q<Label>("result-sub");
            var order = root.Q("order");
            var results = root.Q("result-rows");
            for (var i = 0; i < KartConfig.KartCount; i++)
            {
                _order[i] = new Row(order, "order", withTime: false);
                _results[i] = new Row(results, "result", withTime: true);
            }

            Minimap = new ThistleMinimap(track);
            root.Q("minimap-card").Add(Minimap);
        }

        public ThistleMinimap Minimap { get; }

        public static string Ordinal(int place) => place switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };

        /// <summary>完走の記録（Tick）を「分:秒.百分の一」にする。</summary>
        public static string Time(int ticks)
        {
            var hundredths = ticks * 100 / RaceFlow.TickRate;
            return $"{hundredths / 6000}:{hundredths / 100 % 60:00}.{hundredths % 100:00}";
        }

        public static string Name(int index) => index == 0 ? "YOU" : "CPU " + index;

        public void Show(RaceFlow flow)
        {
            var screen = flow.Screen;
            Display(_titleLayer, screen == AppScreen.Title);
            Display(_raceLayer, screen != AppScreen.Title);
            Display(_pauseLayer, screen == AppScreen.Paused);
            Display(_resultLayer, screen == AppScreen.Result);
            var race = flow.Race;
            if (screen != AppScreen.Title) { ShowRace(race, screen); }
            if (screen == AppScreen.Result) { ShowResult(race, flow.Won); }
            else { _shownResult = -1; }
        }

        private static void Display(VisualElement element, bool visible)
        {
            var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display != display) { element.style.display = display; }
        }

        private void ShowRace(Race race, AppScreen screen)
        {
            var player = race.KartAt(0);
            var place = race.PlaceOf(0);
            if (place != _shownPlace)
            {
                _shownPlace = place;
                _place.text = place.ToString();
                _placeSuffix.text = Ordinal(place);
            }

            var lap = Mathf.Min(player.Laps + 1, KartConfig.LapsToFinish);
            if (player.Finished) { lap = KartConfig.LapsToFinish; }
            if (lap != _shownLap)
            {
                _shownLap = lap;
                _lap.text = $"LAP {lap}/{KartConfig.LapsToFinish}";
            }

            var speed = Mathf.Abs(player.Speed) * RaceFlow.TickRate * 36 / 10000;
            if (speed != _shownSpeed)
            {
                _shownSpeed = speed;
                _speed.text = speed.ToString();
            }

            ShowCharge(player, screen);
            ShowCountdown(race);
            ShowOrder(race);
            Minimap.Show(race);
        }

        private void ShowCharge(Kart player, AppScreen screen)
        {
            var charge = 0;
            if (screen == AppScreen.Playing)
            {
                if (player.BoostLeft > 0) { charge = 3; }
                else if (player.DriftTicks >= _config.MiniTurboTicks) { charge = 2; }
                else if (player.DriftTicks > 0) { charge = 1; }
            }

            if (charge == _shownCharge) { return; }
            _shownCharge = charge;
            _charge.RemoveFromClassList("charge-drift");
            _charge.RemoveFromClassList("charge-ready");
            _charge.RemoveFromClassList("charge-boost");
            switch (charge)
            {
                case 1:
                    _charge.text = "DRIFT";
                    _charge.AddToClassList("charge-drift");
                    break;
                case 2:
                    _charge.text = "MINI TURBO";
                    _charge.AddToClassList("charge-ready");
                    break;
                case 3:
                    _charge.text = "BOOST";
                    _charge.AddToClassList("charge-boost");
                    break;
                default:
                    _charge.text = string.Empty;
                    break;
            }
        }

        /// <summary>スタート待ちは 3・2・1、動き出してから <see cref="GoTicks"/> の間は GO。</summary>
        private void ShowCountdown(Race race)
        {
            var delay = _config.StartDelayTicks;
            var count = -1;
            if (delay > 0 && race.Tick < delay) { count = 3 - (race.Tick * 3 / delay); }
            else if (delay > 0 && race.Tick <= delay + GoTicks && race.Phase != RacePhase.Over) { count = 0; }
            if (count == _shownCount) { return; }
            _shownCount = count;
            _countdown.text = count < 0 ? string.Empty : count == 0 ? "GO!" : count.ToString();
            _countdown.EnableInClassList("countdown-go", count == 0);
        }

        private void ShowOrder(Race race)
        {
            var key = 0;
            for (var i = 0; i < KartConfig.KartCount; i++) { key = (key * 10) + race.PlaceOf(i); }
            if (key == _shownOrder) { return; }
            _shownOrder = key;
            for (var i = 0; i < KartConfig.KartCount; i++)
            {
                var place = race.PlaceOf(i);
                _order[place - 1].Set(place, i, null);
            }
        }

        private void ShowResult(Race race, bool won)
        {
            var key = race.Tick;
            if (key == _shownResult) { return; }
            _shownResult = key;
            _resultTitle.text = won ? "勝利" : "敗北";
            _resultTitle.EnableInClassList("result-win", won);
            _resultTitle.EnableInClassList("result-lose", !won);
            var player = race.KartAt(0);
            _resultSub.text = player.Finished
                ? $"{race.PlaceOf(0)} 位でゴール   タイム {Time(player.FinishTick)}"
                : $"時間切れ   {race.PlaceOf(0)} 位";
            for (var i = 0; i < KartConfig.KartCount; i++)
            {
                var place = race.PlaceOf(i);
                var kart = race.KartAt(i);
                var time = kart.Finished ? Time(kart.FinishTick) : $"走行中  {Mathf.Min(kart.Laps + 1, KartConfig.LapsToFinish)} 周目";
                _results[place - 1].Set(place, i, time);
            }
        }

        /// <summary>順位表・結果の 1 行（順位、色の丸、名前、記録）。</summary>
        private sealed class Row
        {
            private readonly VisualElement _row;
            private readonly Label _place;
            private readonly VisualElement _chip;
            private readonly Label _name;
            private readonly Label _time;
            private readonly string _prefix;

            public Row(VisualElement parent, string prefix, bool withTime)
            {
                _prefix = prefix;
                _row = new VisualElement { pickingMode = PickingMode.Ignore };
                _row.AddToClassList(prefix + "-row");
                _place = new Label();
                _place.AddToClassList(prefix + "-place");
                _chip = new VisualElement { pickingMode = PickingMode.Ignore };
                _chip.AddToClassList(prefix + "-chip");
                _name = new Label();
                _name.AddToClassList(prefix + "-name");
                _row.Add(_place);
                _row.Add(_chip);
                _row.Add(_name);
                if (withTime)
                {
                    _time = new Label();
                    _time.AddToClassList(prefix + "-time");
                    _row.Add(_time);
                }

                parent.Add(_row);
            }

            public void Set(int place, int kart, string time)
            {
                _place.text = place.ToString();
                _chip.style.backgroundColor = ThistlePalette.Kart(kart);
                _name.text = Name(kart);
                _name.EnableInClassList("order-you", kart == 0);
                _row.EnableInClassList(_prefix + "-row-you", kart == 0);
                if (_time != null) { _time.text = time; }
            }
        }
    }
}
