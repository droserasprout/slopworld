using System;

namespace SlopWorld.Tests
{
    static class HubCatalogTests
    {
        static JVal Snapshot(string name, int catalog) => catalog == 0
            ? ProtobufFixtures.Json(new Wire.ProjectsReply { Projects = { new Wire.Project { Name = name } } })
            : catalog == 1
                ? ProtobufFixtures.Json(new Wire.LibraryReply { Library = { new Wire.LibraryItem { Name = name } } })
                : ProtobufFixtures.Json(new Wire.PresetsReply {
                    Presets = { new Wire.SandboxPreset { Name = name } }, Commands = { new Wire.CommandPreset { Name = name } } });

        public static void SupersededRefreshesWaitForWinningSuccessOrFailure()
        {
            var requests = DaemonClient.Requests;
            requests.Clear();
            try
            {
                var hub = new HubCatalog(() => { });
                int loaded = 0, failed = 0;
                Action success = () => { AssertEx.Equal("winner", hub.Templates[0].Name, "publish before callbacks"); loaded++; };
                Action<string> failure = error => { AssertEx.Equal("unavailable", error, "winning failure"); failed++; };
                hub.RefreshTemplates(failure, success);
                hub.RefreshTemplates(failure, success);
                requests[0].Ok(JVal.Parse(@"{""templates"":[{""name"":""stale""}]}"));
                AssertEx.Equal(0, loaded, "stale success cannot settle callers");
                requests[1].Ok(JVal.Parse(@"{""templates"":[{""name"":""winner""}]}"));
                AssertEx.Equal(2, loaded, "both callers see winning snapshot");
                hub.RefreshTemplates(failure, success);
                hub.RefreshTemplates(failure, success);
                requests[2].Fail("stale failure");
                AssertEx.Equal(0, failed, "stale failure cannot settle callers");
                requests[3].Fail("unavailable");
                AssertEx.Equal(2, failed, "both callers receive winning error");
                AssertEx.Equal(2, loaded, "failure is never reported as success");
            }
            finally { requests.Clear(); }

        }

        public static void MutationFailureSettlesReadsAndReentrantRefreshWaitsForItsOwnOutcome()
        {
            var requests = DaemonClient.Requests;
            requests.Clear();
            try
            {
                var hub = new HubCatalog(() => { });
                int failed = 0, loaded = 0;
                hub.RefreshTemplates(_ => failed++);
                hub.RemoveAgentTemplate(new AgentTemplateInfo(), null, null, _ => failed++);
                requests[1].Fail("write failed");
                AssertEx.Equal(2, failed, "write failure releases refresh and mutation callers");
                hub.RefreshTemplates(loaded: () => { loaded++; hub.RefreshTemplates(loaded: () => loaded++); });
                requests[2].Ok(JVal.Parse("{}"));
                AssertEx.Equal(1, loaded, "reentrant request is not completed by old outcome");
                requests[3].Ok(JVal.Parse("{}"));
                AssertEx.Equal(2, loaded, "reentrant request completes on its response");
            }
            finally { requests.Clear(); }

        }

        sealed class Fixture : IDisposable
        {
            public readonly HubCatalog Hub;
            public readonly System.Collections.Generic.List<DaemonClient.Request> Requests = DaemonClient.Requests;
            public int Errors, Sessions;
            public Fixture()
            {
                Requests.Clear();
                Hub = new HubCatalog(() => Sessions++);
            }
            public void Refresh(int catalog)
            {
                switch (catalog)
                {
                    case 0: Hub.RefreshProjects(_ => Errors++); break;
                    case 1: Hub.RefreshLibrary(_ => Errors++); break;
                    case 2: Hub.LoadPresets(fail: _ => Errors++); break;
                    default: throw new ArgumentOutOfRangeException(nameof(catalog));
                }
            }
            public string Name(int catalog) => catalog == 0 ? Hub.Projects[0].Name :
                catalog == 1 ? Hub.Library[0].Name : Hub.Presets[0].Name;
            public void Dispose() => Requests.Clear();
        }

        public static void NewestGetWinsAndStaleFailuresAreIgnored()
        {
            for (int catalog = 0; catalog < 3; catalog++)
            {
                using var fixture = new Fixture();
                fixture.Refresh(catalog);
                fixture.Requests[0].Ok(Snapshot("initial", catalog));
                fixture.Refresh(catalog);
                var stale = fixture.Requests[1];
                AssertEx.Equal("initial", fixture.Name(catalog), "retain snapshot while loading");
                fixture.Refresh(catalog);
                var winner = fixture.Requests[2];
                winner.Ok(Snapshot("new", catalog));
                stale.Ok(Snapshot("old", catalog));
                stale.Fail("stale");
                AssertEx.Equal("new", fixture.Name(catalog), "newest GET wins for catalog " + catalog);
                AssertEx.Equal(0, fixture.Errors, "stale failure ignored");
                fixture.Refresh(catalog);
                fixture.Requests[3].Fail("current");
                AssertEx.Equal(1, fixture.Errors, "current failure delivered");
                if (catalog == 2) AssertEx.Equal("new", fixture.Hub.Commands[0].Name, "commands share preset revision");
            }
        }

        public static void SocketSnapshotSupersedesPendingGet()
        {
            for (int catalog = 0; catalog < 2; catalog++)
            {
                using var fixture = new Fixture();
                fixture.Refresh(catalog);
                var pending = fixture.Requests[0];
                if (catalog == 0) fixture.Hub.ApplyProjects(ProtobufFixtures.Read<Wire.ProjectsReply>(Snapshot("socket", catalog)));
                else fixture.Hub.ApplyLibrary(ProtobufFixtures.Read<Wire.LibraryReply>(Snapshot("socket", catalog)));
                pending.Ok(Snapshot("stale", catalog));
                pending.Fail("stale");
                AssertEx.Equal("socket", fixture.Name(catalog), "socket snapshot wins");
                AssertEx.Equal(0, fixture.Errors, "superseded GET error ignored");
            }
        }

        public static void WritesInvalidateReadsAndCompleteDespiteSupersededReload()
        {
            foreach (string operation in new[] { "project", "library", "copy-preset", "save-preset", "remove-preset", "command" })
            {
                using var fixture = new Fixture();
                int catalog = operation == "project" ? 0 : operation == "library" ? 1 : 2;
                fixture.Refresh(catalog);
                fixture.Requests[0].Ok(Snapshot("initial", catalog));
                fixture.Refresh(catalog);
                var pending = fixture.Requests[1];
                int completed = 0;
                Action ok = () => completed++;
                Action<string> fail = _ => fixture.Errors++;
                switch (operation)
                {
                    case "project": fixture.Hub.SaveProject(new ProjectInfo(), false, "a/b", ok, fail); break;
                    case "library": fixture.Hub.SaveLibraryItem(new LibraryItemInfo(), true, "", ok, fail); break;
                    case "copy-preset": fixture.Hub.CopyPreset("sandbox_presets", "a/b", "copy", ok, fail); break;
                    case "save-preset": fixture.Hub.SavePreset(new PresetInfo(), ok, fail); break;
                    case "remove-preset": fixture.Hub.RemovePreset("sandbox_presets", "a/b", ok, fail); break;
                    case "command": fixture.Hub.SaveCommand(new CommandInfo(), ok, fail); break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation));
                }
                var write = fixture.Requests[2];
                pending.Ok(Snapshot("stale", catalog));
                pending.Fail("stale");
                AssertEx.Equal("initial", fixture.Name(catalog), "write invalidates pending read: " + operation);
                write.Ok(JVal.Parse("{}"));
                var reload = fixture.Requests[3];
                fixture.Refresh(catalog);
                reload.Ok(Snapshot("stale", catalog));
                AssertEx.Equal(1, completed, "write completes despite superseded reload: " + operation);
                AssertEx.Equal("initial", fixture.Name(catalog), "superseded reload cannot replace snapshot");
                fixture.Requests[4].Ok(Snapshot("fresh", catalog));
                AssertEx.Equal("fresh", fixture.Name(catalog), "winning reload publishes snapshot");
                AssertEx.Equal(operation == "project" ? 1 : 0, fixture.Sessions, "only project save refreshes sessions");
                AssertEx.Equal(0, fixture.Errors, "no stale errors delivered");
            }
        }

        public static void SupersededPresetLoadsNotifyEveryCallerOnWinningReply()
        {
            using var fixture = new Fixture();
            int completed = 0;
            fixture.Hub.LoadPresets(() => completed++, _ => fixture.Errors++);
            var obsolete = fixture.Requests[0];
            fixture.Hub.LoadPresets(() => completed++, _ => fixture.Errors++);
            obsolete.Ok(Snapshot("stale", 2));
            obsolete.Fail("stale");
            AssertEx.Equal(0, completed, "superseded callbacks wait");
            fixture.Requests[1].Ok(Snapshot("current", 2));
            AssertEx.Equal(2, completed, "both callers see current catalog");
            AssertEx.Equal(0, fixture.Errors, "stale error ignored");
        }

        public static void ProjectDeletionInvalidatesGetAndRefreshesSessions()
        {
            using var fixture = new Fixture();
            fixture.Refresh(0);
            fixture.Requests[0].Ok(Snapshot("initial", 0));
            fixture.Refresh(0);
            var obsolete = fixture.Requests[1];
            fixture.Hub.RemoveProject("a/b", _ => fixture.Errors++);
            var deletion = fixture.Requests[2];
            obsolete.Ok(Snapshot("stale", 0));
            AssertEx.Equal("initial", fixture.Name(0), "delete invalidates pending GET");
            AssertEx.True(deletion.Path.EndsWith("/a%2Fb", StringComparison.Ordinal), "escaped project deletion path");
            deletion.Ok(JVal.Parse("{}"));
            AssertEx.Equal(1, fixture.Sessions, "deletion refreshes session membership");
            AssertEx.Equal(0, fixture.Errors, "successful deletion reports no failure");
        }
    }
}
