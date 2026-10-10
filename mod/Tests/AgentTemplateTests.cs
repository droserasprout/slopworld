using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class AgentTemplateTests
    {
        public static void CapturedDependenciesKeepBreadthFirstOrderAndDeduplicateCycles()
        {
            var catalog = SessionHub.Instance.Catalog;
            var presets = catalog.Presets;
            try
            {
                catalog.Presets = new List<PresetInfo>
                {
                    new PresetInfo { Name = "a", Description = "live" },
                    new PresetInfo { Name = "b", Requires = new List<string> { "c" } },
                };
                var template = new AgentTemplateInfo();
                template.DefaultsSnapshot.Command = new Wire.CommandPreset { Name = "agent", Sandbox = { "b", "a" } };
                template.DefaultsSnapshot.SandboxPresets.Add(new Wire.SandboxPreset { Name = "a", Description = "captured", Requires = { "c" } });
                template.DefaultsSnapshot.SandboxPresets.Add(new Wire.SandboxPreset { Name = "c", Requires = { "a" } });
                var form = new SessionInfo { Command = "agent", Sandbox = new List<string> { "a", "a" } };
                var saved = template.ToWire(form).Defaults;
                AssertEx.Equal("a,b,c", string.Join(",", saved.Sandbox), "explicit, command, then dependency order");
                AssertEx.Equal(3, saved.SandboxPresets.Count, "cycle and duplicates captured once");
                AssertEx.Equal("captured", saved.SandboxPresets[0].Description, "captured definition wins");
                saved.SandboxPresets[0].Description = "edited";
                AssertEx.Equal("captured", template.DefaultsSnapshot.SandboxPresets[0].Description, "captured snapshot is independent");
                form.Sandbox.Add("missing");
                AssertEx.Throws<System.InvalidOperationException>(() => template.ToWire(form), "unknown dependency is rejected");
            }
            finally { catalog.Presets = presets; }
        }

        public static void VersionTokensRemainExact()
        {
            var template = AgentTemplateInfo.FromWire(ProtobufFixtures.Read<Wire.AgentTemplate>(JVal.Parse("{\"version\":9007199254740993}")));
            AssertEx.Equal(9007199254740993L, template.Version, "version does not round through double");
        }

        public static void DisplayLabelUsesNameAndDescription()
        {
            var template = AgentTemplateInfo.FromWire(ProtobufFixtures.Read<Wire.AgentTemplate>(JVal.Parse(@"{
                ""name"":""reviewer"", ""description"":""Review changes""
            }")));
            AssertEx.Equal("reviewer  -  Review changes", template.DisplayLabel,
                "template labels do not include source agent metadata");
            var saved = JVal.Parse(template.ToJson(new SessionInfo()));
            AssertEx.True(saved["origin"].IsNull, "template writes no origin metadata");
        }

        static AgentTemplateInfo CapturedTemplate()
        {
            return AgentTemplateInfo.FromWire(ProtobufFixtures.Read<Wire.AgentTemplate>(JVal.Parse(@"{
                ""name"":""reviewer"", ""version"":1,
                ""defaults"":{
                    ""command"":{ ""name"":""agent"", ""cmd"":""captured"", ""sandbox"":[""dependency""] },
                    ""sandbox"":[],
                    ""sandbox_presets"":[{""name"":""dependency"", ""description"":""captured sandbox""}],
                    ""args"":""--extra"",
                    ""limits"":{ ""memory_mb"":512 }
                }
            }")));
        }

        public static void DescriptionEditsPreserveCapturedSnapshots()
        {
            var template = CapturedTemplate();
            var catalog = SessionHub.Instance.Catalog;
            var commands = catalog.Commands;
            try
            {
                catalog.Commands = new List<CommandInfo> { new CommandInfo { Name = "agent", Cmd = "changed" } };
                var form = new SessionInfo();
                template.ApplyTo(form);
                template.Description = "Edited description";
                var saved = template.ToWire(form);
                AssertEx.Equal("Edited description", saved.Description, "description edit is submitted");
                AssertEx.Equal("captured", saved.Defaults.Command.Cmd, "description edit retains command snapshot");
                AssertEx.Equal("captured sandbox", saved.Defaults.SandboxPresets[0].Description, "description edit retains sandbox snapshot");
                AssertEx.Equal("--extra", template.Copy().Args, "arguments survive draft copy");
                AssertEx.Equal("--extra", saved.Defaults.Args, "arguments retained on save");
            }
            finally { catalog.Commands = commands; }
        }

        public static void OptionalTemplateArgumentsAndLimitsCanBeCleared()
        {
            var template = CapturedTemplate();
            var form = new SessionInfo();
            template.ApplyTo(form);
            AssertEx.Equal("--extra", form.Args, "template arguments initialize form");
            form.Limits = new SessionLimits();
            form.Args = "";
            var saved = template.ToWire(form).Defaults;
            AssertEx.False(saved.HasArgs, "arguments can be cleared");
            AssertEx.True(saved.Limits == null || SessionLimits.FromWire(saved.Limits).IsEmpty, "cleared caps stay cleared");
        }

        public static void RawCommandsRemoveUnusedCapturedDependencies()
        {
            var template = CapturedTemplate();
            var form = new SessionInfo();
            template.ApplyTo(form);
            var saved = template.ToWire(form).Defaults;
            AssertEx.Equal("dependency", saved.Sandbox[0], "command dependency selected");
            AssertEx.Equal("dependency", saved.SandboxPresets[0].Name, "captured command dependency retained");
            form.Command = form.CommandPreset = "";
            form.Cmd = "custom";
            saved = template.ToWire(form).Defaults;
            AssertEx.True(saved.Command == null, "raw command clears preset snapshot");
            AssertEx.Equal(0, saved.SandboxPresets.Count, "unused dependency removed");
        }

        public static void SharedEditorUsesCapturedCatalogsAndSavesAgentSettings()
        {
            var template = AgentTemplateInfo.FromWire(ProtobufFixtures.Read<Wire.AgentTemplate>(JVal.Parse(@"{
                ""defaults"": {
                    ""command"": { ""name"":""agent"", ""cmd"":""captured"" },
                    ""sandbox_presets"": [{ ""name"":""sandbox"", ""description"":""captured"" }],
                    ""network"":""private"", ""dns"": { ""mode"":""resolved"" }
                }
            }")));
            var catalog = SessionHub.Instance.Catalog;
            var commands = catalog.Commands;
            var presets = catalog.Presets;
            try
            {
                catalog.Commands = new List<CommandInfo> { new CommandInfo { Name = "agent", Cmd = "live" } };
                catalog.Presets = new List<PresetInfo> { new PresetInfo { Name = "sandbox", Description = "live" } };
                AssertEx.Equal("captured", template.ResolveCommand("agent").Cmd, "editor uses captured command");
                AssertEx.Equal("captured", template.SandboxCatalog()[0].Description, "picker uses captured sandbox");
                AssertEx.Equal(1, template.SandboxCatalog().Count, "no duplicate sandbox choices");
                var form = new SessionInfo();
                template.ApplyTo(form);
                form.Network = NetworkMode.None;
                form.Dns = DnsConfig.Custom();
                form.Dns.Servers.Add("1.1.1.1");
                template.Description = "Edited description";
                var saved = JVal.Parse(template.ToJson(form))["defaults"];
                AssertEx.Equal("none", saved["network"].AsString(), "shared network override is saved");
                AssertEx.Equal("1.1.1.1", saved["dns"]["servers"][0].AsString(), "shared DNS override is saved");
            }
            finally
            {
                catalog.Commands = commands;
                catalog.Presets = presets;
            }
        }

        public static void SparseRecipesRoundTripInheritanceAndPreserveExplicitValues()
        {
            var sparse = new AgentTemplateInfo { Name = "sparse" };
            var form = new SessionInfo();
            sparse.ApplyTo(form);
            var json = JVal.Parse(sparse.ToJson(form))["defaults"];
            AssertEx.True(json["network"].IsNull, "new recipe inherits network");
            AssertEx.True(json["dns"].IsNull, "new recipe inherits DNS");
            AssertEx.True(json["autostart"].IsNull, "new recipe leaves startup unspecified");
            var explicitValues = AgentTemplateInfo.FromWire(ProtobufFixtures.Read<Wire.AgentTemplate>(JVal.Parse(@"{
                ""name"":""explicitValues"", ""defaults"": { ""network"":""host"",
                    ""dns"": { ""mode"":""resolved"" }, ""autostart"":false }
            }")));
            explicitValues.Copy().ApplyTo(form);
            json = JVal.Parse(explicitValues.ToJson(form))["defaults"];
            AssertEx.Equal("host", json["network"].AsString(), "existing explicit network remains pinned");
            AssertEx.True(!json["autostart"].IsNull && !json["autostart"].AsBool(), "explicit false stays explicit");
            explicitValues.SpecifiedFlags.Remove("autostart");
            explicitValues.NetworkSpecified = false;
            explicitValues.DnsSpecified = false;
            json = JVal.Parse(explicitValues.ToJson(form))["defaults"];
            AssertEx.True(json["network"].IsNull && json["dns"].IsNull && json["autostart"].IsNull,
                "inherit controls clear pinned values");
        }

        public static void SupersededTemplateLoadsSettleWithoutReplacingCatalog()
        {
            var requests = DaemonClient.Requests;
            requests.Clear();
            try
            {
                var catalog = new HubCatalog(() => { });
                int loaded = 0;
                catalog.RefreshTemplates(loaded: () => loaded++);
                var stale = requests[0];
                catalog.RefreshTemplates();
                requests[1].Ok(JVal.Parse("{\"templates\":[{\"name\":\"new\"}]}"));
                stale.Ok(JVal.Parse("{\"templates\":[{\"name\":\"stale\"}]}"));
                AssertEx.Equal(1, loaded, "superseded page leaves loading state");
                AssertEx.Equal("new", catalog.Templates[0].Name, "stale response does not replace catalog");
            }
            finally { requests.Clear(); }
        }
    }
}
