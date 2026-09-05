using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // F1 opens a filtered command palette; all window and button actions, including nested selections, are registered here.
    public partial class CommandPalette : Window
    {
        const float Width = 520f;
        const float MaxH = 440f;
        const float Pad = UiWidgets.GapS;

        // The three heights this list is built from, off the font rather than written down:
        // an input is a field, a row is a line with room round it, and a group heading is a
        // tiny line. A figure here holds only for the face it was set against, and a row
        // shorter than its line loses the top and bottom of every label in the palette.
        static float InputH => UiWidgets.FieldH;
        static float RowH => UiWidgets.PaletteRowH;
        static float GroupH => UiWidgets.TinyRowH;
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
        readonly FieldLifetime _fieldLifetime = new FieldLifetime();
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
            closeOnClickedOutside = true;
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
            SessionHub.Instance.RefreshLibrary();
            SessionHub.Instance.LoadPresets();

            Find.WindowStack.Add(new CommandPalette());
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

            public static CommandDef ForLibraryItem(string id, string label,
                Func<List<SubOption>> filter, Action<LibraryItemInfo> action, Func<bool> enabled = null) =>
                For(id, label, "Library", filter, v => SessionHub.Instance.LibraryItem(v), action, enabled);

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


        static bool Playing() => Current.ProgramState == ProgramState.Playing;

        static bool HasTaskRecipients() => SessionHub.Instance.Sessions.Any(s =>
            s != null && !s.Ephemeral && !s.Host && !string.IsNullOrEmpty(s.Name));

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
                .Where(s => !s.Ephemeral && !s.Host)
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

        static List<SubOption> AgentsSubEditable()
        {
            var list = SessionHub.Instance.Sessions
                .Where(s => !s.Ephemeral && !s.Host && !s.Worker)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.State.ToString().ToLower()})  -  {s.Project}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no editable agents)", Enabled = false });
            return list;
        }

        static List<SubOption> AgentsSubWithPawn()
        {
            var colony = AgentColony.Current;
            var list = SessionHub.Instance.Sessions
                .Where(s => !s.Ephemeral && !s.Host && colony?.PawnOf(s.Name) != null)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.State.ToString().ToLower()})  -  {s.Project}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no agents with a colonist)", Enabled = false });
            return list;
        }

        // Duplicate is meaningful for any session with a project, including a temporary
        // errand: the dialog copies that project's command and sandbox context, while a
        // project-less session has nowhere useful to start from.
        static List<SubOption> AgentsSubWithProject()
        {
            var list = SessionHub.Instance.Sessions
                .Where(s => !s.Host && !s.Worker && !string.IsNullOrEmpty(s.Project))
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

        static List<SubOption> DeletableProjectsSub()
        {
            var list = SessionHub.Instance.Projects
                .Where(p => !SessionHub.Instance.Sessions.Any(s => s.Project == p.Name))
                .Select(p => new SubOption
                {
                    Label = $"{p.Name}  -  {p.Dir}",
                    Value = p.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no projects without sessions)", Enabled = false });
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

        static List<SubOption> LibraryItemsSub()
        {
            var list = SessionHub.Instance.Library
                .Where(s => s.Kind != LibraryItemKind.Breadcrumb && s.Kind != LibraryItemKind.FileAction)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.Kind.ToString().ToLower()})",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no library entries)", Enabled = false });
            return list;
        }

        static List<SubOption> LibraryManageSub()
        {
            var list = SessionHub.Instance.Library
                .Where(s => !s.Builtin)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  ({s.Kind.ToString().ToLower()})",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption { Label = "(no editable library entries)", Enabled = false });
            return list;
        }

        static List<SubOption> NewLibraryItemSub() => new List<SubOption>
        {
            new SubOption { Label = "Prompt", Select = () => NewLibraryItem(LibraryItemKind.Prompt) },
            new SubOption { Label = "Breadcrumb", Select = () => NewLibraryItem(LibraryItemKind.Breadcrumb) },
            new SubOption { Label = "Shell", Select = () => NewLibraryItem(LibraryItemKind.Shell) },
            new SubOption { Label = "File Action", Select = () => NewLibraryItem(LibraryItemKind.FileAction) },
        };

        static void NewLibraryItem(LibraryItemKind kind) =>
            TerminalWindow.OpenOverPane(new EditLibraryItemDialog(kind));

        static List<SubOption> HostShellSub()
        {
            var list = new List<SubOption>
            {
                new SubOption
                {
                    Label = "~",
                    Value = "",
                    Select = () => SessionHub.Instance.RunHostShell("",
                        session => TerminalWindow.Open(session), UiWidgets.Fail),
                },
            };

            foreach (var p in SessionHub.Instance.Projects)
            {
                string name = p.Name;
                list.Add(new SubOption
                {
                    Label = $"{name}  -  {p.Dir}",
                    Value = name,
                    Select = () => SessionHub.Instance.RunHostShell(name,
                        session => TerminalWindow.Open(session), UiWidgets.Fail),
                });
            }
            return list;
        }

        static List<SubOption> HostSessionsSub()
        {
            var list = SessionHub.Instance.Sessions
                .Where(s => s.Host)
                .Select(s => new SubOption
                {
                    Label = $"{s.Name}  -  {s.Dir}",
                    Value = s.Name,
                })
                .ToList();

            if (list.Count == 0)
                list.Add(new SubOption
                {
                    Label = "(no " + SessionHub.Instance.Capabilities.TerminalNames + ")",
                    Enabled = false,
                });
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

        static void AskWhere(LibraryItemInfo info)
        {
            var name = info.Name;
            var options = SessionHub.Instance.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () => RunLibraryItemWith(name, p.Name)))
                .ToList();
            options.Add(new FloatMenuOption(
                $"A temporary project under {ProjectInfo.TempRoot}",
                () => RunLibraryItemWith(name, null, true)));
            Find.WindowStack.Add(new UiMenu(options));
        }

        static void RunLibraryItemWith(string name, string project = null, bool temp = false)
        {
            SessionHub.Instance.RunLibraryItem(name,
                session => { TerminalWindow.Open(session); }, UiWidgets.Fail, project, temp,
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

            return Mathf.Min(Pad + InputH + UiWidgets.GapXS + body + Pad, MaxH);
        }

    }
}
