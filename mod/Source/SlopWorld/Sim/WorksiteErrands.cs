using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public partial class Worksite
    {
        // A run is Least..Most items in Lines rows with Gap cells between them. The whole
        // run is pitched in one pass so its frames reserve the ground before construction;
        // spacing comes from the rotated footprint rather than duplicated def dimensions.
        struct Run
        {
            public int Least, Most, Lines, Gap;
        }

        struct Errand
        {
            public BuildableDef What;
            public float Seconds;
            public float Weight;
            public float Bloom; // cells of plague the finished thing seeds
            public Run Run; // how many go down at once, and in what shape
        }

        // Keyed on what is being built, not on the frame, which is gone by the time
        // anything asks twice. Static: Patch_ErrandWork has no map to ask.
        static readonly Dictionary<BuildableDef, float> Work = new Dictionary<BuildableDef, float>();
        static readonly Dictionary<BuildableDef, float> Blooms = new Dictionary<BuildableDef, float>();

        // Ensure() here rather than at the next errand: a load comes back with frames on
        // the board and pawns already swinging, and the first thing to ask about one is
        // Patch_ErrandWork. An empty table there is a plate costing vanilla's 1100 ticks.
        public static float WorkFor(BuildableDef def)
        {
            if (def == null) return 0f;
            Ensure();
            return Work.TryGetValue(def, out float w) ? w : 0f;
        }

        public static float BloomFor(BuildableDef def)
        {
            if (def == null) return 0f;
            Ensure();
            return Blooms.TryGetValue(def, out float r) ? r : 0f;
        }

        static void Ensure() { if (_errands == null) { var _ = Errands; } }

        // Nothing on the list is the map's; the stone is, and Blocks() settles that.
        static List<Errand> _errands;
        static bool _grandmaModeCached;
        static TerrainDef _plate;

        static List<Errand> Errands
        {
            get
            {
                bool grandma = Settings.GrandmaMode;
                if (_errands != null && _grandmaModeCached == grandma) return _errands;
                _grandmaModeCached = grandma;
                _errands = new List<Errand>();

                // Metal plate rather than the tile's flagstone, which read as a garden path
                // through a dead world.
                _plate = DefDatabase<TerrainDef>.GetNamedSilentFail("MetalTile");

                Add(_plate, PavingSeconds, PavingOdds, PavingBloom,
                    new Run { Least = PavingSide, Most = PavingSide, Lines = PavingSide });

                if (grandma)
                {
                    // Grandma mode: furniture and flower pots instead of graves and obelisks.
                    Add(Named("FlowerPot"), SmallSeconds, 5f, SmallBloom,
                        new Run { Least = 3, Most = 6, Gap = 1 });
                    Add(Named("Chair"), SmallSeconds, 3f, SmallBloom,
                        new Run { Least = 2, Most = 5, Gap = 1 });
                    Add(Named("Armchair"), SmallSeconds, 2f, SmallBloom);
                    Add(Named("Table1x2c"), SmallSeconds, 2f, SmallBloom);
                    Add(Named("EndTable"), SmallSeconds, 2f, SmallBloom);
                    Add(Named("Bookshelf"), MediumSeconds, 2f, MediumBloom);
                    Add(Named("Dresser"), MediumSeconds, 2f, MediumBloom);
                    Add(Named("Lamp"), SmallSeconds, 3f, SmallBloom);
                    Add(Named("Bed"), MediumSeconds, 2f, MediumBloom);
                }
                else
                {
                    Add(Named("Column"), SmallSeconds, ColumnOdds, SmallBloom);
                    Add(Named("Grave"), SmallSeconds, GraveOdds, SmallBloom,
                        new Run { Least = GraveRowLeast, Most = GraveRowMost, Gap = GraveAisle });
                    Add(Named("Sarcophagus"), MediumSeconds, SarcophagusOdds, MediumBloom);
                    Add(Named("SteleLarge"), LargeSeconds, SteleLargeOdds, LargeBloom);
                    Add(Named("SteleGrand"), MonumentSeconds, SteleGrandOdds, MonumentBloom);
                }

                // Ruin scenery vanilla lets no player build; Patches/AncientBuildings.xml is
                // what hands them a frame. They cost nothing and want no skill. The lamp is
                // the only one that is not decoration - a CompGlower with neither a power
                // comp nor a fuel one, the one light in the game that simply burns.
                Add(Named("AncientLamp"), SmallSeconds, LampOdds, SmallBloom);
                Add(Named("AncientLamppost"), SmallSeconds, LamppostOdds, SmallBloom);
                Add(Named("AncientSystemRack"), MediumSeconds, RackOdds, MediumBloom);
                Add(Named("AncientDisplayBank"), MediumSeconds, ScreensOdds, MediumBloom);
                Add(Named("AncientLockerBank"), MediumSeconds, LockersOdds, MediumBloom);
                Add(Named("AncientGenerator"), MediumSeconds, GeneratorOdds, MediumBloom);
                Add(Named("AncientMachine"), MonumentSeconds, MachineOdds, MonumentBloom);

                if (_errands.Count == 0)
                    Log.Warning("[SlopWorld] no errands this build knows how to build; agents will stand about");

                return _errands;
            }
        }

        static void Add(BuildableDef what, float seconds, float weight, float bloom,
                        Run run = default(Run))
        {
            if (what == null || what.frameDef == null) return;

            // So an omitted run is one thing on its own rather than none of it.
            run.Least = Mathf.Max(1, run.Least);
            run.Most = Mathf.Max(run.Least, run.Most);
            run.Lines = Mathf.Max(1, run.Lines);
            run.Gap = Mathf.Max(0, run.Gap);

            _errands.Add(new Errand
            {
                What = what,
                Seconds = seconds,
                Weight = weight,
                Bloom = bloom,
                Run = run,
            });
            Work[what] = seconds * RealClock.TicksPerRealSecond * WorkPerTick;
            Blooms[what] = bloom;
        }

        static ThingDef Named(string name) => DefDatabase<ThingDef>.GetNamedSilentFail(name);
    }
}
