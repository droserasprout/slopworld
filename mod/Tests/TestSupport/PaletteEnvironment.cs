using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Verse
{
    public enum WindowLayer
    {
        Super,
    }

    public partial class Window
    {
        public bool doCloseX,
            doCloseButton,
            doWindowBackground,
            drawShadow,
            absorbInputAroundWindow,
            closeOnClickedOutside,
            closeOnAccept,
            forcePause,
            preventCameraMotion,
            draggable,
            resizeable;
        public WindowLayer layer;
        public Rect windowRect;
        public bool Closed;
        protected virtual float Margin => 0;
        public virtual Vector2 InitialSize => Vector2.zero;

        protected virtual void SetInitialSizeAndPosition() { }

        public void Close()
        {
            Closed = true;
            Find.WindowStack.Remove(this);
        }
    }

    static class PaletteRectExtensions
    {
        public static Rect ContractedBy(this Rect r, float x, float y) =>
            new Rect(r.x + x, r.y + y, r.width - 2 * x, r.height - 2 * y);
    }
}

namespace UnityEngine
{
    static partial class GUI
    {
        public static string FocusedControl;

        public static string GetNameOfFocusedControl() => FocusedControl;

        public static void FocusControl(string name) => FocusedControl = name;
    }
}

namespace SlopWorld
{
    static partial class UiTheme
    {
        public const float PaletteRowH = 24f,
            TinyRowH = 18f;

        public static float Wide(string text) => text.Length * 8;
    }

    static partial class UiText
    {
        public static string PaletteEdit;

        public static void FieldFrame(Rect r, bool focused) { }

        public static string BareField(Rect r, string name, string value) => PaletteEdit ?? value;
    }

    sealed partial class SessionHub
    {
        public LibraryItemInfo LibraryItem(string name) =>
            Catalog.Library.FirstOrDefault(x => x.Name == name);
    }

    // Creation and non-Git/task registration are host inputs here. The production window,
    // execution, search and input code are linked; drawing is not simulated.
    static class CreationActions
    {
        public sealed class Entry
        {
            public static readonly Entry Separator = new Entry();
            public bool Enabled = true;
            public string Label = "";
            public Action Select = null;
            public Func<List<Entry>> Children = null;
        }
    }

    public partial class CommandPalette
    {
        static readonly List<CommandDef> CommandTable = new List<CommandDef>();
    }
}

namespace SlopWorld
{
    // Record the final view/action handoff; candidate lists and command delegates are production code.
    static class GitView
    {
        public static readonly List<(string Action, string Scope)> Calls =
            new List<(string, string)>();

        public static void DiffAll(string scope) => Calls.Add(("diff", scope));

        public static void StageAll(string scope) => Calls.Add(("stage", scope));

        public static void UnstageAll(string scope) => Calls.Add(("unstage", scope));

        public static void CommitStaged(string scope) => Calls.Add(("commit", scope));
    }

    static class TaskActions
    {
        public static TaskInfo Canceled,
            Removed;

        public static void CancelTask(TaskInfo task) => Canceled = task;

        public static void RemoveTask(TaskInfo task) => Removed = task;
    }

    static class TasksView
    {
        public static int FiltersOpened;

        public static void OpenFilterMenu() => FiltersOpened++;
    }
}
