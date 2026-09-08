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
                            ? "Sandbox to copy (blank = plain: private network, no presets)"
                            : "Project (the directory and sandbox it runs in)",
                        dialog => string.IsNullOrEmpty(dialog._s.Project)
                            ? (dialog._s.Link == LibraryItemLink.Temp ? "None" : "Pick a project...")
                            : dialog._s.Project,
                        (dialog, project) => dialog.Explain(project),
                        "Agent (blank = the default)", dialog => dialog._agentDefault)
                },
                {
                    LibraryItemKind.Shell,
                    new LibraryItemKindDescriptor(
                        "Shell - run a command", true,
                        dialog => dialog._s.Link != LibraryItemLink.Ask,
                        dialog => dialog._s.Link == LibraryItemLink.Temp
                            ? "Sandbox to copy (blank = plain: private network, no presets)"
                            : "Project (the directory and sandbox it runs in)",
                        dialog => string.IsNullOrEmpty(dialog._s.Project)
                            ? (dialog._s.Link == LibraryItemLink.Temp ? "None" : "Pick a project...")
                            : dialog._s.Project,
                        (dialog, project) => dialog.Explain(project),
                        "Shell (blank = the default)", dialog => dialog._shellDefault)
                },
                {
                    LibraryItemKind.Breadcrumb,
                    new LibraryItemKindDescriptor(
                        "Breadcrumb - append to the first prompt", false,
                        dialog => true,
                        dialog => "Project (attach to every agent in this project)",
                        dialog => string.IsNullOrEmpty(dialog._s.Project) ? "None" : dialog._s.Project,
                        (dialog, project) =>
                            "Attach this text to projects and agents; it is not runnable.",
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

        readonly bool _isNew;
        readonly LibraryItemInfo _s;
        // The edit is addressed to it, and a changed name in the field is a rename.
        readonly string _origName;
        // Set only for a duplicate, so the title can distinguish copying from editing.
        readonly string _copiedFrom;

        // Asked for rather than assumed: `[defaults] shell` is a per-machine answer and
        // this dialog would otherwise print somebody else's.
        string _agentDefault = "claude";
        string _shellDefault = "bash";

        public EditLibraryItemDialog(LibraryItemInfo existing) : this(existing, false) { }

        public static EditLibraryItemDialog Copy(LibraryItemInfo of) =>
            new EditLibraryItemDialog(of, true);

        EditLibraryItemDialog(LibraryItemInfo existing, bool copy)
        {
            // A duplicate is a new daemon entry: it must POST rather than PUT, and its
            // name is suggested rather than copied so saving it cannot collide by default.
            _isNew = existing == null || copy;
            _origName = copy ? "" : (existing?.Name ?? "");
            _copiedFrom = copy ? existing.Name : null;
            _s = existing?.Copy() ?? new LibraryItemInfo();
            if (copy)
                _s.Name = UiWidgets.FreeName(_s.Name,
                    SessionHub.Instance.Library.Select(s => s.Name), "library");


            SessionHub.Instance.RefreshProjects();
            DaemonClient.Get(WireContract.Routes.Config, j =>
            {
                var d = j["values"]["defaults"];
                _agentDefault = d["agent"].AsString("claude");
                _shellDefault = d["shell"].AsString("bash");
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
        public override Vector2 InitialSize => new Vector2(560f, 660f);

        protected override void DoBody(Rect rect)
        {
            // One column, on the room it has: a Listing_Standard begun on a rect too short
            // for its contents does not overflow, it breaks to a column off the right-hand
            // edge and puts CurHeight back to nearly zero - and the prompt box below is
            // placed and sized from that number. See EditProjectDialog.DoFields.
            UiWidgets.Title(TitleRect(rect), _copiedFrom != null
                ? $"Copy of '{_copiedFrom}'"
                : _isNew ? "New library entry" : $"Edit '{_origName}'");

            float head = UiWidgets.HeaderH + UiWidgets.GapS;
            float used = DrawFields(new Rect(rect.x, rect.y + head, rect.width, rect.height - head));
            float y = rect.y + head + used + UiWidgets.GapL;
            DrawTextEditor(rect, y);
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
            DrawExplanation(l, kind);
            DrawCommand(l, kind);

            float used = l.CurHeight;
            l.End();
            return used;
        }

        void DrawName(Listing_Standard l)
        {
            l.Label("Name (also what the temporary colonist is called)");
            _s.Name = UiWidgets.Field(l, "library.name", _s.Name);
        }

        void DrawKindAndLink(Listing_Standard l, LibraryItemKindDescriptor kind)
        {
            UiWidgets.Select(l, "Kind", kind.ButtonLabel, KindOptions(), out _,
                openMenu: TerminalWindow.OpenOverPane);

            if (!kind.ShowWhere) return;
            UiWidgets.Select(l, "Where it runs", LinkLabel(_s.Link), LinkOptions(), out _,
                openMenu: TerminalWindow.OpenOverPane);
        }

        void DrawProject(Listing_Standard l, LibraryItemKindDescriptor kind)
        {
            // The project dropdown stays up for every kind that uses a project. In temp mode
            // it still answers which sandbox the scratch project is given.
            if (!kind.ShowProject(this)) return;
            UiWidgets.Select(l, kind.ProjectLabel(this), kind.ProjectValue(this),
                ProjectOptions(_s.Kind == LibraryItemKind.Breadcrumb), out _,
                openMenu: TerminalWindow.OpenOverPane);
        }

        void DrawFileActionMode(Listing_Standard l)
        {
            if (_s.Kind != LibraryItemKind.FileAction) return;
            UiWidgets.Select(l, "After choosing the file action",
                FileActionModeText.Label(_s.Mode), FileActionModeOptions(), out _,
                openMenu: TerminalWindow.OpenOverPane);
        }

        void DrawExplanation(Listing_Standard l, LibraryItemKindDescriptor kind)
        {
            var project = SessionHub.Instance.Project(_s.Project);
            GUI.color = UiWidgets.Dim;
            l.Label(kind.ExplainText(this, project));
            GUI.color = Color.white;
        }

        void DrawCommand(Listing_Standard l, LibraryItemKindDescriptor kind)
        {
            l.Gap(UiWidgets.GapS);
            if (kind.CommandLabel == null)
            {
                // Breadcrumbs have one text editor below, just like prompts. Keeping a
                // second Area here caused the lower editor to overwrite this value.
                _s.Command = "";
                return;
            }

            l.Label(kind.CommandLabel);
            var box = UiWidgets.FieldRect(l);
            if (!string.IsNullOrEmpty((_s.Command ?? "").Trim()))
            {
                _s.Command = UiWidgets.Field(box, "library.command", _s.Command);
                return;
            }

            string placeholder = kind.CommandPlaceholder(this);
            GUI.color = UiWidgets.Faint;
            string shown = UiWidgets.Field(box, "library.command", placeholder);
            GUI.color = Color.white;
            if (shown != placeholder) _s.Command = shown;
        }

        float DrawTextEditor(Rect rect, float y)
        {
            UiWidgets.SectionHeading(new Rect(rect.x, y, rect.width, UiWidgets.RowH),
                _s.Kind == LibraryItemKind.Shell || _s.Kind == LibraryItemKind.FileAction ? "Command line" :
                _s.Kind == LibraryItemKind.Breadcrumb ? "Breadcrumb text" : "Prompt");
            y += UiWidgets.RowH + UiWidgets.GapXS;

            if (_s.Kind != LibraryItemKind.FileAction)
            {
                var area = new Rect(rect.x, y, rect.width,
                    rect.yMax - UiWidgets.BtnH - UiWidgets.GapS - y);
                _s.Text = UiWidgets.Area(area, "library.text", _s.Text ?? "");
            }
            else
            {
                _s.Text = "";
            }
            return rect.yMax - y;
        }

        void DrawFooter(Rect rect)
        {
            var foot = new UiWidgets.Bar(UiWidgets.FooterBar(rect));
            if (foot.Left("Cancel", UiWidgets.Btn.Ghost)) Close();
            if (foot.Right("Save", UiWidgets.Btn.Primary)) Save();
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
                               ? $", sandboxed like '{project.Name}'."
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
                new SelectorOption("Breadcrumb - append to the first prompt",
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

            SessionHub.Instance.SaveLibraryItem(_s, _isNew, _origName,
                ok: () => Close(),
                fail: msg => Messages.Message($"SlopWorld: {msg}",
                    MessageTypeDefOf.RejectInput, false));
        }
    }
}
