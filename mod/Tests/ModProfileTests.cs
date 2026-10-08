using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class ModProfileTests
    {
        string _folder;
        string _previousFolder;

        [SetUp]
        public void SetUp()
        {
            _previousFolder = GenFilePaths.SaveDataFolderPath;
            _folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_folder);
            GenFilePaths.SaveDataFolderPath = _folder;
            foreach (var name in new[] { "_ok", "_notice" }) Set(name, null);
            foreach (var name in new[] { "_complained", "_noticePending", "_disabled" }) Set(name, false);
            Find.WindowStack = new WindowStack();
            ModsConfig.Active.Clear();
            ModsConfig.Active.Add("ludeon.rimworld");
            ModsConfig.Active.Add("other.mod");
            ModsConfig.Active.Add("io.drsr.slopworld");
            ModsConfig.Saves = 0;
            ModsConfig.FailSave = false;
        }

        static void Set(string name, object value) => typeof(ModProfile)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);

        [TearDown]
        public void TearDown()
        {
            GenFilePaths.SaveDataFolderPath = _previousFolder;
            Find.WindowStack = new WindowStack();
            Directory.Delete(_folder, true);
            Set("_ok", null);
        }

        [Test]
        public void RefusalDisablesOnlySlopWorldAndSavesOnce()
        {
            Assert.That(ModProfile.Ok, Is.False);
            ModProfile.Complain();
            ModProfile.Complain();
            Assert.That(ModsConfig.Active, Is.EquivalentTo(new[] { "ludeon.rimworld", "other.mod" }));
            Assert.That(ModsConfig.Saves, Is.EqualTo(1));
        }

        [Test]
        public void MarkedProfileIsAcceptedWithoutChangingMods()
        {
            File.WriteAllText(Path.Combine(_folder, ModProfile.Marker), "");
            Assert.That(ModProfile.Ok, Is.True);
            Assert.That(ModsConfig.Active.Contains("io.drsr.slopworld"), Is.True);
            Assert.That(ModsConfig.Saves, Is.Zero);
        }

        [Test]
        public void SaveFailureStillShowsMandatoryRefusal()
        {
            ModsConfig.FailSave = true;
            Assert.DoesNotThrow(ModProfile.Complain);
            ModProfile.ShowPendingNotice();
            Assert.That(Find.WindowStack.Count, Is.EqualTo(1));
            Assert.That(((Window)Find.WindowStack[0]).closeOnCancel, Is.False);
        }

        [Test]
        public void EarlyNoticeWaitsForWindowStackAndDoesNotDuplicate()
        {
            Find.WindowStack = null;
            ModProfile.Complain();
            Assert.DoesNotThrow(ModProfile.ShowPendingNotice);
            Find.WindowStack = new WindowStack();
            ModProfile.ShowPendingNotice();
            Assert.That(Find.WindowStack.Count, Is.EqualTo(1));
            ModProfile.Complain();
            ModProfile.ShowPendingNotice();
            Assert.That(Find.WindowStack.Count, Is.EqualTo(1));
            Find.WindowStack.Clear();
            ModProfile.ShowPendingNotice();
            Assert.That(Find.WindowStack.Count, Is.EqualTo(1));
        }
    }
}
