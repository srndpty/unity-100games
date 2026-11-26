#nullable enable
using System;
using System.Globalization;
using System.Linq;
using Game.App;
using Game.Core;
using Game.Core.Analysis;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <c>dev capture 047</c> の撮影台本。乱数を使わないので、解答手順（Data/Walkthrough.txt）と Tick だけで進めて毎回同じ画像になる。
    /// 動詞・持ち物の選択、命令文、拾う、組み合わせ、会話、扉が開く、灯をともす、結果を撮る。
    /// </summary>
    public sealed class AdventureCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public const int MaxTicks = 20000;

        [SerializeField] private TextAsset? _walkthrough;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            if (capture == null) { throw new ArgumentNullException(nameof(capture)); }
            var game = FindAnyObjectByType<AdventureGame>();
            if (game == null) { throw new InvalidOperationException("AdventureGame がありません。"); }
            if (_walkthrough == null) { throw new InvalidOperationException("解答手順が設定されていません。"); }
            await capture.WaitUntilAsync(() => game.Hud != null, 10f);
            game.Reinitialize();
            var flow = game.Flow;
            var book = game.Book;
            var moves = Adventure.ParseWalkthrough(book, _walkthrough.text).ToArray();
            capture.Record("fingerprint", book.Fingerprint.ToString("X8", CultureInfo.InvariantCulture));
            capture.Record("walkSpeed", game.Settings.WalkSpeed.ToString(CultureInfo.InvariantCulture));
            var next = 0;

            async Awaitable Shoot(string name, string note)
            {
                game.Render();
                await capture.ShootAsync(name, $"{note} (tick={flow.WorldTick}, hash=0x{flow.WorldHash:X8}, solved={flow.SolvedCount})");
            }

            void Key(IntentKind kind, int index = -1)
            {
                flow.Press(kind, index);
                flow.StepTicks(1);
            }

            // 台詞は送り、歩き終えて次の手を受け付けるまで進める。
            void WaitIdle()
            {
                for (int i = 0; i < MaxTicks; i++)
                {
                    if (flow.Screen != FlowScreen.Playing) { return; }
                    if (flow.CurrentLine != null)
                    {
                        flow.Press(IntentKind.Confirm);
                        flow.FlushIntents();
                        continue;
                    }

                    if (flow.IsIdle) { return; }
                    flow.StepTicks(1);
                }

                throw new InvalidOperationException("手を受け付ける状態になりません");
            }

            void Play(int count)
            {
                while (next < count)
                {
                    WaitIdle();
                    if (!flow.TryIssue(moves[next])) { throw new InvalidOperationException($"手 {next + 1}（{moves[next].Describe(book)}）を受け付けません"); }
                    next++;
                }

                WaitIdle();
            }

            // 次の手を命じ、その結果の台詞が出るまで進める。
            void Issue()
            {
                WaitIdle();
                if (!flow.TryIssue(moves[next])) { throw new InvalidOperationException($"手 {next + 1}（{moves[next].Describe(book)}）を受け付けません"); }
                next++;
                for (int i = 0; i < MaxTicks && flow.CurrentLine == null; i++) { flow.StepTicks(1); }
                if (flow.CurrentLine == null) { throw new InvalidOperationException("台詞が出ません"); }
            }

            await Shoot("01_title", "タイトル");
            Key(IntentKind.Down);
            Key(IntentKind.Confirm);
            await Shoot("02_howto", "操作説明");
            Key(IntentKind.Confirm);
            Key(IntentKind.Confirm);

            Key(IntentKind.VerbKey, 1);
            flow.SetHover(book.HotspotIndex("cottage.knife"), -1);
            await Shoot("03_cottage_verb", "灯台守小屋：動詞「取る」を選び、包丁の上（命令文）");
            Key(IntentKind.Deselect);
            flow.SetHover(-1, -1);

            Play(1);
            Issue();
            await Shoot("04_take_knife", "包丁を拾った（台詞と持ち物の枠）");

            Play(5);
            flow.Click(ClickTarget.Item, flow.Inventory.ToList().IndexOf(book.ItemIndex("cup")));
            flow.StepTicks(1);
            flow.SetHover(book.HotspotIndex("cottage.stove"), -1);
            await Shoot("05_use_sentence", "持ち物のカップを選び、ストーブの上（使う カップ → ストーブ）");
            Key(IntentKind.Deselect);
            flow.SetHover(-1, -1);

            Play(6);
            Issue();
            await Shoot("06_combine", "茶葉とお湯のカップを組み合わせた");

            Play(8);
            Issue();
            await Shoot("07_dock_net", "船着き場：漁網を見る");

            Play(13);
            Issue();
            WaitIdle();
            await Shoot("08_topics", "船頭との会話の話題");

            Issue();
            Key(IntentKind.Confirm);
            await Shoot("09_captain_line", "船頭の返事");

            Play(19);
            Issue();
            await Shoot("10_door_open", "錆びた鍵で灯台への扉が開いた");

            Play(21);
            await Shoot("11_lamp_room", "灯室");

            Play(22);
            Issue();
            if (!flow.Escaped) { throw new InvalidOperationException("灯がともりません"); }
            await Shoot("12_lamp_lit", "灯がともった（脱出）");

            WaitIdle();
            if (flow.Screen != FlowScreen.Result) { throw new InvalidOperationException($"結果画面になりません（{flow.Screen}）"); }
            capture.Record("finalHash", flow.WorldHash.ToString("X8", CultureInfo.InvariantCulture));
            await Shoot("13_result", "結果（謎 5/5・行動の数・時間）");
            Key(IntentKind.Confirm);
            if (flow.Screen != FlowScreen.Title) { throw new InvalidOperationException("タイトルに戻りません"); }
        }
    }
}
