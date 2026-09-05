using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shared single-line form contract: title, optional note, field, error row, and footer
    // spacing are common; validation, field meaning, and button actions stay with the caller.
    public static class SlopTextDialog
    {
        public static string Draw(Rect rect, string title, string note, string fieldName,
                                  string value, string error, float noteHeight = -1f)
        {
            using (WidgetState.Save())
            {
                SlopWidgets.Title(rect, title);
                float y = rect.y + SlopWidgets.HeaderH + SlopWidgets.GapM;
                if (!string.IsNullOrEmpty(note))
                {
                    float h = noteHeight >= 0f ? noteHeight : SlopWidgets.RowH;
                    GUI.color = SlopWidgets.Dim;
                    SlopWidgets.RowLabel(new Rect(rect.x, y, rect.width, h), note);
                    y += h + SlopWidgets.GapS;
                }

                var field = new Rect(rect.x, y, rect.width, SlopWidgets.FieldH);
                string result = SlopWidgets.Field(field, fieldName, value ?? "");
                if (!string.IsNullOrEmpty(error))
                {
                    GUI.color = SlopWidgets.Bad;
                    SlopWidgets.RowLabel(new Rect(rect.x, field.yMax + SlopWidgets.GapXS,
                        rect.width, SlopWidgets.RowH), error);
                }
                return result;
            }
        }

        public static SlopWidgets.Bar Footer(Rect rect) =>
            new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
    }
}
