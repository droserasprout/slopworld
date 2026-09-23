using System;
using System.Collections.Generic;

// Lifecycle doubles only. Production compilation checks these APIs against RimWorld.
// Unity native destruction and visual regeneration require an in-game toggle check.
namespace SlopWorld
{
    static class Eco { public static bool Resting; }
}

namespace Verse
{
    public class LayerSubMesh
    {
        public readonly List<int> verts = new List<int>(), tris = new List<int>(),
            colors = new List<int>(), pollution = new List<int>(), uvs = new List<int>(),
            uvsChannelTwo = new List<int>(), normals = new List<int>();
    }
    public class MapDrawLayer
    {
        public readonly List<LayerSubMesh> subMeshes = new List<LayerSubMesh>();
        public int Disposals, Regenerations;
        public void Dispose() { Disposals++; subMeshes.Clear(); }
        public virtual void Regenerate() { Regenerations++; subMeshes.Add(new LayerSubMesh()); }
    }
    public class SectionLayer : MapDrawLayer { }
    public class SectionLayer_Terrain : SectionLayer { }
    public class SectionLayer_Things : SectionLayer
    {
        public readonly List<LayerSubMesh> tmpFormerlyEnabled = new List<LayerSubMesh>();
    }
    public class SectionLayer_ThingsGeneral : SectionLayer_Things { }
    public class SectionLayer_FogOfWar : SectionLayer { }
    public class SectionLayer_LightingOverlay : SectionLayer { }
    public class SectionLayer_Snow : SectionLayer { }
    public class SectionLayer_Sand : SectionLayer { }
    public class Section
    {
        public readonly List<SectionLayer> Layers = new List<SectionLayer>();
        public SectionLayer GetLayer(Type type) => Layers.Find(layer => layer.GetType() == type);
    }
    public class MapDrawer
    {
        public Section[,] sections;
        public int Rebuilds;
        public bool FailRebuild;
        public ulong DirtyFlags;
        public void RegenerateEverythingNow()
        {
            Rebuilds++;
            if (FailRebuild) throw new InvalidOperationException("Rebuild failed");
            foreach (var section in sections)
                foreach (var layer in section.Layers) layer.Regenerate();
        }
        public void WholeMapChanged(ulong flags) { DirtyFlags |= flags; }
    }
    public class Map
    {
        public bool Disposed;
        public MapDrawer mapDrawer;
        public void MapUpdate() { }
    }
    public class PawnTextureAtlas
    {
        public UnityEngine.Texture2D RawTexture = new UnityEngine.Texture2D();
    }
    public static class GlobalTextureAtlasManager
    {
        public static readonly List<PawnTextureAtlas> pawnTextureAtlases = new List<PawnTextureAtlas>();
        public static void FreeAllRuntimeAtlases() { pawnTextureAtlases.Clear(); }
    }
}
