using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class TaskInfoBehaviorTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (var item in new[]
            {
                (Wire: "queued", Parsed: DelegatedTaskStatus.Queued, Terminal: false),
                (Wire: "accepted", Parsed: DelegatedTaskStatus.Accepted, Terminal: false),
                (Wire: "working", Parsed: DelegatedTaskStatus.Working, Terminal: false),
                (Wire: "done", Parsed: DelegatedTaskStatus.Done, Terminal: true),
                (Wire: "failed", Parsed: DelegatedTaskStatus.Failed, Terminal: true),
                (Wire: "canceled", Parsed: DelegatedTaskStatus.Canceled, Terminal: true),
            })
                yield return ($"task status {item.Wire} roundtrips and classifies terminal state", () =>
                {
                    var task = TaskInfo.FromWire(new Wire.Task { Status = item.Wire.ToUpperInvariant() });
                    Assert.That(task.Status, Is.EqualTo(item.Parsed));
                    Assert.That(task.Terminal, Is.EqualTo(item.Terminal));
                    Assert.That(TaskInfo.StatusText(task.Status), Is.EqualTo(item.Wire));
                });
            foreach (var item in new[] { (Elapsed: -120, Expected: "now"), (Elapsed: 0, Expected: "now"), (Elapsed: 15, Expected: "15s"), (Elapsed: 150, Expected: "2m"), (Elapsed: 9000, Expected: "2h"), (Elapsed: 216000, Expected: "2d") })
                yield return ($"task age formats {item.Elapsed} seconds", () => Age(item.Elapsed, item.Expected));
        }

        static void Age(int seconds, string expected)
        {
            // Retry only across an observed second boundary, avoiding dependence on the test
            // runner's speed while still checking exact output from the real wall clock.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var task = new TaskInfo { CreatedMs = (now - seconds) * 1000, UpdatedMs = (now + 120) * 1000 };
                string created = task.Age();
                string updated = task.Age(updated: true);
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() != now) continue;
                Assert.That(created, Is.EqualTo(expected));
                Assert.That(updated, Is.EqualTo("now"), "updated age reads its own timestamp");
                return;
            }
            Assert.Fail("could not read task age within one clock second");
        }

        public static void WorkerMetadataAndOptionalFieldsPreserveWirePresence()
        {
            var task = TaskInfo.FromWire(ProtobufFixtures.Read<Wire.Task>(JVal.Parse(@"{
                ""id"":""task"", ""worker"": { ""session"":""worker-1"", ""parent"":""parent"", ""durable"":true },
                ""note"":"""", ""summary"":"""", ""created_ms"":100, ""updated_ms"":200
            }")));
            Assert.That(task.Worker, Is.True);
            Assert.That(task.WorkerSession, Is.EqualTo("worker-1"));
            Assert.That(task.WorkerParent, Is.EqualTo("parent"));
            Assert.That(task.WorkerDurable, Is.True);
            Assert.That(task.Note, Is.Empty, "present empty note is distinct from omission");
            Assert.That(task.GeneratedSummary, Is.Empty);
            Assert.That(task.CreatedMs, Is.EqualTo(100));
            Assert.That(task.UpdatedMs, Is.EqualTo(200));
            var absent = TaskInfo.FromWire(new Wire.Task());
            Assert.That(absent.Worker, Is.False);
            Assert.That(absent.Note, Is.Null);
            Assert.That(absent.GeneratedSummary, Is.Null);
            Assert.That(absent.WorkerParent, Is.Empty);
        }

        public static void DirectionAndCounterpartDistinguishHostAndAgentMail()
        {
            var incoming = new TaskInfo { From = "agent", To = TaskInfo.Host };
            Assert.That(incoming.Incoming, Is.True);
            Assert.That(incoming.Outgoing, Is.False);
            Assert.That(incoming.Direction, Is.EqualTo("agent -> you"));
            Assert.That(incoming.Counterpart, Is.EqualTo("agent"));
            var outgoing = new TaskInfo { From = TaskInfo.Host, To = "worker" };
            Assert.That(outgoing.Outgoing, Is.True);
            Assert.That(outgoing.Direction, Is.EqualTo("you -> worker"));
            Assert.That(outgoing.Counterpart, Is.EqualTo("worker"));
            var other = new TaskInfo { From = "parent", To = "child" };
            Assert.That(other.Incoming || other.Outgoing, Is.False);
            Assert.That(other.Direction, Is.EqualTo("parent -> child"));
            Assert.That(other.Counterpart, Is.EqualTo("child"));
            Assert.That(other.Direction, Is.EqualTo("parent -> child"), "cached direction remains stable");
        }

        public static void SummaryFallbackNormalizesWhitespaceAndTrimsTruncationBoundary()
        {
            Assert.That(TaskInfo.OneLine(null), Is.Empty);
            Assert.That(TaskInfo.OneLine(""), Is.Empty);
            Assert.That(TaskInfo.OneLine(" \t\r\n"), Is.Empty);
            Assert.That(TaskInfo.OneLine(" \tfirst\u00a0\u2003second \n"), Is.EqualTo("first second"));
            var task = new TaskInfo { GeneratedSummary = " \t", Body = new string('a', TaskInfo.SummaryChars - 1) + " rest" };
            Assert.That(task.Summary, Is.EqualTo(new string('a', TaskInfo.SummaryChars - 1) + "..."));
            task = new TaskInfo { Body = "body", GeneratedSummary = "  generated\n summary  " };
            Assert.That(task.Summary, Is.EqualTo("generated summary"));
            Assert.That(task.Summary, Is.EqualTo("generated summary"));
            Assert.That(TaskInfo.ParseStatus(null), Is.EqualTo(DelegatedTaskStatus.Queued));
            Assert.That(TaskInfo.StatusText((DelegatedTaskStatus)999), Is.EqualTo("queued"));
        }
    }
}
