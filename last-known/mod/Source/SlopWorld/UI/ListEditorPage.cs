using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Settings pages that edit daemon config share the same scrollable form and footer.
    // Subclasses only supply the fields that make up their row body.
    public abstract class ListEditorPage : IOptionPage
    {
        protected SlopConfig _cfg;
        protected string _path = "";
        protected string _error;
        protected bool _loaded;

        readonly SmoothScroll _scroll = new SmoothScroll();
        float _fieldsH;

        protected abstract string SavedMessage { get; }

        protected abstract void DrawFields(Listing_Standard l);

        protected virtual void AfterLoad() { }

        public void Load()
        {
            SlopClient.Get("/api/config",
                j =>
                {
                    _cfg = SlopConfig.FromJson(j["values"]);
                    SessionHub.Instance.Config = _cfg;
                    AfterLoad();
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
                DrawFields(inner);
            }

            DoFooter(SlopWidgets.FooterBar(rect));
        }

        void DrawFields(Rect r)
        {
            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW,
                Mathf.Max(_fieldsH, r.height));
            _scroll.Begin(r, view);

            var l = new Listing_Standard { maxOneColumn = true };
            l.Begin(new Rect(0f, 0f, view.width, 4000f));
            DrawFields(l);
            _fieldsH = l.CurHeight + SlopWidgets.GapS;
            l.End();
            _scroll.End();
        }

        void DoFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);
            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (foot.Left("Edit", SlopWidgets.Btn.Ghost,
                    _loaded && !string.IsNullOrEmpty(_path)))
                FilesView.EditFile(null, _path, "edit-config.toml");
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
                    Messages.Message("SlopWorld: " + SavedMessage,
                        MessageTypeDefOf.TaskCompletion, false);
                },
                msg => _error = msg);
        }
    }
}
