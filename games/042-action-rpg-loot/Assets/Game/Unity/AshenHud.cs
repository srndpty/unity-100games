using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Game.App;
using Game.Core;
using TileMotion;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity
{
    /// <summary>
    /// HUD（UI Toolkit）。ステータス・番人の体力・地面の名前札・タイトルなどのパネル・持ち物の画面。
    /// 文字は Core（<see cref="ItemText"/>）が作ったものを出すだけ。ボタンは意図にして <see cref="_queue"/> へ渡す（入力と同じ列で処理する）。
    /// 書き換えは表示が変わるときだけ行う。
    /// </summary>
    public sealed class AshenHud
    {
        private readonly VisualElement _root;
        private readonly Action<Intent> _queue;
        private readonly List<Button> _tags = new();
        private readonly List<long> _tagIds = new();
        private readonly List<Rect> _placed = new();

        /// <summary>名前札の高さと 1 文字の幅の見積もり（パネルの px）。</summary>
        private const float TagHeight = 26;
        private const float TagCharWidth = 16;
        private readonly Button[] _slots = new Button[SlotInfo.Count];
        private readonly List<Button> _cells = new();
        private object _shownStats;
        private object _shownPanel;
        private object _shownInventory;
        private string _toast = string.Empty;
        private DelveRun _toastRun;
        private long _toastUntilTick;

        public AshenHud(VisualElement root, int inventorySize, int tagCount, Action<Intent> queue)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            var tags = root.Q("tags");
            for (int i = 0; i < tagCount; i++)
            {
                var tag = new Button { focusable = false };
                tag.AddToClassList("tag");
                tag.style.display = DisplayStyle.None;
                int index = i;
                tag.clicked += () => { if (_tagIds[index] > 0) { _queue(Intent.With(IntentKind.PickGround, _tagIds[index], AshenGame.Now)); } };
                tags.Add(tag);
                _tags.Add(tag);
                _tagIds.Add(0);
            }

            var equip = root.Q("equip");
            for (int s = 0; s < SlotInfo.Count; s++)
            {
                var button = new Button { focusable = false };
                button.AddToClassList("slot");
                int slot = s;
                button.clicked += () => Select(new Selection(true, slot));
                equip.Add(button);
                _slots[s] = button;
            }

            var bag = root.Q("bag");
            for (int i = 0; i < inventorySize; i++)
            {
                var button = new Button { focusable = false };
                button.AddToClassList("cell");
                int index = i;
                button.clicked += () => Select(new Selection(false, index));
                bag.Add(button);
                _cells.Add(button);
            }

            root.Q<Button>("act-equip").clicked += () => { if (!Selected.IsSlot && Selected.Index >= 0) { _queue(Intent.With(IntentKind.Equip, Selected.Index, AshenGame.Now)); } };
            root.Q<Button>("act-unequip").clicked += () => { if (Selected.IsSlot) { _queue(Intent.With(IntentKind.Unequip, Selected.Index, AshenGame.Now)); } };
            root.Q<Button>("act-discard").clicked += () => { if (!Selected.IsSlot && Selected.Index >= 0) { _queue(Intent.With(IntentKind.Discard, Selected.Index, AshenGame.Now)); } };
            root.Q<Button>("act-close").clicked += () => _queue(Intent.Of(IntentKind.ToggleInventory, AshenGame.Now));
            root.Query<Button>().ForEach(button => button.focusable = false);
        }

        /// <summary>持ち物の画面で選んでいる品（枠か持ち物の番号。-1 は選んでいない）。</summary>
        public Selection Selected { get; private set; } = Selection.None;

        public VisualElement Root => _root;

        /// <summary>表示中の名前札の数（テスト用）。</summary>
        public int VisibleTags { get; private set; }

        public void Select(Selection selection)
        {
            Selected = selection;
            _shownInventory = null;
        }

        /// <summary>出来事から短い案内を出す（その試合の 120 Tick の間。実時間ではなく Tick で消すので撮影でも同じ絵になる）。</summary>
        public void Toast(string text, DelveRun run)
        {
            _toast = text;
            _toastRun = run;
            _toastUntilTick = run == null ? 0 : run.Tick + ToastTicks;
        }

        public const int ToastTicks = 120;

        /// <summary>今出している案内（なければ空）。</summary>
        public string ToastText(DelveRun run) => run != null && ReferenceEquals(run, _toastRun) && run.Tick < _toastUntilTick ? _toast : string.Empty;

        public void Render(DelveFlow flow, DelveConfig rules, Camera camera, AshenStage stage, bool showTags)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var run = flow.Run;
            var screen = flow.Screen;
            RenderStats(run, rules);
            RenderPanel(screen, run, rules);
            RenderInventory(flow, run, rules);
            RenderTags(run, screen == AppScreen.Playing && showTags, camera, stage);
            var toast = ToastText(run);
            if (toast.Length == 0) { toast = " "; }
            var toastLabel = _root.Q<Label>("toast");
            if (toastLabel.text != toast) { toastLabel.text = toast; }
        }

        private void RenderStats(DelveRun run, DelveConfig rules)
        {
            var hero = run?.Hero;
            Foe boss = null;
            if (run != null)
            {
                foreach (var f in run.Foes)
                {
                    if (f.Kind == FoeKind.Boss && !f.Dead && f.Aggro) { boss = f; }
                }
            }

            int skillSeconds = hero == null ? 0 : (hero.SkillCooldown + Units.TickRate - 1) / Units.TickRate;
            var state = (run != null, hero?.Life ?? 0, run?.Stats.Life ?? 0, hero?.Potions ?? 0, skillSeconds, run?.FloorNumber ?? 0, boss?.Life ?? -1, hero?.Dead ?? false);
            if (Equals(_shownStats, state)) { return; }
            _shownStats = state;
            _root.Q("stats").style.display = run == null ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q("hint").style.display = run == null ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q("boss").style.display = boss == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (run == null) { return; }
            _root.Q<Label>("life").text = hero.Dead ? "倒れた…" : $"HP {hero.Life} / {run.Stats.Life}";
            _root.Q("life-fill").style.width = Length.Percent(100f * hero.Life / Math.Max(1, run.Stats.Life));
            _root.Q<Label>("potions").text = $"回復薬 ×{hero.Potions}";
            _root.Q<Label>("skill").text = skillSeconds == 0 ? "薙ぎ払い：使える" : $"薙ぎ払い：あと {skillSeconds} 秒";
            _root.Q<Label>("floor").text = $"地下 {run.FloorNumber} 階  {run.Map.Name}";
            if (boss != null)
            {
                _root.Q<Label>("boss-name").text = FoeInfo.Name(FoeKind.Boss) + (boss.Enraged ? "（激昂）" : string.Empty);
                _root.Q("boss-fill").style.width = Length.Percent(100f * boss.Life / boss.MaxLife);
            }
        }

        private void RenderPanel(AppScreen screen, DelveRun run, DelveConfig rules)
        {
            var state = (screen, run?.IsOver ?? false);
            if (Equals(_shownPanel, state)) { return; }
            _shownPanel = state;
            bool panel = screen == AppScreen.Title || screen == AppScreen.HowTo || screen == AppScreen.Paused || screen == AppScreen.Result;
            _root.Q("panel").style.display = panel ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q("inventory").style.display = screen == AppScreen.Inventory ? DisplayStyle.Flex : DisplayStyle.None;
            if (!panel) { return; }
            _root.Q<Label>("headline").text = Headline(screen);
            _root.Q<Label>("message").text = Message(screen, run, rules);
            _root.Q<Button>("submit").text = screen switch
            {
                AppScreen.Title => "はじめる  /  Enter",
                AppScreen.HowTo => "降りる  /  Enter",
                AppScreen.Paused => "つづける  /  Enter",
                _ => "タイトルへ  /  Enter",
            };
            Show("submit", true);
            Show("back", screen == AppScreen.HowTo || screen == AppScreen.Paused);
            Show("title", screen == AppScreen.Paused);
            Show("quit", screen == AppScreen.Title);
        }

        private void RenderInventory(DelveFlow flow, DelveRun run, DelveConfig rules)
        {
            if (flow.Screen != AppScreen.Inventory || run == null)
            {
                _shownInventory = null;
                return;
            }

            // 持ち物が減って選択が範囲外になったら外す。
            if (!Selected.IsSlot && Selected.Index >= run.Inventory.Count) { Selected = Selection.None; }
            int signature = run.Inventory.Count;
            foreach (var item in run.Inventory) { signature = unchecked((signature * 31) + RuntimeHelpers.GetHashCode(item)); }
            for (int s = 0; s < SlotInfo.Count; s++)
            {
                var item = run.EquippedAt((Slot)s);
                signature = unchecked((signature * 31) + (item == null ? 0 : RuntimeHelpers.GetHashCode(item)));
            }

            var state = (run, signature, Selected, flow.LastItemResult);
            if (Equals(_shownInventory, state)) { return; }
            _shownInventory = state;

            for (int s = 0; s < SlotInfo.Count; s++)
            {
                var item = run.EquippedAt((Slot)s);
                var button = _slots[s];
                button.text = SlotInfo.Name((Slot)s) + "：" + (item == null ? "なし" : item.Name);
                button.style.color = item == null ? new Color(.6f, .55f, .5f) : AshenStage.RarityColor(item.Rarity);
                button.EnableInClassList("selected", Selected.IsSlot && Selected.Index == s);
            }

            for (int i = 0; i < _cells.Count; i++)
            {
                var button = _cells[i];
                bool has = i < run.Inventory.Count;
                button.text = has ? run.Inventory[i].Name : "—";
                button.style.color = has ? AshenStage.RarityColor(run.Inventory[i].Rarity) : new Color(.5f, .45f, .4f);
                button.SetEnabled(has);
                button.EnableInClassList("selected", has && !Selected.IsSlot && Selected.Index == i);
            }

            _root.Q<Label>("bag-title").text = $"持ち物  {run.Inventory.Count} / {rules.InventorySize}";
            _root.Q<Label>("sheet").text = string.Join("\n", ItemText.Stats(run.Stats));

            Item chosen = Selected.Index < 0 ? null : Selected.IsSlot ? run.EquippedAt((Slot)Selected.Index) : run.Inventory[Selected.Index];
            var name = _root.Q<Label>("detail-name");
            if (chosen == null)
            {
                name.text = "品を選ぶと説明が出ます";
                name.style.color = new Color(.85f, .8f, .72f);
                _root.Q<Label>("detail").text = " ";
                _root.Q<Label>("compare").text = " ";
            }
            else
            {
                var lines = ItemText.Describe(chosen);
                name.text = lines[0];
                name.style.color = AshenStage.RarityColor(chosen.Rarity);
                _root.Q<Label>("detail").text = string.Join("\n", lines.GetRange(1, lines.Count - 1));
                _root.Q<Label>("compare").text = Selected.IsSlot ? "装備中" : CompareText(run.Compare(chosen));
            }

            _root.Q<Button>("act-equip").SetEnabled(chosen != null && !Selected.IsSlot);
            _root.Q<Button>("act-discard").SetEnabled(chosen != null && !Selected.IsSlot);
            _root.Q<Button>("act-unequip").SetEnabled(chosen != null && Selected.IsSlot);
            _root.Q<Label>("inv-note").text = flow.LastItemResult switch
            {
                ItemResult.InventoryFull => "持ち物がいっぱいです。",
                ItemResult.GroundFull => "足元に置けません（地面がいっぱい）。",
                _ => " ",
            };
        }

        /// <summary>比較の文：装備すると変わる能力値の一覧。</summary>
        public static string CompareText(List<StatDiff> diffs)
        {
            if (diffs == null) { throw new ArgumentNullException(nameof(diffs)); }
            if (diffs.Count == 0) { return "装備しても能力値は変わらない"; }
            var sb = new StringBuilder("装備すると：");
            foreach (var d in diffs) { sb.Append('\n').Append(ItemText.Diff(d)); }
            return sb.ToString();
        }

        private void RenderTags(DelveRun run, bool show, Camera camera, AshenStage stage)
        {
            int used = 0;
            if (show && run != null && _root.panel != null)
            {
                foreach (var g in run.Ground)
                {
                    if (used >= _tags.Count) { break; }
                    var world = stage.transform.TransformPoint(AshenStage.Local(g.Position) + new Vector3(0, .45f, 0));
                    var viewport = camera.WorldToViewportPoint(world);
                    if (viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) { continue; }
                    var point = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, world, camera);
                    var text = g.IsPotion ? "回復薬" : g.Item.Name;

                    // 近くの品の札が重ならないよう、先に置いた札と重なる間は上へずらす（幅は文字数からの見積もり）。
                    float width = (text.Length * TagCharWidth) + 16;
                    var rect = new Rect(point.x - (width / 2), point.y - TagHeight, width, TagHeight);
                    for (int k = 0; k < used; k++)
                    {
                        if (_placed[k].Overlaps(rect))
                        {
                            rect.y = _placed[k].y - TagHeight - 2;
                            k = -1;
                        }
                    }

                    if (_placed.Count <= used) { _placed.Add(rect); } else { _placed[used] = rect; }
                    point.y = rect.y + TagHeight;
                    var tag = _tags[used];
                    _tagIds[used] = g.Id;
                    if (tag.text != text) { tag.text = text; }
                    tag.style.color = g.IsPotion ? new Color(1f, .6f, .6f) : AshenStage.RarityColor(g.Item.Rarity);
                    tag.style.left = point.x;
                    tag.style.top = point.y;
                    tag.style.display = DisplayStyle.Flex;
                    used++;
                }
            }

            for (int i = used; i < _tags.Count; i++)
            {
                if (_tagIds[i] == 0 && _tags[i].style.display == DisplayStyle.None) { continue; }
                _tagIds[i] = 0;
                _tags[i].style.display = DisplayStyle.None;
            }

            VisibleTags = used;
        }

        public static string Headline(AppScreen screen) => screen switch
        {
            AppScreen.Title => "ASHEN RELIQUARY",
            AppScreen.HowTo => "あそびかた",
            AppScreen.Paused => "一時停止",
            AppScreen.Result => "番人を倒した！",
            _ => string.Empty,
        };

        public static string Message(AppScreen screen, DelveRun run, DelveConfig rules)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            switch (screen)
            {
                case AppScreen.Title:
                    return "灰に沈んだ聖堂の地下 3 階を降り、最奥の灰の番人を倒せ。\n敵が落とす装備には、出るたびに違う能力の修飾（Affix）が付く。";
                case AppScreen.HowTo:
                    return "W A S D：動く　　マウス：狙う\n" +
                        "左クリック：攻撃（押したままで続ける）\n" +
                        $"右クリック：薙ぎ払い（周りの敵すべて、{rules.SkillCooldown / Units.TickRate} 秒に 1 回）\n" +
                        "Q：回復薬　　E：近くの装備を拾う（名前札のクリックでも拾える）\n" +
                        "I：持ち物と装備（開いている間は止まる）　　Esc：一時停止\n\n" +
                        "名前の色は希少度（白：普通　青：魔法　金：希少）。\n" +
                        "持ち物で選ぶと、効果と装備したときの能力値の変化が見られる。\n" +
                        "倒れてもその階の入口で復活する（持ち物は失わず、生きている敵は元に戻る）。";
                case AppScreen.Paused:
                    return "Enter：つづける　　T：タイトルへ";
                case AppScreen.Result when run != null:
                    var r = run.Record;
                    return $"かかった時間  {FormatTicks(run.Tick)}\n倒した敵  {r.Kills}　　倒れた回数  {r.Deaths}\n" +
                        $"拾った装備  {r.ItemsPicked}　　最も希少な品  {RarityInfo.Name(r.BestRarity)}　　使った回復薬  {r.PotionsUsed}";
                default:
                    return string.Empty;
            }
        }

        public static string FormatTicks(long ticks)
        {
            long seconds = ticks / Units.TickRate;
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        private void Show(string name, bool visible) =>
            _root.Q<Button>(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>持ち物の画面の選択。</summary>
        public readonly struct Selection : IEquatable<Selection>
        {
            public Selection(bool isSlot, int index)
            {
                IsSlot = isSlot;
                Index = index;
            }

            public static Selection None => new(false, -1);

            public bool IsSlot { get; }

            public int Index { get; }

            public bool Equals(Selection other) => IsSlot == other.IsSlot && Index == other.Index;

            public override bool Equals(object obj) => obj is Selection other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(IsSlot, Index);
        }
    }
}
