using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class WorkerBasePreviewStateTests
    {
        public static void RepeatedResolutionRejectsOlderSuccessAndFailure()
        {
            var state = new WorkerBasePreviewState();
            long first = state.Begin("host", "HEAD");
            long latest = state.Begin("host", "HEAD");
            state.Accept(latest, "host", "HEAD", "new-commit");
            state.Accept(first, "host", "HEAD", "old-commit");
            state.Fail(first, "host", "HEAD", "old-error");
            Assert.That(state.Commit, Is.EqualTo("new-commit"));
            Assert.That(state.Error, Is.Null);
        }

        public static void ReturningToRevisionDoesNotReviveItsEarlierRequest()
        {
            var state = new WorkerBasePreviewState();
            long first = state.Begin("host", "main");
            state.Begin("host", "other");
            long latest = state.Begin("host", "main");
            state.Accept(first, "host", "main", "old-commit");
            Assert.That(state.Commit, Is.Empty);
            state.Fail(latest, "host", "main", "unavailable");
            Assert.That(state.Error, Is.EqualTo("unavailable"));
            long retry = state.Begin("host", "main");
            state.Accept(retry, "host", "main", "recovered");
            Assert.That(state.Commit, Is.EqualTo("recovered"));
            Assert.That(state.Error, Is.Null);
        }

        public static void ChangedFieldsAndClosedDialogRejectReplies()
        {
            var state = new WorkerBasePreviewState();
            long request = state.Begin("agent", "HEAD");
            state.Accept(request, "other-agent", "HEAD", "wrong-caller");
            state.Accept(request, "agent", "main", "wrong-revision");
            Assert.That(state.Commit, Is.Empty);
            state.Reset();
            state.Reset();
            state.Accept(request, "agent", "HEAD", "closed");
            state.Fail(request, "agent", "HEAD", "closed-error");
            Assert.That(state.Commit, Is.Empty);
            Assert.That(state.Error, Is.Null);
        }
    }
}
