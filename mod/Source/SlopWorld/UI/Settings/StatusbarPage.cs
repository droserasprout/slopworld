using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Statusbar presentation is kept separate from the rest of Appearance so its display
    // switches do not get buried below the global scale, scheme, font, and cursor controls.
    public class StatusbarPage : IOptionPage
    {
        static ModSettings S => ModEntry.Instance.settings;

        public void Load() { }

        public void Draw(Rect rect)
        {
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                var body = UiWidgets.PageBody(rect);
                body.height += UiWidgets.BtnH + UiWidgets.GapS;
                var inner = body.ContractedBy(UiWidgets.GapM);

                var l = new Listing_Standard { maxOneColumn = true };
                l.Begin(inner);

                UiWidgets.SectionHeading(l, "Statusbar");
                UiWidgets.CheckboxSetting(l, "Show Usage in statusbar", S, ref S.statusbarUsage,
                    "Show quota readouts in the top statusbar.");
                UiWidgets.CheckboxSetting(l, "Show spent instead of left", S, ref S.usageSpent,
                    "Applies to every provider. Left is the amount remaining; spent is the " +
                    "provider-facing percentage or amount used.");
                string clockPosition = StatusbarClockMode.Normalize(S.statusbarClockPosition);
                UiWidgets.Select(l, "Clock position", StatusbarClockMode.Label(clockPosition),
                    new[]
                    {
                        new SelectorOption("Right", () => SetClockPosition(StatusbarClockMode.Right)),
                        new SelectorOption("Center", () => SetClockPosition(StatusbarClockMode.Center)),
                        new SelectorOption("Hidden", () => SetClockPosition(StatusbarClockMode.Hidden)),
                    }, out _);
                UiWidgets.CheckboxSetting(l, "Show Jukebox in statusbar", S, ref S.statusbarJukebox,
                    "Show the jukebox door when a jukebox is present.");
                UiWidgets.CheckboxSetting(l, "Show GM in statusbar", S, ref S.statusbarGM,
                    "Show the Computer Core door when the core is present.");
                UiWidgets.CheckboxSetting(l, "Show agent status indicators", S, ref S.statusbarAgentIndicators,
                    "Show autostart, resume-on-start, and host-network flags in Agents.");

                l.End();
            }
        }

        static void SetClockPosition(string position)
            => UiWidgets.SetSetting(S, ref S.statusbarClockPosition, position);
    }
}
