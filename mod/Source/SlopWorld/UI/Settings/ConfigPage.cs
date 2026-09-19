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
        protected override bool DrawFieldsBeforeLoad => true;
        protected override bool ShowEditButton => true;
        protected override bool ShowSaveButton => true;

        protected override void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Connection");
            DrawConnectionSummary(l);

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Game");
            var s = ModEntry.Instance.settings;
            bool eco = UiControls.Checkbox(l, "Eco mode", s.ecoMode,
                "80% less CPU, 0.1% less guilt. You're welcome, Earth!");

            // Only with the mode on: a slider for a backdrop nothing is drawing is a knob that
            // does nothing. Stepped to twentieths because
            // the value keys a material - see Eco.Shade.
            if (s.ecoMode)
            {
                l.Gap(UiTheme.GapS);
                float dim = Mathf.Round(UiControls.Slider(l, "Backdrop dimming", s.ecoDim,
                    0f, 0.8f, Mathf.RoundToInt(s.ecoDim * 100f) + "%") * 20f) / 20f;
                if (dim != s.ecoDim) { s.ecoDim = dim; s.MarkDirty(); }
            }

            bool gm = UiControls.Checkbox(l, "Grandma's visiting", s.grandmaMode,
                "No fun allowed! Disable violence and offensive/harmful tips.");

            if (gm != s.grandmaMode || eco != s.ecoMode)
            {
                s.grandmaMode = gm;
                s.ecoMode = eco;
                s.MarkDirty();
            }

            l.Gap(UiTheme.GapL);
            UiLayout.SectionHeading(l, "Locale");
            if (UiLayout.Button(l,
                    "TemperatureMode".Translate() + ": " +
                    TemperatureUnit.Label(Settings.TemperatureUnit,
                        Prefs.TemperatureMode.ToStringHuman())))
            {
                var choices = Enum.GetValues(typeof(TemperatureDisplayMode))
                    .Cast<TemperatureDisplayMode>()
                    .Select(mode => new FloatMenuOption(mode.ToStringHuman(),
                        () => SetTemperatureUnit(mode)))
                    .ToList();
                choices.Add(new FloatMenuOption(TemperatureUnit.GlazedBaldLabel,
                    SetGlazedBaldTemperatureUnit));
                Find.WindowStack.Add(new UiMenu(choices));
            }

            if (UiLayout.Button(l, "Time format: " + TimeFormat.Label(s.timeFormat)))
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
            var row = l.GetRect(UiTheme.LineH);
            string status = hub.Online ? "connected" : "offline";
            string suffix = $" · {RuntimeLabel(hub)} · slopd {health.Version} · " +
                            health.Hostname;
            float x = row.x;

            DrawConnectionSegment(row, ref x, "Daemon: ", UiTheme.Dim);
            DrawConnectionSegment(row, ref x, status,
                hub.Online ? UiTheme.Yes : UiTheme.Bad);
            DrawConnectionSegment(row, ref x, suffix, UiTheme.Dim);

            GUI.color = UiTheme.Dim;
            l.Label($"Client: SlopWorld {ModEntry.ClientVersion} · " +
                    $"RimWorld {VersionControl.CurrentVersionString}");
            GUI.color = Color.white;
        }

        static void DrawConnectionSegment(Rect row, ref float x, string text, Color color)
        {
            float width = UiTheme.Wide(text);
            GUI.color = color;
            UiText.RowLabel(new Rect(x, row.y, width, row.height), text);
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

        static void SetTemperatureUnit(TemperatureDisplayMode mode)
        {
            Prefs.TemperatureMode = mode;
            var settings = ModEntry.Instance.settings;
            settings.temperatureUnit = "";
            settings.MarkDirty();
        }

        static void SetGlazedBaldTemperatureUnit()
        {
            Prefs.TemperatureMode = TemperatureDisplayMode.Fahrenheit;
            var settings = ModEntry.Instance.settings;
            settings.temperatureUnit = TemperatureUnit.GlazedBald;
            settings.MarkDirty();
        }

    }
}
