using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The generated SLOPWORLD.md belongs to the daemon, but its template and discovery knobs
    // are small enough to keep beside the other Integrations settings. Preview asks slopd to
    // render the unsaved template, then feeds that text to the native Markdown viewer.
    public sealed class InstructionsPage : IOptionPage, IDisposable
    {
        enum Tab { Editor, Preview }

        readonly DaemonConfigState _configState = new DaemonConfigState();
        string _previewError;
        bool _previewBusy;
        bool _previewSample;
        string _previewText;
        string _previewProject;
        int _previewRequest;
        Tab _tab;

        SlopConfig _cfg => _configState.Config;
        string _error { get => _configState.Error; set => _configState.Error = value; }
        bool _loaded => _configState.Loaded;

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly MarkdownPreview _preview = new MarkdownPreview("", "SLOPWORLD.md");
        float _fieldsH = 520f;

        public InstructionsPage()
        {
            _preview.Opened();
        }

        public void Load()
        {
            _configState.Load(false, () =>
            {
                if (_tab == Tab.Preview) RequestPreview();
            });
        }

        public void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            var body = SlopWidgets.PageBody(rect).ContractedBy(SlopWidgets.GapM);
            Tab before = _tab;
            DrawTabs(new Rect(body.x, body.y, body.width, SlopWidgets.BtnH));
            if (before != _tab && _tab == Tab.Preview) RequestPreview();

            var content = new Rect(body.x, body.y + SlopWidgets.BtnH + SlopWidgets.GapM,
                body.width, Mathf.Max(0f, body.height - SlopWidgets.BtnH - SlopWidgets.GapM));
            if (!_loaded)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(content, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
            }
            else if (_tab == Tab.Editor)
            {
                DrawEditor(content);
            }
            else
            {
                DrawPreview(content);
            }

            DoFooter(SlopWidgets.FooterBar(rect));
        }

        void DrawTabs(Rect r)
        {
            float w = Mathf.Min(150f, (r.width - SlopWidgets.GapS) / 2f);
            if (SlopWidgets.Button(new Rect(r.x, r.y, w, r.height), "Template",
                    _tab == Tab.Editor ? SlopWidgets.Btn.Primary : SlopWidgets.Btn.Ghost))
                _tab = Tab.Editor;
            if (SlopWidgets.Button(new Rect(r.x + w + SlopWidgets.GapS, r.y, w, r.height),
                    "Preview", _tab == Tab.Preview ? SlopWidgets.Btn.Primary : SlopWidgets.Btn.Ghost))
                _tab = Tab.Preview;
        }

        void DrawEditor(Rect r)
        {
            var view = SlopScrollBody.View(r, _fieldsH);
            using (_scroll.Scope(r, view))
            {

                var l = new Listing_Standard { maxOneColumn = true };
                l.Begin(new Rect(0f, 0f, view.width, 4000f));

                SlopWidgets.SectionHeading(l, "SLOPWORLD.md");
                SlopWidgets.Note(l, "Generated runtime context is read-only in agent sandboxes. " +
                    "The template is rendered once for each project snapshot.");
                l.Gap(SlopWidgets.GapS);
                l.Label("Content template");
                _cfg.InstructionsTemplate = SlopWidgets.Area(l.GetRect(320f), "instructions.template",
                    _cfg.InstructionsTemplate);
                if (SlopWidgets.Button(l, "Reset to default", SlopWidgets.Btn.Ghost))
                    _cfg.InstructionsTemplate = SlopConfig.DefaultInstructionsTemplate;
                SlopWidgets.Note(l, "Variables: {{ runtime_context }}, {{ project }}, " +
                    "{{ mount_path }}, and {{ file }}. Unknown variables are left unchanged.");

                l.Gap(SlopWidgets.GapL);
                SlopWidgets.SectionHeading(l, "Discovery breadcrumb");
                SlopWidgets.Note(l, "This text is added to the agent's first prompt when the manifest " +
                    "is mounted. It is separate from the generated file body.");
                l.Label("Breadcrumb template");
                _cfg.InstructionsBreadcrumb = SlopWidgets.Area(l.GetRect(120f),
                    "instructions.breadcrumb", _cfg.InstructionsBreadcrumb);
                if (SlopWidgets.Button(l, "Reset to default", SlopWidgets.Btn.Ghost))
                    _cfg.InstructionsBreadcrumb = SlopConfig.DefaultInstructionsBreadcrumb;
                SlopWidgets.Note(l, "Variables: {{ project }}, {{ mount_path }}, and {{ file }}. " +
                    "Unknown variables are left unchanged.");
                _cfg.InstructionsBreadcrumbEnabled = SlopWidgets.Checkbox(l,
                    "Add discovery breadcrumb", _cfg.InstructionsBreadcrumbEnabled,
                    "Adds the configured discovery text to opted-in agents.");
                SlopWidgets.Note(l, "Reset changes the form only; press Save to apply it.");

                l.Gap(SlopWidgets.GapL);
                SlopWidgets.SectionHeading(l, "Worker bootstrap");
                SlopWidgets.Note(l, "This prompt is submitted to each worker spawned with slopctl spawn. " +
                    "Use $SLOPWORLD_TASK_ID to refer to its exact mailbox task.");
                l.Label("Worker prompt");
                _cfg.WorkerPrompt = SlopWidgets.Area(l.GetRect(180f), "instructions.worker_prompt",
                    _cfg.WorkerPrompt);
                if (SlopWidgets.Button(l, "Reset to default", SlopWidgets.Btn.Ghost))
                    _cfg.WorkerPrompt = SlopConfig.DefaultWorkerPrompt;
                SlopWidgets.Note(l, "The task body stays in the mailbox; this prompt tells the worker " +
                    "how to retrieve and finish it. Reset changes the form only; press Save to apply it.");

                l.Gap(SlopWidgets.GapL);
                SlopWidgets.SectionHeading(l, "Sandbox delivery");
                l.Label("Mount path (relative to the project)");
                _cfg.InstructionsMountPath = SlopWidgets.Field(l, "instructions.mount_path",
                    _cfg.InstructionsMountPath);
                SlopWidgets.Note(l, "The generated source remains the project-root " +
                    "SLOPWORLD.md; this is where its read-only copy appears to the agent.");
                SlopWidgets.Note(l, "Agents still opt in per session with Mount SLOPWORLD.md.");

                _fieldsH = l.CurHeight + SlopWidgets.GapS;
                l.End();
            }
        }

        void DrawPreview(Rect r)
        {
            float y = r.y;
            SlopWidgets.SectionHeading(new Rect(r.x, y, r.width, SlopWidgets.RowH),
                "Rendered SLOPWORLD.md");
            y += SlopWidgets.RowH + SlopWidgets.GapXS;

            string selected = PreviewProjectName();
            string label = string.IsNullOrEmpty(selected) ? "Sample project" : selected;
            if (SlopWidgets.Button(new Rect(r.x, y, Mathf.Min(300f, r.width), SlopWidgets.BtnH),
                    "Project: " + label))
                PickPreviewProject();
            y += SlopWidgets.BtnH + SlopWidgets.GapS;

            if (_previewBusy)
            {
                GUI.color = SlopWidgets.Dim;
                Widgets.Label(new Rect(r.x, y, r.width, SlopWidgets.LineH),
                    "Rendering preview...");
                GUI.color = Color.white;
                return;
            }
            if (_previewError != null)
            {
                GUI.color = SlopWidgets.Bad;
                Widgets.Label(new Rect(r.x, y, r.width, SlopWidgets.LineH), _previewError);
                GUI.color = Color.white;
                return;
            }
            if (_previewText == null)
            {
                GUI.color = SlopWidgets.Dim;
                Widgets.Label(new Rect(r.x, y, r.width, SlopWidgets.LineH),
                    "Choose Preview or Refresh to render the document.");
                GUI.color = Color.white;
                return;
            }

            var box = new Rect(r.x, y, r.width, Mathf.Max(0f, r.yMax - y));
            Slab.Box(box, SlopWidgets.Well, SlopWidgets.Edge);
            _preview.Draw(box.ContractedBy(SlopWidgets.GapS));
        }

        string PreviewProjectName()
        {
            if (_previewSample) return "";
            var projects = SessionHub.Instance.Projects;
            if (!string.IsNullOrEmpty(_previewProject) &&
                projects.Any(p => p.Name == _previewProject))
                return _previewProject;
            return projects.FirstOrDefault()?.Name ?? "";
        }

        void PickPreviewProject()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Sample project", () =>
                {
                    _previewSample = true;
                    _previewProject = "";
                    RequestPreview();
                })
            };
            foreach (var project in SessionHub.Instance.Projects)
            {
                var name = project.Name;
                options.Add(new FloatMenuOption(name, () =>
                {
                    _previewSample = false;
                    _previewProject = name;
                    RequestPreview();
                }));
            }
            Find.WindowStack.Add(new SlopMenu(options));
        }

        void RequestPreview()
        {
            if (!_loaded || _cfg == null) return;

            string project = PreviewProjectName();
            int request = ++_previewRequest;
            _previewBusy = true;
            _previewError = null;
            string body = "{" +
                $"\"project\":{JVal.Q(project)}," +
                $"\"template\":{JVal.Q(_cfg.InstructionsTemplate)}," +
                $"\"mount_path\":{JVal.Q(_cfg.InstructionsMountPath)}" +
                "}";
            SlopClient.Post("/api/instructions/preview", body,
                j =>
                {
                    if (request != _previewRequest) return;
                    _previewBusy = false;
                    _previewText = j["text"].AsString();
                    _preview.SetInlineText(_previewText);
                },
                msg =>
                {
                    if (request != _previewRequest) return;
                    _previewBusy = false;
                    _previewError = msg;
                    _previewText = null;
                });
        }

        void DoFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);
            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (_tab == Tab.Editor && foot.Left("Preview", SlopWidgets.Btn.Ghost))
            {
                _tab = Tab.Preview;
                RequestPreview();
            }
            if (_tab == Tab.Preview && foot.Left("Refresh", SlopWidgets.Btn.Ghost))
                RequestPreview();
            if (foot.Right("Save", SlopWidgets.Btn.Primary, _loaded)) Save();

            string error = _error ?? (_tab == Tab.Preview ? _previewError : null);
            if (error != null && _loaded)
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(foot.Rest(), error);
                GUI.color = Color.white;
            }
        }

        void Save()
        {
            if (!_loaded) return;

            string path = (_cfg.InstructionsMountPath ?? "").Trim().Replace('\\', '/');
            if (path.Length == 0 || path.StartsWith("/") || path.Split('/').Any(part =>
                    part == "." || part == ".."))
            {
                _error = "mount path must be relative to the project and cannot contain . or ..";
                return;
            }
            _cfg.InstructionsMountPath = path;

            _configState.Save(null, () =>
            {
                Messages.Message("SlopWorld: instructions settings saved.",
                    MessageTypeDefOf.TaskCompletion, false);
            });
        }

        public void Dispose()
        {
            ++_previewRequest;
            _preview.Closed();
        }
    }
}
