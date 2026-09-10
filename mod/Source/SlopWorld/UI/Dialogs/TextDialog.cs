using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared single-line form contract: title, optional note, field, error row, and footer
    // spacing are common; validation, field meaning, and button actions stay with the caller.
    public static class TextDialog
    {
        public static string Draw(Rect rect, string title, string note, string fieldName,
                                  string value, string error, float noteHeight = -1f,
                                  Rect? titleRect = null)
        {
            using (WidgetState.Save())
            {
                UiLayout.Title(titleRect ?? rect, title);
                float y = rect.y + UiTheme.HeaderH + UiTheme.GapM;
                if (!string.IsNullOrEmpty(note))
                {
                    float h = noteHeight >= 0f ? noteHeight : UiTheme.RowH;
                    GUI.color = UiTheme.Dim;
                    UiText.RowLabel(new Rect(rect.x, y, rect.width, h), note);
                    y += h + UiTheme.GapS;
                }

                var field = new Rect(rect.x, y, rect.width, UiTheme.FieldH);
                string result = UiText.Field(field, fieldName, value ?? "");
                if (!string.IsNullOrEmpty(error))
                {
                    GUI.color = UiTheme.Bad;
                    UiText.RowLabel(new Rect(rect.x, field.yMax + UiTheme.GapXS,
                        rect.width, UiTheme.RowH), error);
                }
                return result;
            }
        }

        public static UiLayout.Bar Footer(Rect rect) =>
            new UiLayout.Bar(UiLayout.FooterBar(rect));
    }
}
