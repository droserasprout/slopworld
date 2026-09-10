using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    static class GUI { public static Color color; }
}

namespace Verse
{
    static partial class Text
    {
        public static float CalcHeight(string text, float width) =>
            13f * (float)Math.Ceiling(Math.Max(1, text.Length) * 7f / Math.Max(1f, width));
    }
    static class TooltipHandler { public static void TipRegion(Rect r, string text) { } }
    static class Find { public static readonly List<object> WindowStack = new List<object>(); }
}

namespace SlopWorld
{
    // Record the real editor's control geometry without Unity or a running game.
    static class EditorTrace
    {
        public static readonly List<(string Name, Rect Rect)> Draws = new List<(string, Rect)>();
        public static string EditValue;
        public static void Record(string name, Rect r) => Draws.Add((name, r));
    }

    static partial class UiTheme
    {
        public const float RowH = 24f, LineH = 13f, FieldH = 22f, BtnH = 24f;
        public const float GapXS = 4f, GapS = 8f, GapM = 16f, ScrollbarW = 12f;
        public const float FieldPadX = 6f, FieldPadY = 2f;
        public static readonly Color Dim = Color.white, Warn = Color.white, Global = Color.white,
            Lead = Color.white, Name = Color.white, Faint = Color.white, Yes = Color.white,
            Edge = Color.white, Bad = Color.white;
        public enum Btn { Default, Primary, Danger, Ghost }
    }

    static class UiText
    {
        public static float StatusLabelHeight(string text, float width) => Verse.Text.CalcHeight(text, width);
        public static void StatusLabel(Rect r, string text, Color color) => EditorTrace.Record(text, r);
        public static void RowLabel(Rect r, string text) => EditorTrace.Record(text, r);
        public static string Field(Rect r, string name, string value, bool on)
        {
            EditorTrace.Record(name, r);
            return on ? EditorTrace.EditValue ?? value : value;
        }
        public static string Area(Rect r, string name, string value, bool on) => Field(r, name, value, on);
    }

    static class UiButtons
    {
        public static bool RowButton(Rect r) => false;
        public static bool Button(Rect r, string label, UiTheme.Btn kind = UiTheme.Btn.Default)
        {
            EditorTrace.Record(label, r);
            return false;
        }
    }

    static partial class UiLayout
    {
        public static void SectionHeading(Rect r, string text) => EditorTrace.Record(text, r);
        public struct Bar
        {
            readonly Rect _rect;
            public Bar(Rect rect) { _rect = rect; }
            public bool Left(string label, UiTheme.Btn kind) => false;
            public Rect Rest() => _rect;
        }
    }

    sealed class SelectorOption
    {
        public SelectorOption(string label, Action choose, bool enabled) { }
    }
    static class UiControls
    {
        public static bool Checkbox(Rect r, string name, bool on, string tip, bool locked, bool warn)
        {
            EditorTrace.Record("check:" + name, r);
            return on;
        }
        public static void Select(Rect r, string label, string value, SelectorOption[] options,
                                  out Rect box, bool on)
        {
            box = r;
            EditorTrace.Record(label, r);
        }
    }
    enum RowHoverPolicy { OverlayAware }
    static class RowChrome
    {
        public static void Hover(Rect r, bool selected, bool on, RowHoverPolicy policy) { }
    }
    static class Slab
    {
        public static void Hairline(Rect r, Color color) => EditorTrace.Record("rule", r);
    }
    sealed class SmoothScroll : IDisposable
    {
        public IDisposable Scope(Rect frame, Rect view) { EditorTrace.Record("scroll", view); return this; }
        public void Dispose() { }
    }
    static class ConfirmDialog
    {
        public static object Create(string text, Action action) => throw new InvalidOperationException(text);
    }
    sealed partial class SessionHub
    {
        public readonly HubCatalog Catalog = new HubCatalog(() => { });
        public List<PresetInfo> Presets => Catalog.Presets;
        public List<CommandInfo> Commands => Catalog.Commands;
    }

    public partial class SandboxPage
    {
        readonly SmoothScroll _listScroll = new SmoothScroll(), _editorScroll = new SmoothScroll();
        PresetInfo _preset;
        CommandInfo _command;
        bool _newEntry;
        string _error;
        void Load() => throw new InvalidOperationException("unexpected load");
        void NewCommand() => throw new InvalidOperationException("unexpected new command");
        public float TestPreset(PresetInfo p, float width, bool draw, bool isNew = false)
        {
            _newEntry = isNew;
            return DrawPresetFields(new Rect(0f, 0f, width, 0f), 0f, p, isNew || p.Source != "system", draw);
        }
        public float TestCommand(CommandInfo c, float width, bool draw)
        {
            return DrawCommandFields(new Rect(0f, 0f, width, 0f), c, c.Source != "system", draw);
        }
        public void TestPresetHost(PresetInfo p, float width)
        {
            _preset = p;
            DrawPresetEditor(new Rect(0f, 0f, width + UiTheme.ScrollbarW, 10f));
        }
        public void TestCommandHost(CommandInfo c, float width)
        {
            _command = c;
            DrawCommandEditor(new Rect(0f, 0f, width + UiTheme.ScrollbarW, 10f));
        }
    }
}
