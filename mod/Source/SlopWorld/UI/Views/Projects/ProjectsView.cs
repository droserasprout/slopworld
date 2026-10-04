using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A project owns a directory and the mounts shared by every agent in it.
    public class ProjectsView : UiListView<ProjectInfo>
    {
        public override void Opened()
        {
            SessionHub.Instance.Catalog.RefreshProjects();
        }

        public override string Title => "Projects";

        // The second line holds a row button. Therefore, the pitch is off that rather than off two
        // line heights.
        protected override float RowH => UiListRow.TwoLineH;

        protected override string EmptyNote => SessionHub.Instance.Online
            ? "No projects yet. Add one, then put an agent in it."
            : "Daemon unreachable. Is slopd running?  systemctl --user status slopd";

        protected override void DrawHeader(Rect rect)
        {
            var hub = SessionHub.Instance;
            UiLayout.Header(rect, Title, $"{DaemonClient.BaseUrl} - {hub.Status}", hub.Online);
        }

        protected override IList<ProjectInfo> Rows => SessionHub.Instance.Projects;

        readonly ProjectSessionCounts _counts = new ProjectSessionCounts();

        protected override void DoFooter(Rect bar)
        {
            var row = new UiLayout.Bar(bar);

            if (row.Left("Add project", UiTheme.Btn.Primary))
                TerminalWindow.OpenOverPane(new EditProjectDialog(null));

            if (row.Right("Reload", UiTheme.Btn.Ghost))
                SessionHub.Instance.Catalog.RefreshProjects(UiLayout.Fail);
        }

        protected override void DrawRow(Rect r, ProjectInfo p)
        {
            UiListRow.Prepare(r);

            // Measure both row lines from the active font.
            // `Widgets.Label` clips to its rect, so fixed offsets can crop descenders in another font.
            float l1 = UiListRow.LineY(r, 0), l2 = UiListRow.LineY(r, 1);

            // Measure the name column from the active font. Fixed widths worked for only one font and size.
            float nameW = Mathf.Max(UiTheme.Wide("mmmmmmmmmmmmmmmm"), 200f);

            GUI.color = UiTheme.Lead;
            UiText.RowLabel(
                new Rect(r.x + UiTheme.GapS, l1, nameW, UiTheme.LineH), p.Name);

            // Count agents before allowing project deletion.
            var hub = SessionHub.Instance;
            int agents = _counts.Get(hub.Sessions, hub.SessionsVersion, p.Name);
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(
                new Rect(r.x + UiTheme.GapS + nameW + UiTheme.GapS, l1,
                    UiTheme.Wide("99 agents") + 4f, UiTheme.LineH),
                agents == 1 ? "1 agent" : $"{agents} agents");

            // Truncate this text instead of wrapping it.
            // The slot has one line, and both the directory and summary can contain spaces.
            UiText.RowLabel(
                new Rect(r.x + UiTheme.GapS, l2, Mathf.Max(60f, r.width - 150f),
                    UiTheme.LineH), $"{p.Dir}  ({ProjectSummary.Of(p)})");
            GUI.color = Color.white;

            float right = UiListRow.Right(r);
            float actW = Mathf.Max(UiLayout.BtnW("Delete", 120f), UiLayout.BtnW("Edit", 120f));

            if (UiButtons.Button(new Rect(right - actW, r.y + 1f, actW, UiTheme.RowBtnH), "Edit"))
                TerminalWindow.OpenOverPane(new EditProjectDialog(p));

            if (UiButtons.Button(new Rect(right - actW, l2, actW, UiTheme.RowBtnH), "Delete",
                    UiTheme.Btn.Danger))
                TerminalWindow.OpenOverPane(CatalogActions.RemoveProject(p.Name));
        }

    }
}
