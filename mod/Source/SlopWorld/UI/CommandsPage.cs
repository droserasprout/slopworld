using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Machine-wide command defaults. Preset names stay separate from executable templates:
    // Agent and Shell select daemon command files, while the other fields are host apps the
    // mod starts for a file or URL.
    public class CommandsPage
    {
        SlopConfig _cfg;
        string _error;
        bool _loaded;

        readonly SmoothScroll _scroll = new SmoothScroll();
        float _fieldsH;

        public void Load()
        {
            SlopClient.Get("/api/config",
                j =>
                {
                    _cfg = SlopConfig.FromJson(j["values"]);
                    SessionHub.Instance.Config = _cfg;
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _loaded = false; });
        }

        public void Draw(Rect rect)
        {
            SlopWidgets.PageCaption(rect,
                "Defaults for sessions, file viewers, editors and desktop links.");

            var body = SlopWidgets.PageBody(rect);
            SlopWidgets.Card(body);
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
            _scroll.Begin(r, view);

            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));

            SlopWidgets.SectionHeading(l, "Session defaults");
            Note(l, "These are command preset names, not executable paths. The daemon's " +
                "preset library decides what each agent or shell errand runs.");
            l.Gap(SlopWidgets.GapS);
            l.Label("Agent preset");
            _cfg.Agent = SlopWidgets.Field(l, "commands.agent", _cfg.Agent);
            l.Gap(SlopWidgets.GapS);
            l.Label("Shell preset");
            _cfg.Shell = SlopWidgets.Field(l, "commands.shell", _cfg.Shell);

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Files");
            l.Label("Pager");
            _cfg.Pager = SlopWidgets.Field(l, "commands.pager", _cfg.Pager);
            Note(l, "The file is appended as -- {file} unless the command contains {file}. " +
                "A line jump can use {line}; the search viewer supplies it.");
            l.Gap(SlopWidgets.GapS);
            l.Label("Editor");
            _cfg.Editor = SlopWidgets.Field(l, "commands.editor", _cfg.Editor);
            Note(l, "The file is appended as -- {file} unless the command contains {file}.");

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Syntax highlighting");
            l.Label("Highlighter");
            _cfg.Highlighter = SlopWidgets.Field(l, "commands.highlighter", _cfg.Highlighter);
            Note(l, "Used by the pager through LESSOPEN. %s is replaced by less with the " +
                "current file. Leave blank to disable highlighting.");

            l.Gap(SlopWidgets.GapL);
            SlopWidgets.SectionHeading(l, "Web links");
            l.Label("URL opener");
            _cfg.Opener = SlopWidgets.Field(l, "commands.opener", _cfg.Opener);
            Note(l, "The URL is appended unless the command contains {url}. Blank tries " +
                "xdg-open, gio, then wslview.");

            _fieldsH = l.CurHeight + SlopWidgets.GapS;
            l.End();
            _scroll.End();
        }

        static void Note(Listing_Standard l, string text)
        {
            GUI.color = SlopWidgets.Dim;
            l.Label(text);
            GUI.color = Color.white;
        }

        void DoFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);
            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (foot.Left("Edit as TOML", SlopWidgets.Btn.Ghost)) ConfigWindow.Open();
            if (foot.Right("Save", SlopWidgets.Btn.Primary, _loaded)) Save();

            if (_error != null && _loaded)
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(foot.Rest(), _error);
                GUI.color = Color.white;
            }
        }

        void Save()
        {
            if (!_loaded) return;

            SlopClient.Put("/api/config/patch", _cfg.ToPatchJson(),
                _ =>
                {
                    _error = null;
                    SessionHub.Instance.Config = _cfg;
                    SlopOptions.Reread();
                    Messages.Message("SlopWorld: command settings saved.",
                        MessageTypeDefOf.TaskCompletion, false);
                },
                msg => _error = msg);
        }
    }
}
