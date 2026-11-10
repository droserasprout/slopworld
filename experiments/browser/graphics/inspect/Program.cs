// Inspect managed PE metadata without loading assemblies or invoking native code.
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

var root = Path.GetFullPath(args[0]);
var reports = new List<object>();
foreach (var file in Directory.GetFiles(root, "*.dll").Order())
{
    var name = Path.GetFileName(file);
    if (!name.StartsWith("Waylith") && !name.StartsWith("Brutal"))
        continue;
    using var stream = File.OpenRead(file);
    using var pe = new PEReader(stream);
    if (!pe.HasMetadata)
        continue;
    var md = pe.GetMetadataReader();
    var signatureNames = new SignatureNames();
    string TypeName(TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        return md.GetString(type.Namespace) + "." + md.GetString(type.Name);
    }
    var imports = new List<object>();
    foreach (var handle in md.MethodDefinitions)
    {
        var method = md.GetMethodDefinition(handle);
        if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0)
            continue;
        var import = method.GetImport();
        imports.Add(
            new
            {
                type = TypeName(method.GetDeclaringType()),
                method = md.GetString(method.Name),
                library = md.GetString(md.GetModuleReference(import.Module).Name),
                entry = md.GetString(import.Name),
            }
        );
    }
    var types = md
        .TypeDefinitions.Select(handle =>
        {
            var type = md.GetTypeDefinition(handle);
            return new
            {
                name = TypeName(handle),
                methods = type.GetMethods()
                    .Select(h => md.GetString(md.GetMethodDefinition(h).Name))
                    .ToArray(),
                signatures = type.GetMethods()
                    .Select(h =>
                    {
                        var method = md.GetMethodDefinition(h);
                        var signature = method.DecodeSignature(signatureNames, (object?)null);
                        return signature.ReturnType
                            + " "
                            + md.GetString(method.Name)
                            + "("
                            + string.Join(", ", signature.ParameterTypes)
                            + ")";
                    })
                    .ToArray(),
            };
        })
        .Where(t =>
            name == "Waylith.dll"
            && new[]
            {
                "Graphics",
                "Render",
                "Vulkan",
                "Window",
                "Platform",
                "Shader",
                "App",
                "Input",
            }.Any(t.name.Contains)
        )
        .ToArray();
    var members = new List<object>();
    foreach (var handle in md.MemberReferences)
    {
        var member = md.GetMemberReference(handle);
        if (member.Parent.Kind != HandleKind.TypeReference)
            continue;
        var parent = md.GetTypeReference((TypeReferenceHandle)member.Parent);
        var ns = md.GetString(parent.Namespace);
        if (
            !ns.StartsWith("Brutal.Vulkan")
            && !ns.StartsWith("Brutal.Glfw")
            && !ns.StartsWith("Brutal.Slang")
            && !ns.StartsWith("System.Runtime.InteropServices")
        )
            continue;
        members.Add(
            new { type = ns + "." + md.GetString(parent.Name), name = md.GetString(member.Name) }
        );
    }
    reports.Add(
        new
        {
            assembly = name,
            sha256 = Convert
                .ToHexString(SHA256.HashData(File.ReadAllBytes(file)))
                .ToLowerInvariant(),
            references = md
                .AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name))
                .ToArray(),
            imports,
            types,
            members,
        }
    );
}
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(
    args[1],
    JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true })
);
Console.WriteLine($"Inspected {reports.Count} assemblies -> {args[1]}");

sealed class SignatureNames : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string element, ArrayShape shape) =>
        element + "[" + new string(',', shape.Rank - 1) + "]";

    public string GetByReferenceType(string element) => element + "&";

    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

    public string GetGenericInstantiation(
        string type,
        System.Collections.Immutable.ImmutableArray<string> args
    ) => type + "<" + string.Join(",", args) + ">";

    public string GetGenericMethodParameter(object? context, int index) => "!!" + index;

    public string GetGenericTypeParameter(object? context, int index) => "!" + index;

    public string GetModifiedType(string modifier, string element, bool required) => element;

    public string GetPinnedType(string element) => element;

    public string GetPointerType(string element) => element + "*";

    public string GetPrimitiveType(PrimitiveTypeCode type) => type.ToString();

    public string GetSZArrayType(string element) => element + "[]";

    public string GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte kind
    )
    {
        var type = reader.GetTypeDefinition(handle);
        return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte kind)
    {
        var type = reader.GetTypeReference(handle);
        return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
    }

    public string GetTypeFromSpecification(
        MetadataReader reader,
        object? context,
        TypeSpecificationHandle handle,
        byte kind
    ) => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
}
