// The installed parser owns which angle-bracket sequences are markup. Do not
// force literal comparisons, prompts or filenames onto the fallback font path.
using System.Reflection;

namespace BrowserAdapters;

public static class BrowserNativeText
{
    static MethodInfo? parse;

    public static bool HasMarkup(string text)
    {
        int position = text.IndexOf('<');
        if (position < 0)
            return false;
        parse ??= AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "RimWorld")
            .GetType("RimWorldWaylith.Imgui.RichText", true)!
            .GetMethod("Parse", BindingFlags.Static | BindingFlags.Public)!;
        while (position >= 0)
        {
            object?[] arguments = [text, position, 0, null, 0f];
            var result = parse.Invoke(null, arguments)!;
            if (result.ToString() != "None")
                return true;
            position = text.IndexOf('<', position + 1);
        }
        return false;
    }
}
