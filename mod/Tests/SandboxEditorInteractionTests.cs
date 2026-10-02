using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld.Tests
{
    static class SandboxEditorInteractionTests
    {
        enum EditorKind { Preset, Command }

        static void Run(Action action)
        {
            var hub = SessionHub.Instance;
            var requests = DaemonClient.Requests.ToArray();
            DaemonClient.Requests.Clear();
            SessionHub.Instance = new SessionHub();
            try { action(); }
            finally
            {
                SessionHub.Instance = hub;
                EditorTrace.Draws.Clear();
                EditorTrace.Edits.Clear();
                EditorTrace.FieldValues.Clear();
                EditorTrace.Checks.Clear();
                EditorTrace.Clicks.Clear();
                EditorTrace.PickRow = EditorTrace.PickOption = EditorTrace.EditValue = null;
                DaemonClient.Requests.Clear();
                DaemonClient.Requests.AddRange(requests);
                Verse.Find.WindowStack.Clear();
            }
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (EditorKind editor in new[] { EditorKind.Preset, EditorKind.Command })
            {
                string kind = editor == EditorKind.Command ? "command" : "preset";
                yield return (kind + " save reports failure and settles successful retry", () => Run(() => Save(editor)));
                yield return (kind + " system copy reports failure and reloads after success", () => Run(() => Copy(editor)));
                foreach (string source in new[] { "user", "override" })
                    yield return (kind + " " + source + " removal waits for confirmation", () => Run(() => Remove(editor, source)));
            }
        }

        static void Draw(SandboxPage page, EditorKind editor, string source)
        {
            bool command = editor == EditorKind.Command;
            if (command) page.TestCommandHost(new CommandInfo { Name = "name with space", Source = source, Cmd = "bash -l" }, 300);
            else page.TestPresetHost(new PresetInfo { Name = "name with space", Source = source, Tmux = true }, 300);
        }

        static string RouteKind(EditorKind editor) => editor == EditorKind.Command ? "app_presets" : "sandbox_presets";

        static void Save(EditorKind editor)
        {
            bool command = editor == EditorKind.Command;
            var page = new SandboxPage { TestNewEntry = true };
            EditorTrace.Clicks.Add("Save");
            Draw(page, editor, "user");
            var request = DaemonClient.Requests.Single();
            AssertEx.Equal("PUT", request.Method, "save uses catalog write");
            AssertEx.True(request.Path.EndsWith("/" + RouteKind(editor) + "/name%20with%20space", StringComparison.Ordinal), "identity is escaped in route");
            var body = (Wire.PresetRequest)request.Body;
            if (command) AssertEx.Equal("bash -l", body.Command.Cmd, "command payload retained");
            else AssertEx.True(body.Sandbox.Tmux, "generated bind setting retained");
            request.Fail("save rejected");
            AssertEx.Equal("save rejected", page.TestError, "failed save remains visible");
            AssertEx.True(page.TestNewEntry, "failure leaves new entry editable");
            page.TestFooter();
            AssertEx.True(EditorTrace.Draws.Any(d => d.Name == "save rejected"), "footer displays failure");
            EditorTrace.Clicks.Add("Save");
            Draw(page, editor, "user");
            DaemonClient.Requests.Last().Ok(JVal.Parse("{}"));
            AssertEx.Equal<string>(null, page.TestError, "successful retry clears error");
            AssertEx.False(page.TestNewEntry, "successful save settles new identity");
            AssertEx.Equal("GET", DaemonClient.Requests.Last().Method, "save refreshes catalog");
            AssertEx.Equal(0, page.TestLoads, "save does not reload the page draft");
        }

        static void Copy(EditorKind editor)
        {
            var page = new SandboxPage();
            EditorTrace.Clicks.Add("Copy to user");
            Draw(page, editor, "system");
            var request = DaemonClient.Requests.Single();
            AssertEx.Equal("POST", request.Method, "copy creates an override");
            AssertEx.True(request.Path.EndsWith("/" + RouteKind(editor) + "/name%20with%20space/copy", StringComparison.Ordinal), "copy uses escaped catalog identity");
            AssertEx.Equal("name with space", ((Wire.CopyPresetReq)request.Body).Name, "copy retains original name");
            request.Fail("copy rejected");
            AssertEx.Equal("copy rejected", page.TestError, "copy failure reported");
            AssertEx.Equal(0, page.TestLoads, "failed copy leaves draft alone");
            EditorTrace.Clicks.Add("Copy to user");
            Draw(page, editor, "system");
            DaemonClient.Requests.Last().Ok(JVal.Parse("{}"));
            AssertEx.Equal(1, page.TestLoads, "successful copy reloads selection");
            AssertEx.Equal<string>(null, page.TestError, "successful copy clears error");
        }

        static void Remove(EditorKind editor, string source)
        {
            bool command = editor == EditorKind.Command;
            var page = new SandboxPage();
            SessionHub.Instance.Presets.Add(new PresetInfo { Name = "name with space", Source = source });
            string button = source == "override" ? "Reset to system" : "Remove";
            EditorTrace.Clicks.Add(button);
            Draw(page, editor, source);
            AssertEx.Equal(0, DaemonClient.Requests.Count, "opening confirmation does not delete");
            var prompt = (ConfirmDialog.Prompt)Verse.Find.WindowStack.Single();
            AssertEx.Equal(!command && source == "override"
                ? "Reset this user override and return to the system preset?" : "Remove this user preset?",
                prompt.Text, "confirmation describes requested operation");
            AssertEx.True(prompt.Destructive, "removal uses destructive confirmation");
            prompt.Confirm();
            var request = DaemonClient.Requests.Single();
            AssertEx.Equal("DELETE", request.Method, "confirmed removal reaches catalog");
            AssertEx.True(request.Path.EndsWith("/" + RouteKind(editor) + "/name%20with%20space", StringComparison.Ordinal), "delete targets selected entry");
            request.Fail("still in use");
            AssertEx.Equal("still in use", page.TestError, "failed removal reported");
            AssertEx.True(command ? page.TestSelectedCommand != null : page.TestSelectedPreset != null, "failed removal retains selection");
            prompt.Confirm();
            DaemonClient.Requests.Last().Ok(JVal.Parse("{}"));
            AssertEx.True(page.TestSelectedPreset == null && page.TestSelectedCommand == null, "successful removal clears selection");
            AssertEx.Equal<string>(null, page.TestError, "successful removal clears error");
            AssertEx.Equal(1, page.TestLoads, "successful removal reloads page");
        }

        public static void PresetLibraryGroupsSortAndSelectEntries() => Run(() =>
        {
            var presets = SessionHub.Instance.Presets;
            presets.Add(new PresetInfo { Name = "zebra", Source = "user" });
            presets.Add(new PresetInfo { Name = "python-cache", Source = "system", Requires = new List<string> { "python" } });
            presets.Add(new PresetInfo { Name = "python", Source = "system", Description = "Interpreter" });
            presets.Add(new PresetInfo { Name = "global", Source = "system" });
            presets.Add(new PresetInfo { Name = "Alpha", Source = "override", Escapes = "host access" });
            presets.Add(new PresetInfo { Name = "global", Source = "user" });
            presets.Add(new PresetInfo { Name = "unrelated-cache", Source = "user" });
            var page = new SandboxPage { TestNewEntry = true };
            EditorTrace.PickRow = "Alpha  (override)";
            page.TestPresetList(300);
            AssertEx.Sequence(new[] { "User", "global", "Alpha  (override)", "unrelated-cache", "zebra", "System", "global", "python", "python-cache" },
                EditorTrace.Draws.Where(d => d.Name != "scroll").Select(d => d.Name), "groups sort global first then names ignoring case");
            AssertEx.Equal("Alpha", page.TestSelectedPreset.Name, "clicked row becomes editor selection");
            AssertEx.False(page.TestNewEntry, "selection exits new-entry mode");
            AssertEx.Equal(UiTheme.GapM, EditorTrace.Draws.Single(d => d.Name == "python-cache").Rect.x, "dependent cache indented");
            AssertEx.Equal(UiTheme.GapS, EditorTrace.Draws.Single(d => d.Name == "unrelated-cache").Rect.x, "suffix alone does not imply dependency");
        });

        public static void EmptyLibrariesAndEditorsOfferActions() => Run(() =>
        {
            var page = new SandboxPage();
            page.TestPresetList(300);
            AssertEx.Equal(2, EditorTrace.Draws.Count(d => d.Name == "(none)"), "both empty preset groups explained");
            EditorTrace.Draws.Clear();
            EditorTrace.Clicks.Add("+ New command");
            page.TestCommandList(300);
            AssertEx.Equal(2, EditorTrace.Draws.Count(d => d.Name == "(none)"), "both empty command groups explained");
            AssertEx.Equal(1, page.TestNewCommands, "new command action dispatched");
            var commandView = EditorTrace.Draws.Single(d => d.Name == "scroll").Rect;
            var newCommand = EditorTrace.Draws.Single(d => d.Name == "+ New command").Rect;
            AssertEx.True(newCommand.yMax <= commandView.yMax,
                "empty command groups and creation button fit inside scroll extent");
            page.TestPresetHost(null, 300);
            page.TestCommandHost(null, 300);
            AssertEx.True(EditorTrace.Draws.Any(d => d.Name == "Select a preset to inspect or edit it."), "empty preset editor explained");
            AssertEx.True(EditorTrace.Draws.Any(d => d.Name == "Select a command to inspect or edit it."), "empty command editor explained");
            EditorTrace.Clicks.Add("Reload");
            page.TestFooter();
            AssertEx.Equal(1, page.TestLoads, "footer reload dispatched");
        });

        public static void CommandLibraryPreservesCatalogOrderAndSelection() => Run(() =>
        {
            var commands = SessionHub.Instance.Commands;
            commands.Add(new CommandInfo { Name = "zsh", Source = "system", Description = "Shell" });
            commands.Add(new CommandInfo { Name = "agent", Source = "system" });
            commands.Add(new CommandInfo { Name = "custom", Source = "override" });
            var page = new SandboxPage { TestNewEntry = true };
            EditorTrace.PickRow = "custom  (override)";
            page.TestCommandList(300);
            AssertEx.Sequence(new[] { "System", "zsh", "agent", "User", "custom  (override)", "+ New command" },
                EditorTrace.Draws.Where(d => d.Name != "scroll").Select(d => d.Name), "commands retain daemon catalog order within groups");
            AssertEx.Equal(commands[2], page.TestSelectedCommand, "clicked command selected");
            AssertEx.False(page.TestNewEntry, "selection exits new command mode");
        });

        public static void PresetFieldsNormalizeEditsAndSwitchEnvironmentOwner() => Run(() =>
        {
            var page = new SandboxPage();
            var preset = new PresetInfo { Name = "old", Source = "user" };
            EditorTrace.Edits["preset.name"] = "new";
            EditorTrace.Edits["preset.ro"] = " /first \n\n /second \n";
            EditorTrace.Edits["preset.setenv"] = " MODE =first\ninvalid\n=ignored\nMODE=last=value\nEMPTY=\n";
            page.TestPreset(preset, 300, true, isNew: true);
            AssertEx.Equal("new", preset.Name, "new identity editable");
            AssertEx.Sequence(new[] { "/first", "/second" }, preset.Ro, "path edits trim lines and skip blanks");
            AssertEx.Equal(2, preset.Setenv.Count, "invalid environment rows ignored");
            AssertEx.Equal("last=value", preset.Setenv["MODE"], "last duplicate wins and additional equals retained");
            AssertEx.Equal("", preset.Setenv["EMPTY"], "empty environment value retained");
            EditorTrace.Edits.Clear();
            page.TestPreset(preset, 300, true);
            AssertEx.True(EditorTrace.FieldValues["preset.setenv"].Contains("invalid"), "raw edit text retained while editing same owner");
            var other = new PresetInfo { Source = "user" };
            other.Setenv["OTHER"] = "value";
            page.TestPreset(other, 300, true);
            AssertEx.Equal("OTHER=value", EditorTrace.FieldValues["preset.setenv"], "switching owner replaces raw environment draft");
            AssertEx.Equal("last=value", preset.Setenv["MODE"], "switching selection does not modify prior entry");
        });

        public static void ReadOnlyAndUnchangedFieldsDoNotRewriteCatalogData() => Run(() =>
        {
            foreach (string source in new[] { "system", "user" })
            {
                var preset = new PresetInfo { Name = "fixed", Source = source };
                preset.Ro.Add(" /literal path ");
                preset.Setenv["PADDED"] = " value ";
                var before = preset.ToWire();
                new SandboxPage().TestPreset(preset, 300, true);
                AssertEx.Equal(before, preset.ToWire(), "drawing without edits preserves literal values for " + source);
            }
        });

        public static void CommandKindAndDependenciesRespectEditability() => Run(() =>
        {
            SessionHub.Instance.Presets.Add(new PresetInfo { Name = "global" });
            SessionHub.Instance.Presets.Add(new PresetInfo { Name = "one" });
            SessionHub.Instance.Presets.Add(new PresetInfo { Name = "two", Escapes = "host" });
            var command = new CommandInfo { Name = "fixed", Source = "user", Sandbox = new List<string> { "one" } };
            var page = new SandboxPage();
            EditorTrace.Edits["command.name"] = "renamed";
            EditorTrace.Edits["command.cmd"] = "bash -l";
            EditorTrace.Checks["one"] = false;
            EditorTrace.Checks["two"] = true;
            EditorTrace.PickOption = "Shell";
            page.TestCommand(command, 300, true);
            AssertEx.Equal("fixed", command.Name, "existing identity remains fixed");
            AssertEx.Equal("bash -l", command.Cmd, "command line editable");
            AssertEx.Equal(CommandInfo.ShellKind, command.Kind, "kind selection applied");
            AssertEx.Sequence(new[] { "two" }, command.Sandbox, "dependency additions and removals applied");
            AssertEx.False(EditorTrace.Draws.Any(d => d.Name == "check:global"), "global cannot become an explicit dependency");
            EditorTrace.PickOption = "Agent";
            page.TestCommand(command, 300, true);
            AssertEx.Equal(CommandInfo.AgentKind, command.Kind, "kind can switch back");
            command.Source = "system";
            EditorTrace.PickOption = "Shell";
            EditorTrace.Checks["one"] = true;
            EditorTrace.Checks["two"] = false;
            EditorTrace.Edits["command.cmd"] = "forbidden";
            page.TestCommand(command, 300, true);
            AssertEx.Equal(CommandInfo.AgentKind, command.Kind, "system kind read-only");
            AssertEx.Equal("bash -l", command.Cmd, "system command read-only");
            AssertEx.Sequence(new[] { "two" }, command.Sandbox, "system dependencies read-only");
        });
    }
}
