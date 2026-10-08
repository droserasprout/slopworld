using RimWorld;
using Verse;

namespace SlopWorld
{
    // FactionTick requests faction and settlement names when they are absent.
    // Set both in FinalizeInit before the prompt. FinalizeInit also runs for loaded saves.
    public class ColonyNames : GameComponent
    {
        // Faction name for colonist-bar tooltips and the world map.
        public const string FactionName = "Clankers";

        // Settlement name used to identify the colony.
        public const string SettlementName = "SlopWorld";

        public ColonyNames(Game game) { }

        public override void FinalizeInit()
        {

            var player = Faction.OfPlayerSilentFail;
            if (player == null) return;

            // Set the name directly.
            // NamePlayerFactionDialogUtility.Named also queues a permadeath save-file rename, which this colony does not need.
            if (player.Name != FactionName) player.Name = FactionName;

            var objects = Find.WorldObjects;
            if (objects == null) return;

            foreach (var settlement in objects.Settlements)
            {
                if (settlement.Faction != player) continue;
                // Use the utility to set both the name and namedByPlayer.
                // The namedByPlayer flag prevents the naming dialog.
                NamePlayerSettlementDialogUtility.Named(settlement, SettlementName);
            }
        }
    }
}
