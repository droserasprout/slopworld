using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The kind is chosen by the window that was opened, not edited inside the form. Each
    // concrete editor below owns one kind's fields; this base owns identity, shared chrome,
    // project pickers and persistence.
    public abstract class EditLibraryItemDialog : UiWindow
    {
        protected readonly EditIdentity _identity;
        protected readonly LibraryItemInfo _s;
        protected readonly LibraryItemKind Kind;

        // Asked for rather than assumed: `[defaults] shell` is a per-machine answer and
        // this dialog would otherwise print somebody else's.
        protected string _agentDefault = "claude";
        protected string _shellDefault = "bash";

        protected EditLibraryItemDialog(LibraryItemInfo existing, bool copy, LibraryItemKind kind)
        {
            if (copy && existing == null) throw new ArgumentNullException(nameof(existing));

            // A duplicate is a new daemon entry: it must POST rather than PUT, and its
            // name is suggested rather than copied so saving it cannot collide by default.
            _identity = copy ? EditIdentity.ForCopy(existing.Name) :
                existing == null ? EditIdentity.ForNew() : EditIdentity.ForEdit(existing.Name);
            _s = existing?.Copy() ?? new LibraryItemInfo();
            Kind = kind;
            _s.Kind = kind;
            if (copy)
                _s.Name = _identity.CopyName(SessionHub.Instance.Library.Select(s => s.Name),
                    "library");

            // Breadcrumbs and file actions do not choose a runnable project. Keep the wire
            // shape explicit for new records while preserving all values when editing/copying.
            if (existing == null && (kind == LibraryItemKind.Breadcrumb ||
                kind == LibraryItemKind.FileAction))
            {
                _s.Link = LibraryItemLink.Project;
                _s.Project = "";
            }

            SessionHub.Instance.Catalog.RefreshProjects();
            SessionHub.Instance.Catalog.RefreshTemplates();
            DaemonClient.Get<Wire.ConfigResult>(WireProtocol.Routes.Config, j =>
            {
                var d = j.Values.Defaults;
                _agentDefault = d.Agent;
                _shellDefault = d.Shell;
            });
        }

        public static EditLibraryItemDialog New(LibraryItemKind kind) =>
            Create(kind, null, false);

        public static EditLibraryItemDialog ForEdit(LibraryItemInfo existing)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            return Create(existing.Kind, existing, false);
        }

        public static EditLibraryItemDialog Copy(LibraryItemInfo of)
        {
            if (of == null) throw new ArgumentNullException(nameof(of));
            return Create(of.Kind, of, true);
        }

        static EditLibraryItemDialog Create(LibraryItemKind kind, LibraryItemInfo existing, bool copy)
        {
            switch (kind)
            {
                case LibraryItemKind.Prompt:
                    return new EditPromptDialog(existing, copy);
                case LibraryItemKind.Shell:
                    return new EditShellDialog(existing, copy);
                case LibraryItemKind.Breadcrumb:
                    return new EditBreadcrumbDialog(existing, copy);
                case LibraryItemKind.FileAction:
                    return new EditFileActionDialog(existing, copy);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        // What is left at the bottom is the prompt box - the one field here somebody
        // writes paragraphs in, and the one that gets squeezed when anything above grows.
        public override Vector2 InitialSize => new Vector2(600f, 740f);

        protected abstract string TitleNoun { get; }
        protected abstract string TextHeading { get; }
        protected abstract void DrawKindFields(Listing_Standard listing);

        protected override void DoBody(Rect rect)
        {
            // One column, on the room it has: a Listing_Standard begun on a rect too short
            // for its contents does not overflow, it breaks to a column off the right-hand
            // edge and puts CurHeight back to nearly zero - and the prompt box below is
            // placed and sized from that number. See EditProjectDialog.DoFields.
            UiLayout.Title(TitleRect(rect), _identity.Title(TitleNoun));

            float head = UiTheme.HeaderH + UiTheme.GapS;
            bool enabled = GUI.enabled;
            float used = DrawFields(new Rect(rect.x, rect.y + head, rect.width, rect.height - head));
            float y = rect.y + head + used + UiTheme.GapL;
            DrawTextEditor(rect, y);
            GUI.enabled = enabled;
            DrawFooter(rect);
        }

        float DrawFields(Rect rect)
        {
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(rect);

            DrawName(l);
            DrawKindFields(l);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        void DrawName(Listing_Standard l)
        {
            l.Label("Name (also what the temporary colonist is called)");
            _s.Name = UiControls.Field(l, "library.name", _s.Name);
        }

        protected void DrawRunLocation(Listing_Standard l)
        {
            UiControls.Select(l, "Where it runs", LinkLabel(_s.Link), LinkOptions(), out _,
                openMenu: TerminalWindow.OpenOverPane);
        }

        protected void DrawProject(Listing_Standard l)
        {
            // Temporary mode creates its own workspace and Ask mode chooses one when run,
            // so neither needs a project selector in the editor.
            if (_s.Link == LibraryItemLink.Ask) return;
            UiControls.Select(l,
                _s.Link == LibraryItemLink.Temp
                    ? "Temporary workspace (blank = plain agent settings)"
                    : "Project workspace (directory and shared mounts)",
                string.IsNullOrEmpty(_s.Project)
                    ? (_s.Link == LibraryItemLink.Temp ? "None" : "Pick a project...")
                    : _s.Project,
                ProjectOptions(), out _, openMenu: TerminalWindow.OpenOverPane);
        }

        protected void DrawExecution(Listing_Standard l)
        {
            var options = new List<SelectorOption>
            {
                new SelectorOption("Host", () => { _s.Host = true; _s.AgentTemplate = ""; }),
            };
            options.AddRange(SessionHub.Instance.Catalog.Templates.Select(t =>
                new SelectorOption("Agent template: " + t.Name,
                    () => { _s.Host = false; _s.AgentTemplate = t.Name; })));
            string label = _s.Host ? "Host" : string.IsNullOrEmpty(_s.AgentTemplate)
                ? "Choose Host or an agent template..." : "Agent template: " + _s.AgentTemplate;
            UiControls.Select(l, "Run using", label, options, out _);
        }

        protected void DrawFileActionMode(Listing_Standard l)
        {
            UiControls.Select(l, "After choosing the file action",
                FileActionModeText.Label(_s.Mode), FileActionModeOptions(), out _,
                openMenu: TerminalWindow.OpenOverPane);
        }

        protected void DrawExplanation(Listing_Standard l, string text)
        {
            GUI.color = UiTheme.Dim;
            l.Label(text);
            GUI.color = Color.white;
        }

        protected void DrawCommand(Listing_Standard l, string label, string placeholder,
            bool templatePlaceholder = false)
        {
            l.Gap(UiTheme.GapS);
            l.Label(label);
            var box = UiControls.FieldRect(l);
            if (!string.IsNullOrEmpty((_s.Command ?? "").Trim()))
            {
                _s.Command = UiText.Field(box, "library.command", _s.Command);
                return;
            }

            string shownPlaceholder = templatePlaceholder && !string.IsNullOrEmpty(_s.AgentTemplate)
                ? "From agent template" : placeholder;
            GUI.color = UiTheme.Faint;
            string shown = UiText.Field(box, "library.command", shownPlaceholder);
            GUI.color = Color.white;
            if (shown != shownPlaceholder) _s.Command = shown;
        }

        protected float DrawTextEditor(Rect rect, float y)
        {
            UiLayout.SectionHeading(new Rect(rect.x, y, rect.width, UiTheme.RowH), TextHeading);
            y += UiTheme.RowH + UiTheme.GapXS;

            if (Kind != LibraryItemKind.FileAction)
            {
                var area = new Rect(rect.x, y, rect.width,
                    rect.yMax - UiTheme.BtnH - UiTheme.GapS - y);
                _s.Text = UiText.Area(area, "library.text", _s.Text ?? "");
            }
            else
            {
                _s.Text = "";
            }
            return rect.yMax - y;
        }

        void DrawFooter(Rect rect)
        {
            var foot = new UiLayout.Bar(UiLayout.FooterBar(rect));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            if (foot.Right("Save", UiTheme.Btn.Primary)) Save();
        }

        public static string LinkLabel(LibraryItemLink link)
        {
            switch (link)
            {
                case LibraryItemLink.Temp: return "A new temporary project each run";
                case LibraryItemLink.Ask: return "Ask me every time";
                default: return "One project, named below";
            }
        }

        string Explain()
        {
            var project = SessionHub.Instance.Project(_s.Project);
            switch (_s.Link)
            {
                case LibraryItemLink.Temp:
                    return $"Each run gets an empty directory under {ProjectInfo.TempRoot}" +
                           (project != null
                               ? $", using '{project.Name}' as its workspace."
                               : ". Nothing deletes it; the machine clears /tmp.");
                case LibraryItemLink.Ask:
                    return "Running it opens a list of projects, plus a temporary one.";
                default:
                    return project != null
                        ? $"{project.Dir}  ({ProjectsView.Summary(project)})"
                        : SessionHub.Instance.Projects.Count == 0
                            ? "No projects yet - make one in the Projects window first."
                            : "";
            }
        }

        protected string RunExplanation => Explain();

        IEnumerable<SelectorOption> LinkOptions()
        {
            return new[]
            {
                new SelectorOption(LinkLabel(LibraryItemLink.Project),
                    () => _s.Link = LibraryItemLink.Project),
                new SelectorOption(LinkLabel(LibraryItemLink.Temp),
                    () => _s.Link = LibraryItemLink.Temp),
                new SelectorOption(LinkLabel(LibraryItemLink.Ask),
                    () => _s.Link = LibraryItemLink.Ask),
            };
        }

        IEnumerable<SelectorOption> FileActionModeOptions()
        {
            return new[]
            {
                new SelectorOption(FileActionModeText.Label(FileActionMode.Nothing),
                    () => _s.Mode = FileActionMode.Nothing),
                new SelectorOption(FileActionModeText.Label(FileActionMode.Ask),
                    () => _s.Mode = FileActionMode.Ask),
                new SelectorOption(FileActionModeText.Label(FileActionMode.ShowResult),
                    () => _s.Mode = FileActionMode.ShowResult),
                new SelectorOption(FileActionModeText.Label(FileActionMode.OpenTerminal),
                    () => _s.Mode = FileActionMode.OpenTerminal),
            };
        }

        IEnumerable<SelectorOption> ProjectOptions()
        {
            var options = SessionHub.Instance.Projects
                .Select(p => new SelectorOption($"{p.Name}  -  {p.Dir}",
                    () => _s.Project = p.Name)).ToList();
            if (_s.Link == LibraryItemLink.Temp)
                options.Insert(0, new SelectorOption("None", () => _s.Project = ""));
            options.Add(new SelectorOption("New project...",
                () => TerminalWindow.OpenOverPane(new EditProjectDialog(null))));
            return options;
        }

        void Save()
        {
            if (string.IsNullOrEmpty((_s.Name ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a library entry needs a name.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if ((Kind == LibraryItemKind.Prompt || Kind == LibraryItemKind.Shell) &&
                _s.Link == LibraryItemLink.Project &&
                string.IsNullOrEmpty((_s.Project ?? "").Trim()))
            {
                Messages.Message("SlopWorld: pick a project, or a way to choose one.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if ((Kind == LibraryItemKind.Prompt || Kind == LibraryItemKind.Shell) &&
                !_s.Host && string.IsNullOrWhiteSpace(_s.AgentTemplate))
            {
                UiLayout.Fail("Choose Host or an agent template for this entry.");
                return;
            }
            if (Kind == LibraryItemKind.FileAction &&
                string.IsNullOrEmpty((_s.Command ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a file action needs a command.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (Kind != LibraryItemKind.FileAction &&
                string.IsNullOrEmpty((_s.Text ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a library entry needs something to send.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            SessionHub.Instance.Catalog.SaveLibraryItem(_s, _identity.IsNew,
                _identity.OriginalName,
                ok: () => Close(),
                fail: msg => Messages.Message($"SlopWorld: {msg}",
                    MessageTypeDefOf.RejectInput, false));
        }
    }
}
