using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The colony overview: every session, its state, and the buttons to act on it.
    public class SessionsView : SlopListView<SessionInfo>
    {
        public override void Opened()
        {
            SessionHub.Instance.Refresh();
            // The rows name a project, and the dialog they open picks one.
            SessionHub.Instance.RefreshProjects();
        }

        public override string Title => "Agents";

        protected override float RowH => 2 * SlopWidgets.LineH + SlopWidgets.GapXS + 4f;

        protected override string EmptyNote =>
            "No sessions yet. Add one and it will show up as a colonist.";

        protected override IEnumerable<SessionInfo> Rows => SessionHub.Instance.Sessions;

        protected override void DoFooter(Rect bar, SessionHub hub)
        {
            var row = new SlopWidgets.Bar(bar);

            if (row.Left("Add agent", SlopWidgets.Btn.Primary))
                TerminalWindow.OpenOverPane(new EditSessionDialog(null));

            // Shortcuts is not here: it is a window of its own in the bottom bar, and an
            // errand is not something you do to an agent on this list. Nor is "New colony",
            // for the same reason - it is "Next planet" in the menu behind Escape now.
            if (row.Right("Reconnect", SlopWidgets.Btn.Ghost))
                hub.Connect();
        }

        protected override void DrawRow(Rect r, SessionInfo s)
        {
            SlopWidgets.RowChrome(r);

            // State chip, so the list scans the same way the map overlay does.
            var chip = new Rect(r.x + 6f, r.y + 6f, 10f, r.height - 12f);
            Widgets.DrawBoxSolid(chip, TerminalWindow.StateColor(s.State));

            Text.Font = GameFont.Small;
            float l1 = r.y + SlopWidgets.GapXS, l2 = l1 + SlopWidgets.LineH;

            GUI.color = SlopWidgets.Lead;
            Widgets.Label(new Rect(r.x + 24f, l1, 200f, SlopWidgets.LineH), s.Name);

            // The state in words next to the name, so the row scans without decoding the
            // colour of the chip beside it.
            GUI.color = TerminalWindow.StateColor(s.State);
            Widgets.Label(new Rect(r.x + 230f, l1, 100f, SlopWidgets.LineH),
                s.State.ToString().ToLower());
            GUI.color = SlopWidgets.Dim;

            // The project first, because it answers where this agent runs and what it can
            // reach. A blank project is an entry pointing at one that has gone, which is
            // worth saying; a temporary agent says so instead, the interesting thing about
            // that row being that it is on its way out.
            string where = string.IsNullOrEmpty(s.Project)
                ? (s.Ephemeral
                    ? "temporary - adopted from tmux, and it goes when it exits"
                    : "no project - it will not start")
                : s.Ephemeral
                    ? $"temporary in {s.Project} - it goes when it exits"
                    : $"{s.Project}  -  {s.Dir}";
            Widgets.Label(new Rect(r.x + 24f, l2, r.width - 340f, SlopWidgets.LineH), where);
            GUI.color = Color.white;

            // The top line is the two buttons that open a dialog, the bottom one everything
            // that acts on the agent directly, terminal last.
            float top = r.y + 1f, bottom = l2;
            float right = r.xMax - 6f;

            // Nothing in config.toml stands behind a temporary agent, so the dialog would
            // write an entry the daemon has never had and the save would be refused.
            if (!s.Ephemeral &&
                SlopWidgets.Button(new Rect(right - 174f, top, 96f, SlopWidgets.RowBtnH), "Edit"))
                TerminalWindow.OpenOverPane(new EditSessionDialog(s));

            // Next to Edit rather than down with Del and Start, because what it does is open
            // the same dialog. It is the one of the two that still means something for a
            // temporary agent - "keep this one".
            if (!string.IsNullOrEmpty(s.Project))
            {
                var dup = new Rect(right - 74f, top, 74f, SlopWidgets.RowBtnH);
                TooltipHandler.TipRegion(dup, s.Ephemeral
                    ? $"A permanent agent in {s.Project}, like the one running this errand."
                    : $"New agent with '{s.Name}'s project and command, under a new name.");
                if (SlopWidgets.Button(dup, "Duplicate"))
                    TerminalWindow.OpenOverPane(EditSessionDialog.Copy(s));
            }

            var term = new Rect(right - 22f, bottom, 22f, SlopWidgets.RowBtnH);
            TooltipHandler.TipRegion(term, s.Gone
                ? $"'{s.Name}' is not running - start it first."
                : $"Open the terminal for '{s.Name}'.");
            var was = GUI.color;
            if (s.Gone) GUI.color = new Color(1f, 1f, 1f, 0.35f);
            if (Widgets.ButtonImage(term, Icons.Terminal) && !s.Gone)
                TerminalWindow.Open(s.Name);
            GUI.color = was;

            float x = right - 22f;

            x -= 62f;
            if (s.Alive)
            {
                if (SlopWidgets.Button(new Rect(x, bottom, 58f, SlopWidgets.RowBtnH), "Stop"))
                    SessionHub.Instance.Stop(s.Name, SlopWidgets.Fail);
            }
            else if (SlopWidgets.Button(new Rect(x, bottom, 58f, SlopWidgets.RowBtnH), "Start",
                         SlopWidgets.Btn.Primary))
            {
                SessionHub.Instance.Start(s.Name, SlopWidgets.Fail);
            }

            // Stop is Del for a temporary agent: killing the process is what removes it.
            x -= 52f;
            if (!s.Ephemeral && SlopWidgets.Button(new Rect(x, bottom, 48f, SlopWidgets.RowBtnH), "Del",
                                    SlopWidgets.Btn.Danger))
            {
                var name = s.Name;
                TerminalWindow.OpenOverPane(Dialog_MessageBox.CreateConfirmation(
                    $"Remove session '{name}'? This kills the tmux session and drops it from config.toml.",
                    () => SessionHub.Instance.Remove(name, SlopWidgets.Fail),
                    destructive: true));
            }
        }
    }
}
