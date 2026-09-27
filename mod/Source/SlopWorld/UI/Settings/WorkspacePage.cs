using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Statusbar and sidebar preferences share a page because both shape the workspace.
    // These local preferences apply as the control changes and persist with mod settings.
    public sealed class WorkspacePage : IOptionPage
    {
        readonly SettingsForm _form = new SettingsForm();
        static ModSettings S => ModEntry.Instance.settings;

        public void Load() { }

        public void Draw(Rect rect)
        {
            _form.Draw(SettingsPageLayout.Body(rect, false), DrawFields);
        }

        static void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Statusbar");
            UiControls.CheckboxSetting(l, "Show Usage in statusbar", S, ref S.statusbarUsage,
                "Show quota readouts in the top statusbar.");
            UiControls.CheckboxSetting(l, "Show spent instead of left", S, ref S.usageSpent,
                "Applies to every provider. Left is the amount remaining. Spent is the " +
                "provider-facing percentage or amount used.");
            string summaryPosition = StatusbarSummaryMode.Normalize(S.statusbarSummaryPosition);
            UiControls.Select(l, "Summary position", StatusbarSummaryMode.Label(summaryPosition),
                new[]
                {
                    new SelectorOption("Left", () => SetSummaryPosition(StatusbarSummaryMode.Left)),
                    new SelectorOption("Center", () => SetSummaryPosition(StatusbarSummaryMode.Center)),
                }, out _);
            string clockPosition = StatusbarClockMode.Normalize(S.statusbarClockPosition);
            UiControls.Select(l, "Clock position", StatusbarClockMode.Label(clockPosition),
                new[]
                {
                    new SelectorOption("Right", () => SetClockPosition(StatusbarClockMode.Right)),
                    new SelectorOption("Center", () => SetClockPosition(StatusbarClockMode.Center)),
                    new SelectorOption("Hidden", () => SetClockPosition(StatusbarClockMode.Hidden)),
                }, out _);
            UiControls.CheckboxSetting(l, "Show Jukebox in statusbar", S, ref S.statusbarJukebox,
                "Show the jukebox door when a jukebox is present.");
            UiControls.CheckboxSetting(l, "Show GM in statusbar", S, ref S.statusbarGM,
                "Show the Computer Core door when the core is present.");

            UiLayout.SectionHeading(l, "Sidebar");
            string side = NavigationSide.Normalize(S.sidebarSide);
            UiControls.Select(l, "Sidebar side", NavigationSide.Label(side),
                new[]
                {
                    new SelectorOption("Left", () => SetSidebarSide(ref S.sidebarSide,
                        NavigationSide.Left)),
                    new SelectorOption("Right", () => SetSidebarSide(ref S.sidebarSide,
                        NavigationSide.Right)),
                }, out _);

            bool visible = UiControls.Checkbox(l, "Show sidebar", !S.sidebarHidden,
                "Keep the sidebar visible. Hidden sidebar consumes no width.");
            if (visible == S.sidebarHidden)
            {
                S.sidebarHidden = !visible;
                S.MarkDirty();
                AgentSidebar.LayoutChanged();
            }

            UiControls.CheckboxSetting(l, "Show agent status indicators", S,
                ref S.statusbarAgentIndicators,
                "Show autostart, resume-on-start, and host-network flags in agent rows.");
        }

        static void SetClockPosition(string position)
            => UiControls.SetSetting(S, ref S.statusbarClockPosition, position);

        static void SetSummaryPosition(string position)
            => UiControls.SetSetting(S, ref S.statusbarSummaryPosition, position);

        static void SetSidebarSide(ref string field, string value)
        {
            if (field == value) return;
            field = value;
            S.MarkDirty();
            AgentSidebar.LayoutChanged();
        }
    }
}
