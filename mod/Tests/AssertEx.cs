using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld.Tests
{
    static class AssertEx
    {
        public static void True(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        public static void False(bool value, string message)
        {
            if (value) throw new Exception(message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{message}: expected {Format(expected)}, got {Format(actual)}");
        }

        public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual,
                                       string message)
        {
            var left = expected.ToArray();
            var right = actual.ToArray();
            if (!left.SequenceEqual(right))
                throw new Exception($"{message}: expected [{string.Join(", ", left)}], " +
                                    $"got [{string.Join(", ", right)}]");
        }

        public static T Throws<T>(Action action, string message) where T : Exception
        {
            try
            {
                action();
            }
            catch (T exception)
            {
                return exception;
            }
            catch (Exception exception)
            {
                throw new Exception($"{message}: expected {typeof(T).Name}, " +
                                    $"got {exception.GetType().Name}");
            }

            throw new Exception($"{message}: expected {typeof(T).Name}");
        }

        static string Format<T>(T value) => value == null ? "<null>" : value.ToString();
    }
}
