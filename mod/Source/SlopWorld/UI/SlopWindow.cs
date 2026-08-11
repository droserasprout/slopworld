using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The frame every dialog here wears. Five windows used to take vanilla's - its tiled
    // background, its border and its close cross - which is the one piece of chrome a player
    // sees before they have read a word of the form inside it.
    //
    // The frame is drawn on the window's **own** rect, so the margin is taken here rather
    // than by `Window.Margin`: vanilla's margin is not padding, it opens a GUI group and
    // hands `DoWindowContents` a rect translated inside it (see gotchas), which would leave
    // the background short on every side and the game showing through the gap. `Margin` is
    // nought and [Pad] is the shared sixteen, applied to the body alone, so every form keeps
    // the same rectangular frame rhythm.
    //
    // The close button is drawn **over** the body rather than in a bar of its own. A titled
    // header would be the tidier thing and it would move every row in five forms down by its
    // own height - the prompt box at the foot of `EditShortcutDialog` is already the field
    // that gets squeezed. Every title here is left-aligned, so the corner is free.
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
