using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;

// Model the 5014 input signatures, including nullable event metadata and the
// absence of ResetAll. Exercise the actual browser adapter without a game loop.
public static class InputFrameFixture
{
    private static readonly HashSet<object> Keys = [];
    private static readonly HashSet<object> Buttons = [];
    private static readonly List<string> Calls = [];

    public static object? Dispatch(string owner, string method, object?[] args)
    {
        Calls.Add(owner + "." + method);
        if (owner != "waylith")
            return null;
        switch (method)
        {
            case "BeginFrame":
                if ((bool)args[0]!)
                    throw new Exception("Frames must discard retained events");
                break;
            case "IsKeyDown":
                return Keys.Contains(args[0]!);
            case "IsMouseButtonDown":
                return Buttons.Contains(args[0]!);
            case "get_MousePosition":
                return new Vector2(12, 34);
            case "OnKey":
                if (args[2] != null)
                    throw new Exception("Browser keys must not invent native edit commands");
                if ((bool)args[1]!)
                    Keys.Add(args[0]!);
                else
                    Keys.Remove(args[0]!);
                break;
            case "OnMouseButton":
                if ((bool)args[1]!)
                    Buttons.Add(args[0]!);
                else
                    Buttons.Remove(args[0]!);
                break;
            case "OnScroll":
                if (args[2] != null)
                    throw new Exception("Browser scroll must not invent touchpad movement");
                break;
        }
        return null;
    }

    public static void Check()
    {
        var waylith = Owner(true);
        var unity = Owner(false);
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
            Probe.InputFrame(
                """
                [{"kind":"key","key":"F12","down":true},
                 {"kind":"repeat","key":"F12"},
                 {"kind":"button","button":0,"down":true,"x":12,"y":34},
                 {"kind":"scroll","dx":0,"dy":1,"x":12,"y":34}]
                """,
                240
            );
            if (Keys.Count != 1 || Buttons.Count != 1)
                throw new Exception("Physical presses were not delivered");
            Probe.InputFrame("[{\"kind\":\"reset\"}]", 240);
            if (Keys.Count != 0 || Buttons.Count != 0)
                throw new Exception("Focus loss must release held keys and mouse buttons");
            int releases = Calls.Count(call => call == "waylith.OnKey");
            Probe.InputFrame("[{\"kind\":\"reset\"}]", 240);
            if (Calls.Count(call => call == "waylith.OnKey") != releases)
                throw new Exception("Repeated reset must not release already released keys");
            Probe.InputFrame("[{\"kind\":\"key\",\"key\":\"F12\",\"down\":true}]", 240);
            Probe.InputFrame("[{\"kind\":\"key\",\"key\":\"F12\",\"down\":false}]", 240);
            if (Keys.Count != 0 || !Calls.Contains("unity.QueueMouseScroll"))
                throw new Exception("Input must continue after reset and reach both owners");
            if (!Calls.Take(2).SequenceEqual(["waylith.BeginFrame", "unity.BeginFrame"]))
                throw new Exception("Frame setup order changed");
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    private static Assembly Owner(bool waylith)
    {
        string assembly = waylith ? "Waylith" : "UnityStubs";
        string prefix = waylith ? "Waylith.Input." : "UnityEngine.InputSystem.";
        var builder = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(assembly),
            AssemblyBuilderAccess.Run
        );
        var module = builder.DefineDynamicModule(assembly);
        var key = module.DefineEnum(prefix + "Key", TypeAttributes.Public, typeof(int));
        key.DefineLiteral("F12", 12);
        var keyType = key.CreateTypeInfo()!.AsType();
        var button = module.DefineEnum(prefix + "MouseButton", TypeAttributes.Public, typeof(int));
        button.DefineLiteral("Left", 0);
        var buttonType = button.CreateTypeInfo()!.AsType();
        // UnityVector construction only needs its public two-float constructor.
        var vector = module.DefineType("UnityEngine.Vector2", TypeAttributes.Public);
        var ctor = vector.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(float), typeof(float)]
        );
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ret);
        var vectorType = vector.CreateType()!;
        var type = module.DefineType(
            prefix + (waylith ? "InputState" : "InputSystem"),
            TypeAttributes.Public
        );
        if (waylith)
        {
            Method("BeginFrame", typeof(void), typeof(bool));
            Method("IsKeyDown", typeof(bool), keyType);
            Method("IsMouseButtonDown", typeof(bool), buttonType);
            Method("get_MousePosition", typeof(Vector2));
            Method("OnKey", typeof(void), keyType, typeof(bool), typeof(DayOfWeek?));
            Method("OnMouseMove", typeof(void), typeof(Vector2));
            Method("OnMouseButton", typeof(void), buttonType, typeof(bool), typeof(Vector2));
            Method("OnScroll", typeof(void), typeof(Vector2), typeof(Vector2), typeof(Vector2?));
        }
        else
        {
            Method("BeginFrame", typeof(void));
            Method("ResetAllInput", typeof(void));
            Method("QueueKeyChange", typeof(void), keyType, typeof(bool));
            Method("QueueKeyRepeat", typeof(void), keyType);
            Method("QueueMouseMove", typeof(void), vectorType);
            Method("QueueMouseButton", typeof(void), typeof(int), typeof(bool), vectorType);
            Method("QueueMouseScroll", typeof(void), vectorType, vectorType);
        }
        type.CreateType();
        return builder;

        void Method(string name, Type result, params Type[] parameters)
        {
            var method = type.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                result,
                parameters
            );
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldstr, waylith ? "waylith" : "unity");
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_I4, parameters.Length);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (int i = 0; i < parameters.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldarg, i);
                if (parameters[i].IsValueType)
                    il.Emit(OpCodes.Box, parameters[i]);
                il.Emit(OpCodes.Stelem_Ref);
            }
            il.Emit(OpCodes.Call, typeof(InputFrameFixture).GetMethod(nameof(Dispatch))!);
            if (result == typeof(void))
                il.Emit(OpCodes.Pop);
            else
                il.Emit(OpCodes.Unbox_Any, result);
            il.Emit(OpCodes.Ret);
        }
    }
}
