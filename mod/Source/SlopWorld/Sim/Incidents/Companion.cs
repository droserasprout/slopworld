using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public class Companion : GameComponent
    {
        static readonly string TargetDef = System.Text.Encoding.UTF8.GetString(new byte[] { 0x43, 0x61, 0x70, 0x79, 0x62, 0x61, 0x72, 0x61 });
        const float BubbleSeconds = 3f;
        const float BadgeSize = 24f;
        const float BadgeLift = 48f;

        readonly HashSet<Pawn> _immune = new HashSet<Pawn>();
        Pawn _bubblePawn;
        float _bubbleUntil;
        bool _used;

        static Texture2D _badge;

        public Companion(Game game) { }

        // Runtime-only state resets on load. Activation also clears existing plague marks.
        public static bool IsImmune(Pawn pawn) =>
            pawn != null && Verse.Current.Game?.GetComponent<Companion>()?._immune.Contains(pawn) == true;

        public override void GameComponentOnGUI()
        {
            if (Eco.Bare || UiLayout.Hidden) return;
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return;

            var map = Find.CurrentMap;
            if (map == null) return;

            if (!_used && Event.current.type == EventType.MouseDown && Event.current.button == 0
                && Find.WindowStack.FloatMenu == null)
            {
                var pawn = At(map, UI.MouseCell());
                if (pawn != null)
                {
                    Event.current.Use();
                    Activate(map, pawn);
                }
            }

            DrawBubble(map);
        }

        static Pawn At(Map map, IntVec3 cell)
        {
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                if (pawn.Position == cell && pawn.def?.defName == TargetDef)
                    return pawn;
            return null;
        }

        void Activate(Map map, Pawn clicked)
        {
            _used = true;
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.def?.defName != TargetDef) continue;

                _immune.Add(pawn);
                var plague = pawn.health?.hediffSet?.GetFirstHediffOfDef(ModDefOf.SlopPlague);
                if (plague != null) pawn.health.RemoveHediff(plague);
            }

            _bubblePawn = clicked;
            _bubbleUntil = Time.realtimeSinceStartup + BubbleSeconds;
        }

        void DrawBubble(Map map)
        {
            if (_bubblePawn == null || _bubblePawn.Destroyed || !_bubblePawn.Spawned
                || _bubblePawn.Map != map)
                return;
            if (Time.realtimeSinceStartup >= _bubbleUntil)
            {
                _bubblePawn = null;
                return;
            }

            var badge = Badge;
            if (badge == null) return;

            var pos = GenMapUI.LabelDrawPosFor(_bubblePawn, -0.85f);
            var badgeRect = new Rect(pos.x - BadgeSize / 2f, pos.y - BadgeSize - BadgeLift,
                BadgeSize, BadgeSize);
            const float margin = 4f;
            badgeRect.x = Mathf.Clamp(badgeRect.x, margin, UI.screenWidth - margin - badgeRect.width);
            badgeRect.y = Mathf.Clamp(badgeRect.y, margin, UI.screenHeight - margin - badgeRect.height);

            GUI.DrawTexture(badgeRect, badge, ScaleMode.ScaleToFit, true);
        }

        static Texture2D Badge
        {
            get
            {
                if (_badge == null)
                {
                    _badge = ContentFinder<Texture2D>.Get("SlopWorld/Marks/07", false);
                    if (_badge != null) _badge.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                return _badge;
            }
        }
    }
}
