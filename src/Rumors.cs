using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace WorldfallAIChat
{
    /// <summary>
    /// v7: lightweight shared knowledge between NPCs.
    ///  - Messages ("recados"): stored in the RECIPIENT's memory file as a note, once delivered.
    ///  - Rumors ("boatos"): short pt-BR lines about notable events (god blessed/cursed/killed/made prophet someone,
    ///    a new city leader or king), kept per city in memory/&lt;world&gt;/_rumors.json (capped), so the other units of
    ///    the same city can mention them.
    /// </summary>
    public static class Rumors
    {
        class Store
        {
            [JsonProperty("cities")] public Dictionary<string, List<MemNote>> Cities = new Dictionary<string, List<MemNote>>();
            [JsonProperty("leaders")] public Dictionary<string, string> Leaders = new Dictionary<string, string>();   // city/kingdom key -> last known ruler name
        }

        static readonly Dictionary<string, Store> Cache = new Dictionary<string, Store>();

        static string PathFor(string world) => Path.Combine(MemoryStore.Root, "memory", world, "_rumors.json");

        static Store Get()
        {
            string world = MemoryStore.CurrentWorldKey();
            if (Cache.TryGetValue(world, out var s)) return s;
            try { var p = PathFor(world); if (File.Exists(p)) s = JsonConvert.DeserializeObject<Store>(File.ReadAllText(p)); }
            catch (Exception e) { Main.LogWarning("[GodBox] rumors file unreadable: " + e.Message); }
            s ??= new Store();
            s.Cities ??= new Dictionary<string, List<MemNote>>();
            s.Leaders ??= new Dictionary<string, string>();
            Cache[world] = s;
            return s;
        }

        static void Save()
        {
            try
            {
                string world = MemoryStore.CurrentWorldKey();
                string p = PathFor(world);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonConvert.SerializeObject(Get(), Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not save rumors: " + e.Message); }
        }

        static string Key(City c) { try { return c == null ? null : "c" + c.getID(); } catch { return null; } }
        static string Name(Actor a) { try { return a.getName(); } catch { return "?"; } }

        /// <summary>Puts a delivered message into the recipient's memory (capped).</summary>
        public static void Deliver(Actor to, string text)
        {
            try
            {
                var mem = MemoryStore.Get(to);
                mem.Notes.Add(new MemNote { Year = Main.CurrentYear(), Text = text });
                int cap = Math.Max(2, Main.Cfg?.NotesPerUnit ?? 6);
                while (mem.Notes.Count > cap) mem.Notes.RemoveAt(0);
                MemoryStore.Save(mem);
            }
            catch (Exception e) { Main.LogWarning("[GodBox] message delivery failed: " + e.Message); }
        }

        public static void Add(City city, string text)
        {
            if (Main.Cfg == null || !Main.Cfg.RumorsEnabled) return;
            if (SafetyRules.HasForeignScript(text))
            {   // v0.8.0: never store a rumour with Chinese / other scripts
                string cut = SafetyRules.CutForeign(text);
                Main.LogInfo("[GodBox] rumour had a foreign script; " + (cut.Length > 0 ? "cut it" : "dropped it") + ".");
                text = cut;
            }
            string k = Key(city); if (k == null || string.IsNullOrWhiteSpace(text)) return;
            var s = Get();
            if (!s.Cities.TryGetValue(k, out var list)) s.Cities[k] = list = new List<MemNote>();
            if (list.Count > 0 && list[list.Count - 1].Text == text) return;
            list.Add(new MemNote { Year = Main.CurrentYear(), Text = text });
            int cap = Math.Max(2, Main.Cfg.RumorsPerCity);
            while (list.Count > cap) list.RemoveAt(0);
            Save();
            Main.LogInfo("[GodBox] rumor in " + SafeCity(city) + ": " + text);
        }

        static string SafeCity(City c) { try { return c.name; } catch { return "?"; } }

        /// <summary>Notices a new city leader / king since the last time someone from this city talked (no game hooks).</summary>
        public static void Observe(Actor npc)
        {
            if (Main.Cfg == null || !Main.Cfg.RumorsEnabled || npc == null) return;
            try
            {
                var city = npc.city; if (city == null) return;
                var s = Get(); bool dirty = false;
                void Check(string key, Actor ruler, string role)
                {
                    if (ruler == null) return;
                    string n = Name(ruler);
                    if (s.Leaders.TryGetValue(key, out var old) && old != n)
                        Add(city, n + " virou " + role + " (antes era " + old + ")");
                    if (old != n) { s.Leaders[key] = n; dirty = true; }
                }
                Check(Key(city) + "_leader", city.leader, "líder da cidade " + city.name);
                // kings: handled by TickKings (v0.7.8)
                if (dirty) Save();
            }
            catch { }
        }

        public static IEnumerable<string> CityTexts(City c)
        {
            string k = Key(c);
            if (k != null && Get().Cities.TryGetValue(k, out var list)) foreach (var n in list) yield return n.Text;
        }

        /// <summary>v0.7.8: a king died: forget the cached name so FACTS/rumours never keep a dead king.</summary>
        public static void KingDied(long kingdomId, string name)
        {
            var s = Get(); string key = "k" + kingdomId + "_king";
            if (s.Leaders.TryGetValue(key, out var old) && old == name) { s.Leaders[key] = "(morto) " + name; Save(); }
        }

        static float _nextKings;
        /// <summary>v0.7.8: every ~10 s, notice king changes in every kingdom ("X é o novo rei de K") without waiting for a chat.</summary>
        public static void TickKings()
        {
            if (Main.Cfg == null || !Main.Cfg.RumorsEnabled) return;
            float now = UnityEngine.Time.unscaledTime; if (now < _nextKings) return; _nextKings = now + 10f;
            try
            {
                var s = Get(); bool dirty = false;
                foreach (Kingdom k in World.world.kingdoms)
                {
                    if (k == null || !k.isAlive() || !k.isCiv()) continue;
                    var king = k.king; if (king == null || !king.isAlive()) continue;
                    string key = "k" + k.getID() + "_king", n = Name(king);
                    s.Leaders.TryGetValue(key, out var old);
                    if (old == n) continue;
                    s.Leaders[key] = n; dirty = true;
                    if (old == null) continue;   // first sighting: no news
                    string prev = old.StartsWith("(morto) ") ? old.Substring(8) : old;
                    string t = n + " é o novo " + (king.isSexFemale() ? "rainha" : "rei") + " de " + k.name + " (antes era " + prev + (old.StartsWith("(morto) ") ? ", que morreu" : "") + ")";
                    Main.LogInfo("[GodBox] KING: " + t);
                    foreach (City c in k.getCities()) Add(c, t);
                }
                if (dirty) Save();
            }
            catch { }
        }

        /// <summary>Turns a successful god/native action into a rumor for the NPC's city.</summary>
        public static void FromResult(WorldfallActions.Result res, string npcName, City city, Actor you, Actor npc = null)
        {
            string god = null; try { god = Gods.NameFor(npc); } catch { }   // v0.7.6: the god's proper name in that religion
            if (res == null || res.Outcome != "feito" && res.Outcome != "em_andamento" || city == null) return;
            string by = you == null ? "Deus" : Name(you);
            string first = res.Changes.Count > 0 ? System.Text.RegularExpressions.Regex.Replace(res.Changes[0], @"\s*\([a-z_]+\)", "") : "";
            string t = null;
            switch (res.Id)
            {
                case "bless": t = "Deus abençoou " + npcName; break;
                case "curse": t = "Deus amaldiçoou " + npcName; break;
                case "lift_curse": t = "Deus tirou a maldição de " + npcName; break;
                case "heal": t = "Deus curou " + npcName; break;
                case "kill": t = npcName + " foi morto(a) pela vontade de Deus"; break;
                case "add_trait": case "remove_trait": t = "por obra de Deus, " + npcName + " " + first; break;
                case "give_item": t = npcName + " recebeu um presente dos deuses: " + System.Text.RegularExpressions.Regex.Replace(first, @"^equipou\s+", ""); break;
                case "make_prophet": t = npcName + ", " + GodOrders082.ProphetWord(npc) + " de " + (god ?? "Deus") + ", foi " + (GodOrders082.Female(npc) ? "escolhida" : "escolhido") + " pela voz do céu: " + SafetyRules.StripNarrationNumbers(first); break;   // v0.8.2 (I6/I7)
                case "preach": t = npcName + (god != null ? ", " + GodOrders082.ProphetWord(npc) + " de " + god + "," : "") + " anda pregando a religião " + (god != null ? "de " + god : "de Deus"); break;
                case "rename": t = (you == null || Avatar.IsAvatar(you) ? "por ordem de Deus, " : "por ordem do rei, ") + System.Text.RegularExpressions.Regex.Replace(first, @"^por ordem de Deus,\s*", ""); break;   // v0.8.1 (B12): prose
                case "create_village": t = "o reino mandou fundar uma nova vila"; break;
            }
            if (t != null) Add(city, t.Length > 200 ? t.Substring(0, 200) : t);
        }

        /// <summary>Prompt block: delivered messages and city rumors (pt-BR), or null.</summary>
        public static string ForPrompt(Actor npc, UnitMemory mem)
        {
            var sb = new StringBuilder();
            try
            {
                if (mem?.Notes != null)
                    foreach (var n in mem.Notes.Skip(Math.Max(0, mem.Notes.Count - 4)))
                        sb.Append("- Recado recebido (ano ").Append(n.Year).Append("): ").Append(n.Text).Append('\n');
                if (Main.Cfg != null && Main.Cfg.RumorsEnabled)
                {
                    string k = Key(npc.city);
                    if (k != null && Get().Cities.TryGetValue(k, out var list))
                        foreach (var r in list.Skip(Math.Max(0, list.Count - 6)))
                            sb.Append("- Boato na cidade (ano ").Append(r.Year).Append("): ").Append(Deaths.SelfView(r.Text, npc)).Append('\n');
                }
            }
            catch { }
            return sb.Length > 0 ? sb.ToString() : null;
        }
    }
}
