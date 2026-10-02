using System;
using System.Collections.Generic;
using System.Reflection;

namespace SlopWorld
{
    // Resolve inherited implementations before deduplication: a concrete worker can
    // inherit an override from an abstract intermediate class.
    internal static class EffectiveMethods
    {
        public static IEnumerable<MethodInfo> Find(Type baseType, IEnumerable<Type> concreteTypes, string name)
        {
            var seen = new HashSet<MethodInfo>();
            var method = Resolve(baseType, name);
            if (method != null && !method.IsAbstract && seen.Add(method)) yield return method;
            foreach (var type in concreteTypes)
            {
                method = Resolve(type, name);
                if (method != null && !method.IsAbstract && seen.Add(method)) yield return method;
            }
        }
        static MethodInfo Resolve(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null) return method;
            }
            return null;
        }
    }
}
