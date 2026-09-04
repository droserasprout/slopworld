using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // General options page for install/game controls; the daemon file stays in raw TOML.
    public class ConfigPage : IOptionPage
    {
        SlopConfig _cfg;
        string _path = "";
        string _error;
        bool _loaded;

        readonly SmoothScroll _scroll = new SmoothScroll();
        // Last frame's measured height for the field column. The listing is begun on a
        // rect far taller than it needs, so it never breaks to a second column, and what
        // it actually used is what the scroll view is sized from next frame.
        float _fieldsH;

        public void Load()
        {
            SessionHub.Instance.RefreshHealth();
            SlopClient.Get("/api/config",
                j =>
                {
                    _cfg = SlopConfig.FromJson(j["values"]);
                    SessionHub.Instance.Config = _cfg;
                    _path = j["path"].AsString();
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });
        }

        public void Draw(Rect rect)
        {
            var body = SlopWidgets.PageBody(rect);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            if (!_loaded)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(inner, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
            }
            else
            {
                DoFields(inner);
            }

            DoFooter(SlopWidgets.FooterBar(rect));
        }


        void DoFields(Rect r)
        {
            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW,
                Mathf.Max(_fieldsH, r.height));
            using (_scroll.Scope(r, view))
            {

                // Begun far taller than it is, so a control that would cross the bottom does
                // not start a second column and drop the rest of the form on top of itself.
                var l = new Listing_Standard { maxOneColumn = true };
                l.Begin(new Rect(0f, 0f, view.width, 4000f));

                SlopWidgets.SectionHeading(l, "Connection");
                DrawConnectionSummary(l);

                l.Gap(SlopWidgets.GapL);
                SlopWidgets.SectionHeading(l, "Game");
                var s = SlopWorldMod.Instance.settings;
                bool eco = SlopWidgets.Checkbox(l, "Eco mode", s.ecoMode);
                SlopWidgets.Note(l, "80% less CPU. 0.1% less guilt. You're welcome, Earth.");

                l.Gap(SlopWidgets.GapS);
                bool gm = SlopWidgets.Checkbox(l, "Grandma's visiting", s.grandmaMode);
                SlopWidgets.Note(l, "No fun allowed! Disable gore, vomit, and offensive/harmful tips.");

                if (gm != s.grandmaMode || eco != s.ecoMode)
                {
                    s.grandmaMode = gm;
                    s.ecoMode = eco;
                    s.MarkDirty();
                }

                // Only with the mode on: a slider for a backdrop nothing is drawing is a knob that
                // does nothing, and the note above is what says so. Stepped to twentieths because
                // the value keys a material - see Eco.Shade.
                if (s.ecoMode)
                {
                    l.Gap(SlopWidgets.GapS);
                    float dim = Mathf.Round(SlopWidgets.Slider(l, "Backdrop dimming", s.ecoDim,
                        0f, 0.8f, Mathf.RoundToInt(s.ecoDim * 100f) + "%") * 20f) / 20f;
                    if (dim != s.ecoDim) { s.ecoDim = dim; s.MarkDirty(); }
                }

                l.Gap(SlopWidgets.GapL);
                SlopWidgets.SectionHeading(l, "Locale");
                if (SlopWidgets.Button(l,
                        "TemperatureMode".Translate() + ": " + Prefs.TemperatureMode.ToStringHuman()))
                {
                    Find.WindowStack.Add(new SlopMenu(Enum.GetValues(typeof(TemperatureDisplayMode))
                        .Cast<TemperatureDisplayMode>()
                        .Select(mode => new FloatMenuOption(mode.ToStringHuman(),
                            () => Prefs.TemperatureMode = mode))
                        .ToList()));
                }

                if (SlopWidgets.Button(l, "Time format: " + TimeFormat.Label(s.timeFormat)))
                {
                    Find.WindowStack.Add(new SlopMenu(new[]
                    {
                    new FloatMenuOption("24-hour", () => SetTimeFormat(TimeFormat.TwentyFourHour)),
                    new FloatMenuOption("12-hour", () => SetTimeFormat(TimeFormat.TwelveHour))
                }.ToList()));
                }

                _fieldsH = l.CurHeight + SlopWidgets.GapS;
                l.End();

            }
        }

        static void DrawConnectionSummary(Listing_Standard l)
        {
            var hub = SessionHub.Instance;
            var health = hub.Health;
            var row = l.GetRect(SlopWidgets.LineH);
            string status = hub.Online ? "connected" : "offline";
            string suffix = $" · {RuntimeLabel(hub)} · slopd {health.Version} · " +
                            health.Hostname;
            float x = row.x;

            DrawConnectionSegment(row, ref x, "Daemon: ", SlopWidgets.Dim);
            DrawConnectionSegment(row, ref x, status,
                hub.Online ? SlopWidgets.Yes : SlopWidgets.Bad);
            DrawConnectionSegment(row, ref x, suffix, SlopWidgets.Dim);

            GUI.color = SlopWidgets.Dim;
            l.Label($"Client: SlopWorld {SlopWorldMod.ClientVersion} · " +
                    $"RimWorld {VersionControl.CurrentVersionString}");
            GUI.color = Color.white;
        }

        static void DrawConnectionSegment(Rect row, ref float x, string text, Color color)
        {
            float width = SlopWidgets.Wide(text);
            GUI.color = color;
            SlopWidgets.RowLabel(new Rect(x, row.y, width, row.height), text);
            x += width;
        }

        static string RuntimeLabel(SessionHub hub) =>
            hub.Capabilities.Runtime == "slopcar" ? "sidecar" : "host";

        static void SetTimeFormat(string format)
        {
            var settings = SlopWorldMod.Instance.settings;
            settings.timeFormat = format;
            settings.MarkDirty();
        }

        void DoFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);

            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (foot.Left("Edit", SlopWidgets.Btn.Ghost,
                    _loaded && !string.IsNullOrEmpty(_path)))
                FilesView.EditFile(null, _path, "edit-config.toml");

            // Between the two ends, which is where the room actually is - the offsets that
            // used to put it there were counted off labels this bar now measures itself.
            if (_error != null && _loaded)
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(foot.Rest(), _error);
                GUI.color = Color.white;
            }
        }

    }
}
