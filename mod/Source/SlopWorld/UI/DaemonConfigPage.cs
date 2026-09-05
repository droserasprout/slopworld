using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared shell for pages that read daemon config: the form body is scrollable, while the
    // concrete page supplies its fields and any save-time conversion it needs.
    public abstract class DaemonConfigPage : IOptionPage
    {
        readonly DaemonConfigState _configState = new DaemonConfigState();

        protected SlopConfig _cfg => _configState.Config;
        protected string _path => _configState.Path;
        protected string _error { get => _configState.Error; set => _configState.Error = value; }
        protected bool _loaded => _configState.Loaded;

        readonly SmoothScroll _scroll = new SmoothScroll();
        float _fieldsH;

        protected virtual bool RefreshHealthOnLoad => false;
        protected virtual bool DrawFieldsWhenOffline => false;
        protected virtual bool ShowEditButton => false;
        protected virtual bool ShowSaveButton => true;
        protected virtual string SavedMessage => null;

        protected abstract void DrawFields(Listing_Standard l);

        // A page such as Usage can append a fixed-position table after its listing. Return the
        // content bottom so the shared scroll view still measures the whole form.
        protected virtual float DrawTrailingFields(Rect rect, float y) => y;

        protected virtual void AfterLoad() { }

        // Hooks keep raw text in the form until Save, where concrete pages can parse it once.
        protected virtual void BeforeSave() { }

        protected virtual void DrawOverlay(Rect rect) { }

        protected virtual void AfterSave()
        {
            SessionHub.Instance.Config = _cfg;
            SlopOptions.Reread();
        }

        public void Load()
        {
            _configState.Load(RefreshHealthOnLoad, AfterLoad);
        }

        public void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            var body = SlopWidgets.PageBody(rect);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            if (!_loaded && (!DrawFieldsWhenOffline || _cfg == null))
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(inner, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
            }
            else
            {
                DrawFieldsBody(inner);
            }

            DrawFooter(SlopWidgets.FooterBar(rect));
            DrawOverlay(rect);
        }

        void DrawFieldsBody(Rect r)
        {
            var view = SlopScrollBody.View(r, _fieldsH);
            using (_scroll.Scope(r, view))
            {
                var l = new Listing_Standard { maxOneColumn = true };
                l.Begin(new Rect(0f, 0f, view.width, 4000f));
                DrawFields(l);
                float y = l.CurHeight;
                l.End();

                _fieldsH = DrawTrailingFields(view, y) + SlopWidgets.GapS;
            }
        }

        void DrawFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);
            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (ShowEditButton && foot.Left("Edit", SlopWidgets.Btn.Ghost,
                    _loaded && !string.IsNullOrEmpty(_path)))
                FilesView.EditFile(null, _path, "edit-config.toml");
            if (ShowSaveButton && foot.Right("Save", SlopWidgets.Btn.Primary, _loaded)) Save();

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
            _configState.Save(BeforeSave, () =>
            {
                AfterSave();
                if (!string.IsNullOrEmpty(SavedMessage))
                    Messages.Message("SlopWorld: " + SavedMessage,
                        MessageTypeDefOf.TaskCompletion, false);
            });
        }
    }
}
