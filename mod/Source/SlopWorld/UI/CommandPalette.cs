using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // F1: VSCode-style command palette. Top centre float, input field, filtered command
    // list, recently used first. Nested sub-commands for actions that need a second
    // selection (e.g. "Agent: Stop" → pick which agent, or "Jukebox: Tune" → station → quality).
    //
    // Everything that any window or button does is reachable from here, so no action is
    // hidden behind a dialog the player has not found yet.
    public class CommandPalette : Window
    {
        const float Width = 520f;
        const float MaxH = 440f;
        const float Pad = SlopWidgets.GapS;

        // The three heights this list is built from, off the font rather than written down:
        // an input is a field, a row is a line with room round it, and a group heading is a
        // tiny line. A figure here holds only for the face it was set against, and a row
        // shorter than its line loses the top and bottom of every label in the palette.
        static float InputH => SlopWidgets.FieldH;
        static float RowH => SlopWidgets.PaletteRowH;
        static float GroupH => SlopWidgets.TinyRowH;
        const int RecentMax = 8;
        // What a hit found only in a command's id is docked, the name being what is read.
        const int IdCost = 80;

        string _input = "";
        string _filter = "";
        bool _focusInput = true;

        List<Hit> _matches = new List<Hit>();
        // How many of _matches are recently used (front of the list, no filter).
        int _recentInList;

        // Sub-mode: a command that needs a second selection transitions here.
        Entry _subCmd;
        string _subPrompt;
        string _subFilter = "";
        bool _subHasFilter;
        List<SubOption> _subOptions = new List<SubOption>();
        // _subOptions after the filter, rebuilt when it moves rather than per frame.
        List<SubHit> _subShown = new List<SubHit>();
        int _subIndex;
        readonly List<SubFrame> _subStack = new List<SubFrame>();

        readonly SmoothScroll _scroll = new SmoothScroll();
        int _selectedIndex;
        // Visible height of the list area, used by ScrollToSelection.
        float _listH;

        static List<Entry> _commands;
        static readonly List<string> _recent = new List<string>();

        enum Mode { Commands, Sub }
        Mode _mode = Mode.Commands;

        public CommandPalette()
        {
            doCloseX = false;
            doCloseButton = false;
            doWindowBackground = false;
            drawShadow = false;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = false;
            forcePause = false;
            layer = WindowLayer.Super;
            preventCameraMotion = false;
            draggable = false;
            resizeable = false;

            if (_commands == null) BuildCommands();
            RebuildMatches();
        }

        protected override float Margin => 0f;

        public override Vector2 InitialSize =>
            new Vector2(Width, Mathf.Min(ContentHeight(), MaxH));

        protected override void SetInitialSizeAndPosition()
        {
            var size = InitialSize;
            windowRect = new Rect(
                (UI.screenWidth - size.x) / 2f,
                Mathf.Max(60f, UI.screenHeight * 0.08f),
                size.x, size.y);
        }

        public static void Toggle()
        {
            var open = Find.WindowStack.WindowOfType<CommandPalette>();
            if (open != null) { open.Close(); return; }

            SessionHub.Instance.Refresh();
            SessionHub.Instance.RefreshProjects();
            SessionHub.Instance.RefreshShortcuts();
            SessionHub.Instance.LoadPresets();

            Find.WindowStack.Add(new CommandPalette());
        }

        public override void DoWindowContents(Rect rect)
        {
            // A raised rectangular surface: this is an instrument panel, not a vanilla menu.
            Slab.Box(rect, SlopWidgets.PopoverBg, SlopWidgets.Edge);

            var inputRect = new Rect(rect.x + Pad, rect.y + Pad,
                rect.width - Pad * 2, InputH);

            float listTop = inputRect.yMax + SlopWidgets.GapXS;
            var listRect = new Rect(rect.x + Pad, listTop,
                rect.width - Pad * 2, rect.yMax - listTop - Pad);
            // Set before DrawInput, which reads it when a key scrolls the selection.
            _listH = listRect.height;

            DrawInput(inputRect);

            if (_mode == Mode.Sub) DrawSubList(listRect);
            else DrawCommandList(listRect);
        }

        void DrawInput(Rect r)
        {
            // One entry round the whole line, prompt included: in sub-mode the prompt is part
            // of what is being typed into, not a label beside a second box.
            SlopWidgets.FieldFrame(r, GUI.GetNameOfFocusedControl() == "paletteInput");
            var inner = r.ContractedBy(SlopWidgets.FieldPadX, SlopWidgets.FieldPadY);

            var e = Event.current;
            bool isKeyDown = e.type == EventType.KeyDown;

            // Space ticks the row a checklist has under the cursor rather than being typed
            // into the filter, and the palette stays up - ticking one of several is the
            // whole point of a list of boxes. IMGUI sends the key and the character it
            // produced as two events and the field reads the second, so both are taken
            // here; only the one carrying the key code is the press.
            if (isKeyDown && _mode == Mode.Sub && Checklist &&
                (e.keyCode == KeyCode.Space || e.character == ' '))
            {
                if (e.keyCode == KeyCode.Space) ToggleSub();
                e.Use();
                return;
            }

            // Handle navigation keys before the text field, which would otherwise consume
            // arrows, Escape and Enter for its own cursor motion and focus management.
            if (isKeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.Escape:
                        if (_mode == Mode.Sub) BackSub();
                        else Close();
                        e.Use();
                        return;

                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        if (_mode == Mode.Sub) ExecuteSub();
                        else ExecuteSelected();
                        e.Use();
                        return;

                    case KeyCode.UpArrow:
                        if (_mode == Mode.Sub)
                        {
                            _subIndex = Mathf.Max(0, _subIndex - 1);
                            ScrollToSub();
                        }
                        else
                        {
                            _selectedIndex = Mathf.Max(0, _selectedIndex - 1);
                            ScrollToSelected();
                        }
                        e.Use();
                        return;

                    case KeyCode.DownArrow:
                        if (_mode == Mode.Sub)
                        {
                            _subIndex = Mathf.Min(_subShown.Count - 1, _subIndex + 1);
                            ScrollToSub();
                        }
                        else
                        {
                            _selectedIndex = Mathf.Min(_matches.Count - 1, _selectedIndex + 1);
                            ScrollToSelected();
                        }
                        e.Use();
                        return;
                }
            }

            if (_mode == Mode.Sub)
            {
                // Prompt on the left, filter input on the right.
                string prompt = _subPrompt + " ";
                float promptW = SlopWidgets.Wide(prompt);
                var labelRect = new Rect(inner.x, inner.y, promptW, inner.height);
                var fieldRect = new Rect(inner.x + promptW, inner.y,
                    inner.width - promptW, inner.height);

                GUI.color = SlopWidgets.Dim;
                SlopWidgets.RowLabel(labelRect, prompt);
                GUI.color = Color.white;

                bool hadFilter = _subHasFilter;
                string wasSub = _subFilter;
                _subFilter = SlopWidgets.BareField(fieldRect, "paletteInput", _subFilter);
                _subHasFilter = !string.IsNullOrEmpty(_subFilter);
                if (_subFilter != wasSub)
                {
                    RebuildSub();
                    _subIndex = 0;
                    _scroll.JumpTo(Vector2.zero);
                    Resize();
                }

                // Backspace on empty filter in sub-mode: go back to command list.
                // The text field was empty so it didn't consume the key; ours to take.
                if (isKeyDown && e.keyCode == KeyCode.Backspace && !hadFilter)
                {
                    BackSub();
                    e.Use();
                    return;
                }
            }
            else
            {
                string was = _input;
                _input = SlopWidgets.BareField(inner, "paletteInput", _input);
                if (_input != was)
                {
                    _filter = _input.ToLowerInvariant();
                    RebuildMatches();
                    _selectedIndex = 0;
                    _scroll.JumpTo(Vector2.zero);
                    Resize();
                }
            }

            if (_focusInput)
            {
                GUI.FocusControl("paletteInput");
                _focusInput = false;
            }
        }

        void BackToCommands()
        {
            _mode = Mode.Commands;
            _subCmd = null;
            _subStack.Clear();
            _subFilter = "";
            _subHasFilter = false;
            _input = "";
            _filter = "";
            _selectedIndex = 0;
            _scroll.JumpTo(Vector2.zero);
            _focusInput = true;
            RebuildMatches();
            Resize();
        }

        // The box is as tall as what is in it: filtered down to one answer, a palette
        // holding its opening height is mostly empty dark. Lands next frame, this one's
        // window group having been opened already.
        void Resize()
        {
            float h = ContentHeight();
            if (Mathf.Abs(windowRect.height - h) > 0.5f) windowRect.height = h;
        }

        // Walk the same order as DrawCommandList to find the y of the selected item,
        // then scroll to keep it visible.
        void ScrollToSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _matches.Count) return;

            float y = 0f;
            string prev = null;
            for (int i = 0; i < _selectedIndex; i++)
            {
                if (Grouped)
                {
                    string group = GroupOf(i);
                    if (group != prev) { y += GroupH; prev = group; }
                }
                y += RowH;
            }

            _scroll.Reveal(y, RowH, _listH);
        }

        void ScrollToSub()
        {
            float y = _subIndex * RowH;
            _scroll.Reveal(y, RowH, _listH);
        }

        // --------------------------------------------------------------- command list

        void DrawCommandList(Rect r)
        {
            if (_matches.Count == 0)
            {
                GUI.color = SlopWidgets.Faint;
                SlopWidgets.RowLabel(r, _filter.Length > 0
                    ? "No matching commands"
                    : "No commands available", TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                return;
            }

            // Total height, with a header where the group changes. Recent entries carry
            // their own group so they read as one block ahead of the rest.
            float totalH = 0f;
            string prev = null;
            for (int i = 0; i < _matches.Count; i++)
            {
                if (Grouped)
                {
                    string group = GroupOf(i);
                    if (group != prev) { totalH += GroupH; prev = group; }
                }
                totalH += RowH;
            }

            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW, totalH);

            _scroll.Begin(r, view);

            float y = 0f;
            prev = null;
            for (int i = 0; i < _matches.Count; i++)
            {
                string group = Grouped ? GroupOf(i) : null;
                if (group != null && group != prev)
                {
                    var header = new Rect(0f, y, view.width, GroupH);
                    GUI.color = SlopWidgets.Faint;
                    Text.Font = GameFont.Tiny;
                    SlopWidgets.RowLabel(header, group.ToUpperInvariant());
                    Text.Font = GameFont.Small;
                    GUI.color = Color.white;
                    y += GroupH;
                    prev = group;
                }

                var row = new Rect(0f, y, view.width, RowH);
                bool selected = i == _selectedIndex;

                if (selected)
                    Slab.Fill(row, SlopWidgets.Sel);
                else
                    if (Mouse.IsOver(row)) Slab.Fill(row, SlopWidgets.Hover);

                if (Widgets.ButtonInvisible(row))
                {
                    _selectedIndex = i;
                    if (_matches[i].E.SubAction != null) EnterSub(_matches[i].E);
                    else Execute(_matches[i].E);
                }

                GUI.color = selected ? SlopWidgets.Lead : SlopWidgets.Name;
                SlopWidgets.RowLabel(
                    new Rect(row.x + SlopWidgets.FieldPadX, row.y + SlopWidgets.FieldPadY,
                        view.width - SlopWidgets.FieldPadX * 2f,
                        RowH - SlopWidgets.FieldPadY * 2f),
                    _matches[i].Label);
                GUI.color = Color.white;

                y += RowH;
            }

            _scroll.End();
        }

        // A filtered list is ranked rather than grouped: the answer is the top row, and a
        // heading between every pair of rows is where that stops reading as an order.
        bool Grouped => _filter.Length == 0;

        // Recent entries (front of the list, no filter) group under "Recently"; everything
        // else under its own category.
        string GroupOf(int index) =>
            index < _recentInList ? "Recently" : _matches[index].E.Category;

        // --------------------------------------------------------------- sub list

        void DrawSubList(Rect r)
        {
            var options = _subShown;

            if (options.Count == 0)
            {
                GUI.color = SlopWidgets.Faint;
                SlopWidgets.RowLabel(r, _subHasFilter ? "No matches" : "Nothing available",
                    TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                return;
            }

            _subIndex = Mathf.Clamp(_subIndex, 0, options.Count - 1);

            float totalH = options.Count * RowH;
            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW, totalH);

            _scroll.Begin(r, view);

            float y = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                var row = new Rect(0f, y, view.width, RowH);
                bool selected = i == _subIndex;

                if (selected)
                    Slab.Fill(row, SlopWidgets.Sel);
                else
                    if (Mouse.IsOver(row)) Slab.Fill(row, SlopWidgets.Hover);

                if (Widgets.ButtonInvisible(row))
                {
                    _subIndex = i;
                    ExecuteSub();
                }

                float left = row.x + SlopWidgets.FieldPadX;

                // The checkbox goes before the label, the way a settings page draws one,
                // and the label starts after it. Rows without one keep the whole line:
                // a list is all ticks or none, so nothing is left hanging.
                var box = options[i].O.Checked;
                if (box.HasValue)
                {
                    SlopWidgets.TickBox(new Rect(left, row.y, SlopWidgets.TickW, RowH),
                        box.Value);
                    left += SlopWidgets.TickColW;
                }

                if (!options[i].O.Enabled)
                {
                    GUI.color = SlopWidgets.Off;
                }
                else
                {
                    GUI.color = selected ? SlopWidgets.Lead : SlopWidgets.Name;
                }

                SlopWidgets.RowLabel(
                    new Rect(left, row.y + SlopWidgets.FieldPadY,
                        row.xMax - SlopWidgets.FieldPadX - left,
                        RowH - SlopWidgets.FieldPadY * 2f),
                    options[i].Label);
                GUI.color = Color.white;

                y += RowH;
            }

            _scroll.End();
        }

        // --------------------------------------------------------------- entry model

        class Entry
        {
            public string Id;
            public string Name;
            public string Category;
            // If set, this command needs a sub-selection.
            public Func<List<SubOption>> SubAction;
            // The action to execute. Called with null for commands with no sub-action,
            // or with the selected sub-option's value.
            public Action<string> Execute;
        }

        class SubOption
        {
            public string Label;
            public string Value;
            public bool Enabled = true;
            public Func<List<SubOption>> Children;
            public Action Select;
            // A checkbox row when it is set, and the state the box is in. Null for the sub
            // lists that pick one thing and are done, which is most of them.
            public bool? Checked;
        }

        class SubFrame
        {
            public List<SubOption> Options;
            public string Prompt;
            public string Filter;
            public bool HasFilter;
            public int Index;
        }

        // A row as the list draws it: the command, and the name with whatever the search
        // matched marked up. Built when the filter moves, not per frame.
        class Hit
        {
            public Entry E;
            public string Label;
            public int Score;
        }

        class SubHit
        {
            public SubOption O;
            public string Label;
            public int Score;
        }

        // --------------------------------------------------------------- command catalogue

        static void BuildCommands()
        {
            _commands = new List<Entry>();

            // Agent
            _commands.Add(new Entry
            {
                Id = "agent.start",
                Name = "Agent: Start",
                Category = "Agent",
                SubAction = () => AgentsSub(AgentState.Down),
                Execute = v => { if (v != null) SessionHub.Instance.Start(v, SlopWidgets.Fail); },
            });
            _commands.Add(new Entry
            {
                Id = "agent.stop",
                Name = "Agent: Stop",
                Category = "Agent",
                SubAction = () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                Execute = v => { if (v != null) SessionHub.Instance.Stop(v, SlopWidgets.Fail); },
            });
            _commands.Add(new Entry
            {
                Id = "agent.restart",
                Name = "Agent: Restart",
                Category = "Agent",
                SubAction = () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                Execute = v => { if (v != null) SessionHub.Instance.Restart(v, SlopWidgets.Fail); },
            });
            _commands.Add(new Entry
            {
                Id = "agent.edit",
                Name = "Agent: Edit",
                Category = "Agent",
                SubAction = () => AgentsSubAll(),
                Execute = v =>
                {
                    if (v == null) return;
                    var info = SessionHub.Instance.Get(v);
                    if (info != null) Find.WindowStack.Add(new EditSessionDialog(info));
                },
            });
            _commands.Add(new Entry
            {
                Id = "agent.terminal",
                Name = "Agent: Open Terminal",
                Category = "Agent",
                SubAction = () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                Execute = v =>
                {
                    if (v != null)
                    {
                        var info = SessionHub.Instance.Get(v);
                        if (info != null && !info.Gone) TerminalWindow.Open(v);
                    }
                },
            });
            _commands.Add(new Entry
            {
                Id = "agent.delete",
                Name = "Agent: Delete",
                Category = "Agent",
                SubAction = () => AgentsSubAll(),
                Execute = v =>
                {
                    if (v == null) return;
                    var name = v;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Remove session '{name}'? This kills it, drops it from config.toml, and moves " +
                    "its private state to recoverable trash for 14 days.",
                        () => SessionHub.Instance.Remove(name, SlopWidgets.Fail), destructive: true));
                },
            });
            _commands.Add(new Entry
            {
                Id = "agent.duplicate",
                Name = "Agent: Duplicate",
                Category = "Agent",
                SubAction = () => AgentsSubWithProject(),
                Execute = v =>
                {
                    if (v == null) return;
                    var info = SessionHub.Instance.Get(v);
                    if (info != null && !string.IsNullOrEmpty(info.Project))
                        TerminalWindow.OpenOverPane(EditSessionDialog.Copy(info));
                },
            });

            // Project
            _commands.Add(new Entry
            {
                Id = "project.new",
                Name = "Project: New",
                Category = "Project",
                Execute = _ => Find.WindowStack.Add(new EditProjectDialog(null)),
            });
            _commands.Add(new Entry
            {
                Id = "project.edit",
                Name = "Project: Edit",
                Category = "Project",
                SubAction = () => ProjectsSub(),
                Execute = v =>
                {
                    if (v == null) return;
                    var p = SessionHub.Instance.Project(v);
                    if (p != null) Find.WindowStack.Add(new EditProjectDialog(p));
                },
            });
            _commands.Add(new Entry
            {
                Id = "project.delete",
                Name = "Project: Delete",
                Category = "Project",
                SubAction = () => ProjectsSub(),
                Execute = v =>
                {
                    if (v == null) return;
                    var name = v;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Remove project '{name}'? The directory is left alone; only the entry in config.toml goes.",
                        () => SessionHub.Instance.RemoveProject(name, SlopWidgets.Fail), destructive: true));
                },
            });
            _commands.Add(new Entry
            {
                Id = "project.duplicate",
                Name = "Project: Duplicate",
                Category = "Project",
                SubAction = () => ProjectsSub(),
                Execute = v =>
                {
                    if (v == null) return;
                    var project = SessionHub.Instance.Project(v);
                    if (project != null)
                        TerminalWindow.OpenOverPane(EditProjectDialog.Copy(project));
                },
            });
            _commands.Add(new Entry
            {
                Id = "project.host-terminal",
                Name = "Project: Open Host Terminal",
                Category = "Project",
                SubAction = () => ProjectsSub(),
                Execute = v =>
                {
                    if (v == null) return;
                    SessionHub.Instance.RunHostShell(v,
                        session => TerminalWindow.Open(session), SlopWidgets.Fail);
                },
            });

            // Shortcut
            _commands.Add(new Entry
            {
                Id = "shortcut.run",
                Name = "Shortcut: Run",
                Category = "Shortcut",
                SubAction = () => ShortcutsSub(),
                Execute = v =>
                {
                    if (v == null) return;
                    var info = SessionHub.Instance.Shortcut(v);
                    if (info == null || info.Kind == ShortcutKind.Breadcrumb ||
                        info.Kind == ShortcutKind.FileAction) return;
                    if (info.Link == ShortcutLink.Ask) AskWhere(info);
                    else RunShortcutWith(v);
                },
            });
            _commands.Add(new Entry
            {
                Id = "shortcut.new",
                Name = "Shortcut: New",
                Category = "Shortcut",
                Execute = _ => Find.WindowStack.Add(new EditShortcutDialog(null)),
            });
            _commands.Add(new Entry
            {
                Id = "shortcut.edit",
                Name = "Shortcut: Edit",
                Category = "Shortcut",
                SubAction = () => ShortcutsSub(),
                Execute = v =>
                {
                    if (v == null) return;
                    var info = SessionHub.Instance.Shortcut(v);
                    if (info != null) Find.WindowStack.Add(new EditShortcutDialog(info));
                },
            });
            _commands.Add(new Entry
            {
                Id = "shortcut.delete",
                Name = "Shortcut: Delete",
                Category = "Shortcut",
                SubAction = () => ShortcutsSub(),
                Execute = v =>
                {
                    if (v == null) return;
                    var name = v;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Remove shortcut '{name}'? Anything it already started keeps running.",
                        () => SessionHub.Instance.RemoveShortcut(name, SlopWidgets.Fail), destructive: true));
                },
            });

            // Daemon and data refresh
            _commands.Add(new Entry
            {
                Id = "daemon.reconnect",
                Name = "Daemon: Reconnect",
                Category = "Daemon",
                Execute = _ => SessionHub.Instance.Connect(),
            });
            _commands.Add(new Entry
            {
                Id = "agents.refresh",
                Name = "Agents: Refresh",
                Category = "Refresh",
                Execute = _ => SessionHub.Instance.Refresh(),
            });
            _commands.Add(new Entry
            {
                Id = "projects.refresh",
                Name = "Projects: Refresh",
                Category = "Refresh",
                Execute = _ => SessionHub.Instance.RefreshProjects(SlopWidgets.Fail),
            });
            _commands.Add(new Entry
            {
                Id = "shortcuts.refresh",
                Name = "Shortcuts: Refresh",
                Category = "Refresh",
                Execute = _ => SessionHub.Instance.RefreshShortcuts(SlopWidgets.Fail),
            });
            _commands.Add(new Entry
            {
                Id = "files.reload",
                Name = "Files: Reload",
                Category = "Refresh",
                Execute = _ => FilesView.Reload(),
            });
            _commands.Add(new Entry
            {
                Id = "search.open",
                Name = "Search: Find in Files",
                Category = "View",
                Execute = _ => AgentSidebar.ShowSearch(),
            });
            _commands.Add(new Entry
            {
                Id = "git.refresh",
                Name = "Git: Refresh",
                Category = "Refresh",
                Execute = _ => GitView.Refresh(),
            });

            // View
            _commands.Add(new Entry
            {
                Id = "view.config",
                Name = "View: Config",
                Category = "View",
                Execute = _ => SlopOptions.Toggle(),
            });
            _commands.Add(new Entry
            {
                Id = "view.storage",
                Name = "View: Storage",
                Category = "View",
                Execute = _ => SlopOptions.OpenCategory(SlopOptions.StorageCategory),
            });
            _commands.Add(new Entry
            {
                Id = "config.toml",
                Name = "Config: Edit as TOML",
                Category = "Config",
                Execute = _ => ConfigWindow.Open(),
            });
            _commands.Add(new Entry
            {
                Id = "view.filter",
                Name = "View: Filter Projects",
                Category = "View",
                SubAction = () => FilterSub(),
                Execute = v => { if (v != null) AgentSidebar.ToggleFilter(v); },
            });
            _commands.Add(new Entry
            {
                Id = "view.terminal-settings",
                Name = "View: Terminal",
                Category = "View",
                Execute = _ => SlopOptions.OpenCategory(SlopOptions.TerminalCategory),
            });
            _commands.Add(new Entry
            {
                Id = "view.appearance",
                Name = "View: Appearance",
                Category = "View",
                Execute = _ => SlopOptions.OpenCategory(SlopOptions.AppearanceCategory),
            });
            _commands.Add(new Entry
            {
                Id = "view.zoom-in",
                Name = "View: Zoom In",
                Category = "View",
                Execute = _ => SlopUIScale.Zoom(1),
            });
            _commands.Add(new Entry
            {
                Id = "view.zoom-out",
                Name = "View: Zoom Out",
                Category = "View",
                Execute = _ => SlopUIScale.Zoom(-1),
            });
            _commands.Add(new Entry
            {
                Id = "view.audio",
                Name = "View: Audio",
                Category = "View",
                Execute = _ => SlopOptions.OpenCategory(SlopOptions.AudioCategory),
            });
            _commands.Add(new Entry
            {
                Id = "view.integrations",
                Name = "View: Integrations",
                Category = "View",
                Execute = _ => SlopOptions.OpenCategory(SlopOptions.IntegrationsCategory),
            });
            _commands.Add(new Entry
            {
                Id = "view.usage",
                Name = "View: Usage",
                Category = "View",
                Execute = _ => SlopOptions.OpenCategory(SlopOptions.UsageCategory),
            });
            _commands.Add(new Entry
            {
                Id = "view.summaries",
                Name = "View: Summaries",
                Category = "View",
                Execute = _ => SlopOptions.OpenCategory(SlopOptions.SummariesCategory),
            });
            _commands.Add(new Entry
            {
                Id = "view.sandbox",
                Name = "View: Sandbox",
                Category = "View",
                Execute = _ => SlopOptions.OpenCategory(SlopOptions.SandboxCategory),
            });
            _commands.Add(new Entry
            {
                Id = "view.about",
                Name = "View: About",
                Category = "View",
                Execute = _ => SlopOptions.OpenCategory(SlopOptions.AboutCategory),
            });
            _commands.Add(new Entry
            {
                Id = "view.shortcuts-settings",
                Name = "View: Keyboard Shortcuts",
                Category = "View",
                Execute = _ => SlopOptions.OpenCategory(SlopOptions.KeyboardCategory),
            });

            // Jukebox
            _commands.Add(new Entry
            {
                Id = "jukebox.mute",
                Name = "Jukebox: Mute",
                Category = "Jukebox",
                Execute = _ => Radio.ToggleMute(),
            });
            _commands.Add(new Entry
            {
                Id = "jukebox.random",
                Name = "Jukebox: Random",
                Category = "Jukebox",
                Execute = _ => Radio.PickRandom(),
            });
            _commands.Add(new Entry
            {
                Id = "jukebox.tune",
                Name = "Jukebox: Tune",
                Category = "Jukebox",
                SubAction = JukeboxSub,
                Execute = _ => { },
            });
            _commands.Add(new Entry
            {
                Id = "jukebox.like",
                Name = "Jukebox: Like",
                Category = "Jukebox",
                Execute = _ => Radio.Like(),
            });

            // Game
            if (Current.ProgramState == ProgramState.Playing)
            {
                _commands.Add(new Entry
                {
                    Id = "game.quickstart",
                    Name = "Game: Quick Start",
                    Category = "Game",
                    Execute = _ => QuickStart.Queue(),
                });
                _commands.Add(new Entry
                {
                    Id = "game.nextplanet",
                    Name = "Game: Next Planet",
                    Category = "Game",
                    Execute = _ => NextPlanet.Begin(),
                });
            }
        }

        // --------------------------------------------------------------- sub-option builders

        static List<SubOption> AgentsSub(params AgentState[] states)
        {
            var set = new HashSet<AgentState>(states);
            var list = SessionHub.Instance.Sessions
                .Where(s => set.Contains(s.State) && !s.Ephemeral)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.State.ToString().ToLower()})  -  {s.Project}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no agents in that state)", Enabled = false });
            return list;
        }

        static List<SubOption> AgentsSubAll()
        {
            var list = SessionHub.Instance.Sessions
                .Where(s => !s.Ephemeral)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.State.ToString().ToLower()})  -  {s.Project}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no agents)", Enabled = false });
            return list;
        }

        // Duplicate is meaningful for any session with a project, including a temporary
        // errand: the dialog copies that project's command and sandbox context, while a
        // project-less session has nowhere useful to start from.
        static List<SubOption> AgentsSubWithProject()
        {
            var list = SessionHub.Instance.Sessions
                .Where(s => !string.IsNullOrEmpty(s.Project))
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.State.ToString().ToLower()})  -  {s.Project}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no agents with a project)", Enabled = false });
            return list;
        }

        static List<SubOption> ProjectsSub()
        {
            var list = SessionHub.Instance.Projects
                .Select(p => new SubOption
                {
                    Label = $"{p.Name}  -  {p.Dir}",
                    Value = p.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no projects)", Enabled = false });
            return list;
        }

        // The sidebar's filter, one row a checkbox - the same ticks the strip's own menu
        // draws. The blank value is that menu's "all projects", which clears the filter
        // rather than ticking anything.
        static List<SubOption> FilterSub()
        {
            var list = new List<SubOption>
            {
                new SubOption
                {
                    Label = "All projects",
                    Value = "",
                    Checked = !AgentSidebar.Filtering,
                },
            };

            foreach (var p in SessionHub.Instance.Projects.OrderBy(p => p.Name,
                StringComparer.Ordinal))
                list.Add(new SubOption
                {
                    Label = $"{p.Name}  -  {p.Dir}",
                    Value = p.Name,
                    Checked = AgentSidebar.Ticked(p.Name),
                });

            list.Add(new SubOption
            {
                Label = $"{AgentSidebar.NoProject}  -  whatever belongs to no project",
                Value = AgentSidebar.NoProject,
                Checked = AgentSidebar.Ticked(AgentSidebar.NoProject),
            });
            return list;
        }

        static List<SubOption> ShortcutsSub()
        {
            var list = SessionHub.Instance.Shortcuts
                .Where(s => s.Kind != ShortcutKind.Breadcrumb && s.Kind != ShortcutKind.FileAction)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.Kind.ToString().ToLower()})",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no shortcuts)", Enabled = false });
            return list;
        }

        static List<SubOption> JukeboxSub()
        {
            var list = new List<SubOption>
            {
                new SubOption
                {
                    Label = Radio.Picked == null && !Radio.Muted ? "OST  (playing)" : "OST",
                    Select = Radio.PickOst,
                },
            };

            foreach (var station in Radio.Stations)
            {
                var s = station;
                list.Add(new SubOption
                {
                    Label = Radio.Picked == s && !Radio.Muted
                        ? $"{s.Name}  -  {Radio.RateLabel(s.Rate)}  (playing)"
                        : s.Name,
                    Children = () => JukeboxPresetsSub(s),
                });
            }
            return list;
        }

        static List<SubOption> JukeboxPresetsSub(Radio.Station station)
        {
            var list = new List<SubOption>();
            foreach (int preset in station.Rates)
            {
                var rate = preset;
                list.Add(new SubOption
                {
                    Label = Radio.RateLabel(rate)
                        + (Radio.Picked == station && !Radio.Muted && station.Rate == rate
                            ? "  (playing)" : ""),
                    Select = () => Radio.Pick(station, rate),
                });
            }
            return list;
        }

        // --------------------------------------------------------------- actions

        void ExecuteSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _matches.Count) return;
            var entry = _matches[_selectedIndex].E;
            if (entry.SubAction != null) EnterSub(entry);
            else Execute(entry);
        }

        void ExecuteSub()
        {
            if (_subIndex < 0 || _subIndex >= _subShown.Count) return;
            var opt = _subShown[_subIndex].O;
            if (!opt.Enabled) return;

            if (opt.Children != null)
            {
                EnterNestedSub(opt);
                return;
            }

            if (opt.Select != null) opt.Select();
            else _subCmd?.Execute(opt.Value);
            TrackRecent(_subCmd?.Id);
            Close();
        }

        // A sub list is a checklist when its options carry boxes. Then Space ticks and
        // stays, while Enter is the same tick and done.
        bool Checklist
        {
            get
            {
                foreach (var o in _subOptions)
                    if (o.Checked.HasValue) return true;
                return false;
            }
        }

        void ToggleSub()
        {
            if (_subIndex < 0 || _subIndex >= _subShown.Count) return;
            var opt = _subShown[_subIndex].O;
            if (!opt.Enabled || !opt.Checked.HasValue) return;

            _subCmd?.Execute(opt.Value);
            TrackRecent(_subCmd?.Id);

            // Asked for again rather than flipped in place: the boxes show the caller's
            // state, and one tick can move another - ticking a project clears "all".
            int was = _subIndex;
            if (_subCmd?.SubAction != null) _subOptions = _subCmd.SubAction();
            RebuildSub();
            _subIndex = Mathf.Clamp(was, 0, Mathf.Max(0, _subShown.Count - 1));
        }

        void Execute(Entry entry)
        {
            if (entry.SubAction != null) { EnterSub(entry); return; }
            entry.Execute(null);
            TrackRecent(entry.Id);
            Close();
        }

        void EnterSub(Entry entry)
        {
            _mode = Mode.Sub;
            _subCmd = entry;
            _subStack.Clear();
            _subOptions = entry.SubAction();
            _subIndex = 0;
            _subPrompt = entry.Name + ":";
            _subFilter = "";
            _subHasFilter = false;
            _input = "";
            _filter = "";
            _scroll.JumpTo(Vector2.zero);
            _focusInput = true;
            RebuildSub();
            Resize();
        }

        void EnterNestedSub(SubOption option)
        {
            var children = option.Children();
            if (children == null || children.Count == 0) return;

            _subStack.Add(new SubFrame
            {
                Options = _subOptions,
                Prompt = _subPrompt,
                Filter = _subFilter,
                HasFilter = _subHasFilter,
                Index = _subIndex,
            });
            _subOptions = children;
            _subPrompt = option.Label + ":";
            _subFilter = "";
            _subHasFilter = false;
            _subIndex = 0;
            _scroll.JumpTo(Vector2.zero);
            _focusInput = true;
            RebuildSub();
            Resize();
        }

        void BackSub()
        {
            if (_subStack.Count == 0)
            {
                BackToCommands();
                return;
            }

            var frame = _subStack[_subStack.Count - 1];
            _subStack.RemoveAt(_subStack.Count - 1);
            _subOptions = frame.Options;
            _subPrompt = frame.Prompt;
            _subFilter = frame.Filter;
            _subHasFilter = frame.HasFilter;
            _subIndex = frame.Index;
            _scroll.JumpTo(Vector2.zero);
            _focusInput = true;
            RebuildSub();
            Resize();
        }

        static void AskWhere(ShortcutInfo info)
        {
            var name = info.Name;
            var options = SessionHub.Instance.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () => RunShortcutWith(name, p.Name)))
                .ToList();
            options.Add(new FloatMenuOption(
                $"A temporary project under {ProjectInfo.TempRoot}",
                () => RunShortcutWith(name, null, true)));
            Find.WindowStack.Add(new SlopMenu(options));
        }

        static void RunShortcutWith(string name, string project = null, bool temp = false)
        {
            SessionHub.Instance.RunShortcut(name,
                session => { TerminalWindow.Open(session); }, SlopWidgets.Fail, project, temp,
                Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch));
        }

        // --------------------------------------------------------------- filtering

        void RebuildMatches()
        {
            _matches.Clear();
            _recentInList = 0;

            if (string.IsNullOrEmpty(_filter))
            {
                foreach (var id in _recent)
                {
                    var entry = _commands.FirstOrDefault(e => e.Id == id);
                    if (entry != null && !Listed(entry))
                    {
                        _matches.Add(new Hit { E = entry, Label = entry.Name });
                        _recentInList++;
                    }
                }
                foreach (var e in _commands)
                    if (!Listed(e)) _matches.Add(new Hit { E = e, Label = e.Name });
                return;
            }

            var scored = new List<Hit>();
            foreach (var e in _commands)
            {
                int score;
                List<int> hits;

                if (Fuzzy.Match(e.Name, _filter, out score, out hits))
                {
                    scored.Add(new Hit
                    {
                        E = e,
                        Label = Fuzzy.Highlight(e.Name, hits),
                        Score = score + RecentBonus(e.Id),
                    });
                }
                else if (Fuzzy.Match(e.Id, _filter, out score))
                {
                    // The id is the command's other name - "view.config" for anyone who
                    // types the dotted form - and none of it is on screen to mark up, so
                    // the row draws plain and ranks below anything the name itself found.
                    scored.Add(new Hit
                    {
                        E = e,
                        Label = e.Name,
                        Score = score - IdCost + RecentBonus(e.Id),
                    });
                }
            }

            // Stable, so commands scoring the same keep the catalogue's order.
            _matches.AddRange(scored.OrderByDescending(h => h.Score));
        }

        bool Listed(Entry e) => _matches.Any(h => h.E == e);

        // What a command was used recently is worth: enough to break a tie between two
        // equally good matches, never enough to outrank a better one.
        static int RecentBonus(string id)
        {
            int i = _recent.IndexOf(id);
            return i < 0 ? 0 : (RecentMax - i) * 4;
        }

        void RebuildSub()
        {
            _subShown.Clear();

            if (string.IsNullOrEmpty(_subFilter))
            {
                foreach (var o in _subOptions)
                    _subShown.Add(new SubHit { O = o, Label = o.Label });
            }
            else
            {
                var f = _subFilter.ToLowerInvariant();
                var scored = new List<SubHit>();
                foreach (var o in _subOptions)
                {
                    // The placeholder row is not an option, so it is not searched either.
                    if (!o.Enabled) continue;

                    int score;
                    List<int> hits;
                    if (Fuzzy.Match(o.Label, f, out score, out hits))
                        scored.Add(new SubHit
                        {
                            O = o,
                            Label = Fuzzy.Highlight(o.Label, hits),
                            Score = score,
                        });
                }
                _subShown.AddRange(scored.OrderByDescending(h => h.Score));
            }

            _subIndex = Mathf.Clamp(_subIndex, 0, Mathf.Max(0, _subShown.Count - 1));
        }

        // --------------------------------------------------------------- recent tracking

        static void TrackRecent(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _recent.Remove(id);
            _recent.Insert(0, id);
            if (_recent.Count > RecentMax) _recent.RemoveAt(_recent.Count - 1);
        }

        // --------------------------------------------------------------- height

        float ContentHeight()
        {
            float body;

            if (_mode == Mode.Sub)
            {
                body = Mathf.Max(1, Mathf.Min(_subShown.Count, 10)) * RowH;
            }
            else if (_matches.Count == 0)
            {
                body = RowH;
            }
            else
            {
                // Walked the way the list is drawn, so the headings are counted.
                body = 0f;
                string prev = null;
                for (int i = 0; i < _matches.Count && body < MaxH; i++)
                {
                    if (Grouped)
                    {
                        string group = GroupOf(i);
                        if (group != prev) { body += GroupH; prev = group; }
                    }
                    body += RowH;
                }
            }

            return Mathf.Min(Pad + InputH + SlopWidgets.GapXS + body + Pad, MaxH);
        }

    }
}
