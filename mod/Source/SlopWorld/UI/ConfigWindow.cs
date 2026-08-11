using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The daemon parses on save and rejects the write if it does not round-trip, so a
    // typo cannot leave slopd with no config.
    public class ConfigWindow : SlopWindow
    {
        string _text = "loading...";
        string _path = "";
        string _error;
        bool _loaded;
        readonly SmoothScroll _scroll = new SmoothScroll();

        public static void Open()
        {
            var w = new ConfigWindow();
            w.Load();
            Find.WindowStack.Add(w);
        }

        public ConfigWindow()
        {
            // No `optionalTitle`: vanilla draws that one itself, in its own font and centred,
            // before `DoWindowContents` is ever called. The caption below names the file
            // being edited, which is the more useful of the two things a title could say.
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(720f, 620f);

        void Load()
        {
            SlopClient.Get("/api/config",
                j =>
                {
                    _text = j["text"].AsString();
                    _path = j["path"].AsString();
                    _loaded = true;
                    _error = null;
                },
                msg => { _error = msg; _text = ""; });
        }

        protected override void DoBody(Rect rect)
        {
            Text.Font = GameFont.Small;
            SlopWidgets.PageCaption(rect, string.IsNullOrEmpty(_path) ? "config.toml" : _path);

            // The caption above and the footer below, both off the font: the figures here
            // were 24, 28 and 100, and the last of them left the error line lying across
            // the footer as soon as a line grew.
            float top = rect.y + SlopWidgets.RowH + SlopWidgets.GapXS;
            float foot = SlopWidgets.BtnH + SlopWidgets.GapS + SlopWidgets.LineH
                         + SlopWidgets.GapXS;
            var area = new Rect(rect.x, top, rect.width, rect.yMax - foot - top);
            var view = new Rect(0f, 0f, area.width - SlopWidgets.ScrollbarW,
                Mathf.Max(area.height, Text.CalcHeight(_text, area.width - 24f) + 40f));

            // The box is the scroll view's frame, so it is drawn round the outside and the
            // area inside it draws none of its own: a well as tall as the content would put
            // its border somewhere off the bottom of the window.
            Slab.Box(area, SlopWidgets.Well, SlopWidgets.Edge);
            _scroll.Begin(area, view);
            _text = SlopWidgets.Area(view.ContractedBy(6f, 4f), "config.toml", _text,
                _loaded, frame: false);
            _scroll.End();

            if (_error != null)
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(
                    new Rect(rect.x, area.yMax + SlopWidgets.GapXS, rect.width,
                        SlopWidgets.LineH), _error);
                GUI.color = Color.white;
            }

            var bar = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (bar.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (bar.Right("Save", SlopWidgets.Btn.Primary, _loaded)) Save();
        }

        void Save()
        {
            if (!_loaded) return;

            SlopClient.Put("/api/config", $"{{\"text\":{JVal.Q(_text)}}}",
                _ =>
                {
                    _error = null;
                    SessionHub.Instance.Refresh();
                    Messages.Message("SlopWorld: config saved.",
                        MessageTypeDefOf.TaskCompletion, false);
                    Close();
                },
                msg => _error = msg);
        }
    }
}
