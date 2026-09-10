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

        DaemonConfig _cfg => _configState.Config;
        string _error { get => _configState.Error; set => _configState.Error = value; }
        bool _loaded => _configState.Loaded;

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly MarkdownPreview _preview = new MarkdownPreview("", "SLOPWORLD.md");
        readonly SettingsContentHeight _height = new SettingsContentHeight(520f);

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
            var body = SettingsPageLayout.Body(rect);
            if (_loaded && !_cfg.ExperimentalInstructions) _tab = Tab.Editor;
            Tab before = _tab;
            DrawTabs(new Rect(body.x, body.y, body.width, UiWidgets.BtnH));
            if (before != _tab && _tab == Tab.Preview) RequestPreview();

            var content = new Rect(body.x, body.y + UiWidgets.BtnH + UiWidgets.GapM,
                body.width, Mathf.Max(0f, body.height - UiWidgets.BtnH - UiWidgets.GapM));
            if (!_loaded)
            {
                UiWidgets.StatusLabel(content, _error ?? "Waiting for the daemon...",
                    _error != null ? UiWidgets.Bad : UiWidgets.Dim);
            }
            else if (_tab == Tab.Editor)
            {
                DrawEditor(content);
            }
            else
            {
                DrawPreview(content);
            }

            DoFooter(SettingsPageLayout.Footer(rect));
        }

        void DrawTabs(Rect r)
        {
            float w = Mathf.Min(150f, (r.width - UiWidgets.GapS) / 2f);
            if (UiWidgets.Button(new Rect(r.x, r.y, w, r.height), "Template",
                    _tab == Tab.Editor ? UiWidgets.Btn.Primary : UiWidgets.Btn.Ghost))
            {
                if (_tab != Tab.Editor) TextFieldSelection.ReleaseFocus();
                _tab = Tab.Editor;
            }
            if (UiWidgets.Button(new Rect(r.x + w + UiWidgets.GapS, r.y, w, r.height),
                    "Preview", _tab == Tab.Preview ? UiWidgets.Btn.Primary : UiWidgets.Btn.Ghost,
                    _loaded && _cfg.ExperimentalInstructions))
            {
                if (_tab != Tab.Preview) TextFieldSelection.ReleaseFocus();
                _tab = Tab.Preview;
            }
        }

        void DrawEditor(Rect r)
        {
            var view = UiScrollBody.View(r, _height.BeginFrame(Time.frameCount));
            using (_scroll.Scope(r, view))
            {

                var l = new Listing_Standard { maxOneColumn = true };
                l.Begin(new Rect(0f, 0f, view.width, UiWidgets.ListingHeight));

                if (!_cfg.ExperimentalInstructions)
                    UiWidgets.Note(l, "Enable instructions in Settings > General to edit SLOPWORLD.md instructions.");
                UiWidgets.SectionHeading(l, "SLOPWORLD.md");
                UiWidgets.Note(l, "Generated runtime context is read-only in agent sandboxes. " +
                    "The template is rendered once for each project snapshot.");
                l.Gap(UiWidgets.GapS);
                l.Label("Content template");
                _cfg.InstructionsTemplate = UiWidgets.Area(l, 320f, "instructions.template",
                    _cfg.InstructionsTemplate, on: _cfg.ExperimentalInstructions,
                    defaultValue: DaemonConfig.DefaultInstructionsTemplate);
                UiWidgets.Note(l, "Variables: {{ runtime_context }}, {{ project }}, " +
                    "{{ mount_path }}, and {{ file }}. Unknown variables are left unchanged.");

                l.Gap(UiWidgets.GapL);
                UiWidgets.SectionHeading(l, "Discovery breadcrumb");
                UiWidgets.Note(l, "This text is added to the agent's first prompt when the manifest " +
                    "is mounted. It is separate from the generated file body.");
                l.Label("Breadcrumb template");
                _cfg.InstructionsBreadcrumb = UiWidgets.Area(l, 120f, "instructions.breadcrumb",
                    _cfg.InstructionsBreadcrumb,
                    on: _cfg.ExperimentalBreadcrumbs && _cfg.ExperimentalInstructions,
                    defaultValue: DaemonConfig.DefaultInstructionsBreadcrumb);
                UiWidgets.Note(l, "Variables: {{ project }}, {{ mount_path }}, and {{ file }}. " +
                    "Unknown variables are left unchanged.");
                _cfg.InstructionsBreadcrumbEnabled = UiWidgets.Checkbox(l,
                    "Add discovery breadcrumb", _cfg.InstructionsBreadcrumbEnabled,
                    "Adds the configured discovery text to opted-in agents.",
                    locked: !_cfg.ExperimentalBreadcrumbs || !_cfg.ExperimentalInstructions);
                UiWidgets.Note(l, "Reset changes the form only; press Save to apply it.");

                l.Gap(UiWidgets.GapL);
                UiWidgets.SectionHeading(l, "Sandbox delivery");
                l.Label("Mount path (relative to the project)");
                _cfg.InstructionsMountPath = UiWidgets.Field(l, "instructions.mount_path",
                    _cfg.InstructionsMountPath, on: _cfg.ExperimentalInstructions,
                    defaultValue: WireContract.DefaultInstructionsMountPath);
                UiWidgets.Note(l, "The generated source remains the project-root " +
                    "SLOPWORLD.md; this is where its read-only copy appears to the agent.");
                UiWidgets.Note(l, "Agents still opt in per session with Mount SLOPWORLD.md.");

                _height.Measure(l.CurHeight + UiWidgets.GapS);
                l.End();
            }
        }

        void DrawPreview(Rect r)
        {
            float y = r.y;
            UiWidgets.SectionHeading(new Rect(r.x, y, r.width, UiWidgets.RowH),
                "Rendered SLOPWORLD.md");
            y += UiWidgets.RowH + UiWidgets.GapXS;

            string selected = PreviewProjectName();
            string label = string.IsNullOrEmpty(selected) ? "Sample project" : selected;
            if (UiWidgets.Button(new Rect(r.x, y, Mathf.Min(300f, r.width), UiWidgets.BtnH),
                    "Project: " + label))
                PickPreviewProject();
            y += UiWidgets.BtnH + UiWidgets.GapS;

            if (_previewBusy)
            {
                UiWidgets.StatusLabel(new Rect(r.x, y, r.width, UiWidgets.LineH),
                    "Rendering preview...", UiWidgets.Dim);
                return;
            }
            if (_previewError != null)
            {
                float h = UiWidgets.StatusLabelHeight(_previewError, r.width);
                UiWidgets.StatusLabel(new Rect(r.x, y, r.width, h), _previewError,
                    UiWidgets.Bad);
                return;
            }
            if (_previewText == null)
            {
                const string note = "Choose Preview or Refresh to render the document.";
                float h = UiWidgets.StatusLabelHeight(note, r.width);
                UiWidgets.StatusLabel(new Rect(r.x, y, r.width, h), note, UiWidgets.Dim);
                return;
            }

            var box = new Rect(r.x, y, r.width, Mathf.Max(0f, r.yMax - y));
            Slab.Box(box, UiWidgets.Well, UiWidgets.Edge);
            _preview.Draw(SettingsPageLayout.Inset(box, UiWidgets.GapS));
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
            Find.WindowStack.Add(new UiMenu(options));
        }

        void RequestPreview()
        {
            if (!_loaded || _cfg == null || !_cfg.ExperimentalInstructions) return;

            string project = PreviewProjectName();
            int request = ++_previewRequest;
            _previewBusy = true;
            _previewError = null;
            string body = "{" +
                $"\"project\":{JVal.Q(project)}," +
                $"\"template\":{JVal.Q(_cfg.InstructionsTemplate)}," +
                $"\"mount_path\":{JVal.Q(_cfg.InstructionsMountPath)}" +
                "}";
            DaemonClient.Post(WireContract.Routes.InstructionsPreview, body,
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
            var foot = new UiWidgets.Bar(bar);
            if (foot.Left("Reload", UiWidgets.Btn.Ghost)) Load();
            if (_tab == Tab.Editor && foot.Left("Preview", UiWidgets.Btn.Ghost,
                    _loaded && _cfg.ExperimentalInstructions))
            {
                _tab = Tab.Preview;
                RequestPreview();
            }
            if (_tab == Tab.Preview && foot.Left("Refresh", UiWidgets.Btn.Ghost))
                RequestPreview();
            if (foot.Right("Save", UiWidgets.Btn.Primary, _loaded)) Save();

            string error = _error ?? (_tab == Tab.Preview ? _previewError : null);
            if (error != null && _loaded)
            {
                GUI.color = UiWidgets.Bad;
                UiWidgets.RowLabel(foot.Rest(), error);
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
