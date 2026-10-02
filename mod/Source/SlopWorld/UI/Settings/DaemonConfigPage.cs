using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared shell for pages that read daemon config: the form is disposable, while the draft
    // and its request lifecycle live in DaemonConfigState. Concrete pages only own field layout
    // and validation/conversion.
    public abstract class DaemonConfigPage : IOptionPage, IDisposable
    {
        protected readonly DaemonConfigState _configState;
        bool _disposed;

        protected DaemonConfigPage()
        {
            _configState = new DaemonConfigState(GetType().FullName);
        }

        protected DaemonConfig _cfg => _configState.Config;
        protected string _path => _configState.Path;
        protected string _error { get => _configState.Error; set => _configState.Error = value; }
        protected bool _loaded => _configState.Loaded;
        protected bool _saving => _configState.Saving;
        protected bool _dirty => _configState.Dirty || LocalDirty;

        readonly ScrollableListing _listing = new ScrollableListing();

        protected virtual bool RefreshHealthOnLoad => false;
        protected virtual bool DrawFieldsWhenOffline => false;
        protected virtual bool DrawFieldsBeforeLoad => false;
        protected virtual bool ShowEditButton => false;
        protected virtual bool ShowSaveButton => true;
        protected virtual string SavedMessage => null;
        protected virtual string SaveScope => null;
        protected virtual bool LocalDirty => false;
        protected virtual Action CaptureLocalSave() => null;

        protected abstract void DrawFields(Listing_Standard l);

        // A page such as Usage can append a fixed-position table after its listing. Return the
        // content bottom so the shared scroll view still measures the whole form.
        protected virtual float DrawTrailingFields(Rect rect, float y) => y;

        protected virtual void AfterLoad() { }
        protected virtual void AfterDiscard() { }
        protected virtual void AfterSave() { }

        // Return false without changing _cfg when field text is incomplete or invalid. This is
        // deliberately separate from validation so a save cannot submit a partially parsed form.
        protected virtual bool PrepareSave(out string error)
        {
            error = null;
            return true;
        }

        protected virtual string ValidationError => null;

        public virtual void Load()
        {
            _configState.Load(RefreshHealthOnLoad, () =>
            {
                if (_disposed) return;
                // The form stays editable during reload. Check the merged draft now so
                // edits made while the request was pending never become a clean baseline.
                bool clean = !_configState.Dirty;
                AfterLoad();
                if (clean) _configState.MarkCurrentClean();
            });
        }

        public virtual void Draw(Rect rect)
        {
            using (WidgetState.Save()) DrawCore(rect);
        }

        void DrawCore(Rect rect)
        {
            var inner = SettingsPageLayout.Body(rect);

            if (!_loaded && !DrawFieldsBeforeLoad && (!DrawFieldsWhenOffline || _cfg == null))
            {
                UiText.PlainStatusLabel(inner, _error ?? "Waiting for the daemon",
                    _error != null ? UiTheme.Bad : UiTheme.Dim);
            }
            else
            {
                DrawFieldsBody(inner);
            }

            DrawConfigFooter(SettingsPageLayout.Footer(rect));
            DrawOverlay(rect);
        }

        // Preview pages can compose a pinned preview around the form while this shell
        // retains the daemon status, save footer, and overlay lifecycle.
        protected virtual void DrawFieldsBody(Rect r)
        {
            _listing.Draw(r, DrawFieldsWithMetadata, DrawTrailingFields);
        }

        void DrawFieldsWithMetadata(Listing_Standard l)
        {
            DrawMetadataStatus(l);
            DrawFields(l);
        }

        protected void DrawMetadataStatus(Listing_Standard l)
        {
            if (_loaded && _cfg != null && !_cfg.MetadataAvailable)
                UiLayout.Note(l, "This daemon does not provide policy metadata. The mod needs it to " +
                    "reset factory settings or show a policy preview.");
        }

        protected void DrawConfigFooter(Rect bar)
        {
            var foot = new UiLayout.Bar(bar);
            if (foot.Left("Reload", UiTheme.Btn.Ghost, !_saving)) Load();
            if (ShowEditButton && foot.Left("Edit", UiTheme.Btn.Ghost,
                    _loaded && !string.IsNullOrEmpty(_path)))
                FilesView.EditFile(null, _path, "edit-config.toml");

            if (ShowSaveButton && foot.Right("Discard", UiTheme.Btn.Ghost,
                    (_loaded || LocalDirty) && _dirty && !_saving)) DiscardConfig();
            if (ShowSaveButton && foot.Right("Save", UiTheme.Btn.Primary, CanSave)) SaveConfig();
            DrawConfigStatus(foot);
        }

        protected void DrawConfigStatus(UiLayout.Bar foot)
        {
            string message = ValidationError;
            Color color = UiTheme.Bad;
            if (_saving)
            {
                message = "Saving";
                color = UiTheme.Dim;
            }
            else if (string.IsNullOrEmpty(message)) message = _error ?? _configState.ConflictMessage;
            if (string.IsNullOrEmpty(message) && _dirty)
            {
                message = "Unsaved changes";
                color = UiTheme.Warn;
            }
            if (string.IsNullOrEmpty(message) && _loaded && ShowSaveButton)
            {
                message = "Save to apply";
                color = UiTheme.Dim;
            }
            if (!string.IsNullOrEmpty(message) && !string.IsNullOrEmpty(SaveScope) &&
                (_saving || message == "Unsaved changes" || message == "Save to apply"))
                message += " · " + SaveScope;
            if (string.IsNullOrEmpty(message)) return;

            GUI.color = color;
            UiText.RowLabel(foot.Rest(), message);
            GUI.color = Color.white;
        }

        protected bool CanSave => (_loaded || LocalDirty) && !_saving && _dirty &&
            string.IsNullOrEmpty(ValidationError);

        protected void SaveConfig(Action saved = null)
        {
            if ((!_loaded && !LocalDirty) || _saving) return;
            if (!PrepareSave(out string preparationError))
            {
                _error = preparationError;
                return;
            }
            if (!string.IsNullOrEmpty(ValidationError)) return;

            var saveLocal = CaptureLocalSave();
            void Complete()
            {
                // Submitted local changes still commit if the page closes during the request.
                try { saveLocal?.Invoke(); }
                catch (Exception e) { _error = "Could not save appearance: " + e.Message; return; }
                _error = null;
                if (_disposed) return;
                AfterSave();
                saved?.Invoke();
                if (!string.IsNullOrEmpty(SavedMessage))
                    Messages.Message("SlopWorld: " + SavedMessage,
                        MessageTypeDefOf.TaskCompletion, false);
            }
            // Theme-only saves never write or reload daemon configuration.
            if (_configState.Dirty) _configState.Save(Complete);
            else Complete();
        }

        protected void DiscardConfig()
        {
            if ((!_loaded && !LocalDirty) || _saving) return;
            _configState.Discard();
            AfterDiscard();
            _configState.MarkCurrentClean();
        }

        protected virtual void DrawOverlay(Rect rect) { }

        public virtual void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
