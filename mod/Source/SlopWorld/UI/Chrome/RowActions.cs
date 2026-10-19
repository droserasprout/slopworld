using System;
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
            foreach (int flag in order)
                if ((mask & flag) != 0)
                    count++;
            return count;
        }

        public static float Width(int mask, int[] order)
        {
            // Include half a gap at either end so the label reserves the entire target.
            return Count(mask, order) * (IconW + Gap);
        }

        public static float Left(float right, int mask, int[] order) => right - Width(mask, order);

        static Rect ButtonRect(Rect row, float x)
        {
            float left = Mathf.Max(row.x, x);
            float right = Mathf.Min(row.xMax, x + IconW + Gap);
            return new Rect(left, row.y, Mathf.Max(0f, right - left), row.height);
        }

        public static float Draw(
            Rect row,
            float right,
            int mask,
            int[] order,
            System.Func<int, Texture2D> icon,
            System.Func<int, string> tip
        )
        {
            float left = Left(right, mask, order);
            float x = left;
            float y = row.y + (row.height - IconW) / 2f;
            using (WidgetState.Save())
            {
                foreach (int flag in order)
                {
                    if ((mask & flag) == 0)
                        continue;
                    var target = ButtonRect(row, x);
                    var rect = new Rect(x + Gap / 2f, y, IconW, IconW);
                    x += IconW + Gap;
                    bool over = RowChrome.Hover(target, false, true, RowHoverPolicy.OverlayAware);
                    if (over)
                        TooltipHandler.TipRegion(target, tip(flag));
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
            foreach (int flag in order)
            {
                if ((mask & flag) == 0)
                    continue;
                var rect = ButtonRect(row, x);
                x += IconW + Gap;
                if (rect.Contains(Event.current.mousePosition))
                    return flag;
            }
            return 0;
        }
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

    // Hover actions shared by Files, Git and Search replace the right-hand row tail.
    // Files, Git and Search dispatch hits in a separate click pass using the drawn geometry.
    // Manual drawing leaves each owner to consume a click once.
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
        // less whatever padding the tree uses. Targets fill the row height and split
        // the gaps between icons; the glyphs remain centered at their original size.
        public static float Draw(Rect row, float right, RowAct acts)
        {
            return ActionStrip.Draw(row, right, (int)acts, Order, flag => Tex((RowAct)flag), Tip);
        }

        static string Tip(int flag) =>
            (RowAct)flag == RowAct.View ? "View this file."
            : (RowAct)flag == RowAct.Edit ? "Open this file in an editor."
            : "Show what this file has that the last commit does not.";

        // Which button a press landed on, or None for a press anywhere else on the row. Read from
        // the click pass with the row's rect in *its* coordinates - the geometry is the row's own.
        // Therefore, the same call answers inside the scroll view's group and outside it.
        public static RowAct Hit(Rect row, float right, RowAct acts)
        {
            return (RowAct)ActionStrip.Hit(row, right, (int)acts, Order);
        }

        public static Texture2D Tex(RowAct act)
        {
            switch (act)
            {
                case RowAct.View:
                    return Icons.View;
                case RowAct.Edit:
                    return Icons.Edit;
                case RowAct.Diff:
                    return Icons.Diff;
                default:
                    return null;
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
            return ActionStrip.Draw(
                row,
                right,
                (int)acts,
                Order,
                flag => (GroupAct)flag == GroupAct.Diff ? Icons.Diff : Icons.Refresh,
                flag =>
                    (GroupAct)flag == GroupAct.Diff
                        ? "Show all changes in this project."
                        : "Read this working tree again."
            );
        }

        public static GroupAct Hit(Rect row, float right, GroupAct acts)
        {
            return (GroupAct)ActionStrip.Hit(row, right, (int)acts, Order);
        }
    }
}
