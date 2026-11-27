using System;
using System.Globalization;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class MuseumCapture : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = FindAnyObjectByType<DeductionGame>();
            if (game == null) { throw new InvalidOperationException("調査画面がありません。"); }
            var flow = game.Flow;
            await capture.WaitUntilAsync(() => game.Root != null, 10f);
            flow.Focus(true);
            flow.PointerIdle();

            var tick = 0;
            void Act(Operation operation, int id = -1)
            {
                flow.Enqueue(operation, id);
                game.Flush();
                tick++;
            }

            async Awaitable Shot(string file, string note) => await capture.ShootAsync(file, $"{note} (tick={tick})");

            Act(Operation.Title);
            await Shot("01_title", "タイトル");
            Act(Operation.Help);
            await Shot("00_help", "遊び方");
            Act(Operation.Start);
            var attempts = flow.Case.Attempts;
            capture.Record("attempts", attempts.ToString(CultureInfo.InvariantCulture));
            Act(Operation.Question);
            await Shot("02_question", "警備員に尋問");
            Act(Operation.Present);
            if (attempts > 1) { await Shot("03_incorrect", "展示目録を誤提示"); }
            else { capture.Record("incorrectShot", "残り1回では誤提示で即座に敗北するため省略"); }
            for (var i = 1; i < attempts; i++) { Act(Operation.Present); }
            if (flow.Case.Outcome != Outcome.Lost) { throw new InvalidOperationException("敗北に到達できません。"); }
            await Shot("04_lost", "敗北");

            Act(Operation.Start);
            Act(Operation.Question);
            Act(Operation.SelectEvidence, 2);
            Act(Operation.Present);
            await Shot("05_correct", "入退室ログで証言を覆す");
            Act(Operation.SelectTestimony, 1);
            Act(Operation.Question);
            Act(Operation.SelectTestimony, 3);
            Act(Operation.Question);
            Act(Operation.SelectTestimony, 2);
            Act(Operation.Question);
            Act(Operation.SelectEvidence, 6);
            await Shot("06_seal", "封印の検印と複製品の刻印");
            Act(Operation.Present);
            Act(Operation.SelectTestimony, 4);
            Act(Operation.Question);
            Act(Operation.SelectEvidence, 7);
            await Shot("08_records", "取得した8点の証拠");
            Act(Operation.Present);
            if (flow.Case.Outcome != Outcome.Won) { throw new InvalidOperationException("勝利に到達できません。"); }
            await Shot("07_won", "真相");
        }
    }
}
