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
            UiWidgets.Note(l, "80% less CPU. 0.1% less guilt. You're welcome, Earth.");

            // Only with the mode on: a slider for a backdrop nothing is drawing is a knob that
            // does nothing, and the note above is what says so. Stepped to twentieths because
            // the value keys a material - see Eco.Shade.
            if (s.ecoMode)
            {
                l.Gap(UiWidgets.GapS);
                float dim = Mathf.Round(UiWidgets.Slider(l, "Backdrop dimming", s.ecoDim,
                    0f, 0.8f, Mathf.RoundToInt(s.ecoDim * 100f) + "%") * 20f) / 20f;
                if (dim != s.ecoDim) { s.ecoDim = dim; s.MarkDirty(); }
            }

            l.Gap(UiWidgets.GapS);
            bool gm = UiWidgets.Checkbox(l, "Grandma's visiting", s.grandmaMode);
            UiWidgets.Note(l, "No fun allowed! Disable gore, vomit, and offensive/harmful tips.");

            if (gm != s.grandmaMode || eco != s.ecoMode)
            {
                s.grandmaMode = gm;
                s.ecoMode = eco;
                s.MarkDirty();
            }

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
