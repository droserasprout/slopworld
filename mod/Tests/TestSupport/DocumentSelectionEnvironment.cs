using System;
using System.Collections.Generic;
using UnityEngine;

namespace Verse
{
    public sealed class FloatMenuOption
    {
        public readonly string Label;
        public readonly Action action;
        public bool Disabled;
        public FloatMenuOption(string label, Action action) { Label = label; this.action = action; }
    }
}

namespace SlopWorld
{
    sealed class UiMenu
    {
        public readonly List<Verse.FloatMenuOption> Options;
        public UiMenu(List<Verse.FloatMenuOption> options) { Options = options; }
    }
    static partial class TerminalWindow
    {
        public static bool CanPasteClipboardToAgent => true;
        public static int SelectionPastes;
        public static UiMenu SelectionMenu;
        public static void PasteClipboardToAgent() { SelectionPastes++; }
        public static void OpenOverPane(UiMenu menu) { SelectionMenu = menu; }
    }
    static partial class UiTheme { public static Color Sel => Color.white; }
    static partial class Slab
    {
        public static void Fill(Rect rect, Color color) => EditorTrace.Record("selection", rect);
    }
}
