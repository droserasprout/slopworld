using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length >= 3 && args[1] == "--stage")
{
    var jobs = args.Length == 4 && args[3] == "--jobs";
    var lifecycle = jobs || (args.Length == 4 && args[3] == "--lifecycle");
    var adapted = lifecycle || (args.Length == 4 && args[3] == "--adapt");
    Directory.CreateDirectory(args[2]);
    var queue = new Queue<string>(["RimWorld", "Assembly-CSharp", "UnityStubs", "Waylith"]);
    // System.* is not a framework guarantee: System.IO.Hashing is a NuGet
    // dependency. Use the matching host framework's assembly inventory instead.
    var framework = Directory
        .GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
        .Select(Path.GetFileNameWithoutExtension)
        .ToHashSet();
    var seen = new HashSet<string>();
    var manifest = new List<object>();
    var missing = new List<string>();
    while (queue.TryDequeue(out var name))
    {
        if (!seen.Add(name) || framework.Contains(name))
            continue;
        var path = Path.Combine(args[0], name + ".dll");
        if (!File.Exists(path))
        {
            missing.Add(name);
            continue;
        }
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        foreach (var reference in md.AssemblyReferences)
            queue.Enqueue(md.GetString(md.GetAssemblyReference(reference).Name));
        var destination = Path.Combine(args[2], name + ".dll");
        File.Copy(path, destination, true);
        if (name == "RimWorld")
        {
            // Only the ignored experimental copy is changed. Preserve the original
            // for the baseline; a separate page mode explicitly opts into this probe.
            var owner = md
                .TypeDefinitions.Select(h => md.GetTypeDefinition(h))
                .Single(t =>
                    md.GetString(t.Namespace) == "RimWorldWaylith"
                    && md.GetString(t.Name) == "Program"
                );
            var method = owner
                .GetMethods()
                .Select(h => md.GetMethodDefinition(h))
                .Single(m => md.GetString(m.Name) == "InitLogger");
            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            if (body.ExceptionRegions.Length != 0)
                throw new InvalidOperationException("Logger patch requires no exception regions");
            var section = pe.PEHeaders.SectionHeaders.Single(s =>
                method.RelativeVirtualAddress >= s.VirtualAddress
                && method.RelativeVirtualAddress < s.VirtualAddress + s.VirtualSize
            );
            var offset =
                method.RelativeVirtualAddress - section.VirtualAddress + section.PointerToRawData;
            var bytes = File.ReadAllBytes(path);
            var headerSize = (bytes[offset] & 3) == 2 ? 1 : (bytes[offset + 1] >> 4) * 4;
            Array.Clear(bytes, offset + headerSize, body.GetILBytes()!.Length);
            bytes[offset + headerSize] = 0x2a; // ret
            Directory.CreateDirectory(Path.Combine(args[2], "skip-logger"));
            File.WriteAllBytes(Path.Combine(args[2], "skip-logger", "RimWorld.dll"), bytes);
            if (adapted)
            {
                var bootstrap = md
                    .TypeDefinitions.Select(h => md.GetTypeDefinition(h))
                    .Single(t =>
                        md.GetString(t.Namespace) == "RimWorldWaylith"
                        && md.GetString(t.Name) == "Bootstrap"
                    );
                var resolve = bootstrap
                    .GetMethods()
                    .Select(h => md.GetMethodDefinition(h))
                    .Single(m => md.GetString(m.Name) == "ResolvePersistentDataRoot");
                var tempDirectory = md.MemberReferences.Single(h =>
                {
                    var member = md.GetMemberReference(h);
                    if (
                        md.GetString(member.Name) != "GetTempPath"
                        || member.Parent.Kind != HandleKind.TypeReference
                    )
                        return false;
                    var type = md.GetTypeReference((TypeReferenceHandle)member.Parent);
                    return md.GetString(type.Namespace) == "System.IO"
                        && md.GetString(type.Name) == "Path";
                });
                var replacement = new List<byte> { 0x28 };
                replacement.AddRange(
                    BitConverter.GetBytes(
                        System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(tempDirectory)
                    )
                );
                replacement.Add(0x2a);
                ReplaceBody(bytes, pe, resolve, replacement.ToArray());
                if (lifecycle)
                    BrowserBootstrapPaths(bytes, pe, md, bootstrap);
                File.WriteAllBytes(destination, bytes);
            }
            File.WriteAllText(
                Path.Combine(args[2], "skip-logger", "patch.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        method = "RimWorldWaylith.Program.InitLogger",
                        change = "Replace IL body with ret/nop; no installed files changed",
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                    }
                )
            );
        }
        if (adapted && name == "UnityStubs")
        {
            var owner = md
                .TypeDefinitions.Select(h => md.GetTypeDefinition(h))
                .Single(t =>
                    md.GetString(t.Namespace) == "UnityEngine"
                    && md.GetString(t.Name) == "Application"
                );
            var method = owner
                .GetMethods()
                .Select(h => md.GetMethodDefinition(h))
                .Single(m => md.GetString(m.Name) == ".cctor");
            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            var il = body.GetILBytes()!;
            var constructor = md.MemberReferences.Single(h =>
            {
                var member = md.GetMemberReference(h);
                if (
                    md.GetString(member.Name) != ".ctor"
                    || member.Parent.Kind != HandleKind.TypeReference
                )
                    return false;
                var type = md.GetTypeReference((TypeReferenceHandle)member.Parent);
                return md.GetString(type.Namespace) == "System"
                    && md.GetString(type.Name) == "NotImplementedException";
            });
            var token = System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(constructor);
            var sites = Enumerable
                .Range(0, il.Length - 5)
                .Where(i =>
                    il[i] == 0x73 && BitConverter.ToInt32(il, i + 1) == token && il[i + 5] == 0x7a
                )
                .ToArray();
            // The inspected default arm is newobj/throw followed by ldloc.0/stsfld.
            // Refuse changed layouts instead of guessing how a newer build works.
            if (sites.Length != 1 || il[sites[0] + 6] != 0x06 || il[sites[0] + 7] != 0x80)
                throw new InvalidOperationException(
                    "Unrecognized Application platform initializer layout"
                );
            var section = pe.PEHeaders.SectionHeaders.Single(s =>
                method.RelativeVirtualAddress >= s.VirtualAddress
                && method.RelativeVirtualAddress < s.VirtualAddress + s.VirtualSize
            );
            var bytes = File.ReadAllBytes(path);
            var offset =
                method.RelativeVirtualAddress - section.VirtualAddress + section.PointerToRawData;
            var headerSize = (bytes[offset] & 3) == 2 ? 1 : (bytes[offset + 1] >> 4) * 4;
            // WebGLPlayer=17, assigned to the existing local before the existing field store.
            new byte[] { 0x1f, 17, 0x0a, 0, 0, 0 }.CopyTo(bytes, offset + headerSize + sites[0]);
            File.WriteAllBytes(destination, bytes);
        }
        if (adapted && name == "Brutal.Core.Common")
        {
            var owner = md
                .TypeDefinitions.Select(h => md.GetTypeDefinition(h))
                .Single(t =>
                    md.GetString(t.Namespace) == "Brutal" && md.GetString(t.Name) == "Platform"
                );
            var initializer = owner
                .GetMethods()
                .Select(h => md.GetMethodDefinition(h))
                .Single(m => md.GetString(m.Name) == ".cctor");
            var family = owner
                .GetFields()
                .Single(h => md.GetString(md.GetFieldDefinition(h).Name) == "CurrentFamily");
            // Use a sentinel family outside Windows/Linux/MacOS; all native OS
            // predicates stay false. This is diagnostic, not a browser platform port.
            var replacement = new List<byte> { 0x19, 0x80 }; // ldc.i4.3; stsfld
            replacement.AddRange(
                BitConverter.GetBytes(
                    System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(family)
                )
            );
            replacement.Add(0x2a);
            var bytes = File.ReadAllBytes(path);
            ReplaceBody(bytes, pe, initializer, replacement.ToArray());
            File.WriteAllBytes(destination, bytes);
        }
        if (jobs && name == "Waylith")
        {
            var bytes = File.ReadAllBytes(destination);
            BrowserJobsPatch.Apply(bytes, pe, md, ReplaceBody);
            File.WriteAllBytes(destination, bytes);
        }
        manifest.Add(
            new
            {
                file = name + ".dll",
                sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
                stagedSha256 = Convert.ToHexStringLower(
                    SHA256.HashData(File.ReadAllBytes(destination))
                ),
                bytes = stream.Length,
            }
        );
    }
    File.WriteAllText(
        Path.Combine(args[2], "manifest.json"),
        JsonSerializer.Serialize(manifest, ScannerJson.Options)
    );
    File.WriteAllText(Path.Combine(args[2], "missing.json"), JsonSerializer.Serialize(missing));
    File.WriteAllText(
        Path.Combine(args[2], "adaptations.json"),
        JsonSerializer.Serialize(
            (
                adapted
                    ? new[]
                    {
                        "InitLogger body replaced with ret",
                        "Application.platform unknown OS arm assigned WebGLPlayer=17",
                        "Bootstrap.ResolvePersistentDataRoot uses browser virtual temporary directory",
                        "Brutal.Platform.CurrentFamily assigned diagnostic sentinel 3 (all desktop predicates false)",
                    }
                    : Array.Empty<string>()
            )
                .Concat(
                    jobs
                        ? new[]
                        {
                            "WaylithJobRuntime uses synchronous serial job execution with no worker threads",
                        }
                        : Array.Empty<string>()
                )
                .Concat(
                    lifecycle
                        ? new[]
                        {
                            "Bootstrap.ConfigureRuntime uses AppContext.BaseDirectory instead of the native executable directory",
                        }
                        : Array.Empty<string>()
                )
        )
    );
    Console.WriteLine(
        $"Staged {manifest.Count} installed assemblies into {args[2]} (local-only; do not redistribute)."
    );
    return;
}

// Preserve all runtime/asset configuration. Only replace the native executable
// directory expression; browser assets will be mounted beneath the managed base.
static void BrowserBootstrapPaths(
    byte[] bytes,
    PEReader pe,
    MetadataReader md,
    TypeDefinition bootstrap
)
{
    int Member(string ns, string owner, string name) =>
        System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(
            md.MemberReferences.Single(h =>
            {
                var m = md.GetMemberReference(h);
                if (m.Parent.Kind != HandleKind.TypeReference || md.GetString(m.Name) != name)
                    return false;
                var t = md.GetTypeReference((TypeReferenceHandle)m.Parent);
                return md.GetString(t.Namespace) == ns && md.GetString(t.Name) == owner;
            })
        );
    var method = bootstrap
        .GetMethods()
        .Select(md.GetMethodDefinition)
        .Single(m => md.GetString(m.Name) == "ConfigureRuntime");
    var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
    var processPath = Member("System", "Environment", "get_ProcessPath");
    var directoryName = Member("System.IO", "Path", "GetDirectoryName");
    var baseDirectory = Member("System", "AppContext", "get_BaseDirectory");
    var sites = Enumerable
        .Range(0, il.Length - 9)
        .Where(i =>
            il[i] == 0x28
            && BitConverter.ToInt32(il, i + 1) == processPath
            && il[i + 5] == 0x28
            && BitConverter.ToInt32(il, i + 6) == directoryName
        )
        .ToArray();
    if (sites.Length != 1)
        throw new InvalidOperationException(
            "Unrecognized bootstrap executable directory expression"
        );
    BitConverter.GetBytes(baseDirectory).CopyTo(il, sites[0] + 1);
    Array.Clear(il, sites[0] + 5, 5);
    ReplaceBody(bytes, pe, method, il);
}

static void ReplaceBody(byte[] bytes, PEReader pe, MethodDefinition method, byte[] replacement)
{
    var body = pe.GetMethodBody(method.RelativeVirtualAddress);
    if (body.ExceptionRegions.Length != 0 || replacement.Length > body.GetILBytes()!.Length)
        throw new InvalidOperationException("Unsafe method body replacement refused");
    var section = pe.PEHeaders.SectionHeaders.Single(s =>
        method.RelativeVirtualAddress >= s.VirtualAddress
        && method.RelativeVirtualAddress < s.VirtualAddress + s.VirtualSize
    );
    var offset = method.RelativeVirtualAddress - section.VirtualAddress + section.PointerToRawData;
    var headerSize = (bytes[offset] & 3) == 2 ? 1 : (bytes[offset + 1] >> 4) * 4;
    Array.Clear(bytes, offset + headerSize, body.GetILBytes()!.Length);
    replacement.CopyTo(bytes, offset + headerSize);
}

foreach (
    var path in Directory
        .GetFiles(args[0], "*.dll")
        .Where(p =>
            new[]
            {
                "RimWorld",
                "Assembly-CSharp",
                "UnityStubs",
                "Waylith",
                "Brutal.Glfw",
                "Brutal.Vulkan",
            }.Contains(Path.GetFileNameWithoutExtension(p))
        )
)
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var md = pe.GetMetadataReader();
    Console.WriteLine($"ASSEMBLY {Path.GetFileName(path)}");
    var entry = pe.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress;
    if (entry != 0)
    {
        var method = md.GetMethodDefinition(
            (MethodDefinitionHandle)System.Reflection.Metadata.Ecma335.MetadataTokens.Handle(entry)
        );
        var type = md.GetTypeDefinition(method.GetDeclaringType());
        Console.WriteLine(
            $"ENTRYPOINT {md.GetString(type.Namespace)}.{md.GetString(type.Name)}.{md.GetString(method.Name)}"
        );
    }
    Console.WriteLine(
        "REFERENCES "
            + string.Join(
                ", ",
                md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name))
            )
    );
    foreach (var h in md.TypeDefinitions)
    {
        var t = md.GetTypeDefinition(h);
        var name = md.GetString(t.Namespace) + "." + md.GetString(t.Name);
        var selected =
            name.Contains("GenMath")
            || name.Contains("GenDate")
            || name.Contains("IntVec3")
            || name.Contains("VersionControl")
            || name.EndsWith("Glfw", StringComparison.Ordinal)
            || name.EndsWith("Application", StringComparison.Ordinal)
            || name.EndsWith("Program", StringComparison.Ordinal);
        foreach (var mh in t.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            if (selected)
                Console.WriteLine($"METHOD {name}.{md.GetString(m.Name)} [{m.Attributes}]");
            if ((m.Attributes & MethodAttributes.PinvokeImpl) != 0)
            {
                var import = m.GetImport();
                Console.WriteLine(
                    $"PINVOKE {name}.{md.GetString(m.Name)} -> {md.GetString(md.GetModuleReference(import.Module).Name)}!{md.GetString(import.Name)}"
                );
            }
        }
    }
}

file static class ScannerJson
{
    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}
