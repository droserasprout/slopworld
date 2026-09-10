using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Preset and command editors used by the sandbox settings page.
    public partial class SandboxPage
    {
        void DrawPresetList(Rect r)
        {
            var all = SessionHub.Instance.Presets;
            var system = all.Where(p => p.Source == "system")
                .OrderBy(p => p.Name == "global" ? 0 : 1)
                .ThenBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
            var user = all.Where(p => p.Source != "system")
                .OrderBy(p => p.Name == "global" ? 0 : 1)
                .ThenBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
            float h = (system.Count + user.Count + 2 +
                (system.Count == 0 ? 1 : 0) + (user.Count == 0 ? 1 : 0)) * UiTheme.RowH
                + UiTheme.GapS * 2f;
            var view = new Rect(0f, 0f, Mathf.Max(0f, r.width - UiTheme.ScrollbarW),
                Mathf.Max(h, r.height));
            using (_listScroll.Scope(r, view))
            {
                float y = 0f;
                y = DrawLibraryGroup(view, y, "User", user,
                    p => p.Name + (p.Source == "override" ? "  (override)" : ""),
                    p => { _preset = p; _newEntry = false; });
                DrawLibraryGroup(view, y, "System", system, p => p.Name,
                    p => { _preset = p; _newEntry = false; });
            }
        }

        float DrawLibraryGroup<T>(Rect view, float y, string heading, List<T> items,
                                  Func<T, string> label, Action<T> pick)
        {
            UiLayout.SectionHeading(new Rect(0f, y, view.width, UiTheme.RowH), heading);
            y += UiTheme.RowH;
            foreach (var item in items)
            {
                bool child = item is PresetInfo preset && IsOptionalChild(preset);
                float inset = child ? UiTheme.GapM : UiTheme.GapS;
                var cell = new Rect(inset, y, view.width - inset, UiTheme.RowH);
                string name = label(item);
                bool selected = (item is PresetInfo p && p == _preset) ||
                                (item is CommandInfo c && c == _command);
                RowChrome.Hover(cell, selected, true, RowHoverPolicy.OverlayAware);
                // A preset that hands the sandbox a road back out is dangerous even when it
                // is selected: the yellow stays on the name so the warning is visible in the
                // library, not only after opening its editor.
                bool dangerous = item is PresetInfo dangerousPreset && dangerousPreset.IsEscape;
                bool global = item is PresetInfo globalPreset && globalPreset.Name == "global";
                GUI.color = dangerous ? UiTheme.Warn
                    : global ? UiTheme.Global
                    : selected ? UiTheme.Lead : UiTheme.Name;
                UiText.RowLabel(cell, name);
                GUI.color = Color.white;
                string description = item is PresetInfo info ? info.Description
                    : item is CommandInfo command ? command.Description : "";
                if (!string.IsNullOrEmpty(description)) TooltipHandler.TipRegion(cell, description);
                if (UiButtons.RowButton(cell)) pick(item);
                y += UiTheme.RowH;
            }
            if (items.Count == 0)
            {
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(new Rect(UiTheme.GapS, y, view.width, UiTheme.RowH), "(none)");
                GUI.color = Color.white;
                y += UiTheme.RowH;
            }
            return y + UiTheme.GapS;
        }

        // The optional half of one integration follows its read-only essential by name and
        // requires it. It is indented rather than separated, so `python` and
        // `python-cache` read as one small tree.
        static bool IsOptionalChild(PresetInfo p) =>
            p.Name.EndsWith("-cache", StringComparison.OrdinalIgnoreCase) &&
            p.Requires.Any(required => required == p.Name.Substring(0, p.Name.Length - "-cache".Length));

        void DrawCommandList(Rect r)
        {
            var all = SessionHub.Instance.Commands;
            var system = all.Where(c => c.Source == "system").ToList();
            var user = all.Where(c => c.Source != "system").ToList();
            float h = (system.Count + user.Count + 3) * UiTheme.RowH;
            var view = new Rect(0f, 0f, Mathf.Max(0f, r.width - UiTheme.ScrollbarW),
                Mathf.Max(h, r.height));
            view.height = Mathf.Max(r.height, h + UiTheme.GapS + UiTheme.BtnH);
            using (_listScroll.Scope(r, view))
            {
                float y = 0f;
                y = DrawLibraryGroup(view, y, "System", system, c => c.Name,
                    c => { _command = c; _newEntry = false; });
                y = DrawLibraryGroup(view, y, "User", user,
                    c => c.Name + (c.Source == "override" ? "  (override)" : ""),
                    c => { _command = c; _newEntry = false; });
                if (UiButtons.Button(new Rect(0f, y + UiTheme.GapS, view.width, UiTheme.BtnH),
                        "+ New command", UiTheme.Btn.Ghost))
                {
                    NewCommand();
                }
            }
        }

        void DrawPresetEditor(Rect r)
        {
            if (_preset == null)
            {
                EmptyEditor(r, "Select a preset to inspect or edit it.");
                return;
            }
            var p = _preset;
            bool editable = _newEntry || p.Source != "system";
            float width = Mathf.Max(0f, r.width - UiTheme.ScrollbarW);
            var layout = BuildPresetLayout(p, width, editable);
            var view = new Rect(0f, 0f, width, Mathf.Max(layout.ContentHeight, r.height));
            using (_editorScroll.Scope(r, view))
                DrawEditorLayout(view, layout);
        }

        SandboxEditorLayout BuildPresetLayout(PresetInfo p, float width, bool editable)
        {
            var rows = new List<SandboxEditorLayout.Row>();
            // Keep an escape warning above the identity so a long editor does not hide it.
            if (!string.IsNullOrEmpty(p.Escapes))
                AddWarning(rows, width, $"Escape path: {p.Escapes}.");

            AddTitleRows(rows, p.Name, p.Source, "sandbox");
            AddField(rows, width, "Name", "preset.name", p.Name, _newEntry,
                v => p.Name = v);
            AddArea(rows, width, "Description", "preset.description", p.Description,
                editable, 44f, v => p.Description = v);
            AddList(rows, width, "Requires", "preset.requires", p.Requires, editable);
            AddRule(rows, true);
            AddList(rows, width, "Read-only binds", "preset.ro", p.Ro, editable);
            AddList(rows, width, "Read-write binds", "preset.rw", p.Rw, editable);
            AddList(rows, width, "Device binds", "preset.dev", p.Dev, editable);
            AddRule(rows, false);
            AddList(rows, width, "Private paths", "preset.private", p.Private, editable);
            AddList(rows, width, "Seed paths", "preset.seed", p.Seed, editable);
            AddList(rows, width, "Skip paths", "preset.skip", p.Skip, editable);
            AddList(rows, width, "Shared files", "preset.shared", p.Shared, editable);
            AddRule(rows, false);
            AddList(rows, width, "Forwarded environment", "preset.env", p.Env, editable);
            string setenv = SetenvLines(p);
            AddArea(rows, width, "Set environment (KEY=VALUE)", "preset.setenv", setenv,
                editable, 48f, v => { _setenvText = v; ParseSetenv(p); });
            rows.Add(EditorButtonsRow(UiTheme.BtnH + UiTheme.GapM, editable, p.Source,
                "sandbox", p.Name,
                () => SessionHub.Instance.Catalog.SavePreset(p, () =>
                {
                    _newEntry = false; _error = null;
                }, msg => _error = msg),
                () => Remove("sandbox", p.Name)));
            return SandboxEditorLayout.Measure(width, rows);
        }

        SandboxEditorLayout BuildCommandLayout(CommandInfo c, float width, bool editable)
        {
            var rows = new List<SandboxEditorLayout.Row>();
            AddTitleRows(rows, c.Name, c.Source, "command");
            AddField(rows, width, "Name", "command.name", c.Name, _newEntry,
                v => c.Name = v);
            rows.Add(EditorRow(SandboxEditorRowKind.Field, FieldRowHeight(), true, g =>
                DrawCommandKind(ToRect(g), c, editable)));
            AddArea(rows, width, "Description", "command.description", c.Description,
                editable, 44f, v => c.Description = v);
            AddArea(rows, width, "Command line", "command.cmd", c.Cmd,
                editable, 52f, v => c.Cmd = v);

            rows.Add(EditorRow(SandboxEditorRowKind.Heading, UiTheme.GapS + UiTheme.RowH,
                true, g => UiLayout.SectionHeading(
                    new Rect(g.X, g.Y + UiTheme.GapS, g.Width, UiTheme.RowH),
                    "Sandbox dependencies")));
            rows.Add(EditorRow(SandboxEditorRowKind.Note, UiTheme.LineH + UiTheme.GapXS,
                true, g =>
                {
                    GUI.color = UiTheme.Dim;
                    UiText.RowLabel(new Rect(g.X, g.Y, g.Width, UiTheme.LineH),
                        "These presets are added whenever this command runs.");
                    GUI.color = Color.white;
                }));
            foreach (var p in SessionHub.Instance.Presets.Where(p => p.Name != "global"))
            {
                rows.Add(EditorRow(SandboxEditorRowKind.Checkbox, UiTheme.RowH, true, g =>
                {
                    bool on = c.Sandbox.Contains(p.Name);
                    bool was = on;
                    bool next = UiControls.Checkbox(ToRect(g), p.Name, on,
                        p.Description, !editable, p.IsEscape);
                    if (editable && next != was)
                    {
                        if (next) c.Sandbox.Add(p.Name); else c.Sandbox.Remove(p.Name);
                    }
                }));
            }
            rows.Add(EditorButtonsRow(UiTheme.GapS + UiTheme.BtnH + UiTheme.GapM,
                editable, c.Source, "command", c.Name,
                () => SessionHub.Instance.Catalog.SaveCommand(c, () =>
                {
                    _newEntry = false; _error = null;
                }, msg => _error = msg),
                () => Remove("command", c.Name), UiTheme.GapS));
            return SandboxEditorLayout.Measure(width, rows);
        }

        string _setenvText;
        PresetInfo _setenvOwner;
        void ParseSetenv(PresetInfo p)
        {
            p.Setenv.Clear();
            foreach (var line in DaemonConfig.Split(_setenvText))
            {
                int at = line.IndexOf('=');
                if (at > 0) p.Setenv[line.Substring(0, at).Trim()] = line.Substring(at + 1);
            }
        }

        string SetenvLines(PresetInfo p)
        {
            if (_setenvOwner != p)
            {
                _setenvOwner = p;
                _setenvText = string.Join("\n", p.Setenv.Select(x => x.Key + "=" + x.Value).ToArray());
            }
            return _setenvText ?? "";
        }

        void DrawCommandEditor(Rect r)
        {
            if (_command == null)
            {
                EmptyEditor(r, "Select a command to inspect or edit it.");
                return;
            }
            var c = _command;
            bool editable = _newEntry || c.Source != "system";
            float width = Mathf.Max(0f, r.width - UiTheme.ScrollbarW);
            var layout = BuildCommandLayout(c, width, editable);
            var view = new Rect(0f, 0f, width, Mathf.Max(layout.ContentHeight, r.height));
            using (_editorScroll.Scope(r, view))
                DrawEditorLayout(view, layout);
        }

        static void DrawCommandKind(Rect row, CommandInfo c, bool editable)
        {
            string label = c.Kind == CommandInfo.ShellKind ? "Shell" : "Agent";
            var options = new[]
            {
                new SelectorOption("Agent", () => c.Kind = CommandInfo.AgentKind, editable),
                new SelectorOption("Shell", () => c.Kind = CommandInfo.ShellKind, editable),
            };
            UiControls.Select(row, "Kind", label, options, out _, on: editable);
        }

        void AddTitleRows(List<SandboxEditorLayout.Row> rows, string name, string source,
                          string kind)
        {
            rows.Add(EditorRow(SandboxEditorRowKind.Title, UiTheme.RowH, true, g =>
            {
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(ToRect(g), name + (source == "override" ? "  (override)" : ""));
                GUI.color = Color.white;
            }));
            rows.Add(EditorRow(SandboxEditorRowKind.Status, UiTheme.LineH + UiTheme.GapS,
                true, g =>
                {
                    GUI.color = source == "system" ? UiTheme.Faint : UiTheme.Yes;
                    UiText.RowLabel(new Rect(g.X, g.Y, g.Width, UiTheme.LineH),
                        source == "system" ? "System preset (read-only)" : "User preset");
                    GUI.color = Color.white;
                }));
            if (source == "system")
            {
                rows.Add(EditorRow(SandboxEditorRowKind.Copy, UiTheme.BtnH + UiTheme.GapM,
                    true, g =>
                    {
                        if (UiButtons.Button(new Rect(g.X, g.Y, g.Width, UiTheme.BtnH),
                                "Copy to user", UiTheme.Btn.Primary))
                            Copy(kind, name);
                    }));
            }
        }

        void AddWarning(List<SandboxEditorLayout.Row> rows, float width, string warning)
        {
            float labelHeight = UiText.StatusLabelHeight(warning, width);
            rows.Add(EditorRow(SandboxEditorRowKind.Warning, labelHeight + UiTheme.GapM,
                true, g => UiText.StatusLabel(
                    new Rect(g.X, g.Y, g.Width, labelHeight), warning, UiTheme.Warn)));
        }

        void AddField(List<SandboxEditorLayout.Row> rows, float width, string label, string name,
                      string value, bool editable, Action<string> set)
        {
            bool visible = SandboxEditorLayout.OptionalVisible(editable, value);
            rows.Add(EditorRow(SandboxEditorRowKind.Field, FieldRowHeight(), visible, g =>
            {
                DrawCaption(g, label);
                set(UiText.Field(new Rect(g.X, g.Y + UiTheme.LineH + UiTheme.GapXS,
                    g.Width, UiTheme.FieldH), name, value, editable));
            }));
        }

        void AddArea(List<SandboxEditorLayout.Row> rows, float width, string label, string name,
                     string value, bool editable, float minimum, Action<string> set)
        {
            bool visible = SandboxEditorLayout.OptionalVisible(editable, value);
            if (!visible)
            {
                rows.Add(EditorRow(SandboxEditorRowKind.Area, 0f, false, null));
                return;
            }
            float actual = SandboxEditorLayout.WrappedAreaHeight(width, value, minimum,
                UiTheme.FieldPadX, UiTheme.FieldPadY,
                (text, available) => Text.CalcHeight(text, available));
            float rowHeight = UiTheme.LineH + UiTheme.GapXS + actual + UiTheme.GapS;
            rows.Add(EditorRow(SandboxEditorRowKind.Area, rowHeight, true, g =>
            {
                DrawCaption(g, label);
                set(UiText.Area(new Rect(g.X, g.Y + UiTheme.LineH + UiTheme.GapXS,
                    g.Width, actual), name, value, editable));
            }));
        }

        void AddList(List<SandboxEditorLayout.Row> rows, float width, string label, string name,
                     List<string> items, bool editable)
        {
            string text = DaemonConfig.Lines(items);
            AddArea(rows, width, label, name, text, editable, 48f, value =>
            {
                items.Clear();
                items.AddRange(DaemonConfig.Split(value));
            });
        }

        static void AddRule(List<SandboxEditorLayout.Row> rows, bool leadingGap)
        {
            float top = leadingGap ? UiTheme.GapXS : 0f;
            rows.Add(EditorRow(SandboxEditorRowKind.Rule, top + 1f + UiTheme.GapM, true,
                g => Slab.Hairline(new Rect(g.X, g.Y + top, g.Width, 1f), UiTheme.Edge)));
        }

        static void DrawCaption(UiLayoutRect row, string label)
        {
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(new Rect(row.X, row.Y, row.Width, UiTheme.LineH), label);
            GUI.color = Color.white;
        }

        static float FieldRowHeight() => UiTheme.LineH + UiTheme.GapXS +
                                         UiTheme.FieldH + UiTheme.GapS;

        static SandboxEditorLayout.Row EditorRow(SandboxEditorRowKind kind, float height,
                                                  bool visible, Action<UiLayoutRect> paint) =>
            new SandboxEditorLayout.Row(kind, height, visible, paint);

        SandboxEditorLayout.Row EditorButtonsRow(float height, bool editable, string source,
                                                 string kind, string name, Action save,
                                                 Action remove, float topGap = 0f) =>
            EditorRow(SandboxEditorRowKind.Actions, height, true, g =>
            {
                DrawEditorButtons(new Rect(g.X, g.Y + topGap, g.Width, UiTheme.BtnH),
                    editable, source, kind, name, save, remove);
            });

        static void DrawEditorButtons(Rect row, bool editable, string source, string kind,
                                      string name, Action save, Action remove)
        {
            float gap = UiTheme.GapS;
            float width = Mathf.Max(0f, (row.width - gap) / 2f);
            if (editable && UiButtons.Button(new Rect(row.x, row.y, width, UiTheme.BtnH),
                    "Save", UiTheme.Btn.Primary))
                save();
            if (source != "system" && UiButtons.Button(new Rect(row.x + width + gap, row.y,
                    width, UiTheme.BtnH), source == "override" ? "Reset to system" : "Remove",
                    UiTheme.Btn.Danger))
                remove();
        }

        static void DrawEditorLayout(Rect view, SandboxEditorLayout layout)
        {
            foreach (var row in layout.Rows)
            {
                if (!row.Visible || row.Paint == null) continue;
                var bounds = row.Bounds;
                row.Paint(new UiLayoutRect(view.x + bounds.X, view.y + bounds.Y,
                    bounds.Width, bounds.Height));
            }
        }

        void EmptyEditor(Rect r, string text)
        {
            UiText.StatusLabel(new Rect(r.x, r.y, r.width, UiTheme.LineH * 2f), text,
                UiTheme.Dim);
        }

        void Copy(string kind, string name)
        {
            SessionHub.Instance.Catalog.CopyPreset(kind, name, name, () =>
            {
                _error = null;
                Load();
            }, msg => _error = msg);
        }

        void Remove(string kind, string name)
        {
            Find.WindowStack.Add(ConfirmDialog.Create(
                kind == "sandbox" && SessionHub.Instance.Presets.Any(p => p.Name == name && p.Source == "override")
                    ? "Reset this user override and return to the system preset?"
                    : "Remove this user preset?",
                () => SessionHub.Instance.Catalog.RemovePreset(kind, name, () =>
                {
                    _preset = null; _command = null; _error = null; Load();
                }, msg => _error = msg)));
        }

        void DoFooter(Rect bar)
        {
            var foot = new UiLayout.Bar(bar);
            if (foot.Left("Reload", UiTheme.Btn.Ghost)) Load();
            if (_error != null)
            {
                GUI.color = UiTheme.Bad;
                UiText.RowLabel(foot.Rest(), _error);
                GUI.color = Color.white;
            }
        }

    }
}
