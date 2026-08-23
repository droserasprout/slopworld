using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared dialog frame: draw background/border on the window rect, keep `Margin = 0`, and
    // pad only contents. Draw the close control over the body so existing forms gain no header row.
    public abstract class SlopWindow : Window
    {
        protected SlopWindow()
        {
            doWindowBackground = false;
            doCloseX = false;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            draggable = true;
        }

        protected override float Margin => 0f;

        // A form group starts on the shared sixteen-pixel rhythm.
        protected virtual float Pad => SlopWidgets.GapM;

        // Whether the corner carries a cross. Off for a window that has a Cancel in its
        // footer and nothing else to dismiss.
        protected virtual bool Closable => true;

        const float CloseSize = 22f;

        protected static float MessageHeight(string text, float width)
        {
            var wasFont = Text.Font;
            var wasWrap = Text.WordWrap;
            try
            {
                Text.Font = GameFont.Small;
                Text.WordWrap = true;
                return Mathf.Max(SlopWidgets.LineHOf(GameFont.Small),
                    Text.CalcHeight(string.IsNullOrEmpty(text) ? " " : text, width));
            }
            finally
            {
                Text.WordWrap = wasWrap;
                Text.Font = wasFont;
            }
        }

        public override void DoWindowContents(Rect rect)
        {
            Slab.Box(rect, SlopWidgets.WindowBg, SlopWidgets.Edge);

            DoBody(rect.ContractedBy(Pad));

            // After the body: the corner is over the title's line, and the press has to be
            // taken in front of whatever the form drew there.
            if (Closable) DoClose(rect);
        }

        protected abstract void DoBody(Rect body);

        void DoClose(Rect rect)
        {
            var r = new Rect(rect.xMax - Pad - CloseSize, rect.y + Pad,
                CloseSize, CloseSize);

            if (SlopWidgets.IconButton(r, Icons.Cross, SlopWidgets.Dim,
                SlopWidgets.FieldPadX)) Close();
        }
    }
}
