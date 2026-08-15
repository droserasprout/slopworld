using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // F1 opens a filtered command palette; all window and button actions, including nested selections, are registered here.
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
        CommandDef _subCmd;
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

        static readonly List<string> _recent = new List<string>();
        static bool _recentLoaded;

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
            // PageUp/PageDown are vanilla map-zoom bindings. The palette owns those keys
            // while it is open, so stop the camera pass from consuming them first.
            preventCameraMotion = true;
            draggable = false;
            resizeable = false;

            LoadRecent();
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

            // IMGUI emits Space as key and character events; consume both in checklist mode, but toggle only on the key event so Space is not typed into the filter.
            if (isKeyDown && _mode == Mode.Sub && Checklist &&
                (e.keyCode == KeyCode.Space || e.character == ' '))
            {
                if (e.keyCode == KeyCode.Space) ToggleSub();
                e.Use();
                return;
            }

            // Handle navigation keys before the text field, which would otherwise consume
            // arrows, Escape and Enter for its own cursor motion and focus management.
            if (isKeyDown && HandleNavigation(e)) return;

            if (_mode == Mode.Sub)
            {
                if (DrawSubInput(inner, e, isKeyDown)) return;
            }
            else
            {
                DrawCommandInput(inner);
            }

            if (_focusInput)
            {
                GUI.FocusControl("paletteInput");
                _focusInput = false;
            }
        }

        bool HandleNavigation(Event e)
        {
            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    if (_mode == Mode.Sub) BackSub();
                    else Close();
                    e.Use();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_mode == Mode.Sub) ExecuteSub();
                    else ExecuteSelected();
                    e.Use();
                    return true;

                case KeyCode.UpArrow:
                    if (_mode == Mode.Sub)
                    {
                        _subIndex = Mathf.Max(0, _subIndex - 1);
                        ScrollToSub();
                    }
                    else if (_matches.Count > 0)
                    {
                        _selectedIndex = _selectedIndex == 0
                            ? _matches.Count - 1
                            : _selectedIndex - 1;
                        ScrollToSelected();
                    }
                    e.Use();
                    return true;

                case KeyCode.DownArrow:
                    if (_mode == Mode.Sub)
                    {
                        _subIndex = Mathf.Min(_subShown.Count - 1, _subIndex + 1);
                        ScrollToSub();
                    }
                    else if (_matches.Count > 0)
                    {
                        _selectedIndex = _selectedIndex == _matches.Count - 1
                            ? 0
                            : _selectedIndex + 1;
                        ScrollToSelected();
                    }
                    e.Use();
                    return true;

                case KeyCode.PageUp:
                    if (_mode == Mode.Sub)
                    {
                        _subIndex = Mathf.Max(0, _subIndex - PageSize);
                        ScrollToSub();
                    }
                    else
                    {
                        _selectedIndex = Mathf.Max(0, _selectedIndex - PageSize);
                        ScrollToSelected();
                    }
                    e.Use();
                    return true;

                case KeyCode.PageDown:
                    if (_mode == Mode.Sub)
                    {
                        _subIndex = Mathf.Min(_subShown.Count - 1, _subIndex + PageSize);
                        ScrollToSub();
                    }
                    else
                    {
                        _selectedIndex = Mathf.Min(_matches.Count - 1, _selectedIndex + PageSize);
                        ScrollToSelected();
                    }
                    e.Use();
                    return true;
            }
            return false;
        }

        bool DrawSubInput(Rect inner, Event e, bool isKeyDown)
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
                return true;
            }
            return false;
        }

        float DrawCommandInput(Rect inner)
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
            return inner.height;
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

        // Page navigation follows the number of complete rows visible in the list. A page
        // jump is still at least one row when the palette is shorter than a row.
        int PageSize => Mathf.Max(1, Mathf.FloorToInt(_listH / RowH));

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
                    if (_matches[i].Command.SubAction != null) EnterSub(_matches[i].Command);
                    else Execute(_matches[i].Command);
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
            index < _recentInList ? "Recently" : _matches[index].Command.Group;

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

        // --------------------------------------------------------------- command model

        sealed class CommandDef
        {
            public readonly string Id;
            public readonly string Label;
            public readonly string Group;
            public readonly Func<bool> Enabled;
            public readonly Func<List<SubOption>> SubAction;
            public readonly Action<string> Action;

            public CommandDef(string id, string label, string group, Action<string> action,
                Func<bool> enabled = null, Func<List<SubOption>> subAction = null)
            {
                Id = id;
                Label = label;
                Group = group;
                Enabled = enabled ?? (() => true);
                SubAction = subAction;
                Action = action;
            }

            public bool IsEnabled => Enabled();

            public static CommandDef ForAgent(string id, string label,
                Func<List<SubOption>> filter, Action<SessionInfo> action, Func<bool> enabled = null) =>
                For(id, label, "Agent", filter, v => SessionHub.Instance.Get(v), action, enabled);

            public static CommandDef ForProject(string id, string label,
                Func<List<SubOption>> filter, Action<ProjectInfo> action, Func<bool> enabled = null) =>
                For(id, label, "Project", filter, v => SessionHub.Instance.Project(v), action, enabled);

            public static CommandDef ForShortcut(string id, string label,
                Func<List<SubOption>> filter, Action<ShortcutInfo> action, Func<bool> enabled = null) =>
                For(id, label, "Shortcut", filter, v => SessionHub.Instance.Shortcut(v), action, enabled);

            static CommandDef For<T>(string id, string label, string group,
                Func<List<SubOption>> filter, Func<string, T> resolve, Action<T> action,
                Func<bool> enabled)
                where T : class
            {
                return new CommandDef(id, label, group, value =>
                {
                    if (value == null) return;
                    var item = resolve(value);
                    if (item != null) action(item);
                }, enabled, filter);
            }
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
            public CommandDef Command;
            public string Label;
            public int Score;
        }

        class SubHit
        {
            public SubOption O;
            public string Label;
            public int Score;
        }

        // --------------------------------------------------------------- command table

        // The palette is a catalogue of data. Availability is evaluated while the list is
        // rebuilt, so game-state changes do not require rebuilding the definitions.
        static readonly List<CommandDef> CommandTable = new List<CommandDef>
        {
            CommandDef.ForAgent("agent.start", "Agent: Start",
                () => AgentsSub(AgentState.Down),
                s => SessionHub.Instance.Start(s.Name, SlopWidgets.Fail)),
            CommandDef.ForAgent("agent.stop", "Agent: Stop",
                () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                s => SessionHub.Instance.Stop(s.Name, SlopWidgets.Fail)),
            CommandDef.ForAgent("agent.restart", "Agent: Restart",
                () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                s => SessionHub.Instance.Restart(s.Name, SlopWidgets.Fail)),
            CommandDef.ForAgent("agent.edit", "Agent: Edit", AgentsSubAll,
                s => Find.WindowStack.Add(new EditSessionDialog(s))),
            CommandDef.ForAgent("agent.terminal", "Agent: Open Terminal",
                () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                s => { if (!s.Gone) TerminalWindow.Open(s.Name); }),
            CommandDef.ForAgent("agent.delete", "Agent: Delete", AgentsSubAll, s =>
            {
                var name = s.Name;
                Find.WindowStack.Add(SlopConfirmDialog.Create(
                    $"Remove session '{name}'? This kills it, drops it from config.toml, and moves " +
                    "its private state to recoverable trash for 14 days.",
                    () => SessionHub.Instance.Remove(name, SlopWidgets.Fail), destructive: true));
            }),
            CommandDef.ForAgent("agent.duplicate", "Agent: Duplicate", AgentsSubWithProject,
                s =>
                {
                    if (!string.IsNullOrEmpty(s.Project))
                        TerminalWindow.OpenOverPane(EditSessionDialog.Copy(s));
                }),

            new CommandDef("project.new", "Project: New", "Project",
                _ => Find.WindowStack.Add(new EditProjectDialog(null))),
            CommandDef.ForProject("project.edit", "Project: Edit", ProjectsSub,
                p => Find.WindowStack.Add(new EditProjectDialog(p))),
            CommandDef.ForProject("project.delete", "Project: Delete", ProjectsSub, p =>
            {
                var name = p.Name;
                Find.WindowStack.Add(SlopConfirmDialog.Create(
                    $"Remove project '{name}'? The directory is left alone; only the entry in config.toml goes.",
                    () => SessionHub.Instance.RemoveProject(name, SlopWidgets.Fail), destructive: true));
            }),
            CommandDef.ForProject("project.duplicate", "Project: Duplicate", ProjectsSub,
                p => TerminalWindow.OpenOverPane(EditProjectDialog.Copy(p))),
            CommandDef.ForProject("project.host-terminal", "Project: Open Host Terminal",
                ProjectsSub,
                p => SessionHub.Instance.RunHostShell(p.Name,
                    session => TerminalWindow.Open(session), SlopWidgets.Fail)),

            CommandDef.ForShortcut("shortcut.run", "Shortcut: Run", ShortcutsSub, s =>
            {
                if (s.Kind == ShortcutKind.Breadcrumb || s.Kind == ShortcutKind.FileAction) return;
                if (s.Link == ShortcutLink.Ask) AskWhere(s);
                else RunShortcutWith(s.Name);
            }),
            new CommandDef("shortcut.new", "Shortcut: New", "Shortcut",
                _ => Find.WindowStack.Add(new EditShortcutDialog(null))),
            CommandDef.ForShortcut("shortcut.edit", "Shortcut: Edit", ShortcutsSub,
                s => Find.WindowStack.Add(new EditShortcutDialog(s))),
            CommandDef.ForShortcut("shortcut.delete", "Shortcut: Delete", ShortcutsSub, s =>
            {
                var name = s.Name;
                Find.WindowStack.Add(SlopConfirmDialog.Create(
                    $"Remove shortcut '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.RemoveShortcut(name, SlopWidgets.Fail), destructive: true));
            }),

            new CommandDef("daemon.reconnect", "Daemon: Reconnect", "Daemon",
                _ => SessionHub.Instance.Connect()),
            new CommandDef("agents.refresh", "Agents: Refresh", "Refresh",
                _ => SessionHub.Instance.Refresh()),
            new CommandDef("projects.refresh", "Projects: Refresh", "Refresh",
                _ => SessionHub.Instance.RefreshProjects(SlopWidgets.Fail)),
            new CommandDef("shortcuts.refresh", "Shortcuts: Refresh", "Refresh",
                _ => SessionHub.Instance.RefreshShortcuts(SlopWidgets.Fail)),
            new CommandDef("files.reload", "Files: Reload", "Refresh",
                _ => FilesView.Reload()),
            new CommandDef("search.open", "Search: Find in Files", "View",
                _ => AgentSidebar.ShowSearch()),
            new CommandDef("git.refresh", "Git: Refresh", "Refresh",
                _ => GitView.Refresh()),

            new CommandDef("view.config", "Settings: General", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.Category)),
            new CommandDef("view.storage", "Settings: Storage", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.StorageCategory)),
            new CommandDef("view.commands", "Settings: Commands", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CommandsCategory)),
            new CommandDef("view.command-presets", "Settings: Commands - Presets", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CommandPresetsCategory)),
            new CommandDef("view.terminal-settings", "Settings: Terminal", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.TerminalCategory)),
            new CommandDef("view.appearance", "Settings: Appearance", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.AppearanceCategory)),
            new CommandDef("view.audio", "Settings: Audio", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.AudioCategory)),
            new CommandDef("view.integrations", "Settings: Integrations", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.IntegrationsCategory)),
            new CommandDef("view.usage", "Settings: Integrations - Usage", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.UsageCategory)),
            new CommandDef("view.summaries", "Settings: Integrations - Summaries", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.SummariesCategory)),
            new CommandDef("view.sandbox", "Settings: Sandbox", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.SandboxCategory)),
            new CommandDef("view.shortcuts-settings", "Settings: Keyboard", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.KeyboardCategory)),
            new CommandDef("settings.graphics", "Settings: RimWorld - Graphics", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.GraphicsCategory)),
            new CommandDef("settings.interface", "Settings: RimWorld - Interface", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.InterfaceCategory)),
            new CommandDef("settings.controls", "Settings: RimWorld - Controls", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.ControlsCategory)),
            new CommandDef("view.about", "Settings: About", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.AboutCategory)),
            new CommandDef("config.toml", "Configuration: Edit config.toml", "Configuration",
                _ => ConfigWindow.Open()),
            new CommandDef("view.filter", "View: Filter Projects", "View",
                v => { if (v != null) AgentSidebar.ToggleFilter(v); }, subAction: () => FilterSub()),
            new CommandDef("view.zoom-in", "View: Zoom In", "View",
                _ => SlopUIScale.Zoom(1)),
            new CommandDef("view.zoom-out", "View: Zoom Out", "View",
                _ => SlopUIScale.Zoom(-1)),
            new CommandDef("window.fullscreen", "Window: Toggle Fullscreen", "View",
                _ => WindowMaximizer.Toggle()),

            new CommandDef("jukebox.mute", "Jukebox: Mute", "Jukebox",
                _ => Radio.ToggleMute()),
            new CommandDef("jukebox.random", "Jukebox: Random", "Jukebox",
                _ => Radio.PickRandom()),
            new CommandDef("jukebox.tune", "Jukebox: Tune", "Jukebox",
                _ => { }, subAction: JukeboxSub),
            new CommandDef("jukebox.like", "Jukebox: Like", "Jukebox",
                _ => Radio.Like()),
            new CommandDef("jukebox.history", "Jukebox: History", "Jukebox",
                _ => StoragePage.EditLikes()),

            new CommandDef("game.new-looks", "Game: New looks", "Game",
                _ => CoreTip.NewLooks(), enabled: Playing),
            new CommandDef("game.kill-something", "Game: Kill something", "Game",
                _ => CoreTip.KillSomething(), enabled: () => Playing() && !Settings.GrandmaMode),
            new CommandDef("game.hint", "Game: Hint", "Game",
                _ => CoreTip.ShowHint(), enabled: Playing),
            new CommandDef("game.nextplanet", "Game: Next Planet", "Game",
                _ => NextPlanet.Begin(), enabled: Playing),
        };

        static bool Playing() => Current.ProgramState == ProgramState.Playing;

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
            var entry = _matches[_selectedIndex].Command;
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
            else _subCmd?.Action(opt.Value);
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

            _subCmd?.Action(opt.Value);
            TrackRecent(_subCmd?.Id);

            // Asked for again rather than flipped in place: the boxes show the caller's
            // state, and one tick can move another - ticking a project clears "all".
            int was = _subIndex;
            if (_subCmd?.SubAction != null) _subOptions = _subCmd.SubAction();
            RebuildSub();
            _subIndex = Mathf.Clamp(was, 0, Mathf.Max(0, _subShown.Count - 1));
        }

        void Execute(CommandDef entry)
        {
            if (entry.SubAction != null) { EnterSub(entry); return; }
            if (!entry.IsEnabled) return;
            entry.Action(null);
            TrackRecent(entry.Id);
            Close();
        }

        void EnterSub(CommandDef entry)
        {
            if (!entry.IsEnabled) return;
            _mode = Mode.Sub;
            _subCmd = entry;
            _subStack.Clear();
            _subOptions = entry.SubAction();
            _subIndex = 0;
            _subPrompt = entry.Label + ":";
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
                    var entry = CommandTable.FirstOrDefault(e => e.Id == id && e.IsEnabled);
                    if (entry != null && !Listed(entry))
                    {
                        _matches.Add(new Hit { Command = entry, Label = entry.Label });
                        _recentInList++;
                    }
                }
                // The catalogue is authored in feature order, not category order. Group the
                // rows after recent entries so a category such as View does not split around
                // the settings entries that happen to be registered beside it.
                foreach (var category in CommandTable.Where(e => e.IsEnabled).GroupBy(e => e.Group))
                    foreach (var e in category)
                        if (!Listed(e)) _matches.Add(new Hit { Command = e, Label = e.Label });
                return;
            }

            var scored = new List<Hit>();
            foreach (var e in CommandTable)
            {
                if (!e.IsEnabled) continue;
                int score;
                List<int> hits;

                if (Fuzzy.Match(e.Label, _filter, out score, out hits))
                {
                    scored.Add(new Hit
                    {
                        Command = e,
                        Label = Fuzzy.Highlight(e.Label, hits),
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
                        Command = e,
                        Label = e.Label,
                        Score = score - IdCost + RecentBonus(e.Id),
                    });
                }
            }

            // Stable, so commands scoring the same keep the catalogue's order.
            _matches.AddRange(scored.OrderByDescending(h => h.Score));
        }

        bool Listed(CommandDef e) => _matches.Any(h => h.Command == e);

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

        static void LoadRecent()
        {
            if (_recentLoaded) return;
            _recentLoaded = true;
            _recent.Clear();

            foreach (var id in (Settings.CommandPaletteHistory ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (_recent.Count >= RecentMax) break;
                if (_recent.Contains(id)) continue;
                if (CommandTable.Any(e => e.Id == id)) _recent.Add(id);
            }
        }

        static void TrackRecent(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_recent.Count > 0 && _recent[0] == id) return;
            _recent.Remove(id);
            _recent.Insert(0, id);
            if (_recent.Count > RecentMax) _recent.RemoveAt(_recent.Count - 1);

            Settings.S.commandPaletteHistory = string.Join("\n", _recent.ToArray());
            Settings.S.Write();
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
