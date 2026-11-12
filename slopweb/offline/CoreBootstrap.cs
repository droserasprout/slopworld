using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Staged Core-only startup adaptations. Preserve installed loading and write
// algorithms; reject mod injection and expose unavailable desktop services.
internal static class CoreBootstrap
{
    internal static void Apply(string root, string probePath)
    {
        var path = Path.Combine(root, "game", "RimWorld.dll");
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.Combine(root, "game"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location));
        using var target = AssemblyDefinition.ReadAssembly(
            path,
            new ReaderParameters { InMemory = true, AssemblyResolver = resolver }
        );
        using var probe = AssemblyDefinition.ReadAssembly(probePath);
        var adapter = probe.MainModule.Types.Single(t => t.Name == "Probe");
        var initialize = target
            .MainModule.Types.Single(t => t.FullName == "RimWorldWaylith.Bootstrap")
            .Methods.Single(m => m.Name == "Initialize");
        var replacements = new Dictionary<string, string>
        {
            ["Waylith.Injection.Injection::ApplyPendingAtFrameBoundary"] = "RequireCoreOnly",
            ["RimWorldWaylith.PrepatcherCompat::Install"] = "RequireCoreOnly",
            ["RimWorldWaylith.PrepatcherCompat::ProcessModAssemblies"] = "RejectModAssemblies",
        };
        foreach (var (original, replacement) in replacements)
        {
            var site = initialize.Body.Instructions.Single(i =>
                i.Operand is MethodReference m
                && m.DeclaringType.FullName + "::" + m.Name == original
            );
            var old = (MethodReference)site.Operand;
            var method = adapter.Methods.Single(m => m.Name == replacement);
            if (
                old.ReturnType.FullName != method.ReturnType.FullName
                || !old
                    .Parameters.Select(p => p.ParameterType.FullName)
                    .SequenceEqual(method.Parameters.Select(p => p.ParameterType.FullName))
            )
                throw new InvalidOperationException("Core bootstrap callback signature changed");
            site.Operand = target.MainModule.ImportReference(method);
        }
        BrowserTextLayoutPatch.Apply(target, root);
        target.Write(path + ".partial");
        File.Move(path + ".partial", path, true);
        var manifestPath = Path.Combine(root, "game", "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsArray();
        manifest.Single(n => n!["file"]!.GetValue<string>() == "RimWorld.dll")!["stagedSha256"] =
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        var adaptationsPath = Path.Combine(root, "game", "adaptations.json");
        var adaptations = JsonNode.Parse(File.ReadAllText(adaptationsPath))!.AsArray();
        adaptations.Add(
            "Core-only bootstrap: custom CLR injection callbacks replaced with guards rejecting mod assemblies"
        );
        adaptations.Add(BrowserTextLayoutPatch.Description);
        File.WriteAllText(adaptationsPath, adaptations.ToJsonString());
        PatchRuntimeServices(root, resolver);
        PatchCoreLoading(root, resolver);
        BrowserGuiPatch.Apply(root, resolver);
    }

    private static void PatchCoreLoading(string root, IAssemblyResolver resolver)
    {
        var path = Path.Combine(root, "game", "Assembly-CSharp.dll");
        using var target = AssemblyDefinition.ReadAssembly(
            path,
            new ReaderParameters { InMemory = true, AssemblyResolver = resolver }
        );
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        DisableStaticAtlasBatching(target);
        DisablePawnAtlasCaching(target);
        var constructors = target
            .MainModule.Types.Single(t => t.FullName == "Verse.StaticConstructorOnStartupUtility")
            .Methods.Single(m => m.Name == "CallAll");
        var runConstructor = constructors.Body.Instructions.Single(i =>
            i.Operand is MethodReference m
            && m.DeclaringType.FullName == "System.Runtime.CompilerServices.RuntimeHelpers"
            && m.Name == "RunClassConstructor"
        );
        runConstructor.Operand = target.MainModule.ImportReference(
            adapters
                .MainModule.Types.Single(t => t.Name == "RuntimeServices")
                .Methods.Single(m => m.Name == "RunStartupConstructor")
        );
        var quickTest = target
            .MainModule.Types.Single(t => t.FullName == "Verse.Root_Play")
            .Methods.Single(m => m.Name == "SetupForQuickTestPlay");
        var traceStage = target.MainModule.ImportReference(
            adapters
                .MainModule.Types.Single(t => t.Name == "RuntimeServices")
                .Methods.Single(m => m.Name == "TraceLoadingStage")
        );
        var quickIl = quickTest.Body.GetILProcessor();
        foreach (
            var site in quickTest
                .Body.Instructions.Where(i =>
                    i.Operand is MethodReference m
                    && new[]
                    {
                        "ClearCaches",
                        "PreConfigure",
                        "RandomSeedString",
                        "GenerateWorld",
                        "ChooseRandomStartingTile",
                        "PostIdeoChosen",
                    }.Contains(m.Name)
                )
                .ToArray()
        )
        {
            quickIl.InsertBefore(
                site,
                quickIl.Create(OpCodes.Ldstr, "quick test: " + ((MethodReference)site.Operand).Name)
            );
            quickIl.InsertBefore(site, quickIl.Create(OpCodes.Call, traceStage));
        }
        ExpandBranches(quickTest);
        TraceLoopIndex(
            target,
            adapters,
            "RimWorld.Planet.PlanetLayer",
            "InitializeLayer",
            "Subdivide",
            "TraceSubdivision"
        );
        TraceLoopIndex(
            target,
            adapters,
            "RimWorld.Planet.WorldGenStep_Tiles",
            "GenerateFresh",
            "GenerateTileFor",
            "TraceTile"
        );
        var forEach = target
            .MainModule.Types.Single(t => t.FullName == "Verse.GenThreading")
            .Methods.Single(m => m.Name == "ParallelForEach");
        if (
            forEach.GenericParameters.Count != 1
            || forEach.Parameters.Count != 3
            || !forEach.Body.Instructions.Any(i =>
                i.Operand is MethodReference m && m.Name == "WaitOne"
            )
        )
            throw new InvalidOperationException("GenThreading parallel helper contract changed");
        var serial = new GenericInstanceMethod(
            target.MainModule.ImportReference(
                adapters
                    .MainModule.Types.Single(t => t.Name == "SerialWork")
                    .Methods.Single(m => m.Name == "ForEach")
            )
        );
        serial.GenericArguments.Add(forEach.GenericParameters[0]);
        forEach.Body = new MethodBody(forEach);
        var serialIl = forEach.Body.GetILProcessor();
        serialIl.Emit(OpCodes.Ldarg_0);
        serialIl.Emit(OpCodes.Ldarg_1);
        serialIl.Emit(OpCodes.Ldarg_2);
        serialIl.Emit(OpCodes.Call, serial);
        serialIl.Emit(OpCodes.Ret);
        var parallelFor = target
            .MainModule.Types.Single(t => t.FullName == "Verse.GenThreading")
            .Methods.Single(m => m.Name == "ParallelFor");
        var queue = parallelFor.Body.Instructions.Single(i =>
            i.Operand is MethodReference m
            && m.DeclaringType.FullName == "System.Threading.ThreadPool"
            && m.Name == "QueueUserWorkItem"
        );
        var queuedMethod = (MethodReference)queue.Operand;
        if (
            queuedMethod.Parameters.Count != 1
            || queuedMethod.Parameters[0].ParameterType.FullName != "System.Threading.WaitCallback"
            || queuedMethod.ReturnType.FullName != "System.Boolean"
        )
            throw new InvalidOperationException("GenThreading range scheduler signature changed");
        queue.Operand = target.MainModule.ImportReference(
            adapters
                .MainModule.Types.Single(t => t.Name == "RuntimeServices")
                .Methods.Single(m => m.Name == "RunInline")
        );
        var events = target.MainModule.Types.Single(t => t.FullName == "Verse.LongEventHandler");
        var update = events.Methods.Single(m => m.Name == "LongEventsUpdate");
        var synchronous = events.Methods.Single(m => m.Name == "UpdateCurrentSynchronousEvent");
        var asyncCall = update.Body.Instructions.Single(i =>
            i.Operand is MethodReference m && m.Name == "UpdateCurrentAsynchronousEvent"
        );
        // Retain the queue, action, exception, completion and scene-change owners.
        // The async flag stays set, so this branch does not wait for GUI display.
        update.Body.GetILProcessor().InsertBefore(asyncCall, Instruction.Create(OpCodes.Ldarg_0));
        asyncCall.Operand = synchronous;
        ExpandBranches(update);

        var xml = target
            .MainModule.Types.Single(t => t.FullName == "Verse.DirectXmlLoader")
            .Methods.Single(m => m.Name == "XmlAssetsInModFolder");
        var threads = xml.Body.Instructions.Single(i =>
            i.OpCode == OpCodes.Newarr
            && i.Operand is TypeReference t
            && t.FullName == "System.Threading.Thread"
        );
        if (threads.Previous.OpCode != OpCodes.Ldc_I4_2)
            throw new InvalidOperationException("XML loader worker count changed");
        // The original calling-thread drain still loads every item in its bag.
        threads.Previous.OpCode = OpCodes.Ldc_I4_0;

        var steam = target
            .MainModule.Types.Single(t => t.FullName == "Verse.Steam.SteamManager")
            .Methods.Single(m => m.Name == "InitIfNeeded");
        steam.Body = new MethodBody(steam);
        var il = steam.Body.GetILProcessor();
        il.Emit(
            OpCodes.Call,
            target.MainModule.ImportReference(
                adapters
                    .MainModule.Types.Single(t => t.Name == "RuntimeServices")
                    .Methods.Single(m => m.Name == "DesktopSteamUnavailable")
            )
        );
        il.Emit(OpCodes.Ret);
        // Coarse installed loader stages remain observable while a synchronous
        // interpreter frame occupies the browser thread. Do not log XML nodes.
        foreach (
            var (type, method) in new[]
            {
                ("Verse.PlayDataLoader", "DoPlayLoad"),
                ("Verse.LoadedModManager", "LoadAllActiveMods"),
                ("RimWorld.Planet.WorldGenerator", "GenerateWorld"),
                ("RimWorld.Planet.WorldGenerator", "GeneratePlanetLayer"),
                ("Verse.MapGenerator", "GenerateMap"),
                ("Verse.MapGenerator", "GenerateContentsIntoMap"),
            }
        )
        {
            var body = target
                .MainModule.Types.Single(t => t.FullName == type)
                .Methods.Single(m => m.Name == method)
                .Body;
            var processor = body.GetILProcessor();
            foreach (
                var start in body
                    .Instructions.Where(i =>
                        i.Operand is MethodReference m
                        && m.DeclaringType.FullName == "Verse.DeepProfiler"
                        && m.Name == "Start"
                    )
                    .ToArray()
            )
            {
                processor.InsertBefore(start, processor.Create(OpCodes.Dup));
                processor.InsertBefore(
                    start,
                    processor.Create(
                        OpCodes.Call,
                        target.MainModule.ImportReference(
                            adapters
                                .MainModule.Types.Single(t => t.Name == "RuntimeServices")
                                .Methods.Single(m => m.Name == "TraceLoadingStage")
                        )
                    )
                );
            }
            ExpandBranches(body.Method);
        }
        target.Write(path + ".partial");
        File.Move(path + ".partial", path, true);
        var manifestPath = Path.Combine(root, "game", "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsArray();
        manifest.Single(n => n!["file"]!.GetValue<string>() == "Assembly-CSharp.dll")![
            "stagedSha256"
        ] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        var adaptationsPath = Path.Combine(root, "game", "adaptations.json");
        var adaptations = JsonNode.Parse(File.ReadAllText(adaptationsPath))!.AsArray();
        adaptations.Add(
            "Long events use the installed synchronous executor; XML loading retains its calling-thread drain without workers; desktop Steam disabled"
        );
        adaptations.Add(
            "GenThreading.ParallelForEach executes serially, retaining per-item exception logging and continuation"
        );
        adaptations.Add(
            "GenThreading.ParallelFor dispatches original batches inline; world/map generation stages are traced"
        );
        adaptations.Add(
            "Static atlas admission disabled: original material/UV fallback avoids unbuilt native GPU atlas pages"
        );
        adaptations.Add(
            "Pawn pre-render uses its existing disableCache argument, retaining direct body/apparel rendering without native offscreen atlases"
        );
        File.WriteAllText(adaptationsPath, adaptations.ToJsonString());
    }

    private static void DisableStaticAtlasBatching(AssemblyDefinition target)
    {
        var admission = target
            .MainModule.Types.Single(t => t.FullName == "Verse.GlobalTextureAtlasManager")
            .Methods.Single(m => m.Name == "TryInsertStatic");
        if (
            !admission.IsStatic
            || admission.ReturnType.FullName != "System.Boolean"
            || !admission
                .Parameters.Select(p => p.ParameterType.FullName)
                .SequenceEqual(
                    new[]
                    {
                        "Verse.TextureAtlasGroup",
                        "UnityEngine.Texture2D",
                        "UnityEngine.Texture2D",
                    }
                )
        )
            throw new InvalidOperationException("Static atlas admission contract changed");
        // The native baker blits into GPU-only pages. Reject this optional
        // batching optimization before tiles/materials are cached; Graphic's
        // existing fallback retains original textures, UVs and vertex colors.
        admission.Body = new MethodBody(admission);
        var il = admission.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private static void DisablePawnAtlasCaching(AssemblyDefinition target)
    {
        var prepare = target
            .MainModule.Types.Single(t => t.FullName == "Verse.PawnRenderer")
            .Methods.Single(m => m.Name == "ParallelGetPreRenderResults");
        var disableCache = prepare.Parameters.Single(p => p.Name == "disableCache");
        if (disableCache.ParameterType.FullName != "System.Boolean" || !disableCache.IsOptional)
            throw new InvalidOperationException("Pawn renderer cache policy contract changed");
        // Select the original direct-render path before it prepares the render
        // tree. Rejecting a frame-set lookup later would omit the pawn entirely.
        var first = prepare.Body.Instructions[0];
        var il = prepare.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(first, il.Create(OpCodes.Starg, disableCache));
    }

    private static void ExpandBranches(MethodDefinition method)
    {
        // Cecil preserves short branch encodings. Inserted tracing/callback IL
        // must not push a target beyond its signed-byte displacement.
        var codes = typeof(OpCodes)
            .GetFields()
            .Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(code => code.Name);
        foreach (var instruction in method.Body.Instructions)
            if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                instruction.OpCode = codes[instruction.OpCode.Name[..^2]];
    }

    private static void TraceLoopIndex(
        AssemblyDefinition target,
        AssemblyDefinition adapters,
        string owner,
        string methodName,
        string callName,
        string traceName
    )
    {
        var method = target
            .MainModule.Types.Single(t => t.FullName == owner)
            .Methods.Single(m => m.Name == methodName);
        var index = method.Body.Variables.Single(v => v.VariableType.FullName == "System.Int32");
        var site = method.Body.Instructions.Single(i =>
            i.Operand is MethodReference m && m.Name == callName
        );
        var trace = target.MainModule.ImportReference(
            adapters
                .MainModule.Types.Single(t => t.Name == "RuntimeServices")
                .Methods.Single(m => m.Name == traceName)
        );
        var il = method.Body.GetILProcessor();
        il.InsertBefore(site, il.Create(OpCodes.Ldloc, index));
        il.InsertBefore(site, il.Create(OpCodes.Call, trace));
        ExpandBranches(method);
    }

    private static void PatchRuntimeServices(string root, IAssemblyResolver resolver)
    {
        var path = Path.Combine(root, "game", "Waylith.dll");
        using var target = AssemblyDefinition.ReadAssembly(
            path,
            new ReaderParameters { InMemory = true, AssemblyResolver = resolver }
        );
        using var adapters = AssemblyDefinition.ReadAssembly(Path.Combine(root, "Adapters.dll"));
        var applyTexture = target
            .MainModule.Types.Single(t => t.FullName == "Waylith.Rendering.Texture2D")
            .Methods.Single(m => m.Name == "Apply");
        var discardPixels = applyTexture.Parameters.Single(p => p.Name == "makeNoLongerReadable");
        if (discardPixels.ParameterType.FullName != "System.Boolean")
            throw new InvalidOperationException("Texture CPU retention contract changed");
        // WebGL uploads from CPU storage after capture. The headless native
        // upload cannot retain procedural pixels for us; disposal still releases
        // them through the original texture lifetime owner.
        var applyIl = applyTexture.Body.GetILProcessor();
        var applyFirst = applyTexture.Body.Instructions[0];
        applyIl.InsertBefore(applyFirst, applyIl.Create(OpCodes.Ldc_I4_0));
        applyIl.InsertBefore(applyFirst, applyIl.Create(OpCodes.Starg, discardPixels));
        var owner = target.MainModule.Types.Single(t =>
            t.FullName == "Waylith.Runtime.CacheFileWriter"
        );
        var init = owner.Methods.Single(m => m.IsConstructor && m.IsStatic);
        var bound = init.Body.Instructions.Single(i => i.Offset == 0x7d);
        if (bound.OpCode != OpCodes.Ldc_I4_2 || bound.Next.OpCode != OpCodes.Blt_S)
            throw new InvalidOperationException("Cache writer worker loop changed");
        bound.OpCode = OpCodes.Ldc_I4_0;
        var enqueue = owner.Methods.Single(m => m.Name == "Enqueue");
        var replacement = adapters
            .MainModule.Types.Single(t => t.Name == "RuntimeServices")
            .Methods.Single(m => m.Name == "EnqueueCache");
        enqueue.Body = new MethodBody(enqueue);
        var il = enqueue.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, target.MainModule.ImportReference(replacement));
        il.Emit(OpCodes.Ret);
        // The browser VFS mapping has no OS resident-page advice API. Retain the
        // readable mapping; desktop staged execution keeps its original hint.
        var drop = target
            .MainModule.Types.Single(t => t.FullName == "Waylith.Assets.AssetPack")
            .Methods.Single(m => m.Name == "DropResidentPages");
        var dropIl = drop.Body.GetILProcessor();
        var original = drop.Body.Instructions[0];
        dropIl.InsertBefore(
            original,
            dropIl.Create(
                OpCodes.Call,
                target.MainModule.ImportReference(
                    typeof(OperatingSystem).GetMethod("IsBrowser", Type.EmptyTypes)
                )
            )
        );
        dropIl.InsertBefore(original, dropIl.Create(OpCodes.Brfalse, original));
        dropIl.InsertBefore(original, dropIl.Create(OpCodes.Ret));
        ExpandBranches(drop);
        target.Write(path + ".partial");
        File.Move(path + ".partial", path, true);
        var manifestPath = Path.Combine(root, "game", "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsArray();
        manifest.Single(n => n!["file"]!.GetValue<string>() == "Waylith.dll")!["stagedSha256"] =
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        var adaptationsPath = Path.Combine(root, "game", "adaptations.json");
        var adaptations = JsonNode.Parse(File.ReadAllText(adaptationsPath))!.AsArray();
        adaptations.Add(
            "Cache writes execute serially using the installed atomic writer; zero cache worker threads"
        );
        adaptations.Add(
            "AssetPack retains its readable browser mapping without native OS resident-page advice"
        );
        adaptations.Add(
            "Texture2D.Apply retains CPU pixels for WebGL capture instead of discarding them after headless native upload"
        );
        File.WriteAllText(adaptationsPath, adaptations.ToJsonString());
    }
}
