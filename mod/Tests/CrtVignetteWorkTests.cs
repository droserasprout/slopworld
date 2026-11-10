using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    public class CrtVignetteWorkTests
    {
        static readonly CrtVignetteWork.Request Small = new(4, 4, .2f, .1f, 1f);

        [Test]
        public void ResizeRejectsStalePixelsAndPublishesOnlyReplacement()
        {
            var first = new TaskCompletionSource<Color32[]>();
            var second = new TaskCompletionSource<Color32[]>();
            CancellationToken retired = default;
            int calls = 0;
            using var work = new CrtVignetteWork(
                e => Assert.Fail(e.ToString()),
                (_, token) =>
                {
                    if (++calls == 1)
                    {
                        retired = token;
                        return first.Task;
                    }
                    return second.Task;
                }
            );
            Assert.That(work.TryGet(Small, out _), Is.False);
            var larger = Small with { Width = 8 };
            Assert.That(work.TryGet(larger, out _), Is.False);
            Assert.That(retired.IsCancellationRequested, Is.True);
            first.SetResult(new Color32[16]);
            Assert.That(work.TryGet(larger, out _), Is.False);
            var replacement = new Color32[32];
            second.SetResult(replacement);
            Assert.That(work.TryGet(larger, out var pixels), Is.True);
            Assert.That(pixels, Is.SameAs(replacement));
        }

        [Test]
        public void FailureReportsOnceAndCancelAllowsRetry()
        {
            int reports = 0,
                calls = 0;
            using var work = new CrtVignetteWork(
                _ => reports++,
                (_, _) =>
                {
                    calls++;
                    return Task.FromException<Color32[]>(new InvalidOperationException());
                }
            );
            work.TryGet(Small, out _);
            work.TryGet(Small, out _);
            work.TryGet(Small, out _);
            Assert.That((reports, calls), Is.EqualTo((1, 1)));
            work.Cancel();
            work.Cancel();
            work.TryGet(Small, out _);
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void DisabledWorkerCannotPublishAfterRestart()
        {
            var old = new TaskCompletionSource<Color32[]>();
            var current = new Color32[16];
            int calls = 0;
            using var work = new CrtVignetteWork(
                e => Assert.Fail(e.ToString()),
                (_, _) => ++calls == 1 ? old.Task : Task.FromResult(current)
            );
            work.TryGet(Small, out _);
            work.Cancel();
            old.SetResult(new Color32[16]);
            Assert.That(work.TryGet(Small, out _), Is.False);
            Assert.That(work.TryGet(Small, out var result), Is.True);
            Assert.That(result, Is.SameAs(current));
        }

        [Test]
        public void CancellationStopsPixelGeneration()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                CrtVignettePixels.Build(4, 4, .2f, .1f, 1f, cancellation.Token)
            );
        }
    }
}
