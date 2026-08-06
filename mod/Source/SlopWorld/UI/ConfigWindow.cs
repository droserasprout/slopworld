using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The daemon parses on save and rejects the write if it does not round-trip, so a
    // typo cannot leave slopd with no config.
    public class ConfigWindow : Window
    {
        string _text = "loading...";
        string _path = "";
        string _error;
        bool _loaded;
        Vector2 _scroll;

        public static void Open()
        {
            var w = new ConfigWindow();
            w.Load();
            Find.WindowStack.Add(w);
        }

        public ConfigWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            optionalTitle = "Config Editor";
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

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f),
                string.IsNullOrEmpty(_path) ? "config.toml" : _path);

            var area = new Rect(rect.x, rect.y + 28f, rect.width, rect.height - 100f);
            var view = new Rect(0f, 0f, area.width - 18f,
                Mathf.Max(area.height, Text.CalcHeight(_text, area.width - 24f) + 40f));

            // The box is the scroll view's frame, so it is drawn round the outside and the
            // area inside it draws none of its own: a well as tall as the content would put
            // its border somewhere off the bottom of the window.
            Slab.Box(area, SlopWidgets.Well, SlopWidgets.Edge);
            Widgets.BeginScrollView(area, ref _scroll, view);
            _text = SlopWidgets.Area(view.ContractedBy(6f, 4f), "config.toml", _text,
                _loaded, frame: false);
            Widgets.EndScrollView();

            if (_error != null)
            {
                GUI.color = SlopWidgets.Bad;
                Widgets.Label(new Rect(rect.x, area.yMax + 2f, rect.width, 40f), _error);
                GUI.color = Color.white;
            }

            var foot = new SlopWidgets.Bar(
                new Rect(rect.x, rect.yMax - 36f, rect.width, SlopWidgets.BtnH));
            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (foot.Right("Save", SlopWidgets.Btn.Primary, _loaded)) Save();
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
