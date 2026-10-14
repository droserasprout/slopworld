using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns the shared disclaimer content and spacing; pages retain their content and scroll state.
    static class SettingsDisclaimer
    {
        const string EulaDisclaimer =
            "Portions of the materials used to create this content/mod are trademarks and/or "
            + "copyrighted works of Ludeon Studios Inc. All rights reserved by Ludeon. This "
            + "content/mod is not official and is not endorsed by Ludeon.";

        public static float DrawEulaDisclaimer(Rect r, float y)
        {
            y += UiTheme.GapS;
            y = UiText.Paragraph(
                r,
                y,
                EulaDisclaimer,
                GameFont.Medium,
                UiTheme.Dim,
                TextAnchor.UpperCenter,
                15
            );
            return y + UiTheme.GapS;
        }
    }
}
