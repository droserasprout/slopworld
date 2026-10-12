using System.Linq;
using UnityEngine;

namespace SlopWorld
{
    public partial class CommandPalette
    {
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

        // --------------------------------------------------------------- filtering

        void RebuildMatches()
        {
            var entries = CommandTable.Select((command, index) => new PaletteSearch.Entry(
                index, command.Label, command.IsEnabled, command.Id, command.Group)).ToList();
            var result = PaletteSearch.Commands(entries, _filter, History.Entries);
            _matches.Clear();
            foreach (var hit in result.Hits)
                _matches.Add(new Hit { Command = CommandTable[hit.Index], Label = hit.Label });
            _recentInList = result.RecentCount;
        }

        void RebuildSub()
        {
            var entries = _subOptions.Select((option, index) => new PaletteSearch.Entry(
                index, option.Label, option.Enabled)).ToList();
            _subShown.Clear();
            foreach (var hit in PaletteSearch.Options(entries, _subFilter))
                _subShown.Add(new SubHit { O = _subOptions[hit.Index], Label = hit.Label });
            _subIndex = Mathf.Clamp(_subIndex, 0, Mathf.Max(0, _subShown.Count - 1));
        }

        // --------------------------------------------------------------- recent tracking

        static void LoadRecent()
        {
            if (_recentLoaded) return;
            _recentLoaded = true;
            History.Load(Settings.CommandPaletteHistory, CommandTable.Select(command => command.Id));
        }

        static void TrackRecent(string id)
        {
            if (!History.Track(id)) return;
            Settings.S.commandPaletteHistory = History.Text;
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
                // Count group headings in the same order as the rendered list.
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

            return Mathf.Min(Pad + InputH + UiTheme.GapXS + body + Pad, MaxH);
        }

    }
}
