using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class MainThreadCompletionsTests
    {
        public static void CallbackFailurePreservesRemainingWorkAndReportsOnPumpThread()
        {
            var queue = new MainThreadCompletions(2, 10, () => 0);
            var delivered = new List<int>();
            var failure = new InvalidOperationException("callback failed");
            int pumpThread = Environment.CurrentManagedThreadId;
            var producer = new Thread(() =>
            {
                queue.Enqueue(() => delivered.Add(1));
                queue.Enqueue(() => throw failure);
                queue.Enqueue(() => delivered.Add(3));
            });
            producer.Start();
            producer.Join();
            Assert.That(delivered, Is.Empty, "admission does not run callbacks");

            Exception reported = null;
            Assert.That(queue.Pump(error =>
            {
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(pumpThread));
                reported = error;
            }), Is.EqualTo(2));
            Assert.That(reported, Is.SameAs(failure));
            Assert.That(delivered, Is.EqualTo(new[] { 1 }));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.Pump(error => throw error), Is.EqualTo(1));
            Assert.That(delivered, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(queue.Pump(error => throw error), Is.Zero, "settled work is not replayed");
        }

        public static void SlowCallbackYieldsWithFifoWorkRetainedForNextFrame()
        {
            long now = 0;
            var queue = new MainThreadCompletions(32, 2, () => now);
            var delivered = new List<int>();
            queue.Enqueue(() => { delivered.Add(1); now += 3; });
            queue.Enqueue(() => delivered.Add(2));
            Assert.That(queue.Pump(error => throw error), Is.EqualTo(1));
            Assert.That(delivered, Is.EqualTo(new[] { 1 }));
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.Pump(error => throw error), Is.EqualTo(1));
            Assert.That(delivered, Is.EqualTo(new[] { 1, 2 }));
        }

        public static void ZeroBudgetStillDeliversOneCallbackAndReentrantAdmissionKeepsOrder()
        {
            var queue = new MainThreadCompletions(32, 0, () => 0);
            var delivered = new List<int>();
            queue.Enqueue(null);
            queue.Enqueue(() =>
            {
                delivered.Add(1);
                queue.Enqueue(() => delivered.Add(3));
            });
            queue.Enqueue(() => delivered.Add(2));
            Assert.That(queue.Pump(error => throw error), Is.EqualTo(1));
            Assert.That(queue.Pump(error => throw error), Is.EqualTo(1));
            Assert.That(queue.Pump(error => throw error), Is.EqualTo(1));
            Assert.That(delivered, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(queue.Count, Is.Zero);
        }
    }
}
