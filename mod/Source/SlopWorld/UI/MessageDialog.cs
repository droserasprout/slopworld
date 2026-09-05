using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared message surface for dialogs whose body is wrapped text and a footer. Concrete
    // dialogs only decide their title, width, and action policy.
    public abstract class MessageDialog : SlopWindow
    {
        protected readonly string Message;

        protected MessageDialog(string message)
        {
            Message = message ?? "";
        }

        protected abstract string DialogTitle { get; }
        protected abstract float DialogWidth { get; }

        public override Vector2 InitialSize => new Vector2(DialogWidth,
            4f * SlopWidgets.GapM + SlopWidgets.HeaderH +
            MessageHeight(Message, DialogWidth - 2f * SlopWidgets.GapM) + SlopWidgets.BtnH);

        protected override bool Closable => false;

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, DialogTitle);

            float messageY = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapM;
            float messageH = MessageHeight(Message, rect.width);
            var message = new Rect(rect.x, messageY, rect.width, messageH);

            using (WidgetState.Save())
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.color = SlopWidgets.Name;
                Widgets.Label(message, Message);
            }

            DrawActions(new SlopWidgets.Bar(SlopWidgets.FooterBar(rect)));
        }

        protected abstract void DrawActions(SlopWidgets.Bar foot);
    }
}
