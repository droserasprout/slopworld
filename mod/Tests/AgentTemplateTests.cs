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
