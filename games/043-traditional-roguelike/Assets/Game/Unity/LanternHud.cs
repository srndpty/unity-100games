using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// HUD（UI Toolkit）。上の帯（階・レベル・HP・満腹度・行動数）、下のメッセージ欄、タイトルなどのパネル、持ち物の画面。
    /// 数値は <see cref="PlayerView"/>、文は <see cref="MessageLog"/> から出すだけ。書き換えは表示が変わるときだけ行う。
    /// ボタンは意図にして <see cref="_queue"/> へ渡す（キーと同じ列で処理する）。
    /// </summary>
    public sealed class LanternHud
    {
        public const int LogLines = 6;

        private readonly VisualElement _root;
        private readonly List<Label> _lines = new();
        private readonly List<Label> _cells = new();
        private (int RunNumber, int Version, AppScreen Screen) _shownStats = (-1, -1, AppScreen.Title);
        private (int Messages, AppScreen Screen) _shownLog = (-1, AppScreen.Title);
        private (AppScreen Screen, int RunNumber) _shownPanel = ((AppScreen)(-1), -1);
        private (int Version, int Selected, AppScreen Screen, int RunNumber) _shownInventory = (-1, -1, AppScreen.Title, -1);

        public LanternHud(VisualElement root, int inventoryCapacity, Action<Intent> queue)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            if (queue == null) { throw new ArgumentNullException(nameof(queue)); }
            var lines = root.Q("lines");
            for (int i = 0; i < LogLines; i++)
            {
                var label = new Label(" ") { pickingMode = PickingMode.Ignore };
                label.AddToClassList("line");
                lines.Add(label);
                _lines.Add(label);
            }

            var bag = root.Q("bag");
            for (int i = 0; i < inventoryCapacity; i++)
            {
                var cell = new Label(" ");
                cell.AddToClassList("cell");
                bag.Add(cell);
                _cells.Add(cell);
            }

            root.Query<Button>().ForEach(button => button.focusable = false);
        }

        public VisualElement Root => _root;

        public static string DirectionName(Direction d) => d switch
        {
            Direction.North => "北（上）",
            Direction.NorthEast => "北東（右上）",
            Direction.East => "東（右）",
            Direction.SouthEast => "南東（右下）",
            Direction.South => "南（下）",
            Direction.SouthWest => "南西（左下）",
            Direction.West => "西（左）",
            _ => "北西（左上）",
        };

        public static string HowToText(CrawlConfig c) =>
            "移動：テンキー 1〜9（8 方向）、または矢印 / WASD。Shift を押しながら 2 キーで斜め。Ctrl＋方向で向きだけ変える。\n" +
            "敵のいる方へ動くと攻撃。Space / テンキー 5 で足踏み。品の上に乗ると拾う（G でも拾う）。階段の上で . か Enter で降りる。\n" +
            "I で持ち物：使う・投げる（向いている方へ）・置く。Esc で一時停止。\n" +
            "あなたが 1 回動くと、敵もそれぞれの速さで動く（速い敵は 2 回）。壁に向かうなど無効な操作では誰も動かない。\n" +
            $"見えているのは周り {c.ViewRadius} マスまで。一度見た地形は暗く残り、品は最後に見たものを覚えている。敵は見えている間だけ映る。\n" +
            $"満腹度は {c.HungerEvery} 行動で 1 減り、0 になると HP が減っていく。パンを食べて補う。5 階の階段を降りれば生還。倒れたら最初から。";

        public void Render(CrawlFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            RenderStats(flow);
            RenderLog(flow);
            RenderPanel(flow);
            RenderInventory(flow);
        }

        private void RenderStats(CrawlFlow flow)
        {
            var view = flow.View;
            var showStats = view != null && flow.Screen != AppScreen.Title && flow.Screen != AppScreen.HowTo;
            var key = (flow.RunNumber, view?.Version ?? -1, showStats ? AppScreen.Playing : AppScreen.Title);
            if (key == _shownStats) { return; }
            _shownStats = key;
            Show("stats", showStats);
            if (!showStats) { return; }
            _root.Q<Label>("floor").text = $"{view!.Floor} 階";
            _root.Q<Label>("level").text = $"Lv {view.Level}";
            _root.Q<Label>("hp").text = $"HP {Math.Max(0, view.Hp)} / {view.MaxHp}";
            _root.Q("hp-fill").style.width = new Length(100f * Math.Max(0, view.Hp) / view.MaxHp, LengthUnit.Percent);
            _root.Q<Label>("food").text = $"満腹 {view.Satiety} / {view.MaxSatiety}";
            _root.Q("food-fill").style.width = new Length(100f * view.Satiety / view.MaxSatiety, LengthUnit.Percent);
            _root.Q<Label>("turns").text = $"行動 {view.Turns}　攻 {view.Attack}　防 {view.Defense}";
        }

        private void RenderLog(CrawlFlow flow)
        {
            var show = flow.View != null && flow.Screen != AppScreen.Title && flow.Screen != AppScreen.HowTo;
            var key = (flow.Messages.Version, show ? AppScreen.Playing : AppScreen.Title);
            if (key == _shownLog) { return; }
            _shownLog = key;
            Show("log", show);
            if (!show) { return; }
            var lines = flow.Messages.Lines;
            var start = Math.Max(0, lines.Count - LogLines);
            for (int i = 0; i < LogLines; i++)
            {
                var index = start + i;
                var label = _lines[i];
                label.text = index < lines.Count ? lines[index] : " ";
                label.EnableInClassList("line-old", index < lines.Count - 2);
            }
        }

        private void RenderPanel(CrawlFlow flow)
        {
            var key = (flow.Screen, flow.RunNumber);
            if (key == _shownPanel) { return; }
            _shownPanel = key;
            var screen = flow.Screen;
            var show = screen == AppScreen.Title || screen == AppScreen.HowTo || screen == AppScreen.Paused || screen == AppScreen.Result;
            Show("panel", show);
            if (!show) { return; }
            var headline = _root.Q<Label>("headline");
            var message = _root.Q<Label>("message");
            var submit = _root.Q<Button>("submit");
            switch (screen)
            {
                case AppScreen.Title:
                    headline.text = "MIRE LANTERN";
                    message.text = "灯りを提げて、湿った地下の 5 つの階を降りていく。\n地形は降りるたびに作り直され、倒れたら最初から。5 階の階段の先に地上の光がある。";
                    submit.text = "はじめる / Enter";
                    break;
                case AppScreen.HowTo:
                    headline.text = "遊び方";
                    message.text = HowToText(flow.Config);
                    submit.text = "出発する / Enter";
                    break;
                case AppScreen.Paused:
                    headline.text = "一時停止";
                    message.text = "ターン制なので、止めている間も考えている間も敵は動かない。";
                    submit.text = "再開 / Enter";
                    break;
                default:
                    var view = flow.View!;
                    headline.text = view.State == RunState.Won ? "生還！" : "力尽きた……";
                    var cause = view.State == RunState.Won
                        ? "5 階の階段を降り、地上へ出た。"
                        : view.DeathCause == DeathCause.Starvation ? $"{view.Floor} 階で空腹のため倒れた。" : $"{view.Floor} 階で{Names.Of(view.Killer)}に倒された。";
                    message.text = $"{cause}\nレベル {view.Level}　行動 {view.Turns}　倒した敵 {view.Kills}　シード {flow.Seed}";
                    submit.text = "タイトルへ / Enter";
                    break;
            }

            Show("back", screen == AppScreen.HowTo);
            Show("title", screen == AppScreen.Paused);
            Show("quit", screen == AppScreen.Title);
        }

        private void RenderInventory(CrawlFlow flow)
        {
            var view = flow.View;
            var show = flow.Screen == AppScreen.Inventory && view != null;
            var key = (view?.Version ?? -1, flow.Selected, show ? AppScreen.Inventory : AppScreen.Title, flow.RunNumber);
            if (key == _shownInventory) { return; }
            _shownInventory = key;
            Show("inventory", show);
            if (!show) { return; }
            var items = view!.Inventory;
            _root.Q<Label>("bag-title").text = $"持ち物 {items.Count} / {view.InventoryCapacity}";
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                var has = i < items.Count;
                cell.text = has ? $"{(char)('a' + i)}  {Names.Of(items[i])}" : "―";
                cell.EnableInClassList("empty", !has);
                cell.EnableInClassList("selected", has && i == flow.Selected);
            }

            var selected = flow.Selected < items.Count ? items[flow.Selected] : (ItemKind?)null;
            _root.Q<Label>("detail-name").text = selected.HasValue ? Names.Of(selected.Value) : "持ち物は空です";
            _root.Q<Label>("detail").text = selected.HasValue ? Names.Describe(selected.Value, flow.Config) : " ";
            _root.Q<Label>("facing").text = $"今の向き：{DirectionName(view.Facing)}";
        }

        private void Show(string name, bool show) => _root.Q(name).style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
