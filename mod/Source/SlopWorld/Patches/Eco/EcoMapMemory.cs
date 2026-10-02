using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Retain sections and simulation grids because arrivals call MapMeshDirty during Eco rest.
    // Release only the listed base game layer types. Derived types can own additional buffers.
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
        sealed class DrawerReleaseState { public bool Released; }
        sealed class ReleasedLayer { }
        static readonly ConditionalWeakTable<MapDrawer, DrawerReleaseState> States =
            new ConditionalWeakTable<MapDrawer, DrawerReleaseState>();
        static readonly ConditionalWeakTable<SectionLayer, ReleasedLayer> ReleasedLayers =
            new ConditionalWeakTable<SectionLayer, ReleasedLayer>();
        static readonly ConditionalWeakTable<MapDrawer, DrawerReleaseState>.CreateValueCallback NewState =
            _ => new DrawerReleaseState();

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
            // Wait for all sections to initialize before releasing geometry.
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
                    // Clear the temporary gravship drawing list so it does not retain disposed submeshes or geometry arrays.
                    if (layer is SectionLayer_Things things)
                        ((List<LayerSubMesh>)FormerlyEnabled.GetValue(things)).Clear();
                    layer.Dispose();
                    ReleasedLayers.GetValue(layer, _ => new ReleasedLayer());
                    layers++;
                }
            state.Released = true;
            Log.Message($"[SlopWorld] eco memory released layers={layers} meshes={meshes} " +
                $"geometryCapacityBytes={buffers}");
        }

        internal static void Restore(MapDrawer drawer)
        {
            if (Eco.Resting || !States.TryGetValue(drawer, out var state) || !state.Released) return;
            // Regenerate all map geometry to restore section bounds and meshes.
            // Include colony changes made during Eco rest.
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

        // Estimate array payload from capacity. Exclude object headers, Unity allocations, and GPU memory.
        static long BufferBytes(LayerSubMesh sub) =>
            12L * (sub.verts.Capacity + (long)sub.uvs.Capacity + sub.uvsChannelTwo.Capacity +
                sub.normals.Capacity + sub.pollution.Capacity) +
            4L * (sub.tris.Capacity + (long)sub.colors.Capacity);

        internal static bool CanRegenerate(SectionLayer layer) =>
            !Eco.Resting || !ReleasedLayers.TryGetValue(layer, out _);

        // Prevent direct regeneration from allocating released layer geometry during Eco rest.
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
