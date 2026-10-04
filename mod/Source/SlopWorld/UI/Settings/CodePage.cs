using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public sealed class CodePage : DaemonConfigPage
    {
        const string Sample = "// Rust strings, macros, and tmux formats\n"
            + "fn main() {\n"
            + "    let name: &str = \"#{session_name}\";\n"
            + "    let raw = r#\"a \"quoted\" string\"#;\n"
            + "    let count = 42;\n"
            + "    println!(\"{name}: {count} {raw}\");\n"
            + "}\n";
        readonly SettingsPreviewForm _form = new SettingsPreviewForm(300f);
        readonly List<string> _themes = new List<string>();
        string _command, _engine = "", _catalogError, _previewError;
        string _preview = Sample;
        string _parsedPreview;
        TerminalTheme _parsedTheme;
        List<SgrRun>[] _previewLines;
        bool _loading, _previewLoading, _disposed;
        int _catalogRequest, _previewRequest;
        bool _pagerCustom, _highlighterCustom;
        bool _restartAfterLoad, _savingCommands;
        GUIStyle _previewStyle, _fontSource;
        protected override bool DrawFieldsBeforeLoad => true;
        protected override string SavedMessage => "code reader settings saved.";
        protected override string SaveScope => "code appearance";
        CodeAppearanceDraft Appearance => CodeAppearanceDraft.For(S);
        protected override bool LocalDirty => Appearance.Dirty;
        protected override Action CaptureLocalSave() => Appearance.CaptureSave(settings => settings.Write());

        protected override bool PrepareSave(out string error)
        {
            _savingCommands = _configState.Dirty;
            error = null;
            return true;
        }
        static ModSettings S => ModEntry.Instance.settings;

        protected override void AfterLoad()
        {
            _pagerCustom = _highlighterCustom = false;
            Reload();
            if (_restartAfterLoad)
            {
                _restartAfterLoad = false;
                ReaderAppearance.OfferRestart();
            }
        }

        protected override void AfterDiscard()
        {
            _pagerCustom = _highlighterCustom = false;
            Appearance.Discard();
            Reload();
        }

        protected override void AfterSave()
        {
            if (_savingCommands)
            {
                _restartAfterLoad = true;
                Load();
            }
            else
            {
                Preview();
                ReaderAppearance.OfferRestart();
            }
        }

        public override void Dispose()
        {
            _disposed = true;
            ++_catalogRequest;
            ++_previewRequest;
            base.Dispose();
        }

        string SelectedCommand => (_loaded ? _cfg?.Highlighter : SessionHub.Instance.Config.Highlighter) ?? "";

        void Reload()
        {
            ++_previewRequest;
            _preview = Sample;
            _previewError = null;
            _previewLoading = false;
            _command = SelectedCommand;
            _engine = CodeHighlight.Engine(_command);
            _themes.Clear();
            _catalogError = null;
            _loading = true;
            int request = ++_catalogRequest;
            DaemonClient.Get<Wire.HighlightThemes>(WireProtocol.Routes.HighlightThemes + "?command=" + Uri.EscapeDataString(_command), result =>
            {
                if (_disposed || request != _catalogRequest) return;
                _loading = false;
                _engine = result.Engine;
                _themes.AddRange(result.Themes);
                if (_command.Trim() == "auto") Preview();
            }, error =>
            {
                if (_disposed || request != _catalogRequest) return;
                _loading = false;
                _catalogError = error;
            });
            // Resolve Auto's engine before choosing its profile-local theme.
            if (_command.Trim() != "auto") Preview();
        }

        void Preview()
        {
            _previewError = null;
            _preview = Sample;
            _previewLoading = !string.IsNullOrWhiteSpace(_command);
            int request = ++_previewRequest;
            if (!_previewLoading) return;
            DaemonClient.Post<Wire.TextResult>(WireProtocol.Routes.Highlight,
                new Wire.HighlightReq { Text = Sample, Language = "rs", Engine = _engine,
                    Theme = Appearance.Theme(_engine), Command = _command }, result =>
                {
                    if (_disposed || request != _previewRequest) return;
                    _previewLoading = false;
                    _preview = result.Text;
                }, error =>
                {
                    if (_disposed || request != _previewRequest) return;
                    _previewLoading = false;
                    _previewError = error;
                });
        }

        void SelectTheme(string theme)
        {
            if (Appearance.SelectTheme(_engine, theme)) Preview();
        }

        public override void Draw(Rect rect)
        {
            if (_command != SelectedCommand) Reload();
            base.Draw(rect);
        }

        protected override void DrawFields(Listing_Standard l)
        {
            UiLayout.SectionHeading(l, "Commands");
            if (_loaded && _cfg != null)
            {
                CommandPicker.Draw(l, "Pager", "commands.pager", _cfg.Pager, PagerCommands(),
                    _pagerCustom, value => _cfg.Pager = value, value => _pagerCustom = value,
                    defaultValue: _cfg.FactoryDefaults?.Pager,
                    selectedLabel: _cfg.Pager == "auto" ? AutoLabel(_cfg.AutoCommands?.Pager) : null);
                CommandPicker.Draw(l, "Syntax highlighter", "commands.highlighter", _cfg.Highlighter,
                    HighlighterCommands(), _highlighterCustom, value => _cfg.Highlighter = value,
                    value => _highlighterCustom = value, defaultValue: _cfg.FactoryDefaults?.Highlighter,
                    selectedLabel: _cfg.Highlighter == "auto" ? AutoHighlighterLabel() : null);
                if (_pagerCustom || _highlighterCustom)
                    UiLayout.Note(l, "Pager templates accept {file} and {line}. Highlighter templates use %s for the file path.");
                // Apply edits from this GUI pass; Draw checks changes made before the pass.
                if (_command != SelectedCommand) Reload();
            }
            else UiLayout.Note(l, "Connect to the daemon to configure the pager and highlighter.");
            l.Gap(UiTheme.GapM);
            UiLayout.SectionHeading(l, "Display");
            Appearance.LineNumbers = UiControls.Checkbox(l, "Line numbers", Appearance.LineNumbers,
                "Show source line numbers in files and diffs. Custom file pagers manage their own numbering.");
            l.Gap(UiTheme.GapM);
            UiLayout.SectionHeading(l, "Colors");
            if (_loading) UiLayout.Note(l, "Loading installed themes…");
            else if (_catalogError != null) UiLayout.Note(l, _catalogError);
            else if (_engine.Length == 0)
                UiLayout.Note(l, _command.Trim() == "auto"
                    ? "No supported highlighter installed; using plain text."
                    : "Choose highlight, Pygments, or bat to select a theme here.");
            else
            {
                var choices = new List<FloatMenuOption> { new FloatMenuOption("Use command default", () => SelectTheme("")) };
                choices.AddRange(_themes
                    .Select(t => new FloatMenuOption(t, () => SelectTheme(t))));
                string theme = Appearance.Theme(_engine);
                UiControls.Select(l, "Theme (" + (_engine == "pygments" ? "Pygments" : _engine) + ")", theme.Length == 0 ? "Use command default" : theme, choices, out _);
            }
            if (_previewError != null) UiLayout.Note(l, _previewError);
        }

        protected override void DrawFieldsBody(Rect inner)
        {
            Text.Font = GameFont.Small;
            _ = TerminalFont.Style;
            float previewH = Mathf.Max(150f, TerminalFont.CellH * 9f);
            _form.Draw(inner, previewH, DrawForm, DrawPreviewBlock);
        }

        void DrawForm(Listing_Standard l)
        {
            DrawMetadataStatus(l);
            DrawFields(l);
        }

        void DrawPreviewBlock(Rect caption, Rect preview)
        {
            Text.Font = GameFont.Small;
            UiLayout.SectionHeading(caption, _previewLoading ? "Preview · Loading…" : "Preview");
            DrawPreview(preview);
        }

        void DrawPreview(Rect rect)
        {
            if (_parsedPreview != _preview || _parsedTheme != TerminalTheme.Current)
            {
                _parsedPreview = _preview;
                _parsedTheme = TerminalTheme.Current;
                _previewLines = _preview.Replace("\r\n", "\n").Split('\n').Select(Sgr.ParseLine).ToArray();
            }
            var source = TerminalFont.Style;
            if (_fontSource != source)
            {
                _fontSource = source;
                _previewStyle = new GUIStyle(source);
            }
            Color previousColor = GUI.color, previousContent = GUI.contentColor;
            GUI.color = GUI.contentColor = Color.white;
            Widgets.DrawBoxSolid(rect, TerminalTheme.Current.Bg);
            GUI.BeginClip(rect);
            try
            {
                float y = 6f;
                float cw = TerminalFont.CellWAtScreenScale(Prefs.UIScale);
                float gutter = Appearance.LineNumbers ? 4f * cw : 0f;
                int rows = _previewLines.Length;
                if (rows > 0 && _previewLines[rows - 1].Count == 0) rows--;
                for (int row = 0; row < rows; row++)
                {
                    if (Appearance.LineNumbers)
                    {
                        var numberColor = TerminalTheme.Current.Fg;
                        numberColor.a *= .6f;
                        _previewStyle.normal.textColor = numberColor;
                        GUI.Label(new Rect(6f, y, gutter, TerminalFont.CellH),
                            (row + 1).ToString(CultureInfo.CurrentCulture).PadLeft(3), _previewStyle);
                    }
                    foreach (var run in _previewLines[row])
                    {
                        var box = new Rect(6f + gutter + run.Col * cw, y, run.Columns * cw, TerminalFont.CellH);
                        if (run.HasBg) Widgets.DrawBoxSolid(box, run.Bg);
                        _previewStyle.normal.textColor = run.Fg;
                        GUI.Label(box, run.Text, _previewStyle);
                    }
                    y += TerminalFont.CellH;
                }
            }
            finally { GUI.EndClip(); GUI.color = previousColor; GUI.contentColor = previousContent; }
        }

        static string AutoLabel(string command)
        {
            if (command == null) return "Auto";
            string tool = System.IO.Path.GetFileName(SlopWorld.PagerCommands.Executable(command));
            return "Auto (" + (tool.Length == 0 ? "none" : tool == "pygmentize" ? "Pygments" : tool) + ")";
        }

        string AutoHighlighterLabel()
        {
            if (_command?.Trim() == "auto" && !_loading && _catalogError == null)
                return "Auto (" + (_engine.Length == 0 ? "none" : _engine == "pygments" ? "Pygments" : _engine) + ")";
            return AutoLabel(_cfg.AutoCommands?.Highlighter);
        }

        static List<CommandChoice> PagerCommands() => new List<CommandChoice>
        {
            new CommandChoice("Auto", "auto"),
            new CommandChoice("less", "less"),
            new CommandChoice("bat", "bat --paging=always"),
            new CommandChoice("more", "more"),
        };

        static List<CommandChoice> HighlighterCommands() => new List<CommandChoice>
        {
            new CommandChoice("Auto", "auto"),
            new CommandChoice("highlight", "highlight --out-format=xterm256"),
            new CommandChoice("Pygments", "pygmentize -f terminal256 -O style=monokai"),
            new CommandChoice("bat", "bat --color=always --style=plain --paging=never"),
            new CommandChoice("Off", ""),
        };
    }
}
