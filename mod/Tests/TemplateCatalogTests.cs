using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class TemplateCatalogTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (string operation in new[] { "capture", "duplicate", "create", "edit", "remove", "remove-original" })
            {
                string kind = operation;
                yield return ($"template {kind} invalidates pending reads and reloads after success", () => Write(kind, false));
                yield return ($"template {kind} failure retains the catalog and reports the error", () => Write(kind, true));
            }
        }

        static void Write(string kind, bool failure)
        {
            var requests = DaemonClient.Requests;
            requests.Clear();
            try
            {
                var catalog = new HubCatalog(() => { });
                var retained = new AgentTemplateInfo { Name = "retained" };
                catalog.Templates.Add(retained);
                int completed = 0, settled = 0;
                string error = null;
                Action ok = () => completed++;
                Action<string> fail = message => error = message;
                catalog.RefreshTemplates(fail, () => settled++);
                var pending = requests[0];
                int revision = catalog.TemplatesRevision;
                string settingsRevision = catalog.SettingsRevision;
                var template = new AgentTemplateInfo { Name = "new/name", Description = "description", Version = 42 };
                var form = new SessionInfo { Cmd = "echo hello" };
                switch (kind)
                {
                    case "capture": catalog.SaveAgentTemplate("source-agent", template.Name, template.Description, ok, fail); break;
                    case "duplicate": catalog.DuplicateAgentTemplate("source-template", template.Name, template.Description, ok, fail); break;
                    case "create": catalog.SaveAgentTemplateForm(template, form, true, null, ok, fail); break;
                    case "edit": catalog.SaveAgentTemplateForm(template, form, false, "old/name", ok, fail); break;
                    case "remove": catalog.RemoveAgentTemplate(template, null, ok, fail); break;
                    default: catalog.RemoveAgentTemplate(template, "old/name", ok, fail); break;
                }
                var write = requests[1];
                AssertEx.Equal(revision + 1, catalog.TemplatesRevision, "write invalidates template reads");
                AssertEx.False(settingsRevision == catalog.SettingsRevision, "settings previews observe catalog mutation");
                pending.Ok(JVal.Parse("{\"templates\":[{\"name\":\"stale\"}]}"));
                AssertEx.Equal(0, settled, "superseded editor waits for the winning outcome");
                AssertEx.True(ReferenceEquals(retained, catalog.Templates[0]), "stale reply cannot replace retained catalog");
                string root = WireProtocol.Routes.Templates;
                AssertEx.Equal(kind == "edit" ? "PUT" : kind.StartsWith("remove") ? "DELETE" : "POST",
                    write.Method, "HTTP verb matches operation");
                string path = kind == "edit" ? root + "/old%2Fname" : kind.StartsWith("remove")
                    ? root + (kind == "remove" ? "/new%2Fname" : "/old%2Fname") + "?" + WireProtocol.TemplateVersionQuery + "=42" : root;
                AssertEx.Equal(path, write.Path, "original identity is escaped and delete carries version");
                if (write.Body is Wire.SaveTemplateRequest capture)
                {
                    AssertEx.Equal(template.Name, capture.Name, "new name is sent");
                    AssertEx.Equal(template.Description, capture.Description, "description is sent");
                    AssertEx.Equal(kind == "capture" ? "source-agent" : "", capture.Source, "capture source is scoped to capture");
                    AssertEx.Equal(kind == "duplicate" ? "source-template" : "", capture.Duplicate, "duplicate source is scoped to duplicate");
                    if (kind == "create")
                    {
                        AssertEx.Equal(42UL, capture.Version, "form version survives request conversion");
                        AssertEx.Equal("echo hello", capture.Defaults.Cmd, "form defaults are submitted");
                    }
                }
                if (write.Body is Wire.AgentTemplate edit)
                {
                    AssertEx.Equal(template.Name, edit.Name, "rename submits new identity");
                    AssertEx.Equal(42UL, edit.Version, "edit preserves compare-and-swap version");
                    AssertEx.Equal("echo hello", edit.Defaults.Cmd, "edit submits form defaults");
                }
                if (failure)
                {
                    write.Fail("version conflict");
                    AssertEx.Equal("version conflict", error, "write error reaches caller");
                    AssertEx.Equal(0, completed, "failure does not signal success");
                    AssertEx.Equal(2, requests.Count, "failure does not reload");
                    AssertEx.True(ReferenceEquals(retained, catalog.Templates[0]), "failure preserves displayed catalog");
                }
                else
                {
                    write.Ok(JVal.Parse("{}"));
                    AssertEx.Equal(1, completed, "successful write completes once");
                    AssertEx.Equal(3, requests.Count, "successful write reloads templates");
                    AssertEx.Equal("GET", requests[2].Method, "reload reads catalog");
                    AssertEx.Equal(root, requests[2].Path, "reload uses template route");
                    requests[2].Ok(JVal.Parse("{\"templates\":[{\"name\":\"fresh\"}]}"));
                    AssertEx.Equal("fresh", catalog.Templates[0].Name, "fresh snapshot replaces catalog");
                    AssertEx.Equal(1, settled, "reload settles superseded editor");
                    AssertEx.Equal<string>(null, error, "successful write reports no error");
                }
            }
            finally { requests.Clear(); }
        }

        public static void InvalidFormDoesNotInvalidatePendingCatalogRead()
        {
            var requests = DaemonClient.Requests;
            requests.Clear();
            try
            {
                var catalog = new HubCatalog(() => { });
                catalog.RefreshTemplates();
                int revision = catalog.TemplatesRevision;
                string error = null;
                catalog.SaveAgentTemplateForm(new AgentTemplateInfo(),
                    new SessionInfo { Command = "missing-command-for-validation-test" }, false, "original",
                    () => throw new Exception("invalid form succeeded"), message => error = message);
                AssertEx.Equal("Unknown command: missing-command-for-validation-test", error, "validation error reaches editor");
                AssertEx.Equal(1, requests.Count, "invalid form sends no write");
                AssertEx.Equal(revision, catalog.TemplatesRevision, "validation preserves pending read");
                requests[0].Ok(JVal.Parse("{\"templates\":[{\"name\":\"current\"}]}"));
                AssertEx.Equal("current", catalog.Templates[0].Name, "pending read can still settle");
            }
            finally { requests.Clear(); }
        }
    }
}
