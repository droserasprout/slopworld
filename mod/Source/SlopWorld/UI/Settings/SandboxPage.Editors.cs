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
            var systemTree = new PresetHierarchy(all.Where(p => p.Source == "system"));
            var userTree = new PresetHierarchy(all.Where(p => p.Source != "system"));
            var system = systemTree.Items;
            var user = userTree.Items;
            float h =
                (
                    system.Count
                    + user.Count
                    + 2
                    + (system.Count == 0 ? 1 : 0)
                    + (user.Count == 0 ? 1 : 0)
                ) * UiTheme.RowH
                + UiTheme.GapS * 2f;
            var view = new Rect(
                0f,
                0f,
                Mathf.Max(0f, r.width - UiTheme.ScrollbarW),
                Mathf.Max(h, r.height)
            );
            using (_listScroll.Scope(r, view))
            {
                float y = 0f;
                y = DrawLibraryGroup(
                    view,
                    y,
                    "User",
                    user,
                    p => p.Name + (p.Source == "override" ? "  (override)" : ""),
                    p =>
                    {
                        _preset = p;
                        _newEntry = false;
                    },
                    userTree.Depth
                );
                DrawLibraryGroup(
                    view,
                    y,
                    "System",
                    system,
                    p => p.Name,
                    p =>
                    {
                        _preset = p;
                        _newEntry = false;
                    },
                    systemTree.Depth
                );
            }
        }

        static float LibraryGroupHeight(int count) =>
            (1 + Mathf.Max(1, count)) * UiTheme.RowH + UiTheme.GapS;

        float DrawLibraryGroup<T>(
            Rect view,
            float y,
            string heading,
            List<T> items,
            Func<T, string> label,
            Action<T> pick,
            Func<T, int> depth = null
        )
        {
            UiLayout.SectionHeading(new Rect(0f, y, view.width, UiTheme.RowH), heading);
            y += UiTheme.RowH;
            foreach (var item in items)
            {
                float inset =
                    UiTheme.GapS + (depth?.Invoke(item) ?? 0) * (UiTheme.GapM - UiTheme.GapS);
                var cell = new Rect(inset, y, view.width - inset, UiTheme.RowH);
                string name = label(item);
                bool selected =
                    (item is PresetInfo p && p == _preset)
                    || (item is CommandInfo c && c == _command);
                RowChrome.Hover(cell, selected, true, RowHoverPolicy.OverlayAware);
                var warning =
                    item is PresetInfo warningPreset
                        ? PresetWarnings.For(warningPreset, SessionHub.Instance.Presets)
                    : item is CommandInfo warningCommand
                        ? PresetWarnings.ForSelection(
                            warningCommand.Sandbox.Concat(new[] { "global" }),
                            SessionHub.Instance.Presets
                        )
                    : new PresetWarning();
                bool global = item is PresetInfo globalPreset && globalPreset.Name == "global";
                GUI.color =
                    warning.Level == PresetWarningLevel.HostControl ? UiTheme.HostControl
                    : warning.Level == PresetWarningLevel.Sensitive ? UiTheme.Warn
                    : global ? UiTheme.Global
                    : selected ? UiTheme.Lead
                    : UiTheme.Name;
                UiText.RowLabel(cell, name);
                GUI.color = Color.white;
                string description =
                    item is PresetInfo info ? info.Description
                    : item is CommandInfo command ? command.Description
                    : "";
                if (warning.Level != PresetWarningLevel.None)
                    description = warning.Details + "\n\n" + description;
                if (!string.IsNullOrEmpty(description))
                    TooltipHandler.TipRegion(cell, description);
                if (UiButtons.RowButton(cell))
                    pick(item);
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

        void DrawCommandList(Rect r)
        {
            var all = SessionHub.Instance.Commands;
            var system = all.Where(c => c.Source == "system").ToList();
            var user = all.Where(c => c.Source != "system").ToList();
            float h =
                LibraryGroupHeight(system.Count)
                + LibraryGroupHeight(user.Count)
                + UiTheme.GapS
                + UiTheme.BtnH;
            var view = new Rect(
                0f,
                0f,
                Mathf.Max(0f, r.width - UiTheme.ScrollbarW),
                Mathf.Max(h, r.height)
            );
            using (_listScroll.Scope(r, view))
            {
                float y = 0f;
                y = DrawLibraryGroup(
                    view,
                    y,
                    "System",
                    system,
                    c => c.Name,
                    c =>
                    {
                        _command = c;
                        _newEntry = false;
                    }
                );
                y = DrawLibraryGroup(
                    view,
                    y,
                    "User",
                    user,
                    c => c.Name + (c.Source == "override" ? "  (override)" : ""),
                    c =>
                    {
                        _command = c;
                        _newEntry = false;
                    }
                );
                if (
                    UiButtons.Button(
                        new Rect(0f, y + UiTheme.GapS, view.width, UiTheme.BtnH),
                        "+ New command",
                        UiTheme.Btn.Ghost
                    )
                )
                {
                    NewCommand();
                }
            }
        }

        void DrawPresetEditor(Rect r) =>
            DrawEditor(
                r,
                _preset,
                "Select a preset to inspect or edit it.",
                _preset?.Source,
                (view, p, editable, draw) => DrawPresetFields(view, 0f, p, editable, draw),
                (p, ok, fail) => SessionHub.Instance.Catalog.SavePreset(p, ok, fail),
                p => Remove("sandbox_presets", p.Name)
            );

        // Owns the editor lifecycle; fields and catalog routes remain specific to each library.
        void DrawEditor<T>(
            Rect r,
            T selected,
            string emptyText,
            string source,
            Func<Rect, T, bool, bool, float> fields,
            Action<T, Action, Action<string>> save,
            Action<T> remove
        )
            where T : class
        {
            if (selected == null)
            {
                EmptyEditor(r, emptyText);
                return;
            }
            BindAreas(selected);
            bool editable = _newEntry || source != "system";
            var view = new Rect(0f, 0f, Mathf.Max(0f, r.width - UiTheme.ScrollbarW), 0f);
            // The same form measures and draws. Measurement must not invoke controls or setters.
            view.height = Mathf.Max(
                fields(view, selected, editable, false) + UiTheme.BtnH + UiTheme.GapM,
                r.height
            );
            using (_editorScroll.Scope(r, view))
            {
                float y = fields(view, selected, editable, true);
                EditorButtons(
                    view,
                    y,
                    editable,
                    source,
                    () =>
                        save(
                            selected,
                            () =>
                            {
                                _newEntry = false;
                                _error = null;
                            },
                            msg => _error = msg
                        ),
                    () => remove(selected)
                );
            }
        }

        float DrawPresetFields(Rect view, float y, PresetInfo p, bool editable, bool draw)
        {
            // Include inherited access before the identity so long editors keep it visible.
            var access = PresetWarnings.For(p, SessionHub.Instance.Presets);
            if (access.Level != PresetWarningLevel.None)
            {
                string warning = access.Details;
                float warningH = UiText.PlainStatusLabelHeight(warning, view.width);
                if (draw)
                    UiText.PlainStatusLabel(
                        new Rect(0f, y, view.width, warningH),
                        warning,
                        access.Level == PresetWarningLevel.HostControl
                            ? UiTheme.HostControl
                            : UiTheme.Warn
                    );
                y += warningH + UiTheme.GapM;
            }
            EditorTitle(view, ref y, p.Name, p.Source, "sandbox_presets", draw);
            y = EditorField(
                view,
                y,
                "Name",
                "preset.name",
                p.Name,
                _newEntry,
                v => p.Name = v,
                draw
            );
            y = EditorArea(
                view,
                y,
                "Description",
                "preset.description",
                p.Description,
                editable,
                44f,
                v => p.Description = v,
                draw
            );
            if (draw)
            {
                var levels = new[]
                {
                    WireProtocol.PresetWarningLevel.None,
                    WireProtocol.PresetWarningLevel.Sensitive,
                    WireProtocol.PresetWarningLevel.HostControl,
                };
                var labels = new[] { "No declared warning", "Sensitive access", "Host control" };
                var options = levels
                    .Select(
                        (level, index) =>
                            new SelectorOption(
                                labels[index],
                                () => p.WarningLevel = level,
                                editable
                            )
                    )
                    .ToArray();
                int selected = System.Array.IndexOf(levels, p.WarningLevel);
                UiControls.Select(
                    new Rect(0f, y, view.width, FieldHeight()),
                    "Warning level",
                    selected < 0 ? p.WarningLevel : labels[selected],
                    options,
                    out _,
                    on: editable
                );
            }
            y += FieldHeight();
            y = EditorArea(
                view,
                y,
                "Access warning",
                "preset.escapes",
                p.Escapes,
                editable,
                44f,
                v => p.Escapes = v,
                draw
            );
            y = EditorList(view, y, "Requires", "preset.requires", p.Requires, editable, draw);
            y = Rule(view.width, y + UiTheme.GapXS, draw);
            y = DrawBindFields(view, y, p, editable, draw);
            y = DrawPathFields(view, y, p, editable, draw);
            y = Rule(view.width, y, draw);
            y = EditorList(view, y, "Forwarded environment", "preset.env", p.Env, editable, draw);
            return EditorArea(
                view,
                y,
                "Set environment (KEY=VALUE)",
                "preset.setenv",
                SetenvLines(p),
                editable,
                48f,
                v =>
                {
                    _setenvText = v;
                    ParseSetenv(p);
                },
                draw
            );
        }

        float DrawBindFields(Rect view, float y, PresetInfo p, bool editable, bool draw)
        {
            y = EditorList(view, y, "Read-only binds", "preset.ro", p.Ro, editable, draw);
            y = EditorList(view, y, "Read-write binds", "preset.rw", p.Rw, editable, draw);
            y = EditorList(
                view,
                y,
                "Shared cache directories (created at launch)",
                "preset.cache",
                p.Cache,
                editable,
                draw
            );
            y = EditorList(view, y, "Device binds", "preset.dev", p.Dev, editable, draw);
            return Rule(view.width, y, draw);
        }

        float DrawPathFields(Rect view, float y, PresetInfo p, bool editable, bool draw)
        {
            y = EditorList(view, y, "Private paths", "preset.private", p.Private, editable, draw);
            y = EditorList(view, y, "Seed paths", "preset.seed", p.Seed, editable, draw);
            y = EditorList(view, y, "Skip paths", "preset.skip", p.Skip, editable, draw);
            return EditorList(view, y, "Shared files", "preset.shared", p.Shared, editable, draw);
        }

        string _setenvText;
        PresetInfo _setenvOwner;

        void ParseSetenv(PresetInfo p)
        {
            p.Setenv.Clear();
            foreach (var line in DaemonConfig.Split(_setenvText))
            {
                int at = line.IndexOf('=');
                if (at > 0)
                    p.Setenv[line.Substring(0, at).Trim()] = line.Substring(at + 1);
            }
        }

        string SetenvLines(PresetInfo p)
        {
            if (_setenvOwner != p)
            {
                _setenvOwner = p;
                _setenvText = string.Join(
                    "\n",
                    p.Setenv.Select(x => x.Key + "=" + x.Value).ToArray()
                );
            }
            return _setenvText ?? "";
        }

        void DrawCommandEditor(Rect r) =>
            DrawEditor(
                r,
                _command,
                "Select a command to inspect or edit it.",
                _command?.Source,
                DrawCommandFields,
                (c, ok, fail) => SessionHub.Instance.Catalog.SaveCommand(c, ok, fail),
                c => Remove("app_presets", c.Name)
            );

        float DrawCommandFields(Rect view, CommandInfo c, bool editable, bool draw)
        {
            float y = 0f;
            EditorTitle(view, ref y, c.Name, c.Source, "app_presets", draw);
            y = EditorField(
                view,
                y,
                "Name",
                "command.name",
                c.Name,
                _newEntry,
                v => c.Name = v,
                draw
            );
            DrawCommandKind(view, ref y, c, editable, draw);
            y = EditorArea(
                view,
                y,
                "Description",
                "command.description",
                c.Description,
                editable,
                44f,
                v => c.Description = v,
                draw
            );
            y = EditorArea(
                view,
                y,
                "Command line",
                "command.cmd",
                c.Cmd,
                editable,
                52f,
                v => c.Cmd = v,
                draw
            );
            y += UiTheme.GapS;
            if (draw)
                UiLayout.SectionHeading(
                    new Rect(0f, y, view.width, UiTheme.RowH),
                    "Sandbox dependencies"
                );
            y += UiTheme.RowH;
            if (draw)
            {
                GUI.color = UiTheme.Dim;
                UiText.RowLabel(
                    new Rect(0f, y, view.width, UiTheme.LineH),
                    "The daemon includes these presets whenever an agent uses this command."
                );
                GUI.color = Color.white;
            }
            y += UiTheme.LineH + UiTheme.GapXS;
            var selectedWarning = PresetWarnings.ForSelection(
                c.Sandbox.Concat(new[] { "global" }),
                SessionHub.Instance.Presets
            );
            if (selectedWarning.Level != PresetWarningLevel.None)
            {
                float height = UiText.PlainStatusLabelHeight(selectedWarning.Details, view.width);
                if (draw)
                    UiText.PlainStatusLabel(
                        new Rect(0f, y, view.width, height),
                        selectedWarning.Details,
                        selectedWarning.Level == PresetWarningLevel.HostControl
                            ? UiTheme.HostControl
                            : UiTheme.Warn
                    );
                y += height + UiTheme.GapS;
            }
            foreach (var p in SessionHub.Instance.Presets.Where(p => p.Name != "global"))
            {
                if (draw)
                {
                    var warning = PresetWarnings.For(p, SessionHub.Instance.Presets);
                    bool on = c.Sandbox.Contains(p.Name);
                    bool next = UiControls.Checkbox(
                        new Rect(0f, y, view.width, UiTheme.RowH),
                        p.Name,
                        on,
                        warning.Level == PresetWarningLevel.None
                            ? p.Description
                            : warning.Details + "\n\n" + p.Description,
                        !editable,
                        false,
                        warning.Level == PresetWarningLevel.None ? (Color?)null
                            : warning.Level == PresetWarningLevel.HostControl ? UiTheme.HostControl
                            : UiTheme.Warn
                    );
                    if (editable && next != on)
                    {
                        if (next)
                            c.Sandbox.Add(p.Name);
                        else
                            c.Sandbox.Remove(p.Name);
                    }
                }
                y += UiTheme.RowH;
            }
            return y + UiTheme.GapS;
        }

        static void DrawCommandKind(Rect view, ref float y, CommandInfo c, bool editable, bool draw)
        {
            if (!draw)
            {
                y += FieldHeight();
                return;
            }
            string label = c.Kind == CommandInfo.ShellKind ? "Shell" : "Agent";
            var options = new[]
            {
                new SelectorOption("Agent", () => c.Kind = CommandInfo.AgentKind, editable),
                new SelectorOption("Shell", () => c.Kind = CommandInfo.ShellKind, editable),
            };
            var row = new Rect(0f, y, view.width, FieldHeight());
            UiControls.Select(row, "Kind", label, options, out _, on: editable);
            y += row.height;
        }

        void EditorTitle(Rect view, ref float y, string name, string source, string kind, bool draw)
        {
            if (draw)
            {
                GUI.color = UiTheme.Lead;
                UiText.RowLabel(
                    new Rect(0f, y, view.width, UiTheme.RowH),
                    name + (source == "override" ? "  (override)" : "")
                );
                GUI.color = source == "system" ? UiTheme.Faint : UiTheme.Yes;
                UiText.RowLabel(
                    new Rect(0f, y + UiTheme.RowH, view.width, UiTheme.LineH),
                    source == "system" ? "System preset (read-only)" : "User preset"
                );
                GUI.color = Color.white;
            }
            y += UiTheme.RowH + UiTheme.LineH + UiTheme.GapS;
            if (source == "system")
            {
                if (
                    draw
                    && UiButtons.Button(
                        new Rect(0f, y, view.width, UiTheme.BtnH),
                        "Copy to user",
                        UiTheme.Btn.Primary
                    )
                )
                    Copy(kind, name);
                y += UiTheme.BtnH + UiTheme.GapM;
            }
        }

        float EditorField(
            Rect view,
            float y,
            string label,
            string name,
            string value,
            bool editable,
            Action<string> set,
            bool draw
        )
        {
            if (!editable && string.IsNullOrWhiteSpace(value))
                return y;
            if (draw)
            {
                EditorCaption(view, y, label);
                string next = UiText.Field(
                    new Rect(0f, y + UiTheme.LineH + UiTheme.GapXS, view.width, UiTheme.FieldH),
                    name,
                    value,
                    editable
                );
                if (editable && next != value)
                    set(next);
            }
            return y + FieldHeight();
        }

        readonly Dictionary<string, UiAreaResize> _areas = new Dictionary<string, UiAreaResize>();
        object _areaOwner;

        void BindAreas(object owner)
        {
            // Names remain editable while creating an entry; only a different draft
            // should replace its raw text and retained field geometry.
            if (ReferenceEquals(_areaOwner, owner))
                return;
            _areaOwner = owner;
            _areas.Clear();
            _listDrafts.Clear();
        }

        float EditorArea(
            Rect view,
            float y,
            string label,
            string name,
            string value,
            bool editable,
            float height,
            Action<string> set,
            bool draw
        )
        {
            if (!editable && string.IsNullOrWhiteSpace(value))
                return y;
            if (!_areas.TryGetValue(name, out var sizing))
            {
                sizing = new UiAreaResize(height, minimum: height, grow: true);
                _areas.Add(name, sizing);
            }
            float actual = UiText.AreaHeight(view.width, value, sizing, editable);
            if (draw)
            {
                EditorCaption(view, y, label);
                string next = UiText.Area(
                    new Rect(0f, y + UiTheme.LineH + UiTheme.GapXS, view.width, actual),
                    name,
                    value,
                    editable,
                    resize: sizing
                );
                // List/environment setters normalize text. Repainting must not rewrite it.
                if (editable && next != value)
                    set(next);
            }
            return y + UiTheme.LineH + UiTheme.GapXS + actual + UiTheme.GapS;
        }

        static void EditorCaption(Rect view, float y, string label)
        {
            GUI.color = UiTheme.Dim;
            UiText.RowLabel(new Rect(0f, y, view.width, UiTheme.LineH), label);
            GUI.color = Color.white;
        }

        static float FieldHeight() => UiTheme.LineH + UiTheme.GapXS + UiTheme.FieldH + UiTheme.GapS;

        // The editable document must retain whitespace and empty lines. The daemon list
        // is a normalized projection, never the source for the next native editor pass.
        sealed class ListDraft
        {
            public readonly List<string> Items;
            public string Text;

            public ListDraft(List<string> items)
            {
                Items = items;
                Text = DaemonConfig.Lines(items);
            }
        }

        readonly Dictionary<string, ListDraft> _listDrafts = new Dictionary<string, ListDraft>();

        float EditorList(
            Rect view,
            float y,
            string label,
            string name,
            List<string> items,
            bool editable,
            bool draw
        )
        {
            if (!_listDrafts.TryGetValue(name, out var draft) || draft.Items != items)
            {
                draft = new ListDraft(items);
                _listDrafts[name] = draft;
            }
            return EditorArea(
                view,
                y,
                label,
                name,
                draft.Text,
                editable,
                48f,
                v =>
                {
                    draft.Text = v;
                    items.Clear();
                    items.AddRange(DaemonConfig.Split(v));
                },
                draw
            );
        }

        void EditorButtons(
            Rect view,
            float y,
            bool editable,
            string source,
            Action save,
            Action remove
        )
        {
            float gap = UiTheme.GapS;
            float width = Mathf.Max(0f, (view.width - gap) / 2f);
            if (
                editable
                && UiButtons.Button(
                    new Rect(0f, y, width, UiTheme.BtnH),
                    "Save",
                    UiTheme.Btn.Primary
                )
            )
                save();
            if (
                source != "system"
                && UiButtons.Button(
                    new Rect(width + gap, y, width, UiTheme.BtnH),
                    source == "override" ? "Reset to system" : "Remove",
                    UiTheme.Btn.Danger
                )
            )
                remove();
        }

        void EmptyEditor(Rect r, string text)
        {
            UiText.PlainStatusLabel(
                new Rect(r.x, r.y, r.width, UiTheme.LineH * 2f),
                text,
                UiTheme.Dim
            );
        }

        static float Rule(float width, float y, bool draw)
        {
            // The hairline sits inside the gap. It does not add another unit of height.
            if (draw)
                Slab.Hairline(new Rect(0f, y, width, 1f), UiTheme.Edge);
            return y + UiTheme.GapM;
        }

        void Copy(string kind, string name)
        {
            SessionHub.Instance.Catalog.CopyPreset(
                kind,
                name,
                name,
                () =>
                {
                    _error = null;
                    Load();
                },
                msg => _error = msg
            );
        }

        void Remove(string kind, string name)
        {
            Find.WindowStack.Add(
                ConfirmDialog.Create(
                    kind == "sandbox_presets"
                    && SessionHub.Instance.Presets.Any(p =>
                        p.Name == name && p.Source == "override"
                    )
                        ? "Reset this user override and return to the system preset?"
                        : "Remove this user preset?",
                    () =>
                        SessionHub.Instance.Catalog.RemovePreset(
                            kind,
                            name,
                            () =>
                            {
                                _preset = null;
                                _command = null;
                                _error = null;
                                Load();
                            },
                            msg => _error = msg
                        ),
                    destructive: true
                )
            );
        }

        void DoFooter(Rect bar)
        {
            var foot = new UiLayout.Bar(bar);
            if (foot.Left("Reload", UiTheme.Btn.Ghost))
                Load();
            if (_error != null)
            {
                GUI.color = UiTheme.Bad;
                UiText.RowLabel(foot.Rest(), _error);
                GUI.color = Color.white;
            }
        }
    }
}
