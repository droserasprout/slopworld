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
        string _error;
        bool _pending;
        readonly OperationGate _operations = new OperationGate();
        readonly SmoothScroll _scroll = new SmoothScroll();

        // One row and the clearance under it, so the list has a pitch rather than two figures
        // four pixels apart written at three call sites.
        static float Pitch => UiTheme.RowH + UiTheme.GapXS;

        public BrowseDialog(string start, System.Action<string> pick)
        {
            _pick = pick;
            AcceptOnEnter(UseCurrent);
            Load(start ?? "");
        }

        public override Vector2 InitialSize => new Vector2(520f, 480f);

        void Load(string path)
        {
            int generation = _operations.Begin();
            _pending = true;
            _error = null;
            _path = null;
            _parent = null;
            _dirs = new string[0];

            DaemonClient.Get($"{WireProtocol.Routes.Browse}?path={System.Uri.EscapeDataString(path)}",
                j =>
                {
                    if (!_operations.IsCurrent(generation)) return;

                    string resolved = j == null ? null : j["path"].AsString(null);
                    if (resolved == null)
                    {
                        _pending = false;
                        _error = "The daemon returned no directory path.";
                        return;
                    }

                    _path = resolved;
                    _parent = j["parent"].IsNull ? null : j["parent"].AsString();
                    _dirs = j["dirs"].Items.Select(d => d.AsString()).ToArray();
                    _pending = false;
                },
                msg =>
                {
                    if (!_operations.IsCurrent(generation)) return;
                    _pending = false;
                    _error = msg;
                    UiLayout.Fail(msg);
                });
        }

        void UseCurrent()
        {
            if (_pending || _path == null) return;
            _pick?.Invoke(_path);
            Close();
        }

        protected override void DoBody(Rect rect)
        {
            UiLayout.PageCaption(TitleRect(rect), _path ?? (_pending ? "loading..." :
                _error ?? "no directory selected"));

            float top = rect.y + UiTheme.RowH + UiTheme.GapXS;
            var list = new Rect(rect.x, top, rect.width,
                rect.yMax - UiTheme.BtnH - UiTheme.GapS - top);
            int count = _pending ? 0 : _dirs.Length + (_parent != null ? 1 : 0);
            var view = new Rect(0f, 0f, list.width - UiTheme.ScrollbarW, count * Pitch);

            using (_scroll.Scope(list, view))
            {
                float y = 0f;
                if (!_pending && _parent != null)
                {
                    // Ghost the whole way down: forty directories in forty raised slabs is a wall
                    // of buttons, and what this is is a list that answers to a click.
                    if (UiButtons.Button(new Rect(0f, y, view.width, UiTheme.RowH), "..",
                            UiTheme.Btn.Ghost))
                        Load(_parent);
                    y += Pitch;
                }

                if (!_pending) foreach (var d in _dirs)
                    {
                        if (UiButtons.Button(new Rect(0f, y, view.width, UiTheme.RowH), d,
                                UiTheme.Btn.Ghost))
                        {
                            Load(System.IO.Path.Combine(_path ?? "", d).Replace('\\', '/'));
                            break; // _dirs is about to be replaced under us
                        }
                        y += Pitch;
                    }
            }

            if (UiButtons.Button(UiLayout.FooterBar(rect),
                    "Use this directory", UiTheme.Btn.Primary, !_pending && _path != null))
                UseCurrent();
        }

        public override void PostClose()
        {
            _operations.Invalidate();
            _pending = false;
            base.PostClose();
        }
    }
}
