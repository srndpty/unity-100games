using System.Collections.Generic;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>メッセージ欄：見えた出来事だけを文にする。</summary>
    public sealed class MessageTests
    {
        [Test]
        public void EveryEventKindHasTextOrIsDeliberatelySilent()
        {
            for (var kind = EventKind.Attack; kind <= EventKind.Won; kind++)
            {
                var text = MessageLog.Text(new RunEvent(kind, 1, 0, MonsterKind.Thief, ItemKind.Bread, default, 3, true));
                if (kind == EventKind.Starve) { Assert.That(text, Is.Empty); }
                else { Assert.That(text, Is.Not.Empty, kind.ToString()); }
            }
        }

        [Test]
        public void UnseenEventsNeverBecomeMessagesOrSounds()
        {
            var flow = new CrawlFlow(new CrawlConfig(), 2);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            var run = flow.Run!;
            run.ClearMonsters();
            Vec2i? far = null;
            foreach (var room in run.Grid.Rooms)
            {
                var visible = new bool[run.Grid.Width * run.Grid.Height];
                Fov.Compute(run.Grid, room.Center, run.Config.ViewRadius, visible);
                if (!visible[run.Grid.Index(run.Player.Position)]) { far = room.Center; }
            }

            var golem = run.AddMonster(MonsterKind.Golem, far!.Value);
            golem.SleepTurns = 1;
            run.Refresh();
            var lines = flow.Messages.Version;
            flow.Apply(Intent.Down(Control.Wait, 1));
            Assert.That(run.Events, Has.Some.Matches<RunEvent>(e => e.Kind == EventKind.MonsterWoke), "Core は記録している");
            Assert.That(flow.Messages.Version, Is.EqualTo(lines), "視界の外で目を覚ましても文にしない");
            var events = new List<RunEvent>();
            flow.TakeEvents(events);
            Assert.That(events, Is.Empty, "音も鳴らさない");
        }

        [Test]
        public void LogKeepsTheLatestLines()
        {
            var log = new MessageLog();
            for (int i = 0; i < MessageLog.Capacity + 10; i++) { log.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            Assert.That(log.Lines, Has.Count.EqualTo(MessageLog.Capacity));
            Assert.That(log.Lines[0], Is.EqualTo("10"));
            Assert.That(log.Version, Is.EqualTo(MessageLog.Capacity + 10));
            log.Add(string.Empty);
            Assert.That(log.Version, Is.EqualTo(MessageLog.Capacity + 10));
        }
    }
}
