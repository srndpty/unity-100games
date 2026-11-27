#nullable enable
using System;
using System.Globalization;
using Game.App;
using Game.Core.Mechanisms;
using Game.Core.Play;
using Game.Core.Rooms;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <c>dev capture 048</c> の撮影台本。撮影のシード（<see cref="EvidenceCapture.Seed"/>）で答えを作り、操縦役（ヒントと同じ次の一手）と
    /// 遊ぶときと同じ入力で Tick ずつ進める。同じビルドなら毎回同じ画像になる。
    /// 壁・近景・手に取った手がかり・開いた仕掛け・望遠鏡の星・ヒント・脱出・結果を撮る。
    /// </summary>
    public sealed class EscapeCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public const int MaxTicks = 20000;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            if (capture == null) { throw new ArgumentNullException(nameof(capture)); }
            var game = FindAnyObjectByType<EscapeGame>();
            if (game == null) { throw new InvalidOperationException("EscapeGame がありません。"); }
            await capture.WaitUntilAsync(() => game.Hud != null, 10f);
            game.Reinitialize(EvidenceCapture.Seed);
            var flow = game.Flow;
            var def = game.Def;
            capture.Record("fingerprint", def.Fingerprint.ToString("X8", CultureInfo.InvariantCulture));
            capture.Record("transitionTicks", game.Settings.TransitionTicks.ToString(CultureInfo.InvariantCulture));

            int Part(string id)
            {
                foreach (var p in def.Parts)
                {
                    if (p.Id == id) { return p.Index; }
                }

                throw new InvalidOperationException($"部品 {id} がありません");
            }

            int View(string id)
            {
                foreach (var v in def.Views)
                {
                    if (v.Id == id) { return v.Index; }
                }

                throw new InvalidOperationException($"視点 {id} がありません");
            }

            int Mechanism(MechanismKind kind)
            {
                foreach (var m in def.Mechanisms)
                {
                    if (m.Kind == kind) { return m.Index; }
                }

                throw new InvalidOperationException($"{kind} がありません");
            }

            async Awaitable Shoot(string name, string note)
            {
                game.Render();
                await capture.ShootAsync(name, $"{note} (seed={flow.Seed}, tick={flow.PlayTicks}, hash=0x{flow.WorldHash:X8}, solved={flow.SolvedCount})");
            }

            void Key(IntentKind kind, int index = 0)
            {
                flow.Press(kind, index);
                flow.StepTicks(1);
            }

            void Settle()
            {
                for (int i = 0; i < MaxTicks && flow.InTransition; i++) { flow.StepTicks(1); }
                if (flow.InTransition) { throw new InvalidOperationException("カメラの演出が終わりません"); }
            }

            void Do(Command command)
            {
                Autopilot.Perform(flow, command);
                Settle();
            }

            void GoTo(int view)
            {
                var w = flow.Snapshot()!;
                foreach (var c in Navigation.PathTo(w, view)) { Do(c); }
            }

            void DriveUntil(Func<bool> done)
            {
                var pilot = new Autopilot();
                for (int i = 0; i < MaxTicks && !done(); i++)
                {
                    pilot.Drive(flow);
                    flow.StepTicks(1);
                }

                if (!done()) { throw new InvalidOperationException("操縦役が目標に届きません"); }
                Settle();
            }

            var dial = Mechanism(MechanismKind.Dial);
            var globe = Mechanism(MechanismKind.Rings);
            var clock = Mechanism(MechanismKind.Clock);
            var safe = Mechanism(MechanismKind.Sequence);
            var cabinet = def.Parts[Part("cabinet.keyhole")].Mechanism;

            await Shoot("01_title", "タイトル");
            Key(IntentKind.Down);
            Key(IntentKind.Confirm);
            await Shoot("02_howto", "操作説明");
            Key(IntentKind.Confirm);
            Key(IntentKind.Confirm);
            if (flow.Screen != FlowScreen.Playing) { throw new InvalidOperationException("遊ぶ画面になりません"); }
            capture.Record("matchSeed", flow.Seed.ToString(CultureInfo.InvariantCulture));
            await Shoot("03_north_wall", "北の壁（机と望遠鏡）");

            Do(Command.Focus(View("desk")));
            await Shoot("04_desk", "机の近景（ダイヤル錠・真鍮の筒）");
            Do(Command.Inspect(Part("desk.cylinder")));
            Do(Command.Rotate(0, 1));
            if (!flow.ClueFacing) { throw new InvalidOperationException("筒の底が向きません"); }
            await Shoot("05_cylinder_clue", "真鍮の筒を手に取って回すと、底に 3 桁");
            Do(Command.Back);

            DriveUntil(() => flow.PhaseOf(dial) == Phase.Solved);
            await Shoot("06_drawer_open", "ダイヤル錠が開き、前板が倒れて真鍮の鍵が見える");

            DriveUntil(() => flow.PhaseOf(cabinet) == Phase.Solved);
            await Shoot("07_cabinet_open", "真鍮の鍵でキャビネットが開き、星図の円盤");

            DriveUntil(() => flow.Inventory.Count == 1);
            Do(Command.InspectItem(flow.Inventory[0]));
            for (int i = 0; i < 4; i++) { Do(Command.Rotate(1, 0)); }
            if (!flow.ClueFacing) { throw new InvalidOperationException("円盤の裏が向きません"); }
            await Shoot("08_disc_clue", "持ち物の星図の円盤を手に取り、裏返すと 3 つの星座");
            Do(Command.Back);

            DriveUntil(() => flow.PhaseOf(globe) == Phase.Active);
            await Shoot("09_globe_rings", "円盤を台座にはめ、天球儀のリングが回るようになった");
            DriveUntil(() => flow.PhaseOf(globe) == Phase.Solved);
            await Shoot("10_globe_open", "リングを合わせると、ふたが外れて歯車");

            GoTo(View("bookshelf"));
            Do(Command.Inspect(Part("shelf.astronomy")));
            for (int i = 0; i < 4; i++) { Do(Command.Rotate(1, 0)); }
            if (!flow.ClueFacing) { throw new InvalidOperationException("天文書の裏表紙が向きません"); }
            await Shoot("11_book_clue", "天文書の裏表紙の時刻");
            Do(Command.Back);

            DriveUntil(() => flow.PhaseOf(clock) == Phase.Solved);
            await Shoot("12_clock_open", "針を合わせると振り子の扉が開き、金庫のハンドル");

            GoTo(View("telescope"));
            var cycle = (SequenceLock.GemCount * StudyStage.StarStepTicks) + StudyStage.StarRestTicks;
            for (int i = 0; i < cycle && flow.PlayTicks % cycle != StudyStage.StarStepTicks + 10; i++) { flow.StepTicks(1); }
            await Shoot("13_telescope", "望遠鏡をのぞくと、星が宝石の色の順に瞬く（2 番目の星）");

            DriveUntil(() => flow.PhaseOf(safe) == Phase.Active);
            var order = flow.Room.Mechanisms[safe].Answer;
            Do(Command.Operate(Part($"safe.gem{order[0]}")));
            Do(Command.Operate(Part($"safe.gem{order[1]}")));
            await Shoot("14_safe_gems", "ハンドルを差し、宝石を 2 つ正しく押した");

            for (int i = 0; i < 3; i++) { Do(Command.Hint); }
            await Shoot("15_hint_answer", "H を 3 回：同じ一手のヒントが答えの段階まで進む");

            DriveUntil(() => flow.Escaped);
            await Shoot("16_escape", "出口の鍵で扉が開いた");
            for (int i = 0; i < MaxTicks && flow.Screen != FlowScreen.Result; i++) { flow.StepTicks(1); }
            if (flow.Screen != FlowScreen.Result) { throw new InvalidOperationException($"結果画面になりません（{flow.Screen}）"); }
            capture.Record("finalHash", flow.WorldHash.ToString("X8", CultureInfo.InvariantCulture));
            await Shoot("17_result", "結果（経過時間・操作の回数・ヒントの数）");
            Key(IntentKind.Confirm);
            if (flow.Screen != FlowScreen.Title) { throw new InvalidOperationException("タイトルに戻りません"); }
        }
    }
}
