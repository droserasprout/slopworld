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
            Widgets.DrawBoxSolid(area, SlopWidgets.Well);

            var view = new Rect(0f, 0f, area.width - 18f,
                Mathf.Max(area.height, Text.CalcHeight(_text, area.width - 24f) + 40f));

            Widgets.BeginScrollView(area, ref _scroll, view);
            _text = Widgets.TextArea(view.ContractedBy(4f), _text, !_loaded);
            Widgets.EndScrollView();

            if (_error != null)
            {
                GUI.color = SlopWidgets.Bad;
                Widgets.Label(new Rect(rect.x, area.yMax + 2f, rect.width, 40f), _error);
                GUI.color = Color.white;
            }

            var bar = new Rect(rect.x, rect.yMax - 36f, rect.width, 32f);
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 120f, 32f), "Reload"))
                Load();

            if (Widgets.ButtonText(new Rect(bar.xMax - 120f, bar.y, 120f, 32f), "Save"))
                Save();
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
