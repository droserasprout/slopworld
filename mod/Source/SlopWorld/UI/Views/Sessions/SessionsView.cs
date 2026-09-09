using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The colony overview: every session, its state, and the buttons to act on it.
    public class SessionsView : UiListView<SessionInfo>
    {
        public override void Opened()
        {
            SessionHub.Instance.SessionStore.Refresh();
            // The rows name a project, and the dialog they open picks one.
            SessionHub.Instance.Catalog.RefreshProjects();
        }

        public override string Title => "Agents";

        // Two lines, and the second of them holds a row button rather than a bare line -
        // so the pitch is measured off what is actually in it. At two line heights the
        // buttons on the bottom line hung a few pixels past the row they belong to.
        protected override float RowH => UiListRow.TwoLineH;

        protected override string EmptyNote =>
            "No sessions yet. Add one and it will show up as a colonist.";

        protected override IList<SessionInfo> Rows => SessionHub.Instance.Sessions;

        protected override void DoFooter(Rect bar, SessionHub hub)
        {
            var row = new UiWidgets.Bar(bar);

            if (row.Left("Add agent", UiWidgets.Btn.Primary))
                TerminalWindow.OpenOverPane(new EditSessionDialog(null));

            // Library is not here: it is a window of its own in the bottom bar, and an
            // errand is not something you do to an agent on this list. Nor is "New colony",
            // for the same reason - it is "Next planet" in the menu behind Escape now.
            if (row.Right("Reconnect", UiWidgets.Btn.Ghost))
                hub.Connect();
        }

        protected override void DrawRow(Rect r, SessionInfo s)
        {
            UiListRow.Prepare(r);

            DrawIdentity(r, s);
            float y = UiListRow.LineY(r, 1);
            DrawLocation(new Rect(r.x, y, r.width, UiWidgets.LineH), s);
            DrawDialogActions(r, s);
            DrawSessionActions(new Rect(r.x, y, r.width, UiWidgets.RowBtnH), s);
        }

        void DrawIdentity(Rect r, SessionInfo s)
        {
            // State chip, so the list scans the same way the map overlay does.
            var chip = new Rect(r.x + 6f, r.y + 6f, 10f, r.height - 12f);
            Slab.Fill(chip, TerminalWindow.StateColor(s.State));

            float l1 = UiListRow.LineY(r, 0);

            // The name's column and the state's beside it, off the font: at 200 and 230 the
            // pair held for one face at one size and overlapped at the next.
            float nameW = Mathf.Max(UiWidgets.Wide("mmmmmmmmmmmmmmmm"), 200f);
            float stateX = r.x + 24f + nameW + UiWidgets.GapM;

            GUI.color = UiWidgets.Lead;
            UiWidgets.RowLabel(new Rect(r.x + 24f, l1, nameW, UiWidgets.LineH), s.Name);

            // The state in words next to the name, so the row scans without decoding the
            // color of the chip beside it.
            GUI.color = TerminalWindow.StateColor(s.State);
            UiWidgets.RowLabel(new Rect(stateX, l1, UiWidgets.Wide("connecting") + 4f,
                UiWidgets.LineH), s.State.ToString().ToLower());
            GUI.color = UiWidgets.Dim;

        }

        float DrawLocation(Rect r, SessionInfo s)
        {
            // The project first, because it answers where this agent runs and what it can
            // reach. A blank project is an entry pointing at one that has gone, which is
            // worth saying; a temporary agent says so instead, the interesting thing about
            // that row being that it is on its way out.
            string terminal = SessionHub.Instance.Capabilities.TerminalName;
            string where = s.Host
                ? (string.IsNullOrEmpty(s.Project)
                    ? $"{terminal}  -  {s.Dir}"
                    : $"{terminal} in {s.Project}  -  {s.Dir}")
                : string.IsNullOrEmpty(s.Project)
                ? (s.Ephemeral
                    ? "temporary - adopted from tmux, and it goes when it exits"
                    : "no project - it will not start")
                : s.Ephemeral
                    ? $"temporary in {s.Project} - it goes when it exits"
                    : $"{s.Project}  -  {s.Dir}";
            // Cut rather than wrapped: this is the one line here with spaces in it, and a
            // sentence that wrapped inside a one-line slot lost the bottom half of both lines
            // and the buttons' row with them.
            UiWidgets.RowLabel(
                new Rect(r.x + 24f, r.y, Mathf.Max(60f, r.width - 340f), UiWidgets.LineH),
                where);
            GUI.color = Color.white;

            return UiWidgets.LineH;
        }

        float DrawDialogActions(Rect r, SessionInfo s)
        {
            float top = r.y + 1f;
            float right = UiListRow.Right(r);

            // The top line is the two buttons that open a dialog, the bottom one everything
            // that acts on the agent directly, terminal last.
            // Every width here is measured, floored at the figure it used to be written as:
            // "Duplicate" in a box counted off one font is a word with both ends cut off in
            // the next, a press being centred in what it was given.
            float dupW = UiWidgets.BtnW("Duplicate", 74f);
            float editW = UiWidgets.BtnW("Edit", 96f);

            // Nothing in config.toml stands behind a temporary agent, so the dialog would
            // write an entry the daemon has never had and the save would be refused.
            if (!s.Ephemeral && !s.Host &&
                UiWidgets.Button(
                    new Rect(right - dupW - UiWidgets.GapXS - editW, top, editW,
                        UiWidgets.RowBtnH), "Edit"))
                TerminalWindow.OpenOverPane(new EditSessionDialog(s));

            // Next to Edit rather than down with Del and Start, because what it does is open
            // the same dialog. It is the one of the two that still means something for a
            // temporary agent - "keep this one".
            if (!s.Host && !string.IsNullOrEmpty(s.Project))
            {
                var dup = new Rect(right - dupW, top, dupW, UiWidgets.RowBtnH);
                TooltipHandler.TipRegion(dup, s.Ephemeral
                    ? $"A permanent agent in {s.Project}, like the one running this errand."
                    : $"New agent with '{s.Name}'s project and command, under a new name.");
                if (UiWidgets.Button(dup, "Duplicate"))
                    TerminalWindow.OpenOverPane(EditSessionDialog.Copy(s));
            }

            return UiWidgets.RowBtnH;
        }

        float DrawSessionActions(Rect r, SessionInfo s)
        {
            float bottom = r.y;
            float right = UiListRow.Right(r);
            float termW = UiWidgets.RowBtnH;

            var term = new Rect(right - termW, bottom, termW, UiWidgets.RowBtnH);
            TooltipHandler.TipRegion(term, s.Gone
                ? $"'{s.Name}' is not running - start it first."
                : $"Open the terminal for '{s.Name}'.");
            if (UiWidgets.IconButton(term, Icons.Terminal, UiWidgets.Name, !s.Gone))
                TerminalWindow.Open(s.Name);

            float x = right - termW;

            float runW = UiWidgets.BtnW(s.Alive ? "Stop" : "Start", 58f);
            x -= runW + UiWidgets.GapXS;
            if (s.Alive)
            {
                if (UiWidgets.Button(new Rect(x, bottom, runW, UiWidgets.RowBtnH), "Stop"))
                    SessionHub.Instance.SessionStore.Stop(s.Name, UiWidgets.Fail);
            }
            else if (UiWidgets.Button(new Rect(x, bottom, runW, UiWidgets.RowBtnH), "Start",
                         UiWidgets.Btn.Primary))
            {
                SessionHub.Instance.SessionStore.Start(s.Name, UiWidgets.Fail);
            }

            // Stop is Del for a temporary agent: killing the process is what removes it.
            float resetW = UiWidgets.BtnW("Reset", 58f);
            x -= resetW + UiWidgets.GapXS;
            if (!s.Ephemeral && UiWidgets.Button(new Rect(x, bottom, resetW, UiWidgets.RowBtnH),
                                    "Reset", UiWidgets.Btn.Ghost))
                TerminalWindow.OpenOverPane(CatalogActions.ResetState(s.Name));

            float delW = UiWidgets.BtnW("Del", 48f);
            x -= delW + UiWidgets.GapXS;
            if (!s.Ephemeral && UiWidgets.Button(new Rect(x, bottom, delW, UiWidgets.RowBtnH),
                                    "Del", UiWidgets.Btn.Danger))
                TerminalWindow.OpenOverPane(CatalogActions.RemoveSession(s.Name));

            return UiWidgets.RowBtnH;
        }
    }
}
