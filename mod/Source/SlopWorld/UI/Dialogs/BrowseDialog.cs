using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The game is inside Wine and cannot see the host filesystem, so the daemon does
    // the listing.
    public class BrowseDialog : UiWindow
    {
        readonly System.Action<string> _pick;
        readonly BrowseDialogState _state = new BrowseDialogState();
        string _filter = "";
        readonly SmoothScroll _scroll = new SmoothScroll();

        // One row and the clearance under it, so the list has a pitch rather than two figures
        // four pixels apart written at three call sites.
        static float Pitch => UiTheme.RowH + UiTheme.GapXS;

        public BrowseDialog(string start, System.Action<string> pick)
        {
            resizeable = true;
            _pick = pick;
            AcceptOnEnter(UseCurrent);
            Load(start ?? "");
        }

        public override Vector2 InitialSize => new Vector2(520f, 480f);

        protected override Vector2 MinimumSize => new Vector2(420f, 340f);

        void Load(string path, bool keepFilter = false)
        {
            if (!keepFilter)
                _filter = "";
            long generation = _state.Begin();

            DaemonClient.Get<Wire.BrowseResult>(
                $"{WireProtocol.Routes.Browse}?path={System.Uri.EscapeDataString(path)}&filter={System.Uri.EscapeDataString(_filter)}",
                j =>
                {
                    _state.Accept(generation, j);
                },
                msg =>
                {
                    if (_state.Fail(generation, msg))
                        UiLayout.Fail(msg);
                }
            );
        }

        void UseCurrent()
        {
            if (_state.Pending || _state.Path == null)
                return;
            _pick?.Invoke(_state.Path);
            Close();
        }

        protected override void DoBody(Rect rect)
        {
            UiLayout.PageCaption(
                TitleRect(rect),
                _state.Path
                    ?? (_state.Pending ? "Loading" : _state.Error ?? "no directory selected")
            );

            float top = rect.y + UiTheme.RowH + UiTheme.GapXS;
            float buttonWidth = 80f;
            _filter = UiText.Field(
                new Rect(rect.x, top, rect.width - buttonWidth - UiTheme.GapS, UiTheme.FieldH),
                "browse.filter",
                _filter
            );
            if (
                UiButtons.Button(
                    new Rect(rect.xMax - buttonWidth, top, buttonWidth, UiTheme.FieldH),
                    "Filter",
                    on: !_state.Pending && _state.Path != null
                )
            )
                Load(_state.Path, keepFilter: true);
            top += UiTheme.FieldH + UiTheme.GapS;
            UiText.PlainStatusLabel(
                new Rect(rect.x, top, rect.width, UiTheme.RowH * 2f),
                _state.Error
                    ?? (
                        _state.Truncated
                            ? "More directories exist. Filter by name to find omitted entries."
                            : "Filter by directory name (case-sensitive). Clear it to show all names."
                    ),
                _state.Error == null ? UiTheme.Dim : UiTheme.Bad
            );
            top += UiTheme.RowH * 2f + UiTheme.GapS;
            var list = new Rect(
                rect.x,
                top,
                rect.width,
                rect.yMax - UiTheme.BtnH - UiTheme.GapS - top
            );
            int count = _state.Pending
                ? 0
                : _state.Directories.Length + (_state.Parent != null ? 1 : 0);
            var view = new Rect(0f, 0f, list.width - UiTheme.ScrollbarW, count * Pitch);

            using (_scroll.Scope(list, view))
            {
                float y = 0f;
                if (!_state.Pending && _state.Parent != null)
                {
                    // Ghost the whole way down: forty directories in forty raised slabs is a wall
                    // of buttons, and what this is is a list that answers to a click.
                    if (
                        UiButtons.Button(
                            new Rect(0f, y, view.width, UiTheme.RowH),
                            "..",
                            UiTheme.Btn.Ghost
                        )
                    )
                        Load(_state.Parent);
                    y += Pitch;
                }

                if (!_state.Pending)
                    foreach (var d in _state.Directories)
                    {
                        if (
                            UiButtons.Button(
                                new Rect(0f, y, view.width, UiTheme.RowH),
                                d,
                                UiTheme.Btn.Ghost
                            )
                        )
                        {
                            Load(System.IO.Path.Combine(_state.Path ?? "", d).Replace('\\', '/'));
                            break; // _state.Directories is about to be replaced under us
                        }
                        y += Pitch;
                    }
            }

            if (
                UiButtons.Button(
                    UiLayout.FooterBar(rect),
                    "Use this directory",
                    UiTheme.Btn.Primary,
                    !_state.Pending && _state.Path != null
                )
            )
                UseCurrent();
        }

        public override void PostClose()
        {
            _state.Close();
            base.PostClose();
        }
    }
}
