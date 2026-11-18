using System.Reflection;
using System.Reflection.Emit;

// The installed 5011 input owner requires an explicit retainEvents argument at
// reflection boundaries, even though ordinary C# callers can omit its default.
public static class InputFrameFixture
{
    private static readonly List<string> Calls = [];

    public static void Record(string call) => Calls.Add(call);

    public static void BeginFrame(bool retainEvents) => Record($"waylith.begin:{retainEvents}");

    public static void Check()
    {
        var waylith = Owner("Waylith", "Waylith.Input.InputState", true);
        var unity = Owner("UnityStubs", "UnityEngine.InputSystem.InputSystem", false);
        ResolveEventHandler resolver = (_, request) =>
            new AssemblyName(request.Name).Name switch
            {
                "Waylith" => waylith,
                "UnityStubs" => unity,
                _ => null,
            };
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            Probe.InputFrame("[]", 240);
            Probe.InputFrame("[{\"kind\":\"reset\"}]", 240);
            string[] expected =
            [
                "waylith.begin:False",
                "unity.begin",
                "waylith.begin:False",
                "unity.begin",
                "unity.reset",
                "unity.begin",
                "waylith.reset",
                "waylith.begin:False",
            ];
            if (!Calls.SequenceEqual(expected))
                throw new Exception("Input frames must clear prior events, including after reset");
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    private static Assembly Owner(string assembly, string name, bool waylith)
    {
        var builder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(assembly),
            AssemblyBuilderAccess.Run
        );
        var type = builder.DefineDynamicModule(assembly).DefineType(name, TypeAttributes.Public);
        const MethodAttributes flags = MethodAttributes.Public | MethodAttributes.Static;
        foreach (var reset in new[] { false, true })
        {
            var method = type.DefineMethod(
                reset ? (waylith ? "ResetAll" : "ResetAllInput") : "BeginFrame",
                flags,
                typeof(void),
                waylith && !reset ? [typeof(bool)] : Type.EmptyTypes
            );
            var il = method.GetILGenerator();
            if (waylith && !reset)
            {
                method
                    .DefineParameter(
                        1,
                        ParameterAttributes.Optional | ParameterAttributes.HasDefault,
                        "retainEvents"
                    )
                    .SetConstant(false);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, typeof(InputFrameFixture).GetMethod(nameof(BeginFrame))!);
            }
            else
            {
                il.Emit(
                    OpCodes.Ldstr,
                    (waylith ? "waylith" : "unity") + (reset ? ".reset" : ".begin")
                );
                il.Emit(OpCodes.Call, typeof(InputFrameFixture).GetMethod(nameof(Record))!);
            }
            il.Emit(OpCodes.Ret);
        }
        type.CreateType();
        return builder;
    }
}
