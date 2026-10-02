using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SlopWorld
{
    // Activated file links resolve against the daemon before opening file actions.
    sealed partial class TerminalPanel
    {
        // Resolve only on activation, never during terminal repaint or hover.
        internal void OpenPathMenu(string path, int line)
        {
            var info = SessionHub.Instance.Get(_state.Name);
            string project = info?.Project;
            if (string.IsNullOrEmpty(project)) return;
            var owner = SessionHub.Instance.Project(project);
            if (owner != null)
            {
                SidebarScopes.Update();
                project = BrowseScope.Identity(BrowseScope.ProjectIdOf(owner), string.IsNullOrEmpty(info.Worktree) ? "main" : info.Worktree);
            }
            SessionHub.Instance.SessionStore.CurrentPath(_state.Name, cwd =>
                LoadPathMenu(project, FilesView.ResolveProjectPath(project, path, cwd), line));
        }

        void LoadPathMenu(string project, string absolute, int line)
        {
            if (absolute == null) return;

            // Ask the daemon host, not the game machine. Include hidden and ignored entries:
            // terminal links must not depend on the sidebar's current listing filters.
            absolute = absolute.TrimEnd('/');
            string parent = absolute.Substring(0, absolute.LastIndexOf('/'));
            if (parent.Length == 0) parent = "/";
            string name = Leaf(absolute);
            DaemonClient.Get<Wire.BrowseResult>(WireProtocol.Routes.Browse +
                "?files=1&hidden=1&gitignore=0&path=" + System.Uri.EscapeDataString(parent),
                listing =>
                {
                    bool directory = listing.Dirs.Contains(name);
                    bool file = listing.Files.Contains(name);
                    if (!directory && !file && !listing.Truncated)
                    {
                        string candidate = PathScan.BeforeProseDash(name);
                        if (candidate != null)
                        {
                            directory = listing.Dirs.Contains(candidate);
                            file = listing.Files.Contains(candidate);
                            if (directory || file)
                                absolute = parent.TrimEnd('/') + "/" + candidate;
                        }
                    }
                    if (directory || file)
                        ShowPathMenu(project, absolute, line, file);
                });
        }

        void ShowPathMenu(string project, string absolute, int line, bool isFile)
        {
            var options = new List<FloatMenuOption>();
            string name = Leaf(absolute);
            options.Add(new FloatMenuOption("Focus", () => FilesView.FocusPath(project, absolute)));
            if (isFile && FilesView.IsText(name))
            {
                options.Add(new FloatMenuOption("View", () =>
                    FilesView.ViewFile(project, absolute, "view-" + name, line)));
                options.Add(new FloatMenuOption("Edit", () =>
                    FilesView.EditFile(project, absolute, "edit-" + name, line)));
            }
            FilesView.AddOpenIn(options, absolute, project);
            FilesView.AddFileActions(options, project, absolute, name);
            options.Add(new FloatMenuOption("Copy path", () => CopyText(absolute)));
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        static string Leaf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

    }
}
