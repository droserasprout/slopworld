// Owns the browser's presentation-only snapshot. Native ProfileSettings.Write is
// redirected here in isolated copies; neither a profile nor daemon config is serialized.
using System.Reflection;
using System.Text.Json;

namespace BrowserAdapters;

public static class BrowserPreferences
{
    public static Action<string>? Persist;
    public static Action<string>? Fonts;
    static string? lastFonts;

    public static string FontSelection(object settings) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["uiFontName"] = Field(settings, "uiFontName").GetValue(settings),
                ["fontName"] = Field(settings, "fontName").GetValue(settings),
                ["uiFontSize"] = Field(settings, "uiFontSize").GetValue(settings),
                ["fontSize"] = Field(settings, "fontSize").GetValue(settings),
            }
        );

    static readonly string[] Allowed =
    [
        "sidebarHidden",
        "sidebarWidth",
        "sidebarOpenFilesVisible",
        "sidebarFilesOpenFraction",
        "sidebarSide",
        "uiDensity",
        "foldedProjects",
        "sidebarTab",
        "sidebarAgentStatus",
        "sidebarFilter",
        "sidebarWorktrees",
        "fontSize",
        "fontName",
        "uiFontSize",
        "uiFontName",
        "uiScheme",
        "theme",
        "grandmaMode",
        "cursor",
        "cursorGrayscale",
        "visualEffects",
        "sidebarShowHidden",
        "sidebarShowGitignored",
        "cursorColor",
        "smoothScrolling",
        "statusbarUsage",
        "statusbarSummaryPosition",
        "statusbarClockPosition",
        "statusbarPlanner",
        "statusbarJukebox",
        "statusbarGM",
        "statusbarAgentIndicators",
        "timeZone",
        "timeFormat",
        "dateFormat",
    ];

    public static void Changed(object settings)
    {
        if (Fonts != null)
        {
            string selected = FontSelection(settings);
            if (selected != lastFonts)
            {
                lastFonts = selected;
                Fonts(selected);
            }
        }
    }

    public static void Write(object settings)
    {
        Changed(settings);
        if (Persist == null)
            return;
        var values = new Dictionary<string, object?>();
        foreach (var name in Allowed)
        {
            var field = Field(settings, name);
            values.Add(name, field.GetValue(settings));
        }
        Persist(JsonSerializer.Serialize(values));
    }

    public static void Apply(object settings, string json)
    {
        // Treat local storage as untrusted, including old schema fields. Validate
        // each scalar before assigning so one damaged row cannot block startup.
        if (json.Length > 131072)
            return;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;
            foreach (var name in Allowed)
            {
                if (!document.RootElement.TryGetProperty(name, out var value))
                    continue;
                var field = Field(settings, name);
                if (field.FieldType == typeof(string) && value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString()!;
                    if (text.Length <= 16384)
                        field.SetValue(settings, text);
                }
                else if (
                    field.FieldType == typeof(bool)
                    && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                )
                    field.SetValue(settings, value.GetBoolean());
                else if (
                    field.FieldType == typeof(int)
                    && value.ValueKind == JsonValueKind.Number
                    && value.TryGetInt32(out var integer)
                    && integer >= 0
                    && integer <= 48
                )
                    field.SetValue(settings, integer);
                else if (
                    field.FieldType == typeof(float)
                    && value.ValueKind == JsonValueKind.Number
                    && value.TryGetSingle(out var number)
                    && float.IsFinite(number)
                    && number >= 0
                    && number <= (name == "sidebarWidth" ? 7680 : 1)
                )
                    field.SetValue(settings, number);
            }
        }
        catch (JsonException) { }
    }

    static FieldInfo Field(object settings, string name) =>
        settings.GetType().GetField(name)
        ?? throw new InvalidOperationException("Browser preference boundary changed: " + name);
}
