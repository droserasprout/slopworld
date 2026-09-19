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

            string side = NavigationSide.Normalize(S.sidebarSide);
            UiControls.Select(l, "Sidebar side", NavigationSide.Label(side),
                new[]
                {
                    new SelectorOption("Left", () => SetLayout(ref S.sidebarSide,
                        NavigationSide.Left)),
                    new SelectorOption("Right", () => SetLayout(ref S.sidebarSide,
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

        static void SetLayout(ref string field, string value)
        {
            if (field == value) return;
            field = value;
            S.MarkDirty();
            AgentSidebar.LayoutChanged();
        }
    }
}
