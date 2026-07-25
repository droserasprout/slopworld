using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>Add or edit one session. Writes straight through to config.toml on the daemon.</summary>
    public class EditSessionDialog : Window
    {
        readonly bool _isNew;
        readonly SessionInfo _s;
        /// The name the daemon still knows this session by: the edit is addressed
        /// to it, and a changed name in the field is a rename.
        readonly string _origName;
        string _cols, _rows;

        public EditSessionDialog(SessionInfo existing)
        {
            _isNew = existing == null;
            _origName = existing?.Name ?? "";
            _s = existing == null
                ? new SessionInfo { Name = "", Dir = "", Agent = "", Net = true, Sandbox = true }
                : new SessionInfo
                {
                    Name = existing.Name,
                    Dir = existing.Dir,
                    Agent = existing.Agent,
                    Net = existing.Net,
                    Sandbox = existing.Sandbox,
                    Autostart = existing.Autostart,
                    Cols = existing.Cols,
                    Rows = existing.Rows,
                };

            _cols = _s.Cols.ToString();
            _rows = _s.Rows.ToString();

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(560f, 400f);

        public override void DoWindowContents(Rect rect)
        {
            var l = new Listing_Standard();
            l.Begin(rect);

            Text.Font = GameFont.Medium;
            l.Label(_isNew ? "New session" : $"Edit '{_s.Name}'");
            Text.Font = GameFont.Small;
            l.Gap(6f);

            l.Label("Name (also the colonist's name)");
            _s.Name = l.TextEntry(_s.Name);

            l.Gap(4f);
            l.Label("Project directory");
            _s.Dir = l.TextEntry(_s.Dir);
            if (l.ButtonText("Browse..."))
                Find.WindowStack.Add(new BrowseDialog(_s.Dir, d => _s.Dir = d));

            l.Gap(4f);
            l.Label("Agent command (blank = daemon default)");
            _s.Agent = l.TextEntry(_s.Agent ?? "");

            l.Gap(6f);
            l.CheckboxLabeled("Sandbox with bubblewrap", ref _s.Sandbox);
            l.CheckboxLabeled("Allow network", ref _s.Net);

            l.Gap(6f);
            var row = l.GetRect(28f);
            Widgets.Label(new Rect(row.x, row.y, 90f, 24f), "Cols");
            _cols = Widgets.TextField(new Rect(row.x + 90f, row.y, 70f, 24f), _cols);
            Widgets.Label(new Rect(row.x + 180f, row.y, 90f, 24f), "Rows");
            _rows = Widgets.TextField(new Rect(row.x + 270f, row.y, 70f, 24f), _rows);

            l.End();

            var bar = new Rect(rect.x, rect.yMax - 36f, rect.width, 32f);
            if (Widgets.ButtonText(new Rect(bar.x, bar.y, 120f, 32f), "Cancel"))
                Close();

            if (Widgets.ButtonText(new Rect(bar.xMax - 120f, bar.y, 120f, 32f), "Save"))
                Save();
        }

        void Save()
        {
            if (string.IsNullOrEmpty(_s.Name) || string.IsNullOrEmpty(_s.Dir))
            {
                Messages.Message("SlopWorld: name and directory are required.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (int.TryParse(_cols, out int c)) _s.Cols = Mathf.Clamp(c, 20, 500);
            if (int.TryParse(_rows, out int r)) _s.Rows = Mathf.Clamp(r, 5, 200);

            string from = _origName, to = _s.Name;
            SessionHub.Instance.Save(_s, _isNew, _origName,
                ok: () =>
                {
                    // The daemon took the rename, so carry the colonist over before
                    // the next reconcile sees a name it doesn't know and retires it.
                    if (!_isNew && from != to) AgentColony.Current?.Rename(from, to);
                    Close();
                },
                fail: msg => Messages.Message($"SlopWorld: {msg}",
                    MessageTypeDefOf.RejectInput, false));
        }
    }

    /// <summary>
    /// Directory picker backed by the daemon's /api/browse. The game is inside Wine
    /// and cannot see the host filesystem, so the daemon does the listing.
    /// </summary>
    public class BrowseDialog : Window
    {
        readonly System.Action<string> _pick;
        string _path;
        string _parent;
        string[] _dirs = new string[0];
        Vector2 _scroll;

        public BrowseDialog(string start, System.Action<string> pick)
        {
            _pick = pick;
            doCloseX = true;
            absorbInputAroundWindow = true;
            Load(start ?? "");
        }

        public override Vector2 InitialSize => new Vector2(520f, 480f);

        void Load(string path)
        {
            SlopClient.Get($"/api/browse?path={System.Uri.EscapeDataString(path)}",
                j =>
                {
                    _path = j["path"].AsString();
                    _parent = j["parent"].IsNull ? null : j["parent"].AsString();
                    _dirs = j["dirs"].Items.Select(d => d.AsString()).ToArray();
                },
                msg => Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false));
        }

        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), _path ?? "loading...");

            var list = new Rect(rect.x, rect.y + 30f, rect.width, rect.height - 76f);
            int count = _dirs.Length + (_parent != null ? 1 : 0);
            var view = new Rect(0f, 0f, list.width - 18f, count * 28f);

            Widgets.BeginScrollView(list, ref _scroll, view);
            float y = 0f;

            if (_parent != null)
            {
                if (Widgets.ButtonText(new Rect(0f, y, view.width, 26f), ".."))
                    Load(_parent);
                y += 28f;
            }

            foreach (var d in _dirs)
            {
                if (Widgets.ButtonText(new Rect(0f, y, view.width, 26f), d))
                {
                    Load(System.IO.Path.Combine(_path ?? "", d).Replace('\\', '/'));
                    break; // _dirs is about to be replaced under us
                }
                y += 28f;
            }
            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(rect.x, rect.yMax - 36f, rect.width, 32f),
                                   $"Use this directory"))
            {
                _pick(_path);
                Close();
            }
        }
    }
}
