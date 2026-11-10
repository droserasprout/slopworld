// Advertise the browser renderer's actual capabilities, retaining native defaults
// when the staged assembly is running with another GUI backend.
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserGuiPatch
{
    internal static void Apply(string root, IAssemblyResolver resolver)
    {
        var path = Path.Combine(root, "game", "UnityStubs.dll");
        using var target = AssemblyDefinition.ReadAssembly(
            path,
            new ReaderParameters { InMemory = true, AssemblyResolver = resolver }
        );
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var clipboard = adapters.MainModule.GetType("BrowserAdapters.BrowserClipboard");
        var utility = target.MainModule.GetType("UnityEngine.GUIUtility");
        BrowserClipboardPatch.Redirect(utility, clipboard, "get_systemCopyBuffer", "Get");
        BrowserClipboardPatch.Redirect(utility, clipboard, "set_systemCopyBuffer", "Set");
        var method = target
            .MainModule.GetType("UnityEngine.SystemInfo")
            .Methods.Single(m => m.Name == "get_copyTextureSupport");
        if (
            !method.IsStatic
            || method.ReturnType.FullName != "UnityEngine.Rendering.CopyTextureSupport"
        )
            throw new InvalidOperationException("GUI copy capability contract changed");
        var installed = adapters
            .MainModule.GetType("BrowserAdapters.BrowserGui")
            .Methods.Single(m => m.Name == "IsInstalled");
        var first = method.Body.Instructions[0];
        var il = method.Body.GetILProcessor();
        il.InsertBefore(
            first,
            il.Create(OpCodes.Call, target.MainModule.ImportReference(installed))
        );
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
        var changed = target.MainModule.ImportReference(
            adapters
                .MainModule.GetType("BrowserAdapters.BrowserGui")
                .Methods.Single(m => m.Name == "TextureApplied")
        );
        foreach (
            var apply in target
                .MainModule.GetType("UnityEngine.Texture2D")
                .Methods.Where(m =>
                    (m.Name == "Apply" && m.Parameters.Count == 2) || m.Name == "LoadImage"
                )
        )
        {
            if (apply.IsStatic || !apply.HasBody)
                throw new InvalidOperationException("Texture update contract changed");
            var processor = apply.Body.GetILProcessor();
            var start = apply.Body.Instructions[0];
            processor.InsertBefore(start, processor.Create(OpCodes.Ldarg_0));
            processor.InsertBefore(start, processor.Create(OpCodes.Call, changed));
        }
        target.Write(path + ".partial");
        File.Move(path + ".partial", path, true);
        var manifestPath = Path.Combine(root, "game", "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsArray();
        manifest.Single(n => n!["file"]!.GetValue<string>() == "UnityStubs.dll")!["stagedSha256"] =
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        var adaptationsPath = Path.Combine(root, "game", "adaptations.json");
        var adaptations = JsonNode.Parse(File.ReadAllText(adaptationsPath))!.AsArray();
        adaptations.Add(
            "Canvas GUI supports render targets but not GPU CopyTexture; native capability defaults remain outside BrowserGui"
        );
        adaptations.Add(
            "Texture2D.Apply/LoadImage invalidate browser image uploads; original pixel update and lifetime bodies remain"
        );
        File.WriteAllText(adaptationsPath, adaptations.ToJsonString());
    }
}
