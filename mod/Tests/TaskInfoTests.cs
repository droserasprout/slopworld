using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class TaskInfoTests
    {
        public static void SummaryDoesNotSplitSurrogatePair()
        {
            var task = new TaskInfo { Body = new string('a', TaskInfo.SummaryChars - 1) + "\U0001F600tail" };
            AssertEx.Equal(new string('a', TaskInfo.SummaryChars - 1) + "...", task.Summary,
                "truncate before a split scalar");
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("reads task mailbox records", ReadsTask);
            yield return ("normalizes task status and body", NormalizesTaskValues);
            yield return ("bounds mailbox row summaries", BoundsSummary);
            yield return ("uses daemon task summaries", UsesGeneratedSummary);
        }

        static void ReadsTask()
        {
            var task = TaskInfo.FromWire(ProtobufFixtures.Read<Wire.Task>(JVal.Parse(
                "{\"id\":\"abc\",\"from\":\"host\",\"to\":\"agent\"," +
                "\"body\":\"fix it\",\"status\":\"working\"," +
                "\"note\":\"in progress\",\"created_ms\":100," +
                "\"updated_ms\":200}")));

            AssertEx.Equal("abc", task.Id, "id");
            AssertEx.Equal("host", task.From, "sender");
            AssertEx.Equal("agent", task.To, "recipient");
            AssertEx.Equal(DelegatedTaskStatus.Working, task.Status, "status");
            AssertEx.Equal("in progress", task.Note, "note");
            AssertEx.False(task.Incoming, "host-sent task direction");
            AssertEx.Equal("you -> agent", task.Direction, "direction");
            AssertEx.False(task.Terminal, "working is not terminal");
        }

        static void NormalizesTaskValues()
        {
            AssertEx.Equal(DelegatedTaskStatus.Queued, TaskInfo.ParseStatus("unknown"),
                           "unknown status fallback");
            AssertEx.Equal(DelegatedTaskStatus.Failed, TaskInfo.ParseStatus("FAILED"),
                           "status is case-insensitive");
            AssertEx.Equal(DelegatedTaskStatus.Canceled, TaskInfo.ParseStatus("canceled"),
                           "canceled status");
            AssertEx.Equal("one two three", TaskInfo.OneLine("one\n\ttwo\r\nthree"),
                           "body is one line");
            AssertEx.Equal("done", TaskInfo.StatusText(DelegatedTaskStatus.Done),
                           "wire status");
            AssertEx.True(new TaskInfo { Status = DelegatedTaskStatus.Canceled }.Terminal,
                          "canceled is terminal");
        }

        static void BoundsSummary()
        {
            var task = new TaskInfo { Body = new string('x', TaskInfo.SummaryChars + 40) };
            AssertEx.Equal(TaskInfo.SummaryChars + 3, task.Summary.Length,
                           "bounded preview plus ellipsis");
            AssertEx.True(task.Summary.EndsWith("..."), "long preview marker");
            AssertEx.Equal(new string('y', TaskInfo.SummaryChars),
                new TaskInfo { Body = new string('y', TaskInfo.SummaryChars) }.Summary,
                "boundary is preserved");
        }

        static void UsesGeneratedSummary()
        {
            var task = TaskInfo.FromWire(ProtobufFixtures.Read<Wire.Task>(JVal.Parse(
                "{\"body\":\"a much longer original task body\",\"summary\":\"plan " +
                "sidebar fix\"}")));

            AssertEx.Equal("plan sidebar fix", task.Summary, "daemon summary wins");
        }
    }
}
