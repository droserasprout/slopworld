using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class OfflineRedirect
{
    internal static void Redirect(
        TypeDefinition owner,
        TypeDefinition adapter,
        string name,
        string replacement,
        string returnType,
        params string[] parameters
    )
    {
        MethodDefinition Resolve(TypeDefinition type, string member)
        {
            var matches = type
                .Methods.Where(m =>
                    m.Name == member
                    && m.IsStatic
                    && !m.HasGenericParameters
                    && m.ReturnType.FullName == returnType
                    && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(parameters)
                )
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Expected static {returnType} {type.FullName}.{member}({string.Join(", ", parameters)}), found {matches.Length}"
                );
            return matches[0];
        }
        var method = Resolve(owner, name);
        var target = Resolve(adapter, replacement);
        if (!method.HasBody)
            throw new InvalidOperationException(
                "Missing body for offline redirect: " + method.FullName
            );
        if (
            method.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Call
                && i.Operand is MethodReference reference
                && reference.FullName == target.FullName
            )
        )
            throw new InvalidOperationException("Duplicate offline redirect: " + method.FullName);
        method.Body = new MethodBody(method);
        var il = method.Body.GetILProcessor();
        foreach (var parameter in method.Parameters)
            il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, owner.Module.ImportReference(target));
        il.Emit(OpCodes.Ret);
    }
}
