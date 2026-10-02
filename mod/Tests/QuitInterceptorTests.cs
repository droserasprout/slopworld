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
            var phase = typeof(QuitInterceptor).GetField("_phase", BindingFlags.Static | BindingFlags.NonPublic);
            phase.SetValue(null, System.Enum.ToObject(phase.FieldType, 0));
            UnityEngine.Application.wantsToQuit = null;
            SaveCoordinator.Programmatic = false;
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
            SaveCoordinator.Programmatic = true;
            Assert.That(UnityEngine.Application.wantsToQuit(), Is.True);
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
        }
    }
}

namespace UnityEngine
{
    public static class Application
    {
        public static System.Func<bool> wantsToQuit;
    }
}

namespace Verse
{
    public static class Root
    {
        public static int Shutdowns;
        public static void Shutdown()
        {
            Shutdowns++;
            SlopWorld.SaveCoordinator.Programmatic = true;
            NUnit.Framework.Assert.That(UnityEngine.Application.wantsToQuit(), NUnit.Framework.Is.True);
        }
    }
}

namespace SlopWorld
{
    public static class SaveCoordinator
    {
        public static bool Programmatic;
        public static bool ConsumeProgrammaticShutdown()
        {
            bool result = Programmatic;
            Programmatic = false;
            return result;
        }
    }
}
