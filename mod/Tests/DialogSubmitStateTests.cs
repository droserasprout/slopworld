using System;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class DialogSubmitStateTests
    {
        public static void PendingSavesAreBlockedAndFailureAllowsRetry()
        {
            var state = new DialogSubmitState();
            int requests = 0;
            int closes = 0;
            Action finish = null;
            Action<string> fail = null;
            void Request(Action done, Action<string> failed)
            {
                requests++;
                finish = done;
                fail = failed;
            }
            state.Submit(null, Request, () => closes++);
            state.Submit(null, Request, () => closes++);
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(state.Pending, Is.True);
            fail("offline");
            Assert.That(state.Pending, Is.False);
            Assert.That(state.Error, Is.EqualTo("offline"));
            state.Submit(null, Request, () => closes++);
            Assert.That(state.Error, Is.Null);
            finish();
            state.Submit(null, Request, () => closes++);
            Assert.That(requests, Is.EqualTo(2));
            Assert.That(closes, Is.EqualTo(1));
        }

        public static void SynchronousFailureReleasesSubmission()
        {
            var state = new DialogSubmitState();
            state.Submit(null, (_, fail) => fail("offline"), () => Assert.Fail("must not close"));
            Assert.That(state.Pending, Is.False);
            Assert.That(state.Error, Is.EqualTo("offline"));
        }
    }
}
