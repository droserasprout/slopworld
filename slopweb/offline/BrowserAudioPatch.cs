// Keep Radio's native policy and replace only its local music adapter.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BrowserAudioPatch
{
    internal static void Apply(AssemblyDefinition mod, string root)
    {
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var owner = mod.MainModule.GetType("SlopWorld.NativeJukebox");
        var adapter = adapters.MainModule.GetType("BrowserAdapters.BrowserAudio");
        foreach (var name in new[] { "SetMuted", "Resume", "Navigate", "get_Volume" })
            BrowserClipboardPatch.Redirect(owner, adapter, name, name);
        var read = owner.Methods.Single(m => m.Name == "ReadNativeTrack");
        if (
            !read.IsStatic
            || read.Parameters.Count != 0
            || read.ReturnType.FullName != "SlopWorld.NativeTrackSnapshot"
        )
            throw new InvalidOperationException("OST metadata boundary changed");
        read.Body = new MethodBody(read);
        var il = read.Body.GetILProcessor();
        il.Emit(
            OpCodes.Call,
            mod.MainModule.ImportReference(adapter.Methods.Single(m => m.Name == "ReadNativeTrack"))
        );
        il.Emit(OpCodes.Castclass, read.ReturnType);
        il.Emit(OpCodes.Ret);
    }
}
