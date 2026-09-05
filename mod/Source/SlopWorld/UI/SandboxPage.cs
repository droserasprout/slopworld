using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The daemon's sandbox and command libraries. Each is a master/detail page: there are
    // many fields in one definition, but only one preset or command is being edited at once.
    public class SandboxPage : IOptionPage
    {
        public enum Section { Presets, Commands }

        readonly AsyncLoadState<bool> _load = new AsyncLoadState<bool>();
        readonly Section _section;

        string _error { get => _load.Error; set => _load.SetError(value); }
        bool _loaded => _load.HasValue && !_load.Loading;

        readonly SmoothScroll _listScroll = new SmoothScroll();
        readonly SmoothScroll _editorScroll = new SmoothScroll();
        PresetInfo _preset;
        CommandInfo _command;
        bool _newEntry;
        bool _newPresetRequested;
        bool _newCommandRequested;

        public SandboxPage(Section section)
        {
            _section = section;
        }

        public void Load()
        {
            _load.Load((ok, fail) => SessionHub.Instance.LoadPresets(() => ok(true), fail), _ =>
            {
                if (_preset != null && !_newEntry)
                    _preset = SessionHub.Instance.Presets.FirstOrDefault(p => p.Name == _preset.Name);
                if (_command != null && !_newEntry)
                    _command = SessionHub.Instance.Commands.FirstOrDefault(c => c.Name == _command.Name);
                _error = null;
                if (_newPresetRequested) NewPreset();
                else if (_newCommandRequested) NewCommand();
            });
        }

        public void NewPreset()
        {
            if (!_loaded)
            {
                _newPresetRequested = true;
                _newCommandRequested = false;
                return;
            }

            _newPresetRequested = false;
            string name = "new-preset";
            int suffix = 2;
            while (SessionHub.Instance.Presets.Any(p => p.Name == name))
                name = "new-preset-" + suffix++;

            _preset = new PresetInfo { Name = name, Source = "user" };
            _command = null;
            _newEntry = true;
            _setenvOwner = null;
            _setenvText = "";
            _error = null;
        }

        public void NewCommand()
        {
            if (!_loaded)
            {
                _newCommandRequested = true;
                _newPresetRequested = false;
                return;
            }

            _newCommandRequested = false;
            string name = "new-command";
            int suffix = 2;
            while (SessionHub.Instance.Commands.Any(c => c.Name == name))
                name = "new-command-" + suffix++;

            _preset = null;
            _command = new CommandInfo { Name = name, Source = "user" };
            _newEntry = true;
            _error = null;
        }

        public void Draw(Rect rect)
        {
            var body = SlopWidgets.PageBody(rect);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            if (!_loaded)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(inner, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
            }
            else
            {
                if (_section == Section.Presets) DoPresets(inner);
                else DoCommands(inner);
            }
            DoFooter(SlopWidgets.FooterBar(rect));
        }

        void DoPresets(Rect r)
        {
            SlopWidgets.SectionHeading(new Rect(r.x, r.y, r.width, SlopWidgets.RowH), "Presets");
            var caption = "System presets are supplied by slopd. Copy one to the user list to edit it; user presets can also be new entries.";
            GUI.color = SlopWidgets.Dim;
            float y = r.y + SlopWidgets.RowH + SlopWidgets.GapXS;
            float h = Text.CalcHeight(caption, r.width);
            float newW = SlopWidgets.BtnW("New user preset", 142f);
            Widgets.Label(new Rect(r.x, y, r.width - newW - SlopWidgets.GapS, h), caption);
            GUI.color = Color.white;
            if (SlopWidgets.Button(new Rect(r.xMax - newW, y, newW, SlopWidgets.BtnH),
                    "New user preset", SlopWidgets.Btn.Primary))
                NewPreset();
            y += h + SlopWidgets.GapS;
            var content = new Rect(r.x, y, r.width, r.yMax - y);
            float detailW = Mathf.Min(590f, content.width * .60f);
            float listW = content.width - detailW - SlopWidgets.GapM;
            DrawPresetList(new Rect(content.x, content.y, listW, content.height));
            DrawPresetEditor(new Rect(content.x + listW + SlopWidgets.GapM, content.y, detailW, content.height));
        }

        void DoCommands(Rect r)
        {
            SlopWidgets.SectionHeading(new Rect(r.x, r.y, r.width, SlopWidgets.RowH), "Commands");
            var caption = "Commands say what an agent runs and which presets it requires. Copy a system command to make a user override.";
            GUI.color = SlopWidgets.Dim;
            float y = r.y + SlopWidgets.RowH + SlopWidgets.GapXS;
            float h = Text.CalcHeight(caption, r.width);
            Widgets.Label(new Rect(r.x, y, r.width, h), caption);
            GUI.color = Color.white;
            y += h + SlopWidgets.GapS;
            var content = new Rect(r.x, y, r.width, r.yMax - y);
            float detailW = Mathf.Min(590f, content.width * .60f);
            float listW = content.width - detailW - SlopWidgets.GapM;
            DrawCommandList(new Rect(content.x, content.y, listW, content.height));
            DrawCommandEditor(new Rect(content.x + listW + SlopWidgets.GapM, content.y, detailW, content.height));
        }

        void DrawPresetList(Rect r)
        {
            var all = SessionHub.Instance.Presets;
            var system = all.Where(p => p.Source == "system")
                .OrderBy(p => p.Name == "global" ? 0 : 1)
                .ThenBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
            var user = all.Where(p => p.Source != "system")
                .OrderBy(p => p.Name == "global" ? 0 : 1)
                .ThenBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
            float h = (system.Count + user.Count + 3) * SlopWidgets.RowH;
            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW,
                Mathf.Max(h, r.height));
            using (_listScroll.Scope(r, view))
            {
                float y = 0f;
                y = DrawLibraryGroup(view, y, "System", system, p => p.Name,
                    p => { _preset = p; _newEntry = false; });
                DrawLibraryGroup(view, y, "User", user,
                    p => p.Name + (p.Source == "override" ? "  (override)" : ""),
                    p => { _preset = p; _newEntry = false; });
            }
        }

        float DrawLibraryGroup<T>(Rect view, float y, string heading, List<T> items,
                                  Func<T, string> label, Action<T> pick)
        {
            SlopWidgets.SectionHeading(new Rect(0f, y, view.width, SlopWidgets.RowH), heading);
            y += SlopWidgets.RowH;
            foreach (var item in items)
            {
                bool child = item is PresetInfo preset && IsOptionalChild(preset);
                float inset = child ? SlopWidgets.GapM : SlopWidgets.GapS;
                var cell = new Rect(inset, y, view.width - inset, SlopWidgets.RowH);
                string name = label(item);
                bool selected = (item is PresetInfo p && p == _preset) ||
                                (item is CommandInfo c && c == _command);
                RowChrome.Hover(cell, selected, true, RowHoverPolicy.OverlayAware);
                // A preset that hands the sandbox a road back out is dangerous even when it
                // is selected: the yellow stays on the name so the warning is visible in the
                // library, not only after opening its editor.
                bool dangerous = item is PresetInfo dangerousPreset && dangerousPreset.IsEscape;
                bool global = item is PresetInfo globalPreset && globalPreset.Name == "global";
                GUI.color = dangerous ? SlopWidgets.Warn
                    : global ? SlopWidgets.Global
                    : selected ? SlopWidgets.Lead : SlopWidgets.Name;
                SlopWidgets.RowLabel(cell, name);
                GUI.color = Color.white;
                string description = item is PresetInfo info ? info.Description
                    : item is CommandInfo command ? command.Description : "";
                if (!string.IsNullOrEmpty(description)) TooltipHandler.TipRegion(cell, description);
                if (Widgets.ButtonInvisible(cell)) pick(item);
                y += SlopWidgets.RowH;
            }
            if (items.Count == 0)
            {
                GUI.color = SlopWidgets.Dim;
                SlopWidgets.RowLabel(new Rect(SlopWidgets.GapS, y, view.width, SlopWidgets.RowH), "(none)");
                GUI.color = Color.white;
                y += SlopWidgets.RowH;
            }
            return y + SlopWidgets.GapS;
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
            float h = (system.Count + user.Count + 3) * SlopWidgets.RowH;
            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW,
                Mathf.Max(h, r.height));
            using (_listScroll.Scope(r, view))
            {
                float y = 0f;
                y = DrawLibraryGroup(view, y, "System", system, c => c.Name,
                    c => { _command = c; _newEntry = false; });
                y = DrawLibraryGroup(view, y, "User", user,
                    c => c.Name + (c.Source == "override" ? "  (override)" : ""),
                    c => { _command = c; _newEntry = false; });
                if (SlopWidgets.Button(new Rect(0f, y + SlopWidgets.GapS, view.width, SlopWidgets.BtnH),
                        "+ New command", SlopWidgets.Btn.Ghost))
                {
                    _command = new CommandInfo { Name = "new-command", Source = "user" };
                    _newEntry = true;
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
            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW,
                Mathf.Max(PresetEditorHeight(p, r.width - SlopWidgets.ScrollbarW), r.height));
            using (_editorScroll.Scope(r, view))
            {
                float y = DrawPresetFields(view, 0f, p, editable);
                EditorButtons(view, y, editable, p.Source, "sandbox", p.Name,
                    () => SessionHub.Instance.SavePreset(p, () => { _newEntry = false; _error = null; }, msg => _error = msg),
                    () => Remove("sandbox", p.Name));
            }
        }

        float DrawPresetFields(Rect view, float y, PresetInfo p, bool editable)
        {
            // Keep an escape warning above the identity so a long editor does not hide it.
            if (!string.IsNullOrEmpty(p.Escapes))
            {
                GUI.color = SlopWidgets.Warn;
                string warning = $"Escape path: {p.Escapes}.";
                float warningH = Text.CalcHeight(warning, view.width);
                Widgets.Label(new Rect(0f, y, view.width, warningH), warning);
                GUI.color = Color.white;
                y += warningH + SlopWidgets.GapM;
            }
            EditorTitle(view, ref y, p.Name, p.Source, editable, "sandbox");
            y = EditorField(view, y, "Name", "preset.name", p.Name, _newEntry,
                v => p.Name = v);
            y = EditorArea(view, y, "Description", "preset.description", p.Description,
                editable, 44f, v => p.Description = v);
            y = EditorList(view, y, "Requires", "preset.requires", p.Requires, editable);
            y = Rule(view.width, y + SlopWidgets.GapXS);
            y = DrawBindFields(view, y, p, editable);
            y = DrawPathFields(view, y, p, editable);
            y = Rule(view.width, y);
            y = EditorList(view, y, "Forwarded environment", "preset.env", p.Env, editable);
            return EditorArea(view, y, "Set environment (KEY=VALUE)", "preset.setenv",
                SetenvLines(p), editable, 48f, v => { _setenvText = v; ParseSetenv(p); });
        }

        float DrawBindFields(Rect view, float y, PresetInfo p, bool editable)
        {
            y = EditorList(view, y, "Read-only binds", "preset.ro", p.Ro, editable);
            y = EditorList(view, y, "Read-write binds", "preset.rw", p.Rw, editable);
            y = EditorList(view, y, "Device binds", "preset.dev", p.Dev, editable);
            return Rule(view.width, y);
        }

        float DrawPathFields(Rect view, float y, PresetInfo p, bool editable)
        {
            y = EditorList(view, y, "Private paths", "preset.private", p.Private, editable);
            y = EditorList(view, y, "Seed paths", "preset.seed", p.Seed, editable);
            y = EditorList(view, y, "Skip paths", "preset.skip", p.Skip, editable);
            return EditorList(view, y, "Shared files", "preset.shared", p.Shared, editable);
        }

        string _setenvText;
        PresetInfo _setenvOwner;
        void ParseSetenv(PresetInfo p)
        {
            p.Setenv.Clear();
            foreach (var line in SlopConfig.Split(_setenvText))
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
            var view = new Rect(0f, 0f, r.width - SlopWidgets.ScrollbarW,
                Mathf.Max(CommandEditorHeight(c, r.width - SlopWidgets.ScrollbarW), r.height));
            using (_editorScroll.Scope(r, view))
            {
                float y = 0f;
                EditorTitle(view, ref y, c.Name, c.Source, editable, "command");
                y = EditorField(view, y, "Name", "command.name", c.Name, _newEntry, v => c.Name = v);
                y = EditorArea(view, y, "Description", "command.description", c.Description, editable, 44f, v => c.Description = v);
                y = EditorArea(view, y, "Command line", "command.cmd", c.Cmd, editable, 52f, v => c.Cmd = v);
                y += SlopWidgets.GapS;
                SlopWidgets.SectionHeading(new Rect(0f, y, view.width, SlopWidgets.RowH), "Sandbox dependencies");
                y += SlopWidgets.RowH;
                GUI.color = SlopWidgets.Dim;
                SlopWidgets.RowLabel(new Rect(0f, y, view.width, SlopWidgets.LineH),
                    "These presets are added whenever this command runs.");
                GUI.color = Color.white;
                y += SlopWidgets.LineH + SlopWidgets.GapXS;
                foreach (var p in SessionHub.Instance.Presets.Where(p => p.Name != "global"))
                {
                    bool on = c.Sandbox.Contains(p.Name);
                    bool was = on;
                    bool next = SlopWidgets.Checkbox(new Rect(0f, y, view.width, SlopWidgets.RowH), p.Name, on,
                        p.Description, !editable, p.IsEscape);
                    if (editable && next != was)
                    {
                        if (next) c.Sandbox.Add(p.Name); else c.Sandbox.Remove(p.Name);
                    }
                    y += SlopWidgets.RowH;
                }
                EditorButtons(view, y + SlopWidgets.GapS, editable, c.Source, "command", c.Name,
                    () => SessionHub.Instance.SaveCommand(c, () => { _newEntry = false; _error = null; }, msg => _error = msg),
                    () => Remove("command", c.Name));
            }
        }

        void EditorTitle(Rect view, ref float y, string name, string source, bool editable, string kind)
        {
            GUI.color = SlopWidgets.Lead;
            SlopWidgets.RowLabel(new Rect(0f, y, view.width, SlopWidgets.RowH),
                name + (source == "override" ? "  (override)" : ""));
            GUI.color = Color.white;
            y += SlopWidgets.RowH;
            GUI.color = source == "system" ? SlopWidgets.Faint : SlopWidgets.Yes;
            SlopWidgets.RowLabel(new Rect(0f, y, view.width, SlopWidgets.LineH),
                source == "system" ? "System preset (read-only)" : "User preset");
            GUI.color = Color.white;
            y += SlopWidgets.LineH + SlopWidgets.GapS;
            if (source == "system")
            {
                if (SlopWidgets.Button(new Rect(0f, y, view.width, SlopWidgets.BtnH), "Copy to user", SlopWidgets.Btn.Primary))
                    Copy(kind, name);
                y += SlopWidgets.BtnH + SlopWidgets.GapM;
            }
        }

        float EditorField(Rect view, float y, string label, string name, string value, bool editable, Action<string> set)
        {
            if (!editable && string.IsNullOrWhiteSpace(value)) return y;
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(new Rect(0f, y, view.width, SlopWidgets.LineH), label);
            GUI.color = Color.white;
            y += SlopWidgets.LineH + SlopWidgets.GapXS;
            set(SlopWidgets.Field(new Rect(0f, y, view.width, SlopWidgets.FieldH), name, value, editable));
            return y + SlopWidgets.FieldH + SlopWidgets.GapS;
        }

        float EditorArea(Rect view, float y, string label, string name, string value, bool editable,
                         float height, Action<string> set)
        {
            if (!editable && string.IsNullOrWhiteSpace(value)) return y;
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(new Rect(0f, y, view.width, SlopWidgets.LineH), label);
            GUI.color = Color.white;
            y += SlopWidgets.LineH + SlopWidgets.GapXS;
            float actual = AreaHeight(view.width, value, height);
            set(SlopWidgets.Area(new Rect(0f, y, view.width, actual), name, value, editable));
            return y + actual + SlopWidgets.GapS;
        }

        static float AreaHeight(float width, string text, float minimum) =>
            Mathf.Max(minimum, Text.CalcHeight(string.IsNullOrEmpty(text) ? " " : text,
                                                width - 12f) + 8f);

        static float FieldHeight() => SlopWidgets.LineH + SlopWidgets.GapXS +
                                      SlopWidgets.FieldH + SlopWidgets.GapS;

        static float AreaEditorHeight(float width, string text, float minimum) =>
            SlopWidgets.LineH + SlopWidgets.GapXS + AreaHeight(width, text, minimum) + SlopWidgets.GapS;

        static float OptionalAreaEditorHeight(float width, string text, float minimum, bool editable) =>
            !editable && string.IsNullOrWhiteSpace(text) ? 0f : AreaEditorHeight(width, text, minimum);

        static float OptionalListEditorHeight(float width, List<string> items, bool editable) =>
            OptionalAreaEditorHeight(width, SlopConfig.Lines(items), 48f, editable);

        static float TitleHeight(string source) => SlopWidgets.RowH + SlopWidgets.LineH +
            SlopWidgets.GapS + (source == "system" ? SlopWidgets.BtnH + SlopWidgets.GapM : 0f);

        static float PresetEditorHeight(PresetInfo p, float width)
        {
            bool editable = p.Source != "system";
            float y = string.IsNullOrEmpty(p.Escapes) ? 0f
                : Text.CalcHeight($"Escape path: {p.Escapes}.", width) + SlopWidgets.GapM;
            y += TitleHeight(p.Source) + FieldHeight();
            y += OptionalAreaEditorHeight(width, p.Description, 44f, editable) +
                 OptionalListEditorHeight(width, p.Requires, editable);
            y += SlopWidgets.GapXS + 1f + SlopWidgets.GapM;
            y += OptionalListEditorHeight(width, p.Ro, editable) +
                 OptionalListEditorHeight(width, p.Rw, editable) +
                 OptionalListEditorHeight(width, p.Dev, editable);
            y += 1f + SlopWidgets.GapM;
            y += OptionalListEditorHeight(width, p.Private, editable) +
                 OptionalListEditorHeight(width, p.Seed, editable) +
                 OptionalListEditorHeight(width, p.Skip, editable) +
                 OptionalListEditorHeight(width, p.Shared, editable);
            y += 1f + SlopWidgets.GapM;
            y += OptionalListEditorHeight(width, p.Env, editable) +
                 OptionalAreaEditorHeight(width,
                     string.Join("\n", p.Setenv.Select(x => x.Key + "=" + x.Value).ToArray()),
                     48f, editable);
            return y + SlopWidgets.BtnH + SlopWidgets.GapM;
        }

        static float CommandEditorHeight(CommandInfo c, float width)
        {
            bool editable = c.Source != "system";
            return TitleHeight(c.Source) + FieldHeight() +
            OptionalAreaEditorHeight(width, c.Description, 44f, editable) +
            OptionalAreaEditorHeight(width, c.Cmd, 52f, editable) +
            SlopWidgets.GapS + SlopWidgets.RowH + SlopWidgets.LineH + SlopWidgets.GapXS +
            SessionHub.Instance.Presets.Count(p => p.Name != "global") * SlopWidgets.RowH +
            SlopWidgets.BtnH + SlopWidgets.GapM;
        }

        float EditorList(Rect view, float y, string label, string name, List<string> items, bool editable)
        {
            string text = SlopConfig.Lines(items);
            y = EditorArea(view, y, label, name, text, editable, 48f, v =>
            {
                items.Clear();
                items.AddRange(SlopConfig.Split(v));
            });
            return y;
        }

        void EditorButtons(Rect view, float y, bool editable, string source, string kind, string name,
                           Action save, Action remove)
        {
            if (editable && SlopWidgets.Button(new Rect(0f, y, view.width * .48f, SlopWidgets.BtnH), "Save", SlopWidgets.Btn.Primary))
                save();
            if (source != "system" && SlopWidgets.Button(new Rect(view.width * .52f, y, view.width * .48f, SlopWidgets.BtnH),
                    source == "override" ? "Reset to system" : "Remove", SlopWidgets.Btn.Danger))
                remove();
        }

        void EmptyEditor(Rect r, string text)
        {
            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(r.x, r.y, r.width, SlopWidgets.LineH * 2f), text);
            GUI.color = Color.white;
        }

        static float Rule(float width, float y)
        {
            Slab.Hairline(new Rect(0f, y, width, 1f), SlopWidgets.Edge);
            return y + SlopWidgets.GapM;
        }

        void Copy(string kind, string name)
        {
            SessionHub.Instance.CopyPreset(kind, name, name, () =>
            {
                _error = null;
                Load();
            }, msg => _error = msg);
        }

        void Remove(string kind, string name)
        {
            Find.WindowStack.Add(SlopConfirmDialog.Create(
                kind == "sandbox" && SessionHub.Instance.Presets.Any(p => p.Name == name && p.Source == "override")
                    ? "Reset this user override and return to the system preset?"
                    : "Remove this user preset?",
                () => SessionHub.Instance.RemovePreset(kind, name, () =>
                {
                    _preset = null; _command = null; _error = null; Load();
                }, msg => _error = msg)));
        }

        void DoFooter(Rect bar)
        {
            var foot = new SlopWidgets.Bar(bar);
            if (foot.Left("Reload", SlopWidgets.Btn.Ghost)) Load();
            if (_error != null)
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(foot.Rest(), _error);
                GUI.color = Color.white;
            }
        }

    }
}
