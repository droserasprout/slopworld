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
        readonly AsyncLoadState<string> _previewLoad = new AsyncLoadState<string>();
        bool _previewSample;
        string _previewProject;
        Tab _tab;

        DaemonConfig _cfg => _configState.Config;
        string _error { get => _configState.Error; set => _configState.Error = value; }
        bool _loaded => _configState.Loaded;

        readonly ScrollableListing _editorListing = new ScrollableListing(520f);
        readonly MarkdownPreview _preview = new MarkdownPreview("", "SLOPWORLD.md");

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
            DrawTabs(new Rect(body.x, body.y, body.width, UiTheme.BtnH));
            if (before != _tab && _tab == Tab.Preview) RequestPreview();

            var content = new Rect(body.x, body.y + UiTheme.BtnH + UiTheme.GapM,
                body.width, Mathf.Max(0f, body.height - UiTheme.BtnH - UiTheme.GapM));
            if (!_loaded)
            {
                UiText.StatusLabel(content, _error ?? "Waiting for the daemon...",
                    _error != null ? UiTheme.Bad : UiTheme.Dim);
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
            float w = Mathf.Min(150f, (r.width - UiTheme.GapS) / 2f);
            if (UiButtons.Button(new Rect(r.x, r.y, w, r.height), "Template",
                    _tab == Tab.Editor ? UiTheme.Btn.Primary : UiTheme.Btn.Ghost))
            {
                if (_tab != Tab.Editor) TextFieldSelection.ReleaseFocus();
                _tab = Tab.Editor;
            }
            if (UiButtons.Button(new Rect(r.x + w + UiTheme.GapS, r.y, w, r.height),
                    "Preview", _tab == Tab.Preview ? UiTheme.Btn.Primary : UiTheme.Btn.Ghost,
                    _loaded && _cfg.ExperimentalInstructions))
            {
                if (_tab != Tab.Preview) TextFieldSelection.ReleaseFocus();
                _tab = Tab.Preview;
            }
        }

        void DrawEditor(Rect r)
        {
            _editorListing.Draw(r, DrawEditorFields);
        }

        void DrawEditorFields(Listing_Standard l)
        {
            if (!_cfg.ExperimentalInstructions)
                UiLayout.Note(l, "Enable instructions in Settings > General to edit SLOPWORLD.md instructions.");
            UiLayout.SectionHeading(l, "SLOPWORLD.md");
            UiLayout.Note(l, "Generated runtime context is read-only in agent sandboxes. " +
                "The template is rendered once for each project snapshot.");
            l.Gap(UiTheme.GapS);
            l.Label("Content template");
            _cfg.InstructionsTemplate = UiControls.Area(l, 320f, "instructions.template",
                _cfg.InstructionsTemplate, on: _cfg.ExperimentalInstructions,
                defaultValue: DaemonConfig.DefaultInstructionsTemplate);
            UiLayout.Note(l, "Variables: {{ runtime_context }}, {{ project }}, " +
                "{{ mount_path }}, and {{ file }}. Unknown variables are left unchanged.");

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Discovery breadcrumb");
            UiLayout.Note(l, "This text is added to the agent's first prompt when the manifest " +
                "is mounted. It is separate from the generated file body.");
            l.Label("Breadcrumb template");
            _cfg.InstructionsBreadcrumb = UiControls.Area(l, 120f, "instructions.breadcrumb",
                _cfg.InstructionsBreadcrumb,
                on: _cfg.ExperimentalBreadcrumbs && _cfg.ExperimentalInstructions,
                defaultValue: DaemonConfig.DefaultInstructionsBreadcrumb);
            UiLayout.Note(l, "Variables: {{ project }}, {{ mount_path }}, and {{ file }}. " +
                "Unknown variables are left unchanged.");
            _cfg.InstructionsBreadcrumbEnabled = UiControls.Checkbox(l,
                "Add discovery breadcrumb", _cfg.InstructionsBreadcrumbEnabled,
                "Adds the configured discovery text to opted-in agents.",
                locked: !_cfg.ExperimentalBreadcrumbs || !_cfg.ExperimentalInstructions);
            UiLayout.Note(l, "Reset changes the form only; press Save to apply it.");

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Sandbox delivery");
            l.Label("Mount path (relative to the project)");
            _cfg.InstructionsMountPath = UiControls.Field(l, "instructions.mount_path",
                _cfg.InstructionsMountPath, on: _cfg.ExperimentalInstructions,
                defaultValue: WireContract.DefaultInstructionsMountPath);
            UiLayout.Note(l, "The generated source remains the project-root " +
                "SLOPWORLD.md; this is where its read-only copy appears to the agent.");
            UiLayout.Note(l, "Agents still opt in per session with Mount SLOPWORLD.md.");
        }

        void DrawPreview(Rect r)
        {
            float y = r.y;
            UiLayout.SectionHeading(new Rect(r.x, y, r.width, UiTheme.RowH),
                "Rendered SLOPWORLD.md");
            y += UiTheme.RowH + UiTheme.GapXS;

            string selected = PreviewProjectName();
            string label = string.IsNullOrEmpty(selected) ? "Sample project" : selected;
            if (UiButtons.Button(new Rect(r.x, y, Mathf.Min(300f, r.width), UiTheme.BtnH),
                    "Project: " + label))
                PickPreviewProject();
            y += UiTheme.BtnH + UiTheme.GapS;

            if (_previewLoad.Loading)
            {
                UiText.StatusLabel(new Rect(r.x, y, r.width, UiTheme.LineH),
                    "Rendering preview...", UiTheme.Dim);
                return;
            }
            if (_previewLoad.Error != null)
            {
                float h = UiText.StatusLabelHeight(_previewLoad.Error, r.width);
                UiText.StatusLabel(new Rect(r.x, y, r.width, h), _previewLoad.Error,
                    UiTheme.Bad);
                return;
            }
            if (!_previewLoad.HasValue)
            {
                const string note = "Choose Preview or Refresh to render the document.";
                float h = UiText.StatusLabelHeight(note, r.width);
                UiText.StatusLabel(new Rect(r.x, y, r.width, h), note, UiTheme.Dim);
                return;
            }

            var box = new Rect(r.x, y, r.width, Mathf.Max(0f, r.yMax - y));
            Slab.Box(box, UiTheme.Well, UiTheme.Edge);
            _preview.Draw(SettingsPageLayout.Inset(box, UiTheme.GapS));
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
            string body = "{" +
                $"\"project\":{JVal.Q(project)}," +
                $"\"template\":{JVal.Q(_cfg.InstructionsTemplate)}," +
                $"\"mount_path\":{JVal.Q(_cfg.InstructionsMountPath)}" +
                "}";
            _previewLoad.Load((ok, fail) => DaemonClient.Post(
                WireContract.Routes.InstructionsPreview, body,
                j => ok(j["text"].AsString()), fail),
                text => _preview.SetInlineText(text));
        }

        void DoFooter(Rect bar)
        {
            var foot = new UiLayout.Bar(bar);
            if (foot.Left("Reload", UiTheme.Btn.Ghost)) Load();
            if (_tab == Tab.Editor && foot.Left("Preview", UiTheme.Btn.Ghost,
                    _loaded && _cfg.ExperimentalInstructions))
            {
                _tab = Tab.Preview;
                RequestPreview();
            }
            if (_tab == Tab.Preview && foot.Left("Refresh", UiTheme.Btn.Ghost))
                RequestPreview();
            if (foot.Right("Save", UiTheme.Btn.Primary, _loaded)) Save();

            string error = _error ?? (_tab == Tab.Preview ? _previewLoad.Error : null);
            if (error != null && _loaded)
            {
                GUI.color = UiTheme.Bad;
                UiText.RowLabel(foot.Rest(), error);
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
            _previewLoad.Invalidate();
            _preview.Closed();
        }
    }
}
