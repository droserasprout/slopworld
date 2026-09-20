using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Retain sections and simulation grids: arrivals still call MapMeshDirty while resting.
    // Only audited vanilla layer types are released; derived/mod layers may own extra buffers.
    static class EcoMapMemory
    {
        static readonly Type[] Rebuildable =
        {
            typeof(SectionLayer_Terrain), typeof(SectionLayer_ThingsGeneral),
            typeof(SectionLayer_FogOfWar), typeof(SectionLayer_LightingOverlay),
            typeof(SectionLayer_Snow), typeof(SectionLayer_Sand)
        };
        static readonly FieldInfo Sections = AccessTools.Field(typeof(MapDrawer), "sections");
        static readonly FieldInfo FormerlyEnabled =
            AccessTools.Field(typeof(SectionLayer_Things), "tmpFormerlyEnabled");
        static readonly FieldInfo Atlases =
            AccessTools.Field(typeof(GlobalTextureAtlasManager), "pawnTextureAtlases");
        sealed class State { public bool Released; }
        static readonly ConditionalWeakTable<MapDrawer, State> States =
            new ConditionalWeakTable<MapDrawer, State>();
        static readonly ConditionalWeakTable<SectionLayer, State> ReleasedLayers =
            new ConditionalWeakTable<SectionLayer, State>();
        static readonly ConditionalWeakTable<MapDrawer, State>.CreateValueCallback NewState =
            _ => new State();

        internal static bool BeforeMeshUpdate(MapDrawer drawer)
        {
            if (!Eco.Resting)
            {
                Restore(drawer);
                return true;
            }
            Release(drawer);
            return false;
        }

        static void Release(MapDrawer drawer)
        {
            var state = States.GetValue(drawer, NewState);
            if (state.Released) return;
            var sections = (Section[,])Sections.GetValue(drawer);
            // Initialization must finish before taking ownership of any layer geometry.
            if (sections == null) return;
            foreach (var section in sections)
                if (section == null) return;

            int layers = 0, meshes = 0;
            long buffers = 0;
            foreach (var section in sections)
                foreach (var type in Rebuildable)
                {
                    var layer = section.GetLayer(type);
                    if (layer == null || layer.GetType() != type) continue;
                    foreach (var sub in layer.subMeshes)
                    {
                        meshes++;
                        buffers += BufferBytes(sub);
                    }
                    // Things keeps a temporary list after gravship drawing. It must not
                    // retain disposed submeshes and their managed geometry arrays.
                    if (layer is SectionLayer_Things things)
                        ((List<LayerSubMesh>)FormerlyEnabled.GetValue(things)).Clear();
                    layer.Dispose();
                    ReleasedLayers.GetValue(layer, _ => new State()).Released = true;
                    layers++;
                }
            state.Released = true;
            Log.Message($"[SlopWorld] eco memory released layers={layers} meshes={meshes} " +
                $"geometryCapacityBytes={buffers}");
        }

        internal static void Restore(MapDrawer drawer)
        {
            if (Eco.Resting || !States.TryGetValue(drawer, out var state) || !state.Released) return;
            // Use vanilla's full regeneration to restore section bounds as well as meshes.
            // This also handles colony changes made while simulation was paused.
            drawer.RegenerateEverythingNow();
            drawer.WholeMapChanged(ulong.MaxValue);
            var sections = (Section[,])Sections.GetValue(drawer);
            if (sections != null)
                foreach (var section in sections)
                    foreach (var type in Rebuildable)
                    {
                        var layer = section?.GetLayer(type);
                        if (layer != null) ReleasedLayers.Remove(layer);
                    }
            state.Released = false;
            Log.Message("[SlopWorld] eco memory restored map geometry");
        }

        // Capacity measures retained array payload, not object headers or Unity/GPU memory.
        static long BufferBytes(LayerSubMesh sub) =>
            12L * (sub.verts.Capacity + (long)sub.uvs.Capacity + sub.uvsChannelTwo.Capacity +
                sub.normals.Capacity + sub.pollution.Capacity) +
            4L * (sub.tris.Capacity + (long)sub.colors.Capacity);

        internal static bool CanRegenerate(SectionLayer layer) =>
            !Eco.Resting || !ReleasedLayers.TryGetValue(layer, out var state) || !state.Released;

        // Direct regeneration (outside normal maintenance) must not refill released layers.
        [HarmonyPatch]
        static class Regeneration
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                var methods = new HashSet<MethodBase>();
                foreach (var type in Rebuildable)
                    methods.Add(AccessTools.Method(type, nameof(MapDrawLayer.Regenerate)));
                return methods;
            }

            static bool Prefix(SectionLayer __instance) => CanRegenerate(__instance);
        }

        [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
        static class PawnAtlases
        {
            static void Prefix(Map __instance)
            {
                if (!Eco.Resting || __instance.Disposed) return;
                Release(__instance.mapDrawer);
                var atlases = (List<PawnTextureAtlas>)Atlases.GetValue(null);
                if (atlases.Count == 0) return;
                long colorBytes = 0;
                foreach (var atlas in atlases)
                {
                    var texture = atlas.RawTexture;
                    if (texture != null) colorBytes += (long)texture.width * texture.height * 4;
                }
                int count = atlases.Count;
                GlobalTextureAtlasManager.FreeAllRuntimeAtlases();
                Log.Message($"[SlopWorld] eco memory released pawnAtlases={count} " +
                    $"atlasColorBytes={colorBytes}");
            }
        }
    }
}
