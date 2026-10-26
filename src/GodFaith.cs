using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NeoModLoader.General;
using UnityEngine;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.9 "Religião de Deus": a religion template written by the player (GodBox tab button), saved per world in
    /// memory/&lt;world&gt;/_god_religion.json. REAL game integration: the template names the game's own Religion object
    /// (World.world.religions.newReligion / Religion.setName, as the make_prophet power already did), conversions use the
    /// game's Actor.tryToConvertToReligion / City.setReligion / Kingdom.setReligion. MOD-SIDE: the doctrine text
    /// (commandments, sins, rituals, symbol, stance, reward), prophets' missionary walks, the kingdom "ban" list, and the
    /// facts given to followers. The game has no doctrine/commandment data, so those live only in our file and prompts.
    /// </summary>
    public static class GodFaith
    {
        /// <summary>v0.8.0: name, god name, banner/colour (applied to the real religion) and stance. Rituals are the REAL religion
        /// traits, edited in WorldBox's own religion window. Old 0.7.9 fields (belief, commandments, sins, rituals, symbol, reward)
        /// are ignored on load (Newtonsoft skips unknown members) and no longer written.</summary>
        public class Template
        {
            [JsonProperty("name")] public string Name = "";
            [JsonProperty("god_name")] public string GodName = "";
            [JsonProperty("banner_background")] public int BannerBackground = -1;   // -1 = keep the game's
            [JsonProperty("banner_icon")] public int BannerIcon = -1;
            [JsonProperty("color_id")] public int ColorId = -1;
            // STANCE (Postura com outras religiões): kept per Pedro; to remove it, delete this field, StanceLine() and the window block marked STANCE.
            [JsonProperty("stance")] public string Stance = "tolerancia";   // tolerancia | conversao | guerra_santa
            [JsonProperty("banned_kingdoms")] public List<long> Banned = new List<long>();
            [JsonProperty("saved_year")] public int SavedYear;
        }

        static readonly Dictionary<string, Template> Cache = new Dictionary<string, Template>();
        static string PathFor(string world) => Path.Combine(MemoryStore.Root ?? "", "memory", world, "_god_religion.json");

        /// <summary>The saved template of the current world, or null.</summary>
        public static Template Current()
        {
            string w = MemoryStore.CurrentWorldKey();
            if (Cache.TryGetValue(w, out var t)) return t;
            try { var p = PathFor(w); if (File.Exists(p)) t = JsonConvert.DeserializeObject<Template>(File.ReadAllText(p, Encoding.UTF8)); }
            catch (Exception e) { Main.LogWarning("[GodBox] _god_religion.json unreadable: " + e.Message); }
            if (t != null) { t.Banned ??= new List<long>(); t.Stance ??= "tolerancia"; }
            Cache[w] = t; return t;
        }

        public static void Save(Template t)
        {
            string w = MemoryStore.CurrentWorldKey();
            t.SavedYear = Main.CurrentYear();
            Cache[w] = t;
            try
            {
                var p = PathFor(w); Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonConvert.SerializeObject(t, Formatting.Indented), Encoding.UTF8);
                Main.LogInfo("[GodBox] religion template saved: \"" + t.Name + "\" (postura " + t.Stance + ", estandarte " + t.BannerBackground + "/" + t.BannerIcon + ", cor " + t.ColorId + ").");
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not save the religion template: " + e.Message); }
            ApplyToGame(t);
        }

        public static void Delete()
        {
            string w = MemoryStore.CurrentWorldKey();
            Cache[w] = null;
            _missions.Clear();
            try { var p = PathFor(w); if (File.Exists(p)) File.Delete(p); } catch { }
            Main.LogInfo("[GodBox] religion template deleted (the game's religion itself stays; only the doctrine is gone).");
        }

        /// <summary>Applies the template to the real religion: setName, god name, banner ids, colour (updateColor + on_new_color).</summary>
        static void ApplyToGame(Template t)
        {
            var rel = GodPowers.CurrentReligion();
            if (rel == null) { Main.LogInfo("[GodBox] religion template: no religion of God in the game yet; it is founded with the first prophet (or by \"Editar rituais e costumes\")."); return; }
            ApplyTo(rel, t);
        }

        static void ApplyTo(Religion rel, Template t)
        {
            if (rel == null || t == null) return;
            try
            {
                if (!string.IsNullOrWhiteSpace(t.Name) && rel.name != t.Name.Trim())
                {
                    string old = rel.name;
                    rel.setName(t.Name.Trim());
                    var g = MemoryStore.God(); g.ReligionName = rel.name; MemoryStore.SaveGod();
                    Main.LogInfo("[GodBox] game religion renamed: " + old + " -> " + rel.name + ".");
                }
                if (!string.IsNullOrWhiteSpace(t.GodName)) Gods.SetName(rel, t.GodName.Trim(), "nome dado no modelo da Religião de Deus");
                var bl = AssetManager.religion_banners_library.getCurrentAsset();
                if (t.BannerBackground >= 0 && t.BannerBackground < bl.backgrounds.Count) rel.data.banner_background_id = t.BannerBackground;
                if (t.BannerIcon >= 0 && t.BannerIcon < bl.icons.Count) rel.data.banner_icon_id = t.BannerIcon;
                var cl = AssetManager.religion_colors_library;
                if (t.ColorId >= 0 && t.ColorId < cl.list.Count && rel.updateColor(cl.list[t.ColorId]))
                {
                    try { AssetManager.meta_customization_library.getAsset(MetaType.Religion).on_new_color?.Invoke(); } catch { }
                    Main.LogInfo("[GodBox] game religion colour set to #" + t.ColorId + ".");
                }
                Main.LogInfo("[GodBox] game religion " + rel.name + ": banner " + rel.data.banner_background_id + "/" + rel.data.banner_icon_id + ", colour " + rel.data.color_id + ", rituals: " + RitualIds(rel) + ".");
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not apply the template to the game religion: " + e.Message); }
        }

        /// <summary>v0.8.0: God's real religion; founded now if it does not exist yet (needed to open the game's own window).
        /// The game needs a founder unit: a sapient unit with a kingdom (holy beings first) founds it with the game's own
        /// ReligionManager.newReligion and becomes its first follower (NOT a prophet), so the empty religion is not removed.</summary>
        public static Religion EnsureReligion(out string why)
        {
            why = null;
            var rel = GodPowers.CurrentReligion();
            if (rel != null) return rel;
            Actor best = null; int bestScore = -1;
            try
            {
                foreach (Actor a in World.world.units)
                {
                    try
                    {
                        if (a == null || !a.isAlive() || a.kingdom == null || !a.kingdom.isCiv() || a.subspecies == null || !a.subspecies.has_advanced_memory) continue;
                        if (Malefic(a)) continue;   // never a being hostile to God's current form
                        int sc = (Alignment.IsHoly(a) ? 4 : 0) + (a.hasTrait("blessed") ? 2 : 0) + (a.city != null ? 1 : 0);
                        if (sc > bestScore) { best = a; bestScore = sc; }
                    }
                    catch { }
                }
            }
            catch { }
            if (best == null) { why = "não há nenhuma criatura com reino que possa fundá-la"; return null; }
            try
            {
                rel = World.world.religions.newReligion(best, true);
                var t = Current(); if (t != null && !string.IsNullOrWhiteSpace(t.Name)) rel.setName(t.Name.Trim());
                best.setReligion(rel); try { rel.countConversion(); } catch { }
                var g = MemoryStore.God(); g.ReligionId = rel.getID(); g.ReligionName = rel.name; MemoryStore.SaveGod();
                ApplyTo(rel, t);
                Main.LogInfo("[GodBox] God's religion founded from the window: " + rel.name + " (id " + rel.getID() + "), founder/first follower " + Name(best) + "; game-default rituals: " + RitualIds(rel) + ".");
                try { Rumors.Add(best.city, "uma nova fé nasceu: " + rel.name + ", a religião de Deus"); } catch { }
                return rel;
            }
            catch (Exception e) { why = e.Message; Main.LogWarning("[GodBox] could not found God's religion: " + e.Message); return null; }
        }

        /// <summary>Called by make_prophet after founding/adopting God's religion.</summary>
        public static void OnReligionReady(Religion rel) { var t = Current(); if (t != null) ApplyTo(rel, t); }

        // ---------------- real religion traits (rituais e costumes) ----------------
        public static string RitualIds(Religion rel)
        {
            try { var l = rel.getTraits().Select(x => x.id).ToList(); return l.Count == 0 ? "nenhum" : string.Join(", ", l); } catch { return "?"; }
        }

        /// <summary>The religion's REAL traits with the game's pt-BR name and description ("Nome: descrição").</summary>
        public static List<string> RitualsPt(Religion rel)
        {
            var list = new List<string>();
            try
            {
                foreach (var tr in rel.getTraits())
                {
                    if (tr == null || tr.id == null || tr.id.StartsWith("$")) continue;
                    string n = null, d = null;
                    try { n = GameLocalePt.Text(tr.getLocaleID()); } catch { }
                    try { d = GameLocalePt.Text(tr.getDescriptionID()); } catch { }
                    if (string.IsNullOrWhiteSpace(n)) try { n = tr.getTranslatedName(); } catch { }
                    if (string.IsNullOrWhiteSpace(n)) n = tr.id.Replace('_', ' ');
                    list.Add(n.Trim() + (string.IsNullOrWhiteSpace(d) ? "" : ": " + StripTags(d)));
                }
            }
            catch { }
            return list;
        }
        static string StripTags(string s) => Regex.Replace(s ?? "", "<[^>]+>", "").Replace("\n", " ").Trim();

        public static string StancePt(string s) => s == "conversao" ? "conversão pacífica" : s == "guerra_santa" ? "guerra santa" : "tolerância";

        static string Doctrine(Template t, Religion rel, Actor npc)
        {
            var sb = new StringBuilder();
            string god = !string.IsNullOrWhiteSpace(t.GodName) ? t.GodName : Gods.GodName() ?? "Deus";   // v0.8.2 (A)
            sb.Append("Religião " + (rel?.name ?? t.Name) + " (deus: " + god + ").");
            var r = rel != null ? RitualsPt(rel) : new List<string>();
            sb.Append(r.Count == 0 ? " Rituais e costumes: nenhum ainda." : " Rituais e costumes REAIS desta fé no jogo (EXATAMENTE estes " + r.Count + ", não existem outros): " + string.Join("; ", r) + ".");
            sb.Append(StanceLine(t));
            sb.Append(" Não existem mandamentos, pecados, recompensas ou promessas além disso: NUNCA invente.");
            return sb.ToString();
        }

        // STANCE: remove this method (and the field + window block) to drop the stance.
        static string StanceLine(Template t) => " Postura com outras religiões: " + StancePt(t.Stance) + (t.Stance == "guerra_santa" ? " (mas guerra só acontece se o próprio Deus ordenar e confirmar)" : "") + ".";

        /// <summary>FACTS block: only followers and prophets of God's religion know the doctrine; others get nothing here
        /// (they only hear rumours of preaching through the normal rumour system).</summary>
        public static string FactsFor(Actor npc)
        {
            try
            {
                var t = Current(); var rel = GodPowers.CurrentReligion();
                if (t == null || rel == null || npc == null || npc.religion != rel) return null;
                bool prophet = MemoryStore.God().Prophets.Contains(npc.getID());
                string d = Doctrine(t, rel, npc);
                return (prophet
                    ? "VOCÊ é PROFETA desta fé, escolhido por Deus. Pregue FIELMENTE: o nome da fé, o nome de Deus e os rituais reais abaixo; NUNCA invente mandamentos, rituais, recompensas ou promessas que não estejam aqui. "
                    : "Você SEGUE esta fé (foi convertido). Conhece a doutrina e pode citá-la; não invente nada além dela. ") + d + " " + ProphetsFact(npc);
            }
            catch { return null; }
        }

        /// <summary>v0.8.1 (B11): the prophet list is a hard fact from _god.json (living prophets only).</summary>
        public static List<Actor> LivingProphets()
        {
            var list = new List<Actor>();
            try
            {
                var ids = MemoryStore.God().Prophets; if (ids == null || ids.Count == 0) return list;
                var set = new HashSet<long>(ids);
                foreach (Actor a in World.world.units) { if (a == null || !a.isAlive()) continue; try { if (set.Contains(a.getID())) list.Add(a); } catch { } }
            }
            catch { }
            return list;
        }
        public static bool IsProphet(Actor a) { try { return a != null && MemoryStore.God().Prophets.Contains(a.getID()); } catch { return false; } }
        public static string ProphetsFact(Actor npc)
        {
            var ps = LivingProphets();
            if (ps.Count == 0) return "PROFETAS DE DEUS (fato do jogo): nenhum vivo agora. NUNCA invente um profeta.";
            var names = ps.Take(8).Select(p => { string n = "?"; try { n = p.getName(); } catch { } return p == npc ? n + " (você)" : n + (p.city != null ? " (" + p.city.name + ")" : ""); });
            return "PROFETAS DE DEUS (fato do jogo, lista COMPLETA): " + string.Join(", ", names) + ". Ninguém mais é profeta: NUNCA chame outra pessoa de profeta.";
        }

        /// <summary>v0.8.0 fix: which rule makes a unit a dark soul, or null. Species/kingdom rules come from Alignment
        /// (evil mage, necromancer, demon, undead/zombie, skeletons, evil-tagged kingdoms); trait rules are ONLY the real
        /// personal evil traits "evil" and "psychopath". NOT bloodlust (orcs are born with it: ActorAssetLibrary orc
        /// addTrait("bloodlust")) and NOT madness (a temporary, curable state).</summary>
        static string DarkRule(Actor a)
        {
            if (a == null) return null;
            try
            {
                if (Alignment.IsMalefic(a))
                {
                    bool evilT = false; try { evilT = a.hasTrait("evil"); } catch { }
                    string why = null; try { why = Alignment.Why(a); } catch { }
                    if (why == null || why.Contains("traço evil")) why = evilT ? null : "reino maléfico";
                    if (why == null) return "traço evil";
                    return "espécie/reino: " + why + " (" + (a.asset?.id ?? "?") + ")";
                }
            }
            catch { }
            try { if (a.hasTrait("evil")) return "traço evil"; } catch { }
            try { if (a.hasTrait("psychopath")) return "traço psychopath"; } catch { }
            return null;
        }

        static bool Malefic(Actor a) => Malefic(a, out _);
        static bool Malefic(Actor a, out string rule)
        {   // dark souls reject God's faith (God is always divine)
            string dark = DarkRule(a);
            rule = dark; return dark != null;
        }

        /// <summary>v0.8.0 fix: per-person chance a preaching visit converts them. Tuned so a visit converts roughly
        /// 10-30% of the eligible people (base 0.22, PreachCityShare 0.5 -> 0.22; modifiers +-; clamp 0.04..0.5).</summary>
        public static float ConvertChance(Actor u, Actor prophet) => ConvertChance(u, prophet, out _);
        /// <summary>v0.8.1 (B14/C2): + temple of the listener's city (+0.06; +0.06 more if God raised it), + the kingdom/city
        /// already follow God's religion (+0.08 / +0.05). why = short odds breakdown for the log.</summary>
        public static float ConvertChance(Actor u, Actor prophet, out string why)
        {
            why = "";
            float c0 = ConvertChanceBase(u, prophet);
            if (c0 <= 0f) { why = "maléfico"; return 0f; }
            float c = c0; var bits = new List<string>();
            try
            {
                var rel = GodPowers.CurrentReligion();
                var city = u.city;
                if (city != null && HasTemple(city)) { c += 0.06f; bits.Add("templo+0.06"); if (MemoryStore.God().GodTempleCities.Contains(city.getID())) { c += 0.06f; bits.Add("templo de Deus+0.06"); } }
                if (rel != null && u.kingdom != null && u.kingdom.getReligion() == rel) { c += 0.08f; bits.Add("reino da fé+0.08"); }
                if (rel != null && city != null && city.getReligion() == rel) { c += 0.05f; bits.Add("cidade da fé+0.05"); }
            }
            catch { }
            c = Mathf.Clamp(c, 0.04f, 0.65f);
            why = "base " + c0.ToString("0.00") + (bits.Count > 0 ? " " + string.Join(" ", bits) : "") + " = " + c.ToString("0.00");
            return c;
        }
        public static bool HasTemple(City c) { try { return c != null && c.hasBuildingType("type_temple", false); } catch { return false; } }
        public static Building TempleOf(City c) { try { return c?.getBuildingOfType("type_temple", true) ?? c?.getBuildingOfType("type_temple", false); } catch { return null; } }
        /// <summary>v0.8.2 (F6): a usable, finished temple (not a construction site).</summary>
        public static bool HasFinishedTemple(City c) { try { return c != null && c.hasBuildingType("type_temple", true); } catch { return false; } }
        /// <summary>v0.8.2 (F6): null = no temple; "pronto" = finished; "em obra" = only a construction site.</summary>
        public static string TempleState(City c) => !HasTemple(c) ? null : HasFinishedTemple(c) ? "pronto" : "em obra";

        /// <summary>v0.8.2 (H): the NPC's knowledge of the temple in its own city (state + direction from the centre).</summary>
        public static string TempleFact(City c)
        {
            if (c == null) return null;
            string cn; try { cn = c.name; } catch { return null; }
            string st = TempleState(c);
            if (st == null) return "Na sua cidade, " + cn + ", NÃO existe templo (nem pronto nem em construção).";
            string dir = null;
            try { var b = TempleOf(c); dir = Direction(c.getTile(), b?.current_tile); } catch { }
            return "Na sua cidade, " + cn + ", " + (st == "pronto" ? "existe um templo pronto" : "há um templo EM CONSTRUÇÃO (a obra ainda não terminou)") + (dir != null ? ", " + dir : "") + ".";
        }

        /// <summary>v0.8.2 (H): compass direction of a tile from the city's centre (map up = north).</summary>
        public static string Direction(WorldTile from, WorldTile to)
        {
            if (from == null || to == null) return null;
            float dx = to.x - from.x, dy = to.y - from.y;
            if (dx * dx + dy * dy < 16f) return "bem no centro da cidade";
            double ang = Math.Atan2(dy, dx) * 180.0 / Math.PI;   // 0 = east, 90 = north
            string[] names = { "a leste", "a nordeste", "ao norte", "a noroeste", "a oeste", "a sudoeste", "ao sul", "a sudeste" };
            int i = (int)Math.Round(((ang % 360) + 360) % 360 / 45.0) % 8;
            return names[i] + " do centro da cidade";
        }

        /// <summary>v0.8.2 (G): true when every living person of the city who CAN convert (advanced memory, not malefic,
        /// no 'true roots') already follows the religion -- nobody left to convert there.</summary>
        public static bool FullyFollows(City c, Religion rel, Actor except = null)
        {
            if (c == null || rel == null) return false;
            try
            {
                foreach (var u in c.getUnits())
                {
                    if (u == null || u == except || !u.isAlive() || u.religion == rel) continue;
                    bool mind = false; try { mind = u.hasSubspecies() && u.subspecies.has_advanced_memory; } catch { }
                    if (!mind || Malefic(u)) continue;
                    bool roots = false; try { roots = u.hasCulture() && !u.culture.isPossibleToConvertToOtherMeta(); } catch { }
                    if (roots) continue;
                    return false;
                }
                return true;
            }
            catch { return false; }
        }
        /// <summary>v0.8.2 (G): set by NextCity when cities were in reach but all of them already follow the faith.</summary>
        public static bool LastNoneLeft;
        /// <summary>v0.8.1 (C2): the prophet preaches at the city's temple when it has one (walks there first).</summary>
        public static WorldTile PreachTile(City c)
        {
            try { var b = TempleOf(c); if (b != null && b.isAlive() && b.current_tile != null) return b.current_tile; } catch { }
            return c?.getTile();
        }
        static float ConvertChanceBase(Actor u, Actor prophet)
        {
            if (Malefic(u)) return 0f;
            float share = Main.Cfg?.PreachCityShare ?? 0.5f;
            float c = Mathf.Clamp(share * 0.44f, 0.05f, 0.45f);
            string stance = Current()?.Stance ?? "tolerancia";
            if (stance == "conversao") c += 0.05f;
            try
            {
                if (u.hasTrait("blessed")) c += 0.15f;
                if (Alignment.IsHoly(u)) c += 0.1f;
                if (u.hasTrait("honest") || u.hasTrait("pacifist") || u.hasTrait("content")) c += 0.05f;
                if (u.hasTrait("strong_minded") || u.hasTrait("paranoid") || u.hasTrait("genius")) c -= 0.07f;
                if (u.hasReligion()) c -= 0.05f;   // already of another faith
                if (prophet != null && u.kingdom != null && prophet.kingdom != null && u.kingdom.isEnemy(prophet.kingdom)) c -= 0.08f;
            }
            catch { }
            return Mathf.Clamp(c, 0.04f, 0.5f);
        }

        // ------------------------------------------------------------------ missions (mod-side) ----
        class Mission { public Actor Npc; public City Target; public HashSet<long> Visited = new HashSet<long>(); public int Left; public float Next, Started; public bool Explicit; public int PathFails;
            // v0.8.0 (fix 7): leg timing counts only UNPAUSED time; progress = getting closer to the target
            public float LegTime, LastTick = -1f, StallTime, BestDist = -1f, LegDist = -1f; public int Resumes, Skips; }
        static readonly List<Mission> _missions = new List<Mission>();
        static readonly System.Random Rng = new System.Random();

        /// <summary>Starts (or redirects) a prophet's missionary walk. city == null: the nearest unvisited city.</summary>
        public static string StartMission(Actor npc, City city, bool explicitOrder, IEnumerable<long> skip = null)
        {
            var rel = GodPowers.CurrentReligion();
            if (rel == null || npc == null || npc.religion != rel) return "não segue a religião de Deus";
            _missions.RemoveAll(m => m.Npc == npc);
            var mi = new Mission { Npc = npc, Left = explicitOrder ? 1 : 4, Explicit = explicitOrder, Started = Time.unscaledTime, Next = Time.unscaledTime + 1f };
            if (skip != null) foreach (var id in skip) mi.Visited.Add(id);   // v0.8.2 (G): "outras cidades" never picks the home city again
            mi.Target = city ?? NextCity(mi);
            if (mi.Target == null) return LastNoneLeft ? "não resta ninguém para converter nas cidades por perto: todas já seguem a fé de Deus" : "não há cidade por perto para pregar";
            _missions.Add(mi);
            NativeActions.WalkTo(npc, PreachTile(mi.Target));
            Main.LogInfo("[GodBox] PROPHET " + Name(npc) + ": missionary walk to " + mi.Target.name + (explicitOrder ? " (ordem de Deus)" : " (por conta própria, até 4 cidades)") + ".");
            return null;
        }

        public static void StopMission(Actor npc) { int n = _missions.RemoveAll(m => m.Npc == npc); if (n > 0) Main.LogInfo("[GodBox] PROPHET " + Name(npc) + ": mission stopped."); }
        public static bool OnMission(Actor npc) { for (int i = 0; i < _missions.Count; i++) if (_missions[i].Npc == npc) return true; return false; }
        public static string MissionFact(Actor npc)
        {
            var m = _missions.FirstOrDefault(x => x.Npc == npc);
            return m == null ? null : "você está a caminho de " + m.Target.name + " para pregar a religião de Deus (AINDA NÃO CHEGOU)";
        }

        /// <summary>v0.8.2 (G): the nearest unvisited city that still has someone to convert; a city with a temple
        /// counts as closer (score = distance x 0.6). Cities whose people all follow the faith are skipped.</summary>
        static City NextCity(Mission m)
        {
            City best = null; float bd = float.MaxValue; int inReach = 0;
            var t = Current(); var rel = GodPowers.CurrentReligion();
            foreach (var c in World.world.cities)
            {
                try
                {
                    if (c == null || m.Visited.Contains(c.getID())) continue;
                    if (t != null && c.kingdom != null && t.Banned.Contains(c.kingdom.getID())) continue;
                    var tile = c.getTile(); if (tile == null || m.Npc.current_tile == null) continue;
                    float d = (tile.pos - m.Npc.current_tile.pos).magnitude;
                    if (d > 200f) continue;
                    inReach++;
                    if (rel != null && FullyFollows(c, rel, m.Npc)) continue;
                    float score = HasTemple(c) ? d * 0.6f : d;
                    if (score < bd) { bd = score; best = c; }
                }
                catch { }
            }
            LastNoneLeft = best == null && inReach > 0;
            if (LastNoneLeft) Main.LogInfo("[GodBox] PROPHET " + Name(m.Npc) + ": " + inReach + " city(ies) in reach, all already follow the faith (nobody left to convert).");
            return best;
        }

        static float _nextGiftSweep;
        /// <summary>v0.8.0: prophets from saved worlds get their gifts (blessed, chosen_one, super_health, immortal) when loaded:
        /// a light sweep every 30 s; units that already have all four are skipped.</summary>
        static void GiftSweep()
        {
            if (Time.unscaledTime < _nextGiftSweep) return;
            _nextGiftSweep = Time.unscaledTime + 30f;
            try
            {
                var ids = MemoryStore.God().Prophets;
                if (ids == null || ids.Count == 0) return;
                var set = new HashSet<long>(ids);
                foreach (Actor a in World.world.units)
                {
                    if (a == null || !a.isAlive()) continue;
                    long id; try { id = a.getID(); } catch { continue; }
                    if (!set.Contains(id)) continue;
                    if (GodPowers.ProphetTraitIds.All(t => { try { return a.hasTrait(t); } catch { return true; } })) continue;
                    GodPowers.ProphetGifts(a, "existing prophet, loaded");
                }
            }
            catch { }
        }

        public static void Tick()
        {
            GiftSweep();
            Deadlines.Tick();   // v0.8.1 (B13)
            if (_missions.Count == 0) return;
            bool paused = false; try { paused = Config.paused; } catch { }
            if (paused) return;
            float now = Time.unscaledTime;
            // v0.8.2-alpha: no list copy on the frames where no mission is due (each one is checked every 2 s)
            bool due = false; for (int i = 0; i < _missions.Count; i++) if (now >= _missions[i].Next) { due = true; break; }
            if (!due) return;
            foreach (var m in _missions.ToList())
            {
                if (now < m.Next) continue;
                m.Next = now + 2f;
                bool alive = false; try { alive = m.Npc != null && m.Npc.isAlive(); } catch { }
                if (!alive) { Main.LogInfo("[GodBox] PROPHET mission ended: the prophet died."); _missions.Remove(m); continue; }
                if (m.Target == null || !m.Target.isAlive())
                {
                    Main.LogInfo("[GodBox] PROPHET " + Name(m.Npc) + ": target city is gone; picking the next one.");
                    if (!SkipToNext(m, "target city destroyed")) _missions.Remove(m);
                    continue;
                }
                var tile = PreachTile(m.Target);
                bool arrived = false; float dist = -1f;
                try { dist = (tile.pos - m.Npc.current_tile.pos).magnitude; arrived = HasTemple(m.Target) ? dist < 6f : (m.Npc.city == m.Target && dist < 14f || dist < 6f); } catch { }
                // unpaused time only (Tick returns while paused; a long pause/alt-tab gap counts at most 5 s)
                float dt = m.LastTick < 0f ? 0f : Mathf.Min(5f, now - m.LastTick); m.LastTick = now;
                if (!arrived)
                {
                    m.LegTime += dt;
                    if (m.LegDist < 0f && dist >= 0f) m.LegDist = dist;
                    if (dist >= 0f && (m.BestDist < 0f || dist < m.BestDist - 0.5f)) { m.BestDist = dist; m.StallTime = 0f; } else m.StallTime += dt;
                    bool walking = NativeActions.WalkTo(m.Npc, tile);
                    if (!walking) m.PathFails++;
                    float legMax = Mathf.Max(600f, (m.LegDist > 0f ? m.LegDist : 100f) * 8f);
                    string why = m.PathFails > 5 ? "no path after " + m.PathFails + " tries"
                               : m.StallTime > 180f ? "no progress for " + (int)m.StallTime + " s (distance " + (int)Mathf.Max(0f, dist) + " tiles)"
                               : m.LegTime > legMax ? "the walk took " + (int)m.LegTime + " s of game time (limit " + (int)legMax + " s for " + (int)Mathf.Max(0f, m.LegDist) + " tiles)" : null;
                    if (why != null)
                    {
                        if (m.Resumes < 2 && m.PathFails <= 5)
                        {
                            m.Resumes++; m.StallTime = 0f; m.LegTime = 0f; m.BestDist = dist; m.LegDist = dist;
                            try { m.Npc.cancelAllBeh(); } catch { }
                            NativeActions.WalkTo(m.Npc, tile);
                            Main.LogInfo("[GodBox] PROPHET " + Name(m.Npc) + ": walk to " + m.Target.name + " stalled (" + why + "); re-pathing, resume " + m.Resumes + "/2.");
                        }
                        else
                        {
                            Main.LogInfo("[GodBox] PROPHET " + Name(m.Npc) + ": giving up on " + m.Target.name + " (" + why + (m.Resumes > 0 ? ", after " + m.Resumes + " resume(s)" : "") + ").");
                            m.Visited.Add(m.Target.getID());
                            if (!SkipToNext(m, why)) _missions.Remove(m);
                        }
                    }
                    continue;
                }
                PreachIn(m.Npc, m.Target);
                m.Visited.Add(m.Target.getID());
                if (--m.Left <= 0) { _missions.Remove(m); Main.LogInfo("[GodBox] PROPHET " + Name(m.Npc) + ": mission finished."); continue; }
                m.Target = NextCity(m); ResetLeg(m);
                if (m.Target == null) { _missions.Remove(m); Main.LogInfo("[GodBox] PROPHET " + Name(m.Npc) + ": mission ended (no other city left to visit)."); continue; }
                NativeActions.WalkTo(m.Npc, PreachTile(m.Target));
                Main.LogInfo("[GodBox] PROPHET " + Name(m.Npc) + ": next city " + m.Target.name + ".");
            }
        }

        static void ResetLeg(Mission m) { m.PathFails = 0; m.Resumes = 0; m.LegTime = 0f; m.StallTime = 0f; m.BestDist = -1f; m.LegDist = -1f; }
        /// <summary>v0.8.0 (fix 7): skip the unreachable city and walk to the next one (max 3 skips); false = mission over (logged).</summary>
        static bool SkipToNext(Mission m, string why)
        {
            if (++m.Skips > 3) { Main.LogInfo("[GodBox] PROPHET " + Name(m.Npc) + ": mission ended (3 cities skipped; last reason: " + why + ")."); return false; }
            if (m.Explicit && m.Left < 1) m.Left = 1;   // God's city could not be reached: preach in the next one instead (logged below)
            m.Target = NextCity(m); ResetLeg(m);
            if (m.Target == null) { Main.LogInfo("[GodBox] PROPHET " + Name(m.Npc) + ": mission ended (no other city to try after: " + why + ")."); return false; }
            NativeActions.WalkTo(m.Npc, PreachTile(m.Target));
            Main.LogInfo("[GodBox] PROPHET " + Name(m.Npc) + ": skipped to the next city " + m.Target.name + " (" + m.Skips + "/3 skips).");
            return true;
        }

        /// <summary>A prophet preaches in a city: real conversions (game API) + rumours + king's decision (mod-side ban).</summary>
        static void PreachIn(Actor prophet, City city)
        {
            var rel = GodPowers.CurrentReligion(); var t = Current();
            if (rel == null) return;
            int conv = 0, rej = 0, total = 0, eligible = 0, noMind = 0, roots = 0, failed = 0, already = 0, oddsLogged = 0; double sumP = 0;
            bool temple = HasTemple(city), godTemple = temple && MemoryStore.God().GodTempleCities.Contains(city.getID());
            var rejBy = new Dictionary<string, int>();
            foreach (var u in city.getUnits().ToList())
            {
                if (u == null || u == prophet || !u.isAlive()) continue;
                total++;
                if (u.religion == rel) { already++; continue; }
                bool mind = false; try { mind = u.hasSubspecies() && u.subspecies.has_advanced_memory; } catch { }
                if (!mind) { noMind++; continue; }   // the game itself cannot convert beings without advanced memory
                if (Malefic(u, out string rule)) { rej++; string key = rule ?? "?"; rejBy[key] = (rejBy.TryGetValue(key, out int n0) ? n0 : 0) + 1; continue; }
                eligible++;
                float p = ConvertChance(u, prophet, out string oddsWhy); sumP += p;
                if (oddsLogged++ < 3) Main.LogInfo("[GodBox] PREACH odds " + Name(u) + ": " + oddsWhy + ".");
                if (Rng.NextDouble() >= p) continue;
                bool trueRoots = false; try { trueRoots = u.hasCulture() && !u.culture.isPossibleToConvertToOtherMeta(); } catch { }
                if (trueRoots) { roots++; continue; }   // culture trait "true roots" forbids conversion in the game: respected
                bool ok = false; try { ok = u.tryToConvertToReligion(rel); } catch { }
                if (ok && u.religion != rel) { try { u.setReligion(rel); } catch { } }   // safety: make sure the religion really is set
                if (u.religion == rel) conv++; else failed++;
            }
            Main.LogInfo("[GodBox] PREACH " + city.name + ": " + total + " people, " + already + " already followers, " + noMind + " without advanced memory, "
                + eligible + " eligible (avg chance " + (eligible > 0 ? (sumP / eligible).ToString("0.00") : "-") + "), " + conv + " converted (religion verified)"
                + (roots > 0 ? ", " + roots + " blocked by culture 'true roots'" : "") + (failed > 0 ? ", " + failed + " game conversion failed" : "")
                + "; rejected " + rej + (rejBy.Count > 0 ? " by rule: " + string.Join(", ", rejBy.Select(kv => kv.Key + "=" + kv.Value)) : "") + ".");
            // v0.8.2 (G): followers and total counted the same way (living, prophet excluded): no more "20 de 19 moradores"
            int followers = 0; try { followers = city.getUnits().Count(x => x != null && x != prophet && x.isAlive() && x.religion == rel); } catch { }
            bool wasCity = false; try { wasCity = city.getReligion() == rel; } catch { }
            bool cityAdopts = !wasCity && conv > 0 && total > 0 && followers * 2 >= total;   // v0.8.1: only a REAL new majority adopts
            if (cityAdopts) { try { city.setReligion(rel); } catch { } try { cityAdopts = city.getReligion() == rel; } catch { } }
            try { rel.countConversion(); } catch { }
            var g = MemoryStore.God(); g.ConvertedTotal += conv; try { g.AddConversions(prophet.getID(), conv); } catch { } MemoryStore.SaveGod();
            Main.LogInfo("[GodBox] PREACH " + city.name + ": " + (temple ? (godTemple ? "pregou no templo erguido por Deus" : "pregou no templo da cidade") : "sem templo") + "; seguidores " + followers + "/" + total + (wasCity ? " (a cidade já seguia " + rel.name + ")" : cityAdopts ? " (maioria nova: a cidade adotou " + rel.name + ")" : " (sem maioria nova: a cidade NÃO adotou)") + ".");
            string pn = Name(prophet);
            Rumors.Add(city, "o profeta " + pn + " pregou a religião " + rel.name + " em " + city.name + (conv > 0 ? " e " + conv + " pessoa(s) se converteram" : ""));
            if (rej > 0) Rumors.Add(city, "almas malignas de " + city.name + " zombaram do profeta " + pn + " e rejeitaram a religião " + rel.name);
            string king = KingDecision(city, rel, t);
            Main.LogInfo("[GodBox] PROPHET " + pn + " preached in " + city.name + ": " + conv + " converted, " + rej + " rejected (" + "malefic" + "), " + followers + "/" + total + " followers" + (cityAdopts ? ", the city adopted " + rel.name : "") + (king != null ? "; " + king : "") + ".");
            ChatWindow.Toast("O profeta " + pn + " pregou " + (temple ? "no templo de " : "em ") + city.name + ": " + conv + " conversão(ões); " + followers + " de " + total + " moradores seguem " + rel.name + (cityAdopts ? "; a cidade adotou a fé" : wasCity ? "" : "; a cidade ainda não adotou a fé") + ".");
            Rumors.Deliver(prophet, "Você pregou a religião " + rel.name + (temple ? " no templo de " : " em ") + city.name + ": " + conv + " pessoa(s) se converteram; " + followers + " de " + total + " moradores seguem a fé" + (cityAdopts ? " e a cidade a adotou" : wasCity ? "" : ", a cidade ainda não a adotou") + (rej > 0 ? ", " + rej + " alma(s) maligna(s) rejeitaram" : "") + (king != null ? "; " + king : "") + ".");
        }

        static string Title(Actor a) { bool f = false; try { f = a.isSexFemale(); } catch { } return f ? "a rainha " : "o rei "; }

        /// <summary>v0.8.0 fix: only the REAL ruler of the city's kingdom decides (kingdom.king, isKing(), same kingdom);
        /// a city leader never does. Every roll and reason is logged. Ban (mod-side) only for: a malefic/hostile king,
        /// guerra santa with a king of another faith, or (conversão pacífica / tolerância) an unhappy king or a low roll.</summary>
        static string KingDecision(City city, Religion rel, Template t)
        {
            try
            {
                var k = city.kingdom;
                if (k == null || !k.isAlive() || !k.isCiv()) { Main.LogInfo("[GodBox] KING decision in " + city.name + ": no civ kingdom, skipped."); return null; }
                var king = k.king;
                bool real = false; try { real = king != null && king.isAlive() && king.isKing() && king.kingdom == k; } catch { }
                if (!real) { Main.LogInfo("[GodBox] KING decision in " + city.name + ": kingdom " + k.name + " has no living king (city leaders do not decide), skipped."); return null; }
                if (k.getReligion() == rel) return null;
                string stance = t?.Stance ?? "tolerancia";
                string kn = k.name, who = Title(king) + Name(king);
                bool hostile = Malefic(king, out string hrule);
                double adoptP = king.religion == rel ? 1.0 : hostile ? 0.0 : stance == "conversao" ? 0.35 : stance == "tolerancia" ? 0.15 : 0.1;
                double r1 = Rng.NextDouble();
                if (r1 < adoptP)
                {
                    bool kingOk = king.religion == rel;
                    if (!kingOk) { try { kingOk = king.tryToConvertToReligion(rel); } catch { } }
                    if (kingOk || king.religion == rel)
                    {
                        k.setReligion(rel);
                        Main.LogInfo("[GodBox] KING " + Name(king) + " of " + kn + " ADOPTED " + rel.name + " (stance " + stance + ", roll " + r1.ToString("0.00") + " < " + adoptP.ToString("0.00") + ").");
                        Rumors.Add(city, who + " de " + kn + " adotou a religião " + rel.name + " para o reino");
                        return who + " adotou a fé para o reino " + kn;
                    }
                }
                bool unhappy = false; try { unhappy = king.data.happiness < 0; } catch { }
                bool otherFaith = false; try { otherFaith = king.hasReligion() && king.religion != rel; } catch { }
                double r2 = Rng.NextDouble();
                string reason = null;
                if (hostile) reason = "rei hostil à fé (" + (hrule ?? "?") + ")";
                else if (stance == "guerra_santa" && otherFaith && r2 < 0.5) reason = "guerra santa e o rei segue outra fé (roll " + r2.ToString("0.00") + " < 0.50)";
                else if (stance != "guerra_santa" && unhappy && otherFaith && r2 < 0.25) reason = "rei infeliz e de outra fé (roll " + r2.ToString("0.00") + " < 0.25)";
                else if (stance != "guerra_santa" && r2 < 0.04) reason = "roll baixo (" + r2.ToString("0.00") + " < 0.04)";
                Main.LogInfo("[GodBox] KING " + Name(king) + " of " + kn + ": adopt roll " + r1.ToString("0.00") + " vs " + adoptP.ToString("0.00") + " (no), ban roll " + r2.ToString("0.00")
                    + " -> " + (reason != null ? "BAN: " + reason : "no ban") + " (stance " + stance + ", unhappy " + unhappy + ", other faith " + otherFaith + ").");
                if (reason != null && t != null && !t.Banned.Contains(k.getID()))
                {
                    t.Banned.Add(k.getID()); Save(t);
                    Rumors.Add(city, who + " de " + kn + " proibiu a religião " + rel.name + " no reino");
                    return who + " PROIBIU a fé no reino " + kn + " (proibição do GodBox: profetas não voltam lá por conta própria)";
                }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] king decision failed: " + e.Message); }
            return null;
        }

        // ------------------------------------------------------------------ chat orders ----
        static readonly Regex AppointRx = new Regex(@"\bfac[ao]\s+(?:de|do|da)\s+(.+?)\s+(?:o\s+|a\s+)?(?:meu|minha|seu|nosso)\s+profet[ao]\b", RegexOptions.CultureInvariant);
        static readonly Regex Appoint2Rx = new Regex(@"\b(?:torne|nomeie|escolha|ungi?a|consagre)\s+(.+?)\s+(?:como\s+)?(?:o\s+|a\s+)?(?:meu|minha|seu|nosso)\s+profet[ao]\b", RegexOptions.CultureInvariant);
        static readonly Regex SpreadRx = new Regex(@"\b(?:espalhe|leve|pregue|anuncie|divulgue)\s+(?:a\s+)?(?:minha|nossa|a)\s+(?:religiao|fe|palavra|doutrina)(?:\s+de\s+deus)?\s+(?:em|na|no|para|pra|ate|pela|pelo|por)\s+(.+?)\s*[.!]*$", RegexOptions.CultureInvariant);

        /// <summary>God voice: "faça de X meu profeta" -> appoint_prophet; "espalhe minha religião em C" -> spread_religion.</summary>
        public static WorldfallActions.Request Shortcut(string text, List<WorldfallActions.Available> avail)
        {
            string f = NativeActions.Fold(text ?? "").Trim();
            if (f.EndsWith("?")) return null;
            bool Has(string id) => avail.Any(a => a.Def.Id == id && a.Enabled);
            var m = AppointRx.Match(f); if (!m.Success) m = Appoint2Rx.Match(f);
            if (m.Success && Has("appoint_prophet") && !Regex.IsMatch(m.Groups[1].Value, @"^(voce|vc|ti|tu|ele|ela)$")) return new WorldfallActions.Request { Id = "appoint_prophet", Target = m.Groups[1].Value.Trim(), Confidence = 1f };
            m = SpreadRx.Match(f);
            if (m.Success && Has("spread_religion")) return new WorldfallActions.Request { Id = "spread_religion", Target = Regex.Replace(m.Groups[1].Value, @"^(?:a |o )?(?:cidade (?:de |do |da )?|(?:vila|aldeia) (?:de|do|da) )", "").Trim(), Confidence = 1f };
            return null;
        }

        public static void AppointProphet(Actor npc, string target, DialogueConfig cfg, WorldfallActions.Result res)
        {
            var who = NativeActions.FindUnit(target, npc, null, out int amb);
            if (who == null) { res.Reason = amb > 1 ? "há mais de uma pessoa chamada \"" + target + "\"" : "não existe ninguém chamado \"" + target + "\" neste mundo"; return; }
            var r = GodPowers.Execute(who, null, new WorldfallActions.Request { Id = "make_prophet", Target = "" }, cfg);
            res.Outcome = r.Outcome; res.Reason = r.Reason;
            foreach (var c in r.Changes) res.Changes.Add(Name(who) + ": " + c);
            if (r.Outcome == "feito") Rumors.Deliver(who, "Deus fez de você Seu profeta (a ordem foi dada a " + Name(npc) + ").");
            Main.LogInfo("[GodBox] appoint_prophet " + Name(who) + " -> " + r.Outcome + " " + (r.Reason ?? ""));
        }

        public static void Spread(Actor npc, string target, WorldfallActions.Result res)
        {
            var rel = GodPowers.CurrentReligion();
            if (rel == null) { res.Reason = "Deus ainda não tem religião no mundo (faça primeiro um profeta)"; return; }
            if (npc.religion != rel) { res.Reason = "não segue a religião de Deus (torne-o profeta primeiro)"; return; }
            // v0.8.2 (G): "espalhe por outras cidades / pelo mundo" = any other city, never the home city
            bool others = !string.IsNullOrWhiteSpace(target) && Regex.IsMatch(NativeActions.Fold(target), @"\b(?:outr[ao]s?|demais|tod[ao]s?\s+as?|o\s+mundo|mundo|terras|lugares|vilas|aldeias|cidades)\b");
            var skip = new List<long>();
            if (others) { target = null; try { if (npc.city != null) skip.Add(npc.city.getID()); } catch { } }
            City c = string.IsNullOrWhiteSpace(target) ? null : NativeActions.FindCity(target, npc);
            if (!string.IsNullOrWhiteSpace(target) && c == null) { res.Reason = "não existe cidade chamada \"" + target + "\""; return; }
            var t = Current();
            if (c != null && t != null && c.kingdom != null && t.Banned.Contains(c.kingdom.getID())) res.Changes.Add("aviso: o rei de " + c.kingdom.name + " proibiu a fé; o profeta vai mesmo assim por ordem de Deus");
            if (c != null && FullyFollows(c, rel, npc))
            {   // v0.8.2 (G): nobody left to convert there -> the nearest city that still has someone (temple cities preferred)
                res.Changes.Add("todos em " + c.name + " que podem se converter já seguem a fé; o profeta vai para outra cidade");
                skip.Add(c.getID()); c = null;
            }
            string err = StartMission(npc, c, c != null, skip);
            if (err != null) { res.Reason = err; if (LastNoneLeft) res.Outcome = "sem_efeito"; return; }
            var m = _missions.First(x => x.Npc == npc);
            res.Outcome = "em_andamento";
            res.Changes.Add(Name(npc) + " partiu para pregar a religião " + rel.name + " em " + m.Target.name + " (AINDA NÃO CHEGOU; prega quando chegar, com o mundo rodando)");
        }

        // ------------------------------------------------------------------ tab button ----
        public static void AddButton(PowersTab tab)
        {
            if (tab == null) return;
            LM.Add("pt", "god_religion", "Religião de Deus");
            LM.Add("pt", "god_religion_description", "Crie o modelo da sua religião: os profetas vão espalhá-la pelo mundo.");
            LM.Add("en", "god_religion", "God's Religion");
            LM.Add("en", "god_religion_description", "Write your religion's template: prophets will spread it.");
            LM.ApplyLocale();
            var icon = Avatar.GameIcon(() => "ui/Icons/iconReligion", "religion icon");
            var btn = NeoModLoader.General.PowerButtonCreator.CreateSimpleButton("god_religion", () => GodFaithWindow.Open(), icon);
            NeoModLoader.General.UI.Tab.PowersTabExtension.AddPowerButton(tab, "religion", btn);
            Main.LogInfo("[GodBox] Religião de Deus button added to the GodBox tab.");
        }

        static string Name(Actor a) { try { return a.getName(); } catch { return "?"; } }
    }

    /// <summary>The pt-BR template form (IMGUI, styled like the chat; pauses the world while open).</summary>
    public class GodFaithWindow : MonoBehaviour
    {
        public static bool IsOpen;
        public static Rect ScreenRect;
        static GodFaithWindow _inst;
        Rect _rect = new Rect(120, 60, 560, 600);
        Vector2 _scroll;
        GodFaith.Template _t;
        bool _confirmDelete, _firstTime;
        string _status;
        // v0.8.0: WorldBox's own religion window opened from here ("religion" + traits editor tab, or "religion_customize")
        bool _nativeWatch, _nativeSeen; float _nativeStart; string _nativeKind;
        List<string> _ritualsCache; float _ritualsAt;

        void Awake() { _inst = this; }

        public static void Open() => Open(null);

        static void Open(GodFaith.Template draft)
        {
            if (_inst == null) return;
            if (ChatWindow.IsOpen) { ChatWindow.Toast("Feche o chat antes de abrir a Religião de Deus."); return; }
            var cur = GodFaith.Current();
            _inst._firstTime = cur == null;
            var src = draft ?? cur;
            _inst._t = src == null ? new GodFaith.Template() : JsonConvert.DeserializeObject<GodFaith.Template>(JsonConvert.SerializeObject(src));
            if (string.IsNullOrWhiteSpace(_inst._t.GodName))
            {
                string god = null;
                try { god = Gods.GodName(); } catch { }   // v0.8.2 (A): never prefill a game religion's default god name
                _inst._t.GodName = god ?? "";
            }
            _inst._confirmDelete = false; _inst._status = null; _inst._ritualsCache = null;
            IsOpen = true;
            WorldPause.Begin(Main.Cfg);
            Main.LogInfo("[GodBox] Religião de Deus window opened" + (_inst._firstTime ? " (no template yet)" : "") + ".");
        }

        static void Close()
        {
            IsOpen = false;
            WorldPause.End();
        }

        // ---------------- native WorldBox religion window ----------------
        /// <summary>Saves the draft (name, god name), creates the in-game religion if needed, hides this window and opens
        /// WorldBox's own window: kind "traits" = window "religion" on its traits-editor tab; kind "banner" = "religion_customize".</summary>
        void OpenNative(string kind)
        {
            var t = _t;
            if (string.IsNullOrWhiteSpace(t.Name)) { _status = "Dê um nome à religião antes."; return; }
            GodFaith.Save(t);
            Religion rel = GodFaith.EnsureReligion(out string why);
            if (rel == null) { _status = "Não foi possível criar a religião no jogo: " + why; return; }
            Close();
            try
            {
                SelectedMetas.selected_religion = rel;
                string win = kind == "traits" ? "religion" : "religion_customize";
                ScrollWindow.showWindow(win);
                Main.LogInfo("[GodBox] opened WorldBox's own window \"" + win + "\" for the religion " + rel.name + " (id " + rel.getID() + ").");
                if (kind == "traits") StartCoroutine(SelectEditorTab());
                _nativeWatch = true; _nativeSeen = false; _nativeStart = Time.unscaledTime; _nativeKind = kind;
            }
            catch (Exception e)
            {
                Main.LogWarning("[GodBox] could not open the game's religion window: " + e.Message);
                Open(t); _status = "Não consegui abrir a janela do jogo (veja o log).";
            }
        }

        System.Collections.IEnumerator SelectEditorTab()
        {
            float until = Time.unscaledTime + 3f;
            yield return null;
            while (Time.unscaledTime < until && (ScrollWindow.isAnimationActive() || ScrollWindow.getCurrentWindow() == null)) yield return null;
            try
            {
                var w = ScrollWindow.getCurrentWindow();
                var ed = w != null ? w.GetComponentInChildren<ReligionTraitsEditor>(true) : null;
                var tab = ed != null ? ed.getEditorTab() : null;
                if (tab != null) { w.tabs.showTab(tab); Main.LogInfo("[GodBox] religion window: selected the traits editor tab \"" + tab.name + "\" (ReligionTraitsEditor)."); }
                else if (w != null) { w.tabs.showTab("Traits"); Main.LogInfo("[GodBox] religion window: traits editor not found; showed the \"Traits\" tab."); }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not select the religion traits editor tab: " + e.Message); }
        }

        void Update()
        {
            if (!_nativeWatch) return;
            bool active = false; try { active = ScrollWindow.isWindowActive(); } catch { }
            if (active) { _nativeSeen = true; return; }
            if (!_nativeSeen && Time.unscaledTime - _nativeStart < 3f) return;   // still opening
            _nativeWatch = false;
            var t = _t;
            try
            {
                var rel = GodPowers.CurrentReligion();
                if (rel != null)
                {
                    // the game's window is the source of truth now: keep its banner/colour/name in the template
                    t.BannerBackground = rel.data.banner_background_id; t.BannerIcon = rel.data.banner_icon_id; t.ColorId = rel.data.color_id;
                    if (!string.IsNullOrWhiteSpace(rel.name)) t.Name = rel.name;
                    GodFaith.Save(t);
                    Main.LogInfo("[GodBox] the game's " + (_nativeKind == "traits" ? "religion window" : "religion customize window") + " closed; real rituals now: " + GodFaith.RitualIds(rel) + ".");
                }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] re-reading the religion after the game's window: " + e.Message); }
            Open(t);
        }

        void OnGUI()
        {
            if (!IsOpen || Main.Cfg == null) return;
            GUI.depth = Main.Cfg.GuiDepth;
            var oldM = GUI.matrix; var oldSkin = GUI.skin;
            try
            {
                GUI.skin = null;
                float scale = Mathf.Clamp(Main.Cfg.UiScale, 0.5f, 3f);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                float sw = Screen.width / scale, sh = Screen.height / scale;
                _rect.width = Mathf.Min(560, sw - 10); _rect.height = Mathf.Min(600, sh - 10);
                _rect.x = Mathf.Clamp(_rect.x, 0, Mathf.Max(0, sw - _rect.width)); _rect.y = Mathf.Clamp(_rect.y, 0, Mathf.Max(0, sh - _rect.height));
                ScreenRect = new Rect(_rect.x * scale, _rect.y * scale, _rect.width * scale, _rect.height * scale);
                var e = Event.current;
                if (e.type == EventType.Repaint)
                {
                    GUI.skin.box.Draw(_rect, GUIContent.none, false, false, false, false);
                    GUI.skin.box.Draw(_rect, GUIContent.none, false, false, false, false);
                    GUI.skin.window.Draw(_rect, new GUIContent("GodBox · Religião de Deus"), false, false, false, false);
                }
                GUILayout.BeginArea(new Rect(_rect.x + 8, _rect.y + 24, _rect.width - 16, _rect.height - 32));
                try { Body(); } finally { GUILayout.EndArea(); }
                if ((e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel) && _rect.Contains(e.mousePosition)) e.Use();
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Close(); e.Use(); }
            }
            catch (Exception ex) { Main.LogWarning("[GodBox] religion window GUI error: " + ex.Message); }
            finally { GUI.matrix = oldM; GUI.skin = oldSkin; }
            WorldPause.Keep(Main.Cfg);
        }

        void Field(string label, ref string v)
        {
            GUILayout.Label(label);
            v = GUILayout.TextField(v ?? "");
        }

        static void DrawSprite(Rect r, Sprite sp, Color c)
        {
            if (sp == null || sp.texture == null) return;
            var tr = sp.textureRect; var tex = sp.texture;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            var old = GUI.color; GUI.color = c;
            GUI.DrawTextureWithTexCoords(r, tex, uv);
            GUI.color = old;
        }

        void Body()
        {
            var t = _t;
            Religion rel = null; try { rel = GodPowers.CurrentReligion(); } catch { }
            if (_firstTime)
                GUILayout.Label("Crie aqui a sua religião. Os profetas que você escolher vão pregá-la fielmente, andando de cidade em cidade " +
                                "com o mundo rodando. Os rituais e o estandarte são os do próprio WorldBox, editados na janela de religião do jogo.");
            _scroll = GUILayout.BeginScrollView(_scroll);
            Field("Nome da religião", ref t.Name);
            Field("Nome de Deus nesta religião", ref t.GodName);

            GUILayout.Space(6);
            GUILayout.Label("Rituais e costumes (traços reais da religião no jogo)");
            if (rel != null)
            {
                if (_ritualsCache == null || Time.unscaledTime - _ritualsAt > 2f) { _ritualsCache = GodFaith.RitualsPt(rel); _ritualsAt = Time.unscaledTime; }
                GUILayout.Label(_ritualsCache.Count == 0 ? "(nenhum ainda)" : "• " + string.Join("\n• ", _ritualsCache.Select(x => x.Length > 110 ? x.Substring(0, 107) + "..." : x)));
            }
            else GUILayout.Label("(a religião ainda não existe no jogo: o botão abaixo cria e abre)");
            if (GUILayout.Button("Editar rituais e costumes (janela de religião do WorldBox)")) { OpenNative("traits"); GUILayout.EndScrollView(); return; }

            GUILayout.Space(6);
            GUILayout.Label("Símbolo e cor (estandarte real da religião)");
            if (rel != null)
            {
                Rect pr = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                try
                {
                    var col = rel.getColor();
                    DrawSprite(pr, rel.getBackgroundSprite(), col.getColorMainSecond());
                    DrawSprite(pr, rel.getIconSprite(), col.getColorBanner());
                }
                catch { }
            }
            if (GUILayout.Button("Editar símbolo e cor (janela de personalização do WorldBox)")) { OpenNative("banner"); GUILayout.EndScrollView(); return; }

            // ---- STANCE (Postura com outras religiões): delete this block, Template.Stance and GodFaith.StanceLine to drop it ----
            GUILayout.Space(6);
            GUILayout.Label("Postura com outras religiões");
            if (GUILayout.Toggle(t.Stance == "tolerancia", "Tolerância")) t.Stance = "tolerancia";
            GUILayout.Label("   profetas só pregam, sem atrito");
            if (GUILayout.Toggle(t.Stance == "conversao", "Conversão pacífica")) t.Stance = "conversao";
            GUILayout.Label("   profetas convertem; reis podem adotar a fé");
            if (GUILayout.Toggle(t.Stance == "guerra_santa", "Guerra santa")) t.Stance = "guerra_santa";
            GUILayout.Label("   reis fiéis podem proibir outras fés e ficam mais dispostos a guerrear contra infiéis; guerra só com ordem de Deus confirmada");
            // ---- end STANCE ----
            GUILayout.EndScrollView();
            if (_status != null) GUILayout.Label(_status);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Salvar"))
            {
                if (string.IsNullOrWhiteSpace(t.Name)) _status = "Dê um nome à religião antes de salvar.";
                else { GodFaith.Save(t); ChatWindow.Toast("Religião \"" + t.Name.Trim() + "\" salva. Escolha profetas para espalhá-la."); Close(); }
            }
            if (GUILayout.Button("Cancelar")) Close();
            if (!_firstTime)
            {
                if (!_confirmDelete) { if (GUILayout.Button("Apagar")) _confirmDelete = true; }
                else
                {
                    GUILayout.Label("Apagar o modelo?");
                    if (GUILayout.Button("Sim, apagar")) { GodFaith.Delete(); ChatWindow.Toast("Modelo da religião apagado."); Close(); }
                    if (GUILayout.Button("Não")) _confirmDelete = false;
                }
            }
            GUILayout.EndHorizontal();
        }
    }
}
