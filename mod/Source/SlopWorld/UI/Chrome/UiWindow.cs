using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared dialog frame: draw background/border on the window rect, keep `Margin = 0`, and
    // pad only contents. Draw the close control over the body so existing forms gain no header row.
    public abstract class UiWindow : Window
    {
        protected UiWindow()
        {
            doWindowBackground = false;
            doCloseX = false;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnCancel = true;
            closeOnAccept = false;
            draggable = true;
        }

        Action _acceptAction;
        readonly FieldLifetime _fieldLifetime = new FieldLifetime();

        // Single-line forms can opt into the shared RimWorld accept binding without each
        // repeating the same event plumbing. Multiline editors leave this unset so Enter
        // remains a newline.
        protected void AcceptOnEnter(Action action)
        {
            _acceptAction = action;
            closeOnAccept = true;
        }

        protected override float Margin => 0f;

        // A form group starts on the shared sixteen-pixel rhythm.
        protected virtual float Pad => UiTheme.GapM;

        // Whether the corner carries a cross. Off for a window that has a Cancel in its
        // footer and nothing else to dismiss.
        protected virtual bool Closable => true;

        const float CloseSize = 22f;

        // Titles and captions share the close corner's horizontal lane. Keep their text out
        // of that lane without narrowing the form below it. RowLabel then truncates long names
        // at the edge where the close control begins.
        protected Rect TitleRect(Rect rect)
        {
            if (!Closable) return rect;
            return new Rect(rect.x, rect.y,
                Mathf.Max(0f, rect.width - CloseSize - UiTheme.GapS), rect.height);
        }

        protected static float MessageHeight(string text, float width)
            => UiText.StatusLabelHeight(text, width);

        public override void DoWindowContents(Rect rect)
        {
            using (FieldLifetimeScope.Push(_fieldLifetime))
            using (new FieldFocusScope(_fieldLifetime,
                Find.WindowStack == null || Find.WindowStack.GetsInput(this)))
            {
                // RimWorld's Accept binding normally covers Return, but keypad Enter is not
                // present in every platform's binding. Accepted dialogs should treat both keys
                // alike. Multiline editors leave closeOnAccept false so Enter remains a newline.
                var e = Event.current;
                if (closeOnAccept && e != null && e.type == EventType.KeyDown &&
                    e.keyCode == KeyCode.KeypadEnter)
                {
                    OnAcceptKeyPressed();
                    return;
                }

                Slab.Box(rect, UiTheme.WindowBg, UiTheme.Edge);

                DoBody(rect.ContractedBy(Pad));

                // After the body: the corner is over the title's line, and the press has to be
                // taken in front of whatever the form drew there.
                if (Closable) DoClose(rect);
            }
        }

        public override void OnAcceptKeyPressed()
        {
            _acceptAction?.Invoke();
            Event.current?.Use();
        }

        public override void PostClose()
        {
            _fieldLifetime.Cancel();
            base.PostClose();
            // Pickers live on the window stack beside their form, so closing the form does
            // not close a picker automatically. Remove it before the form disappears.
            UiMenu.CloseAll();
        }

        protected abstract void DoBody(Rect body);

        void DoClose(Rect rect)
        {
            var r = new Rect(rect.xMax - Pad - CloseSize, rect.y + Pad,
                CloseSize, CloseSize);

            if (UiLayout.IconButton(r, Icons.Cross, UiTheme.Dim,
                UiTheme.FieldPadX)) Close();
        }
    }
}
