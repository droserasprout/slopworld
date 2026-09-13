using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Statusbar presentation is kept separate from the rest of Appearance so its display
    // switches do not get buried below the global scale, scheme, font, and cursor controls.
    public class StatusbarPage : IOptionPage
    {
        readonly SettingsForm _form = new SettingsForm();

        static ModSettings S => ModEntry.Instance.settings;

        public void Load() { }

        public void Draw(Rect rect)
        {
            _form.Draw(SettingsPageLayout.Body(rect, false), DrawFields);
        }

        void DrawFields(Listing_Standard l)
        {

            UiLayout.SectionHeading(l, "Statusbar");
            UiLayout.Note(l, "Statusbar changes apply immediately and are written when Settings closes.");
            UiControls.CheckboxSetting(l, "Show Usage in statusbar", S, ref S.statusbarUsage,
                "Show quota readouts in the top statusbar.");
            UiControls.CheckboxSetting(l, "Show spent instead of left", S, ref S.usageSpent,
                "Applies to every provider. Left is the amount remaining; spent is the " +
                "provider-facing percentage or amount used.");
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
        }

        static void SetClockPosition(string position)
            => UiControls.SetSetting(S, ref S.statusbarClockPosition, position);
    }
}
