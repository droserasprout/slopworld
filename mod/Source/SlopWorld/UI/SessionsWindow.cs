using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>The colony overview: every session, its state, and the buttons to act on it.</summary>
    public class SessionsWindow : Window
    {
        const float RowH = 52f;

        Vector2 _scroll;

        public static void Toggle()
        {
            var open = Find.WindowStack.WindowOfType<SessionsWindow>();
            if (open != null) { open.Close(); return; }

            SessionHub.Instance.Refresh();
            // The rows name a project, and the dialog they open picks one.
            SessionHub.Instance.RefreshProjects();
            Find.WindowStack.Add(new SessionsWindow());
        }

        public SessionsWindow()
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
            Widgets.Label(new Rect(rect.x, rect.y, 300f, 32f), "Agents");
            Text.Font = GameFont.Small;

            // Status line doubles as the daemon health indicator.
            GUI.color = hub.Online ? new Color(0.5f, 0.8f, 0.5f) : new Color(0.9f, 0.5f, 0.5f);
            Widgets.Label(new Rect(rect.x + 90f, rect.y + 8f, 400f, 24f),
                $"{SlopClient.BaseUrl} - {hub.Status}");
            GUI.color = Color.white;

            float top = rect.y + 40f;
            var list = new Rect(rect.x, top, rect.width, rect.height - top - 40f);
            DrawList(list, hub);

            var bar = new Rect(rect.x, rect.yMax - 32f, rect.width, 30f);
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 130f, 30f), "Add agent"))
                Find.WindowStack.Add(new EditSessionDialog(null));

            if (Widgets.ButtonText(new Rect(bar.x + 138f, bar.y, 130f, 30f), "Projects"))
                ProjectsWindow.Toggle();

            // Shortcuts is not here: it is a window of its own in the bottom bar,
            // and an errand is not something you do to an agent on this list -
            // running one lands a new colonist rather than touching any of these.
            if (Widgets.ButtonText(new Rect(bar.x + 276f, bar.y, 130f, 30f), "Reconnect"))
                hub.Connect();

            // Furthest from the rest, because it is the one button here that throws
            // something away rather than changing it.
            if (Widgets.ButtonText(new Rect(bar.xMax - 130f, bar.y, 130f, 30f), "New colony"))
                NewColony.Confirm();
        }

        void DrawList(Rect rect, SessionHub hub)
        {
            var view = new Rect(0f, 0f, rect.width - 18f, hub.Sessions.Count * RowH + 4f);

            Widgets.BeginScrollView(rect, ref _scroll, view);

            if (hub.Sessions.Count == 0)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(new Rect(4f, 8f, view.width - 8f, 48f),
                    hub.Online
                        ? "No sessions yet. Add one and it will show up as a colonist."
                        : "Daemon unreachable. Is slopd running?  systemctl --user status slopd");
                GUI.color = Color.white;
            }

            float y = 0f;
            foreach (var s in hub.Sessions.ToList())
            {
                DrawRow(new Rect(0f, y, view.width, RowH - 4f), s);
                y += RowH;
            }

            Widgets.EndScrollView();
        }

        void DrawRow(Rect r, SessionInfo s)
        {
            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.03f));
            Widgets.DrawHighlightIfMouseover(r);

            // State chip, so the list scans the same way the map overlay does.
            var chip = new Rect(r.x + 6f, r.y + 6f, 10f, r.height - 12f);
            Widgets.DrawBoxSolid(chip, TerminalWindow.StateColor(s.State));

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(r.x + 24f, r.y + 4f, 200f, 22f), s.Name);

            // The state in words next to the name, so the row scans without
            // decoding the colour of the chip beside it.
            GUI.color = TerminalWindow.StateColor(s.State);
            Widgets.Label(new Rect(r.x + 230f, r.y + 4f, 100f, 22f), s.State.ToString().ToLower());
            GUI.color = new Color(0.65f, 0.66f, 0.68f);

            // The project first, because it is the thing that answers where this
            // agent runs and what it can reach; the directory is that answer
            // spelled out. A blank project is an entry pointing at one that has
            // gone, which is worth saying rather than drawing as an empty line.
            // A temporary agent says so instead: there is no entry behind it, so
            // the interesting thing about the row is that it is on its way out.
            string where = string.IsNullOrEmpty(s.Project)
                ? (s.Ephemeral
                    ? "temporary - adopted from tmux, and it goes when it exits"
                    : "no project - it will not start")
                : s.Ephemeral
                    ? $"temporary in {s.Project} - it goes when it exits"
                    : $"{s.Project}  -  {s.Dir}";
            Widgets.Label(new Rect(r.x + 24f, r.y + 24f, r.width - 340f, 20f), where);
            GUI.color = Color.white;

            // The top line is the two buttons that open a dialog, the bottom one
            // everything that acts on the agent directly, terminal last. Both
            // lines end at the same right edge.
            float top = r.y + 4f, bottom = r.y + 26f;
            float right = r.xMax - 6f;

            // Nothing in config.toml stands behind a temporary agent, so there is
            // nothing to edit: the dialog would write an entry the daemon has
            // never had and the save would be refused.
            if (!s.Ephemeral &&
                Widgets.ButtonText(new Rect(right - 174f, top, 96f, 20f), "Edit"))
                Find.WindowStack.Add(new EditSessionDialog(s));

            // Next to Edit rather than down with Del and Start, because what it
            // does is open the same dialog with the same fields in it. It is the
            // one of the two that still means something for a temporary agent -
            // "keep this one" - as long as it knows where it is working.
            if (!string.IsNullOrEmpty(s.Project))
            {
                var dup = new Rect(right - 74f, top, 74f, 20f);
                TooltipHandler.TipRegion(dup, s.Ephemeral
                    ? $"A permanent agent in {s.Project}, like the one running this errand."
                    : $"New agent with '{s.Name}'s project and command, under a new name.");
                if (Widgets.ButtonText(dup, "Duplicate"))
                    Find.WindowStack.Add(EditSessionDialog.Copy(s));
            }

            var term = new Rect(right - 22f, bottom, 22f, 20f);
            TooltipHandler.TipRegion(term, s.Gone
                ? $"'{s.Name}' is not running - start it first."
                : $"Open the terminal for '{s.Name}'.");
            var was = GUI.color;
            if (s.Gone) GUI.color = new Color(1f, 1f, 1f, 0.35f);
            if (Widgets.ButtonImage(term, TerminalIcon.Tex) && !s.Gone)
                TerminalWindow.Open(s.Name);
            GUI.color = was;

            float x = right - 22f;

            x -= 62f;
            if (s.Alive)
            {
                if (Widgets.ButtonText(new Rect(x, bottom, 58f, 20f), "Stop"))
                    SessionHub.Instance.Stop(s.Name, Fail);
            }
            else if (Widgets.ButtonText(new Rect(x, bottom, 58f, 20f), "Start"))
            {
                SessionHub.Instance.Start(s.Name, Fail);
            }

            // Stop is Del for a temporary agent: killing the process is what
            // removes it, and there is no entry left over to delete.
            x -= 52f;
            if (!s.Ephemeral && Widgets.ButtonText(new Rect(x, bottom, 48f, 20f), "Del"))
            {
                var name = s.Name;
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Remove session '{name}'? This kills the tmux session and drops it from config.toml.",
                    () => SessionHub.Instance.Remove(name, Fail),
                    destructive: true));
            }
        }

        static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);
    }
}
