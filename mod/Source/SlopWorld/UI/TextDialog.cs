using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared single-line form contract: title, optional note, field, error row, and footer
    // spacing are common; validation, field meaning, and button actions stay with the caller.
    public static class TextDialog
    {
        public static string Draw(Rect rect, string title, string note, string fieldName,
                                  string value, string error, float noteHeight = -1f)
        {
            using (WidgetState.Save())
            {
                UiWidgets.Title(rect, title);
                float y = rect.y + UiWidgets.HeaderH + UiWidgets.GapM;
                if (!string.IsNullOrEmpty(note))
                {
                    float h = noteHeight >= 0f ? noteHeight : UiWidgets.RowH;
                    GUI.color = UiWidgets.Dim;
                    UiWidgets.RowLabel(new Rect(rect.x, y, rect.width, h), note);
                    y += h + UiWidgets.GapS;
                }

                var field = new Rect(rect.x, y, rect.width, UiWidgets.FieldH);
                string result = UiWidgets.Field(field, fieldName, value ?? "");
                if (!string.IsNullOrEmpty(error))
                {
                    GUI.color = UiWidgets.Bad;
                    UiWidgets.RowLabel(new Rect(rect.x, field.yMax + UiWidgets.GapXS,
                        rect.width, UiWidgets.RowH), error);
                }
                return result;
            }
        }

        public static UiWidgets.Bar Footer(Rect rect) =>
            new UiWidgets.Bar(UiWidgets.FooterBar(rect));
    }
}
