using System;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace SlopWorld
{
    // Terminal pane render-texture cache and cached-frame blitting.
    sealed partial class TerminalPanel
    {
        // ------------------------------------------------------------- pane cache

        // What the pane looked like last time it changed.
        RenderTexture _cache;
        TerminalCacheKey _cacheKey;
        int _cacheContentRevision = -1;
        float _cacheLead;
        bool _noCache;
        TerminalCacheRegion _cacheRegion;
        int _cacheScreenWidth,
            _cacheScreenHeight;
        float _cacheScaleX,
            _cacheScaleY,
            _cacheOriginX,
            _cacheOriginY;
        int[] _scrollPaintRows = Array.Empty<int>();

        // Cache the pane between screen frames: the daemon updates more slowly than the monitor.
        // Retain only the pane crop; paint through a temporary screen-sized target so
        // IMGUI clipping, window offsets and glyph sampling retain their screen coordinates.
        // Disable this panel's cache after a render-target failure.
        bool Blit(Rect body, ScreenBuf buf, float cw, float ch)
        {
            if (_noCache)
                return false;
            // Labels and boxes draw on repaint and nowhere else.
            if (Event.current.type != EventType.Repaint)
                return true;

            try
            {
                if (!EnsureCache(body, buf, cw, ch))
                    return false;
                return BlitCached(body);
            }
            catch (System.Exception e)
            {
                DisableCache(e);
                return false;
            }
        }

        bool EnsureCache(Rect body, ScreenBuf buf, float cw, float ch)
        {
            int pw = Screen.width,
                ph = Screen.height;
            if (pw <= 0 || ph <= 0)
                return false;

            bool canCopy = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;
            float lead = CacheLead(body, ch);
            var region = canCopy
                ? TerminalCacheRegion.Crop(
                    body.x * _snapSx + _snapOx,
                    (body.y - lead) * _snapSy + _snapOy,
                    body.xMax * _snapSx + _snapOx,
                    body.yMax * _snapSy + _snapOy,
                    pw,
                    ph
                )
                : new TerminalCacheRegion { Width = pw, Height = ph };
            if (region.Width <= 0 || region.Height <= 0)
                return false;
            if (
                _cache != null
                && (
                    !_cacheRegion.Matches(region)
                    || _cacheScreenWidth != pw
                    || _cacheScreenHeight != ph
                    || _cacheScaleX != _snapSx
                    || _cacheScaleY != _snapSy
                    || _cacheOriginX != _snapOx
                    || _cacheOriginY != _snapOy
                )
            )
                Drop();

            bool fresh = _cache == null;
            _cacheRegion = region;
            _cacheScreenWidth = pw;
            _cacheScreenHeight = ph;
            _cacheScaleX = _snapSx;
            _cacheScaleY = _snapSy;
            _cacheOriginX = _snapOx;
            _cacheOriginY = _snapOy;
            if (fresh)
                _cache = new RenderTexture(
                    region.Width,
                    region.Height,
                    0,
                    RenderTextureFormat.ARGB32
                )
                {
                    name = "SlopWorldPane",
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.DontUnloadUnusedAsset, // see TerminalFont
                };

            if (!_cache.IsCreated())
            {
                if (!_cache.Create())
                {
                    DisableCache(
                        new System.InvalidOperationException("Render texture creation failed")
                    );
                    return false;
                }
                fresh = true;
                _cacheKey = default(TerminalCacheKey);
            }

            var key = CacheKey(body, buf, cw, ch);
            bool repaintAll = fresh || !_cacheKey.Matches(key);
            var repaint = TerminalRepaintPolicy.Choose(
                repaintAll,
                _cacheContentRevision,
                buf,
                out var repaintReason
            );
            TraceCacheDamage(buf, repaintAll);
            if (repaint == TerminalRepaint.None)
                PerfTrace.Count("terminal-cache-hits");
            PaintCache(key, repaintAll, repaint, repaintReason, canCopy);
            if (repaintAll)
                _cacheKey = key;
            _cacheContentRevision = buf.ContentRevision;

            return true;
        }

        void PaintCache(
            TerminalCacheKey key,
            bool repaintAll,
            TerminalRepaint repaint,
            TerminalRepaintReason repaintReason,
            bool canCopy
        )
        {
            if (repaint == TerminalRepaint.None)
                return;
            var buf = key.Buffer;
            var body = new Rect(key.X, key.Y - key.Lead, key.Width, key.Height + key.Lead);
            bool broad = repaint == TerminalRepaint.Full;
            int damageTop = 0,
                damageHeight = _cacheRegion.Height;
            if (!broad)
                _cacheRegion.DamageBand(
                    key,
                    buf.ChangedRows,
                    _snapSy,
                    _snapOy,
                    out damageTop,
                    out damageHeight
                );
            if (damageHeight == 0)
                return;
            var scroll = default(TerminalScrollCopy);
            bool reuse =
                repaintAll
                && canCopy
                && TerminalScrollReuse.TryPlan(
                    _cacheKey,
                    key,
                    _snapSy,
                    _snapOy,
                    _cacheRegion,
                    out scroll
                );
            if (
                PerfTrace.Enabled
                && repaintAll
                && _cacheKey.Buffer != null
                && _cacheKey.Offset > 0
                && key.Offset > 0
                && _cacheKey.Offset != key.Offset
                && _cacheKey.SameSurface(key)
            )
            {
                PerfTrace.Count("terminal-cache-scroll-shifts");
                int overlap = TerminalScrollReuse.MatchingRows(_cacheKey, key);
                if (overlap > 0)
                {
                    PerfTrace.Count("terminal-cache-scroll-overlap", overlap);
                    if (
                        TerminalScrollReuse.PixelAligned(
                            _cacheKey.Offset,
                            key.Offset,
                            key.CellH,
                            _snapSy
                        )
                    )
                        PerfTrace.Count("terminal-cache-scroll-copyable", overlap);
                }
            }
            PerfTrace.Count("terminal-cache-repaints");
            if (repaintAll)
                PerfTrace.Count("terminal-cache-misses");
            else if (broad)
            {
                PerfTrace.Count("terminal-cache-broad-repaints");
                if (PerfTrace.Enabled)
                {
                    if (repaintReason == TerminalRepaintReason.SkippedRevisions)
                        PerfTrace.Count("terminal-cache-skipped-revisions");
                    else if (repaintReason == TerminalRepaintReason.BroadRows)
                        PerfTrace.Count("terminal-cache-broad-rows");
                    else if (repaintReason == TerminalRepaintReason.MissingDamage)
                        PerfTrace.Count("terminal-cache-missing-damage");
                }
            }

            long started = PerfTrace.Start();
            var was = RenderTexture.active;
            RenderTexture scratch = null;
            try
            {
                // The temporary is shared by Unity's pool across panes. No GUI matrix or
                // clip stack changes: painting has exactly the original screen transform.
                var target = _cache;
                if (canCopy)
                {
                    scratch = RenderTexture.GetTemporary(
                        _cacheScreenWidth,
                        _cacheScreenHeight,
                        0,
                        RenderTextureFormat.ARGB32
                    );
                    target = scratch;
                }
                RenderTexture.active = target;
                if (broad)
                    GL.Clear(false, true, SolidTerminalBackground);
                if (canCopy && (!broad || reuse))
                {
                    int sourceTop = reuse ? scroll.SourceTop : damageTop;
                    int destinationTop = reuse ? scroll.DestinationTop : damageTop;
                    int height = reuse ? scroll.Height : damageHeight;
                    CopyPixels(
                        _cache,
                        0,
                        sourceTop,
                        _cacheRegion.Width,
                        height,
                        target,
                        _cacheRegion.X,
                        _cacheRegion.Y + destinationTop
                    );
                }
                _cacheLead = key.Lead;
                if (reuse)
                {
                    int count = buf.Runs.Length - (scroll.EndRow - scroll.FirstRow);
                    if (_scrollPaintRows.Length != count)
                        _scrollPaintRows = new int[count];
                    int index = 0;
                    for (int row = 0; row < buf.Runs.Length; row++)
                        if (row < scroll.FirstRow || row >= scroll.EndRow)
                            _scrollPaintRows[index++] = row;
                    PaintRows(body, buf, key.CellW, key.CellH, _scrollPaintRows);
                    PerfTrace.Count(
                        "terminal-cache-scroll-rows-reused",
                        scroll.EndRow - scroll.FirstRow
                    );
                    PerfTrace.Count("terminal-cache-rows-repainted", count);
                }
                else if (broad)
                    Paint(body, buf, key.CellW, key.CellH);
                else
                {
                    PerfTrace.Count("terminal-cache-rows-repainted", buf.ChangedRows.Length);
                    PaintRows(body, buf, key.CellW, key.CellH, buf.ChangedRows);
                }
                if (canCopy)
                    CopyPixels(
                        target,
                        _cacheRegion.X,
                        _cacheRegion.Y + damageTop,
                        _cacheRegion.Width,
                        damageHeight,
                        _cache,
                        0,
                        damageTop
                    );
            }
            finally
            {
                RenderTexture.active = was;
                if (scratch != null)
                    RenderTexture.ReleaseTemporary(scratch);
                PerfTrace.End(
                    reuse ? "terminal-cache-scroll-paint"
                        : broad ? "terminal-cache-full-paint"
                        : "terminal-cache-row-paint",
                    started,
                    1
                );
            }
            // The key retains the font revision from before Paint: atlas rebuilds during
            // glyph submission must force another full paint on the following frame.
        }

        static void CopyPixels(
            RenderTexture source,
            int x,
            int top,
            int width,
            int height,
            RenderTexture destination,
            int destinationX,
            int destinationTop
        )
        {
            bool startsAtTop = SystemInfo.graphicsUVStartsAtTop;
            Graphics.CopyTexture(
                source,
                0,
                0,
                x,
                TerminalCacheRegion.TextureY(top, height, source.height, startsAtTop),
                width,
                height,
                destination,
                0,
                0,
                destinationX,
                TerminalCacheRegion.TextureY(
                    destinationTop,
                    height,
                    destination.height,
                    startsAtTop
                )
            );
        }

        TerminalCacheKey CacheKey(Rect body, ScreenBuf buf, float cw, float ch)
        {
            return new TerminalCacheKey
            {
                Buffer = buf,
                Session = _state.Name,
                Offset = buf.Off,
                AltScreen = buf.AltScreen,
                X = body.x,
                Y = body.y,
                Width = body.width,
                Height = body.height,
                CellW = cw,
                CellH = ch,
                Lead = CacheLead(body, ch),
                Theme = TerminalTheme.Rev,
                Font = TerminalFont.Rev,
            };
        }

        void TraceCacheDamage(ScreenBuf buf, bool repaintAll)
        {
            if (
                PerfTrace.Enabled
                && !repaintAll
                && buf.ContentRevision == unchecked(_cacheContentRevision + 1)
                && buf.ChangedRows != null
                && buf.ChangedRows.Length > 0
            )
            {
                int changed = buf.ChangedRows.Length;
                int total = System.Math.Max(1, buf.Lines?.Length ?? 0);
                string band =
                    changed == total ? "100"
                    : changed * 4 >= total * 3 ? "75-99"
                    : changed * 2 >= total ? "50-74"
                    : "0-49";
                PerfTrace.Count("terminal-cache-damage-" + band, changed);
            }
        }

        void DisableCache(System.Exception e)
        {
            _noCache = true;
            Drop();
            Log.Warning($"[SlopWorld] pane cache off, drawing straight to the screen: {e}");
        }

        // Draw the last complete pane frame. During a session handoff the new reader can exist
        // before its first screen arrives. Keeping this frame avoids exposing that transport gap
        // as a close-and-reopen of the terminal.
        bool BlitCached(Rect body)
        {
            SyncSnap();
            return DrawCached(body, CacheSource(body), 0f);
        }

        bool DrawCached(Rect destination, Rect source, float sourceExtraBottom)
        {
            if (_cache == null || !_cache.IsCreated())
            {
                PerfTrace.Count("terminal-cache-missing");
                return false;
            }
            // The shared texture is only a valid fallback while this session remains active.
            // A switched tab must use its own displayed-frame snapshot or wait for its first
            // screen. Showing the previous tab for one frame reads as terminal flicker.
            if (_cacheKey.Session != _state.Name)
            {
                PerfTrace.Count("terminal-cache-session-mismatch");
                return false;
            }
            if (Event.current.type != EventType.Repaint)
                return true;

            float debugStarted = ScrollDebugTimer();
            long blitStarted = PerfTrace.Start();
            bool drawn = false;

            int pw = _cache.width,
                ph = _cache.height;
            if (
                Screen.width != _cacheScreenWidth
                || Screen.height != _cacheScreenHeight
                || _snapSx != _cacheScaleX
                || _snapSy != _cacheScaleY
            )
            {
                PerfTrace.Count("terminal-cache-size-mismatch");
                Drop();
                return false;
            }

            float x0 = source.x * _cacheScaleX + _cacheOriginX - _cacheRegion.X;
            float y0 = source.y * _cacheScaleY + _cacheOriginY - _cacheRegion.Y;
            float x1 = x0 + source.width * _snapSx;
            float y1 = y0 + (source.height + sourceExtraBottom) * _snapSy;
            // The crop has no texels outside its retained bounds. Sampling beyond its edge makes
            // the GPU repeat or clamp its last scanline. This turns the bottom terminal row into
            // barcode-like vertical streaks during fractional scrolling.
            bool edgeOverrun = x0 < 0f || y0 < 0f || x1 > pw || y1 > ph;
            if (!TerminalCacheSampling.TryClamp(pw, ph, ref x0, ref y0, ref x1, ref y1))
            {
                PerfTrace.Count("terminal-cache-out-of-bounds");
                return false;
            }
            if (edgeOverrun)
                PerfTrace.Count("terminal-cache-edge-clamps");
            float u0 = x0 / pw,
                u1 = x1 / pw;

            // texCoords y counts from the destination's bottom either way. Which end of
            // the texture that is, is where the API put the target's first row.
            float v0,
                v1;
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                v0 = y1 / ph;
                v1 = y0 / ph;
            }
            else
            {
                v0 = 1f - y1 / ph;
                v1 = 1f - y0 / ph;
            }

            var tint = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(destination, _cache, new Rect(u0, v0, u1 - u0, v1 - v0));
            GUI.color = tint;
            drawn = true;
            ScrollDebugBlit(debugStarted, drawn);
            PerfTrace.End("terminal-cache-blit", blitStarted, 1);
            return true;
        }

        void Drop()
        {
            if (_cache == null)
                return;
            _cache.Release();
            UnityEngine.Object.Destroy(_cache);
            _cache = null;
            _cacheKey = default(TerminalCacheKey);
            _cacheContentRevision = -1;
            _cacheLead = 0f;
        }

        // Fractional history needs one row below the normal viewport. Put the baked rows one
        // cell above the pane so that this overscan row remains inside the screen-sized cache.
        // The source rect is translated back when it is drawn into the pane.
        float CacheLead(Rect body, float ch)
        {
            if (ch <= 0.01f)
                return 0f;
            float top = body.y + _snapOy / _snapSy;
            return Mathf.Clamp(Mathf.Min(ch, top), 0f, ch);
        }

        Rect CacheSource(Rect body) =>
            new Rect(body.x, body.y - _cacheLead, body.width, body.height);
    }
}
