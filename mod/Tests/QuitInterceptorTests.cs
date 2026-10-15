using System.Reflection;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class QuitInterceptorTests
    {
        [SetUp]
        public void Reset()
        {
            var phase = typeof(QuitInterceptor).GetField(
                "_phase",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            phase.SetValue(null, System.Enum.ToObject(phase.FieldType, 0));
            UnityEngine.Application.wantsToQuit = null;
            SaveCoordinator.ConsumeProgrammaticShutdown();
            Verse.Root.Shutdowns = 0;
            QuitInterceptor.Register();
        }

        [Test]
        public void RepeatedExternalCloseWaitsForOneDeferredShutdown()
        {
            Assert.That(UnityEngine.Application.wantsToQuit(), Is.False);
            Assert.That(UnityEngine.Application.wantsToQuit(), Is.False);
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
            QuitInterceptor.Check();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.EqualTo(1));
            Assert.That(UnityEngine.Application.wantsToQuit(), Is.True);
        }

        [Test]
        public void ProgrammaticShutdownDoesNotQueueAnotherShutdown()
        {
            SaveCoordinator.NoteProgrammaticShutdown();
            Assert.That(UnityEngine.Application.wantsToQuit(), Is.True);
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
        }
    }
}

namespace UnityEngine
{
    public static partial class Application
    {
        public static System.Func<bool> wantsToQuit;
    }
}

namespace Verse
{
    public static class Root
    {
        public static int Shutdowns;

        public static void Update() { }

        public static void Shutdown()
        {
            Shutdowns++;
            SlopWorld.SaveCoordinator.NoteProgrammaticShutdown();
            NUnit.Framework.Assert.That(
                UnityEngine.Application.wantsToQuit(),
                NUnit.Framework.Is.True
            );
        }
    }
}
