using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class EffectiveMethodsTests
    {
        class Worker { public virtual float GenerationChance() => 1f; }
        abstract class Intermediate : Worker { public override float GenerationChance() => 2f; }
        class First : Intermediate { }
        class Second : Intermediate { }
        class BaseInherited : Worker { }
        class OwnOverride : Intermediate { public override float GenerationChance() => 3f; }

        public static void FindsAbstractAncestorOverridesOnceAndKeepsConcreteOverrides()
        {
            var methods = EffectiveMethods.Find(typeof(Worker), new[]
            {
                typeof(First), typeof(Second), typeof(BaseInherited), typeof(OwnOverride), typeof(First),
            }, "GenerationChance").ToArray();
            Assert.That(methods.Select(m => m.DeclaringType), Is.EquivalentTo(new[]
            {
                typeof(Worker), typeof(Intermediate), typeof(OwnOverride),
            }));
            Assert.That(methods.Length, Is.EqualTo(3));
        }
    }
}
