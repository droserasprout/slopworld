using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Deliberately one explicit hook contract, not a Harmony attribute interpreter.
// Preserve original target body and invoke the actual mod prefix by reference.
if (args[0] == "--eco")
{
    EcoWorkspace.Apply(args[1], args[2]);
    return;
}
if (args[0] == "--core")
{
    CoreBootstrap.Apply(args[1], args[2]);
    return;
}
var root = Path.GetFullPath(args[0]);
var modPath = Path.GetFullPath(args[1]);
var targetPath = Path.Combine(root, "game", "Assembly-CSharp.dll");
var outputMod = Path.Combine(root, "mod", "SlopWorld.dll");
Directory.CreateDirectory(Path.GetDirectoryName(outputMod)!);
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
var originalTarget = Hash(targetPath);
var originalMod = Hash(modPath);
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.Combine(root, "game"));
resolver.AddSearchDirectory(Path.GetDirectoryName(modPath));
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
using var target = AssemblyDefinition.ReadAssembly(
    targetPath,
    new ReaderParameters { InMemory = true, AssemblyResolver = resolver }
);
using var mod = AssemblyDefinition.ReadAssembly(
    modPath,
    new ReaderParameters { InMemory = true, AssemblyResolver = resolver }
);
var setter = target
    .MainModule.Types.Single(t => t.FullName == "Verse.Prefs")
    .Methods.Single(m => m.Name == "set_RunInBackground");
var prefix = mod
    .MainModule.Types.Single(t => t.FullName == "SlopWorld.Patch_RunInBackground")
    .Methods.Single(m => m.Name == "Prefix");
if (
    !setter.IsStatic
    || setter.ReturnType.FullName != "System.Void"
    || setter.Parameters.Count != 1
    || setter.Parameters[0].ParameterType.FullName != "System.Boolean"
    || !setter.HasBody
    || !prefix.IsStatic
    || prefix.ReturnType.FullName != "System.Void"
    || prefix.Parameters.Count != 1
    || prefix.Parameters[0].ParameterType.FullName != "System.Boolean&"
)
    throw new InvalidOperationException("Offline prefix contract changed");
if (
    setter.Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.DeclaringType.FullName == prefix.DeclaringType.FullName
    )
)
    throw new InvalidOperationException("Refusing duplicate prefix installation");

// The game's assembly cannot directly call a private method across assemblies.
// Widen only this prefix in the ignored mod copy; production remains unchanged.
prefix.IsPublic = true;
var reference = target.MainModule.ImportReference(prefix);
var first = setter.Body.Instructions[0];
var il = setter.Body.GetILProcessor();
il.InsertBefore(first, il.Create(OpCodes.Ldarga, setter.Parameters[0]));
il.InsertBefore(first, il.Create(OpCodes.Call, reference));
target.Write(targetPath + ".partial");
mod.Write(outputMod);
File.Move(targetPath + ".partial", targetPath, true);
var manifestPath = Path.Combine(root, "game", "manifest.json");
var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsArray();
manifest.Single(n => n!["file"]!.GetValue<string>() == "Assembly-CSharp.dll")!["stagedSha256"] =
    Hash(targetPath);
File.WriteAllText(
    manifestPath,
    manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
);
File.WriteAllText(
    Path.Combine(root, "mod", "offline.json"),
    JsonSerializer.Serialize(
        new
        {
            target = "Verse.Prefs.set_RunInBackground(bool)",
            prefix = "SlopWorld.Patch_RunInBackground.Prefix(ref bool)",
            originalTarget,
            stagedTarget = Hash(targetPath),
            originalMod,
            stagedMod = Hash(outputMod),
            dynamicDetours = false,
        }
    )
);
Console.WriteLine(
    "Installed one real SlopWorld prefix in the staged game; original binaries unchanged."
);
