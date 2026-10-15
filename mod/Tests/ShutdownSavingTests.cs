using System;
using System.Reflection;
using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class ShutdownSavingTests
    {
        [SetUp]
        public void Reset()
        {
            typeof(SaveCoordinator)
                .GetField("_shutdownSaved", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, false);
            typeof(LoadFailureRecovery)
                .GetField("<Pending>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, false);
            SaveCoordinator.ConsumeProgrammaticShutdown();
            Current.ProgramState = ProgramState.Playing;
            Current.Game = new Game();
            NextPlanet.Pending = false;
        }

        [TearDown]
        public void CleanUp()
        {
            Reset();
            Current.Game = null;
            Current.ProgramState = ProgramState.Entry;
        }

        static void RunPrefix(Type patch) =>
            patch
                .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);

        [Test]
        public void NativeCloseSavesWithoutUnityQuitEventOrAnotherFrame()
        {
            RunPrefix(typeof(Patch_SaveOnNativeShutdown));
            Assert.That(Current.Game.autosaver.Saves, Is.EqualTo(1));
            Assert.That(SaveCoordinator.ConsumeProgrammaticShutdown(), Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RootAndNativeShutdownSaveOnceInEitherOrder(bool rootFirst)
        {
            var first = rootFirst
                ? typeof(Patch_SaveOnShutdown)
                : typeof(Patch_SaveOnNativeShutdown);
            var second = rootFirst
                ? typeof(Patch_SaveOnNativeShutdown)
                : typeof(Patch_SaveOnShutdown);
            RunPrefix(first);
            RunPrefix(second);
            RunPrefix(second);
            Assert.That(Current.Game.autosaver.Saves, Is.EqualTo(1));
            Assert.That(SaveCoordinator.ConsumeProgrammaticShutdown(), Is.True);
        }

        [Test]
        public void ShutdownDoesNotSaveDiscardedColonyOrFailedLoad()
        {
            NextPlanet.Pending = true;
            RunPrefix(typeof(Patch_SaveOnNativeShutdown));
            Assert.That(Current.Game.autosaver.Saves, Is.Zero);
            Reset();
            LoadFailureRecovery.Begin(new Exception("failed load"));
            RunPrefix(typeof(Patch_SaveOnNativeShutdown));
            Assert.That(Current.Game.autosaver.Saves, Is.Zero);
        }

        [Test]
        public void EntryAndMissingGameDoNotPreventShutdown()
        {
            Current.ProgramState = ProgramState.Entry;
            RunPrefix(typeof(Patch_SaveOnNativeShutdown));
            Assert.That(Current.Game.autosaver.Saves, Is.Zero);
            Reset();
            Current.Game = null;
            Assert.DoesNotThrow(() => RunPrefix(typeof(Patch_SaveOnNativeShutdown)));
        }

        [Test]
        public void SaveFailureDoesNotPreventOrRepeatShutdown()
        {
            Current.Game.autosaver.Fail = true;
            Assert.DoesNotThrow(() => RunPrefix(typeof(Patch_SaveOnNativeShutdown)));
            Assert.DoesNotThrow(() => RunPrefix(typeof(Patch_SaveOnShutdown)));
            Assert.That(Current.Game.autosaver.Saves, Is.EqualTo(1));
        }

        [Test]
        public void PeriodicSaveDoesNotConsumeShutdownSave()
        {
            SaveCoordinator.SaveNow();
            RunPrefix(typeof(Patch_SaveOnNativeShutdown));
            Assert.That(Current.Game.autosaver.Saves, Is.EqualTo(2));
        }
    }
}

namespace Verse
{
    public enum ProgramState
    {
        Entry,
        Playing,
    }

    public static class Current
    {
        public static ProgramState ProgramState;
        public static Game Game;
    }

    public class Game
    {
        public readonly RimWorld.Autosaver autosaver = new();
    }
}

namespace RimWorld
{
    public class Autosaver
    {
        public int Saves;
        public bool Fail;

        public void DoAutosave()
        {
            Saves++;
            if (Fail)
                throw new InvalidOperationException("save failed");
        }
    }
}
