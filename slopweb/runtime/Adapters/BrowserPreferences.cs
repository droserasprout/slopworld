// Browser settings validate host appearance and persist origin-local presentation and Jukebox edits.
using System.Reflection;
using System.Text.Json;
using SlopWorld;

namespace BrowserAdapters;

public static class BrowserPreferences
{
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

    static readonly FieldInfo[] Local =
    [
        .. Allowed,
        typeof(ModSettings).GetField(nameof(ModSettings.radio))!,
        typeof(ModSettings).GetField(nameof(ModSettings.radioOutput))!,
        typeof(ModSettings).GetField(nameof(ModSettings.radioVolume))!,
        typeof(ModSettings).GetField(nameof(ModSettings.radioHiddenSources))!,
    ];

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

    public static Action<ModSettings> Open(
        ModSettings settings,
        Func<string, string> read,
        Func<string, string, bool> persist
    )
    {
        // A tab starts from its effective values, including host defaults for missing
        // fields. Only a successful field write advances that field's baseline.
        var baseline = new Dictionary<string, object?>();
        foreach (var field in Local)
        {
            string json = read(field.Name);
            if (json.Length is > 0 and <= 131072)
            {
                try
                {
                    using var document = JsonDocument.Parse(json);
                    RestoreField(settings, field, document.RootElement);
                }
                catch (JsonException) { }
            }
            baseline.Add(field.Name, field.GetValue(settings));
        }

        return current =>
        {
            Changed(current);
            foreach (var field in Local)
            {
                var value = field.GetValue(current);
                if (
                    !Equals(baseline[field.Name], value)
                    && persist(field.Name, JsonSerializer.Serialize(value))
                )
                    baseline[field.Name] = value;
            }
        };
    }

    public static void ApplyLocal(ModSettings settings, string json) =>
        Restore(settings, json, Local);

    public static void Apply(ModSettings settings, string json) => Restore(settings, json, Allowed);

    static void Restore(ModSettings settings, string json, FieldInfo[] fields)
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
            foreach (var field in fields)
                if (document.RootElement.TryGetProperty(field.Name, out var value))
                    RestoreField(settings, field, value);
        }
        catch (JsonException) { }
    }

    static void RestoreField(ModSettings settings, FieldInfo field, JsonElement value)
    {
        var name = field.Name;
        if (field.FieldType == typeof(string) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (text.Length <= 16384 && (name != "radioOutput" || text is "local" or "server"))
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
