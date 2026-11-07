using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A full cache repaint records its opaque background and glyphs in one GUI draw target.
    // GL.Clear uses a separate GPU command stream from the native GUI texture baker.
    static class TerminalCachePaint
    {
        public static void CopyCompleted(RenderTexture previous, Action copy)
        {
            // Waylith bakes queued GUI glyphs when the target closes. CopyTexture
            // submits GPU work but does not flush that GUI batch itself.
            RenderTexture.active = previous;
            copy();
        }

        public static void Full(RenderTexture target, Rect bounds, Color background, Action paint)
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                Widgets.DrawBoxSolid(bounds, background);
                paint();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }
}
