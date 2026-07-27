using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The colony's two names, written rather than asked for.
    ///
    /// Vanilla puts a box up a few days in - Dialog_NamePlayerFaction, and the
    /// settlement's own, and the combined one when both are due - offering a
    /// generated name and a text field. Faction.FactionTick is what opens it, on
    /// the thousand-tick beat, and it is gated on nothing more than
    /// Faction.OfPlayer.HasName being false and Settlement.namedByPlayer being
    /// false. Which makes closing it a matter of answering it up front rather
    /// than of patching anything: a faction with a name and a settlement flagged
    /// as named are two conditions vanilla itself will never ask about again.
    ///
    /// So there is no patch here and no window suppression - the state the dialog
    /// exists to reach is simply already there, which also means nothing has to
    /// be kept in step if the dialog moves in a future version.
    ///
    /// FinalizeInit and not the quick start, for two reasons. The settlement does
    /// not exist yet while Patch_QuickStart is building the game - it is made on
    /// the way into map generation - and FinalizeInit runs on a loaded save as
    /// well, so a colony saved before this existed comes back named instead of
    /// being asked about on its next in-game morning.
    ///
    /// GameComponents are built for every subclass automatically, so this needs no
    /// def.
    /// </summary>
    public class ColonyNames : GameComponent
    {
        /// The faction, as it reads in the colonist bar's tooltips and on the world map.
        public const string FactionName = "Clankers";

        /// The settlement, which is what the colony is called everywhere it is named.
        public const string SettlementName = "SlopWorld";

        public ColonyNames(Game game) { }

        public override void FinalizeInit()
        {
            var player = Faction.OfPlayerSilentFail;
            if (player == null) return;

            // The setter is a plain field write; NamePlayerFactionDialogUtility.Named
            // is the same write plus a permadeath savefile rename, which is a long
            // event queued for a mode this colony is never in.
            if (player.Name != FactionName) player.Name = FactionName;

            var objects = Find.WorldObjects;
            if (objects == null) return;

            foreach (var settlement in objects.Settlements)
            {
                if (settlement.Faction != player) continue;
                // Through the utility because namedByPlayer is the half that closes
                // the dialog, and this is the one place vanilla says how both are set.
                NamePlayerSettlementDialogUtility.Named(settlement, SettlementName);
            }
        }
    }
}
