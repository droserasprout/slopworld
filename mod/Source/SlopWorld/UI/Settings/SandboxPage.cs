using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The daemon's sandbox and command libraries. Each is a master/detail page: there are
    // many fields in one definition, but only one preset or command is being edited at once.
    public partial class SandboxPage : IOptionPage
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
            _load.Load((ok, fail) => SessionHub.Instance.Catalog.LoadPresets(() => ok(true), fail), _ =>
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
            var body = UiWidgets.PageBody(rect);
            var inner = body.ContractedBy(UiWidgets.GapM);

            if (!_loaded)
            {
                UiWidgets.StatusLabel(inner, _error ?? "Waiting for the daemon...",
                    _error != null ? UiWidgets.Bad : UiWidgets.Dim);
            }
            else
            {
                DoSection(inner);
            }
            DoFooter(UiWidgets.FooterBar(rect));
        }

        void DoSection(Rect r)
        {
            bool presets = _section == Section.Presets;
            string heading = presets ? "Presets" : "Commands";
            string caption = presets
                ? "System presets are supplied by slopd. Copy one to the user list to edit it; user presets can also be new entries."
                : "Commands say what an agent runs and which presets it requires. Copy a system command to make a user override.";
            UiWidgets.SectionHeading(new Rect(r.x, r.y, r.width, UiWidgets.RowH), heading);

            float y = r.y + UiWidgets.RowH + UiWidgets.GapXS;
            float actionWidth = presets ? UiWidgets.BtnW("New user preset", 142f) : 0f;
            float captionWidth = presets
                ? Mathf.Max(0f, r.width - actionWidth - UiWidgets.GapS)
                : r.width;
            float captionHeight = UiWidgets.StatusLabelHeight(caption, captionWidth);
            float rowHeight = Mathf.Max(captionHeight, presets ? UiWidgets.BtnH : 0f);
            UiWidgets.StatusLabel(new Rect(r.x, y, captionWidth, captionHeight), caption,
                UiWidgets.Dim);
            if (presets && UiWidgets.Button(
                    new Rect(r.xMax - actionWidth, y, actionWidth, UiWidgets.BtnH),
                    "New user preset", UiWidgets.Btn.Primary))
                NewPreset();

            y += rowHeight + UiWidgets.GapS;
            var content = new UiLayoutRect(r.x, y, r.width, Mathf.Max(0f, r.yMax - y));
            var split = SandboxLayout.Arrange(content, UiWidgets.GapM);

            if (presets)
            {
                DrawPresetList(ToRect(split.List));
                DrawPresetEditor(ToRect(split.Editor));
            }
            else
            {
                DrawCommandList(ToRect(split.List));
                DrawCommandEditor(ToRect(split.Editor));
            }
        }

        static Rect ToRect(UiLayoutRect r) => new Rect(r.X, r.Y, r.Width, r.Height);

    }
}
