using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class AgentTemplateTests
    {
        public static void VersionTokensRemainExact()
        {
            var template = AgentTemplateInfo.FromJson(JVal.Parse("{\"version\":9007199254740991}"));
            AssertEx.Equal(9007199254740991L, template.Version, "version does not round through double");
        }

        public static void EditingPreservesSnapshotsAndClearsLimits()
        {
            var template = AgentTemplateInfo.FromJson(JVal.Parse(@"{
                ""name"":""reviewer"", ""version"":1,
                ""defaults"":{
                    ""command"":{ ""name"":""agent"", ""cmd"":""captured"", ""sandbox"":[""dependency""] },
                    ""sandbox"":[],
                    ""sandbox_presets"":[{""name"":""dependency"", ""description"":""captured sandbox""}],
                    ""prompts"":[{""name"":""first"",""text"":""one""},{""name"":""second"",""text"":""two""}],
                    ""limits"":{ ""memory_mb"":512 }
                }
            }"));
            var catalog = SessionHub.Instance.Catalog;
            var commands = catalog.Commands;
            catalog.Commands = new List<CommandInfo> { new CommandInfo { Name = "agent", Cmd = "changed" } };
            try
            {
                var form = new SessionInfo();
                template.ApplyTo(form);
                form.Limits = new SessionLimits();
                form.Breadcrumbs = new List<string> { "second", "first" };
                var saved = JVal.Parse(template.ToJson(form))["defaults"];
                AssertEx.Equal("captured", saved["command"]["cmd"].AsString(), "description edit retains command snapshot");
                AssertEx.True(saved["limits"]["memory_mb"].IsNull, "cleared cap stays cleared");
                AssertEx.Equal("dependency", saved["sandbox"][0].AsString(), "command dependency selected");
                AssertEx.Equal("dependency", saved["sandbox_presets"][0]["name"].AsString(), "command dependencies retained");
                AssertEx.Equal("second", saved["prompts"][0]["name"].AsString(), "prompt selection order retained");
                AssertEx.Equal("two", saved["prompts"][0]["text"].AsString(), "prompt contents retained");
                form.Command = form.CommandPreset = "";
                form.Cmd = "custom";
                saved = JVal.Parse(template.ToJson(form))["defaults"];
                AssertEx.True(saved["command"].IsNull, "raw command clears preset");
                AssertEx.Equal(0, saved["sandbox_presets"].Count, "unused dependency removed");
                form.Breadcrumbs.Add("missing");
                bool rejected = false;
                try { template.ToJson(form); }
                catch (System.InvalidOperationException) { rejected = true; }
                AssertEx.True(rejected, "unknown prompts are not silently dropped");
            }
            finally { catalog.Commands = commands; }
        }

        public static void SharedEditorUsesCapturedCatalogsAndSavesNetworkOverrides()
        {
            var template = AgentTemplateInfo.FromJson(JVal.Parse(@"{
                ""defaults"": {
                    ""command"": { ""name"":""agent"", ""cmd"":""captured"" },
                    ""sandbox_presets"": [{ ""name"":""sandbox"", ""description"":""captured"" }],
                    ""prompts"": [{ ""name"":""prompt"", ""text"":""captured"" }]
                }
            }"));
            var catalog = SessionHub.Instance.Catalog;
            var commands = catalog.Commands;
            var presets = catalog.Presets;
            var library = catalog.Library;
            try
            {
                catalog.Commands = new List<CommandInfo> { new CommandInfo { Name = "agent", Cmd = "live" } };
                catalog.Presets = new List<PresetInfo> { new PresetInfo { Name = "sandbox", Description = "live" } };
                catalog.Library = new List<LibraryItemInfo> { new LibraryItemInfo { Name = "prompt", Text = "live" } };
                AssertEx.Equal("captured", template.ResolveCommand("agent").Cmd, "editor uses captured command");
                AssertEx.Equal("captured", template.SandboxCatalog()[0].Description, "picker uses captured sandbox");
                AssertEx.Equal(1, template.SandboxCatalog().Count, "no duplicate sandbox choices");
                AssertEx.Equal("captured", template.BreadcrumbCatalog()[0].Text, "picker uses captured prompt");
                AssertEx.Equal(1, template.BreadcrumbCatalog().Count, "no duplicate prompt choices");
                var form = new SessionInfo();
                template.ApplyTo(form);
                form.NetworkOverride = NetworkMode.None;
                form.DnsOverride = DnsConfig.Custom();
                form.DnsOverride.Servers.Add("1.1.1.1");
                var saved = JVal.Parse(template.ToJson(form))["defaults"];
                AssertEx.Equal("none", saved["network"].AsString(), "shared network override is saved");
                AssertEx.Equal("1.1.1.1", saved["dns"]["servers"][0].AsString(), "shared DNS override is saved");
            }
            finally
            {
                catalog.Commands = commands;
                catalog.Presets = presets;
                catalog.Library = library;
            }
        }

        public static void SparseRecipesRoundTripInheritanceAndPreserveExplicitLegacyValues()
        {
            var sparse = new AgentTemplateInfo { Name = "sparse" };
            var form = new SessionInfo();
            sparse.ApplyTo(form);
            var json = JVal.Parse(sparse.ToJson(form))["defaults"];
            AssertEx.True(json["network"].IsNull, "new recipe inherits network");
            AssertEx.True(json["dns"].IsNull, "new recipe inherits DNS");
            AssertEx.True(json["autostart"].IsNull, "new recipe leaves startup unspecified");
            var legacy = AgentTemplateInfo.FromJson(JVal.Parse(@"{
                ""name"":""legacy"", ""defaults"": { ""network"":""host"",
                    ""dns"": { ""mode"":""resolved"" }, ""autostart"":false }
            }"));
            legacy.Copy().ApplyTo(form);
            json = JVal.Parse(legacy.ToJson(form))["defaults"];
            AssertEx.Equal("host", json["network"].AsString(), "existing explicit network remains pinned");
            AssertEx.True(!json["autostart"].IsNull && !json["autostart"].AsBool(), "explicit false stays explicit");
            legacy.SpecifiedFlags.Remove("autostart");
            form.NetworkOverride = null;
            form.DnsOverride = null;
            json = JVal.Parse(legacy.ToJson(form))["defaults"];
            AssertEx.True(json["network"].IsNull && json["dns"].IsNull && json["autostart"].IsNull,
                "inherit controls clear pinned values");
        }

        public static void SupersededTemplateLoadsSettleWithoutReplacingCatalog()
        {
            var requests = DaemonClient.Requests;
            requests.Clear();
            var catalog = new HubCatalog(() => { });
            int loaded = 0;
            catalog.RefreshTemplates(loaded: () => loaded++);
            var stale = requests[0];
            catalog.RefreshTemplates();
            requests[1].Ok(JVal.Parse("{\"templates\":[{\"name\":\"new\"}]}"));
            stale.Ok(JVal.Parse("{\"templates\":[{\"name\":\"stale\"}]}"));
            AssertEx.Equal(1, loaded, "superseded page leaves loading state");
            AssertEx.Equal("new", catalog.Templates[0].Name, "stale response does not replace catalog");
            requests.Clear();
        }
    }
}
