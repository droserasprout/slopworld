using System;
using System.Runtime.CompilerServices;

namespace SlopWorld
{
    // Retain unsaved appearance across page lifetimes without touching the live profile.
    public sealed class CodeAppearanceDraft
    {
        static readonly ConditionalWeakTable<ModSettings, CodeAppearanceDraft> Drafts =
            new ConditionalWeakTable<ModSettings, CodeAppearanceDraft>();
        readonly ModSettings _settings;
        public string HighlightTheme, PygmentsTheme, BatTheme;
        public bool LineNumbers;

        CodeAppearanceDraft(ModSettings settings) { _settings = settings; Discard(); }
        public static CodeAppearanceDraft For(ModSettings settings) =>
            Drafts.GetValue(settings, value => new CodeAppearanceDraft(value));

        public bool Dirty => HighlightTheme != (_settings.codeHighlightTheme ?? "") ||
            PygmentsTheme != (_settings.codePygmentsTheme ?? "") ||
            BatTheme != (_settings.codeBatTheme ?? "") || LineNumbers != _settings.codeLineNumbers;

        public string Theme(string engine)
        {
            switch (engine)
            {
                case "highlight": return HighlightTheme;
                case "pygments": return PygmentsTheme;
                case "bat": return BatTheme;
                default: return "";
            }
        }

        public bool SelectTheme(string engine, string theme)
        {
            if (Theme(engine) == theme) return false;
            switch (engine)
            {
                case "highlight": HighlightTheme = theme; break;
                case "pygments": PygmentsTheme = theme; break;
                case "bat": BatTheme = theme; break;
                default: return false;
            }
            return true;
        }

        public void Discard()
        {
            HighlightTheme = _settings.codeHighlightTheme ?? "";
            PygmentsTheme = _settings.codePygmentsTheme ?? "";
            BatTheme = _settings.codeBatTheme ?? "";
            LineNumbers = _settings.codeLineNumbers;
        }

        // Capture at submission; later edits remain in the draft if a daemon save is pending.
        public Action CaptureSave(Action<ModSettings> persist)
        {
            if (!Dirty) return null;
            var submitted = new Snapshot(HighlightTheme, PygmentsTheme, BatTheme, LineNumbers);
            return () =>
            {
                var previous = new Snapshot(_settings.codeHighlightTheme, _settings.codePygmentsTheme,
                    _settings.codeBatTheme, _settings.codeLineNumbers);
                submitted.Apply(_settings);
                try { persist(_settings); }
                catch { previous.Apply(_settings); throw; }
            };
        }

        sealed class Snapshot
        {
            readonly string _highlight, _pygments, _bat;
            readonly bool _numbers;
            public Snapshot(string highlight, string pygments, string bat, bool numbers)
            { _highlight = highlight; _pygments = pygments; _bat = bat; _numbers = numbers; }
            public void Apply(ModSettings settings)
            {
                settings.codeHighlightTheme = _highlight;
                settings.codePygmentsTheme = _pygments;
                settings.codeBatTheme = _bat;
                settings.codeLineNumbers = _numbers;
            }
        }
    }
}
