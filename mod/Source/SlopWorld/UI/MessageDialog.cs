using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared message surface for dialogs whose body is wrapped text and a footer. Concrete
    // dialogs only decide their title, width, and action policy.
    public abstract class MessageDialog : UiWindow
    {
        protected readonly string Message;

        protected MessageDialog(string message)
        {
            Message = message ?? "";
        }

        protected abstract string DialogTitle { get; }
        protected abstract float DialogWidth { get; }

        public override Vector2 InitialSize => new Vector2(DialogWidth,
            4f * UiWidgets.GapM + UiWidgets.HeaderH +
            MessageHeight(Message, DialogWidth - 2f * UiWidgets.GapM) + UiWidgets.BtnH);

        protected override bool Closable => false;

        protected override void DoBody(Rect rect)
        {
            UiWidgets.Title(rect, DialogTitle);

            float messageY = rect.y + UiWidgets.HeaderH + UiWidgets.GapM;
            float messageH = MessageHeight(Message, rect.width);
            var message = new Rect(rect.x, messageY, rect.width, messageH);

            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = UiWidgets.Name;
                Widgets.Label(message, Message);
            }

            DrawActions(new UiWidgets.Bar(UiWidgets.FooterBar(rect)));
        }

        protected abstract void DrawActions(UiWidgets.Bar foot);
    }
}
