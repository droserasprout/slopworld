using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The generated SLOPWORLD.md belongs to the daemon, but its template and discovery knobs
    // are small enough to keep beside the other Agents settings. Preview asks slopd to
    // render the unsaved template, then feeds that text to the native Markdown viewer.
    public sealed class InstructionsPage : DaemonConfigPage, IDisposable
    {
        enum Tab { Editor, Preview }

        readonly AsyncLoadState<string> _previewLoad = new AsyncLoadState<string>();
        bool _previewSample;
        string _previewProject;
        Tab _tab;

        readonly ScrollableListing _editorListing = new ScrollableListing(520f);
        readonly MarkdownPreview _preview = new MarkdownPreview("", "SLOPWORLD.md");

        public InstructionsPage()
        {
            _preview.Opened();
        }

        protected override void DrawFields(Listing_Standard l) { }

        protected override void AfterLoad()
        {
            if (_tab == Tab.Preview) RequestPreview();
        }

        public override void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            bool instructions = EffectiveInstructions;
            if (_lastInstructionsGate.HasValue && _lastInstructionsGate.Value && !instructions)
            {
                _previewLoad.Invalidate();
                _preview.Closed();
                _preview.SetInlineText("");
            }
            _lastInstructionsGate = instructions;

            var body = SettingsPageLayout.Body(rect);
            if (!instructions) _tab = Tab.Editor;
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
            bool instructions = EffectiveInstructions;
            float w = Mathf.Min(150f, (r.width - UiTheme.GapS) / 2f);
            if (UiButtons.Button(new Rect(r.x, r.y, w, r.height), "Template",
                    _tab == Tab.Editor ? UiTheme.Btn.Primary : UiTheme.Btn.Ghost))
            {
                if (_tab != Tab.Editor) TextFieldSelection.ReleaseFocus();
                _tab = Tab.Editor;
            }
            if (UiButtons.Button(new Rect(r.x + w + UiTheme.GapS, r.y, w, r.height),
                "Preview", _tab == Tab.Preview ? UiTheme.Btn.Primary : UiTheme.Btn.Ghost,
                    _loaded && instructions && !_saving))
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
            bool instructions = EffectiveInstructions;
            bool breadcrumbs = EffectiveBreadcrumbs;
            if (!instructions || !breadcrumbs)
            {
                UiLayout.Note(l, "These controls use the live daemon gates in " +
                    "Settings > General > Experimental.");
                if (UiLayout.Button(l, "Open General > Experimental", UiTheme.Btn.Ghost))
                    ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Config));
            }
            UiLayout.SectionHeading(l, "SLOPWORLD.md");
            UiLayout.Note(l, "Generated runtime context is read-only in agent sandboxes. " +
                "The template is rendered once for each project snapshot.");
            l.Gap(UiTheme.GapS);
            l.Label("Content template");
            _cfg.InstructionsTemplate = UiControls.Area(l, 320f, "instructions.template",
                _cfg.InstructionsTemplate, on: instructions,
                defaultValue: DaemonConfig.DefaultInstructionsTemplate);
            UiLayout.Note(l, "Variables: {{ runtime_context }}, {{ project }}, " +
                "{{ mount_path }}, and {{ file }}. Unknown variables are left unchanged.");

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Discovery breadcrumb");
            if (!breadcrumbs)
                UiLayout.Note(l, "Requires the separate Breadcrumbs gate in General > Experimental.");
            if (!instructions)
                UiLayout.Note(l, "Requires the Instructions gate in General > Experimental.");
            UiLayout.Note(l, "This text is added to the agent's first prompt when the manifest " +
                "is mounted. It is separate from the generated file body.");
            l.Label("Breadcrumb template");
            _cfg.InstructionsBreadcrumb = UiControls.Area(l, 120f, "instructions.breadcrumb",
                _cfg.InstructionsBreadcrumb,
                on: breadcrumbs && instructions,
                defaultValue: DaemonConfig.DefaultInstructionsBreadcrumb);
            UiLayout.Note(l, "Variables: {{ project }}, {{ mount_path }}, and {{ file }}. " +
                "Unknown variables are left unchanged.");
            _cfg.InstructionsBreadcrumbEnabled = UiControls.Checkbox(l,
                "Add discovery breadcrumb", _cfg.InstructionsBreadcrumbEnabled,
                "Adds the configured discovery text to opted-in agents.",
                locked: !breadcrumbs || !instructions);
            UiLayout.Note(l, "Reset changes the form only; press Save to apply it.");

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Sandbox delivery");
            l.Label("Mount path (relative to the project)");
            _cfg.InstructionsMountPath = UiControls.Field(l, "instructions.mount_path",
                _cfg.InstructionsMountPath, on: instructions,
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
            if (!_loaded || _cfg == null || !EffectiveInstructions) return;

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
            if (foot.Left("Reload", UiTheme.Btn.Ghost, !_saving)) Load();
            if (_tab == Tab.Editor && foot.Left("Preview", UiTheme.Btn.Ghost,
                    _loaded && EffectiveInstructions && !_saving))
            {
                _tab = Tab.Preview;
                RequestPreview();
            }
            if (_tab == Tab.Preview && foot.Left("Refresh", UiTheme.Btn.Ghost,
                    !_saving && EffectiveInstructions))
                RequestPreview();
            if (foot.Right("Discard", UiTheme.Btn.Ghost,
                    _loaded && _dirty && !_saving)) DiscardConfig();
            if (foot.Right("Save", UiTheme.Btn.Primary, CanSave)) SaveConfig();
            DrawConfigStatus(foot);
        }

        protected override bool PrepareSave(out string error)
        {
            string path = (_cfg.InstructionsMountPath ?? "").Trim().Replace('\\', '/');
            if (path.Length == 0 || path.StartsWith("/") || path.Split('/').Any(part =>
                    part == "." || part == ".."))
            {
                error = "mount path must be relative to the project and cannot contain . or ..";
                return false;
            }
            _cfg.InstructionsMountPath = path;
            error = null;
            return true;
        }

        bool EffectiveInstructions => SessionHub.Instance.Config != null &&
            SessionHub.Instance.Config.ExperimentalInstructions;

        bool EffectiveBreadcrumbs => SessionHub.Instance.Config != null &&
            SessionHub.Instance.Config.ExperimentalBreadcrumbs;

        bool? _lastInstructionsGate;

        public override void Dispose()
        {
            base.Dispose();
            _previewLoad.Invalidate();
            _preview.Closed();
        }
    }
}
