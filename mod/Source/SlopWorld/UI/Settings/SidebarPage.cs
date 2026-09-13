using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Navigation layout and agent-row indicators belong together. These are local preferences:
    // they apply as the control changes and are written when Settings closes.
    public sealed class SidebarPage : IOptionPage
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
            UiLayout.SectionHeading(l, "Sidebar");
            UiLayout.Note(l, "Sidebar changes apply immediately and are written when Settings closes.");

            string side = NavigationSide.Normalize(S.sidebarSide);
            UiControls.Select(l, "Navigation side", NavigationSide.Label(side),
                new[]
                {
                    new SelectorOption("Left", () => SetLayout(ref S.sidebarSide,
                        NavigationSide.Left)),
                    new SelectorOption("Right", () => SetLayout(ref S.sidebarSide,
                        NavigationSide.Right)),
                }, out _);

            string density = UiDensityPreset.Normalize(S.uiDensity);
            UiControls.Select(l, "Density", UiDensityPreset.Label(density),
                new[]
                {
                    new SelectorOption("Default", () => SetLayout(ref S.uiDensity,
                        UiDensityPreset.Default)),
                    new SelectorOption("Compact", () => SetLayout(ref S.uiDensity,
                        UiDensityPreset.Compact)),
                }, out _);

            bool visible = UiControls.Checkbox(l, "Show navigation", !S.sidebarHidden,
                "Keep the workspace navigation visible. Hidden navigation consumes no width.");
            if (visible == S.sidebarHidden)
            {
                S.sidebarHidden = !visible;
                S.MarkDirty();
                AgentSidebar.LayoutChanged();
            }

            UiControls.CheckboxSetting(l, "Show agent status indicators", S,
                ref S.statusbarAgentIndicators,
                "Show autostart, resume-on-start, and host-network flags in agent rows.");

            if (UiLayout.Button(l, "Reset sidebar layout", UiTheme.Btn.Ghost))
            {
                S.sidebarSide = NavigationSide.Left;
                S.uiDensity = UiDensityPreset.Default;
                S.sidebarHidden = false;
                S.sidebarWidth = WorkspaceLayout.DefaultNavigationWidth;
                S.MarkDirty();
                AgentSidebar.LayoutChanged();
            }
            UiLayout.Note(l, "Reset sidebar layout affects side, density, visibility, and width. " +
                "The navigation width is still resized from its edge.");
        }

        static void SetLayout(ref string field, string value)
        {
            if (field == value) return;
            field = value;
            S.MarkDirty();
            AgentSidebar.LayoutChanged();
        }
    }
}
