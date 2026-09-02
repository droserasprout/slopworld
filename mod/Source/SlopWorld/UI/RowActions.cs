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

    // Actions for a foldable content-tree group. Kept separate from RowAct because these
    // buttons belong to project headings rather than to files, and their refresh icon does
    // not describe an ephemeral terminal session.
    [System.Flags]
    public enum GroupAct
    {
        None = 0,
        Diff = 1,
        Refresh = 2,
    }

    // Hover actions shared by FilesView and GitView. They replace the row's right-hand
    // tail temporarily and are right-aligned so the column stays fixed. Drawing is manual:
    // both trees handle clicks in a second pass outside the scroll group, and answering in
    // `Widgets.ButtonImage` would also pass the same MouseDown to the row.
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

            Draw(ref x, y, acts, RowAct.View, Icons.View, "View this file.");
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

            bool on = SlopWidgets.HoverRow(r);
            if (on)
            {
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

        // Classify session actions from `Cmd`/`Agent`: ephemeral commands may be in `Cmd`,
        // configured commands in `Agent`. After restart, adopted sessions lack metadata, so
        // view/edit/diff name prefixes are the fallback for routing them to Files/Git.
        public static RowAct Of(SessionInfo info)
        {
            // An adopted ephemeral session has no saved command, so the daemon resolves its
            // blank config through the default agent command. Its generated action prefix is
            // the authoritative identity in that case, and must win before that fallback.
            if (info?.Ephemeral == true)
            {
                RowAct named = ByName(info.Name);
                if (named != RowAct.None) return named;
            }

            string cmd = (info?.Cmd ?? "").TrimStart();
            if (cmd.Length == 0) cmd = (info?.Agent ?? "").TrimStart();
            if (cmd.Length == 0)
            {
                return ByName(info?.Name);
            }

            if (Pager.IsPagerCommand(cmd))
                return RowAct.View;
            if (Pager.IsEditorCommand(cmd)) return RowAct.Edit;
            // The git view's diff, which is a whole `git -C ... --paginate diff` line and the
            // only git this half ever runs in an errand.
            if (cmd.StartsWith("git ") && cmd.Contains(" diff")) return RowAct.Diff;
            return RowAct.None;
        }

        static RowAct ByName(string name)
        {
            name = (name ?? "").TrimStart();
            if (name.StartsWith("view-", System.StringComparison.Ordinal)
                || name.StartsWith("search-", System.StringComparison.Ordinal))
                return RowAct.View;
            if (name.StartsWith("edit-", System.StringComparison.Ordinal)) return RowAct.Edit;
            if (name.StartsWith("diff-", System.StringComparison.Ordinal)) return RowAct.Diff;
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

    // Floating actions for project headings in content trees. Like RowActions, the strip
    // replaces the heading's tail only while its row is hovered and is handled in the second
    // pass so a button click cannot also fold the group.
    public static class GroupActions
    {
        public const float IconW = 14f;
        const float Gap = 4f;

        public static int Count(GroupAct acts) =>
            ((acts & GroupAct.Diff) != 0 ? 1 : 0) +
            ((acts & GroupAct.Refresh) != 0 ? 1 : 0);

        public static float Width(GroupAct acts)
        {
            int n = Count(acts);
            return n == 0 ? 0f : n * IconW + (n - 1) * Gap;
        }

        public static float Left(float right, GroupAct acts) => right - Width(acts);

        public static float Draw(Rect row, float right, GroupAct acts)
        {
            if (acts == GroupAct.None) return right;

            float x = Left(right, acts);
            float y = row.y + (row.height - IconW) / 2f;
            Draw(ref x, y, acts, GroupAct.Diff, Icons.Diff,
                "Show all changes in this project.");
            Draw(ref x, y, acts, GroupAct.Refresh, Icons.Refresh,
                "Read this working tree again.");

            GUI.color = Color.white;
            return Left(right, acts);
        }

        static void Draw(ref float x, float y, GroupAct acts, GroupAct which,
                         Texture2D icon, string tip)
        {
            if ((acts & which) == 0) return;

            var r = new Rect(x, y, IconW, IconW);
            x += IconW + Gap;

            bool on = SlopWidgets.HoverRow(r);
            if (on) TooltipHandler.TipRegion(r, tip);

            GUI.color = on ? Color.white : SlopWidgets.Dim;
            GUI.DrawTexture(r, icon);
        }

        public static GroupAct Hit(Rect row, float right, GroupAct acts)
        {
            if (acts == GroupAct.None) return GroupAct.None;

            float x = Left(right, acts);
            float y = row.y + (row.height - IconW) / 2f;
            if (Hit(ref x, y, acts, GroupAct.Diff)) return GroupAct.Diff;
            if (Hit(ref x, y, acts, GroupAct.Refresh)) return GroupAct.Refresh;
            return GroupAct.None;
        }

        static bool Hit(ref float x, float y, GroupAct acts, GroupAct which)
        {
            if ((acts & which) == 0) return false;
            var r = new Rect(x, y, IconW, IconW);
            x += IconW + Gap;
            return ColonistBarStrip.MouseOver(r);
        }
    }
}
