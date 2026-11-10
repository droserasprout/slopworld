// Offline inventory owns PE/IL inspection only: it never loads target assemblies.
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

static class Inspect
{
    static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes)
        .GetFields()
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => unchecked((ushort)op.Value));

    static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--trace")
        {
            using var stream = File.OpenRead(args[1]);
            using var pe = new PEReader(stream);
            var md = pe.GetMetadataReader();
            foreach (var h in md.MethodDefinitions)
            {
                string name = Name(md, h);
                var method = md.GetMethodDefinition(h);
                if (
                    !name.Contains(args[2], StringComparison.Ordinal)
                    || method.RelativeVirtualAddress == 0
                )
                    continue;
                Console.WriteLine(name);
                foreach (var instruction in Instructions(pe, md, method))
                    Console.WriteLine(
                        $"  IL_{instruction.Offset:x4}: {instruction.Op} {instruction.Operand}"
                    );
            }
            return 0;
        }
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: Inspect GAME_DIRECTORY MOD_ASSEMBLIES | --trace DLL METHOD_SUBSTRING"
            );
            return 2;
        }
        var assemblies = new List<object>();
        foreach (var (directory, owner) in new[] { (args[0], "game"), (args[1], "mod") })
        foreach (var path in Directory.GetFiles(directory, "*.dll").Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(path);
            if (
                owner == "game"
                && (
                    file.StartsWith("System.")
                    || (file.StartsWith("Microsoft.") && !file.StartsWith("Microsoft.Extensions."))
                    || file
                        is "mscorlib.dll"
                            or "netstandard.dll"
                            or "System.dll"
                            or "WindowsBase.dll"
                )
            )
                continue;
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                continue;
            var md = pe.GetMetadataReader();
            var imports = new List<object>();
            var hazards = new List<object>();
            foreach (var handle in md.MethodDefinitions)
            {
                var method = md.GetMethodDefinition(handle);
                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    var import = method.GetImport();
                    imports.Add(
                        new
                        {
                            method = Name(md, handle),
                            library = md.GetString(md.GetModuleReference(import.Module).Name),
                            entryPoint = md.GetString(import.Name),
                        }
                    );
                }
                if (method.RelativeVirtualAddress == 0)
                    continue;
                foreach (var instruction in Instructions(pe, md, method))
                {
                    if (instruction.Op is not ("call" or "callvirt" or "newobj"))
                        continue;
                    var category = Category(instruction.Operand);
                    if (category != null)
                        hazards.Add(
                            new
                            {
                                method = Name(md, handle),
                                offset = $"IL_{instruction.Offset:x4}",
                                category,
                                target = instruction.Operand,
                            }
                        );
                }
            }
            assemblies.Add(
                new
                {
                    owner,
                    file,
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
                    version = md.GetAssemblyDefinition().Version.ToString(),
                    mvid = md.GetGuid(md.GetModuleDefinition().Mvid),
                    machine = pe.PEHeaders.CoffHeader.Machine.ToString(),
                    entryPoint = pe.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress,
                    references = md
                        .AssemblyReferences.Select(h =>
                            md.GetString(md.GetAssemblyReference(h).Name)
                        )
                        .ToArray(),
                    imports,
                    hazards,
                }
            );
        }
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    inspection = "Static IL call-site inventory; not reachability or a runtime compatibility test.",
                    gameVersion = File.ReadAllText(Path.Combine(args[0], "Version.txt")).Trim(),
                    runtime = JsonDocument
                        .Parse(
                            File.ReadAllText(Path.Combine(args[0], "RimWorld.runtimeconfig.json"))
                        )
                        .RootElement,
                    nativeFiles = Directory
                        .GetFiles(args[0], "*.so*")
                        .Select(Path.GetFileName)
                        .Order(StringComparer.Ordinal)
                        .ToArray(),
                    assemblies,
                },
                new JsonSerializerOptions { WriteIndented = true }
            )
        );
        return 0;
    }

    static string? Category(string target)
    {
        if (
            target.Contains("System.Reflection.Emit.")
            || target.Contains("HarmonyLib.")
            || target.Contains("MonoMod.")
        )
            return "dynamic-code";
        if (
            target.Contains("System.Threading.Thread::")
            || target.Contains("System.Threading.Tasks.Task::Run")
            || target.Contains("WaitHandle::")
            || target.Contains("ManualResetEvent")
            || target.Contains("AutoResetEvent")
        )
            return "threads-blocking";
        if (
            target.Contains("System.Net.Sockets.")
            || target.Contains("System.Net.Http.SocketsHttpHandler")
            || target.Contains("System.Net.Security.SslStream")
        )
            return "native-network";
        if (
            target.Contains("System.IO.File")
            || target.Contains("System.IO.Directory")
            || target.Contains("System.IO.MemoryMappedFiles.")
        )
            return "filesystem";
        if (
            target.Contains("System.Diagnostics.Process::")
            || target.Contains("System.Runtime.InteropServices.NativeLibrary::")
        )
            return "host-process-native-loader";
        return null;
    }

    record Instruction(int Offset, string Op, string Operand);

    static IEnumerable<Instruction> Instructions(
        PEReader pe,
        MetadataReader md,
        MethodDefinition method
    )
    {
        var bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
        for (int offset = 0; offset < bytes.Length; )
        {
            int start = offset;
            ushort code = bytes[offset++];
            if (code == 0xfe)
                code = (ushort)(0xfe00 | bytes[offset++]);
            var op = Opcodes[code];
            string operand = "";
            int size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget
                or OperandType.ShortInlineI
                or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + 4 * BitConverter.ToInt32(bytes, offset)),
                _ => 4,
            };
            if (offset + size > bytes.Length)
                throw new BadImageFormatException("Truncated IL operand");
            if (
                op.OperandType
                is OperandType.InlineMethod
                    or OperandType.InlineField
                    or OperandType.InlineType
                    or OperandType.InlineTok
            )
                operand = Name(
                    md,
                    MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes, offset))
                );
            else if (op.OperandType == OperandType.InlineString)
                operand = JsonSerializer.Serialize(
                    md.GetUserString(
                        MetadataTokens.UserStringHandle(
                            BitConverter.ToInt32(bytes, offset) & 0x00ffffff
                        )
                    )
                );
            else if (op.OperandType == OperandType.InlineBrTarget)
                operand = $"IL_{offset + size + BitConverter.ToInt32(bytes, offset):x4}";
            else if (op.OperandType == OperandType.ShortInlineBrTarget)
                operand = $"IL_{offset + size + (sbyte)bytes[offset]:x4}";
            else if (size > 0)
                operand = Convert.ToHexString(bytes.AsSpan(offset, size));
            offset += size;
            yield return new(start, op.Name!, operand);
        }
    }

    static string Name(MetadataReader md, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                var td = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                return td.GetDeclaringType().IsNil
                    ? Qualified(md.GetString(td.Namespace), md.GetString(td.Name))
                    : Name(md, td.GetDeclaringType()) + "+" + md.GetString(td.Name);
            case HandleKind.TypeReference:
                var tr = md.GetTypeReference((TypeReferenceHandle)handle);
                return tr.ResolutionScope.Kind == HandleKind.TypeReference
                    ? Name(md, tr.ResolutionScope) + "+" + md.GetString(tr.Name)
                    : Qualified(md.GetString(tr.Namespace), md.GetString(tr.Name));
            case HandleKind.MethodDefinition:
                var method = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                return Name(md, method.GetDeclaringType()) + "::" + md.GetString(method.Name);
            case HandleKind.MemberReference:
                var member = md.GetMemberReference((MemberReferenceHandle)handle);
                return Name(md, member.Parent) + "::" + md.GetString(member.Name);
            case HandleKind.FieldDefinition:
                var field = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                return Name(md, field.GetDeclaringType()) + "::" + md.GetString(field.Name);
            case HandleKind.MethodSpecification:
                return Name(md, md.GetMethodSpecification((MethodSpecificationHandle)handle).Method)
                    + "<...>";
            default:
                return $"{handle.Kind}(0x{MetadataTokens.GetToken(handle):x8})";
        }
    }

    static string Qualified(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
}
