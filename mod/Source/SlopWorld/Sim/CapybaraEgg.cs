using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Clicking a capybara once per loaded game gives every capybara currently on the map
    // immunity from SlopWorld's plague and lets the chosen one fly the flag of Uruguay.
    public class CapybaraEgg : GameComponent
    {
        const string CapybaraDefName = "Capybara";
        const float BubbleSeconds = 3f;
        // Keep the flag close to the capybara's on-map footprint rather than making a large
        // Keep it easy to miss at a glance and visibly floating above the capybara.
        const float FlagSize = 12f;
        const float FlagLift = 36f;

        readonly HashSet<Pawn> _immune = new HashSet<Pawn>();
        Pawn _bubblePawn;
        float _bubbleUntil;
        bool _used;

        static Texture2D _uruguay;

        public CapybaraEgg(Game game) { }

        // Runtime-only state is deliberate: starting a loaded game gives the egg a fresh use,
        // while a capybara already saved with a plague mark is cleaned up when it is activated.
        public static bool IsImmune(Pawn pawn) =>
            pawn != null && Verse.Current.Game?.GetComponent<CapybaraEgg>()?._immune.Contains(pawn) == true;

        public override void GameComponentOnGUI()
        {
            if (Eco.Bare || SlopLayout.Hidden) return;
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() != null) return;

            var map = Find.CurrentMap;
            if (map == null) return;

            if (!_used && Event.current.type == EventType.MouseDown && Event.current.button == 0
                && Find.WindowStack.FloatMenu == null)
            {
                var capybara = At(map, UI.MouseCell());
                if (capybara != null)
                {
                    Event.current.Use();
                    Activate(map, capybara);
                }
            }

            DrawBubble(map);
        }

        static Pawn At(Map map, IntVec3 cell)
        {
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                if (pawn.Position == cell && pawn.def?.defName == CapybaraDefName)
                    return pawn;
            return null;
        }

        void Activate(Map map, Pawn clicked)
        {
            _used = true;
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.def?.defName != CapybaraDefName) continue;

                _immune.Add(pawn);
                var plague = pawn.health?.hediffSet?.GetFirstHediffOfDef(SlopDefOf.SlopPlague);
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

            var flag = Uruguay;
            if (flag == null) return;

            var pos = GenMapUI.LabelDrawPosFor(_bubblePawn, -0.85f);
            var flagRect = new Rect(pos.x - FlagSize / 2f, pos.y - FlagSize - FlagLift,
                FlagSize, FlagSize);
            const float margin = 4f;
            flagRect.x = Mathf.Clamp(flagRect.x, margin, UI.screenWidth - margin - flagRect.width);
            flagRect.y = Mathf.Clamp(flagRect.y, margin, UI.screenHeight - margin - flagRect.height);

            GUI.DrawTexture(flagRect, flag, ScaleMode.ScaleToFit, true);
        }

        static Texture2D Uruguay
        {
            get
            {
                if (_uruguay == null)
                {
                    _uruguay = ContentFinder<Texture2D>.Get("SlopWorld/Uruguay", false);
                    if (_uruguay != null) _uruguay.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                return _uruguay;
            }
        }
    }
}
