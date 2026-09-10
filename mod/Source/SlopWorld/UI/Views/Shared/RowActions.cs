using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Geometry, drawing and hit testing for every right-aligned icon strip. Typed facades
    // below retain their distinct action enums while sharing this implementation.
    static class ActionStrip
    {
        public const float IconW = 14f;
        const float Gap = 4f;

        public static int Count(int mask, int[] order)
        {
            int count = 0;
            foreach (int flag in order) if ((mask & flag) != 0) count++;
            return count;
        }

        public static float Width(int mask, int[] order)
        {
            int n = Count(mask, order);
            return n == 0 ? 0f : n * IconW + (n - 1) * Gap;
        }

        public static float Left(float right, int mask, int[] order) =>
            right - Width(mask, order);

        public static float Draw(Rect row, float right, int mask, int[] order,
                                 System.Func<int, Texture2D> icon,
                                 System.Func<int, string> tip)
        {
            float left = Left(right, mask, order);
            float x = left;
            float y = row.y + (row.height - IconW) / 2f;
            using (WidgetState.Save())
            {
                foreach (int flag in order)
                {
                    if ((mask & flag) == 0) continue;
                    var rect = new Rect(x, y, IconW, IconW);
                    x += IconW + Gap;
                    bool over = RowChrome.Hover(rect, false, true,
                        RowHoverPolicy.OverlayAware);
                    if (over) TooltipHandler.TipRegion(rect, tip(flag));
                    if (Event.current.type == EventType.Repaint)
                    {
                        GUI.color = over ? Color.white : UiTheme.Dim;
                        GUI.DrawTexture(rect, icon(flag));
                    }
                }
            }
            return left;
        }

        public static int Hit(Rect row, float right, int mask, int[] order)
        {
            float x = Left(right, mask, order);
            float y = row.y + (row.height - IconW) / 2f;
            foreach (int flag in order)
            {
                if ((mask & flag) == 0) continue;
                var rect = new Rect(x, y, IconW, IconW);
                x += IconW + Gap;
                if (ColonistBarStrip.MouseOver(rect)) return flag;
            }
            return 0;
        }
    }

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
        public const float IconW = ActionStrip.IconW;
        static readonly int[] Order = { (int)RowAct.View, (int)RowAct.Edit, (int)RowAct.Diff };

        public static int Count(RowAct acts) => ActionStrip.Count((int)acts, Order);

        public static float Width(RowAct acts)
        {
            return ActionStrip.Width((int)acts, Order);
        }

        // Where the strip starts, which is where the label in front of it has to stop.
        public static float Left(float right, RowAct acts) =>
            ActionStrip.Left(right, (int)acts, Order);

        // The strip drawn, and its left edge given back. `right` is the row's own right edge
        // less whatever padding the tree uses; the row rect is here for the height alone,
        // every button being centred in it.
        public static float Draw(Rect row, float right, RowAct acts)
        {
            return ActionStrip.Draw(row, right, (int)acts, Order,
                flag => Tex((RowAct)flag), Tip);
        }

        static string Tip(int flag) => (RowAct)flag == RowAct.View ? "View this file."
            : (RowAct)flag == RowAct.Edit ? "Open this file in an editor."
            : "Show what this file has that the last commit does not.";

        // Which button a press landed on, or None for a press anywhere else on the row. Read
        // from the click pass with the row's rect in *its* coordinates - the geometry is the
        // row's own, so the same call answers inside the scroll view's group and outside it.
        public static RowAct Hit(Rect row, float right, RowAct acts)
        {
            return (RowAct)ActionStrip.Hit(row, right, (int)acts, Order);
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
        public const float IconW = ActionStrip.IconW;
        static readonly int[] Order = { (int)GroupAct.Diff, (int)GroupAct.Refresh };

        public static int Count(GroupAct acts) => ActionStrip.Count((int)acts, Order);

        public static float Width(GroupAct acts)
        {
            return ActionStrip.Width((int)acts, Order);
        }

        public static float Left(float right, GroupAct acts) =>
            ActionStrip.Left(right, (int)acts, Order);

        public static float Draw(Rect row, float right, GroupAct acts)
        {
            return ActionStrip.Draw(row, right, (int)acts, Order,
                flag => (GroupAct)flag == GroupAct.Diff ? Icons.Diff : Icons.Refresh,
                flag => (GroupAct)flag == GroupAct.Diff
                    ? "Show all changes in this project."
                    : "Read this working tree again.");
        }

        public static GroupAct Hit(Rect row, float right, GroupAct acts)
        {
            return (GroupAct)ActionStrip.Hit(row, right, (int)acts, Order);
        }
    }
}
