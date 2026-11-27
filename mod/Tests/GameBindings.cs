using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

namespace SlopWorld.Tests
{
    // Opt-in validation against installed assemblies, isolated from the harness's Unity stubs.
    // Resolve targets and inspect IL only: never initialize the engine or call game methods.
    static class GameBindings
    {
        internal static int Run(string managed, string modPath)
        {
            try
            {
                var context = new GameContext(Path.GetFullPath(managed), Path.GetFullPath(modPath));
                var mod = context.LoadFromAssemblyPath(Path.GetFullPath(modPath));
                // The executable normally loads these before ModBootstrap. Reflection-only
                // validation must load them explicitly for Harmony's type-name lookup.
                foreach (
                    string assembly in new[]
                    {
                        "Assembly-CSharp.dll",
                        "RimWorld.dll",
                        "Waylith.dll",
                        "UnityStubs.dll",
                        "Waylith.Windowing.dll",
                    }
                )
                    context.LoadFromAssemblyPath(Path.Combine(Path.GetFullPath(managed), assembly));
                int count = 0;
                // Target lookup alone does not initialize a beforefieldinit type. Bind
                // the keyboard delegate explicitly so signature drift fails this check.
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                    mod.GetType("SlopWorld.OrderedKeyboard", throwOnError: true).TypeHandle
                );
                count++;
                Require(
                    Invoke(mod, "LinuxGameWindow+Native", "MainWindow") == null,
                    "native window bindings must resolve before startup"
                );
                count++;
                var mouseDispatch = (MethodBase)Invoke(
                    mod,
                    "OrderedMouse+Dispatch",
                    "TargetMethod"
                );
                var mouseCode = PatchProcessor.GetOriginalInstructions(mouseDispatch);
                var replay = (
                    (IEnumerable<CodeInstruction>)
                        Invoke(mod, "OrderedMouse+Dispatch", "Transpiler", mouseCode)
                ).ToList();
                Require(
                    replay.Count(x =>
                        x.operand is MethodInfo m
                        && m.DeclaringType?.FullName == "SlopWorld.OrderedMouse+Dispatch"
                        && m.Name == "Down"
                    ) == 2
                        && replay.Count(x =>
                            x.operand is MethodInfo m
                            && m.DeclaringType?.FullName == "SlopWorld.OrderedMouse+Dispatch"
                            && m.Name == "Up"
                        ) == 1,
                    "mouse replay must replace both press polls and the release poll"
                );
                count++;
                var pacingLoop = (MethodBase)Invoke(mod, "Patch_RenderPacing", "TargetMethod");
                var loopGenerator = PatchProcessor.CreateILGenerator(pacingLoop);
                // Exercise the queue counter binding without constructing an engine,
                // starting a render thread, or calling a native game method.
                var pacingApp =
                    System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                        pacingLoop.DeclaringType
                    );
                var renderThreadField = pacingLoop.DeclaringType.GetField(
                    "_renderThread",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                var pacingThread =
                    System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                        renderThreadField.FieldType
                    );
                renderThreadField.SetValue(pacingApp, pacingThread);
                var pendingFrames = renderThreadField.FieldType.GetField(
                    "_pendingFrames",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                foreach (int pending in new[] { 0, 1, 2, 8 })
                {
                    pendingFrames.SetValue(pacingThread, pending);
                    Require(
                        (bool)Invoke(mod, "RenderPacing", "QueueAvailable", pacingApp)
                            == (pending <= 1),
                        "native render admission must preserve the bounded queue without waiting"
                    );
                }
                var originalLoop = PatchProcessor.GetOriginalInstructions(
                    pacingLoop,
                    loopGenerator
                );
                var pacedLoop = (
                    (IEnumerable<CodeInstruction>)
                        Invoke(mod, "Patch_RenderPacing", "Transpiler", originalLoop, loopGenerator)
                ).ToList();
                int nativeDeadline = originalLoop.FindIndex(i =>
                    i.operand is MethodInfo m && m.Name == "NextPresentTime"
                );
                Require(
                    nativeDeadline >= 0
                        && pacedLoop[nativeDeadline].opcode == System.Reflection.Emit.OpCodes.Pop
                        && pacedLoop[nativeDeadline + 1].opcode
                            == System.Reflection.Emit.OpCodes.Ldc_I8
                        && Equals(pacedLoop[nativeDeadline + 1].operand, 0L)
                        && !pacedLoop.Any(i =>
                            i.operand is MethodInfo m && m.Name == "NextPresentTime"
                        ),
                    "service-only iterations must not advance native game/presentation time"
                );
                Require(
                    originalLoop.Count(i =>
                        i.operand is MethodInfo m && m.Name == "ThrottleToTargetFrameRate"
                    ) == 3,
                    "unexpected native frame throttle sites"
                );
                Require(
                    !pacedLoop.Any(i =>
                        i.operand is MethodInfo m && m.Name == "ThrottleToTargetFrameRate"
                    ),
                    "render-only pacing cannot leave a main-thread frame throttle"
                );
                Require(
                    pacedLoop.Count(i => i.operand is MethodInfo m && m.Name == "FinishIteration")
                        == 3,
                    "missing render-loop completion paths"
                );
                int inputReset = pacedLoop.FindIndex(i =>
                    i.operand is MethodInfo m
                    && m.Name == "BeginFrame"
                    && m.DeclaringType.FullName == "Waylith.Input.InputState"
                );
                int gameUpdate = pacedLoop.FindIndex(i =>
                    i.operand is FieldInfo f && f.Name == "OnUpdate"
                );
                int renderGate = pacedLoop.FindIndex(i =>
                    i.operand is MethodInfo m && m.Name == "AdmitFrame"
                );
                int captureFrame = pacedLoop.FindIndex(i =>
                    i.operand is MethodInfo m && m.Name == "CaptureFrame"
                );
                Require(
                    inputReset > captureFrame
                        && gameUpdate >= 0
                        && renderGate > gameUpdate
                        && captureFrame > renderGate,
                    "render admission must follow game updates and preserve end-of-frame input reset"
                );
                Require(
                    pacedLoop.Count(i => i.operand is MethodInfo m && m.Name == "EndFrame")
                        == originalLoop.Count(i =>
                            i.operand is MethodInfo m && m.Name == "EndFrame"
                        ),
                    "skipped rendering must retain native frame cleanup"
                );
                count++;
                var musicType = context
                    .LoadFromAssemblyName(new AssemblyName("Assembly-CSharp"))
                    .GetType("RimWorld.MusicManagerPlay", throwOnError: true);
                var idleMusic =
                    System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                        musicType
                    );
                // No game/scenario or audio source exists. Stopping an idle manager
                // must not enter Stop(), which dereferences the loading scenario.
                Invoke(mod, "NativeJukebox", "StopPlaying", idleMusic);
                count++;
                foreach (
                    MethodBase target in (IEnumerable)Invoke(
                        mod,
                        "CameraEdgeScroll",
                        "TargetMethods"
                    )
                )
                {
                    var cameraOriginal = PatchProcessor.GetOriginalInstructions(target);
                    var cameraRewritten = (
                        (IEnumerable<CodeInstruction>)
                            Invoke(
                                mod,
                                "CameraEdgeScroll",
                                "Transpiler",
                                cameraOriginal
                                    .Select(instruction => new CodeInstruction(instruction))
                                    .ToList()
                            )
                    ).ToList();
                    Require(
                        cameraOriginal.Count == cameraRewritten.Count,
                        "camera instructions must be retained"
                    );
                    int replacements = 0;
                    for (int i = 0; i < cameraOriginal.Count; i++)
                    {
                        Require(
                            cameraOriginal[i].opcode == cameraRewritten[i].opcode,
                            "camera opcode changed"
                        );
                        if (Equals(cameraOriginal[i].operand, cameraRewritten[i].operand))
                            continue;
                        Require(
                            cameraOriginal[i].operand is MethodInfo before
                                && before.Name == "get_fullScreen"
                                && before.DeclaringType.FullName == "UnityEngine.Screen"
                                && cameraRewritten[i].operand is MethodInfo after
                                && after.Name == "Fullscreen"
                                && after.DeclaringType.Name == "CameraEdgeScroll",
                            "camera rewrite must only replace the fullscreen getter"
                        );
                        replacements++;
                    }
                    Require(replacements == 1, "camera must use one fullscreen replacement");
                    count++;
                }
                using (var loadingFont = mod.GetManifestResourceStream("SlopWorld.LoadingFont"))
                    Require(
                        loadingFont != null && loadingFont.Length > 0,
                        "early loading font must be embedded independently of game content"
                    );
                Invoke(mod, "LoadingScreenHost", "Validate");
                Require(
                    ((MethodInfo)Invoke(mod, "LoadingScreenHost", "WaitTarget")).ReturnType
                        == typeof(bool),
                    "missing full-screen UI wait contract"
                );
                foreach (
                    MethodBase target in (IEnumerable)Invoke(
                        mod,
                        "LoadingScreenHost",
                        "InteractiveTargets"
                    )
                )
                    Require(
                        target != null
                            && target.DeclaringType.Name is "MainMenuDrawer" or "UIRoot_Play",
                        "missing interactive loading boundary"
                    );
                var background = (MethodBase)Invoke(mod, "Patch_MenuBackgroundDraw", "Target");
                var backgroundSource = background.DeclaringType.GetField(
                    "BGPlanet",
                    BindingFlags.Static | BindingFlags.NonPublic
                );
                Require(
                    backgroundSource != null
                        && backgroundSource.FieldType.FullName == "UnityEngine.Texture2D",
                    "missing early background source"
                );
                var soundResolve = (MethodBase)Invoke(mod, "StartupAudio", "ResolveTarget");
                var soundCode = PatchProcessor.GetOriginalInstructions(soundResolve);
                var deferredCode =
                    (IEnumerable<CodeInstruction>)Invoke(mod, "StartupAudio", "Rewrite", soundCode);
                Require(
                    deferredCode.Count(i =>
                        i.operand is MethodInfo m
                        && m.Name == "Defer"
                        && m.DeclaringType?.Name == "StartupAudio"
                    ) == 1,
                    "startup sound deferral must preserve one game callback"
                );
                var sizeProbe = (MethodInfo)Invoke(mod, "StartupAudio", "SizeTarget");
                Require(
                    sizeProbe.ReturnType == typeof(bool)
                        && sizeProbe.GetParameters()[1].ParameterType
                            == typeof(long).MakeByRefType(),
                    "native audio metadata probe signature changed"
                );
                var soundConsumers = ((IEnumerable)Invoke(mod, "StartupAudio", "ConsumerTargets"))
                    .Cast<MethodBase>()
                    .ToArray();
                foreach (
                    MethodBase target in (IEnumerable)Invoke(mod, "StartupAudio", "ConsumerTargets")
                )
                    Require(target != null, "missing lazy sound consumer");
                foreach (
                    var method in soundResolve.DeclaringType.GetMethods(
                        BindingFlags.Public
                            | BindingFlags.NonPublic
                            | BindingFlags.Instance
                            | BindingFlags.DeclaredOnly
                    )
                )
                {
                    bool readsGrains = PatchProcessor
                        .GetOriginalInstructions(method)
                        .Any(i =>
                            i.operand is FieldInfo field
                            && field.DeclaringType == soundResolve.DeclaringType
                            && new[]
                            {
                                "resolvedGrains",
                                "distinctResolvedGrainsCount",
                                "numToAvoid",
                            }.Contains(field.Name)
                        );
                    Require(
                        !readsGrains
                            || soundConsumers.Contains(method)
                            || soundCode.Any(i =>
                                i.opcode == System.Reflection.Emit.OpCodes.Ldftn
                                && Equals(i.operand, method)
                            ),
                        "unprotected sound-grain consumer: " + method.Name
                    );
                }
                count += 6;
                foreach (string owner in new[] { "Patch_NativeTextStyle", "Patch_NativeTextField" })
                {
                    var targets = (IEnumerable)Invoke(mod, owner, "TargetMethods");
                    foreach (MethodBase target in targets)
                    {
                        Require(target != null, owner + " has a missing game target");
                        count++;
                    }
                }
                var shutdown = (MethodBase)Invoke(
                    mod,
                    "Patch_SaveOnNativeShutdown",
                    "TargetMethod"
                );
                Require(shutdown != null && !shutdown.IsStatic, "missing native quit lifecycle");
                Require(
                    PatchProcessor
                        .GetOriginalInstructions(shutdown)
                        .Any(instruction =>
                            instruction.operand is FieldInfo field && field.Name == "OnShutdown"
                        ),
                    "native quit lifecycle no longer dispatches game shutdown"
                );
                count++;

                var windowClose = pacingLoop
                    .DeclaringType.Assembly.GetType("Waylith.Input.MainWindowHandler", true)
                    .GetMethod(
                        "Waylith.Windowing.IWindowHandler.OnCloseRequested",
                        BindingFlags.NonPublic | BindingFlags.Instance
                    );
                var closeCode = PatchProcessor.GetOriginalInstructions(windowClose);
                int closeReason = closeCode.FindIndex(i => Equals(i.operand, "WindowClose"));
                Require(
                    closeReason >= 0
                        && closeCode[closeReason + 1].operand is MethodInfo request
                        && request.DeclaringType == pacingLoop.DeclaringType
                        && request.Name == "RequestShutdown"
                        && request.GetParameters().Single().ParameterType == typeof(string),
                    "window-close interception requires the native shutdown reason"
                );
                Require(
                    (bool)Invoke(mod, "Patch_NativeWindowClose", "Prefix", pacingApp, "Restart")
                        && !(bool)Invoke(
                            mod,
                            "Patch_NativeWindowClose",
                            "Prefix",
                            pacingApp,
                            "WindowClose"
                        ),
                    "only user window close should defer native shutdown"
                );
                count++;

                var nativeGui = context
                    .LoadFromAssemblyName(new AssemblyName("UnityStubs"))
                    .GetType("UnityEngine.GUI", throwOnError: true);
                var flushWindows = nativeGui.GetMethod("ProcessPendingWindows");
                Require(
                    flushWindows != null && flushWindows.IsStatic,
                    "missing native window flush for CRT overlay"
                );
                var imgui = context
                    .LoadFromAssemblyName(new AssemblyName("RimWorld"))
                    .GetType("RimWorldWaylith.Imgui.ImguiDispatch", throwOnError: true);
                Require(
                    imgui
                        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                        .Any(method =>
                        {
                            var calls = PatchProcessor
                                .GetOriginalInstructions(method)
                                .Select(i => i.operand)
                                .OfType<MethodInfo>()
                                .ToArray();
                            int behaviours = Array.FindIndex(calls, m => m.Name == "DispatchOnGUI");
                            int windows = Array.FindIndex(calls, m => m == flushWindows);
                            int end = Array.FindIndex(calls, m => m.Name == "EndPass");
                            return behaviours >= 0 && windows > behaviours && end > windows;
                        }),
                    "CRT overlay requires window flush after behaviours and before EndPass"
                );
                count++;

                var textBridge = (MethodBase)Invoke(mod, "Patch_TerminalTextInput", "TargetMethod");
                var bridgeCode = (
                    (IEnumerable<CodeInstruction>)
                        Invoke(
                            mod,
                            "Patch_TerminalTextInput",
                            "Transpiler",
                            PatchProcessor.GetOriginalInstructions(textBridge)
                        )
                ).ToList();
                int focus = bridgeCode.FindIndex(i =>
                    i.operand is MethodInfo m && m.Name == "get_CapturesText"
                );
                Require(
                    focus >= 0
                        && bridgeCode[focus + 1].operand is MethodInfo filter
                        && filter.Name == "IncludeTerminal"
                        && bridgeCode
                            .Skip(focus + 2)
                            .Any(i =>
                                i.operand is MethodInfo m
                                && m.Name == "Start"
                                && m.DeclaringType.Name == "ITextInput"
                            ),
                    "terminal text activation must feed the native text-input start decision"
                );
                count++;

                var dispatch = (MethodBase)Invoke(mod, "Patch_NativeWorkspaceKeys", "TargetMethod");
                Require(
                    dispatch != null
                        && dispatch.GetParameters()[1].ParameterType.FullName
                            == "UnityEngine.EventModifiers",
                    "unexpected native keyboard modifier argument"
                );
                var keys = (
                    (IEnumerable<CodeInstruction>)
                        Invoke(
                            mod,
                            "Patch_NativeWorkspaceKeys",
                            "Transpiler",
                            PatchProcessor.GetOriginalInstructions(dispatch)
                        )
                ).ToArray();
                Require(
                    keys.Count(i =>
                        i.operand is MethodInfo m
                        && m.Name == "Consumes"
                        && m.DeclaringType?.FullName == "UnityStubs.Internal.TextFocus"
                    ) == 1,
                    "native edit shortcut filter must remain intact"
                );
                Require(
                    keys.Count(i => i.operand is MethodInfo m && m.Name == "ConsumesGameKey") == 1,
                    "missing native workspace key filter"
                );
                count++;

                foreach (
                    var (owner, arguments) in new[]
                    {
                        ("Patch_OrderedKeyChange", new[] { "key", "isPressed" }),
                        ("Patch_OrderedKeyRepeat", new[] { "key" }),
                        ("Patch_OrderedText", new[] { "character" }),
                    }
                )
                {
                    var target = (MethodBase)Invoke(mod, owner, "TargetMethod");
                    Require(
                        target != null
                            && target.GetParameters().Select(p => p.Name).SequenceEqual(arguments),
                        "unexpected ordered keyboard hook: " + owner
                    );
                    Require(
                        target.GetMethodBody() != null,
                        "keyboard capture requires a managed hook"
                    );
                    count++;
                }
                foreach (
                    MethodBase target in (IEnumerable)Invoke(
                        mod,
                        "Patch_OrderedKeyboardReset",
                        "TargetMethods"
                    )
                )
                {
                    Require(
                        target != null && target.GetParameters().Length == 0,
                        "unexpected native keyboard reset"
                    );
                    count++;
                }
                var nativeDispatcher = dispatch.DeclaringType;
                Require(
                    nativeDispatcher
                        .GetField("_typedChars", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.FieldType == typeof(List<char>),
                    "ordered dispatch must clear the native duplicate text buffer"
                );
                var runKey = nativeDispatcher.GetMethod(
                    "RunKeyPass",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Require(
                    runKey != null
                        && runKey
                            .GetParameters()
                            .Select(p => p.ParameterType.FullName)
                            .SequenceEqual(
                                new[]
                                {
                                    "UnityEngine.KeyCode",
                                    "System.Char",
                                    "UnityEngine.Vector2",
                                    "UnityEngine.EventModifiers",
                                    "UnityEngine.EventType",
                                }
                            ),
                    "unexpected ordered keyboard GUI dispatch signature"
                );
                count++;

                var parser = (MethodBase)Invoke(mod, "Patch_NativeTextTags", "TargetMethod");
                Require(parser != null, "missing native rich-text parser");
                count++;

                var acquire = (MethodBase)Invoke(
                    mod,
                    "Patch_SwapchainAcquireTimeout",
                    "TargetMethod"
                );
                Require(acquire != null, "missing native swapchain acquisition");
                var original = PatchProcessor.GetOriginalInstructions(acquire);
                var rewritten =
                    (IEnumerable<CodeInstruction>)
                        Invoke(mod, "SwapchainAcquireTimeout", "Rewrite", original);
                var acquireCode = rewritten.ToList();
                Require(
                    acquireCode.Count(i => i.LoadsConstant(16_000_000)) == 1
                        && acquireCode.Count(i => i.LoadsConstant(1_000_000)) == 1
                        && !acquireCode.Any(i => i.LoadsConstant(100_000_000))
                        && !acquireCode
                            .Select((instruction, index) => (instruction, index))
                            .Any(item =>
                                (
                                    item.instruction.opcode == System.Reflection.Emit.OpCodes.Blt_S
                                    || item.instruction.opcode == System.Reflection.Emit.OpCodes.Blt
                                )
                                && item.instruction.operand is System.Reflection.Emit.Label label
                                && acquireCode.Take(item.index).Any(i => i.labels.Contains(label))
                            ),
                    "acquisition must preserve the short resize timeout and return after one attempt"
                );
                count++;

                var gui = context.LoadFromAssemblyPath(
                    Path.Combine(Path.GetFullPath(managed), "UnityStubs.dll")
                );
                var rect = gui.GetType("UnityEngine.Rect", throwOnError: true);
                var style = gui.GetType("UnityEngine.GUIStyle", throwOnError: true);
                var fieldRect = gui.GetType("UnityEngine.GUI", throwOnError: true)
                    .GetMethod(
                        "TextFieldTextRect",
                        BindingFlags.Static | BindingFlags.NonPublic,
                        null,
                        new[] { rect, typeof(bool), style },
                        null
                    );
                Require(
                    fieldRect != null && fieldRect.ReturnType == rect,
                    "unexpected field geometry signature"
                );
                count++;

                // The native field allocates its own editor ID, including for repeated
                // occurrences of a name. Exercise that allocation through GUI.TextField.
                var utility = gui.GetType("UnityEngine.GUIUtility", throwOnError: true);
                var textField = nativeGui.GetMethod("TextField", new[] { rect, typeof(string) });
                string fieldName = "binding-check-field";
                var ids = new HashSet<int>();
                for (int occurrence = 0; occurrence < 2; occurrence++)
                {
                    nativeGui.GetMethod("SetNextControlName").Invoke(null, new[] { fieldName });
                    textField.Invoke(null, new[] { Activator.CreateInstance(rect), "field text" });
                    int id = (int)Invoke(
                        mod,
                        "TextEntryController",
                        "NamedId",
                        fieldName,
                        occurrence
                    );
                    var editorType = gui.GetType("UnityEngine.TextEditor", throwOnError: true);
                    var editor = utility
                        .GetMethod("QueryStateObject")
                        .Invoke(null, new object[] { editorType, id });
                    Require(
                        id != 0
                            && ids.Add(id)
                            && (string)editorType.GetProperty("text").GetValue(editor)
                                == "field text",
                        "named field must resolve its own native editor"
                    );
                }
                count++;

                // A retiring form clears keyboardControl. Exercise the native filter
                // with no focused control; this path does not query OS keyboard state.
                var keyboardControl = gui.GetType("UnityEngine.GUIUtility", throwOnError: true)
                    .GetProperty("keyboardControl", BindingFlags.Static | BindingFlags.Public);
                var textFocus = gui.GetType("UnityStubs.Internal.TextFocus", throwOnError: true);
                object previousControl = keyboardControl.GetValue(null);
                try
                {
                    foreach (string kind in new[] { "TextField", "TextArea" })
                    {
                        var draw = nativeGui.GetMethod(kind, new[] { rect, typeof(string) });
                        void DrawField()
                        {
                            utility
                                .GetMethod("ResetControlIds", Type.EmptyTypes)
                                .Invoke(null, null);
                            nativeGui
                                .GetMethod("SetNextControlName")
                                .Invoke(null, new[] { fieldName });
                            draw.Invoke(
                                null,
                                new[] { Activator.CreateInstance(rect), "field text" }
                            );
                        }
                        int id = (int)Invoke(mod, "TextEntryController", "NamedId", fieldName, 0);
                        keyboardControl.SetValue(null, id);
                        DrawField();
                        var editorType = gui.GetType("UnityEngine.TextEditor", throwOnError: true);
                        var editor = utility
                            .GetMethod("QueryStateObject")
                            .Invoke(null, new object[] { editorType, id });
                        var cursor = editorType.GetProperty("cursorIndex");
                        var anchor = editorType.GetProperty("selectIndex");
                        cursor.SetValue(editor, 4);
                        anchor.SetValue(editor, kind == "TextField" ? 4 : 1);
                        DrawField();
                        keyboardControl.SetValue(null, 0);
                        Invoke(mod, "FieldFocusScope", "RestoreFocus", id);
                        Invoke(mod, "FieldFocusScope", "RestoreFocus", id);
                        DrawField();
                        Require(
                            (int)cursor.GetValue(editor) == 4
                                && (int)anchor.GetValue(editor) == (kind == "TextField" ? 4 : 1),
                            kind + " focus restoration changed the selection"
                        );
                        if (kind == "TextArea")
                            CheckPrimaryPublication(mod, gui, fieldName, editor);
                    }
                    keyboardControl.SetValue(null, 0);
                    Require(
                        !(bool)
                            AccessTools
                                .PropertyGetter(textFocus, "CapturesText")
                                .Invoke(null, null),
                        "retired field must release native text capture"
                    );
                    var keyCode = gui.GetType("UnityEngine.KeyCode", throwOnError: true);
                    foreach (string key in new[] { "T", "S", "L", "E", "D", "Delete" })
                        Require(
                            !(bool)
                                AccessTools
                                    .Method(textFocus, "Consumes")
                                    .Invoke(null, new[] { Enum.Parse(keyCode, key) }),
                            "retired field must release agent action key " + key
                        );
                }
                finally
                {
                    keyboardControl.SetValue(null, previousControl);
                }
                count++;

                // The installed engine's readonly heuristic is based on style names,
                // not GUI.enabled. Verify our backgroundless editors avoid that branch.
                var nativeField = gui.GetType("UnityEngine.GUI", throwOnError: true)
                    .GetMethod("TextFieldImpl", BindingFlags.Static | BindingFlags.NonPublic);
                var fieldCode = PatchProcessor.GetOriginalInstructions(nativeField);
                string[] readonlyStyles = fieldCode
                    .Where(i => i.opcode == System.Reflection.Emit.OpCodes.Ldstr)
                    .Select(i => i.operand as string)
                    .Where(s => s == "textarea" || s == "textfield")
                    .ToArray();
                Require(
                    readonlyStyles.Length == 2
                        && fieldCode.Any(i =>
                            i.operand is MethodInfo m && m.Name == "get_background"
                        ),
                    "native readonly field heuristic changed"
                );
                var bare = mod.GetType("SlopWorld.TextEntryController", throwOnError: true)
                    .GetMethod("Bare", BindingFlags.Static | BindingFlags.NonPublic);
                var names = PatchProcessor
                    .GetOriginalInstructions(bare)
                    .Where(i => i.opcode == System.Reflection.Emit.OpCodes.Ldstr)
                    .Select(i => i.operand as string)
                    .ToArray();
                Require(
                    names.Contains("SlopWorld.TextArea")
                        && names.Contains("SlopWorld.TextField")
                        && !names.Intersect(readonlyStyles).Any(),
                    "editable styles collide with native readonly names"
                );
                count++;
                Console.WriteLine(
                    $"Validated {count} native game bindings and swapchain IL without starting the game."
                );
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        static void CheckPrimaryPublication(Assembly mod, Assembly gui, string name, object editor)
        {
            var provider = mod.GetType("SlopWorld.UiClipboard", true).GetProperty("Provider");
            var copy = AccessTools.Property(
                mod.GetType("SlopWorld.DesktopClipboard", true),
                "CopyPrimaryHandler"
            );
            var oldProvider = provider.GetValue(null);
            var oldCopy = copy.GetValue(null);
            var writes = new List<string>();
            try
            {
                provider.SetValue(
                    null,
                    Activator.CreateInstance(mod.GetType("SlopWorld.DesktopUiClipboard", true))
                );
                copy.SetValue(
                    null,
                    (Action<string, Action, Action<string>>)((text, ok, fail) => writes.Add(text))
                );
                var lifetime = Activator.CreateInstance(
                    mod.GetType("SlopWorld.FieldLifetime", true)
                );
                var eventType = gui.GetType("UnityEngine.EventType", true);
                foreach (string pass in new[] { "Repaint", "MouseUp", "Repaint" })
                    Invoke(
                        mod,
                        "TextFieldSelection",
                        "PublishSelection",
                        name,
                        lifetime,
                        editor,
                        Enum.Parse(eventType, pass)
                    );
                Require(
                    writes.SequenceEqual(new[] { "iel" }),
                    "native selection must reach PRIMARY exactly once without a tracked drag"
                );
            }
            finally
            {
                provider.SetValue(null, oldProvider);
                copy.SetValue(null, oldCopy);
            }
        }

        static object Invoke(Assembly mod, string owner, string method, params object[] args) =>
            mod.GetType("SlopWorld." + owner, throwOnError: true)
                .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, args);

        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        sealed class GameContext : AssemblyLoadContext
        {
            readonly string _managed;
            readonly string _mod;

            internal GameContext(string managed, string modPath)
                : base("game-bindings")
            {
                _managed = managed;
                _mod = Path.GetDirectoryName(modPath);
            }

            protected override Assembly Load(AssemblyName name)
            {
                // Share CodeInstruction identity with the checker; isolate game and mod types.
                if (name.Name == typeof(Harmony).Assembly.GetName().Name)
                    return typeof(Harmony).Assembly;
                foreach (string directory in new[] { _mod, _managed })
                {
                    string path = Path.Combine(directory, name.Name + ".dll");
                    if (File.Exists(path))
                        return LoadFromAssemblyPath(path);
                }
                return null;
            }
        }
    }
}
