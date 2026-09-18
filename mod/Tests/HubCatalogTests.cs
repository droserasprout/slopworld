using System;

namespace SlopWorld.Tests
{
    static class HubCatalogTests
    {
        static JVal Snapshot(string name) => JVal.Parse("{\"projects\":[{\"name\":" + JVal.Q(name) +
            "}],\"library\":[{\"name\":" + JVal.Q(name) + "}],\"presets\":[{\"name\":" + JVal.Q(name) +
            "}],\"commands\":[{\"name\":" + JVal.Q(name) + "}]}");

        public static void Ordering()
        {
            var requests = DaemonClient.Requests;
            int errors = 0, sessions = 0, completed = 0;
            var hub = new HubCatalog(() => sessions++);
            Action<string> fail = _ => errors++;
            Action[] refreshes = { () => hub.RefreshProjects(fail), () => hub.RefreshLibrary(fail),
                () => hub.LoadPresets(fail: fail) };
            Func<string>[] names = { () => hub.Projects[0].Name, () => hub.Library[0].Name,
                () => hub.Presets[0].Name };
            for (int i = 0; i < refreshes.Length; i++)
            {
                requests.Clear();
                refreshes[i]();
                requests[0].Ok(Snapshot("initial"));
                refreshes[i]();
                AssertEx.Equal("initial", names[i](), "retain values while loading");
                refreshes[i]();
                requests[2].Ok(Snapshot("new"));
                requests[1].Ok(Snapshot("old"));
                requests[1].Fail("stale");
                AssertEx.Equal("new", names[i](), "newest GET wins");
                AssertEx.Equal(i, errors, "stale failure ignored");
                requests[2].Fail("current");
            }
            AssertEx.Equal("new", hub.Commands[0].Name, "commands share preset revision");
            for (int i = 0; i < 2; i++)
            {
                refreshes[i]();
                var pending = requests[requests.Count - 1];
                if (i == 0) hub.ApplyProjects(Snapshot("socket"));
                else hub.ApplyLibrary(Snapshot("socket"));
                pending.Ok(Snapshot("stale"));
                pending.Fail("stale");
                AssertEx.Equal("socket", names[i](), "socket supersedes GET");
            }
            Action[] writes = {
                () => hub.SaveProject(new ProjectInfo(), false, "a/b", () => completed++, fail),
                () => hub.SaveLibraryItem(new LibraryItemInfo(), true, "", () => completed++, fail),
                () => hub.CopyPreset("sandbox_presets", "a/b", "copy", () => completed++, fail),
                () => hub.SavePreset(new PresetInfo(), () => completed++, fail),
                () => hub.RemovePreset("sandbox_presets", "a/b", () => completed++, fail),
                () => hub.SaveCommand(new CommandInfo(), () => completed++, fail),
            };
            for (int i = 0; i < writes.Length; i++)
            {
                int catalog = Math.Min(i, 2);
                refreshes[catalog]();
                var pending = requests[requests.Count - 1];
                string before = names[catalog]();
                writes[i]();
                var write = requests[requests.Count - 1];
                pending.Ok(Snapshot("stale"));
                pending.Fail("stale");
                AssertEx.Equal(before, names[catalog](), "write invalidates pending read");
                write.Ok(Snapshot("ignored"));
                var reload = requests[requests.Count - 1];
                refreshes[catalog]();
                reload.Ok(Snapshot("stale"));
                AssertEx.Equal(i + 1, completed, "write completes despite superseded reload");
                AssertEx.Equal(before, names[catalog](), "superseded reload ignored");
            }
            AssertEx.Equal(1, sessions, "project save refreshes sessions");
            hub.LoadPresets(() => completed++, fail);
            var obsolete = requests[requests.Count - 1];
            hub.LoadPresets(() => completed++, fail);
            obsolete.Ok(Snapshot("stale"));
            obsolete.Fail("stale");
            AssertEx.Equal(writes.Length, completed, "superseded load callback ignored");
            requests[requests.Count - 1].Ok(Snapshot("current"));
            AssertEx.Equal(writes.Length + 1, completed, "current load callback delivered");
            hub.RefreshProjects(fail);
            obsolete = requests[requests.Count - 1];
            hub.RemoveProject("a/b", fail);
            obsolete.Ok(Snapshot("stale"));
            AssertEx.Equal("socket", hub.Projects[0].Name, "delete invalidates GET");
            AssertEx.True(requests[requests.Count - 1].Path.EndsWith("/a%2Fb"), "escaped project path");
            requests[requests.Count - 1].Ok(Snapshot("ignored"));
            AssertEx.Equal(2, sessions, "project delete refreshes sessions");
            AssertEx.Equal(3, errors, "only current GET failures delivered");
            requests.Clear();
        }
    }
}
