using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class NativeTextScopeTests
    {
        enum Tag
        {
            None,
            Bold,
        }

        // Exercise Harmony's actual enum return and out-argument handoff. Native font
        // layout is deliberately absent; game assembly binding is checked separately.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static Tag Parse(string text, int index, out int end, out Color color, out float size)
        {
            end = index + 3;
            color = Color.white;
            size = 24f;
            return Tag.Bold;
        }

        sealed class StyleProbe : GUIStyle
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal Tag Layout(Action nested = null)
            {
                nested?.Invoke();
                return Parse("<b>", 0, out _, out _, out _);
            }
        }

        readonly Harmony _harmony = new Harmony("SlopWorld.Tests.NativeTextScope");

        [SetUp]
        public void Patch()
        {
            _harmony.Patch(
                AccessTools.Method(typeof(NativeTextScopeTests), nameof(Parse)),
                prefix: new HarmonyMethod(typeof(Patch_NativeTextTags), "Prefix")
            );
            _harmony.Patch(
                AccessTools.Method(typeof(StyleProbe), nameof(StyleProbe.Layout)),
                prefix: new HarmonyMethod(typeof(Patch_NativeTextStyle), "Prefix"),
                finalizer: new HarmonyMethod(typeof(Patch_NativeTextStyle), "Finalizer")
            );
        }

        [TearDown]
        public void Unpatch() => _harmony.UnpatchAll(_harmony.Id);

        [Test]
        public void LiteralParsingPreservesSourceIndicesAndReturnsNativeNoTagResult()
        {
            const string source = "😀<b>value</b>";
            using (new NativeTextScope(false))
            {
                Assert.That(
                    Parse(source, 2, out int end, out var color, out float size),
                    Is.EqualTo(Tag.None)
                );
                Assert.That(end, Is.EqualTo(2));
                Assert.That(color, Is.EqualTo(default(Color)));
                Assert.That(size, Is.Zero);
            }
            Assert.That(Parse(source, 2, out _, out _, out _), Is.EqualTo(Tag.Bold));
        }

        [TestCase(false, Tag.None)]
        [TestCase(true, Tag.Bold)]
        public void StylePolicyAppliesDuringLayoutAndRestoresAfterward(
            bool richText,
            object expected
        )
        {
            Assert.That(new StyleProbe { richText = richText }.Layout(), Is.EqualTo(expected));
            Assert.That(NativeTextScope.Literal, Is.False);
        }

        [Test]
        public void NestedRichLayoutAndExceptionsRestoreTheOuterPolicy()
        {
            var literal = new StyleProbe { richText = false };
            var rich = new StyleProbe { richText = true };
            Assert.That(
                literal.Layout(() =>
                {
                    Assert.That(NativeTextScope.Literal, Is.True);
                    Assert.That(rich.Layout(), Is.EqualTo(Tag.Bold));
                    Assert.That(NativeTextScope.Literal, Is.True);
                    Assert.Throws<InvalidOperationException>(() =>
                        rich.Layout(() => throw new InvalidOperationException())
                    );
                    Assert.That(NativeTextScope.Literal, Is.True);
                }),
                Is.EqualTo(Tag.None)
            );
            Assert.That(NativeTextScope.Literal, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                literal.Layout(() => throw new InvalidOperationException())
            );
            Assert.That(NativeTextScope.Literal, Is.False);
        }

        [Test]
        public void LiteralPolicyDoesNotLeakToAnotherThread()
        {
            using (new NativeTextScope(false))
            {
                Assert.That(
                    Task.Run(() => Parse("<b>", 0, out _, out _, out _)).GetAwaiter().GetResult(),
                    Is.EqualTo(Tag.Bold)
                );
                Assert.That(Parse("<b>", 0, out _, out _, out _), Is.EqualTo(Tag.None));
            }
        }
    }
}
