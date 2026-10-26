using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.8.1: God is ALWAYS divine (the demon form of 0.8.0 was removed). Holy beings are devoted, malefic beings defy him
    /// (same rule as before 0.8.0). A world saved by 0.8.0 may still have memory/&lt;world&gt;/_god_form.json saying "demonio":
    /// it is migrated to "divino" (logged) and otherwise ignored.
    /// </summary>
    public static class GodForm
    {
        public const string VoicePt = "voz do céu", VoiceEn = "voice from the sky";

        /// <summary>Hostile to God: malefic beings.</summary>
        public static bool Hostile(Actor npc) { try { return Alignment.IsMalefic(npc); } catch { return false; } }
        /// <summary>Devoted to God: holy beings.</summary>
        public static bool Devoted(Actor npc) { try { return Alignment.IsHoly(npc); } catch { return false; } }

        /// <summary>Old 0.8.0 save with the demon form: rewritten as divine and logged once per world.</summary>
        public static void MigrateLegacy(string world)
        {
            try
            {
                var p = Path.Combine(MemoryStore.Root ?? "", "memory", world ?? "", "_god_form.json");
                if (!File.Exists(p)) return;
                var o = JObject.Parse(File.ReadAllText(p, Encoding.UTF8));
                string f = (string)o["form"];
                if (f != "demonio") return;
                o["form"] = "divino";
                o.Remove("seen");
                File.WriteAllText(p, o.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
                Main.LogInfo("[GodBox] world '" + world + "': _god_form.json said 'demonio' (from 0.8.0); the demon form no longer exists, migrated to 'divino'. God is always divine.");
            }
            catch (Exception e) { Main.LogWarning("[GodBox] _god_form.json migration: " + e.Message); }
        }
    }
}
