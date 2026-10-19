using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Window presentation, frame pacing, and scrolling preferences apply live.
    // Interface owns visual styling; Workspace owns sidebar and statusbar layout.
    public sealed class DisplayPage : IOptionPage
    {
        readonly SettingsForm _form = new SettingsForm();
        static readonly string[] FrameLabels = new[] { "VSync" }
            .Concat(FramePolicy.Presets.Select(preset => preset + " FPS"))
            .ToArray();
        static ModSettings S => ModEntry.Instance.settings;

        public void Load() { }

        public void Draw(Rect rect)
        {
            _form.Draw(SettingsPageLayout.BodyWithoutFooter(rect), DrawFields);
        }

        static void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Window");
            bool fullscreen = UiControls.Checkbox(
                l,
                "Fullscreen",
                S.fullscreen,
                "Use window-manager fullscreen without changing Unity's render mode."
            );
            if (fullscreen != S.fullscreen)
                LinuxGameWindow.Set(fullscreen);

            UiLayout.SectionHeading(l, "Rendering");
            string mode = FramePolicy.Normalize(S.displayMode);
            int fps = FramePolicy.NearestPreset(S.foregroundFps);
            string framePacingLabel = mode == FramePolicy.Sync ? "VSync" : fps + " FPS";
            string framePacingTip =
                mode == FramePolicy.Limit
                    ? "Disables VSync. Lower limits save power. Higher limits improve responsiveness."
                    : "VSync follows the display refresh rate for smooth presentation.";
            UiSelector.Draw(
                l,
                "Frame pacing",
                framePacingLabel,
                FrameLabels,
                FrameOptions,
                out _,
                framePacingTip
            );

            UiLayout.SectionHeading(l, "Scrolling");
            bool smooth = UiControls.Checkbox(
                l,
                "Smooth scrolling",
                S.smoothScrolling,
                "Use precise touchpad scrolling in terminals, panels, and Markdown. "
                    + "Turn off to use wheel steps. Applies immediately."
            );
            if (smooth != S.smoothScrolling)
            {
                S.smoothScrolling = smooth;
                S.MarkDirty();
            }
        }

        static List<FloatMenuOption> FrameOptions()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "VSync",
                    () =>
                    {
                        S.displayMode = FramePolicy.Sync;
                        S.MarkDirty();
                    }
                ),
            };
            options.AddRange(
                FramePolicy.Presets.Select(preset => new FloatMenuOption(
                    preset + " FPS",
                    () =>
                    {
                        S.displayMode = FramePolicy.Limit;
                        S.foregroundFps = preset;
                        S.MarkDirty();
                    }
                ))
            );
            return options;
        }
    }
}
