using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SidebarProjectStateTests
    {
        static void WithState(Action<SidebarProjectState, string> test)
        {
            var settings = Settings.S;
            string folded = settings.foldedProjects, filter = settings.sidebarFilter;
            string oldProfile = Verse.GenFilePaths.SaveDataFolderPath;
            string profile = Path.Combine(Path.GetTempPath(), "slop-folds-" + Guid.NewGuid());
            Verse.GenFilePaths.SaveDataFolderPath = profile;
            settings.foldedProjects = "keep";
            settings.sidebarFilter = "";
            try { test(new SidebarProjectState(), Path.Combine(profile, "Config", "SlopWorld.toml")); }
            finally
            {
                settings.foldedProjects = folded;
                settings.sidebarFilter = filter;
                Verse.GenFilePaths.SaveDataFolderPath = oldProfile;
                if (Directory.Exists(profile)) Directory.Delete(profile, true);
            }
        }

        public static void BulkFoldsPersistAfterAllKeysAndSkipUnchangedUpdates() => WithState((state, path) =>
        {
            IEnumerable<string> Keys()
            {
                foreach (string key in new[] { "z", "a", "z" })
                {
                    Assert.That(File.Exists(path), Is.False, "bulk enumeration must not write partial settings");
                    Assert.That(Settings.S.foldedProjects, Is.EqualTo("keep"));
                    yield return key;
                }
            }
            state.SetFolded(Keys(), true);
            Assert.That(Settings.S.foldedProjects, Is.EqualTo("a\nkeep\nz"));
            Assert.That(ProfileSettings.Load().foldedProjects, Is.EqualTo("a\nkeep\nz"));
            File.Delete(path);
            state.SetFolded("a", true);
            state.SetFolded("missing", false);
            state.SetFolded(new[] { "z", "a" }, true);
            Assert.That(File.Exists(path), Is.False, "unchanged updates must not write settings");
            state.SetFolded(new[] { "a", "z" }, false);
            Assert.That(ProfileSettings.Load().foldedProjects, Is.EqualTo("keep"));
        });

        public static void MirrorsParseNamesConsistentlyAndFollowExternalChanges() => WithState((state, path) =>
        {
            Settings.S.foldedProjects = "b\n\na\nb\n";
            Settings.S.sidebarFilter = "b\n\na\nb\n";
            Assert.That(state.Folded, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(state.Filter, Is.EquivalentTo(state.Folded));
            int revision = state.Revision;
            Settings.S.foldedProjects = "new";
            Settings.S.sidebarFilter = "";
            Assert.That(state.Revision, Is.GreaterThan(revision));
            Assert.That(state.Folded, Is.EquivalentTo(new[] { "new" }));
            Assert.That(state.Filtering, Is.False);
            Assert.That(File.Exists(path), Is.False, "reading external preferences does not rewrite them");
        });

        public static void AddedProjectsJoinAnExplicitFilterWithoutNarrowingAllProjects() => WithState((state, path) =>
        {
            state.IncludeProject("new");
            Assert.That(state.Filtering, Is.False);
            Assert.That(File.Exists(path), Is.False, "All projects already includes the new project");

            Settings.S.sidebarFilter = "existing";
            state.IncludeProject("new");
            Assert.That(state.Passes("existing"), Is.True);
            Assert.That(state.Passes("new"), Is.True);
            Assert.That(state.Passes("other"), Is.False);
            Assert.That(ProfileSettings.Load().sidebarFilter, Is.EqualTo("existing\nnew"));

            File.Delete(path);
            state.IncludeProject("new");
            Assert.That(state.Passes("new"), Is.True, "repeated inclusion must not toggle the project off");
            Assert.That(File.Exists(path), Is.False);
        });
    }
}
