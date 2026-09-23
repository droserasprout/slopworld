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
            var row = new UiLayout.Bar(bar);

            if (row.Left("Add agent", UiTheme.Btn.Primary))
                TerminalWindow.OpenOverPane(new EditSessionDialog(null));

            // Library is not here: it is a window of its own in the bottom bar, and an
            // errand is not something you do to an agent on this list. Nor is "New colony",
            // for the same reason - it is "Next planet" in the menu behind Escape now.
            if (row.Right("Reconnect", UiTheme.Btn.Ghost))
                hub.Connect();
        }

        protected override void DrawRow(Rect r, SessionInfo s)
        {
            UiListRow.Prepare(r);

            DrawIdentity(r, s);
            float y = UiListRow.LineY(r, 1);
            DrawLocation(new Rect(r.x, y, r.width, UiTheme.LineH), s);
            DrawDialogActions(r, s);
            DrawSessionActions(new Rect(r.x, y, r.width, UiTheme.RowBtnH), s);
        }

        void DrawIdentity(Rect r, SessionInfo s)
        {
            // State chip, so the list scans the same way the map overlay does.
            var chip = new Rect(r.x + 6f, r.y + 6f, 10f, r.height - 12f);
            Slab.Fill(chip, TerminalWindow.StateColor(s.State));

            float l1 = UiListRow.LineY(r, 0);

            // The name's column and the state's beside it, off the font. At 200 and 230 the pair
            // held for one face at one size and overlapped at the next.
            float nameW = Mathf.Max(UiTheme.Wide("mmmmmmmmmmmmmmmm"), 200f);
            float stateX = r.x + 24f + nameW + UiTheme.GapM;

            GUI.color = UiTheme.Lead;
            UiText.RowLabel(new Rect(r.x + 24f, l1, nameW, UiTheme.LineH), s.Name);

            // The state in words next to the name, so the row scans without decoding the
            // color of the chip beside it.
            GUI.color = TerminalWindow.StateColor(s.State);
            UiText.RowLabel(new Rect(stateX, l1, UiTheme.Wide("connecting") + 4f,
                UiTheme.LineH), s.State.ToString().ToLower());
            GUI.color = UiTheme.Dim;

        }

        float DrawLocation(Rect r, SessionInfo s)
        {
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
            // Truncate this text instead of wrapping it.
            // Wrapped text exceeds the one-line slot and can cover the button row.
            UiText.RowLabel(
                new Rect(r.x + 24f, r.y, Mathf.Max(60f, r.width - 340f), UiTheme.LineH),
                where);
            GUI.color = Color.white;

            return UiTheme.LineH;
        }

        float DrawDialogActions(Rect r, SessionInfo s)
        {
            float top = r.y + 1f;
            float right = UiListRow.Right(r);

            float dupW = UiLayout.BtnW("Duplicate", 74f);
            float editW = UiLayout.BtnW("Edit", 96f);

            // Nothing in config.toml stands behind a temporary agent. Therefore, the dialog would
            // write an entry the daemon has never had and the save would be refused.
            if (!s.Ephemeral && !s.Host &&
                UiButtons.Button(
                    new Rect(right - dupW - UiTheme.GapXS - editW, top, editW,
                        UiTheme.RowBtnH), "Edit"))
                TerminalWindow.OpenOverPane(new EditSessionDialog(s));

            if (!s.Host && !string.IsNullOrEmpty(s.Project))
            {
                var dup = new Rect(right - dupW, top, dupW, UiTheme.RowBtnH);
                TooltipHandler.TipRegion(dup, s.Ephemeral
                    ? $"A permanent agent in {s.Project}, like the one running this errand."
                    : $"New agent with '{s.Name}'s project and command, under a new name.");
                if (UiButtons.Button(dup, "Duplicate"))
                    TerminalWindow.OpenOverPane(EditSessionDialog.Copy(s));
            }

            return UiTheme.RowBtnH;
        }

        float DrawSessionActions(Rect r, SessionInfo s)
        {
            float bottom = r.y;
            float right = UiListRow.Right(r);
            float termW = UiTheme.RowBtnH;

            var term = new Rect(right - termW, bottom, termW, UiTheme.RowBtnH);
            TooltipHandler.TipRegion(term, s.Gone
                ? $"'{s.Name}' is not running - start it first."
                : $"Open the terminal for '{s.Name}'.");
            if (UiLayout.IconButton(term, Icons.Terminal, UiTheme.Name, !s.Gone))
                TerminalWindow.Open(s.Name);

            float x = right - termW;

            float runW = UiLayout.BtnW(s.Alive ? "Stop" : "Start", 58f);
            x -= runW + UiTheme.GapXS;
            if (s.Alive)
            {
                if (UiButtons.Button(new Rect(x, bottom, runW, UiTheme.RowBtnH), "Stop"))
                    SessionHub.Instance.SessionStore.Stop(s.Name, UiLayout.Fail);
            }
            else if (UiButtons.Button(new Rect(x, bottom, runW, UiTheme.RowBtnH), "Start",
                         UiTheme.Btn.Primary))
            {
                SessionHub.Instance.SessionStore.Start(s.Name, UiLayout.Fail);
            }

            // Stop is Del for a temporary agent: killing the process is what removes it.
            float resetW = UiLayout.BtnW("Reset", 58f);
            x -= resetW + UiTheme.GapXS;
            if (!s.Ephemeral && UiButtons.Button(new Rect(x, bottom, resetW, UiTheme.RowBtnH),
                                    "Reset", UiTheme.Btn.Ghost))
                TerminalWindow.OpenOverPane(CatalogActions.ResetState(s.Name));

            float delW = UiLayout.BtnW("Del", 48f);
            x -= delW + UiTheme.GapXS;
            if (!s.Ephemeral && UiButtons.Button(new Rect(x, bottom, delW, UiTheme.RowBtnH),
                                    "Del", UiTheme.Btn.Danger))
                TerminalWindow.OpenOverPane(CatalogActions.RemoveSession(s.Name));

            return UiTheme.RowBtnH;
        }
    }
}
