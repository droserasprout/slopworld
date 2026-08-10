using System;
using System.Collections.Generic;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The daemon's sandbox library. Global is the machine-wide base; Presets and Commands
    // are the user-facing files beside it. A library is deliberately a master/detail page:
    // there are many fields in one preset, but only one definition is being edited at once.
    public class SandboxPage
    {
        enum Tab { Global, Presets, Commands }

        SlopConfig _cfg;
        string _error;
        bool _loaded;
        Tab _tab;
        string _roPaths, _rwPaths, _passEnv;

        readonly SmoothScroll _listScroll = new SmoothScroll();
        readonly SmoothScroll _editorScroll = new SmoothScroll();
        PresetInfo _preset;
        CommandInfo _command;
        bool _newEntry;

        public void Load()
        {
            SlopClient.Get("/api/config", j =>
            {
                _cfg = SlopConfig.FromJson(j["values"]);
                _roPaths = SlopConfig.Lines(_cfg.RoPaths);
                _rwPaths = SlopConfig.Lines(_cfg.RwPaths);
                _passEnv = SlopConfig.Lines(_cfg.PassEnv);
                _loaded = true;
                _error = null;
            }, msg => { _error = msg; _loaded = false; });
            SessionHub.Instance.LoadPresets(() =>
            {
                if (_preset != null)
                    _preset = SessionHub.Instance.Presets.FirstOrDefault(p => p.Name == _preset.Name);
                if (_command != null)
                    _command = SessionHub.Instance.Commands.FirstOrDefault(c => c.Name == _command.Name);
            }, msg => _error = msg);
        }

        public void Draw(Rect rect)
        {
            SlopWidgets.PageCaption(rect, "The base every sandbox is built on, and the presets that add to it.");
            var body = SlopWidgets.PageBody(rect);
            Widgets.DrawMenuSection(body);
            var inner = body.ContractedBy(SlopWidgets.GapM);

            if (!_loaded)
            {
                GUI.color = _error != null ? SlopWidgets.Bad : SlopWidgets.Dim;
                Widgets.Label(inner, _error ?? "Waiting for the daemon...");
                GUI.color = Color.white;
            }
            else
            {
                DrawTabs(new Rect(inner.x, inner.y, inner.width, SlopWidgets.BtnH));
                var content = new Rect(inner.x, inner.y + SlopWidgets.BtnH + SlopWidgets.GapM,
                    inner.width, inner.yMax - inner.y - SlopWidgets.BtnH - SlopWidgets.GapM);
                if (_tab == Tab.Global) DoGlobal(content);
                else if (_tab == Tab.Presets) DoPresets(content);
                else DoCommands(content);
            }
            DoFooter(SlopWidgets.FooterBar(rect));
        }

        void DrawTabs(Rect r)
        {
            float gap = SlopWidgets.GapS;
            float w = (r.width - gap * 2f) / 3f;
            DrawTab(new Rect(r.x, r.y, w, r.height), "Global", Tab.Global);
            DrawTab(new Rect(r.x + w + gap, r.y, w, r.height), "Presets", Tab.Presets);
            DrawTab(new Rect(r.x + (w + gap) * 2f, r.y, w, r.height), "Commands", Tab.Commands);
        }

        void DrawTab(Rect r, string label, Tab tab)
        {
            if (SlopWidgets.Button(r, label, _tab == tab ? SlopWidgets.Btn.Primary : SlopWidgets.Btn.Ghost))
            {
                _tab = tab;
                _error = null;
            }
        }

        void DoGlobal(Rect r)
        {
            SlopWidgets.SectionHeading(new Rect(r.x, r.y, r.width, SlopWidgets.RowH), "Global");
            float top = r.y + SlopWidgets.RowH + SlopWidgets.GapXS;
            const string what = "The base every project builds on. A project's own presets and binds are added to these; every agent runs in a sandbox.";
            GUI.color = SlopWidgets.Dim;
            var note = new Rect(r.x, top, r.width, Text.CalcHeight(what, r.width));
            Widgets.Label(note, what);
            GUI.color = Color.white;
            top = note.yMax + SlopWidgets.GapM;

            float gap = SlopWidgets.GapS;
            float boxW = (r.width - gap * 2f) / 3f;
            float boxH = r.yMax - top;
            _roPaths = SlopWidgets.PathList(new Rect(r.x, top, boxW, boxH),
                "sandbox.ro", "Read-only binds", _roPaths);
            _rwPaths = SlopWidgets.PathList(new Rect(r.x + boxW + gap, top, boxW, boxH),
                "sandbox.rw", "Read-write binds", _rwPaths);
            _passEnv = SlopWidgets.PathList(new Rect(r.x + (boxW + gap) * 2f, top, boxW, boxH),
                "sandbox.env", "Passed env vars", _passEnv);
        }

        void DoPresets(Rect r)
        {
            SlopWidgets.SectionHeading(new Rect(r.x, r.y, r.width, SlopWidgets.RowH), "Presets");
            var caption = "System presets are supplied by slopd. Copy one to the user list to edit it; user presets can also be new entries.";
            GUI.color = SlopWidgets.Dim;
            float y = r.y + SlopWidgets.RowH + SlopWidgets.GapXS;
            float h = Text.CalcHeight(caption, r.width);
            Widgets.Label(new Rect(r.x, y, r.width, h), caption);
            GUI.color = Color.white;
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
            var system = all.Where(p => p.Source == "system").ToList();
            var user = all.Where(p => p.Source != "system").ToList();
            float h = (system.Count + user.Count + 3) * SlopWidgets.RowH;
            var view = new Rect(0f, 0f, r.width - 18f, Mathf.Max(h, r.height));
            _listScroll.Begin(r, view);
            float y = 0f;
            y = DrawLibraryGroup(view, y, "System", system, p => p.Name,
                p => { _preset = p; _newEntry = false; });
            y = DrawLibraryGroup(view, y, "User", user, p => p.Name + (p.Source == "override" ? "  (override)" : ""),
                p => { _preset = p; _newEntry = false; });
            if (SlopWidgets.Button(new Rect(0f, y + SlopWidgets.GapS, view.width, SlopWidgets.BtnH),
                    "+ New preset", SlopWidgets.Btn.Ghost))
            {
                _preset = new PresetInfo { Name = "new-preset", Source = "user" };
                _newEntry = true;
            }
            _listScroll.End();
        }

        float DrawLibraryGroup<T>(Rect view, float y, string heading, List<T> items,
                                  Func<T, string> label, Action<T> pick)
        {
            SlopWidgets.SectionHeading(new Rect(0f, y, view.width, SlopWidgets.RowH), heading);
            y += SlopWidgets.RowH;
            foreach (var item in items)
            {
                var cell = new Rect(SlopWidgets.GapS, y, view.width - SlopWidgets.GapS, SlopWidgets.RowH);
                string name = label(item);
                bool selected = (item is PresetInfo p && p == _preset) ||
                                (item is CommandInfo c && c == _command);
                if (selected) Widgets.DrawBoxSolid(cell, SlopWidgets.RowOn);
                // A preset that hands the sandbox a road back out is dangerous even when it
                // is selected: the yellow stays on the name so the warning is visible in the
                // library, not only after opening its editor.
                bool dangerous = item is PresetInfo dangerousPreset && dangerousPreset.IsEscape;
                GUI.color = dangerous ? SlopWidgets.Warn
                    : selected ? SlopWidgets.Lead : SlopWidgets.Name;
                SlopWidgets.RowLabel(cell, name);
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(cell)) pick(item);
                y += SlopWidgets.RowH;
            }
            if (items.Count == 0)
            {
                GUI.color = SlopWidgets.Dim;
                Widgets.Label(new Rect(SlopWidgets.GapS, y, view.width, SlopWidgets.RowH), "(none)");
                GUI.color = Color.white;
                y += SlopWidgets.RowH;
            }
            return y + SlopWidgets.GapS;
        }

        void DrawCommandList(Rect r)
        {
            var all = SessionHub.Instance.Commands;
            var system = all.Where(c => c.Source == "system").ToList();
            var user = all.Where(c => c.Source != "system").ToList();
            float h = (system.Count + user.Count + 3) * SlopWidgets.RowH;
            var view = new Rect(0f, 0f, r.width - 18f, Mathf.Max(h, r.height));
            _listScroll.Begin(r, view);
            float y = 0f;
            y = DrawLibraryGroup(view, y, "System", system, c => c.Name,
                c => { _command = c; _newEntry = false; });
            y = DrawLibraryGroup(view, y, "User", user, c => c.Name + (c.Source == "override" ? "  (override)" : ""),
                c => { _command = c; _newEntry = false; });
            if (SlopWidgets.Button(new Rect(0f, y + SlopWidgets.GapS, view.width, SlopWidgets.BtnH),
                    "+ New command", SlopWidgets.Btn.Ghost))
            {
                _command = new CommandInfo { Name = "new-command", Source = "user" };
                _newEntry = true;
            }
            _listScroll.End();
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
            var view = new Rect(0f, 0f, r.width - 18f, Mathf.Max(1500f, r.height));
            _editorScroll.Begin(r, view);
            float y = 0f;
            // Put the cost before the identity and all the fields. A warning at the bottom is
            // something a long editor makes the player discover after deciding to use it.
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
            y = EditorField(view, y, "Name", "preset.name", p.Name, _newEntry, v => p.Name = v);
            y = EditorField(view, y, "Category", "preset.category", p.Category, editable, v => p.Category = v);
            y = EditorArea(view, y, "Description", "preset.description", p.Description, editable, 44f, v => p.Description = v);
            y = Rule(view.width, y + SlopWidgets.GapXS);
            y = EditorList(view, y, "Read-only binds", "preset.ro", p.Ro, editable);
            y = EditorList(view, y, "Read-write binds", "preset.rw", p.Rw, editable);
            y = EditorList(view, y, "Device binds", "preset.dev", p.Dev, editable);
            y = Rule(view.width, y);
            y = EditorList(view, y, "Private paths", "preset.private", p.Private, editable);
            y = EditorList(view, y, "Seed paths", "preset.seed", p.Seed, editable);
            y = EditorList(view, y, "Skip paths", "preset.skip", p.Skip, editable);
            y = EditorList(view, y, "Shared files", "preset.shared", p.Shared, editable);
            y = Rule(view.width, y);
            y = EditorList(view, y, "Forwarded environment", "preset.env", p.Env, editable);
            y = EditorArea(view, y, "Set environment (KEY=VALUE)", "preset.setenv",
                SetenvLines(p), editable, 48f, v => { _setenvText = v; ParseSetenv(p); });
            y = EditorField(view, y, "Escape warning", "preset.escapes", p.Escapes, editable, v => p.Escapes = v);
            EditorButtons(view, y, editable, p.Source, "sandbox", p.Name,
                () => SessionHub.Instance.SavePreset(p, () => { _newEntry = false; _error = null; }, msg => _error = msg),
                () => Remove("sandbox", p.Name));
            _editorScroll.End();
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
            var view = new Rect(0f, 0f, r.width - 18f, Mathf.Max(900f, r.height));
            _editorScroll.Begin(r, view);
            float y = 0f;
            EditorTitle(view, ref y, c.Name, c.Source, editable, "command");
            y = EditorField(view, y, "Name", "command.name", c.Name, _newEntry, v => c.Name = v);
            y = EditorField(view, y, "Category", "command.category", c.Category, editable, v => c.Category = v);
            y = EditorArea(view, y, "Description", "command.description", c.Description, editable, 44f, v => c.Description = v);
            y = EditorArea(view, y, "Command line", "command.cmd", c.Cmd, editable, 52f, v => c.Cmd = v);
            y += SlopWidgets.GapS;
            SlopWidgets.SectionHeading(new Rect(0f, y, view.width, SlopWidgets.RowH), "Sandbox dependencies");
            y += SlopWidgets.RowH;
            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(0f, y, view.width, SlopWidgets.LineH), "These presets are added whenever this command runs.");
            GUI.color = Color.white;
            y += SlopWidgets.LineH + SlopWidgets.GapXS;
            foreach (var p in SessionHub.Instance.Presets)
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
            _editorScroll.End();
        }

        void EditorTitle(Rect view, ref float y, string name, string source, bool editable, string kind)
        {
            GUI.color = SlopWidgets.Lead;
            Widgets.Label(new Rect(0f, y, view.width, SlopWidgets.RowH), name + (source == "override" ? "  (override)" : ""));
            GUI.color = Color.white;
            y += SlopWidgets.RowH;
            GUI.color = source == "system" ? SlopWidgets.Faint : SlopWidgets.Yes;
            Widgets.Label(new Rect(0f, y, view.width, SlopWidgets.LineH), source == "system" ? "System preset (read-only)" : "User preset");
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
            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(0f, y, view.width, SlopWidgets.LineH), label);
            GUI.color = Color.white;
            y += SlopWidgets.LineH + SlopWidgets.GapXS;
            set(SlopWidgets.Field(new Rect(0f, y, view.width, SlopWidgets.FieldH), name, value, editable));
            return y + SlopWidgets.FieldH + SlopWidgets.GapS;
        }

        float EditorArea(Rect view, float y, string label, string name, string value, bool editable,
                         float height, Action<string> set)
        {
            GUI.color = SlopWidgets.Dim;
            Widgets.Label(new Rect(0f, y, view.width, SlopWidgets.LineH), label);
            GUI.color = Color.white;
            y += SlopWidgets.LineH + SlopWidgets.GapXS;
            set(SlopWidgets.Area(new Rect(0f, y, view.width, height), name, value, editable));
            return y + height + SlopWidgets.GapS;
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
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
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
            if (_tab == Tab.Global && foot.Right("Save", SlopWidgets.Btn.Primary, _loaded)) SaveGlobal();
            if (_error != null)
            {
                var was = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = SlopWidgets.Bad;
                Widgets.Label(foot.Rest(), _error);
                GUI.color = Color.white;
                Text.Anchor = was;
            }
        }

        void SaveGlobal()
        {
            _cfg.RoPaths = SlopConfig.Split(_roPaths);
            _cfg.RwPaths = SlopConfig.Split(_rwPaths);
            _cfg.PassEnv = SlopConfig.Split(_passEnv);
            SlopClient.Put("/api/config/values", _cfg.ToJson(), _ =>
            {
                _error = null;
                SlopOptions.Reread();
                SessionHub.Instance.Refresh();
                Messages.Message("SlopWorld: global sandbox settings saved.", MessageTypeDefOf.TaskCompletion, false);
            }, msg => _error = msg);
        }
    }
}
