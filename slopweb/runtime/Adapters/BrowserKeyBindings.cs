// Only definitions loaded from the authored SlopWorld XML may cross origin storage.
// Verse owns binding slots, conflict checks, defaults, reset, and the save lifecycle.
using System.Text.Json;
using UnityEngine;
using Verse;

namespace BrowserAdapters;

public static class BrowserKeyBindings
{
    public static Action<string>? Persist;
    static KeyBindingDef[] authored = [];
    static KeyBindingDef? fullscreen;
    static string pending = "{}";
    static bool ready;
    static bool restoring;

    // JS supplies origin storage before WorkspaceFrame installs the definitions.
    // Defer restoration until native Init has populated defaults; its initial Save
    // must never publish an empty snapshot over those pending customizations.
    public static void Configure(IEnumerable<KeyBindingDef> definitions)
    {
        authored = definitions.ToArray();
        fullscreen = authored.FirstOrDefault(definition =>
            definition.defName == "SlopToggleFullscreen"
        );
        ready = true;
        Apply(pending);
        pending = "{}";
    }

    // Admission only: native WindowStack/terminal routing still owns eligibility and
    // execution. Read the live slots so edits and reset apply before the next save.
    public static bool FullscreenKey(string key)
    {
        if (
            fullscreen == null
            || KeyPrefs.KeyPrefsData == null
            || !Enum.TryParse<KeyCode>(key, out var code)
            || code == KeyCode.None
        )
            return false;
        var data = KeyPrefs.KeyPrefsData;
        return data.GetBoundKeyCode(fullscreen, KeyPrefs.BindingSlot.A) == code
            || data.GetBoundKeyCode(fullscreen, KeyPrefs.BindingSlot.B) == code;
    }

    public static void Apply(string json)
    {
        if (json.Length > 131072)
            return;
        if (!ready)
        {
            pending = json;
            return;
        }
        restoring = true;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;
            foreach (var definition in authored)
            {
                if (
                    !document.RootElement.TryGetProperty(definition.defName, out var row)
                    || row.ValueKind != JsonValueKind.Object
                )
                    continue;
                ApplySlot(definition, row, "a", KeyPrefs.BindingSlot.A);
                ApplySlot(definition, row, "b", KeyPrefs.BindingSlot.B);
            }
            // Use the installed validation policy, just as KeyPrefs.Init does.
            // Native saves during validation must not publish a partial restore.
            KeyPrefs.KeyPrefsData.ErrorCheck();
        }
        catch (JsonException) { }
        finally
        {
            restoring = false;
        }
    }

    static void ApplySlot(
        KeyBindingDef definition,
        JsonElement row,
        string name,
        KeyPrefs.BindingSlot slot
    )
    {
        if (
            row.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && Enum.TryParse<KeyCode>(value.GetString(), out var code)
            && Enum.IsDefined(code)
            && value.GetString() == code.ToString()
        )
            KeyPrefs.KeyPrefsData.SetBinding(definition, slot, code);
    }

    // Called after native KeyPrefs.Save; never reads the game's XML/profile files
    // or unresolved bindings. Explicit None values preserve cleared slots, and
    // writing defaults too lets the native ResetToDefaults replace an old snapshot.
    public static void Save()
    {
        if (!ready || restoring || Persist == null)
            return;
        var values = new Dictionary<string, object>();
        foreach (var definition in authored)
            values.Add(
                definition.defName,
                new
                {
                    a = KeyPrefs
                        .KeyPrefsData.GetBoundKeyCode(definition, KeyPrefs.BindingSlot.A)
                        .ToString(),
                    b = KeyPrefs
                        .KeyPrefsData.GetBoundKeyCode(definition, KeyPrefs.BindingSlot.B)
                        .ToString(),
                }
            );
        Persist(JsonSerializer.Serialize(values));
    }
}
