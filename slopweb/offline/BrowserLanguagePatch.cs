// Native controls enumerate and choose LoadedLanguage objects. A browser language
// change needs a fresh Entry bootstrap to recreate its mod content and UI definitions.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserLanguagePatch
{
    internal static void Apply(AssemblyDefinition game, TypeDefinition probe)
    {
        var module = game.MainModule;
        var database = module.GetType("Verse.LanguageDatabase");
        var language = module.GetType("Verse.LoadedLanguage");
        var select = database.Methods.Single(m =>
            m.Name == "SelectLanguage"
            && m.IsStatic
            && m.ReturnType.FullName == "System.Void"
            && m.Parameters.Count == 1
            && m.Parameters[0].ParameterType.FullName == language.FullName
        );
        var folder = language.Fields.Single(f =>
            f.Name == "folderName" && !f.IsStatic && f.FieldType.FullName == "System.String"
        );
        var switchLanguage = probe.Methods.Single(m =>
            m.FullName == "System.Boolean Probe::SwitchLanguage(System.String)"
        );
        var il = select.Body.GetILProcessor();
        var first = select.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldfld, folder));
        il.InsertBefore(first, il.Create(OpCodes.Call, module.ImportReference(switchLanguage)));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));

        // Apply the validated folder after Prefs has finished reading its native
        // defaults and before Verse assigns activeLanguage or reads metadata.
        var metadata = database.Methods.Single(m =>
            m.Name == "InitAllMetadata"
            && m.IsStatic
            && m.Parameters.Count == 0
            && m.ReturnType.FullName == "System.Void"
        );
        var preference = module
            .GetType("Verse.Prefs")
            .Properties.Single(p =>
                p.Name == "LangFolderName"
                && p.PropertyType.FullName == "System.String"
                && p.GetMethod.IsStatic
                && p.SetMethod.IsStatic
            );
        var startup = probe.Methods.Single(m =>
            m.FullName == "System.String Probe::StartupLanguage(System.String)"
        );
        il = metadata.Body.GetILProcessor();
        first = metadata.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Call, preference.GetMethod));
        il.InsertBefore(first, il.Create(OpCodes.Call, module.ImportReference(startup)));
        il.InsertBefore(first, il.Create(OpCodes.Call, preference.SetMethod));
    }
}
