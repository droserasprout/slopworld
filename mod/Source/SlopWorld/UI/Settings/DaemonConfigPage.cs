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

        protected DaemonConfig _cfg => _configState.Config;
        protected string _path => _configState.Path;
        protected string _error { get => _configState.Error; set => _configState.Error = value; }
        protected bool _loaded => _configState.Loaded;

        readonly ScrollableListing _listing = new ScrollableListing();

        protected virtual bool RefreshHealthOnLoad => false;
        protected virtual bool DrawFieldsWhenOffline => false;
        protected virtual bool DrawFieldsBeforeLoad => false;
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

        protected virtual void AfterSave() { }

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
            var inner = SettingsPageLayout.Body(rect);

            if (!_loaded && !DrawFieldsBeforeLoad && (!DrawFieldsWhenOffline || _cfg == null))
            {
                UiText.StatusLabel(inner, _error ?? "Waiting for the daemon...",
                    _error != null ? UiTheme.Bad : UiTheme.Dim);
            }
            else
            {
                DrawFieldsBody(inner);
            }

            DrawFooter(SettingsPageLayout.Footer(rect));
            DrawOverlay(rect);
        }

        void DrawFieldsBody(Rect r)
        {
            _listing.Draw(r, DrawFields, DrawTrailingFields);
        }

        void DrawFooter(Rect bar)
        {
            var foot = new UiLayout.Bar(bar);
            if (foot.Left("Reload", UiTheme.Btn.Ghost, !_configState.Saving)) Load();
            if (ShowEditButton && foot.Left("Edit", UiTheme.Btn.Ghost,
                    _loaded && !string.IsNullOrEmpty(_path)))
                FilesView.EditFile(null, _path, "edit-config.toml");
            if (ShowSaveButton && foot.Right("Save", UiTheme.Btn.Primary,
                    _loaded && !_configState.Saving)) Save();

            if (_error != null && _loaded)
            {
                GUI.color = UiTheme.Bad;
                UiText.RowLabel(foot.Rest(), _error);
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
