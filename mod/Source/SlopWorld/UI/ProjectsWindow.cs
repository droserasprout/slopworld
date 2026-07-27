using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Where the work is. A project is a directory plus the sandbox every agent
    /// in it gets: three agents in one repo want the same binds, and keeping that
    /// in three session entries meant it was wrong in at least one of them.
    ///
    /// First button in the bottom bar for the same reason: nothing can be added
    /// on the agents window until there is somewhere to add it.
    /// </summary>
    public class ProjectsWindow : Window
    {
        const float RowH = 52f;

        Vector2 _scroll;

        public static void Toggle()
        {
            var open = Find.WindowStack.WindowOfType<ProjectsWindow>();
            if (open != null) { open.Close(); return; }

            SessionHub.Instance.RefreshProjects();
            SessionHub.Instance.LoadPresets();
            Find.WindowStack.Add(new ProjectsWindow());
        }

        public ProjectsWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(720f, 480f);

        public override void DoWindowContents(Rect rect)
        {
            var hub = SessionHub.Instance;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, 300f, 32f), "Projects");
            Text.Font = GameFont.Small;

            GUI.color = hub.Online ? new Color(0.5f, 0.8f, 0.5f) : new Color(0.9f, 0.5f, 0.5f);
            Widgets.Label(new Rect(rect.x + 110f, rect.y + 8f, 400f, 24f),
                $"{SlopClient.BaseUrl} - {hub.Status}");
            GUI.color = Color.white;

            float top = rect.y + 40f;
            DrawList(new Rect(rect.x, top, rect.width, rect.height - top - 40f), hub);

            var bar = new Rect(rect.x, rect.yMax - 32f, rect.width, 30f);
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 130f, 30f), "Add project"))
                Find.WindowStack.Add(new EditProjectDialog(null));

            if (Widgets.ButtonText(new Rect(bar.x + 138f, bar.y, 130f, 30f), "Agents"))
                SessionsWindow.Toggle();

            if (Widgets.ButtonText(new Rect(bar.x + 276f, bar.y, 130f, 30f), "Reload"))
                hub.RefreshProjects(Fail);
        }

        void DrawList(Rect rect, SessionHub hub)
        {
            var view = new Rect(0f, 0f, rect.width - 18f, hub.Projects.Count * RowH + 4f);

            Widgets.BeginScrollView(rect, ref _scroll, view);

            if (hub.Projects.Count == 0)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(4f, 8f, view.width - 8f, 48f),
                    hub.Online
                        ? "No projects yet. Add one, then put an agent in it."
                        : "Daemon unreachable. Is slopd running?  systemctl --user status slopd");
                GUI.color = Color.white;
            }

            float y = 0f;
            foreach (var p in hub.Projects.ToList())
            {
                DrawRow(new Rect(0f, y, view.width, RowH - 4f), p, hub);
                y += RowH;
            }

            Widgets.EndScrollView();
        }

        void DrawRow(Rect r, ProjectInfo p, SessionHub hub)
        {
            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.03f));
            Widgets.DrawHighlightIfMouseover(r);

            Widgets.Label(new Rect(r.x + 8f, r.y + 4f, 200f, 22f), p.Name);

            // How many agents live here, because it is the number that decides
            // whether this project can be deleted at all.
            int agents = hub.Sessions.Count(s => s.Project == p.Name);
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            Widgets.Label(new Rect(r.x + 214f, r.y + 4f, 120f, 22f),
                agents == 1 ? "1 agent" : $"{agents} agents");

            Widgets.Label(new Rect(r.x + 8f, r.y + 24f, r.width - 150f, 20f),
                $"{p.Dir}  ({Summary(p)})");
            GUI.color = Color.white;

            float right = r.xMax - 6f;

            if (Widgets.ButtonText(new Rect(right - 120f, r.y + 4f, 120f, 20f), "Edit"))
                Find.WindowStack.Add(new EditProjectDialog(p));

            if (Widgets.ButtonText(new Rect(right - 120f, r.y + 26f, 120f, 20f), "Delete"))
            {
                var name = p.Name;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Remove project '{name}'? The directory is left alone; only the entry " +
                    "in config.toml goes.",
                    () => SessionHub.Instance.RemoveProject(name, Fail),
                    destructive: true));
            }
        }

        /// <summary>The sandbox in one line, the way the agent rows read it.</summary>
        public static string Summary(ProjectInfo p)
        {
            var bits = new List<string>();
            // First, because it is the one thing here that is about the ground
            // rather than about the sandbox around it.
            if (p.Temp) bits.Add("temporary");
            if (!p.Sandbox)
            {
                bits.Add("unsandboxed");
                return string.Join(", ", bits.ToArray());
            }
            bits.Add("bwrap");
            if (!p.Net) bits.Add("no net");
            bits.AddRange(p.Presets);
            int extra = p.RoPaths.Count + p.RwPaths.Count;
            if (extra > 0) bits.Add(extra == 1 ? "+1 bind" : $"+{extra} binds");
            return string.Join(", ", bits.ToArray());
        }

        static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);
    }

    /// <summary>
    /// Add or edit one project. Presets are checkboxes drawn from whatever the
    /// daemon says it knows, so this window never has to be kept in step with
    /// the table in sandbox.rs by hand.
    /// </summary>
    public class EditProjectDialog : Window
    {
        readonly bool _isNew;
        readonly ProjectInfo _p;
        /// The name the daemon still knows this project by; a changed name in
        /// the field is a rename, and the daemon carries its sessions over.
        readonly string _origName;

        string _roPaths, _rwPaths, _passEnv;
        Vector2 _scroll;

        public EditProjectDialog(ProjectInfo existing)
        {
            _isNew = existing == null;
            _origName = existing?.Name ?? "";
            _p = existing?.Copy() ?? new ProjectInfo();

            _roPaths = Lines(_p.RoPaths);
            _rwPaths = Lines(_p.RwPaths);
            _passEnv = Lines(_p.PassEnv);

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;

            SessionHub.Instance.LoadPresets();
        }

        public override Vector2 InitialSize => new Vector2(680f, 640f);

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f),
                _isNew ? "New project" : $"Edit '{_origName}'");
            Text.Font = GameFont.Small;

            var body = new Rect(rect.x, rect.y + 38f, rect.width, rect.height - 38f - 40f);
            var view = new Rect(0f, 0f, body.width - 18f, 690f);

            Widgets.BeginScrollView(body, ref _scroll, view);
            DoFields(view);
            Widgets.EndScrollView();

            var bar = new Rect(rect.x, rect.yMax - 34f, rect.width, 32f);
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 120f, 32f), "Cancel"))
                Close();
            if (Widgets.ButtonText(new Rect(bar.xMax - 120f, bar.y, 120f, 32f), "Save"))
                Save();
        }

        void DoFields(Rect r)
        {
            // Begun on the room it actually has, and pinned to one column.
            // Listing_Standard breaks to a second column the moment a control
            // would cross the bottom of the rect it was begun on - and a column
            // break here means curX past the whole width (so the rest of the
            // fields are clipped away by the group Begin opened) *and* CurHeight
            // back to nearly nothing. Everything below is laid out from that
            // number, so a listing begun one control too short does not overflow:
            // it drops the presets and the three path boxes on top of the fields,
            // the last of them sized from a y that is suddenly 20 instead of 200.
            // A fixed height was carrying that fault the whole time; the
            // temporary checkbox is only what tipped it over.
            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(r.x, r.y, r.width, r.height));

            l.Label("Name");
            _p.Name = l.TextEntry(_p.Name);

            l.Gap(4f);
            l.CheckboxLabeled("Temporary - scratch space under /tmp", ref _p.Temp,
                "The directory is made for you under " + ProjectInfo.TempRoot + ", named after " +
                "this project, and it is there the first time an agent starts. Nothing " +
                "deletes it; the machine clears /tmp.");

            l.Gap(4f);
            l.Label("Directory");
            if (_p.Temp)
            {
                // Greyed rather than hidden, the same as the agent dialog's
                // command box: what this project will actually work in is worth
                // reading off the field even when nothing here typed it. Browse
                // goes with it - there is nothing to find yet.
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Widgets.TextField(l.GetRect(28f), ProjectInfo.TempDir(_p.Name));
                GUI.color = Color.white;
            }
            else
            {
                _p.Dir = l.TextEntry(_p.Dir);
                if (l.ButtonText("Browse..."))
                    Find.WindowStack.Add(new BrowseDialog(_p.Dir, d => _p.Dir = d));
            }

            l.Gap(6f);
            l.CheckboxLabeled("Sandbox with bubblewrap", ref _p.Sandbox,
                "Off means every agent in this project runs with your full user account.");
            l.CheckboxLabeled("Allow network", ref _p.Net);

            float used = l.CurHeight;
            l.End();

            float y = r.y + used + 10f;
            Widgets.Label(new Rect(r.x, y, r.width, 22f), "Sandbox presets");
            y += 24f;

            var presets = SessionHub.Instance.Presets;
            if (presets.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(r.x, y, r.width, 22f),
                    "The daemon has not sent its preset list yet.");
                GUI.color = Color.white;
                y += 24f;
            }

            // Two columns: there are a dozen of these and stacking them would
            // push the path boxes off the bottom of the dialog.
            float colW = r.width / 2f;
            for (int i = 0; i < presets.Count; i++)
            {
                var pr = presets[i];
                var cell = new Rect(r.x + (i % 2) * colW, y + (i / 2) * 24f, colW - 8f, 22f);

                bool on = _p.Presets.Contains(pr.Name);
                bool was = on;
                Widgets.CheckboxLabeled(cell, pr.Name, ref on);
                TooltipHandler.TipRegion(cell,
                    $"{pr.Description}\n\n{string.Join("\n", pr.Gives.ToArray())}");

                if (on != was)
                {
                    if (on) _p.Presets.Add(pr.Name);
                    else _p.Presets.Remove(pr.Name);
                }
            }
            y += ((presets.Count + 1) / 2) * 24f + 10f;

            // A Claude agent gets this one whether or not it is ticked here, and
            // saying so is cheaper than the player wondering why ~/.claude is
            // bound in a project that never asked for it.
            GUI.color = new Color(0.65f, 0.66f, 0.68f);
            Widgets.Label(new Rect(r.x, y, r.width, 22f),
                "Claude Code agents always get the 'claude' preset, project or not.");
            GUI.color = Color.white;
            y += 26f;

            float boxW = (r.width - 16f) / 3f;
            float boxH = r.yMax - y - 8f;
            _roPaths = PathList(new Rect(r.x, y, boxW, boxH), "Read-only binds", _roPaths);
            _rwPaths = PathList(new Rect(r.x + boxW + 8f, y, boxW, boxH),
                "Read-write binds", _rwPaths);
            _passEnv = PathList(new Rect(r.x + (boxW + 8f) * 2f, y, boxW, boxH),
                "Passed env vars", _passEnv);
        }

        /// <summary>One entry per line, the way the sandbox tab edits these.</summary>
        static string PathList(Rect r, string label, string text)
        {
            Widgets.Label(new Rect(r.x, r.y, r.width, 22f), label);
            var box = new Rect(r.x, r.y + 24f, r.width, Mathf.Max(r.height - 24f, 60f));
            Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.25f));
            return Widgets.TextArea(box.ContractedBy(4f), text);
        }

        void Save()
        {
            _p.RoPaths = Split(_roPaths);
            _p.RwPaths = Split(_rwPaths);
            _p.PassEnv = Split(_passEnv);

            if (string.IsNullOrEmpty((_p.Name ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a project needs a name.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            // A temporary project's directory is the daemon's to coin, and it
            // coins it again on the way in - this is only so the list has the
            // right path in it before the answer comes back.
            if (_p.Temp) _p.Dir = ProjectInfo.TempDir(_p.Name);
            else if (string.IsNullOrEmpty((_p.Dir ?? "").Trim()))
            {
                Messages.Message("SlopWorld: a project needs a directory.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            SessionHub.Instance.SaveProject(_p, _isNew, _origName,
                ok: () => Close(),
                fail: msg => Messages.Message($"SlopWorld: {msg}",
                    MessageTypeDefOf.RejectInput, false));
        }

        static string Lines(List<string> items) => string.Join("\n", items.ToArray());

        static List<string> Split(string text) =>
            (text ?? "").Split('\n')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
    }
}
