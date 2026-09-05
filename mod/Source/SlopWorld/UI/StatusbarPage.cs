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
                bool usage = UiWidgets.Checkbox(l, "Show Usage in statusbar", S.statusbarUsage,
                    "Show quota readouts in the top statusbar.");
                bool spent = UiWidgets.Checkbox(l, "Show spent instead of left",
                    Settings.UsageSpent,
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
                bool jukebox = UiWidgets.Checkbox(l, "Show Jukebox in statusbar",
                    S.statusbarJukebox,
                    "Show the jukebox door when a jukebox is present.");
                bool gm = UiWidgets.Checkbox(l, "Show GM in statusbar", S.statusbarGM,
                    "Show the Computer Core door when the core is present.");
                bool indicators = UiWidgets.Checkbox(l, "Show agent status indicators",
                    S.statusbarAgentIndicators,
                    "Show autostart, resume-on-start, and host-network flags in Agents.");
                if (usage != S.statusbarUsage || spent != Settings.UsageSpent
                    || jukebox != S.statusbarJukebox || gm != S.statusbarGM
                    || indicators != S.statusbarAgentIndicators)
                {
                    S.statusbarUsage = usage;
                    S.usageSpent = spent;
                    S.statusbarJukebox = jukebox;
                    S.statusbarGM = gm;
                    S.statusbarAgentIndicators = indicators;
                    S.MarkDirty();
                }

                l.End();
            }
        }

        static void SetClockPosition(string position)
        {
            S.statusbarClockPosition = position;
            S.MarkDirty();
        }
    }
}
