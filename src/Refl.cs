using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.8.2-alpha: cached reflection lookups for the optional Worldfall integration. AccessTools.TypeByName scans
    /// every loaded assembly and AccessTools.Field/Property/Method walk the type each time; several of these run every
    /// frame while the chat is open (UnitHold) or on every map ping. Types that are not found are NOT cached (Worldfall
    /// may load after us); members of a found type are cached either way (a type's members never change).
    /// </summary>
    internal static class Refl
    {
        static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();
        static readonly Dictionary<string, MemberInfo> _members = new Dictionary<string, MemberInfo>();

        public static Type Type(string fullName)
        {
            if (_types.TryGetValue(fullName, out var t)) return t;
            t = AccessTools.TypeByName(fullName);
            if (t != null) _types[fullName] = t;
            return t;
        }

        static string Key(Type t, string kind, string name, Type[] args)
        {
            string k = t.AssemblyQualifiedName + "|" + kind + "|" + name;
            if (args != null) foreach (var a in args) k += "|" + a?.FullName;
            return k;
        }

        public static FieldInfo Field(Type t, string name)
        {
            if (t == null) return null;
            string k = Key(t, "F", name, null);
            if (_members.TryGetValue(k, out var m)) return m as FieldInfo;
            var f = AccessTools.Field(t, name); _members[k] = f; return f;
        }

        public static PropertyInfo Property(Type t, string name)
        {
            if (t == null) return null;
            string k = Key(t, "P", name, null);
            if (_members.TryGetValue(k, out var m)) return m as PropertyInfo;
            var p = AccessTools.Property(t, name); _members[k] = p; return p;
        }

        public static MethodInfo Method(Type t, string name, Type[] args = null)
        {
            if (t == null) return null;
            string k = Key(t, "M", name, args);
            if (_members.TryGetValue(k, out var m)) return m as MethodInfo;
            var mi = AccessTools.Method(t, name, args); _members[k] = mi; return mi;
        }
    }
}
