using RimWorld;
using Verse;

namespace SlopWorld
{
    // Vanilla puts a box up a few days in - the faction's, the settlement's, and the
    // combined one - from Faction.FactionTick, gated on nothing more than
    // Faction.OfPlayer.HasName and Settlement.namedByPlayer being false. So closing
    // all three is a matter of answering them up front rather than of patching
    // anything, and nothing has to be kept in step if the dialog moves.
    //
    // FinalizeInit and not the quick start: the settlement is not made until map
    // generation, and FinalizeInit runs on a loaded save too, so a colony saved
    // before this existed is named on its next load instead of being asked.
    public class ColonyNames : GameComponent
    {
        // As it reads in the colonist bar's tooltips and on the world map.
        public const string FactionName = "Clankers";

        // The settlement, which is what the colony is called everywhere it is named.
        public const string SettlementName = "SlopWorld";

        public ColonyNames(Game game) { }

        public override void FinalizeInit()
        {
            var player = Faction.OfPlayerSilentFail;
            if (player == null) return;

            // The setter is a plain field write; NamePlayerFactionDialogUtility.Named is that
            // plus a permadeath savefile rename, which is a long event queued for a mode this
            // colony is never in.
            if (player.Name != FactionName) player.Name = FactionName;

            var objects = Find.WorldObjects;
            if (objects == null) return;

            foreach (var settlement in objects.Settlements)
            {
                if (settlement.Faction != player) continue;
                // Through the utility because namedByPlayer is the half that closes the dialog,
                // and this is the one place vanilla says how both are set.
                NamePlayerSettlementDialogUtility.Named(settlement, SettlementName);
            }
        }
    }
}
