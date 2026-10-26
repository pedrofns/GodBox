using System;
using System.Reflection;
using HarmonyLib;

namespace WorldfallAIChat
{
    /// <summary>
    /// Access to game members that are not public in the real Assembly-CSharp.
    /// Public API is used where it exists; reflection otherwise, so the mod compiles
    /// with or without NML's publicized assembly.
    /// </summary>
    internal static class GameAccess
    {
        static readonly FieldInfo MapStatsField = AccessTools.Field(typeof(MapBox), "map_stats");
        static readonly MethodInfo InputActiveMethod = AccessTools.Method(typeof(HotkeyLibrary), "isInputActive");

        /// <summary>Actor.data is internal; Actor.getData() is public.</summary>
        public static ActorData Data(Actor a)
        {
            try { return a?.getData() as ActorData; } catch { return null; }
        }

        public static MapStats MapStats()
        {
            try { return World.world == null ? null : MapStatsField?.GetValue(World.world) as MapStats; }
            catch { return null; }
        }

        /// <summary>True when a game text field (e.g. rename box) has focus. HotkeyLibrary.isInputActive is private.</summary>
        public static bool GameInputActive()
        {
            try
            {
                var lib = AssetManager.hotkey_library;
                if (lib == null || InputActiveMethod == null) return false;
                // While our chat is open, our own postfix makes this return true; callers check IsOpen first.
                return (bool)InputActiveMethod.Invoke(lib, null);
            }
            catch { return false; }
        }
    }
}
