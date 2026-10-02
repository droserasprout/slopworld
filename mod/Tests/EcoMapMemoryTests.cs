using System;
using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class EcoMapMemoryTests
    {
        sealed class CustomTerrain : SectionLayer_Terrain { }

        static MapDrawer Board(params SectionLayer[] layers)
        {
            var section = new Section();
            foreach (var layer in layers)
            {
                layer.subMeshes.Add(new LayerSubMesh());
                section.Layers.Add(layer);
            }
            return new MapDrawer { sections = new[,] { { section } } };
        }

        [TearDown]
        public void Reset() => Eco.Resting = false;

        [Test]
        public void ReleasePreservesSectionsAndUnknownLayersAndDoesNotRepeat()
        {
            var terrain = new SectionLayer_Terrain();
            var custom = new CustomTerrain();
            var things = new SectionLayer_ThingsGeneral();
            var board = Board(terrain, custom, things);
            var sections = board.sections;
            things.tmpFormerlyEnabled.Add(things.subMeshes[0]);
            Eco.Resting = true;
            Assert.That(EcoMapMemory.BeforeMeshUpdate(board), Is.False);
            Assert.That(board.sections, Is.SameAs(sections));
            board.WholeMapChanged(1); // Mark the board dirty while its retained geometry is released.
            Assert.That(terrain.subMeshes, Is.Empty);
            Assert.That(things.tmpFormerlyEnabled, Is.Empty);
            Assert.That(custom.subMeshes.Count, Is.EqualTo(1));
            Assert.That(EcoMapMemory.CanRegenerate(terrain), Is.False);
            Assert.That(EcoMapMemory.CanRegenerate(custom), Is.True);
            EcoMapMemory.BeforeMeshUpdate(board);
            Assert.That(terrain.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void RevealRestoresOnceAndAnotherEcoEntryReleasesAgain()
        {
            var layer = new SectionLayer_FogOfWar();
            var board = Board(layer);
            Eco.Resting = true;
            EcoMapMemory.BeforeMeshUpdate(board);
            EcoMapMemory.Restore(board);
            Assert.That(board.Rebuilds, Is.Zero);
            Eco.Resting = false; // Both Eco off and cutscene entry take this path.
            EcoMapMemory.Restore(board); // Draw can arrive before maintenance.
            Assert.That(layer.subMeshes.Count, Is.EqualTo(1));
            Assert.That(board.DirtyFlags, Is.EqualTo(ulong.MaxValue));
            Assert.That(EcoMapMemory.BeforeMeshUpdate(board), Is.True);
            Assert.That(board.Rebuilds, Is.EqualTo(1));
            Eco.Resting = true;
            EcoMapMemory.BeforeMeshUpdate(board);
            Assert.That(layer.Disposals, Is.EqualTo(2));
        }

        [Test]
        public void UninitializedMapIsRetriedAndOtherMapsHaveIndependentLifetimes()
        {
            var board = new MapDrawer();
            Eco.Resting = true;
            EcoMapMemory.BeforeMeshUpdate(board);
            board.sections = new Section[1, 1];
            EcoMapMemory.BeforeMeshUpdate(board);
            var layer = new SectionLayer_Snow();
            board.sections = Board(layer).sections;
            EcoMapMemory.BeforeMeshUpdate(board);
            var other = Board(new SectionLayer_Sand());
            EcoMapMemory.BeforeMeshUpdate(other);
            Assert.That(layer.Disposals, Is.EqualTo(1));
            Eco.Resting = false;
            EcoMapMemory.Restore(board);
            Assert.That(other.Rebuilds, Is.Zero);
            EcoMapMemory.Restore(other);
            Assert.That(other.Rebuilds, Is.EqualTo(1));
        }

        [Test]
        public void FailedRestoreKeepsPendingRebuild()
        {
            var board = Board(new SectionLayer_LightingOverlay());
            Eco.Resting = true;
            EcoMapMemory.BeforeMeshUpdate(board);
            Eco.Resting = false;
            board.FailRebuild = true;
            Assert.Throws<InvalidOperationException>(() => EcoMapMemory.Restore(board));
            board.FailRebuild = false;
            EcoMapMemory.Restore(board);
            Assert.That(board.Rebuilds, Is.EqualTo(2));
        }
    }
}
