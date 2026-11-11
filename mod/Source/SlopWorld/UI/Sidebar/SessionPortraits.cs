using System;
using UnityEngine;

namespace SlopWorld
{
    // Presentation-only host snapshots for rows without a pawn. The provider owns
    // texture lifetime; native pawn portraits retain their existing renderer/cache.
    public static class SessionPortraits
    {
        public static Func<string, Texture> Source;

        internal static void Draw(string session, Rect face, AgentState state)
        {
            if (Event.current.type != EventType.Repaint || session == null || Source == null)
                return;
            var texture = Source(session);
            if (texture != null)
                Patch_SidebarPortraitDraw.DrawPortrait(face, texture, state == AgentState.Down, 1f);
        }
    }
}
