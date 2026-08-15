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

        // Two lines, and the second of them holds a row button rather than a bare line -
        // so the pitch is measured off what is actually in it. At two line heights the
        // buttons on the bottom line hung a few pixels past the row they belong to.
        protected override float RowH =>
            SlopWidgets.GapXS + SlopWidgets.LineH + SlopWidgets.RowBtnH + SlopWidgets.GapXS + 4f;

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

            float y = r.y + DrawIdentity(r, s);
            DrawLocation(new Rect(r.x, y, r.width, SlopWidgets.LineH), s);
            DrawDialogActions(r, s);
            DrawSessionActions(new Rect(r.x, y, r.width, SlopWidgets.RowBtnH), s);
        }

        float DrawIdentity(Rect r, SessionInfo s)
        {
            // State chip, so the list scans the same way the map overlay does.
            var chip = new Rect(r.x + 6f, r.y + 6f, 10f, r.height - 12f);
            Slab.Fill(chip, TerminalWindow.StateColor(s.State));

            Text.Font = GameFont.Small;
            float l1 = r.y + SlopWidgets.GapXS;

            // The name's column and the state's beside it, off the font: at 200 and 230 the
            // pair held for one face at one size and overlapped at the next.
            float nameW = Mathf.Max(SlopWidgets.Wide("mmmmmmmmmmmmmmmm"), 200f);
            float stateX = r.x + 24f + nameW + SlopWidgets.GapM;

            GUI.color = SlopWidgets.Lead;
            SlopWidgets.RowLabel(new Rect(r.x + 24f, l1, nameW, SlopWidgets.LineH), s.Name);

            // The state in words next to the name, so the row scans without decoding the
            // color of the chip beside it.
            GUI.color = TerminalWindow.StateColor(s.State);
            SlopWidgets.RowLabel(new Rect(stateX, l1, SlopWidgets.Wide("connecting") + 4f,
                SlopWidgets.LineH), s.State.ToString().ToLower());
            GUI.color = SlopWidgets.Dim;

            return SlopWidgets.GapXS + SlopWidgets.LineH;
        }

        float DrawLocation(Rect r, SessionInfo s)
        {
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
            // Cut rather than wrapped: this is the one line here with spaces in it, and a
            // sentence that wrapped inside a one-line slot lost the bottom half of both lines
            // and the buttons' row with them.
            SlopWidgets.RowLabel(
                new Rect(r.x + 24f, r.y, Mathf.Max(60f, r.width - 340f), SlopWidgets.LineH),
                where);
            GUI.color = Color.white;

            return SlopWidgets.LineH;
        }

        float DrawDialogActions(Rect r, SessionInfo s)
        {
            float top = r.y + 1f;
            float right = r.xMax - 6f;

            // The top line is the two buttons that open a dialog, the bottom one everything
            // that acts on the agent directly, terminal last.
            // Every width here is measured, floored at the figure it used to be written as:
            // "Duplicate" in a box counted off one font is a word with both ends cut off in
            // the next, a press being centred in what it was given.
            float dupW = SlopWidgets.BtnW("Duplicate", 74f);
            float editW = SlopWidgets.BtnW("Edit", 96f);

            // Nothing in config.toml stands behind a temporary agent, so the dialog would
            // write an entry the daemon has never had and the save would be refused.
            if (!s.Ephemeral &&
                SlopWidgets.Button(
                    new Rect(right - dupW - SlopWidgets.GapXS - editW, top, editW,
                        SlopWidgets.RowBtnH), "Edit"))
                TerminalWindow.OpenOverPane(new EditSessionDialog(s));

            // Next to Edit rather than down with Del and Start, because what it does is open
            // the same dialog. It is the one of the two that still means something for a
            // temporary agent - "keep this one".
            if (!string.IsNullOrEmpty(s.Project))
            {
                var dup = new Rect(right - dupW, top, dupW, SlopWidgets.RowBtnH);
                TooltipHandler.TipRegion(dup, s.Ephemeral
                    ? $"A permanent agent in {s.Project}, like the one running this errand."
                    : $"New agent with '{s.Name}'s project and command, under a new name.");
                if (SlopWidgets.Button(dup, "Duplicate"))
                    TerminalWindow.OpenOverPane(EditSessionDialog.Copy(s));
            }

            return SlopWidgets.RowBtnH;
        }

        float DrawSessionActions(Rect r, SessionInfo s)
        {
            float bottom = r.y;
            float right = r.xMax - 6f;
            float termW = SlopWidgets.RowBtnH;

            var term = new Rect(right - termW, bottom, termW, SlopWidgets.RowBtnH);
            TooltipHandler.TipRegion(term, s.Gone
                ? $"'{s.Name}' is not running - start it first."
                : $"Open the terminal for '{s.Name}'.");
            if (SlopWidgets.IconButton(term, Icons.Terminal, SlopWidgets.Name, !s.Gone))
                TerminalWindow.Open(s.Name);

            float x = right - termW;

            float runW = SlopWidgets.BtnW(s.Alive ? "Stop" : "Start", 58f);
            x -= runW + SlopWidgets.GapXS;
            if (s.Alive)
            {
                if (SlopWidgets.Button(new Rect(x, bottom, runW, SlopWidgets.RowBtnH), "Stop"))
                    SessionHub.Instance.Stop(s.Name, SlopWidgets.Fail);
            }
            else if (SlopWidgets.Button(new Rect(x, bottom, runW, SlopWidgets.RowBtnH), "Start",
                         SlopWidgets.Btn.Primary))
            {
                SessionHub.Instance.Start(s.Name, SlopWidgets.Fail);
            }

            // Stop is Del for a temporary agent: killing the process is what removes it.
            float resetW = SlopWidgets.BtnW("Reset", 58f);
            x -= resetW + SlopWidgets.GapXS;
            if (!s.Ephemeral && SlopWidgets.Button(new Rect(x, bottom, resetW, SlopWidgets.RowBtnH),
                                    "Reset", SlopWidgets.Btn.Ghost))
            {
                var name = s.Name;
                TerminalWindow.OpenOverPane(SlopConfirmDialog.Create(
                    $"Reset private state for '{name}'? This stops the agent and gives its tools " +
                    "a fresh state on next start. The old state stays recoverable for 14 days.",
                    () => SessionHub.Instance.ResetState(name, SlopWidgets.Fail), destructive: true));
            }

            float delW = SlopWidgets.BtnW("Del", 48f);
            x -= delW + SlopWidgets.GapXS;
            if (!s.Ephemeral && SlopWidgets.Button(new Rect(x, bottom, delW, SlopWidgets.RowBtnH),
                                    "Del", SlopWidgets.Btn.Danger))
            {
                var name = s.Name;
                TerminalWindow.OpenOverPane(SlopConfirmDialog.Create(
                    $"Remove session '{name}'? This kills it, drops it from config.toml, and moves " +
                    "its private state to recoverable trash for 14 days.",
                    () => SessionHub.Instance.Remove(name, SlopWidgets.Fail),
                    destructive: true));
            }

            return SlopWidgets.RowBtnH;
        }
    }
}
