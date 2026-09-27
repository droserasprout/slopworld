using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
        sealed class CommandChoice
        {
            public readonly string Label;
            public readonly string Value;

            public CommandChoice(string label, string value)
            {
                Label = label;
                Value = value;
            }
        }

    static class CommandPicker
    {
        public static void Draw(Listing_Standard l, string label, string fieldName, string value,
                       List<CommandChoice> choices, bool custom, Action<string> set,
                       Action<bool> setCustom, string customLabel = "Custom template",
                       string defaultValue = null)
        {
            bool isCustom = custom || !choices.Any(c => c.Value == value);
            string shown = isCustom
                ? "Custom"
                : choices.First(c => c.Value == value).Label;
            var options = choices.Select(c => new SelectorOption(c.Label, () =>
                {
                    setCustom(false);
                    set(c.Value);
                })).ToList();
            options.Add(new SelectorOption("Custom", () => setCustom(true)));
            UiControls.Select(l, label, shown, options, out _);

            if (isCustom)
            {
                bool stacked = l.ColumnWidth < 430f;
                if (stacked) UiLayout.Note(l, customLabel);
                Rect customRow = l.GetRect(UiTheme.FieldH);
                float leftW = stacked ? 0f : Mathf.Min(220f, customRow.width * .42f);
                float gap = stacked ? 0f : UiTheme.GapM;
                float rightX = customRow.x + leftW + gap;
                float rightW = Mathf.Max(0f, customRow.width - leftW - gap);
                GUI.color = UiTheme.Dim;
                if (!stacked) UiText.RowLabel(new Rect(customRow.x, customRow.y, leftW,
                    customRow.height), customLabel);
                GUI.color = Color.white;
                set(UiText.Field(new Rect(rightX, customRow.y, rightW, customRow.height),
                    fieldName + ".custom", value, defaultValue: defaultValue));
            }
        }
    }
}
