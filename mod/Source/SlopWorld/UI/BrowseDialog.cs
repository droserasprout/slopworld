using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The game is inside Wine and cannot see the host filesystem, so the daemon does
    // the listing.
    public class BrowseDialog : UiWindow
    {
        readonly System.Action<string> _pick;
        string _path;
        string _parent;
        string[] _dirs = new string[0];
        readonly SmoothScroll _scroll = new SmoothScroll();

        // One row and the clearance under it, so the list has a pitch rather than two figures
        // four pixels apart written at three call sites.
        static float Pitch => UiWidgets.RowH + UiWidgets.GapXS;

        public BrowseDialog(string start, System.Action<string> pick)
        {
            _pick = pick;
            AcceptOnEnter(() =>
            {
                _pick?.Invoke(_path);
                Close();
            });
            Load(start ?? "");
        }

        public override Vector2 InitialSize => new Vector2(520f, 480f);

        void Load(string path)
        {
            DaemonClient.Get($"/api/browse?path={System.Uri.EscapeDataString(path)}",
                j =>
                {
                    _path = j["path"].AsString();
                    _parent = j["parent"].IsNull ? null : j["parent"].AsString();
                    _dirs = j["dirs"].Items.Select(d => d.AsString()).ToArray();
                },
                UiWidgets.Fail);
        }

        protected override void DoBody(Rect rect)
        {
            UiWidgets.PageCaption(rect, _path ?? "loading...");

            float top = rect.y + UiWidgets.RowH + UiWidgets.GapXS;
            var list = new Rect(rect.x, top, rect.width,
                rect.yMax - UiWidgets.BtnH - UiWidgets.GapS - top);
            int count = _dirs.Length + (_parent != null ? 1 : 0);
            var view = new Rect(0f, 0f, list.width - UiWidgets.ScrollbarW, count * Pitch);

            using (_scroll.Scope(list, view))
            {
                float y = 0f;
                if (_parent != null)
                {
                    // Ghost the whole way down: forty directories in forty raised slabs is a wall
                    // of buttons, and what this is is a list that answers to a click.
                    if (UiWidgets.Button(new Rect(0f, y, view.width, UiWidgets.RowH), "..",
                            UiWidgets.Btn.Ghost))
                        Load(_parent);
                    y += Pitch;
                }

                foreach (var d in _dirs)
                {
                    if (UiWidgets.Button(new Rect(0f, y, view.width, UiWidgets.RowH), d,
                            UiWidgets.Btn.Ghost))
                    {
                        Load(System.IO.Path.Combine(_path ?? "", d).Replace('\\', '/'));
                        break; // _dirs is about to be replaced under us
                    }
                    y += Pitch;
                }
            }

            if (UiWidgets.Button(UiWidgets.FooterBar(rect),
                    "Use this directory", UiWidgets.Btn.Primary))
            {
                _pick(_path);
                Close();
            }
        }
    }
}
