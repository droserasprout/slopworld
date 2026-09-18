using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The command box is greyed rather than hidden when it is empty, so the thing
    // that will run is on screen even when nothing here chose it.
    public class EditLibraryItemDialog : UiWindow
    {
        sealed class LibraryItemKindDescriptor
        {
            public readonly string ButtonLabel;
            public readonly bool ShowWhere;
            public readonly Func<EditLibraryItemDialog, bool> ShowProject;
            public readonly Func<EditLibraryItemDialog, string> ProjectLabel;
            public readonly Func<EditLibraryItemDialog, string> ProjectValue;
            public readonly Func<EditLibraryItemDialog, ProjectInfo, string> ExplainText;
            public readonly string CommandLabel;
            public readonly Func<EditLibraryItemDialog, string> CommandPlaceholder;

            public LibraryItemKindDescriptor(string buttonLabel, bool showWhere,
                Func<EditLibraryItemDialog, bool> showProject,
                Func<EditLibraryItemDialog, string> projectLabel,
                Func<EditLibraryItemDialog, string> projectValue,
                Func<EditLibraryItemDialog, ProjectInfo, string> explainText,
                string commandLabel, Func<EditLibraryItemDialog, string> commandPlaceholder)
            {
                ButtonLabel = buttonLabel;
                ShowWhere = showWhere;
                ShowProject = showProject;
                ProjectLabel = projectLabel;
                ProjectValue = projectValue;
                ExplainText = explainText;
                CommandLabel = commandLabel;
                CommandPlaceholder = commandPlaceholder;
            }
        }

        static readonly Dictionary<LibraryItemKind, LibraryItemKindDescriptor> KindDescriptors =
            new Dictionary<LibraryItemKind, LibraryItemKindDescriptor>
            {
                {
                    LibraryItemKind.Prompt,
                    new LibraryItemKindDescriptor(
                        "Prompt - say something to an agent", true,
                        dialog => dialog._s.Link != LibraryItemLink.Ask,
                        dialog => dialog._s.Link == LibraryItemLink.Temp
                            ? "Temporary workspace (blank = plain agent settings)"
                            : "Project workspace (directory and shared mounts)",
                        dialog => string.IsNullOrEmpty(dialog._s.Project)
                            ? (dialog._s.Link == LibraryItemLink.Temp ? "None" : "Pick a project...")
                            : dialog._s.Project,
                        (dialog, project) => dialog.Explain(project),
                        "Command override (blank = template or host default)", dialog => dialog._agentDefault)
                },
                {
                    LibraryItemKind.Shell,
                    new LibraryItemKindDescriptor(
                        "Shell - run a command", true,
                        dialog => dialog._s.Link != LibraryItemLink.Ask,
                        dialog => dialog._s.Link == LibraryItemLink.Temp
                            ? "Temporary workspace (blank = plain agent settings)"
                            : "Project workspace (directory and shared mounts)",
                        dialog => string.IsNullOrEmpty(dialog._s.Project)
                            ? (dialog._s.Link == LibraryItemLink.Temp ? "None" : "Pick a project...")
                            : dialog._s.Project,
                        (dialog, project) => dialog.Explain(project),
                        "Shell (blank = the default)", dialog => dialog._shellDefault)
                },
                {
                    LibraryItemKind.Breadcrumb,
                    new LibraryItemKindDescriptor(
                        "Breadcrumb - insert manually", false,
                        dialog => false,
                        null,
                        null,
                        (dialog, project) =>
                            "Insert this text manually from a terminal context menu; it is not runnable.",
                        null, null)
                },
                {
                    LibraryItemKind.FileAction,
                    new LibraryItemKindDescriptor(
                        "File action - run on a Files row", false,
                        dialog => false,
                        null, null,
                        (dialog, project) =>
                            "This command is offered by the Files sidebar; use {{ absolute_path }} or {{ relative_path }}.",
                        "Command (path is appended unless substituted)", dialog => dialog._agentDefault)
                },
            };

        readonly EditIdentity _identity;
        readonly LibraryItemInfo _s;

        // Asked for rather than assumed: `[defaults] shell` is a per-machine answer and
        // this dialog would otherwise print somebody else's.
        string _agentDefault = "claude";
        string _shellDefault = "bash";

        bool CurrentKindEnabled => true;

        public EditLibraryItemDialog(LibraryItemInfo existing) : this(existing, false) { }

        public static EditLibraryItemDialog Copy(LibraryItemInfo of) =>
            new EditLibraryItemDialog(of, true);

        EditLibraryItemDialog(LibraryItemInfo existing, bool copy)
        {
            // A duplicate is a new daemon entry: it must POST rather than PUT, and its
            // name is suggested rather than copied so saving it cannot collide by default.
            _identity = copy ? EditIdentity.ForCopy(existing?.Name) :
                existing == null ? EditIdentity.ForNew() : EditIdentity.ForEdit(existing.Name);
            _s = existing?.Copy() ?? new LibraryItemInfo();
            if (copy)
                _s.Name = _identity.CopyName(SessionHub.Instance.Library.Select(s => s.Name),
                    "library");


            SessionHub.Instance.Catalog.RefreshProjects();
            SessionHub.Instance.Catalog.RefreshTemplates();
            DaemonClient.Get<Wire.ConfigResult>(WireProtocol.Routes.Config, j =>
            {
                var d = j.Values.Defaults;
                _agentDefault = d.Agent;
                _shellDefault = d.Shell;
            });
        }

        public EditLibraryItemDialog(LibraryItemKind kind) : this(null)
        {
            _s.Kind = kind;
            if (kind == LibraryItemKind.Breadcrumb || kind == LibraryItemKind.FileAction)
            {
                _s.Link = LibraryItemLink.Project;
                _s.Project = "";
            }
        }

        // What is left at the bottom is the prompt box - the one field here somebody
        // writes paragraphs in, and the one that gets squeezed when anything above grows.
        public override Vector2 InitialSize => new Vector2(600f, 740f);

        protected override void DoBody(Rect rect)
        {
            // One column, on the room it has: a Listing_Standard begun on a rect too short
            // for its contents does not overflow, it breaks to a column off the right-hand
            // edge and puts CurHeight back to nearly zero - and the prompt box below is
            // placed and sized from that number. See EditProjectDialog.DoFields.
            UiLayout.Title(TitleRect(rect), _identity.Title("library entry"));

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

            var kind = KindDescriptors[_s.Kind];
            DrawName(l);
            DrawKindAndLink(l, kind);
            DrawFileActionMode(l);
            DrawProject(l, kind);
            DrawExecution(l);
            DrawExplanation(l, kind);
            DrawCommand(l, kind);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        void DrawName(Listing_Standard l)
        {
            l.Label("Name (also what the temporary colonist is called)");
            _s.Name = UiControls.Field(l, "library.name", _s.Name, on: CurrentKindEnabled);
        }

        void DrawKindAndLink(Listing_Standard l, LibraryItemKindDescriptor kind)
        {
            UiControls.Select(l, "Kind", kind.ButtonLabel, KindOptions(), out _,
                on: CurrentKindEnabled, openMenu: TerminalWindow.OpenOverPane);

            if (!kind.ShowWhere) return;
            UiControls.Select(l, "Where it runs", LinkLabel(_s.Link), LinkOptions(), out _,
                openMenu: TerminalWindow.OpenOverPane);
        }

        void DrawProject(Listing_Standard l, LibraryItemKindDescriptor kind)
        {
            // The project dropdown stays up for every kind that uses a project. Temporary mode
            // creates its own workspace and therefore has no project selector.
            if (!kind.ShowProject(this)) return;
            UiControls.Select(l, kind.ProjectLabel(this), kind.ProjectValue(this),
                ProjectOptions(_s.Kind == LibraryItemKind.Breadcrumb), out _,
                on: CurrentKindEnabled, openMenu: TerminalWindow.OpenOverPane);
        }

        void DrawExecution(Listing_Standard l)
        {
            if (_s.Kind != LibraryItemKind.Prompt && _s.Kind != LibraryItemKind.Shell) return;
            var options = new List<SelectorOption>
            {
                new SelectorOption("Host", () => { _s.Host = true; _s.AgentTemplate = ""; }),
            };
            options.AddRange(SessionHub.Instance.Catalog.Templates.Select(t =>
                new SelectorOption("Agent template: " + t.Name,
                    () => { _s.Host = false; _s.AgentTemplate = t.Name; })));
            string label = _s.Host ? "Host" : string.IsNullOrEmpty(_s.AgentTemplate)
                ? "Choose Host or an agent template..." : "Agent template: " + _s.AgentTemplate;
            UiControls.Select(l, "Run using", label, options, out _, openMenu: TerminalWindow.OpenOverPane);
        }

        void DrawFileActionMode(Listing_Standard l)
        {
            if (_s.Kind != LibraryItemKind.FileAction) return;
            UiControls.Select(l, "After choosing the file action",
                FileActionModeText.Label(_s.Mode), FileActionModeOptions(), out _,
                openMenu: TerminalWindow.OpenOverPane);
        }

        void DrawExplanation(Listing_Standard l, LibraryItemKindDescriptor kind)
        {
            var project = SessionHub.Instance.Project(_s.Project);
            GUI.color = UiTheme.Dim;
            l.Label(kind.ExplainText(this, project));
            GUI.color = Color.white;
        }

        void DrawCommand(Listing_Standard l, LibraryItemKindDescriptor kind)
        {
            l.Gap(UiTheme.GapS);
            if (kind.CommandLabel == null)
            {
                // Breadcrumbs have one text editor below, just like prompts. Keeping a
                // second Area here caused the lower editor to overwrite this value.
                _s.Command = "";
                return;
            }

            l.Label(kind.CommandLabel);
            var box = UiControls.FieldRect(l);
            if (!string.IsNullOrEmpty((_s.Command ?? "").Trim()))
            {
                _s.Command = UiText.Field(box, "library.command", _s.Command,
                    on: CurrentKindEnabled);
                return;
            }

            string placeholder = _s.Kind == LibraryItemKind.Prompt && !string.IsNullOrEmpty(_s.AgentTemplate)
                ? "From agent template" : kind.CommandPlaceholder(this);
            GUI.color = UiTheme.Faint;
            string shown = UiText.Field(box, "library.command", placeholder,
                on: CurrentKindEnabled);
            GUI.color = Color.white;
            if (shown != placeholder) _s.Command = shown;
        }

        float DrawTextEditor(Rect rect, float y)
        {
            UiLayout.SectionHeading(new Rect(rect.x, y, rect.width, UiTheme.RowH),
                _s.Kind == LibraryItemKind.Shell || _s.Kind == LibraryItemKind.FileAction ? "Command line" :
                _s.Kind == LibraryItemKind.Breadcrumb ? "Breadcrumb text" : "Prompt");
            y += UiTheme.RowH + UiTheme.GapXS;

            if (_s.Kind != LibraryItemKind.FileAction)
            {
                var area = new Rect(rect.x, y, rect.width,
                    rect.yMax - UiTheme.BtnH - UiTheme.GapS - y);
                _s.Text = UiText.Area(area, "library.text", _s.Text ?? "",
                    on: CurrentKindEnabled);
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
            if (foot.Right("Save", UiTheme.Btn.Primary, CurrentKindEnabled)) Save();
        }

        // The three answers, in the words the dropdown shows them in.
        public static string LinkLabel(LibraryItemLink l)
        {
            switch (l)
            {
                case LibraryItemLink.Temp: return "A new temporary project each run";
                case LibraryItemLink.Ask: return "Ask me every time";
                default: return "One project, named below";
            }
        }

        // What this errand will actually do with the ground it is given, which is the
        // part the two dropdowns together do not say outright.
        string Explain(ProjectInfo project)
        {
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

        IEnumerable<SelectorOption> KindOptions()
        {
            return new[]
            {
                new SelectorOption("Prompt - say something to an agent",
                    () => { _s.Kind = LibraryItemKind.Prompt; _s.Mode = FileActionMode.Ask; }),
                new SelectorOption("Shell - run a command",
                    () => { _s.Kind = LibraryItemKind.Shell; _s.Mode = FileActionMode.Ask; }),
                new SelectorOption("Breadcrumb - insert manually",
                    () => { _s.Kind = LibraryItemKind.Breadcrumb; _s.Mode = FileActionMode.Ask; _s.Link = LibraryItemLink.Project; _s.Project = ""; }),
                new SelectorOption("File action - run on a Files row",
                    () => { _s.Kind = LibraryItemKind.FileAction; _s.Mode = FileActionMode.Ask; _s.Link = LibraryItemLink.Project; _s.Project = ""; _s.Text = ""; }),
            };
        }

        IEnumerable<SelectorOption> ProjectOptions(bool breadcrumb)
        {
            var options = SessionHub.Instance.Projects
                .Select(p => new SelectorOption($"{p.Name}  -  {p.Dir}",
                    () => _s.Project = p.Name)).ToList();
            if (breadcrumb || _s.Link == LibraryItemLink.Temp)
                options.Insert(0, new SelectorOption("None", () => _s.Project = ""));
            options.Add(new SelectorOption("New project...",
                () => TerminalWindow.OpenOverPane(new EditProjectDialog(null))));
            return options;
        }

        void Save()
        {
            if (!CurrentKindEnabled) return;
            if (string.IsNullOrEmpty((_s.Name ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a library entry needs a name.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (_s.Kind != LibraryItemKind.Breadcrumb && _s.Kind != LibraryItemKind.FileAction && _s.Link == LibraryItemLink.Project &&
                string.IsNullOrEmpty((_s.Project ?? "").Trim()))
            {
                Messages.Message("SlopWorld: pick a project, or a way to choose one.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if ((_s.Kind == LibraryItemKind.Prompt || _s.Kind == LibraryItemKind.Shell) && !_s.Host && string.IsNullOrWhiteSpace(_s.AgentTemplate))
            {
                UiLayout.Fail("Choose Host or an agent template for this entry.");
                return;
            }
            if (_s.Kind == LibraryItemKind.FileAction && string.IsNullOrEmpty((_s.Command ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a file action needs a command.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (_s.Kind != LibraryItemKind.FileAction && string.IsNullOrEmpty((_s.Text ?? "").Trim()))
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
