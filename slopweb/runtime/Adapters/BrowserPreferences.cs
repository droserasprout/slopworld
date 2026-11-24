// Browser persistence stores only allowlisted presentation fields.
using System.Reflection;
using System.Text.Json;
using SlopWorld;

namespace BrowserAdapters;

public static class BrowserPreferences
{
    public static Action<string>? Persist;
    public static Action<string>? Fonts;
    static string? lastFonts;

    public static void HoldFonts(ModSettings settings, string json)
    {
        Apply(settings, json);
        lastFonts = FontSelection(settings);
    }

    public static string FontSelection(ModSettings settings) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["uiFontName"] = settings.uiFontName,
                ["fontName"] = settings.fontName,
                ["uiFontSize"] = settings.uiFontSize,
                ["fontSize"] = settings.fontSize,
            }
        );

    static readonly FieldInfo[] Allowed = ReadAllowed();

    static FieldInfo[] ReadAllowed()
    {
        using var reader = new StreamReader(
            typeof(BrowserPreferences).Assembly.GetManifestResourceStream(
                "presentation-preferences.txt"
            )!
        );
        return reader
            .ReadToEnd()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(name =>
                typeof(ModSettings).GetField(name)
                ?? throw new MissingFieldException(typeof(ModSettings).FullName, name)
            )
            .ToArray();
    }

    public static float BackgroundDimming(ModSettings settings)
    {
        float dim = settings.ecoDim;
        return float.IsFinite(dim) ? Math.Clamp(dim, 0f, 0.8f) : 0.3f;
    }

    public static void Changed(ModSettings settings)
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

    public static void Write(ModSettings settings)
    {
        Changed(settings);
        if (Persist == null)
            return;
        var values = new Dictionary<string, object?>();
        foreach (var field in Allowed)
            values.Add(field.Name, field.GetValue(settings));
        Persist(JsonSerializer.Serialize(values));
    }

    public static void Apply(ModSettings settings, string json)
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
            foreach (var field in Allowed)
            {
                var name = field.Name;
                if (!document.RootElement.TryGetProperty(name, out var value))
                    continue;
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
                    && integer <= (name == "foregroundFps" ? 240 : 48)
                )
                    field.SetValue(settings, integer);
                else if (
                    field.FieldType == typeof(float)
                    && value.ValueKind == JsonValueKind.Number
                    && value.TryGetSingle(out var number)
                    && float.IsFinite(number)
                    && number >= 0
                    && number
                        <= (
                            name == "sidebarWidth" ? 7680
                            : name == "ecoDim" ? 0.8f
                            : 1
                        )
                )
                    field.SetValue(settings, number);
            }
        }
        catch (JsonException) { }
    }
}
