using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // F1 opens a filtered command palette; all window and button actions, including nested selections, are registered here.
    public partial class CommandPalette : Window
    {
        const float Width = 520f;
        const float MaxH = 440f;
        const float Pad = UiWidgets.GapS;

        // The three heights this list is built from, off the font rather than written down:
        // an input is a field, a row is a line with room round it, and a group heading is a
        // tiny line. A figure here holds only for the face it was set against, and a row
        // shorter than its line loses the top and bottom of every label in the palette.
        static float InputH => UiWidgets.FieldH;
        static float RowH => UiWidgets.PaletteRowH;
        static float GroupH => UiWidgets.TinyRowH;
        const int RecentMax = 8;
        // What a hit found only in a command's id is docked, the name being what is read.
        const int IdCost = 80;

        string _input = "";
        string _filter = "";
        bool _focusInput = true;

        List<Hit> _matches = new List<Hit>();
        // How many of _matches are recently used (front of the list, no filter).
        int _recentInList;

        // Sub-mode: a command that needs a second selection transitions here.
        CommandDef _subCmd;
        string _subPrompt;
        string _subFilter = "";
        bool _subHasFilter;
        List<SubOption> _subOptions = new List<SubOption>();
        // _subOptions after the filter, rebuilt when it moves rather than per frame.
        List<SubHit> _subShown = new List<SubHit>();
        int _subIndex;
        readonly List<SubFrame> _subStack = new List<SubFrame>();

        readonly SmoothScroll _scroll = new SmoothScroll();
        readonly FieldLifetime _fieldLifetime = new FieldLifetime();
        int _selectedIndex;
        // Visible height of the list area, used by ScrollToSelection.
        float _listH;

        static readonly List<string> _recent = new List<string>();
        static bool _recentLoaded;

        enum Mode { Commands, Sub }
        Mode _mode = Mode.Commands;

        public CommandPalette()
        {
            doCloseX = false;
            doCloseButton = false;
            doWindowBackground = false;
            drawShadow = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            closeOnAccept = false;
            closeOnCancel = false;
            forcePause = false;
            layer = WindowLayer.Super;
            // PageUp/PageDown are vanilla map-zoom bindings. The palette owns those keys
            // while it is open, so stop the camera pass from consuming them first.
            preventCameraMotion = true;
            draggable = false;
            resizeable = false;

            LoadRecent();
            RebuildMatches();
        }

        protected override float Margin => 0f;

        public override Vector2 InitialSize =>
            new Vector2(Width, Mathf.Min(ContentHeight(), MaxH));

        protected override void SetInitialSizeAndPosition()
        {
            var size = InitialSize;
            windowRect = new Rect(
                (UI.screenWidth - size.x) / 2f,
                Mathf.Max(60f, UI.screenHeight * 0.08f),
                size.x, size.y);
        }

        public static void Toggle()
        {
            var open = Find.WindowStack.WindowOfType<CommandPalette>();
            if (open != null) { open.Close(); return; }

            SessionHub.Instance.SessionStore.Refresh();
            SessionHub.Instance.Catalog.RefreshProjects();
            SessionHub.Instance.Catalog.RefreshLibrary();
            SessionHub.Instance.Catalog.LoadPresets();

            Find.WindowStack.Add(new CommandPalette());
        }

        // --------------------------------------------------------------- command model

        sealed class CommandDef
        {
            public readonly string Id;
            public readonly string Label;
            public readonly string Group;
            public readonly Func<bool> Enabled;
            public readonly Func<List<SubOption>> SubAction;
            public readonly Action<string> Action;

            public CommandDef(string id, string label, string group, Action<string> action,
                Func<bool> enabled = null, Func<List<SubOption>> subAction = null)
            {
                Id = id;
                Label = label;
                Group = group;
                Enabled = enabled ?? (() => true);
                SubAction = subAction;
                Action = action;
            }

            public bool IsEnabled => Enabled();

            public static CommandDef ForAgent(string id, string label,
                Func<List<SubOption>> filter, Action<SessionInfo> action, Func<bool> enabled = null) =>
                For(id, label, "Agent", filter, v => SessionHub.Instance.Get(v), action, enabled);

            public static CommandDef ForProject(string id, string label,
                Func<List<SubOption>> filter, Action<ProjectInfo> action, Func<bool> enabled = null) =>
                For(id, label, "Project", filter, v => SessionHub.Instance.Project(v), action, enabled);

            public static CommandDef ForLibraryItem(string id, string label,
                Func<List<SubOption>> filter, Action<LibraryItemInfo> action, Func<bool> enabled = null) =>
                For(id, label, "Library", filter, v => SessionHub.Instance.LibraryItem(v), action, enabled);

            static CommandDef For<T>(string id, string label, string group,
                Func<List<SubOption>> filter, Func<string, T> resolve, Action<T> action,
                Func<bool> enabled)
                where T : class
            {
                return new CommandDef(id, label, group, value =>
                {
                    if (value == null) return;
                    var item = resolve(value);
                    if (item != null) action(item);
                }, enabled, filter);
            }
        }

        class SubOption
        {
            public string Label;
            public string Value;
            public bool Enabled = true;
            public Func<List<SubOption>> Children;
            public Action Select;
            // A checkbox row when it is set, and the state the box is in. Null for the sub
            // lists that pick one thing and are done, which is most of them.
            public bool? Checked;
        }

        class SubFrame
        {
            public List<SubOption> Options;
            public string Prompt;
            public string Filter;
            public bool HasFilter;
            public int Index;
        }

        // A row as the list draws it: the command, and the name with whatever the search
        // matched marked up. Built when the filter moves, not per frame.
        class Hit
        {
            public CommandDef Command;
            public string Label;
            public int Score;
        }

        class SubHit
        {
            public SubOption O;
            public string Label;
            public int Score;
        }


    }
}
