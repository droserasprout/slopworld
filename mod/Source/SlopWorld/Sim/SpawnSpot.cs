using System.Collections.Generic;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Where a new agent's colonist lands.
    ///
    /// CellFinder.RandomSpawnCellForPawnNear is not enough on its own. It asks for
    /// standable, unfogged, unoccupied and reachable - but reachable *from the root
    /// it was handed*, and a root sitting in a pocket inside a mountain satisfies
    /// all four while the pawn cannot walk anywhere at all. Since the anchor was
    /// map.Center, which is inside rock more often than you would think, agents
    /// ended up sealed in stone and immobilised for the life of the colony - and
    /// an immobilised agent is not obviously broken, it just never moves.
    ///
    /// So every candidate here has to sit in a room that touches the map edge.
    /// Rooms are bounded by walls and natural rock counts, so a sealed pocket is a
    /// room that does not reach the edge, while a cave with its mouth open to the
    /// sky is the same room as the outdoors and passes. That is the one question
    /// vanilla's finder never asks: not "can I stand here" but "can I leave".
    /// </summary>
    public static class SpawnSpot
    {
        // How far out from the anchor to look before giving up on staying close.
        const float NearRadius = 30f;

        // Stop collecting once there are this many to choose from. The radial
        // pattern is ordered by distance, so the ones found first are the closest,
        // and picking among a handful of them keeps agents together without
        // stacking every new one on the same cell.
        const int Candidates = 40;

        // Random probes before falling back. On a mountainous map the open ground
        // can be most of the way across the map from the middle.
        const int MapTries = 500;

        /// <summary>An open cell near <paramref name="anchor"/>, or as close to that
        /// as the map allows. Always returns something spawnable: a colonist in a
        /// wall is bad, but a session with no colonist at all is worse.</summary>
        public static IntVec3 Find(Map map, IntVec3 anchor)
        {
            if (map == null) return IntVec3.Invalid;
            if (!anchor.IsValid || !anchor.InBounds(map)) anchor = map.Center;

            if (TryNear(map, anchor, out var cell)) return cell;
            if (TryAnywhere(map, out cell)) return cell;

            Log.Warning("[SlopWorld] no open spawn cell found; using the map centre");
            return CellFinder.RandomSpawnCellForPawnNear(map.Center, map);
        }

        static bool TryNear(Map map, IntVec3 anchor, out IntVec3 cell)
        {
            var found = new List<IntVec3>();
            int cells = GenRadial.NumCellsInRadius(NearRadius);

            for (int i = 0; i < cells && found.Count < Candidates; i++)
            {
                var c = anchor + GenRadial.RadialPattern[i];
                if (Open(map, c)) found.Add(c);
            }

            cell = found.Count > 0 ? found.RandomElement() : IntVec3.Invalid;
            return found.Count > 0;
        }

        // The anchor's whole neighbourhood is sealed, off the map or unexplored.
        // Probing beats widening the ring: the ring would spend its time re-testing
        // the same rock, and there is no reason to prefer any particular direction.
        static bool TryAnywhere(Map map, out IntVec3 cell)
        {
            var size = map.Size;
            for (int i = 0; i < MapTries; i++)
            {
                var c = new IntVec3(Rand.Range(0, size.x), 0, Rand.Range(0, size.z));
                if (Open(map, c)) { cell = c; return true; }
            }
            cell = IntVec3.Invalid;
            return false;
        }

        /// <summary>Somewhere a pawn can stand and then walk away from. The first
        /// three are vanilla's own conditions; the room is the one it leaves out.
        /// Fogged is kept because it is what confines spawns to the explored ground
        /// around the core, which is where the rest of the colony is.</summary>
        static bool Open(Map map, IntVec3 c)
        {
            if (!c.InBounds(map)) return false;
            if (!c.Standable(map)) return false;
            if (c.Fogged(map)) return false;
            if (c.GetFirstPawn(map) != null) return false;

            var room = RegionAndRoomQuery.RoomAt(c, map);
            return room != null && room.TouchesMapEdge;
        }
    }
}
