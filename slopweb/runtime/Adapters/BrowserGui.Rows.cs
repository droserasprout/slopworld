// Optional terminal-row recording owns immutable operations before packet coalescing.
// A completed row is provisional until Drain succeeds; Discard abandons all candidates.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using UnityEngine;
using UnityStubs.Gui;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    const int RowLimit = 256;
    const int RowByteLimit = 4 * 1024 * 1024;
    const int RowOperationLimit = 4096;
    static bool rowsEnabled;
    readonly ConditionalWeakTable<object, RecordedRow> recordedRows = new();
    readonly HashSet<RecordedRow> liveRows = new();
    readonly List<RecordedRow> pendingRows = new();
    RecordedRow? recordingRow;
    int rowBytes;
    long rowFrame,
        rowHits,
        rowMisses,
        rowBypasses;

    sealed record RowOperation(string Kind, object Value);

    sealed record RowImage(int Id, float Red, float Green, float Blue, float Alpha, float[] Quads);

    sealed class RecordedRow
    {
        internal required WeakReference<object> Identity;
        internal required GUIStyle Style;
        internal required float[] Key;
        internal required float[] Placement;
        internal readonly List<RowOperation> Operations = new();
        internal readonly Dictionary<int, WeakReference<ImageState>> Images = new();
        internal Color ExitColor;
        internal bool Valid = true;
        internal int Bytes;
        internal long Frame;
    }

    // The mod keeps the native no-op boundary; no browser assembly is loaded there.
    // Install delegates once, outside the per-row path, without referencing mod types.
    public static bool ConfigureTerminalRows(bool enabled)
    {
        bool previous = rowsEnabled;
        var boundary = AppDomain
            .CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "SlopWorld")
            ?.GetType("SlopWorld.TerminalRowCommands");
        if (enabled && boundary == null)
            throw new InvalidOperationException("Terminal row recording requires the current mod");
        if (boundary != null)
        {
            boundary
                .GetField("TryReplay")!
                .SetValue(
                    null,
                    enabled ? new Func<object, GUIStyle, float[], bool>(TryTerminalRow) : null
                );
            boundary
                .GetField("Complete")!
                .SetValue(null, enabled ? new Action<bool, GUIStyle>(CompleteTerminalRow) : null);
        }
        rowsEnabled = enabled;
        if (GuiBackend.Current is BrowserGui gui)
            gui.ClearRows();
        return previous;
    }

    // Small counters remain observable without enabling managed profiling (which
    // intentionally bypasses replay). No row text or resource objects escape.
    public static string TerminalRowStats() =>
        JsonSerializer.Serialize(
            new
            {
                enabled = rowsEnabled,
                hits = (GuiBackend.Current as BrowserGui)?.rowHits ?? 0,
                misses = (GuiBackend.Current as BrowserGui)?.rowMisses ?? 0,
                bypasses = (GuiBackend.Current as BrowserGui)?.rowBypasses ?? 0,
                rows = (GuiBackend.Current as BrowserGui)?.liveRows.Count ?? 0,
                bytes = (GuiBackend.Current as BrowserGui)?.rowBytes ?? 0,
            }
        );

    static bool TryTerminalRow(object identity, GUIStyle style, float[] placement) =>
        GuiBackend.Current is BrowserGui gui && gui.TryRow(identity, style, placement);

    static void CompleteTerminalRow(bool success, GUIStyle style)
    {
        if (GuiBackend.Current is BrowserGui gui)
            gui.EndRow(success, style);
    }

    // Callers supply final immutable row identity; placement is copied on capture.
    public bool TryRow(object identity, GUIStyle style, float[] placement)
    {
        recordingRow = null;
        if (!rowsEnabled)
            return false;
        if (SuppressDrawing || BrowserGuiInput.Profile || observeLabels)
        {
            rowBypasses++;
            return false;
        }
        float[] key = RowKey(placement, style);
        if (
            recordedRows.TryGetValue(identity, out var saved)
            && ReferenceEquals(saved.Style, style)
            && BitsEqual(saved.Key, key)
            && RowImagesCurrent(saved)
        )
        {
            rowHits++;
            saved.Frame = rowFrame;
            foreach (var operation in saved.Operations)
                if (operation.Value is RowImage image)
                    AppendImages(
                        image.Id,
                        image.Red,
                        image.Green,
                        image.Blue,
                        image.Alpha,
                        image.Quads
                    );
                else
                    Emit(operation.Kind, operation.Value);
            style.normal.textColor = saved.ExitColor;
            return true;
        }
        rowMisses++;
        recordingRow = new RecordedRow
        {
            Identity = new WeakReference<object>(identity),
            Style = style,
            Key = key,
            Placement = placement.ToArray(),
            Bytes = (key.Length + placement.Length) * sizeof(float) + 256,
            Frame = rowFrame,
        };
        return false;
    }

    public void EndRow(bool success, GUIStyle style)
    {
        var row = recordingRow;
        recordingRow = null;
        if (
            row == null
            || !success
            || !row.Valid
            || !RowImagesCurrent(row)
            || !BitsEqual(row.Key, RowKey(row.Placement, style))
        )
            return;
        row.ExitColor = style.normal.textColor;
        // Provisional memory is bounded independently of the retained cache.
        if (
            pendingRows.Count >= RowLimit
            || pendingRows.Sum(r => r.Bytes) + row.Bytes > RowByteLimit
        )
            return;
        pendingRows.Add(row);
    }

    // Full backend transform (not only its 2D serialized subset), physical clipping,
    // target and font resolution all participate. Mod placement includes snap state
    // and terminal theme/font revisions. TerminalFont owns the immutable style setup.
    float[] RowKey(float[] placement, GUIStyle style)
    {
        bool clipped = drawingClips.TryPeek(out var clip);
        float[] context =
        [
            matrix.m00,
            matrix.m01,
            matrix.m02,
            matrix.m03,
            matrix.m10,
            matrix.m11,
            matrix.m12,
            matrix.m13,
            matrix.m20,
            matrix.m21,
            matrix.m22,
            matrix.m23,
            matrix.m30,
            matrix.m31,
            matrix.m32,
            matrix.m33,
            offset.x,
            offset.y,
            clipped ? 1 : 0,
            clip.x,
            clip.y,
            clip.width,
            clip.height,
            GUI.color.r,
            GUI.color.g,
            GUI.color.b,
            GUI.color.a,
            GUI.contentColor.r,
            GUI.contentColor.g,
            GUI.contentColor.b,
            GUI.contentColor.a,
            GUI.enabled ? 1 : 0,
            RenderTexture.active == null
                ? 0
                : BitConverter.Int32BitsToSingle(RenderTexture.active.GetInstanceID()),
            RenderTexture.active == null ? 0 : RenderTexture.active.width,
            RenderTexture.active == null ? 0 : RenderTexture.active.height,
            BitConverter.Int32BitsToSingle(BrowserFontBank.Instance.AtlasVersion),
            BitConverter.Int32BitsToSingle(BrowserFontBank.Instance.ResolutionVersion),
            Screen.width,
            Screen.height,
            style.font == null ? 0 : BitConverter.Int32BitsToSingle(style.font.GetInstanceID()),
            style.fontSize,
            (int)style.fontStyle,
            (int)style.alignment,
            style.wordWrap ? 1 : 0,
            style.richText ? 1 : 0,
            (int)style.clipping,
            style.padding.left,
            style.padding.right,
            style.padding.top,
            style.padding.bottom,
            style.contentOffset.x,
            style.contentOffset.y,
            style.fixedWidth,
            style.fixedHeight,
        ];
        return [.. placement, .. context];
    }

    static bool BitsEqual(float[] a, float[] b) =>
        MemoryMarshal.AsBytes(a.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(b.AsSpan()));

    bool RowImagesCurrent(RecordedRow row)
    {
        foreach (var pair in row.Images)
        {
            if (!pair.Value.TryGetTarget(out var state))
                return false;
            if (
                pendingImages.Contains(pair.Key)
                || !images.TryGetValue(pair.Key, out var current)
                || !ReferenceEquals(state, current)
                || !state.Texture.TryGetTarget(out var texture)
                || texture == null
                || state.Identity.Value != state.Version
                || texture.width != state.Width
                || texture.height != state.Height
                || !ReferenceEquals(texture.CoreTexture.CpuData, state.Data)
            )
                return false;
        }
        return true;
    }

    void RejectRow()
    {
        if (recordingRow != null)
            recordingRow.Valid = false;
    }

    void RecordOperation(string kind, object value, int bytes)
    {
        var row = recordingRow;
        if (row == null || !row.Valid)
            return;
        row.Bytes += bytes + 128;
        if (row.Bytes > RowByteLimit || row.Operations.Count >= RowOperationLimit)
        {
            row.Valid = false;
            return;
        }
        row.Operations.Add(new RowOperation(kind, value));
    }

    void RecordEmit(string kind, object value)
    {
        if (recordingRow == null || !recordingRow.Valid)
            return;
        // These values are fresh immutable arrays/records owned by this call, never
        // slices of the mutable command list or an ImageBatchCommand's growing quads.
        if (kind is "fill" or "matrix" or "clipScreen" or "pop")
            RecordOperation(kind, value, value is float[] a ? a.Length * 4 : 96);
        else
            RejectRow();
    }

    void AppendImages(
        int id,
        float red,
        float green,
        float blue,
        float alpha,
        ReadOnlySpan<float> quads
    )
    {
        if (recordingRow is { Valid: true })
        {
            if (quads.Length * 4 + recordingRow.Bytes > RowByteLimit)
                RejectRow();
            else
                RecordOperation(
                    "imageBatch",
                    new RowImage(id, red, green, blue, alpha, quads.ToArray()),
                    quads.Length * 4
                );
        }
        AppendImageBatch(commands, id, red, green, blue, alpha, quads);
    }

    void CommitRows()
    {
        if (!rowsEnabled && liveRows.Count == 0 && pendingRows.Count == 0)
            return;
        rowFrame++;
        foreach (var row in pendingRows)
        {
            if (!row.Identity.TryGetTarget(out var identity) || !RowImagesCurrent(row))
                continue;
            if (recordedRows.TryGetValue(identity, out var old))
                RemoveRow(old);
            // Evict by last use; both object count and conservative byte accounting
            // are bounded, and unused rows retire after 120 successfully drained passes.
            while (liveRows.Count >= RowLimit || rowBytes + row.Bytes > RowByteLimit)
                RemoveRow(liveRows.MinBy(r => r.Frame)!);
            recordedRows.Add(identity, row);
            liveRows.Add(row);
            rowBytes += row.Bytes;
        }
        pendingRows.Clear();
        foreach (
            var row in liveRows
                .Where(r => !r.Identity.TryGetTarget(out _) || rowFrame - r.Frame > 120)
                .ToArray()
        )
            RemoveRow(row);
    }

    void RemoveRow(RecordedRow row)
    {
        if (row.Identity.TryGetTarget(out var identity))
            recordedRows.Remove(identity);
        liveRows.Remove(row);
        rowBytes -= row.Bytes;
    }

    void ClearRows()
    {
        recordedRows.Clear();
        liveRows.Clear();
        pendingRows.Clear();
        recordingRow = null;
        rowBytes = 0;
        rowHits = rowMisses = rowBypasses = 0;
    }
}
