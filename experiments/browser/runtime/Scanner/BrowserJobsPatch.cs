using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Local-only scheduling adaptation. Keep installed HandleState dependency,
// completion, temporary-allocation, and exception ownership intact.
internal static class BrowserJobsPatch
{
    public static void Apply(
        byte[] bytes,
        PEReader pe,
        MetadataReader md,
        Action<byte[], PEReader, MethodDefinition, byte[]> replace
    )
    {
        var owner = md
            .TypeDefinitions.Select(md.GetTypeDefinition)
            .Single(t =>
                md.GetString(t.Namespace) == "Waylith.Jobs"
                && md.GetString(t.Name) == "WaylithJobRuntime"
            );
        MethodDefinition Method(string name) =>
            owner
                .GetMethods()
                .Select(md.GetMethodDefinition)
                .Single(m => md.GetString(m.Name) == name);
        int Field(string name) =>
            MetadataTokens.GetToken(
                owner.GetFields().Single(h => md.GetString(md.GetFieldDefinition(h).Name) == name)
            );
        var state = owner
            .GetNestedTypes()
            .Select(md.GetTypeDefinition)
            .Single(t => md.GetString(t.Name) == "HandleState");
        int StateMethod(string name) =>
            MetadataTokens.GetToken(
                state.GetMethods().Single(h => md.GetString(md.GetMethodDefinition(h).Name) == name)
            );
        var initializer = Method(".cctor");
        var il = pe.GetMethodBody(initializer.RelativeVirtualAddress).GetILBytes()!;
        // Inspected worker-count expression is Max(1, ProcessorCount - 1).
        // Replacing its result with zero preserves all collections and creates an
        // empty Workers array, so original shutdown never joins a fake thread.
        if (
            il.Length != 231
            || il[0x57] != 0x17
            || il[0x58] != 0x28
            || il[0x5d] != 0x17
            || il[0x5e] != 0x59
            || il[0x5f] != 0x28
            || il[0x64] != 0x0a
            || il[0x66] != 0x8d
            || il[0x6b] != 0x80
            || BitConverter.ToInt32(il, 0x6c) != Field("Workers")
        )
            throw new InvalidOperationException(
                "Unrecognized job initializer; refusing adaptation"
            );
        Array.Clear(il, 0x57, 13);
        il[0x57] = 0x16; // ldc.i4.0
        replace(bytes, pe, initializer, il);

        var submit = Method("Submit");
        il = pe.GetMethodBody(submit.RelativeVirtualAddress).GetILBytes()!;
        if (
            il.Length != 46
            || il[4] != 0x7e
            || BitConverter.ToInt32(il, 5) != Field("WorkBag")
            || il[9] != 0x02
            || il[10] != 0x6f
            || il[0x15] != 0x6f
            || BitConverter.ToInt32(il, 0x16) != StateMethod("get_RunCount")
            || il[0x1c] != 0x7e
            || BitConverter.ToInt32(il, 0x1d) != Field("WorkAvailable")
        )
            throw new InvalidOperationException("Unrecognized job submission; refusing adaptation");
        // Retain the original RunCount loop. Execute each chunk instead of adding
        // it to WorkBag; HandleState still claims runs and dispatches continuations.
        Array.Clear(il, 4, 5);
        BitConverter.GetBytes(StateMethod("Execute")).CopyTo(il, 11);
        Array.Clear(il, 0x1c, il.Length - 0x1c);
        il[0x1c] = 0x2a;
        replace(bytes, pe, submit, il);
        // Batch sizing must not divide by zero. This denotes one execution lane,
        // not one background worker; Workers itself stays empty.
        replace(bytes, pe, Method("get_WorkerCount"), new byte[] { 0x17, 0x2a });
    }
}
