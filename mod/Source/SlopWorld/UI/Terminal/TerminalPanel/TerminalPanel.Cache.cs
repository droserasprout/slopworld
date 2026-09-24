using UnityEngine;
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

        // Cache the pane between screen frames: the daemon updates more slowly than the monitor.
        // Use an opaque screen-sized target so GUI coordinates and glyph edges remain stable.
        // disable the cache for the process after a render-target failure.
        bool Blit(Rect body, ScreenBuf buf, float cw, float ch)
        {
            if (_noCache) return false;
            // Labels and boxes draw on repaint and nowhere else.
            if (Event.current.type != EventType.Repaint) return true;

            try
            {
                if (!EnsureCache(body, buf, cw, ch)) return false;
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
            int pw = Screen.width, ph = Screen.height;
            if (pw <= 0 || ph <= 0) return false;

            if (_cache != null && (_cache.width != pw || _cache.height != ph)) Drop();

            bool fresh = _cache == null;
            if (fresh)
                _cache = new RenderTexture(pw, ph, 0, RenderTextureFormat.ARGB32)
                {
                    name = "SlopWorldPane",
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.DontUnloadUnusedAsset, // see TerminalFont
                };

            if (!_cache.IsCreated()) { _cache.Create(); fresh = true; }

            var key = new TerminalCacheKey
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
            bool repaintAll = fresh || !_cacheKey.Matches(key);
            var repaint = TerminalRepaintPolicy.Choose(repaintAll, _cacheContentRevision, buf);
            if (repaint == TerminalRepaint.None) PerfTrace.Count("terminal-cache-hits");
            if (repaintAll)
            {
                if (PerfTrace.Enabled && _cacheKey.Buffer != null &&
                    _cacheKey.Offset > 0 && key.Offset > 0 &&
                    _cacheKey.Offset != key.Offset && _cacheKey.SameSurface(key))
                {
                    PerfTrace.Count("terminal-cache-scroll-shifts");
                    int rows = TerminalScrollReuse.MatchingRows(_cacheKey, key);
                    if (rows > 0)
                    {
                        PerfTrace.Count("terminal-cache-scroll-overlap", rows);
                        if (TerminalScrollReuse.PixelAligned(_cacheKey.Offset, key.Offset,
                                                             ch, _snapSy))
                            PerfTrace.Count("terminal-cache-scroll-copyable", rows);
                    }
                }
                PerfTrace.Count("terminal-cache-misses");
                PerfTrace.Count("terminal-cache-repaints");
                long paintStarted = PerfTrace.Start();
                // Keep the pre-paint revision. RequestCharactersInTexture can rebuild the
                // atlas while Paint is running. Retaining the old revision forces one clean
                // repaint after that rebuild instead of caching a half-drawn first frame.
                var was = RenderTexture.active;
                try
                {
                    RenderTexture.active = _cache;
                    GL.Clear(false, true, SolidTerminalBackground);
                    _cacheLead = CacheLead(body, ch);
                    Paint(new Rect(body.x, body.y - _cacheLead, body.width,
                                   body.height + _cacheLead), buf, cw, ch);
                }
                finally
                {
                    RenderTexture.active = was;
                    PerfTrace.End("terminal-cache-full-paint", paintStarted, 1);
                }

                _cacheKey = key;
            }
            else if (repaint != TerminalRepaint.None)
            {
                PerfTrace.Count("terminal-cache-repaints");
                // Small live edits only invalidate their rows. A broad terminal scroll changes
                // most rows, where one full paint is cheaper and avoids many GUI draw calls.
                bool broad = repaint == TerminalRepaint.Full;
                long paintStarted = PerfTrace.Start();
                var was = RenderTexture.active;
                try
                {
                    RenderTexture.active = _cache;
                    if (broad)
                    {
                        PerfTrace.Count("terminal-cache-broad-repaints");
                        GL.Clear(false, true, SolidTerminalBackground);
                        Paint(new Rect(body.x, body.y - _cacheLead, body.width,
                                       body.height + _cacheLead), buf, cw, ch);
                    }
                    else
                    {
                        PerfTrace.Count("terminal-cache-rows-repainted", buf.ChangedRows.Length);
                        PaintRows(new Rect(body.x, body.y - _cacheLead, body.width,
                                           body.height + _cacheLead), buf, cw, ch,
                                  buf.ChangedRows);
                    }
                }
                finally
                {
                    RenderTexture.active = was;
                    PerfTrace.End(broad ? "terminal-cache-full-paint" :
                        "terminal-cache-row-paint", paintStarted, 1);
                }
            }

            _cacheContentRevision = buf.ContentRevision;

            return true;
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
            if (Event.current.type != EventType.Repaint) return true;

            float debugStarted = ScrollDebugTimer();
            long blitStarted = PerfTrace.Start();
            bool drawn = false;

            int pw = Screen.width, ph = Screen.height;
            if (pw <= 0 || ph <= 0 || _cache.width != pw || _cache.height != ph)
            {
                PerfTrace.Count("terminal-cache-size-mismatch");
                Drop();
                return false;
            }

            float x0 = source.x * _snapSx + _snapOx;
            float y0 = source.y * _snapSy + _snapOy;
            float x1 = x0 + source.width * _snapSx;
            float y1 = y0 + (source.height + sourceExtraBottom) * _snapSy;
            // A screen-sized cache has no texels outside the screen. Sampling beyond its edge makes
            // the GPU repeat or clamp its last scanline. This turns the bottom terminal row into
            // barcode-like vertical streaks during fractional scrolling.
            bool edgeOverrun = x0 < 0f || y0 < 0f || x1 > pw || y1 > ph;
            if (!TerminalCacheSampling.TryClamp(pw, ph, ref x0, ref y0, ref x1, ref y1))
            {
                PerfTrace.Count("terminal-cache-out-of-bounds");
                return false;
            }
            if (edgeOverrun) PerfTrace.Count("terminal-cache-edge-clamps");
            float u0 = x0 / pw, u1 = x1 / pw;

            // texCoords y counts from the destination's bottom either way. Which end of
            // the texture that is, is where the API put the target's first row.
            float v0, v1;
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
            GUI.DrawTextureWithTexCoords(
                destination, _cache, new Rect(u0, v0, u1 - u0, v1 - v0));
            GUI.color = tint;
            drawn = true;
            ScrollDebugBlit(debugStarted, drawn);
            PerfTrace.End("terminal-cache-blit", blitStarted, 1);
            return true;
        }

        void Drop()
        {
            if (_cache == null) return;
            _cache.Release();
            Object.Destroy(_cache);
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
            if (ch <= 0.01f) return 0f;
            float top = body.y + _snapOy / _snapSy;
            return Mathf.Clamp(Mathf.Min(ch, top), 0f, ch);
        }

        Rect CacheSource(Rect body) =>
            new Rect(body.x, body.y - _cacheLead, body.width, body.height);

    }
}
