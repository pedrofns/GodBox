using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.6 (D1): death chronicle. A Harmony prefix on Actor.die (runs before the game clears attackedBy) records the
    /// deaths of NOTABLE units only: anyone with a memory file, kings, city leaders, army captains, prophets, lovers /
    /// parents / children of units the player talked to, and anyone killed by a unit the player talked to.
    /// Stored per world in memory/&lt;world&gt;/_deaths.json (capped). Feeds rumors, the name lookup and FACTOS.
    /// </summary>
    public static class Deaths
    {
        public class Rec
        {
            [JsonProperty("id")] public long Id;
            [JsonProperty("name")] public string Name;
            [JsonProperty("species")] public string Species;
            [JsonProperty("age")] public int Age;
            [JsonProperty("role")] public string Role;
            [JsonProperty("city")] public string City;
            [JsonProperty("city_id")] public long CityId = -1;
            [JsonProperty("kingdom")] public string Kingdom;
            [JsonProperty("kingdom_id")] public long KingdomId = -1;
            [JsonProperty("year")] public int Year;
            [JsonProperty("cause")] public string Cause;
            [JsonProperty("killer")] public string Killer;
            [JsonProperty("killer_id")] public long KillerId = -1;
            [JsonProperty("killer_species")] public string KillerSpecies;
            [JsonProperty("killer_kingdom")] public string KillerKingdom;
            [JsonProperty("mod_cause")] public string ModCause;
            [JsonProperty("x")] public int X;
            [JsonProperty("y")] public int Y;
        }
        class Store { [JsonProperty("deaths")] public List<Rec> List = new List<Rec>(); }

        static readonly Dictionary<string, Store> Cache = new Dictionary<string, Store>();
        static readonly Dictionary<string, HashSet<long>> Talked = new Dictionary<string, HashSet<long>>();
        static readonly Dictionary<long, (string why, float at)> ModMarks = new Dictionary<long, (string, float)>();
        const int Cap = 400;

        public static void Apply(Harmony h)
        {
            try
            {
                var m = AccessTools.Method(typeof(Actor), "die", new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) });
                if (m == null) { Main.LogWarning("[GodBox] Actor.die not found; death chronicle off."); return; }
                h.Patch(m, prefix: new HarmonyMethod(typeof(Deaths), nameof(DiePrefix)));
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not patch Actor.die: " + e.Message); }
        }

        /// <summary>Call right before a mod action can kill 'a' (god smite, ordered attack).</summary>
        public static void MarkModCause(Actor a, string why)
        {
            try { if (a != null) ModMarks[a.getID()] = (why, UnityEngine.Time.unscaledTime); } catch { }
        }

        static string Path_(string world) => Path.Combine(MemoryStore.Root, "memory", world, "_deaths.json");

        static Store Get()
        {
            string world = MemoryStore.CurrentWorldKey();
            if (Cache.TryGetValue(world, out var s)) return s;
            try { var p = Path_(world); if (File.Exists(p)) s = JsonConvert.DeserializeObject<Store>(File.ReadAllText(p)); } catch (Exception e) { Main.LogWarning("[GodBox] deaths file unreadable: " + e.Message); }
            s ??= new Store(); s.List ??= new List<Rec>();
            Cache[world] = s; return s;
        }

        static void Save()
        {
            try
            {
                string p = Path_(MemoryStore.CurrentWorldKey());
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonConvert.SerializeObject(Get(), Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not save deaths: " + e.Message); }
        }

        /// <summary>Ids of units that have a memory file in this world (listed once, then kept up to date).</summary>
        static HashSet<long> TalkedSet()
        {
            string world = MemoryStore.CurrentWorldKey();
            if (Talked.TryGetValue(world, out var set)) return set;
            set = new HashSet<long>();
            try
            {
                string dir = Path.Combine(MemoryStore.Root, "memory", world);
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                        if (long.TryParse(Path.GetFileNameWithoutExtension(f), out long id)) set.Add(id);
            }
            catch { }
            Talked[world] = set; return set;
        }
        public static void NoteTalked(long id) { try { TalkedSet().Add(id); } catch { } }

        static bool Prophet(long id) { try { return MemoryStore.God().Prophets.Contains(id); } catch { return false; } }

        static void DiePrefix(Actor __instance, bool pDestroy, AttackType pType, bool pCountDeath)
        {
            try
            {
                var a = __instance;
                if (a == null || !a.isAlive() || (pType == AttackType.None && !pCountDeath)) return;   // removals, not deaths
                if (Avatar.IsAvatar(a) || Avatar.IsAvatarId(a.getID())) return;   // v0.7.9: the Avatar of God never enters the chronicle
                try { if (a.data != null && a.data.hasFlag("wfaic_avatar")) return; } catch { }
                if (Main.Cfg != null && !Main.Cfg.DeathChronicle) return;
                long id = a.getID();
                var talked = TalkedSet();
                Actor killer = null;
                try { if (a.attackedBy != null && !a.attackedBy.isRekt() && a.attackedBy.isActor() && a.attackedBy.a != a) killer = a.attackedBy.a; } catch { }
                if (pType == AttackType.Age || pType == AttackType.Starvation || pType == AttackType.Plague || pType == AttackType.Drowning) killer = null;
                long kid = -1; try { kid = killer?.getID() ?? -1; } catch { }
                string role = null;
                try
                {
                    if (a.isKing()) role = (a.isSexFemale() ? "rainha de " : "rei de ") + a.kingdom?.name;
                    else if (a.isCityLeader()) role = "líder de " + a.city?.name;
                    else if (a.is_army_captain) role = "capitão do exército";
                }
                catch { }
                if (Prophet(id)) role = (role == null ? "" : role + ", ") + "profeta";
                bool notable = role != null || talked.Contains(id) || (kid >= 0 && talked.Contains(kid)) || ModMarks.ContainsKey(id);
                if (!notable)
                {
                    try
                    {
                        if (a.lover != null && talked.Contains(a.lover.getID())) notable = true;
                        else if (talked.Contains(a.data.parent_id_1) || talked.Contains(a.data.parent_id_2)) notable = true;
                        else foreach (var c in a.getChildren()) if (talked.Contains(c.getID())) { notable = true; break; }
                    }
                    catch { }
                }
                if (!notable) return;
                Record(a, id, role, pType, killer, kid);
            }
            catch (Exception e) { Main.LogWarning("[GodBox] death record failed: " + e.Message); }
        }

        public static string CausePt(AttackType t, bool hasKiller)
        {
            switch (t)
            {
                case AttackType.Age: return "velhice";
                case AttackType.Starvation: return "fome";
                case AttackType.Plague: return "doença (peste)";
                case AttackType.Infection: return "doença (infecção)";
                case AttackType.Tumor: return "doença (tumor)";
                case AttackType.AshFever: return "doença (febre das cinzas)";
                case AttackType.Drowning: case AttackType.Water: return "afogado(a)";
                case AttackType.Fire: return "fogo";
                case AttackType.Acid: return "ácido";
                case AttackType.Explosion: return "explosão";
                case AttackType.Divine: return "poder divino";
                case AttackType.Weapon: return "em combate";
                case AttackType.Eaten: return "devorado(a)";
                case AttackType.Poison: return "veneno";
                case AttackType.Gravity: return "queda";
                case AttackType.Metamorphosis: return "metamorfose";
                default: return hasKiller ? "em combate" : "causa desconhecida";
            }
        }

        static void Record(Actor a, long id, string role, AttackType type, Actor killer, long kid)
        {
            var r = new Rec { Id = id, Year = Main.CurrentYear(), Role = role };
            try { r.Name = a.getName(); } catch { r.Name = "?"; }
            try { r.Species = FactsBuilder.Species(a); } catch { }
            try { r.Age = a.getAge(); } catch { }
            try { if (a.city != null) { r.City = a.city.name; r.CityId = a.city.getID(); } } catch { }
            try { if (a.kingdom != null && a.kingdom.isCiv()) { r.Kingdom = a.kingdom.name; r.KingdomId = a.kingdom.getID(); } } catch { }
            try { r.X = a.current_tile.x; r.Y = a.current_tile.y; } catch { }
            r.Cause = CausePt(type, killer != null);
            if (killer != null)
            {
                r.KillerId = kid;
                try { r.Killer = killer.getName(); } catch { }
                try { r.KillerSpecies = FactsBuilder.Species(killer); } catch { }
                try { r.KillerKingdom = killer.kingdom != null && killer.kingdom.isCiv() ? killer.kingdom.name : null; } catch { }
            }
            if (ModMarks.TryGetValue(id, out var mk))
            {
                if (UnityEngine.Time.unscaledTime - mk.at < 300f) r.ModCause = mk.why;
                ModMarks.Remove(id);
            }
            var s = Get();
            s.List.RemoveAll(x => x.Id == id);
            s.List.Add(r);
            while (s.List.Count > Cap) s.List.RemoveAt(0);
            Save();
            string text = Sentence(r, true);
            Main.LogInfo("[GodBox] DEATH: " + text + (r.ModCause != null ? " [ação do mod: " + r.ModCause + "]" : ""));
            try { Rumors.Add(a.city, text); } catch { }
            try { if (r.Role != null && r.Role.StartsWith("r") && r.KingdomId >= 0) Rumors.KingDied(r.KingdomId, r.Name); } catch { }
            try { if (killer != null && killer.city != null && killer.city != a.city && r.Role != null) Rumors.Add(killer.city, text); } catch { }
        }

        /// <summary>"Pedro, o profeta, foi morto por Raar (orc de Neen) no ano 50" / "Lia morreu de velhice no ano 12".</summary>
        public static string Sentence(Rec r, bool withKiller)
        {
            string who = r.Name + (r.Role != null ? " (" + r.Role + ")" : "");
            string yr = " no ano " + r.Year;
            if (withKiller && r.Killer != null)
                return who + " foi morto(a) por " + r.Killer + (r.KillerSpecies != null || r.KillerKingdom != null ? " (" + string.Join(", ", new[] { r.KillerSpecies, r.KillerKingdom != null ? "de " + r.KillerKingdom : null }.Where(x => x != null)) + ")" : "") + (r.Cause != "em combate" ? ", " + r.Cause : "") + yr;
            if (r.Cause == "poder divino") return who + " foi morto(a) pelo poder dos deuses" + yr;
            if (r.Cause == "afogado(a)" || r.Cause == "devorado(a)") return who + " morreu " + r.Cause + yr;
            if (r.Cause == "em combate") return who + " morreu em combate" + yr;
            if (r.Cause == "causa desconhecida") return who + " morreu" + yr + " (causa desconhecida)";
            return who + " morreu de " + r.Cause + yr;
        }

        public static IEnumerable<Rec> All() { try { return Get().List; } catch { return Enumerable.Empty<Rec>(); } }

        /// <summary>Dead people matching a name query (fuzzy), most recent first.</summary>
        public static List<Rec> Find(string q, int min = 70)
        {
            var res = new List<Rec>();
            if (string.IsNullOrWhiteSpace(q)) return res;
            foreach (var r in All()) if (r.Name != null && NameLookup.Score(q, NativeActions.Fold(r.Name)) >= min) res.Add(r);
            res.Sort((x, y) => y.Year.CompareTo(x.Year));
            return res;
        }

        /// <summary>How the NPC knows about one death (exact if nearby/same city/king or it was the killer; else a rumor).</summary>
        public static string Describe(Rec r, Actor npc, bool pt)
        {
            long me = -1; try { me = npc.getID(); } catch { }
            if (r.KillerId >= 0 && r.KillerId == me)
                return "VOCÊ MESMO(A) matou " + r.Name + " no ano " + r.Year + " (" + r.Cause + "). Você sabe disso. " + (Liar(npc)
                    ? "Você tem traço enganador/maligno: pode mentir ou desconversar se quiser."
                    : "Você NÃO é mentiroso(a): se perguntarem, admita (pode justificar).");
            bool exact = false;
            try
            {
                if (npc.isKing() || npc.isCityLeader()) exact = r.KingdomId >= 0 && npc.kingdom != null && npc.kingdom.getID() == r.KingdomId || npc.isKing();
                if (!exact && npc.city != null && npc.city.getID() == r.CityId) exact = true;
                if (!exact && npc.current_tile != null) { float dx = npc.current_tile.x - r.X, dy = npc.current_tile.y - r.Y; exact = dx * dx + dy * dy <= 60f * 60f; }
            }
            catch { }
            if (exact) return r.Name + " ESTÁ MORTO(A): " + Sentence(r, true) + (r.Age > 0 ? " (tinha " + r.Age + " anos)" : "") + (r.City != null ? "; era de " + r.City : "");
            return "ouvi dizer (boato, sem certeza) que " + r.Name + " morreu" + (r.Year > 0 ? " por volta do ano " + r.Year : "") + (r.Killer != null ? ", talvez pelas mãos de " + r.Killer : r.Cause != "causa desconhecida" ? " (" + r.Cause + ")" : "");
        }

        static bool Liar(Actor a) { try { return a.hasTrait("deceitful") || a.hasTrait("evil"); } catch { return false; } }

        /// <summary>v0.7.8: the NPC's own name as killer -> "você" (the model spoke of itself in the third person).</summary>
        public static string SelfView(string line, Actor npc)
        {
            if (string.IsNullOrEmpty(line) || npc == null) return line;
            string n = null; try { n = npc.getName(); } catch { }
            if (string.IsNullOrWhiteSpace(n)) return line;
            string e = System.Text.RegularExpressions.Regex.Escape(n);
            var o = System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            line = System.Text.RegularExpressions.Regex.Replace(line, @"\bpor " + e + @"\b(\s*\([^)]*\))?", "por VOCÊ", o);
            line = System.Text.RegularExpressions.Regex.Replace(line, @"\bpelas m[aã]os de " + e + @"\b", "pelas SUAS mãos", o);
            return line;
        }

        static bool Notable(Rec r) => r.Role != null;

        /// <summary>v0.7.9: does the chronicle list this NPC as the killer of someone with this name?
        /// null = nobody with that name is in the chronicle (unknown), true/false otherwise.</summary>
        public static bool? KilledBy(Actor npc, string victim)
        {
            try
            {
                if (npc == null || string.IsNullOrWhiteSpace(victim)) return null;
                string v = NativeActions.Fold(victim).Trim();
                var recs = All().Where(r => r.Name != null && NativeActions.Fold(r.Name) == v).ToList();
                if (recs.Count == 0) return null;
                long me = npc.getID();
                return recs.Any(r => r.KillerId == me);
            }
            catch { return null; }
        }
        /// <summary>v0.7.9: who killed this person according to the chronicle (for the code fallback).</summary>
        public static string KillLine(string victim)
        {
            try
            {
                string v = NativeActions.Fold(victim ?? "").Trim();
                var r = All().LastOrDefault(x => x.Name != null && NativeActions.Fold(x.Name) == v);
                return r == null ? null : Sentence(r, true);
            }
            catch { return null; }
        }

        /// <summary>FACTOS lines: ALL of the NPC's own kills (in the second person), then recent deaths it knows (notable first).</summary>
        public static string FactsFor(Actor npc, bool pt, int cap = 7)
        {
            try
            {
                long me = npc.getID();
                int year = Main.CurrentYear();
                var lines = new List<string>();
                var mine = All().Where(r => r.KillerId == me).Reverse().Take(10).ToList();
                if (mine.Count > 0)
                    lines.Add("VOCÊ MESMO(A) matou: " + string.Join(", ", mine.Select(r => r.Name + " (ano " + r.Year + ", " + r.Cause + ")")) + ". Você sabe disso; fale disso na PRIMEIRA pessoa (\"eu matei...\"), nunca como se fosse outra pessoa. " + (Liar(npc)
                        ? "Você tem traço enganador/maligno: pode mentir ou desconversar se quiser."
                        : "Você NÃO é mentiroso(a): se perguntarem, admita (pode justificar)."));
                bool elder = IsElder(npc);
                int from = elder ? int.MinValue : FromYear(npc);   // v0.7.8 city elder: ordinary NPCs only know their own lifetime / recent window
                var known = Known(npc).Where(r => r.KillerId != me && (r.Year >= from || Rel(npc, r) != null))
                    .OrderByDescending(r => Rel(npc, r) != null).ThenByDescending(Notable).ThenByDescending(r => r.Year).Take(cap).ToList();
                foreach (var r in known) lines.Add((Rel(npc, r) ?? "") + SelfView(Sentence(r, true), npc));
                if (!elder && Known(npc).Any(r => r.Year < from && Rel(npc, r) == null))
                    lines.Add("há mortes mais antigas (antes do ano " + from + ") que você só conhece vagamente, de ouvir falar");
                return lines.Count == 0 ? null : string.Join("; ", lines);
            }
            catch { return null; }
        }

        // ---------- v0.7.8: the city ELDER (ancião/anciã) remembers the old chronicle; everyone else only recent history ----------
        public const int RecentYears = 30;

        /// <summary>The oldest living sapient citizen of the city (computed live).</summary>
        public static Actor Elder(City c)
        {
            if (c == null) return null;
            Actor best = null; int ba = -1;
            try
            {
                foreach (Actor a in (IEnumerable<Actor>)c.units)
                {
                    if (a == null || !a.isAlive()) continue;
                    bool sap = true; try { sap = a.isSapient(); } catch { }
                    if (!sap) continue;
                    int ag = 0; try { ag = a.getAge(); } catch { }
                    if (ag > ba) { ba = ag; best = a; }
                }
            }
            catch { }
            return best;
        }
        public static bool IsElder(Actor npc) { try { return npc?.city != null && Elder(npc.city) == npc; } catch { return false; } }
        public static bool IsRuler(Actor npc) { try { return npc.isKing() || npc.isCityLeader(); } catch { return false; } }
        /// <summary>First year an ordinary NPC remembers well: its birth, but at most RecentYears back.</summary>
        public static int FromYear(Actor npc)
        {
            int y = Main.CurrentYear(), age = 0; try { age = npc.getAge(); } catch { }
            return Math.Max(y - age, y - RecentYears);
        }
        static string ElderTitle(Actor a) { try { return a.isSexFemale() ? "anciã" : "ancião"; } catch { return "ancião"; } }
        static string NameOf(Actor a) { try { return a.getName(); } catch { return "?"; } }
        static int AgeOf(Actor a) { try { return a.getAge(); } catch { return 0; } }

        /// <summary>v0.7.9: always-present FACTS line "o ancião de <cidade> é <nome>, <idade> anos" (city of the NPC, or the nearest city).</summary>
        public static string ElderLine(Actor npc)
        {
            try
            {
                if (npc == null) return null;
                City c = npc.city;
                bool own = c != null;
                if (c == null)
                {
                    float bd = float.MaxValue;
                    foreach (var x in World.world.cities)
                    {
                        var t = x?.getTile(); if (t == null || npc.current_tile == null) continue;
                        float d = (t.pos - npc.current_tile.pos).sqrMagnitude;
                        if (d < bd) { bd = d; c = x; }
                    }
                }
                if (c == null) return null;
                var e = Elder(c);
                if (e == null) return "a cidade " + c.name + " não tem nenhum morador vivo para ser o ancião";
                if (e == npc) return "VOCÊ é o(a) " + ElderTitle(e) + " de " + c.name + ", " + AgeOf(e) + " anos (a pessoa mais velha da cidade)";
                return (ElderTitle(e) == "anciã" ? "a anciã" : "o ancião") + " de " + c.name + " é " + NameOf(e) + ", " + AgeOf(e) + " anos (a pessoa mais velha da cidade" + (own ? "" : "; " + c.name + " é a cidade mais próxima de você") + "). Se perguntarem se há um ancião/idoso, a resposta é SIM: " + NameOf(e)
                     + ". NUNCA invente outro ancião, líder ou nome de pessoa: só cite pessoas que estão nos FATOS. Para história antiga, aponte " + NameOf(e) + ".";
            }
            catch { return null; }
        }

        /// <summary>FACTS line: who the elder is (for non-elders), or the elder's full city/kingdom memory (for the elder).</summary>
        public static string ElderFacts(Actor npc)
        {
            try
            {
                var c = npc?.city; if (c == null) return null;
                var e = Elder(c); if (e == null) return null;
                if (e != npc)
                    return "O " + ElderTitle(e) + " de " + c.name + " é " + NameOf(e) + " (" + AgeOf(e) + " anos), a pessoa mais velha da cidade; " + (ElderTitle(e) == "anciã" ? "ela" : "ele")
                         + " conhece a história antiga (reis e líderes passados, mortes antigas, fundação). Se perguntarem algo antigo que você não sabe, diga com honestidade que não sabe e sugira perguntar a " + NameOf(e) + ".";
                return "VOCÊ é o(a) " + ElderTitle(e).ToUpperInvariant() + " de " + c.name + " (a pessoa mais velha da cidade, " + AgeOf(e) + " anos) e sabe disso. MEMÓRIA DO ANCIÃO (verdade do jogo, fatos na terceira pessoa): " + ElderMemory(npc, 2200);
            }
            catch (Exception ex) { Main.LogWarning("[GodBox] elder facts failed: " + ex.Message); return null; }
        }

        static bool IsRulerRole(string role) => role != null && (role.StartsWith("rei de") || role.StartsWith("rainha de") || role.StartsWith("líder de"));

        /// <summary>Past rulers of the NPC's kingdom/city from the chronicle + "novo rei" rumours (oldest first).</summary>
        static List<string> RulerLines(Actor npc, Kingdom k, City c, int fromYear)
        {
            long kid = -1, cid = -1; try { kid = k?.getID() ?? -1; cid = c?.getID() ?? -1; } catch { }
            string fk = k != null ? NativeActions.Fold(k.name) : null, fc = c != null ? NativeActions.Fold(c.name) : null;
            var rs = All().Where(r => IsRulerRole(r.Role) && r.Year >= fromYear
                        && (((kid >= 0 && r.KingdomId == kid) || (fk != null && NativeActions.Fold(r.Role).Contains(fk))) && !r.Role.StartsWith("líder de")
                            || (r.Role.StartsWith("líder de") && ((cid >= 0 && r.CityId == cid) || (fc != null && NativeActions.Fold(r.Role).Contains(fc))))))
                      .OrderBy(r => r.Year).ToList();
            var lines = rs.Select(r => SelfView(r.Name + " (" + r.Role.Split(',')[0] + ") morreu no ano " + r.Year + " (" + (r.Cause ?? "causa desconhecida") + (r.Killer != null ? ", por " + r.Killer : "") + ")", npc)).ToList();
            try
            {
                if (c != null && k != null)
                    foreach (var t in Rumors.CityTexts(c))
                        if (t.Contains(k.name) && System.Text.RegularExpressions.Regex.IsMatch(t, "é o novo|é a nova|novo rei|nova rainha")) lines.Add("boato: " + t);
            }
            catch { }
            return lines;
        }

        /// <summary>v0.7.9: "REIS do reino X: ..." or "LÍDERES da cidade Y (não são reis): ..." from the game's past_rulers list.</summary>
        static string RulerHistory(Actor npc, List<LeaderEntry> list, Actor current, bool kings, string place)
        {
            try
            {
                if (place == null) return null;
                var items = new List<string>();
                long me = -1; try { me = npc.getID(); } catch { }
                var dead = All();
                if (list != null)
                    foreach (var e in list)
                    {
                        if (e == null) continue;
                        int from = Date.getYear(e.timestamp_ago);
                        bool ended = e.timestamp_end >= e.timestamp_ago && e.timestamp_end > 0;
                        bool isCur = !ended && current != null && current.getID() == e.id;
                        string who = e.id == me ? "VOCÊ (" + e.name + ")" : e.name;
                        string t = who + " (" + (kings ? "rei" : "líder") + (ended ? " do ano " + from + " ao ano " + Date.getYear(e.timestamp_end) : isCur ? " desde o ano " + from + ", o atual" : " desde o ano " + from) + ")";
                        var d = dead.LastOrDefault(r => r.Id == e.id);
                        if (d != null) t += ": morreu no ano " + d.Year + " (" + (d.Cause ?? "causa desconhecida") + (d.Killer != null ? ", morto(a) por " + (d.KillerId == me && me >= 0 ? "VOCÊ" : d.Killer) : "") + ")";
                        items.Add(t);
                    }
                if (current != null && current.isAlive() && (list == null || !list.Any(e => e != null && e.id == current.getID())))
                    items.Add((current.getID() == me ? "VOCÊ (" + current.getName() + ")" : current.getName()) + " (" + (kings ? "rei" : "líder") + " atual)");
                if (items.Count == 0) return null;
                if (items.Count > 12) items = items.Skip(items.Count - 12).ToList();
                return kings
                    ? "REIS do reino " + place + " (lista oficial; só estes foram reis): " + string.Join("; ", items) + (items.Count == 1 ? " — o reino só teve este rei" : "")
                    : "LÍDERES da cidade " + place + " (líderes de cidade, NÃO são reis): " + string.Join("; ", items);
            }
            catch { return null; }
        }

        static string ElderMemory(Actor npc, int maxChars)
        {
            Kingdom k = null; City c = null; try { c = npc.city; k = npc.kingdom != null && npc.kingdom.isCiv() ? npc.kingdom : null; } catch { }
            var parts = new List<string>();
            try
            {
                if (c != null) { int fy = Date.getYear(c.data.created_time); parts.Add("fundação: " + c.name + " foi fundada no ano " + fy + (string.IsNullOrEmpty(c.data.founder_name) ? "" : " por " + c.data.founder_name)); }
                if (k != null) parts.Add("o reino " + k.name + " surgiu no ano " + Date.getYear(k.data.created_time));
            }
            catch { }
            // v0.7.9: kings and city leaders kept APART, from the game's own ruler lists (past_rulers) + the chronicle's causes
            string kings = RulerHistory(npc, k?.data?.past_rulers, k?.king, true, k?.name);
            string leaders = RulerHistory(npc, c?.data?.past_rulers, c?.leader, false, c?.name);
            if (kings != null) parts.Add(kings);
            if (leaders != null) parts.Add(leaders);
            if (kings == null && leaders == null)
            {
                var rl = RulerLines(npc, k, c, int.MinValue);
                parts.Add("governantes passados: " + (rl.Count > 0 ? string.Join("; ", rl.Skip(Math.Max(0, rl.Count - 12))) : "nenhum morreu na crônica que você conhece"));
                try { var kg = k?.king; if (kg != null && kg.isAlive()) parts.Add("rei atual: " + kg.getName()); } catch { }
            }
            long me = -1, kid = -1, cid = -1; try { me = npc.getID(); kid = k?.getID() ?? -1; cid = c?.getID() ?? -1; } catch { }
            var old = All().Where(r => !IsRulerRole(r.Role) && ((cid >= 0 && r.CityId == cid) || (kid >= 0 && r.KingdomId == kid)))
                           .OrderByDescending(Notable).ThenByDescending(r => r.Year).Take(15).OrderBy(r => r.Year)
                           .Select(r => r.KillerId == me && me >= 0 ? "VOCÊ matou " + r.Name + " (ano " + r.Year + ", " + r.Cause + ")"
                                        : r.Name + (r.Role != null ? " (" + r.Role + ")" : "") + " (ano " + r.Year + ", " + r.Cause + (r.Killer != null ? ", por " + r.Killer : "") + ")").ToList();
            if (old.Count > 0) parts.Add("mortes marcantes: " + string.Join(", ", old));
            // v0.7.9: the elder REMEMBERS these events; it did not take part in them
            string me2 = NameOf(npc);
            parts.Add("IMPORTANTE: tudo isto são fatos sobre OUTRAS pessoas, contados na terceira pessoa. Você (" + me2 + ") NÃO participou destes eventos, a não ser que seu nome apareça como autor (\"VOCÊ matou\"). Não diga \"eu matei\" nem \"fui eleito\" sobre eles"
                      + (IsRuler(npc) ? "" : "; você NÃO é rei nem líder") + ".");
            string res = string.Join(" | ", parts);
            return res.Length <= maxChars ? res : res.Substring(0, maxChars) + "…";
        }

        static string Rel(Actor npc, Rec r)
        {
            try
            {
                if (npc.lover != null && npc.lover.getID() == r.Id) return "seu amor ";
                if (r.Id == npc.data.parent_id_1 || r.Id == npc.data.parent_id_2) return "seu pai/mãe ";
            }
            catch { }
            return null;
        }

        /// <summary>Deaths the NPC can know about: its city, its kingdom's notables, its relatives, its own kills, people it talked about,
        /// and any death that reached its city as a rumour.</summary>
        static IEnumerable<Rec> Known(Actor npc)
        {
            long me = -1, cid = -1, kid = -1;
            try { me = npc.getID(); cid = npc.city?.getID() ?? -1; kid = npc.kingdom?.getID() ?? -1; } catch { }
            var heard = new HashSet<string>();
            try { foreach (var t in Rumors.CityTexts(npc.city)) heard.Add(t); } catch { }
            foreach (var r in All().Reverse())
            {
                bool k = r.KillerId == me || (cid >= 0 && r.CityId == cid) || (kid >= 0 && r.KingdomId == kid) || Rel(npc, r) != null;
                if (!k && heard.Count > 0) { string sen = Sentence(r, true); k = heard.Contains(sen); }
                if (k) yield return r;
            }
        }

        // ---------- v0.7.8: "deus matou alguém?" / "quem morreu de velhice?" / "o que aconteceu com Norron?" ----------
        static readonly System.Text.RegularExpressions.Regex DeathQRx = new System.Text.RegularExpressions.Regex(@"\b(morte|mortes|morreu|morreram|morto|morta|mortos|mortas|matou|mataram|matado|assassin\w*|falece\w*|faleci\w*|obito|died|death|killed)\b");
        static readonly (string cause, System.Text.RegularExpressions.Regex rx)[] CauseQ =
        {
            ("poder divino", new System.Text.RegularExpressions.Regex(@"\b(deus|deuses|divin\w*|ceu|ceus|raio|god|gods)\b")),
            ("fogo", new System.Text.RegularExpressions.Regex(@"\b(fogo|queimad\w*|incendio|chamas)\b")),
            ("velhice", new System.Text.RegularExpressions.Regex(@"\b(velhice|velho|velha|idade)\b")),
            ("em combate", new System.Text.RegularExpressions.Regex(@"\b(combate|batalha|guerra|luta|lutando|espada)\b")),
            ("fome", new System.Text.RegularExpressions.Regex(@"\b(fome|faminto)\b")),
            ("doença", new System.Text.RegularExpressions.Regex(@"\b(doenca|peste|praga|febre|infeccao|tumor)\b")),
            ("afogado(a)", new System.Text.RegularExpressions.Regex(@"\b(afog\w*)\b")),
        };

        // ---------- v0.7.8: "quais foram os reis desse reino?" / "quem reinou antes?" -> kings/queens in the death chronicle ----------
        static readonly System.Text.RegularExpressions.Regex RulerQRx = new System.Text.RegularExpressions.Regex(
            @"\b(quem (reinou|reinava|governou|governava|mandava|foi (o )?rei|foi (a )?rainha)|reinaram|governaram|(reis|rainhas|governantes|monarcas|soberanos|lideres)( \w+){0,4} (anterior\w*|passad\w*|antig\w*|antes|foram|teve|tivemos|houve)|(quais|que) (foram |eram )?(os |as )?(reis|rainhas|governantes|monarcas)|(rei|rainha|governante|monarca) anterior|antigos? (reis?|rainhas?|governantes?)|past (kings|rulers)|previous (king|ruler)s?)\b");

        static string ElderTip(Actor npc)
        {
            var e = Elder(npc?.city);
            if (e == null || e == npc) return null;
            return " OBRIGATÓRIO: a história mais antiga você não conhece bem; diga isso com honestidade e diga o NOME de quem sabe: " + (ElderTitle(e) == "anciã" ? "a anciã" : "o ancião") + " da cidade, " + NameOf(e) + " (" + AgeOf(e) + " anos), a pessoa mais velha de " + CityName(e) + ". Não invente outro ancião nem outro nome.";
        }
        static string CityName(Actor a) { try { return a.city?.name ?? "?"; } catch { return "?"; } }

        // ---------- v0.8.0 (fix 6): ordinary NPCs point to the elder ----------
        static readonly Dictionary<long, float> _historyQ = new Dictionary<long, float>();
        /// <summary>Remembers that this NPC was just asked about old history (for an "alguém sabe?" follow-up).</summary>
        public static void NoteHistoryQ(Actor npc) { try { _historyQ[npc.getID()] = UnityEngine.Time.unscaledTime; } catch { } }
        public static bool RecentHistoryQ(Actor npc, float window = 600f) { try { return _historyQ.TryGetValue(npc.getID(), out var t) && UnityEngine.Time.unscaledTime - t < window; } catch { return false; } }
        /// <summary>The elder an ordinary NPC must point to (null for the elder itself, or when the city has none).</summary>
        public static Actor ElderToPoint(Actor npc) { try { var e = Elder(npc?.city); return e == null || e == npc ? null : e; } catch { return null; } }
        /// <summary>Code line naming the elder: "quem sabe dessas coisas antigas é a anciã Ariira, a pessoa mais velha de Yoa Dhil (105 anos)".</summary>
        public static string ElderPointLine(Actor npc)
        {
            var e = ElderToPoint(npc); if (e == null) return null;
            bool f = ElderTitle(e) == "anciã";
            return "Dessas coisas antigas eu não sei bem. Quem sabe é " + (f ? "a anciã " : "o ancião ") + NameOf(e) + ", a pessoa mais velha de " + CityName(e) + " (" + AgeOf(e) + " anos); pergunte a " + (f ? "ela" : "ele") + ".";
        }
        public static string ElderTitleOf(Actor a) => ElderTitle(a);
        public static bool RulerQ(string text) { try { return RulerQRx.IsMatch(NativeActions.Fold(text ?? "")); } catch { return false; } }

        static string RulersQuery(Actor npc, string text, string f, int cap)
        {
            Kingdom k = null; City c = null;
            try { c = npc.city; k = npc.kingdom != null && npc.kingdom.isCiv() ? npc.kingdom : null; } catch { }
            try
            {   // another kingdom named in the question?
                foreach (Kingdom o in World.world.kingdoms)
                {
                    if (o == null || !o.isCiv() || o == k) continue;
                    if (System.Text.RegularExpressions.Regex.IsMatch(f, @"\b" + System.Text.RegularExpressions.Regex.Escape(NativeActions.Fold(o.name)) + @"\b")) { k = o; c = null; break; }
                }
            }
            catch { }
            if (k == null)
                return "REIS ANTERIORES: você não tem reino e ninguém disse de qual reino; diga AGORA que não sabe de qual reino falam (não prometa pesquisar).";
            bool elder = IsElder(npc), ruler = IsRuler(npc);
            bool full = elder || ruler;   // the elder knows the old chronicle; kings and leaders know their rulers
            int from = full ? int.MinValue : FromYear(npc);
            var lines = RulerLines(npc, k, c ?? npc.city, from);
            if (lines.Count > cap) lines = lines.Skip(lines.Count - cap).ToList();
            string cur = null; try { var kg = k.king; if (kg != null && kg.isAlive()) cur = kg.getName(); } catch { }
            Main.LogInfo("[GodBox] rulers query (" + k.name + ", " + (elder ? "elder" : ruler ? "ruler" : "commoner since year " + from) + "): " + lines.Count + " line(s).");
            string head = "GOVERNANTES DE " + k.name + " QUE VOCÊ CONHECE (" + (elder ? "você é o(a) ancião/anciã: memória completa" : ruler ? "você governa: conhece os governantes" : "só o que aconteceu no seu tempo de vida / anos recentes") + "; crônica do jogo, verdade; responda com isto AGORA):";
            string body = lines.Count > 0 ? string.Join("\n", lines.Select(l => "- " + l)) : "- nenhum governante de " + k.name + " morreu na parte da crônica que você conhece";
            return head + "\n" + body + (cur != null ? "\nGoverna hoje: " + cur + "." : "")
                 + (full ? "" : (ElderTip(npc) ?? ""))
                 + "\nSe não souber mais, diga que é tudo o que sabe; NUNCA prometa pesquisar nem voltar depois.";
        }

        /// <summary>Matches for a death question, searched over the whole chronicle the NPC knows (null when it is not a death question).</summary>
        public static string QueryFor(Actor npc, string text, int cap = 8)
        {
            try
            {
                string f = NativeActions.Fold(text ?? "");
                if (SafetyRules.WhoKnowsQ(text) && RecentHistoryQ(npc))
                {   // v0.8.0 (fix 6): "alguém sabe (disso)?" right after an old-history question -> name the elder
                    var e = Elder(npc?.city);
                    Main.LogInfo("[GodBox] 'who knows?' follow-up after an old-history question; elder: " + (e != null ? NameOf(e) : "none") + ".");
                    if (e == null) return null;
                    if (e == npc) return "PERGUNTA DE ACOMPANHAMENTO: perguntaram se alguém sabe da história antiga. VOCÊ é o(a) " + ElderTitle(e) + " (a pessoa mais velha de " + CityName(e) + "): diga que você é quem sabe e conte o que lembra (MEMÓRIA DO ANCIÃO).";
                    return "PERGUNTA DE ACOMPANHAMENTO: perguntaram se alguém sabe da história antiga. RESPONDA dizendo o NOME: " + (ElderTitle(e) == "anciã" ? "a anciã " : "o ancião ") + NameOf(e) + ", a pessoa mais velha de " + CityName(e) + " (" + AgeOf(e) + " anos), é quem sabe. Não invente outro nome.";
                }
                if (RulerQRx.IsMatch(f)) return RulersQuery(npc, text, f, cap);
                if (SafetyRules.OldHistoryQ(text) && !DeathQRx.IsMatch(f))
                {   // v0.8.0 (fix 6): old history (founding, old wars, past rulers in general) -> ordinary NPCs point to the elder
                    if (IsElder(npc)) return null;   // the elder has its full memory in FACTS
                    var tip = ElderTip(npc);
                    return tip == null ? null : "HISTÓRIA ANTIGA (pergunta):" + tip;
                }   // v0.7.8: past kings come from the same chronicle
                if (!DeathQRx.IsMatch(f)) return null;
                long me = -1; try { me = npc.getID(); } catch { }
                var filters = new List<string>();
                bool wise = IsElder(npc);
                int from = wise ? int.MinValue : FromYear(npc);
                var allK = Known(npc).ToList();
                var all = allK.Where(r => r.Year >= from || r.KillerId == me || Rel(npc, r) != null).ToList();
                bool older = allK.Count > all.Count;
                IEnumerable<Rec> q = all;
                foreach (var (cause, rx) in CauseQ)
                    if (rx.IsMatch(f)) { q = q.Where(r => (r.Cause ?? "").StartsWith(cause.Substring(0, 4)) || (cause == "poder divino" && r.ModCause != null && r.ModCause.Contains("deus"))); filters.Add(cause); break; }
                var ym = System.Text.RegularExpressions.Regex.Match(f, @"\bano (\d{1,5})\b");
                if (ym.Success) { int y = int.Parse(ym.Groups[1].Value); q = q.Where(r => r.Year == y); filters.Add("ano " + y); }
                // a named victim or killer
                var words = System.Text.RegularExpressions.Regex.Matches(text ?? "", @"\b\p{Lu}[\p{L}'-]{2,}").Cast<System.Text.RegularExpressions.Match>().Select(m => NativeActions.Fold(m.Value)).ToList();
                var named = all.Where(r => words.Any(w => (r.Name != null && NameLookup.Score(w, NativeActions.Fold(r.Name)) >= 80) || (r.Killer != null && NameLookup.Score(w, NativeActions.Fold(r.Killer)) >= 80))).ToList();
                if (named.Count > 0) { q = q.Intersect(named); filters.Add("nome citado"); }
                var hits = q.OrderByDescending(r => r.Year).Take(cap).ToList();
                if (filters.Count == 0 && hits.Count == 0) return null;
                string head = "MORTES QUE VOCÊ CONHECE E BATEM COM A PERGUNTA" + (filters.Count > 0 ? " (" + string.Join(", ", filters) + ")" : "") + " — crônica do jogo, verdade";
                string elderTip = older ? ElderTip(npc) : null;
                if (hits.Count == 0) return head + ": NENHUMA. Diga que não soube de nenhuma morte assim." + (elderTip ?? "");
                Main.LogInfo("[GodBox] death query (" + string.Join(", ", filters) + "): " + hits.Count + " match(es).");
                return head + ":\n" + string.Join("\n", hits.Select(r => "- " + (r.KillerId == me && me >= 0 ? "VOCÊ MESMO(A) matou " + r.Name + " no ano " + r.Year + " (" + r.Cause + ")" : SelfView(Describe(r, npc, true), npc))))
                     + "\nUse estes fatos (o boato \"pelo poder dos deuses\" é morte causada por Deus). Fale de si mesmo(a) sempre na primeira pessoa." + (elderTip ?? "");
            }
            catch (Exception e) { Main.LogWarning("[GodBox] death query failed: " + e.Message); return null; }
        }
    }
}
