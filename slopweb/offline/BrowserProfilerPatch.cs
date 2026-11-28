using Mono.Cecil;
using Mono.Cecil.Cil;

// The browser CLR has no WaylithClr profiler or method-body rewriting service.
// Remove their metadata as well as their calls: Mono AOT imports unused signatures.
internal static class BrowserProfilerPatch
{
    private const string Instrumentation = "Waylith.Profiling.Instrumentation.";

    internal static void Apply(string root, ModuleDefinition engine, IAssemblyResolver resolver)
    {
        DisableInstrumentation(engine);
        DisableRuntimeSamples(engine);
        var clock = engine.GetType("Waylith.Profiling.ProfilerClock");
        var timestamp = clock.Methods.Single(m => m.Name == "GetTimestamp");
        var stopwatch = Call(
            timestamp,
            "System.Int64 System.Diagnostics.Stopwatch::GetTimestamp()"
        );
        Replace(timestamp, Instruction.Create(OpCodes.Call, stopwatch));

        var path = Path.Combine(root, "game", "Waylith.Injection.dll");
        using var injection = AssemblyDefinition.ReadAssembly(
            path,
            new ReaderParameters { InMemory = true, AssemblyResolver = resolver }
        );
        GuardSplicer(
            injection.MainModule,
            "InjectionProcessor",
            "AddCtorSplices",
            "TryGetCallTokens"
        );
        GuardSplicer(injection.MainModule, "SplicePlan", "Build", "TrySplice");
        StagedAssembly.Write(injection, path);
    }

    private static void GuardSplicer(
        ModuleDefinition module,
        string owner,
        string method,
        string splicer
    )
    {
        var caller = module
            .GetType("Waylith.Injection." + owner)
            .Methods.Single(m => m.Name == method);
        _ = caller.Body.Instructions.Single(i =>
            i.OpCode == OpCodes.Call
            && i.Operand is MethodReference target
            && target.DeclaringType.FullName == Instrumentation + "ILBodySplicer"
            && target.Name == splicer
        );
        caller.Body = new MethodBody(caller);
        var il = caller.Body.GetILProcessor();
        il.Emit(OpCodes.Ldstr, "Runtime field injection is unavailable in the browser.");
        il.Emit(
            OpCodes.Newobj,
            module.ImportReference(
                typeof(PlatformNotSupportedException).GetConstructor([typeof(string)])
            )
        );
        il.Emit(OpCodes.Throw);
    }

    private static void DisableInstrumentation(ModuleDefinition module)
    {
        var facade = module.GetType(Instrumentation + "ProfilerInstrumentation");
        foreach (
            var name in new[] { "ILBodySplicer", "MetadataMatcher", "InstrumentationRewriter" }
        )
        {
            var type = module.GetType(Instrumentation + name);
            if (type == null || !module.Types.Remove(type))
                throw new InvalidOperationException($"Missing Waylith instrumentation type {name}");
        }
        facade.NestedTypes.Clear();
        // Keep the public profiler API for the game settings UI. Ordinary
        // backing fields still serve the settings, events and disabled counters.
        foreach (var field in facade.Fields.ToArray())
        {
            if (
                field.IsPublic
                || field.Name.StartsWith('<')
                || field.Name is "targets" or "InstrumentedMethodsChanged"
            )
                continue;
            facade.Fields.Remove(field);
        }
        foreach (var method in facade.Methods.ToArray())
        {
            switch (method.Name)
            {
                case ".cctor":
                    var empty = Call(method, "!!0[] System.Array::Empty<System.String>()");
                    var fields = new[] { "targets", "<ExcludedAssemblies>k__BackingField" };
                    method.Body = new MethodBody(method);
                    var il = method.Body.GetILProcessor();
                    foreach (var name in fields)
                    {
                        il.Emit(OpCodes.Call, empty);
                        il.Emit(OpCodes.Stsfld, facade.Fields.Single(f => f.Name == name));
                    }
                    il.Emit(OpCodes.Ret);
                    break;
                case "get_InstrumentedMethodCount":
                case "get_MutedMethodCount":
                    Replace(method, Instruction.Create(OpCodes.Ldc_I4_0));
                    break;
                case "OnProfilerFrameStarted":
                case "SetEnabled":
                    Replace(method);
                    break;
                case "SetTargets":
                    var copy = Call(
                        method,
                        "!!0[] System.Linq.Enumerable::ToArray<System.String>(System.Collections.Generic.IEnumerable`1<!!0>)"
                    );
                    Replace(
                        method,
                        Instruction.Create(OpCodes.Ldarg_0),
                        Instruction.Create(OpCodes.Call, copy),
                        Instruction.Create(
                            OpCodes.Stsfld,
                            facade.Fields.Single(f => f.Name == "targets")
                        )
                    );
                    break;
                case "CountMatches":
                    // The UI expects one match count per pattern and an out total.
                    var count = Call(
                        method,
                        "System.Int32 System.Collections.Generic.IReadOnlyCollection`1<System.String>::get_Count()"
                    );
                    Replace(
                        method,
                        Instruction.Create(OpCodes.Ldarg_1),
                        Instruction.Create(OpCodes.Ldc_I4_0),
                        Instruction.Create(OpCodes.Stind_I4),
                        Instruction.Create(OpCodes.Ldarg_0),
                        Instruction.Create(OpCodes.Callvirt, count),
                        Instruction.Create(OpCodes.Newarr, module.TypeSystem.Int32)
                    );
                    break;
                default:
                    if (method.IsGetter || method.IsSetter || method.IsAddOn || method.IsRemoveOn)
                    {
                        if (method.Name != "get_Transformer")
                            break;
                        facade.Properties.Remove(
                            facade.Properties.Single(p => p.GetMethod == method)
                        );
                    }
                    if (method.IsPublic)
                        throw new InvalidOperationException(
                            $"Unrecognized Waylith profiler API: {method.FullName}"
                        );
                    facade.Methods.Remove(method);
                    break;
            }
        }
    }

    private static void DisableRuntimeSamples(ModuleDefinition module)
    {
        var frame = module.GetType("Waylith.Profiling.FrameProfiler");
        Replace(frame.Methods.Single(m => m.Name == "SyncAllocationSubscription"));
        var worker = module.GetType("Waylith.Profiling.ProfilerWorker");
        Replace(worker.Methods.Single(m => m.Name == "DrainRuntimePauses"));

        // Keep the rest of the worker's static initialization, including its queues.
        var scratch = worker.Fields.Single(f => f.Name == "PauseDrainScratch");
        var init = worker.Methods.Single(m => m.IsConstructor && m.IsStatic).Body.Instructions;
        var store = init.Single(i => i.OpCode == OpCodes.Stsfld && i.Operand == scratch);
        var array = store.Previous;
        if (
            array.OpCode != OpCodes.Newarr
            || ((TypeReference)array.Operand).FullName != "WaylithClr.SelfProfiler/RuntimePause"
            || array.Previous.OpCode != OpCodes.Ldc_I4
        )
            throw new InvalidOperationException(
                "Waylith profiler pause buffer initializer changed"
            );
        init.Remove(array.Previous);
        init.Remove(array);
        init.Remove(store);
        worker.Fields.Remove(scratch);
    }

    private static MethodReference Call(MethodDefinition method, string signature) =>
        (MethodReference)
            method
                .Body.Instructions.First(i =>
                    (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                    && i.Operand is MethodReference target
                    && target.FullName == signature
                )
                .Operand;

    private static void Replace(MethodDefinition method, params Instruction[] instructions)
    {
        method.Body = new MethodBody(method);
        foreach (var instruction in instructions)
            method.Body.Instructions.Add(instruction);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    }
}
