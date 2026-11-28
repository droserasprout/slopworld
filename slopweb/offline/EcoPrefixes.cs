// Compile the explicitly selected desktop Harmony prefixes into the private game copy.
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class EcoPrefixes
{
    private const string Marker = "SlopWorld.EcoPrefixAttribute";
    private const string Patch = "HarmonyLib.HarmonyPatch";
    private const string Priority = "HarmonyLib.HarmonyPriority";

    private sealed record Hook(
        MethodDefinition Target,
        MethodDefinition Prefix,
        Instruction[] Loads
    );

    internal static void Apply(AssemblyDefinition game, AssemblyDefinition mod, List<string> hooks)
    {
        var selected = mod
            .MainModule.GetTypes()
            .Where(type => type.CustomAttributes.Any(a => a.AttributeType.FullName == Marker))
            .Select(type => ReadHook(game.MainModule, type))
            .ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException("No Eco prefixes found in " + mod.Name.Name);
        // One selected hook per target makes every HarmonyPriority (including Last) unambiguous.
        foreach (var group in selected.GroupBy(h => h.Target))
            if (group.Count() > 1)
                throw new InvalidOperationException(
                    "Multiple Eco prefixes for " + group.Key.FullName
                );
        foreach (var hook in selected)
        {
            var prefix = hook.Prefix;
            prefix.IsPublic = true;
            var owner = prefix.DeclaringType;
            while (owner.IsNested)
            {
                owner.IsNestedPublic = true;
                owner = owner.DeclaringType;
            }
            owner.IsPublic = true;
            var il = hook.Target.Body.GetILProcessor();
            var first = hook.Target.Body.Instructions[0];
            foreach (var load in hook.Loads)
                il.InsertBefore(first, load);
            il.InsertBefore(
                first,
                il.Create(OpCodes.Call, game.MainModule.ImportReference(prefix))
            );
            if (prefix.ReturnType.MetadataType == MetadataType.Boolean)
            {
                il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
                il.InsertBefore(first, il.Create(OpCodes.Ret));
            }
            hooks.Add(hook.Target.FullName + " <- " + prefix.FullName);
        }
    }

    private static MethodDefinition FindTarget(ModuleDefinition game, TypeDefinition owner)
    {
        var attributes = owner
            .CustomAttributes.Where(a => a.AttributeType.Namespace == "HarmonyLib")
            .ToArray();
        var patches = attributes.Where(a => a.AttributeType.FullName == Patch).ToArray();
        if (
            attributes.Any(a =>
                a.AttributeType.FullName != Patch && a.AttributeType.FullName != Priority
            )
            || patches.Length != 1
            || patches[0].HasFields
            || patches[0].HasProperties
            || patches[0].ConstructorArguments.Count != 2
            || patches[0].ConstructorArguments[0].Value is not TypeReference targetType
            || patches[0].ConstructorArguments[1].Value is not string name
            || targetType.Scope.Name != game.Assembly.Name.Name
        )
            throw Invalid(owner, "expected one HarmonyPatch(game type, method name)");
        var targets = game.GetType(targetType.FullName)
            ?.Methods.Where(m => m.Name == name)
            .ToArray();
        if (targets?.Length != 1)
            throw Invalid(owner, $"missing or ambiguous target {targetType.FullName}.{name}");
        var target = targets[0];
        if (
            !target.HasBody
            || target.Body.Instructions.Count == 0
            || target.IsConstructor
            || target.HasGenericParameters
            || target.DeclaringType.HasGenericParameters
            || target.ReturnType.MetadataType != MetadataType.Void
        )
            throw Invalid(owner, "expected a concrete non-generic void target: " + target.FullName);

        return target;
    }

    private static Hook ReadHook(ModuleDefinition game, TypeDefinition owner)
    {
        var target = FindTarget(game, owner);
        // Selected classes contain only their convention-named Prefix. Reject auxiliary
        // methods (including Prepare/TargetMethods/Postfix) whose semantics we cannot apply.
        if (owner.HasGenericParameters || owner.Methods.Count != 1)
            throw Invalid(owner, "expected a single Prefix method");
        var prefix = owner.Methods[0];
        if (
            prefix.Name != "Prefix"
            || !prefix.IsStatic
            || !prefix.HasBody
            || prefix.CallingConvention != MethodCallingConvention.Default
            || prefix.HasGenericParameters
            || prefix.ReturnType.MetadataType is not (MetadataType.Void or MetadataType.Boolean)
            || prefix.CustomAttributes.Any(a =>
                a.AttributeType.Namespace == "HarmonyLib" && a.AttributeType.FullName != Priority
            )
        )
            throw Invalid(owner, "unsupported Prefix signature or Harmony annotations");
        if (
            target.Body.Instructions.Any(i =>
                i.Operand is MethodReference m && m.FullName == prefix.FullName
            )
        )
            throw Invalid(owner, "already installed on " + target.FullName);
        return new Hook(
            target,
            prefix,
            prefix.Parameters.Select(p => LoadArgument(target, prefix, p)).ToArray()
        );
    }

    private static Instruction LoadArgument(
        MethodDefinition target,
        MethodDefinition prefix,
        ParameterDefinition parameter
    )
    {
        if (!parameter.HasCustomAttributes && parameter.ParameterType is not TypeSpecification)
        {
            if (
                parameter.Name == "__instance"
                && !target.IsStatic
                && !target.DeclaringType.IsValueType
                && SameType(parameter.ParameterType, target.DeclaringType)
            )
                return Instruction.Create(OpCodes.Ldarg_0);
            if (
                parameter.Name.Length > 0
                && !parameter.Name.StartsWith("__", StringComparison.Ordinal)
            )
            {
                var matches = target
                    .Parameters.Where(p =>
                        p.Name == parameter.Name
                        && SameType(p.ParameterType, parameter.ParameterType)
                    )
                    .ToArray();
                if (matches.Length == 1)
                    return Instruction.Create(OpCodes.Ldarg, matches[0]);
            }
        }
        throw Invalid(prefix.DeclaringType, "unsupported or mismatched argument " + parameter.Name);
    }

    private static InvalidOperationException Invalid(TypeDefinition owner, string reason) =>
        new($"Eco prefix {owner.FullName}: {reason}");

    private static bool SameType(TypeReference left, TypeReference right) =>
        left.FullName == right.FullName && AssemblyName(left) == AssemblyName(right);

    private static string AssemblyName(TypeReference type) =>
        type.Scope is ModuleDefinition module ? module.Assembly.Name.Name : type.Scope.Name;
}
