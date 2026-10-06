using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

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
                        Score = score,
                    });
                }
                else if (Fuzzy.Match(e.Id, _filter, out score))
                {
            // The ID is not visible, so display the label without highlighting it.
            // Rank this result below commands whose labels match the filter.
                    scored.Add(new Hit
                    {
                        Command = e,
                        Label = e.Label,
                        Score = score - IdCost,
                    });
                }
            }

            // Recency breaks equal-score ties; otherwise preserve catalog order.
            _matches.AddRange(scored.OrderByDescending(h => h.Score)
                .ThenByDescending(h => RecentBonus(h.Command.Id)));
        }

        bool Listed(CommandDef e) => _matches.Any(h => h.Command == e);

        // Used only after the fuzzy score, so recency cannot outrank a better match.
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
