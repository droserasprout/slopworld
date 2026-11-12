// Real operation replay must preserve adjacent batch merging and native clip state.
// Synthetic faces/CPU textures come from GlyphChecks; no game or browser is started.
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using BrowserAdapters;
using UnityEngine;

static class RowChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo Enabled = typeof(BrowserGui).GetField(
        "rowsEnabled",
        BindingFlags.Static | BindingFlags.NonPublic
    )!;

    static void Require(bool ok, string message)
    {
        if (!ok)
            throw new Exception("Row recording: " + message);
    }

    internal static void Run(int index, Texture2D texture)
    {
        bool profile = BrowserGuiInput.Profile;
        bool labels = BrowserGui.ConfigureObserveLabels(false);
        BrowserGuiInput.Profile = false;
        var style = new GUIStyle { fontSize = 15 };
        var identity = new object();
        float[] placement = [1, 2, 3, 4];
        void Label(BrowserGui gui, string text = "Agyp ASCII") =>
            gui.DrawText(
                new Rect(3, 4, 300, 20),
                text,
                index,
                15,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                false,
                Color.white,
                default,
                false
            );
        void Contents(BrowserGui gui, bool batches = false)
        {
            gui.FillRect(new Rect(2, 3, 200, 20), Color.blue);
            gui.PushGroup(new Rect(1, 2, 240, 30));
            Label(gui);
            gui.PopGroup();
            // Shared style state is part of the PaintRow contract, even on a hit.
            style.normal.textColor = Color.red;
            for (int i = 0; i < (batches ? 1100 : 1); i++)
                Label(gui, "A");
        }
        bool Draw(BrowserGui gui, bool batches = false)
        {
            bool hit = gui.TryRow(identity, style, placement);
            if (!hit)
            {
                Contents(gui, batches);
                gui.EndRow(true, style);
            }
            return hit;
        }
        try
        {
            // Test the real static switch default before enabling the CPU fixture.
            Require(!(bool)Enabled.GetValue(null)!, "must default off");
            var gui = new BrowserGui();
            Require(!Draw(gui), "disabled path reused a row");
            gui.Drain();
            string? mod = Environment
                .GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--row-mod="));
            if (mod != null)
            {
                Assembly.LoadFrom(Path.GetFullPath(mod[10..]));
                Require(
                    !BrowserGui.ConfigureTerminalRows(true),
                    "enable returned wrong previous state"
                );
                var bridge = AppDomain
                    .CurrentDomain.GetAssemblies()
                    .Single(a => a.GetName().Name == "SlopWorld")
                    .GetType("SlopWorld.TerminalRowCommands")!;
                var backend = BrowserGui.Install();
                var replay =
                    (Func<object, GUIStyle, float[], bool>)
                        bridge.GetField("TryReplay")!.GetValue(null)!;
                var complete = (Action<bool, GUIStyle>)bridge.GetField("Complete")!.GetValue(null)!;
                Label(backend);
                backend.Drain();
                Require(!replay(identity, style, placement), "fresh delegate bridge hit");
                Contents(backend);
                complete(true, style);
                backend.Drain();
                Require(
                    replay(identity, style, placement),
                    "mod/browser delegates failed to replay"
                );
                backend.Discard();
                Require(
                    BrowserGui.ConfigureTerminalRows(false),
                    "disable returned wrong previous state"
                );
                Require(
                    bridge.GetField("TryReplay")!.GetValue(null) == null
                        && bridge.GetField("Complete")!.GetValue(null) == null,
                    "disable retained mod delegates"
                );
            }
            Enabled.SetValue(null, true);
            var reference = new BrowserGui();
            Contents(reference, true);
            reference.Drain();
            Contents(gui, true);
            gui.Drain();
            Require(!Draw(gui, true), "first capture unexpectedly hit");
            // The candidate cannot be reused within the same undelivered packet.
            Require(!Draw(gui, true), "candidate published before Drain");
            gui.Discard();
            Require(!Draw(gui, true), "discarded capture published");
            string captured = gui.Drain();
            style.normal.textColor = Color.green;
            Require(Draw(gui, true), "warm complete row did not replay");
            Require(style.normal.textColor == Color.red, "replay lost style exit color");
            Require(gui.Drain() == captured, "ordered row operations changed packet");
            // Neighboring draws must merge through AppendImageBatch, including its cap.
            Label(gui, "A");
            Require(Draw(gui, true), "adjacent replay missed");
            Label(gui, "A");
            Label(reference, "A");
            Contents(reference, true);
            Label(reference, "A");
            Require(gui.Drain() == reference.Drain(), "cross-row merging/batch split changed");

            void Miss(string reason)
            {
                Require(!Draw(gui, true), reason);
                gui.Drain();
                Require(Draw(gui, true), "failed to recover after " + reason);
                gui.Drain();
            }
            placement = [1, 2, 3, 5];
            Miss("placement change hit");
            identity = new object();
            Miss("replacement decorated row hit");
            gui.SetMatrix(Matrix4x4.Scale(new Vector3(1.25f, 1.25f, 1)));
            Miss("matrix change hit");
            gui.PushClipRect(new Rect(0, 0, 400, 100));
            Miss("clip change hit");
            gui.PopClipRect();
            gui.Drain();
            GUI.color = Color.green;
            Miss("GUI tint change hit");
            GUI.color = Color.white;
            style.fontSize++;
            Miss("style change hit");
            BrowserFontBank.ConfigureDensity(2);
            Miss("font density change hit");
            BrowserFontBank.ConfigureDensity(1);
            var bank = BrowserFontBank.Instance;
            var generation = typeof(BrowserFontBank).GetProperty("ResolutionVersion")!;
            Miss("density restoration hit");
            generation.SetValue(bank, bank.ResolutionVersion + 1);
            Miss("font resolution generation change hit");

            BrowserGuiInput.Profile = true;
            Require(!Draw(gui), "profiling reused a row");
            gui.Discard();
            BrowserGuiInput.Profile = false;
            BrowserGui.ConfigureObserveLabels(true);
            Require(!Draw(gui), "label observation reused a row");
            gui.Discard();
            BrowserGui.ConfigureObserveLabels(false);

            // An upload mutation cannot be bypassed by an existing row's image ID.
            texture.Apply(false, false);
            BrowserGui.TextureApplied(texture);
            Require(!Draw(gui), "texture Apply hit");
            gui.Drain();
            Require(!Draw(gui), "cold upload capture published");
            gui.Drain();
            Require(Draw(gui), "warm upload did not recover");
            gui.Drain();
            // Mutation without Apply still invalidates a retained dependency.
            using var replacement = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            replacement.SetPixels32([Color.red, Color.green, Color.white, Color.blue]);
            texture
                .CoreTexture.GetType()
                .GetProperty("CpuData")!
                .SetValue(texture.CoreTexture, replacement.CoreTexture.CpuData);
            Require(!Draw(gui), "CPU backing change hit");
            gui.Drain();
            Require(!Draw(gui), "changed backing cold capture published");
            gui.Drain();
            Require(Draw(gui), "backing replacement did not recover");
            gui.Drain();
            texture.CoreTexture.width = 1;
            texture.CoreTexture.height = 4;
            Require(!Draw(gui), "dimension change hit");
            gui.Drain();
            Require(!Draw(gui), "resized cold capture published");
            gui.Drain();
            Require(Draw(gui), "dimension change did not recover");
            gui.Drain();
            var cold = new BrowserGui();
            Require(!Draw(cold), "fresh backend hit");
            cold.Discard();
            Require(!Draw(cold), "discarded upload hit");
            cold.Drain();
            Require(!Draw(cold), "cold upload published");
            cold.Drain();
            Require(Draw(cold), "discard/upload recovery failed");
            cold.Drain();

            void Reject(string text, bool success = true)
            {
                identity = new object();
                Require(!gui.TryRow(identity, style, placement), "new row hit");
                Label(gui, text);
                gui.EndRow(success, style);
                gui.Drain();
                Require(
                    !gui.TryRow(identity, style, placement),
                    "unsupported/failed capture published"
                );
                gui.EndRow(false, style);
                gui.Discard();
            }
            Reject("A", false);
            Reject("\u00e9");
            Reject("\n");
            // Successful serialization is the publication boundary; force a real
            // writer failure after EndRow and verify a later Drain cannot publish it.
            identity = new object();
            Draw(gui);
            var commands = (IList)typeof(BrowserGui).GetField("commands", Private)!.GetValue(gui)!;
            commands.Add(
                Activator.CreateInstance(
                    typeof(BrowserGui).GetNestedType("Command", BindingFlags.NonPublic)!,
                    "unsupported-row-test",
                    new object()
                )
            );
            try
            {
                gui.Drain();
                throw new Exception("Writer unexpectedly accepted unsupported command");
            }
            catch (InvalidOperationException) { }
            commands.RemoveAt(commands.Count - 1);
            gui.Drain();
            Require(!Draw(gui), "failed serialization published a candidate");
            gui.Discard();

            // Per-row operation overflow must leave normal painting intact.
            identity = new object();
            Require(!gui.TryRow(identity, style, placement), "new oversized row hit");
            for (int i = 0; i < 4100; i++)
                Label(gui, "A");
            gui.EndRow(true, style);
            gui.Drain();
            Require(!gui.TryRow(identity, style, placement), "oversized row published");
            gui.EndRow(false, style);
            gui.Discard();

            // Hold source identities alive so GC cannot mask a missing budget.
            var identities = new List<object>();
            // Count and byte caps also constrain entries when every row is new.
            for (int i = 0; i < 300; i++)
            {
                identity = new object();
                identities.Add(identity);
                Draw(gui);
                gui.Drain();
            }
            object retained = typeof(BrowserGui).GetField("liveRows", Private)!.GetValue(gui)!;
            int RetainedCount() =>
                (int)retained.GetType().GetProperty("Count")!.GetValue(retained)!;
            Require(RetainedCount() <= 256, "row count is unbounded");
            Require(
                (int)typeof(BrowserGui).GetField("rowBytes", Private)!.GetValue(gui)!
                    <= 4 * 1024 * 1024,
                "retained bytes are unbounded"
            );
            for (int i = 0; i < 24; i++)
            {
                identity = new object();
                identities.Add(identity);
                Draw(gui, true);
                gui.Drain();
            }
            Require(RetainedCount() < 24, "large rows did not trigger byte eviction");
            Require(
                (int)typeof(BrowserGui).GetField("rowBytes", Private)!.GetValue(gui)!
                    <= 4 * 1024 * 1024,
                "large rows exceeded byte cap"
            );
            for (int i = 0; i < 122; i++)
                gui.Drain();
            Require(RetainedCount() == 0, "unused rows survived lifetime bound");
            var ephemeral = CaptureEphemeral(gui, style, index);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Require(!ephemeral.TryGetTarget(out _), "cache retained parsed row identity");
            gui.Drain();
            GC.KeepAlive(identities);
            Console.WriteLine(
                "Row replay preserves packets, coalescing, exit color, invalidation, upload/discard/failure recovery, diagnostics and retention bounds."
            );
        }
        finally
        {
            Enabled.SetValue(null, false);
            BrowserGuiInput.Profile = profile;
            BrowserGui.ConfigureObserveLabels(labels);
            GUI.color = Color.white;
            BrowserFontBank.ConfigureDensity(1);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference<object> CaptureEphemeral(BrowserGui gui, GUIStyle style, int index)
    {
        var identity = new object();
        Require(!gui.TryRow(identity, style, []), "ephemeral row hit");
        gui.DrawText(
            new Rect(0, 0, 100, 20),
            "A",
            index,
            15,
            FontStyle.Normal,
            TextAnchor.UpperLeft,
            false,
            Color.white,
            default,
            false
        );
        gui.EndRow(true, style);
        gui.Drain();
        return new WeakReference<object>(identity);
    }
}
