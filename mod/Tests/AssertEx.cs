using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class AssertEx
    {
        public static void True(bool value, string message)
        {
            Assert.That(value, Is.True, message);
        }

        public static void False(bool value, string message)
        {
            Assert.That(value, Is.False, message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            Assert.That(actual, Is.EqualTo(expected), message);
        }

        public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual,
                                       string message)
        {
            var left = expected.ToArray();
            var right = actual.ToArray();
            Assert.That(right, Is.EqualTo(left), message);
        }

        public static T Throws<T>(Action action, string message) where T : Exception
        {
            return Assert.Throws<T>(() => action(), message);
        }
    }
}
