using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The chrome more than one window draws the same way, and the colours that mean the
    // same thing wherever they are drawn. A helper is here rather than on a base class
    // whenever the thing that wants it is not a list window - `ConfigPage` is a category
    // of the options menu and `FilesView` is a panel, and both draw these.
    //
    // Not a theme: `TerminalTheme` is the pane's, and a view that keeps a palette of its
    // own (`FilesView`, `AgentSidebar`, `TopBar`) is naming contrasts for one panel rather
    // than a meaning the rest of the mod shares. What is here is what the same intent was
    // typed out for in eight files, so the intent is the name.
    public static class SlopWidgets
    {
        // A second line about the thing on the first: a path under a name, a count beside
        // it, a note under a field.
        public static readonly Color Dim = new Color(0.65f, 0.66f, 0.68f);

        // Something that went wrong, said in text rather than in a message - the half of
        // an error that stays on screen.
        public static readonly Color Bad = new Color(0.95f, 0.45f, 0.45f);

        // Behind a text area, so an empty one still reads as a box to type in.
        public static readonly Color Well = new Color(0f, 0f, 0f, 0.25f);

        // The daemon behind all of this, up or down.
        static readonly Color Online = new Color(0.5f, 0.8f, 0.5f);
        static readonly Color Offline = new Color(0.9f, 0.5f, 0.5f);

        // A list with nothing in it, which is a thing being said rather than a row.
        public static readonly Color Empty = new Color(0.6f, 0.6f, 0.6f);

        // Behind a list row, under the hover.
        public static readonly Color RowBg = new Color(1f, 1f, 1f, 0.03f);

        // The one answer to an empty list that is not about what the list holds, so no
        // window states its own.
        public const string Unreachable =
            "Daemon unreachable. Is slopd running?  systemctl --user status slopd";

        // Every refusal the player is shown takes this road, so the prefix is written once
        // and a message that skipped it would be the one that did not look like ours.
        public static void Fail(string msg) =>
            Messages.Message($"SlopWorld: {msg}", MessageTypeDefOf.RejectInput, false);

        // Up means close it; down means open it, and whatever the window needs asked for
        // first. The factory rather than an instance, so nothing is built for a window
        // that turns out to be a close.
        public static void ToggleWindow<T>(Func<T> make) where T : Window
        {
            var open = Find.WindowStack.WindowOfType<T>();
            if (open != null) { open.Close(); return; }

            Find.WindowStack.Add(make());
        }

        // The window's name, and beside it the daemon this window is a view of. The status
        // line is laid out from the title's measured width rather than from a figure per
        // window: three of those had been nudged by hand to clear three different titles,
        // which is a thing to get wrong every time a title changes.
        public static void Header(Rect rect, string title, SessionHub hub)
        {
            Text.Font = GameFont.Medium;
            float w = Text.CalcSize(title).x;
            Widgets.Label(new Rect(rect.x, rect.y, 300f, 32f), title);
            Text.Font = GameFont.Small;

            GUI.color = hub.Online ? Online : Offline;
            Widgets.Label(new Rect(rect.x + w + 16f, rect.y + 8f, 400f, 24f),
                $"{SlopClient.BaseUrl} - {hub.Status}");
            GUI.color = Color.white;
        }

        // The fill and the hover behind one row of a list.
        public static void RowChrome(Rect r)
        {
            Widgets.DrawBoxSolid(r, RowBg);
            Widgets.DrawHighlightIfMouseover(r);
        }

        // One entry per line, which is how every list of binds here is edited. The floor is
        // on the box rather than on the rect, so a squeezed window ends up with boxes that
        // overlap rather than boxes with nothing typeable in them.
        public static string PathList(Rect r, string label, string text)
        {
            Widgets.Label(new Rect(r.x, r.y, r.width, 22f), label);
            var box = new Rect(r.x, r.y + 22f, r.width, Mathf.Max(r.height - 22f, 40f));
            Widgets.DrawBoxSolid(box, Well);
            return Widgets.TextArea(box.ContractedBy(4f), text);
        }

        // "claude" -> "claude-2", and a copy of that -> "claude-3" rather than "claude-2-2".
        // Suggested and not enforced - the daemon still refuses a collision, which is why
        // the search gives up rather than looping. `taken` is asked for rather than looked
        // up, agents and projects being two tables with one rule about names.
        public static string FreeName(string name, IEnumerable<string> taken, string fallback)
        {
            string stem = name ?? "";
            while (stem.Length > 0 && char.IsDigit(stem[stem.Length - 1]))
                stem = stem.Substring(0, stem.Length - 1);
            stem = stem.TrimEnd(' ', '-', '_');
            if (stem.Length == 0) stem = name ?? fallback;

            var used = taken.ToList();
            for (int n = 2; n <= 99; n++)
            {
                string candidate = stem + "-" + n;
                if (!used.Contains(candidate)) return candidate;
            }
            return stem;
        }
    }

    // Agents, projects and shortcuts are one window drawn three times: a title with the
    // daemon beside it, a scrolling list of fixed-height rows, and a row of buttons along
    // the bottom. What differs is the rows and what the buttons do, so that is what a
    // subclass says and the rest is here.
    //
    // Generic in the row rather than indexed, because every one of these walks a list the
    // hub owns and wants it typed on the way through.
    public abstract class SlopListWindow<T> : Window
    {
        Vector2 _scroll;

        protected SlopListWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(720f, 480f);

        protected abstract string Title { get; }

        // The pitch, not the height: a row is drawn 4px shorter so neighbours do not touch.
        protected abstract float RowH { get; }

        // What this window says when it has nothing to show and the daemon is *up*. The
        // other half of that answer is `SlopWidgets.Unreachable` and is nobody's to state.
        protected abstract string EmptyNote { get; }

        protected abstract IEnumerable<T> Rows { get; }

        protected abstract void DrawRow(Rect r, T item);

        protected abstract void DoFooter(Rect bar, SessionHub hub);

        public override void DoWindowContents(Rect rect)
        {
            var hub = SessionHub.Instance;

            SlopWidgets.Header(rect, Title, hub);

            float top = rect.y + 40f;
            DrawList(new Rect(rect.x, top, rect.width, rect.height - top - 40f), hub);

            DoFooter(new Rect(rect.x, rect.yMax - 32f, rect.width, 30f), hub);
        }

        void DrawList(Rect rect, SessionHub hub)
        {
            // Snapshotted, and once: the hub is pumped from a `Root.Update` postfix, so the
            // list a frame is sized from has to be the list that frame draws.
            var items = Rows.ToList();
            var view = new Rect(0f, 0f, rect.width - 18f, items.Count * RowH + 4f);

            Widgets.BeginScrollView(rect, ref _scroll, view);

            if (items.Count == 0)
            {
                GUI.color = SlopWidgets.Empty;
                Widgets.Label(new Rect(4f, 8f, view.width - 8f, 64f),
                    hub.Online ? EmptyNote : SlopWidgets.Unreachable);
                GUI.color = Color.white;
            }

            float y = 0f;
            foreach (var item in items)
            {
                DrawRow(new Rect(0f, y, view.width, RowH - 4f), item);
                y += RowH;
            }

            Widgets.EndScrollView();
        }
    }
}
