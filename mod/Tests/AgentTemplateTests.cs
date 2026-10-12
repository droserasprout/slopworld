using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class AgentTemplateTests
    {
        public static void CapturedDependenciesKeepBreadthFirstOrderAndDeduplicateCycles()
        {
            var presets = new List<PresetInfo>
                {
                    new PresetInfo { Name = "a", Description = "live" },
                    new PresetInfo { Name = "b", Requires = new List<string> { "c" } },
                };
            var template = new AgentTemplateInfo();
            template.DefaultsSnapshot.Command = new Wire.CommandPreset { Name = "agent", Sandbox = { "b", "a" } };
            template.DefaultsSnapshot.SandboxPresets.Add(new Wire.SandboxPreset { Name = "a", Description = "captured", Requires = { "c" } });
            template.DefaultsSnapshot.SandboxPresets.Add(new Wire.SandboxPreset { Name = "c", Requires = { "a" } });
            var form = new SessionInfo { Command = "agent", Sandbox = new List<string> { "a", "a" } };
            var saved = template.ToWire(form, System.Array.Empty<CommandInfo>(), presets).Defaults;
            AssertEx.Equal("a,b,c", string.Join(",", saved.Sandbox), "explicit, command, then dependency order");
            AssertEx.Equal(3, saved.SandboxPresets.Count, "cycle and duplicates captured once");
            AssertEx.Equal("captured", saved.SandboxPresets[0].Description, "captured definition wins");
            saved.SandboxPresets[0].Description = "edited";
            AssertEx.Equal("captured", template.DefaultsSnapshot.SandboxPresets[0].Description, "captured snapshot is independent");
            form.Sandbox.Add("missing");
            AssertEx.Throws<System.InvalidOperationException>(() => template.ToWire(form, System.Array.Empty<CommandInfo>(), presets), "unknown dependency is rejected");
        }

        public static void ExplicitCatalogsResolveLiveFallbacksWithoutChangingCapturedSnapshots()
        {
            var template = new AgentTemplateInfo();
            var commands = new[] { new CommandInfo { Name = "live", Cmd = "run", Sandbox = new List<string> { "base" } } };
            var presets = new[] { new PresetInfo { Name = "base", Description = "live preset" } };
            var form = new SessionInfo { Command = "live" };
            AssertEx.Equal("run", template.ResolveCommand("live", commands).Cmd, "supplied command is used");
            var saved = template.ToWire(form, commands, presets);
            AssertEx.Equal("live preset", saved.Defaults.SandboxPresets[0].Description, "supplied dependency is captured");
            AssertEx.True(template.DefaultsSnapshot.Command == null, "saving does not mutate the template snapshot");
            AssertEx.Equal(0, template.DefaultsSnapshot.SandboxPresets.Count, "live presets do not enter the draft snapshot");
            AssertEx.Throws<System.InvalidOperationException>(() => template.ToWire(form, System.Array.Empty<CommandInfo>(), presets), "missing live command is rejected");
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
            var commands = new List<CommandInfo> { new CommandInfo { Name = "agent", Cmd = "changed" } };
            var form = new SessionInfo();
            template.ApplyTo(form);
            template.Description = "Edited description";
            var saved = template.ToWire(form, commands, System.Array.Empty<PresetInfo>());
            AssertEx.Equal("Edited description", saved.Description, "description edit is submitted");
            AssertEx.Equal("captured", saved.Defaults.Command.Cmd, "description edit retains command snapshot");
            AssertEx.Equal("captured sandbox", saved.Defaults.SandboxPresets[0].Description, "description edit retains sandbox snapshot");
            AssertEx.Equal("--extra", template.Copy().Args, "arguments survive draft copy");
            AssertEx.Equal("--extra", saved.Defaults.Args, "arguments retained on save");
        }

        public static void OptionalTemplateArgumentsAndLimitsCanBeCleared()
        {
            var template = CapturedTemplate();
            var form = new SessionInfo();
            template.ApplyTo(form);
            AssertEx.Equal("--extra", form.Args, "template arguments initialize form");
            form.Limits = new SessionLimits();
            form.Args = "";
            var saved = template.ToWire(form, System.Array.Empty<CommandInfo>(), System.Array.Empty<PresetInfo>()).Defaults;
            AssertEx.False(saved.HasArgs, "arguments can be cleared");
            AssertEx.True(saved.Limits == null || SessionLimits.FromWire(saved.Limits).IsEmpty, "cleared caps stay cleared");
        }

        public static void RawCommandsRemoveUnusedCapturedDependencies()
        {
            var template = CapturedTemplate();
            var form = new SessionInfo();
            template.ApplyTo(form);
            var saved = template.ToWire(form, System.Array.Empty<CommandInfo>(), System.Array.Empty<PresetInfo>()).Defaults;
            AssertEx.Equal("dependency", saved.Sandbox[0], "command dependency selected");
            AssertEx.Equal("dependency", saved.SandboxPresets[0].Name, "captured command dependency retained");
            form.Command = form.CommandPreset = "";
            form.Cmd = "custom";
            saved = template.ToWire(form, System.Array.Empty<CommandInfo>(), System.Array.Empty<PresetInfo>()).Defaults;
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
            var commands = new List<CommandInfo> { new CommandInfo { Name = "agent", Cmd = "live" } };
            var presets = new List<PresetInfo> { new PresetInfo { Name = "sandbox", Description = "live" } };
            AssertEx.Equal("captured", template.ResolveCommand("agent", commands).Cmd, "editor uses captured command");
            AssertEx.Equal("captured", template.SandboxCatalog(presets)[0].Description, "picker uses captured sandbox");
            AssertEx.Equal(1, template.SandboxCatalog(presets).Count, "no duplicate sandbox choices");
            var form = new SessionInfo();
            template.ApplyTo(form);
            form.Network = NetworkMode.None;
            form.Dns = DnsConfig.Custom();
            form.Dns.Servers.Add("1.1.1.1");
            template.Description = "Edited description";
            var saved = ProtobufFixtures.Json(template.ToWire(form, commands, presets))["defaults"];
            AssertEx.Equal("none", saved["network"].AsString(), "shared network override is saved");
            AssertEx.Equal("1.1.1.1", saved["dns"]["servers"][0].AsString(), "shared DNS override is saved");
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
                var catalog = new HubCatalog(() => { }, _ => { });
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
