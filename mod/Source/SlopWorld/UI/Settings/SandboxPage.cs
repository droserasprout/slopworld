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
                if (_section == Section.Presets) DoPresets(inner);
                else DoCommands(inner);
            }
            DoFooter(UiWidgets.FooterBar(rect));
        }

        void DoPresets(Rect r)
        {
            UiWidgets.SectionHeading(new Rect(r.x, r.y, r.width, UiWidgets.RowH), "Presets");
            var caption = "System presets are supplied by slopd. Copy one to the user list to edit it; user presets can also be new entries.";
            float y = r.y + UiWidgets.RowH + UiWidgets.GapXS;
            float newW = UiWidgets.BtnW("New user preset", 142f);
            float captionW = r.width - newW - UiWidgets.GapS;
            float h = UiWidgets.StatusLabelHeight(caption, captionW);
            UiWidgets.StatusLabel(new Rect(r.x, y, captionW, h), caption, UiWidgets.Dim);
            if (UiWidgets.Button(new Rect(r.xMax - newW, y, newW, UiWidgets.BtnH),
                    "New user preset", UiWidgets.Btn.Primary))
                NewPreset();
            y += h + UiWidgets.GapS;
            var content = new Rect(r.x, y, r.width, r.yMax - y);
            float detailW = Mathf.Min(590f, content.width * .60f);
            float listW = content.width - detailW - UiWidgets.GapM;
            DrawPresetList(new Rect(content.x, content.y, listW, content.height));
            DrawPresetEditor(new Rect(content.x + listW + UiWidgets.GapM, content.y, detailW, content.height));
        }

        void DoCommands(Rect r)
        {
            UiWidgets.SectionHeading(new Rect(r.x, r.y, r.width, UiWidgets.RowH), "Commands");
            var caption = "Commands say what an agent runs and which presets it requires. Copy a system command to make a user override.";
            float y = r.y + UiWidgets.RowH + UiWidgets.GapXS;
            float h = UiWidgets.StatusLabelHeight(caption, r.width);
            UiWidgets.StatusLabel(new Rect(r.x, y, r.width, h), caption, UiWidgets.Dim);
            y += h + UiWidgets.GapS;
            var content = new Rect(r.x, y, r.width, r.yMax - y);
            float detailW = Mathf.Min(590f, content.width * .60f);
            float listW = content.width - detailW - UiWidgets.GapM;
            DrawCommandList(new Rect(content.x, content.y, listW, content.height));
            DrawCommandEditor(new Rect(content.x + listW + UiWidgets.GapM, content.y, detailW, content.height));
        }

    }
}
