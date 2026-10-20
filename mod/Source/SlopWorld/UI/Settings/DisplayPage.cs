using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Window presentation, frame limits, and scrolling preferences apply live.
    // Interface owns visual styling; Workspace owns sidebar and statusbar layout.
    public sealed class DisplayPage : IOptionPage
    {
        readonly SettingsForm _form = new SettingsForm();
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
            string autoLabel = "Auto (" + BackgroundFrames.AutoFps + " FPS)";
            string frameLimitLabel = mode == FramePolicy.Auto ? autoLabel : fps + " FPS";
            string frameLimitTip = S.vsync
                ? "Turn off VSync to choose an FPS limit."
                : "Auto matches the current display refresh rate. Lower limits save power. "
                    + "Higher limits improve responsiveness.";
            UiSelector.Draw(
                l,
                "Frame limiter",
                frameLimitLabel,
                FrameOptions(),
                out _,
                frameLimitTip,
                enabled: !S.vsync
            );

            bool vsync = UiControls.Checkbox(
                l,
                "VSync (not recommended)",
                S.vsync,
                "Synchronize presentation with the display refresh rate. "
                    + "Adds scrolling latency and overrides the FPS limit."
            );
            if (vsync != S.vsync)
            {
                S.vsync = vsync;
                S.MarkDirty();
            }

            UiSelector.Draw(
                l,
                "Post-processing",
                CrtMode.Label(S.crtOverlay),
                new[] { CrtMode.Off, CrtMode.Mild, CrtMode.Intense, CrtMode.FullOn }.Select(
                    mode => new FloatMenuOption(
                        CrtMode.Label(mode),
                        () =>
                        {
                            S.crtOverlay = mode;
                            S.MarkDirty();
                        }
                    )
                ),
                out _,
                "Scanlines and vignette across the whole window. Mild is the default. Applies immediately."
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
                    "Auto (" + BackgroundFrames.AutoFps + " FPS)",
                    () =>
                    {
                        if (S.vsync)
                            return;
                        S.displayMode = FramePolicy.Auto;
                        S.MarkDirty();
                    }
                ),
            };
            options.AddRange(
                FramePolicy.Presets.Select(preset => new FloatMenuOption(
                    preset + " FPS",
                    () =>
                    {
                        if (S.vsync)
                            return;
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
