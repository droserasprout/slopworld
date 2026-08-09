using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What a row of a tree can be asked to do to the file it names. An enum of bits rather
    // than a list, so a row states what it offers without allocating one a frame: the three
    // are drawn in this order, left to right, wherever they are drawn.
    [System.Flags]
    public enum RowAct
    {
        None = 0,
        View = 1,
        Edit = 2,
        Diff = 4,
    }

    // The little strip of buttons a row grows under the mouse, in both trees ([FilesView],
    // [GitView]) and to the same geometry - the same three errands the right-click menus have
    // carried all along, put where the eye already is.
    //
    // Only under the mouse, and this is the whole of why it is worth having: the icons sit
    // where the row's own tail sits - nothing in the files view, the mark and the counts in
    // the git view - so a row that is not hovered reads exactly as it did. What the strip
    // costs is the end of a long name, and only while the mouse is on that one row.
    //
    // Laid out from the right for the reason the git view's figures are: three buttons under
    // one another down the column, whatever the names in front of them do.
    //
    // Drawn by hand rather than through `Widgets.ButtonImage`, because both trees take their
    // clicks in a second pass after the whole tree is laid out and outside the scroll view's
    // group - a button that answered during the draw would fire *and* let the row's own
    // MouseDown through, which is two things done for one press.
    public static class RowActions
    {
        public const float IconW = 14f;
        const float Gap = 4f;

        public static int Count(RowAct acts) =>
            ((acts & RowAct.View) != 0 ? 1 : 0) +
            ((acts & RowAct.Edit) != 0 ? 1 : 0) +
            ((acts & RowAct.Diff) != 0 ? 1 : 0);

        public static float Width(RowAct acts)
        {
            int n = Count(acts);
            return n == 0 ? 0f : n * IconW + (n - 1) * Gap;
        }

        // Where the strip starts, which is where the label in front of it has to stop.
        public static float Left(float right, RowAct acts) => right - Width(acts);

        // The strip drawn, and its left edge given back. `right` is the row's own right edge
        // less whatever padding the tree uses; the row rect is here for the height alone,
        // every button being centred in it.
        public static float Draw(Rect row, float right, RowAct acts)
        {
            if (acts == RowAct.None) return right;

            float x = Left(right, acts);
            float y = row.y + (row.height - IconW) / 2f;

            Draw(ref x, y, acts, RowAct.View, Icons.View, "View this file in a pager.");
            Draw(ref x, y, acts, RowAct.Edit, Icons.Edit, "Open this file in an editor.");
            Draw(ref x, y, acts, RowAct.Diff, Icons.Diff,
                "Show what this file has that the last commit does not.");

            GUI.color = Color.white;
            return Left(right, acts);
        }

        static void Draw(ref float x, float y, RowAct acts, RowAct which, Texture2D icon,
                         string tip)
        {
            if ((acts & which) == 0) return;

            var r = new Rect(x, y, IconW, IconW);
            x += IconW + Gap;

            bool on = ColonistBarStrip.MouseOver(r);
            if (on)
            {
                Widgets.DrawHighlight(r);
                // The row carries a tooltip of its own in the git view; this one is registered
                // where the button is and the row's is declined while it stands, so the two
                // are never stacked over each other.
                TooltipHandler.TipRegion(r, tip);
            }

            GUI.color = on ? Color.white : SlopWidgets.Dim;
            GUI.DrawTexture(r, icon);
        }

        // Which button a press landed on, or None for a press anywhere else on the row. Read
        // from the click pass with the row's rect in *its* coordinates - the geometry is the
        // row's own, so the same call answers inside the scroll view's group and outside it.
        public static RowAct Hit(Rect row, float right, RowAct acts)
        {
            if (acts == RowAct.None) return RowAct.None;

            float x = Left(right, acts);
            float y = row.y + (row.height - IconW) / 2f;

            if (Hit(ref x, y, acts, RowAct.View)) return RowAct.View;
            if (Hit(ref x, y, acts, RowAct.Edit)) return RowAct.Edit;
            if (Hit(ref x, y, acts, RowAct.Diff)) return RowAct.Diff;
            return RowAct.None;
        }

        static bool Hit(ref float x, float y, RowAct acts, RowAct which)
        {
            if ((acts & which) == 0) return false;
            var r = new Rect(x, y, IconW, IconW);
            x += IconW + Gap;
            return ColonistBarStrip.MouseOver(r);
        }

        // The same three said about a session rather than a row: what this ephemeral agent is,
        // read off the command it was given. The agents view draws the mark in front of a
        // ghost's name, which is the one place in that column where a row is a file being read
        // rather than an agent working.
        //
        // Off the command and not the session's name: a name is what the daemon made of the
        // label it was handed, and the label is only ever a hint.
        public static RowAct Of(SessionInfo info)
        {
            string cmd = (info?.Cmd ?? "").TrimStart();
            if (cmd.Length == 0) return RowAct.None;

            if (cmd.StartsWith("less")) return RowAct.View;
            if (cmd.StartsWith("micro")) return RowAct.Edit;
            // The git view's diff, which is a whole `git -C ... --paginate diff` line and the
            // only git this half ever runs in an errand.
            if (cmd.StartsWith("git ") && cmd.Contains(" diff")) return RowAct.Diff;
            return RowAct.None;
        }

        public static Texture2D Tex(RowAct act)
        {
            switch (act)
            {
                case RowAct.View: return Icons.View;
                case RowAct.Edit: return Icons.Edit;
                case RowAct.Diff: return Icons.Diff;
                default: return null;
            }
        }
    }
}
