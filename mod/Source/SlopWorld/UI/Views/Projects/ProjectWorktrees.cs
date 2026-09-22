using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Worktrees outlive agents and tasks. Only this explicit removal requests teardown.
    public sealed class ProjectWorktrees
    {
        readonly string _project;
        readonly SmoothScroll _scroll = new SmoothScroll();
        List<Wire.Worktree> _rows;
        string _error;
        bool _busy;

        public ProjectWorktrees(ProjectInfo project)
        {
            _project = project.Name;
            _rows = new List<Wire.Worktree> { new Wire.Worktree { Id = "main", Name = "main", Path = project.ExpandedDir, Phase = "ready" } };
            Refresh();
        }

        public void Refresh()
        {
            _busy = true;
            DaemonClient.Get<Wire.WorktreesReply>(WireProtocol.Routes.Worktrees + "?project=" + Uri.EscapeDataString(_project),
                reply => { _rows = reply.Worktrees.OrderBy(w => w.Id == "main" ? 0 : 1).ToList(); _busy = false; _error = null; },
                Fail, TaskInfo.Host, 60000);
        }

        void Fail(string error) { _busy = false; _error = error; }

        public void Draw(Rect rect)
        {
            var bar = new UiLayout.Bar(new Rect(rect.x, rect.y, rect.width, UiTheme.BtnH));
            if (bar.Left("Add", UiTheme.Btn.Ghost, !_busy)) Open(true);
            if (bar.Left("Create", UiTheme.Btn.Primary, !_busy)) Open(false);
            if (bar.Right("Refresh", UiTheme.Btn.Ghost, !_busy)) Refresh();
            float y = rect.y + UiTheme.BtnH + UiTheme.GapS;
            if (!string.IsNullOrEmpty(_error))
            {
                float h = Text.CalcHeight(_error, rect.width);
                Widgets.Label(new Rect(rect.x, y, rect.width, h), _error);
                y += h + UiTheme.GapS;
            }
            var list = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            Slab.Box(list, UiTheme.Well, UiTheme.Edge);
            var pad = list.ContractedBy(UiTheme.ListInset);
            float width = pad.width - UiTheme.ScrollbarW;
            float rowH = UiTheme.RowH + UiTheme.LineH + UiTheme.GapS;
            using (_scroll.Scope(pad, new Rect(0f, 0f, width, _rows.Count * rowH)))
            {
                y = 0f;
                foreach (var w in _rows)
                {
                    float removeW = 32f, terminalW = 80f, gap = UiTheme.GapS;
                    float labelW = Mathf.Max(0f, width - removeW - terminalW - gap * 2f);
                    var label = new Rect(0f, y, labelW, UiTheme.RowH);
                    string branch = string.IsNullOrEmpty(w.Branch) ? "detached" : w.Branch;
                    UiText.RowLabel(label, (w.Id == "main" ? "main" : w.Name) + " — " + branch +
                        (w.Phase == "ready" ? "" : " (" + w.Phase + ")"));
                    TooltipHandler.TipRegion(label, w.Path + "\nHEAD: " + w.Head +
                        (w.Attachments.Count == 0 ? "" : "\nAttached: " + string.Join(", ", w.Attachments)) +
                        (string.IsNullOrEmpty(w.Error) ? "" : "\n" + w.Error));
                    if (UiButtons.Button(new Rect(labelW + gap, y, terminalW, UiTheme.RowH), "Terminal",
                        UiTheme.Btn.Ghost, !_busy && w.Phase != "removing"))
                        SessionHub.Instance.SessionStore.RunHostShell(_project, name => TerminalWindow.Open(name), Fail, w.Id);
                    if (w.Id != "main")
                    {
                        var remove = new Rect(width - removeW, y, removeW, UiTheme.RowH);
                        TooltipHandler.TipRegion(remove, w.Attachments.Count > 0 ? "Detach agents and terminals before removing." :
                            w.Managed ? "Remove this worktree" : "Unregister this worktree (keep files)");
                        if (UiButtons.Button(remove, "×", UiTheme.Btn.Ghost, !_busy && w.Attachments.Count == 0)) Remove(w);
                    }
                    var path = new Rect(0f, y + UiTheme.RowH, width, UiTheme.LineH);
                    UiText.RowLabel(path, string.IsNullOrEmpty(w.Error) ? w.Path : w.Error);
                    TooltipHandler.TipRegion(path, w.Path + (string.IsNullOrEmpty(w.Error) ? "" : "\n" + w.Error));
                    y += rowH;
                }
            }
        }

        void Open(bool existing) => TerminalWindow.OpenOverPane(new ProjectWorktreeDialog(_project, existing, Refresh));

        void Remove(Wire.Worktree tree)
        {
            _busy = true;
            DaemonClient.Send<Wire.Ack>("DELETE", WireProtocol.Routes.Worktrees + "/" + Uri.EscapeDataString(tree.Id) +
                "?project=" + Uri.EscapeDataString(_project), null, _ => Refresh(), Fail, TaskInfo.Host, 60000);
        }
    }

    public sealed class ProjectWorktreeDialog : UiWindow
    {
        readonly string _project;
        readonly bool _existing;
        readonly Action _changed;
        readonly ScrollableListing _listing = new ScrollableListing(280f);
        string _name = "";
        string _base = "HEAD";
        string _path = "";
        string _error;
        bool _busy;

        public ProjectWorktreeDialog(string project, bool existing, Action changed)
        {
            _project = project; _existing = existing; _changed = changed;
            AcceptOnEnter(Save);
        }

        public override Vector2 InitialSize => new Vector2(580f, 380f);

        protected override void DoBody(Rect rect)
        {
            UiLayout.Title(TitleRect(rect), _existing ? "Add existing worktree" : "Create worktree");
            float y = rect.y + UiTheme.HeaderH + UiTheme.GapM;
            _listing.Draw(new Rect(rect.x, y, rect.width, rect.yMax - y - UiTheme.BtnH - UiTheme.GapM), l =>
            {
                l.Label("Name (optional)"); _name = UiControls.Field(l, "worktree.name", _name);
                if (_existing)
                {
                    l.Label("Existing worktree path"); _path = UiControls.Field(l, "worktree.path", _path);
                }
                else
                {
                    l.Label("Base revision"); _base = UiControls.Field(l, "worktree.base", _base);
                    l.Label("Only committed files are copied. Remove the worktree manually when finished.");
                }
                if (!string.IsNullOrEmpty(_error)) l.Label(_error);
            });
            var foot = new UiLayout.Bar(new Rect(rect.x, rect.yMax - UiTheme.BtnH, rect.width, UiTheme.BtnH));
            if (foot.Left("Cancel", UiTheme.Btn.Ghost, !_busy)) Close();
            if (foot.Right(_existing ? "Add" : "Create", UiTheme.Btn.Primary, !_busy)) Save();
        }

        void Save()
        {
            if (_busy) return;
            if (_existing && string.IsNullOrWhiteSpace(_path)) { _error = "Choose an existing worktree path."; return; }
            _busy = true;
            DaemonClient.Send<Wire.Worktree>("POST", WireProtocol.Routes.Worktrees,
                new Wire.CreateWorktreeReq { Project = _project, Name = _name, Base = _existing ? "" : _base, Path = _existing ? _path : "" },
                _ => { _busy = false; _changed(); Close(); }, error => { _busy = false; _error = error; }, TaskInfo.Host, 60000);
        }
    }
}
