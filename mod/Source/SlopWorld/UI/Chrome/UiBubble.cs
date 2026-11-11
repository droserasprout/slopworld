using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // One transient UI bubble across all surfaces. Features supply content and optional
    // lifetime guards; neither a colony component nor a simulation update owns visibility.
    internal static class UiBubble
    {
        static readonly UiBubbleSequence Sequence = new UiBubbleSequence();

        internal static void Show(
            string text,
            Vector2 anchor,
            int followingHints = 0,
            Func<string> next = null,
            Func<bool> alive = null
        )
        {
            Sequence.Show(text, anchor, Time.realtimeSinceStartup, followingHints, next, alive);
        }

        internal static void DrawAfterWindows()
        {
            if (Event.current.type != EventType.Repaint || Sequence.Text == null)
                return;
            if (!Sequence.Advance(Time.realtimeSinceStartup))
                return;

            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = UiTheme.Lead;
                const float margin = 8f;
                float width = Mathf.Min(320f, Mathf.Max(1f, UI.screenWidth - 2f * margin));
                float height = Text.CalcHeight(Sequence.Text, Mathf.Max(1f, width - 16f)) + 20f;
                var rect = UiBubblePlacement.Place(
                    Sequence.Anchor,
                    new Vector2(width, height),
                    new Vector2(UI.screenWidth, UI.screenHeight)
                );
                Slab.Box(rect, UiTheme.PopoverBg, UiTheme.Edge);
                Widgets.Label(rect.ContractedBy(8f), Sequence.Text);
            }
        }
    }
}
