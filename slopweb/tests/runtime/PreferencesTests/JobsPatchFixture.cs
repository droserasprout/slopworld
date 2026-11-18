using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldDefinition = Mono.Cecil.FieldDefinition;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using ModuleDefinition = Mono.Cecil.ModuleDefinition;
using TypeDefinition = Mono.Cecil.TypeDefinition;
using TypeReference = Mono.Cecil.TypeReference;

static class JobsPatchFixture
{
    public static void Check()
    {
        CheckLayout(false);
        CheckLayout(true);
        CheckLayout(true, malformed: true);
    }

    private static void CheckLayout(bool reclaimed, bool malformed = false)
    {
        using var module = ModuleDefinition.CreateModule("JobsFixture", ModuleKind.Dll);
        var owner = new TypeDefinition(
            "Waylith.Jobs",
            "WaylithJobRuntime",
            TypeAttributes.Public,
            module.TypeSystem.Object
        );
        module.Types.Add(owner);
        var state = new TypeDefinition(
            "",
            "HandleState",
            TypeAttributes.NestedPublic,
            module.TypeSystem.Object
        );
        owner.NestedTypes.Add(state);
        FieldDefinition Field(TypeDefinition type, string name, TypeReference value, bool isStatic)
        {
            var field = new FieldDefinition(
                name,
                FieldAttributes.Public | (isStatic ? FieldAttributes.Static : 0),
                value
            );
            type.Fields.Add(field);
            return field;
        }
        MethodDefinition Method(
            TypeDefinition type,
            string name,
            TypeReference result,
            bool isStatic
        )
        {
            var method = new MethodDefinition(
                name,
                MethodAttributes.Public | (isStatic ? MethodAttributes.Static : 0),
                result
            );
            type.Methods.Add(method);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            return method;
        }
        var workers = Field(owner, "Workers", new ArrayType(module.TypeSystem.Object), true);
        var bag = Field(owner, "WorkBag", module.TypeSystem.Object, true);
        var available = Field(owner, "WorkAvailable", module.TypeSystem.Object, true);
        var refs = Field(state, "BagRefs", module.TypeSystem.Int32, false);
        var count = Method(state, "get_RunCount", module.TypeSystem.Int32, false);
        Method(state, "Execute", module.TypeSystem.Void, false);
        var execute = Method(owner, "ExecuteFromBag", module.TypeSystem.Void, true);
        execute.Parameters.Add(new ParameterDefinition(state));
        Method(owner, "get_WorkerCount", module.TypeSystem.Int32, true);
        var initializer = Method(owner, ".cctor", module.TypeSystem.Void, true);
        initializer.Body.Instructions.Clear();
        initializer.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
        var il = initializer.Body.GetILProcessor();
        for (var i = 0; i < (reclaimed ? 0x7a : 0x57); i++)
            il.Emit(OpCodes.Nop);
        il.Emit(malformed ? OpCodes.Ldc_I4_2 : OpCodes.Ldc_I4_1);
        il.Emit(
            OpCodes.Call,
            module.ImportReference(typeof(Environment).GetProperty("ProcessorCount")!.GetMethod!)
        );
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(
            OpCodes.Call,
            module.ImportReference(typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!)
        );
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Newarr, module.TypeSystem.Object);
        il.Emit(OpCodes.Stsfld, workers);
        while (initializer.Body.Instructions.Sum(i => i.GetSize()) < (reclaimed ? 265 : 230))
            il.Emit(OpCodes.Nop);
        il.Emit(OpCodes.Ret);

        var submit = Method(owner, "Submit", module.TypeSystem.Void, true);
        submit.Parameters.Add(new ParameterDefinition(state));
        submit.Body.Instructions.Clear();
        submit.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
        submit.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
        il = submit.Body.GetILProcessor();
        var add = new MethodReference("Add", module.TypeSystem.Void, module.TypeSystem.Object)
        {
            HasThis = true,
        };
        add.Parameters.Add(new ParameterDefinition(state));
        var release = new MethodReference(
            "Release",
            module.TypeSystem.Int32,
            module.TypeSystem.Object
        )
        {
            HasThis = true,
        };
        release.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        if (reclaimed)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, count);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, refs);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(
                OpCodes.Call,
                module.ImportReference(
                    typeof(Interlocked).GetMethod(
                        "Add",
                        [typeof(int).MakeByRefType(), typeof(int)]
                    )!
                )
            );
            il.Emit(OpCodes.Pop);
        }
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(reclaimed ? OpCodes.Stloc_1 : OpCodes.Stloc_0);
        var loop = Instruction.Create(OpCodes.Ldsfld, bag);
        var condition = Instruction.Create(reclaimed ? OpCodes.Ldloc_1 : OpCodes.Ldloc_0);
        il.Emit(OpCodes.Br_S, condition);
        il.Append(loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, add);
        il.Emit(reclaimed ? OpCodes.Ldloc_1 : OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(reclaimed ? OpCodes.Stloc_1 : OpCodes.Stloc_0);
        il.Append(condition);
        if (reclaimed)
            il.Emit(OpCodes.Ldloc_0);
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, count);
        }
        il.Emit(OpCodes.Blt_S, loop);
        il.Emit(OpCodes.Ldsfld, available);
        il.Emit(reclaimed ? OpCodes.Ldloc_0 : OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, release);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        // The older initializer's release tail had five additional bytes.
        if (!reclaimed)
            for (var i = 0; i < 5; i++)
                submit.Body.Instructions.Insert(
                    submit.Body.Instructions.Count - 1,
                    Instruction.Create(OpCodes.Nop)
                );

        using var stream = new MemoryStream();
        module.Write(stream);
        var bytes = stream.ToArray();
        using var pe = new PEReader(new MemoryStream(bytes));
        var md = pe.GetMetadataReader();
        var changed = new Dictionary<string, byte[]>();
        try
        {
            BrowserJobsPatch.Apply(
                bytes,
                pe,
                md,
                (_, _, method, body) => changed.Add(md.GetString(method.Name), body)
            );
            if (malformed)
                throw new Exception("Unknown initializer was accepted");
        }
        catch (InvalidOperationException) when (malformed)
        {
            return;
        }
        var start = reclaimed ? 0x7a : 0x57;
        if (
            changed[".cctor"][start] != 0x16
            || !changed[".cctor"].AsSpan(start + 1, 12).ToArray().All(b => b == 0)
        )
            throw new Exception("Worker threads were not disabled");
        var patched = changed["Submit"];
        if (reclaimed)
        {
            var original = pe.GetMethodBody(
                    md.GetMethodDefinition(
                        (MethodDefinitionHandle)
                            System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(
                                submit.MetadataToken.ToInt32()
                            )
                    ).RelativeVirtualAddress
                )
                .GetILBytes()!;
            if (!patched.AsSpan(0, 0x18).SequenceEqual(original.AsSpan(0, 0x18)))
                throw new Exception("BagRefs reservation changed");
            if (
                patched[0x1e] != 0x28
                || BitConverter.ToInt32(patched, 0x1f) != execute.MetadataToken.ToInt32()
            )
                throw new Exception("BagRefs decrement was bypassed");
        }
        if (patched[reclaimed ? 0x2b : 0x1c] != 0x2a)
            throw new Exception("Submission still signals background workers");
        if (!changed["get_WorkerCount"].SequenceEqual(new byte[] { 0x17, 0x2a }))
            throw new Exception("Serial lane count changed");
    }
}
