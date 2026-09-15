using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The daemon parses on save and rejects the write if it does not round-trip, so a
    // typo cannot leave slopd with no config.
    public class ConfigWindow : UiWindow
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
            DaemonClient.Get(WireProtocol.Routes.Config,
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
            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                UiLayout.PageCaption(TitleRect(rect), string.IsNullOrEmpty(_path) ? "config.toml" : _path);

                // The caption above and the footer below, both off the font: the figures here
                // were 24, 28 and 100, and the last of them left the error line lying across
                // the footer as soon as a line grew.
                float top = rect.y + UiTheme.RowH + UiTheme.GapXS;
                float foot = UiTheme.BtnH + UiTheme.GapS + UiTheme.LineH
                             + UiTheme.GapXS;
                var area = new Rect(rect.x, top, rect.width, rect.yMax - foot - top);
                var view = new Rect(0f, 0f, area.width - UiTheme.ScrollbarW,
                    Mathf.Max(area.height, Text.CalcHeight(_text,
                        area.width - UiTheme.ScrollbarW - UiTheme.FieldPadX * 2f)
                        + UiTheme.GapL + UiTheme.GapM));

                // The box is the scroll view's frame, so it is drawn round the outside and the
                // area inside it draws none of its own: a well as tall as the content would put
                // its border somewhere off the bottom of the window.
                Slab.Box(area, UiTheme.Well, UiTheme.Edge);
                using (_scroll.Scope(area, view))
                    _text = UiText.Area(view.ContractedBy(UiTheme.FieldPadX,
                        UiTheme.FieldPadY * 2f), "config.toml", _text, _loaded,
                        frame: false);

                if (_error != null)
                {
                    GUI.color = UiTheme.Bad;
                    UiText.RowLabel(
                        new Rect(rect.x, area.yMax + UiTheme.GapXS, rect.width,
                            UiTheme.LineH), _error);
                    GUI.color = Color.white;
                }

                var bar = new UiLayout.Bar(UiLayout.FooterBar(rect));
                if (bar.Left("Reload", UiTheme.Btn.Ghost)) Load();
                if (bar.Right("Save", UiTheme.Btn.Primary, _loaded)) Save();
            }
        }

        void Save()
        {
            if (!_loaded) return;

            DaemonClient.Put(WireProtocol.Routes.Config, $"{{\"text\":{JVal.Q(_text)}}}",
                _ =>
                {
                    _error = null;
                    SessionHub.Instance.SessionStore.Refresh();
                    Messages.Message("SlopWorld: config saved.",
                        MessageTypeDefOf.TaskCompletion, false);
                    Close();
                },
                msg => _error = msg);
        }
    }
}
