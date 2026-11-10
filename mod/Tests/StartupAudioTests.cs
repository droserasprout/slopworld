using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class StartupAudioTests
    {
        [Test]
        public void CacheSurvivesRestartAndRejectsDifferentSourceIdentities()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "sizes.json");
            try
            {
                var cache = new AudioSizeCache(path, e => Assert.Fail(e.ToString()));
                cache.Record("runtime:pack:old-content", 1234);
                cache.Record(null, 100);
                cache.Record("failed-probe", 0);
                cache.Flush();
                cache.Flush();
                var restored = new AudioSizeCache(path, e => Assert.Fail(e.ToString()));
                Assert.That(restored.TryGet("runtime:pack:old-content", out long size), Is.True);
                Assert.That(size, Is.EqualTo(1234));
                Assert.That(restored.TryGet("runtime:pack:new-content", out _), Is.False);
                Assert.That(restored.TryGet("new-runtime:pack:old-content", out _), Is.False);
                Assert.That(restored.TryGet("failed-probe", out _), Is.False);
                Assert.That(restored.TryGet(null, out _), Is.False);
                Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void CorruptCacheAndFailedWritesRecoverWithoutLosingMeasurements()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "sizes.json");
            int errors = 0;
            try
            {
                File.WriteAllText(path, "broken");
                var cache = new AudioSizeCache(path, _ => errors++);
                Assert.That(errors, Is.EqualTo(1));
                cache.Record("sound", 42);
                File.Delete(path);
                Directory.CreateDirectory(path); // Atomic replacement must fail.
                cache.Flush();
                Assert.That(errors, Is.EqualTo(2));
                Assert.That(cache.TryGet("sound", out long size), Is.True);
                Assert.That(size, Is.EqualTo(42));
                Directory.Delete(path);
                cache.Flush();
                Assert.That(
                    new AudioSizeCache(path, _ => errors++).TryGet("sound", out _),
                    Is.True
                );
                Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ConcurrentCacheWritersPublishCompleteFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "sizes.json");
            try
            {
                var cache = new AudioSizeCache(path, e => Assert.Fail(e.ToString()));
                Parallel.For(
                    0,
                    32,
                    i =>
                    {
                        cache.Record(i.ToString(CultureInfo.InvariantCulture), i + 1);
                        cache.Flush();
                    }
                );
                cache.Flush();
                var restored = new AudioSizeCache(path, e => Assert.Fail(e.ToString()));
                for (int i = 0; i < 32; i++)
                {
                    Assert.That(
                        restored.TryGet(i.ToString(CultureInfo.InvariantCulture), out long size),
                        Is.True
                    );
                    Assert.That(size, Is.EqualTo(i + 1));
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void FirstUseResolvesOnceAndWarmQueueSkipsCompletedWork()
        {
            var actions = new DeferredStartupActions<object>();
            var sound = new object();
            int calls = 0;
            actions.Schedule(
                sound,
                () =>
                {
                    calls++;
                    actions.Ensure(sound);
                }
            );
            actions.Ensure(sound);
            actions.Ensure(sound);
            Assert.That(actions.WarmOne(), Is.False);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ReloadReplacesOldWorkAndReentrantRegistrationSurvives()
        {
            var actions = new DeferredStartupActions<object>();
            var sound = new object();
            int calls = 0;
            actions.Schedule(sound, () => Assert.Fail("stale callback"));
            actions.Schedule(
                sound,
                () =>
                {
                    calls++;
                    actions.Schedule(sound, () => calls++);
                }
            );
            actions.Ensure(sound);
            actions.Ensure(sound);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(actions.WarmOne(), Is.False);
        }

        [Test]
        public void FailedWarmupDoesNotBlockOtherSoundsOrLaterReloads()
        {
            var actions = new DeferredStartupActions<object>();
            var failed = new object();
            var other = new object();
            int calls = 0;
            actions.Schedule(failed, () => throw new InvalidOperationException("invalid sound"));
            actions.Schedule(other, () => calls++);
            Assert.Throws<InvalidOperationException>(() => actions.WarmOne());
            Assert.That(actions.WarmOne(), Is.True);
            actions.Schedule(failed, () => calls++);
            Assert.That(actions.WarmOne(), Is.True);
            Assert.That(actions.WarmOne(), Is.False);
            Assert.That(calls, Is.EqualTo(2));
        }
    }
}
