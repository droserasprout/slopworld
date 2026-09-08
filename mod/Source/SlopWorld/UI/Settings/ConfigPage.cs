using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // General options page for install/game controls; the daemon file stays in raw TOML.
    public class ConfigPage : DaemonConfigPage
    {
        protected override bool RefreshHealthOnLoad => true;
        protected override bool ShowEditButton => true;
        protected override bool ShowSaveButton => false;

        protected override void DrawFields(Listing_Standard l)
        {
            UiWidgets.SectionHeading(l, "Connection");
            DrawConnectionSummary(l);

            l.Gap(UiWidgets.GapL);
            UiWidgets.SectionHeading(l, "Game");
            var s = ModEntry.Instance.settings;
            bool eco = UiWidgets.Checkbox(l, "Eco mode", s.ecoMode);
            UiWidgets.Note(l, "Pause gameplay and hide the map. Agents and terminal stay responsive.");

            // Only with the mode on: a slider for a backdrop nothing is drawing is a knob that
            // does nothing. Stepped to twentieths because
            // the value keys a material - see Eco.Shade.
            if (s.ecoMode)
            {
                l.Gap(UiWidgets.GapS);
                float dim = Mathf.Round(UiWidgets.Slider(l, "Backdrop dimming", s.ecoDim,
                    0f, 0.8f, Mathf.RoundToInt(s.ecoDim * 100f) + "%") * 20f) / 20f;
                if (dim != s.ecoDim) { s.ecoDim = dim; s.MarkDirty(); }
            }

            bool gm = UiWidgets.Checkbox(l, "Grandma's visiting", s.grandmaMode);
            UiWidgets.Note(l, "No fun allowed! Disable gore, vomit, and offensive/harmful tips.");

            if (gm != s.grandmaMode || eco != s.ecoMode)
            {
                s.grandmaMode = gm;
                s.ecoMode = eco;
                s.MarkDirty();
            }

            l.Gap(UiWidgets.GapL);
            DrawDisplay(l, s);
            l.Gap(UiWidgets.GapL);
            UiWidgets.SectionHeading(l, "Locale");
            if (UiWidgets.Button(l,
                    "TemperatureMode".Translate() + ": " + Prefs.TemperatureMode.ToStringHuman()))
            {
                Find.WindowStack.Add(new UiMenu(Enum.GetValues(typeof(TemperatureDisplayMode))
                    .Cast<TemperatureDisplayMode>()
                    .Select(mode => new FloatMenuOption(mode.ToStringHuman(),
                        () => Prefs.TemperatureMode = mode))
                    .ToList()));
            }

            if (UiWidgets.Button(l, "Time format: " + TimeFormat.Label(s.timeFormat)))
            {
                Find.WindowStack.Add(new UiMenu(new[]
                {
                new FloatMenuOption("24-hour", () => SetTimeFormat(TimeFormat.TwentyFourHour)),
                new FloatMenuOption("12-hour", () => SetTimeFormat(TimeFormat.TwelveHour))
            }.ToList()));
            }

        }

        static void DrawDisplay(Listing_Standard l, ModSettings s)
        {
            UiWidgets.SectionHeading(l, "Display");
            if (UiWidgets.Button(l, "Frame pacing: " + FramePolicy.Label(s.displayMode)))
                Find.WindowStack.Add(new UiMenu(new[] { FramePolicy.Game, FramePolicy.Sync, FramePolicy.Limit }
                    .Select(mode => new FloatMenuOption(FramePolicy.Label(mode), () =>
                    {
                        s.displayMode = mode;
                        s.MarkDirty();
                    })).ToList()));
            if (FramePolicy.Normalize(s.displayMode) == FramePolicy.Limit)
            {
                if (UiWidgets.Button(l, "FPS limit: " + FramePolicy.Clamp(s.foregroundFps)))
                    Find.WindowStack.Add(new UiMenu(new[] { 30, 60, 90, 120, 144 }
                        .Select(fps => new FloatMenuOption(fps + " FPS", () =>
                        {
                            s.foregroundFps = fps;
                            s.MarkDirty();
                        })).ToList()));
                UiWidgets.SliderSetting(l, "Custom FPS", s, ref s.foregroundFps, 30, 360);
                UiWidgets.Note(l, "Disables VSync. Lower limits save power; higher limits improve responsiveness.");
            }
            else
                UiWidgets.Note(l, FramePolicy.Normalize(s.displayMode) == FramePolicy.Sync
                    ? "VSync follows the display refresh rate for smooth presentation."
                    : "Preserve the game's frame rate and VSync settings.");
            UiWidgets.Note(l, "Applies with or without Eco mode. Unfocused windows use 15 FPS.");
        }

        static void DrawConnectionSummary(Listing_Standard l)
        {
            var hub = SessionHub.Instance;
            var health = hub.Health;
            var row = l.GetRect(UiWidgets.LineH);
            string status = hub.Online ? "connected" : "offline";
            string suffix = $" · {RuntimeLabel(hub)} · slopd {health.Version} · " +
                            health.Hostname;
            float x = row.x;

            DrawConnectionSegment(row, ref x, "Daemon: ", UiWidgets.Dim);
            DrawConnectionSegment(row, ref x, status,
                hub.Online ? UiWidgets.Yes : UiWidgets.Bad);
            DrawConnectionSegment(row, ref x, suffix, UiWidgets.Dim);

            GUI.color = UiWidgets.Dim;
            l.Label($"Client: SlopWorld {ModEntry.ClientVersion} · " +
                    $"RimWorld {VersionControl.CurrentVersionString}");
            GUI.color = Color.white;
        }

        static void DrawConnectionSegment(Rect row, ref float x, string text, Color color)
        {
            float width = UiWidgets.Wide(text);
            GUI.color = color;
            UiWidgets.RowLabel(new Rect(x, row.y, width, row.height), text);
            x += width;
        }

        static string RuntimeLabel(SessionHub hub) =>
            hub.Capabilities.Runtime == "slopcar" ? "sidecar" : "host";

        static void SetTimeFormat(string format)
        {
            var settings = ModEntry.Instance.settings;
            settings.timeFormat = format;
            settings.MarkDirty();
        }

    }
}
