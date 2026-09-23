using System.Collections.Generic;
using Verse;

namespace SlopWorld
{
    // Select spawn cells in a room that touches the map edge.
    // The base game's reachability test can accept an isolated area that the pawn cannot leave.
    public static class SpawnSpot
    {
        // Search within this distance of the anchor before searching the full map.
        const float NearRadius = 30f;

        // The radial pattern orders cells by distance.
        // Select from nearby cells to keep agents together without placing them on the same cell.
        const int Candidates = 40;

        // On mountain maps, open ground can be far from the anchor.
        const int MapTries = 500;

        // If both searches fail, use the base game's spawn search near the map center.
        public static IntVec3 Find(Map map, IntVec3 anchor)
        {
            if (map == null) return IntVec3.Invalid;
            if (!anchor.IsValid || !anchor.InBounds(map)) anchor = map.Center;

            if (TryNear(map, anchor, out var cell)) return cell;
            if (TryAnywhere(map, out cell)) return cell;

            Log.Warning("[SlopWorld] No open spawn cell found. Using the map centre.");
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

        // Sample random cells across the map instead of repeating the search near the anchor.
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

        // Require a standable, explored cell with no pawn.
        // Also require a room that touches the map edge.
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
