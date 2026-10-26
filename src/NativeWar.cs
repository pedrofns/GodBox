using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.6: real diplomacy through WorldBox's own APIs (no teleports, no fake state):
    /// declare_war  -> World.world.diplomacy.startWar(ours, theirs, WarTypeLibrary.normal) (alliances, armies and AI react by themselves)
    /// make_peace   -> World.world.wars.endWar(war, WarWinner.Peace) or War.leaveWar for a side kingdom
    /// attack_city  -> the kingdom's cities with warriors get City.target_attack_city/target_attack_zone (the same fields the
    ///                 city AI "check attack zone" behaviour uses), so the armies march there by themselves; a foreign
    ///                 kingdom not at war gets war declared first (it is in the confirmed terms). A city of the NPC's OWN
    ///                 kingdom: only the god voice, two confirmations, then City.makeOwnKingdom(leader, rebellion) +
    ///                 startWar(..., WarTypeLibrary.rebellion), like a vanilla rebellion.
    /// Permissions: the god voice (trait-based defiance in Gate) or the player possessing the king of the NPC's kingdom.
    /// </summary>
    public static partial class NativeActions
    {
        static string KName(Kingdom k) { try { return k?.name ?? "?"; } catch { return "?"; } }

        static bool LiveCiv(Kingdom k) { try { return k != null && k.isAlive() && k.isCiv(); } catch { return false; } }

        static string WarBlock(Actor npc)
        {
            try { return LiveCiv(npc.kingdom) ? null : "não pertence a um reino"; } catch { return "não pertence a um reino"; }
        }

        /// <summary>Possessed speaker: must be the king of the NPC's kingdom. Own-city rebellion: god only. null = allowed.</summary>
        static string KingOrGodBlock(string id, Actor npc, Actor you, string target)
        {
            if (you == null) return null;
            bool king = false;
            try { king = you.isKing() && you.kingdom != null && you.kingdom == npc.kingdom; } catch { }
            string what = id == "make_peace" ? "fazer a paz" : id == "attack_city" ? "mandar o exército atacar uma cidade" : "declarar guerra";
            if (!king) return "só o rei ou os deuses podem " + what;
            if (id == "attack_city")
            {
                var c = FindCity(target, npc);
                try { if (c != null && c.kingdom == npc.kingdom) return "só os deuses podem mandar atacar uma cidade do nosso próprio reino"; } catch { }
            }
            return null;
        }

        // ------------------------------------------------------------------------------------------------
        // Kingdom resolution (name / species / ruler / city), fuzzy via NameLookup.Score
        // ------------------------------------------------------------------------------------------------
        static readonly (string rx, string id, string pt)[] Species =
        {
            (@"^(os |as )?(orc|orcs|orcos?|orques?)$", "orc", "orcs"),
            (@"^(os |as )?(elfo|elfos|elfa|elfas|elf|elves)$", "elf", "elfos"),
            (@"^(os |as )?(humano|humanos|humana|humanas|human|humans|homens)$", "human", "humanos"),
            (@"^(os |as )?(anao|anoes|anaos|ana|anas|dwarf|dwarfs|dwarves)$", "dwarf", "anões"),
        };

        static string KingdomQuery(string q)
        {
            q = CleanTarget(q);
            q = Regex.Replace(q, @"^(contra|com|a|ao|aos|as|o|os|de|do|dos|da|das|em)\s+", "");
            q = Regex.Replace(q, @"^(o |os )?(reino|imperio|nacao|povo|kingdom)\s+(de |do |da |dos |das |of )?", "");
            return q.Trim();
        }

        // ------------------------------------------------------------------------------------------------
        // v0.7.8: ambiguity -> a choice made by the code (option buttons + typed answers), never by the model
        // ------------------------------------------------------------------------------------------------
        public class ChoiceOpt { public string Label, Target, Detail; public float Dist = float.MaxValue; public List<string> Aliases = new List<string>(); }
        public class Choice
        {
            public string Id, What, Question;          // Question: the code-built in-character question (fallback)
            public bool AllowAll;                       // "Ambos"/"Todos" allowed (several targets in one order)
            public List<ChoiceOpt> Options = new List<ChoiceOpt>();
        }

        static bool TryToken(string q, string prefix, out long id)
        {
            id = -1;
            q = (q ?? "").Trim();
            return q.StartsWith(prefix) && long.TryParse(q.Substring(prefix.Length), out id);
        }
        public static string KToken(Kingdom k) { try { return "#k:" + k.getID(); } catch { return KName(k); } }
        public static string CToken(City c) { try { return "#c:" + c.getID(); } catch { return c?.name ?? ""; } }

        static float Dist(WorldTile a, WorldTile b)
        {
            if (a == null || b == null) return float.MaxValue;
            float dx = a.x - b.x, dy = a.y - b.y; return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>The kingdoms a species word ("os orcs") stands for, or null when the query is not a species word.</summary>
        static List<Kingdom> SpeciesCandidates(string query, Actor npc, out string ptName)
        {
            ptName = null;
            string q = KingdomQuery(query);
            if (q.Length == 0) return null;
            Kingdom mine = null; try { mine = npc.kingdom; } catch { }
            foreach (var (rx, id, pn) in Species)
            {
                if (!Regex.IsMatch(q, rx)) continue;
                ptName = pn;
                var list = new List<Kingdom>();
                try { foreach (Kingdom k in World.world.kingdoms) try { if (LiveCiv(k) && k != mine && k.getSpecies() == id) list.Add(k); } catch { } } catch { }
                return list;
            }
            return null;
        }

        static string Address(Actor npc, Actor you)
        {
            if (you == null) return "meu Senhor";
            try { if (you.isKing() && you.kingdom != null && npc.kingdom == you.kingdom) return "Majestade"; } catch { }
            return null;
        }

        static string OrList(List<string> xs) =>
            xs.Count <= 1 ? (xs.Count == 1 ? xs[0] : "") : string.Join(", ", xs.Take(xs.Count - 1)) + " ou " + xs[xs.Count - 1];

        /// <summary>v0.7.8: null = the request is not ambiguous. Otherwise the options the player must choose from
        /// (several kingdoms of one species for war/peace; several cities with the same name for attack_city).</summary>
        public static Choice Ambiguity(WorldfallActions.Request req, Actor npc, Actor you)
        {
            if (req == null || npc == null || (req.Targets != null && req.Targets.Count > 0)) return null;
            try
            {
                if (WarBlock(npc) != null) return null;
                Kingdom mine = npc.kingdom;
                WorldTile here = null; try { here = npc.current_tile; } catch { }
                if (req.Id == "make_peace")
                    return PeaceChoice(req, npc, you, mine, here);
                if (req.Id == "declare_war")
                {
                    var list = SpeciesCandidates(req.Target, npc, out string ptName);
                    if (list == null || list.Count < 2) return null;
                    if (req.Id == "make_peace")
                    {
                        var atWar = list.Where(k => WarBetween(mine, k) != null).ToList();
                        if (atWar.Count == 1) { req.Target = KToken(atWar[0]); return null; }   // only one of them is at war with us
                        if (atWar.Count > 1) list = atWar;
                    }
                    else
                    {
                        var free = list.Where(k => WarBetween(mine, k) == null).ToList();
                        if (free.Count == 1) { req.Target = KToken(free[0]); return null; }      // the other one is already at war with us
                        if (free.Count > 1) list = free;
                    }
                    var c = new Choice { Id = req.Id, What = "reinos de " + ptName, AllowAll = true };
                    foreach (var k in list)
                    {
                        var o = new ChoiceOpt { Label = KName(k), Target = KToken(k) };
                        var extra = new List<string>();
                        try { if (k.king != null) { extra.Add("rei " + k.king.getName()); o.Aliases.Add(k.king.getName()); } } catch { }
                        try { if (k.capital != null) { o.Aliases.Add(k.capital.name); if (here != null) { extra.Add(FactsBuilder.Where(here, k.capital.getTile(), true)); o.Dist = Dist(here, k.capital.getTile()); } } } catch { }
                        try { if (WarBetween(mine, k) != null) extra.Add("já em guerra conosco"); } catch { }
                        o.Detail = string.Join(", ", extra);
                        c.Options.Add(o);
                    }
                    c.Options = c.Options.OrderBy(o => o.Dist).Take(6).ToList();
                    string adr = Address(npc, you);
                    string lead = req.Id == "make_peace" ? "Com qual dos " + c.What + " faremos as pazes" : "Contra qual dos " + c.What + " vamos à guerra";
                    c.Question = lead + (adr != null ? ", " + adr : "") + "? " + OrList(c.Options.Select(o => o.Label).ToList()) + "? Ou " + (c.Options.Count == 2 ? "ambos" : "todos") + "?";
                    return c;
                }
                if (req.Id == "attack_city")
                {
                    if ((req.Target ?? "").Trim().StartsWith("#c:")) return null;
                    string q = CleanTarget(req.Target);
                    q = Regex.Replace(q, @"^(cidade|vila|aldeia|capital|city|town)( de| do| da)?\s+", "");
                    if (q.Length == 0 || Regex.IsMatch(q, @"^(nossa |sua |minha |esta |essa )?(capital|cidade|vila)$")) return null;
                    var hits = new List<(City c, int s)>();
                    foreach (City ci in World.world.cities)
                        try { if (ci != null && ci.isAlive()) { int sc = NameLookup.Score(q, ci.name); if (sc >= 60) hits.Add((ci, sc)); } } catch { }
                    if (hits.Count < 2) return null;
                    int top = hits.Max(h => h.s);
                    var same = hits.Where(h => h.s == top).Select(h => h.c).ToList();
                    if (same.Count < 2) return null;
                    var ch = new Choice { Id = req.Id, What = "cidades chamadas \"" + req.Target + "\"", AllowAll = false };
                    foreach (var ci in same)
                    {
                        var o = new ChoiceOpt { Target = CToken(ci) };
                        string kn = null; try { kn = KName(ci.kingdom); } catch { }
                        o.Label = ci.name + (kn != null ? " (" + kn + ")" : "");
                        if (kn != null) o.Aliases.Add(kn);
                        var extra = new List<string>();
                        try { if (ci.kingdom == mine) extra.Add("DO NOSSO reino"); else extra.Add("do reino " + kn); } catch { }
                        try { if (here != null) { extra.Add(FactsBuilder.Where(here, ci.getTile(), true)); o.Dist = Dist(here, ci.getTile()); } } catch { }
                        o.Detail = string.Join(", ", extra);
                        ch.Options.Add(o);
                    }
                    ch.Options = ch.Options.OrderBy(o => o.Dist).Take(6).ToList();
                    string adr = Address(npc, you);
                    ch.Question = "Qual delas" + (adr != null ? ", " + adr : "") + "? " + OrList(ch.Options.Select(o => o.Label).ToList()) + "?";
                    return ch;
                }
            }
            catch (Exception e) { Main.LogWarning("[GodBox] ambiguity check failed: " + e.Message); }
            return null;
        }

        // ------------------------------------------------------------------------------------------------
        // v0.7.9: make_peace target resolution - only CURRENT enemies are ever offered
        // ------------------------------------------------------------------------------------------------
        static readonly Regex PeaceAllRx = new Regex(
            @"^(com )?(todos|todas|todo mundo|todo o mundo|toda gente|geral|ambos|ambas|os dois|as duas)\b|\b(todos os reinos|todos os inimigos|todas as guerras|com todos|com todo mundo|com todo o mundo|as guerras|todas elas|todos eles)\b|^(all|everyone|all of them|all kingdoms|all wars)$",
            RegexOptions.CultureInvariant);
        static readonly Regex PeaceVagueRx = new Regex(
            @"^((a|as|essa|esta|nossa|da|das) )?(guerra|guerras|paz|pazes|luta|lutas|conflito|conflitos|batalha|batalhas)$|^(eles|elas|o inimigo|os inimigos|inimigo|inimigos|nossos inimigos|isso|logo|agora|ja|ja ja|de vez|por favor|o reino|um reino|o outro reino|os outros|com eles)$",
            RegexOptions.CultureInvariant);

        /// <summary>Every live kingdom at war with ours, sorted by name.</summary>
        public static List<Kingdom> Enemies(Kingdom mine)
        {
            var list = new List<Kingdom>();
            if (mine == null) return list;
            try { foreach (Kingdom k in World.world.kingdoms) try { if (LiveCiv(k) && k != mine && WarBetween(mine, k) != null) list.Add(k); } catch { } } catch { }
            return list.OrderBy(k => KName(k), StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>"todos / com todos / todas as guerras / acabe as guerras" = every current enemy.</summary>
        public static bool PeaceMeansAll(string target, string message)
        {
            string t = KingdomQuery(target ?? "");
            if (t.Length > 0) return PeaceAllRx.IsMatch(t) || PeaceAllRx.IsMatch(Fold(target ?? "").Trim());
            string m = Fold(message ?? "");
            return Regex.IsMatch(m, @"\b(todos os reinos|todos os inimigos|todas as guerras|com todos|com todo mundo|com todo o mundo|(acabe|acabem|termine|terminem|encerre|encerrem|pare|parem|fim (a|as)) (com )?(as|todas as) guerras)\b");
        }

        /// <summary>No target, a vague word, or our OWN kingdom (classifier slip) = no target.</summary>
        static bool PeaceVague(string target, Actor npc)
        {
            string q = KingdomQuery(target ?? "");
            if (q.Length == 0 || PeaceVagueRx.IsMatch(q)) return true;
            if (TryToken(target, "#k:", out _)) return false;
            if (SpeciesCandidates(target, npc, out _) != null) return false;
            Kingdom mine = null; try { mine = npc.kingdom; } catch { }
            var k = FindKingdom(target, npc, out _);
            return k != null && k == mine;
        }

        static Choice PeaceChoice(WorldfallActions.Request req, Actor npc, Actor you, Kingdom mine, WorldTile here)
        {
            var enemies = Enemies(mine);
            if (enemies.Count == 0) return null;   // Execute explains: not at war with anyone
            void All()
            {
                req.Target = KToken(enemies[0]);
                req.Targets = enemies.Count > 1 ? enemies.Select(KToken).ToList() : null;
                Main.LogInfo("[GodBox] ACTION make_peace: \"todos\" = the " + enemies.Count + " current enemies (" + string.Join(", ", enemies.Select(KName)) + ").");
            }
            if (PeaceMeansAll(req.Target, req.Message)) { All(); return null; }
            List<Kingdom> pool = null; string what = "reinos em guerra conosco";
            var sp = SpeciesCandidates(req.Target, npc, out string ptName);
            if (sp != null)
            {
                pool = sp.Where(k => enemies.Contains(k)).OrderBy(k => KName(k), StringComparer.OrdinalIgnoreCase).ToList();
                if (pool.Count == 0) return null;   // Execute explains: no war with that species
                what = "reinos de " + ptName + " em guerra conosco";
            }
            else if (PeaceVague(req.Target, npc))
            {
                if (!string.IsNullOrWhiteSpace(req.Target))
                    Main.LogInfo("[GodBox] ACTION make_peace: target \"" + req.Target + "\" is vague or our own kingdom: treated as no target.");
                pool = enemies;
            }
            else return null;   // a named kingdom: Execute checks the war (sem_efeito when not at war)
            if (pool.Count == 1) { req.Target = KToken(pool[0]); req.Targets = null; return null; }
            var c = new Choice { Id = req.Id, What = what, AllowAll = true };
            foreach (var k in pool)
            {
                var o = new ChoiceOpt { Label = KName(k), Target = KToken(k) };
                var extra = new List<string>();
                try { if (k.king != null) { extra.Add("rei " + k.king.getName()); o.Aliases.Add(k.king.getName()); } } catch { }
                try { if (k.capital != null) { o.Aliases.Add(k.capital.name); if (here != null) { extra.Add(FactsBuilder.Where(here, k.capital.getTile(), true)); o.Dist = Dist(here, k.capital.getTile()); } } } catch { }
                try { string wn = WName(WarBetween(mine, k)); if (wn != null) extra.Add("guerra \"" + wn + "\""); } catch { }
                o.Detail = string.Join(", ", extra);
                c.Options.Add(o);
            }
            c.Options = c.Options.Take(8).ToList();   // already sorted by name
            string adr = Address(npc, you);
            c.Question = "Com qual dos " + c.What + " faremos as pazes" + (adr != null ? ", " + adr : "") + "? " + OrList(c.Options.Select(o => o.Label).ToList()) + "? Ou " + (c.Options.Count == 2 ? "ambos" : "todos") + "?";
            return c;
        }

        /// <summary>v0.7.9: null = a single kingdom to look up normally; otherwise why peace cannot happen.</summary>
        static string PeaceNoTarget(string target, Actor npc, out Kingdom only)
        {
            only = null;
            Kingdom mine = null; try { mine = npc.kingdom; } catch { }
            var enemies = Enemies(mine);
            var sp = SpeciesCandidates(target, npc, out string ptName);
            if (sp != null)
            {
                var pool = sp.Where(k => enemies.Contains(k)).ToList();
                if (pool.Count == 0) return "não estamos em guerra com nenhum reino de " + ptName;
                if (pool.Count == 1) { only = pool[0]; return null; }
                return "há " + pool.Count + " reinos de " + ptName + " em guerra conosco: " + string.Join(", ", pool.Select(KName)) + " — é preciso escolher qual";
            }
            if (PeaceMeansAll(target, null) || PeaceVague(target, npc))
            {
                if (enemies.Count == 0) return "o reino " + KName(mine) + " não está em guerra com ninguém";
                if (enemies.Count == 1) { only = enemies[0]; return null; }
                return "estamos em guerra com " + enemies.Count + " reinos (" + string.Join(", ", enemies.Select(KName)) + ") — é preciso escolher qual, ou todos";
            }
            return null;
        }

        /// <summary>v0.7.8: short "do what" text for the code-built confirmation question.</summary>
        public static string ShortTerms(WorldfallActions.Request req, Actor npc)
        {
            try
            {
                var ts = req.Targets != null && req.Targets.Count > 0 ? req.Targets : new List<string> { req.Target };
                if (req.Id == "declare_war" || req.Id == "make_peace")
                {
                    var names = ts.Select(t => KName(FindKingdom(t, npc, out _))).ToList();
                    string list = names.Count <= 1 ? names.FirstOrDefault() : string.Join(", ", names.Take(names.Count - 1)) + " e " + names.Last();
                    return req.Id == "declare_war" ? "declarar guerra a " + list : "fazer as pazes com " + list;
                }
                if (req.Id == "attack_city")
                {
                    var c = FindCity(req.Target, npc);
                    if (c == null) return null;
                    if (c.kingdom == npc.kingdom) return "fazer a cidade " + c.name + " se rebelar e atacá-la (irreversível)";
                    return (WarBetween(npc.kingdom, c.kingdom) == null ? "declarar guerra a " + KName(c.kingdom) + " e " : "") + "atacar a cidade " + c.name;
                }
                if (req.Id == "create_village") return "fundar uma nova vila";
                if (req.Id == "kill") return "matar " + Name(npc);
            }
            catch { }
            return null;
        }

        /// <summary>v0.7.8: does a repeated order point at the same target as the pending one?</summary>
        public static bool SameTarget(WorldfallActions.Request pend, WorldfallActions.Request again, Actor npc)
        {
            if (pend == null || again == null || pend.Id != again.Id) return false;
            if (pend.Id == "declare_war" || pend.Id == "make_peace")
            {
                var mine = (pend.Targets != null && pend.Targets.Count > 0 ? pend.Targets : new List<string> { pend.Target })
                    .Select(t => FindKingdom(t, npc, out _)).Where(k => k != null).ToList();
                var k2 = FindKingdom(again.Target, npc, out _);
                if (k2 != null) return mine.Contains(k2);
                var sp = SpeciesCandidates(again.Target, npc, out _);   // "os orcs" again, after Ne Ter was picked
                if (sp != null && sp.Count > 0) return mine.Count == 0 ? KingdomQuery(pend.Target) == KingdomQuery(again.Target) : mine.All(k => sp.Contains(k));
                return false;
            }
            if (pend.Id == "attack_city")
            {
                var c1 = FindCity(pend.Target, npc); var c2 = FindCity(again.Target, npc);
                if (c1 != null && c2 != null && c1 == c2) return true;
                try { return c1 != null && c2 != null && Fold(c1.name) == Fold(c2.name); } catch { return false; }   // same-name city (a choice was made)
            }
            return CleanTarget(pend.Target) == CleanTarget(again.Target);
        }

        static WorldfallActions.Request Sub(WorldfallActions.Request req, string t) =>
            new WorldfallActions.Request { Id = req.Id, Target = t, Amount = req.Amount, Style = req.Style, Message = req.Message, Confirms = req.Confirms };

        /// <summary>Finds a kingdom by name, species word, ruler or city. ask = question to the player when ambiguous
        /// (several kingdoms of one species) or why nothing was found.</summary>
        public static Kingdom FindKingdom(string query, Actor npc, out string ask)
        {
            ask = null;
            if (TryToken(query, "#k:", out long kid))
            {   // v0.7.8: a target picked from the choice buttons
                try { foreach (Kingdom k in World.world.kingdoms) if (LiveCiv(k) && k.getID() == kid) return k; } catch { }
                ask = "esse reino não existe mais"; return null;
            }
            string q = KingdomQuery(query);
            if (q.Length == 0) { ask = "não disse qual reino"; return null; }
            Kingdom mine = null; try { mine = npc.kingdom; } catch { }
            if (Regex.IsMatch(q, @"^(nosso|meu|seu|teu) (reino|povo)$|^(nosso|meu|seu|teu)$")) return mine;
            try
            {
                foreach (var (rx, id, ptName) in Species)
                {
                    if (!Regex.IsMatch(q, rx)) continue;
                    var list = new List<Kingdom>();
                    foreach (Kingdom k in World.world.kingdoms)
                        try { if (LiveCiv(k) && k != mine && k.getSpecies() == id) list.Add(k); } catch { }
                    if (list.Count == 1) return list[0];
                    if (list.Count == 0) { ask = "não existe agora nenhum outro reino de " + ptName; return null; }
                    WorldTile here = null; try { here = npc.current_tile; } catch { }
                    var opts = list.Take(6).Select(k =>
                    {
                        string extra = "";
                        try { if (k.king != null) extra += "rei " + k.king.getName(); } catch { }
                        try { if (k.capital != null && here != null) extra += (extra.Length > 0 ? ", " : "") + FactsBuilder.Where(here, k.capital.getTile(), true); } catch { }
                        try { if (mine != null && mine.isEnemy(k)) extra += (extra.Length > 0 ? ", " : "") + "já em guerra conosco"; } catch { }
                        return KName(k) + (extra.Length > 0 ? " (" + extra + ")" : "");
                    });
                    ask = "há " + list.Count + " reinos de " + ptName + ": " + string.Join("; ", opts) + " — pergunte ao jogador QUAL deles";
                    return null;
                }
                Kingdom best = null; int bs = 0;
                foreach (Kingdom k in World.world.kingdoms)
                {
                    if (!LiveCiv(k)) continue;
                    int sc = NameLookup.Score(q, KName(k)) + 3;                    // kingdom name wins ties
                    try { if (k.king != null) sc = Math.Max(sc, NameLookup.Score(q, k.king.getName())); } catch { }
                    try { foreach (City c in k.getCities()) sc = Math.Max(sc, NameLookup.Score(q, c.name) - 5); } catch { }
                    if (sc > bs) { bs = sc; best = k; }
                }
                if (best != null && bs >= 20) return best;
            }
            catch (Exception e) { Main.LogWarning("[GodBox] kingdom lookup failed: " + e.Message); }
            ask = "não existe reino, rei ou cidade chamado \"" + query + "\" neste mundo";
            return null;
        }

        static int BestKingdomScore(string q)
        {
            q = KingdomQuery(q); int bs = 0;
            if (q.Length == 0) return 0;
            foreach (var (rx, _, _) in Species) if (Regex.IsMatch(q, rx)) return 100;
            try { foreach (Kingdom k in World.world.kingdoms) if (LiveCiv(k)) bs = Math.Max(bs, NameLookup.Score(q, KName(k))); } catch { }
            return bs;
        }

        static int BestCityScore(string q)
        {
            q = CleanTarget(q); q = Regex.Replace(q, @"^(a |o )?(cidade|vila|aldeia|capital)\s+(de |do |da )?", ""); int bs = 0;
            if (q.Length == 0) return 0;
            try { foreach (City c in World.world.cities) if (c != null && c.isAlive()) bs = Math.Max(bs, NameLookup.Score(q, c.name)); } catch { }
            return bs;
        }

        static int BestUnitScore(string q, Actor npc)
        {
            q = CleanTarget(q); int bs = 0;
            if (q.Length < 2) return 0;
            try
            {
                foreach (Actor a in World.world.units)
                    try { if (a != null && a != npc && a.isAlive()) { int s = NameLookup.Score(q, a.getName()); if (s > bs) bs = s; if (bs == 100) break; } } catch { }
            }
            catch { }
            return bs;
        }

        /// <summary>Which place-action a bare name means: "declare_war" (kingdom), "attack_city" (city) or null (a person / nothing).</summary>
        public static string PlaceKind(string target, string text, Actor npc)
        {
            string f = Fold((text ?? "") + " " + (target ?? ""));
            if (Regex.IsMatch(Fold(target ?? ""), @"^(a |o )?(cidade|vila|aldeia|capital)\b")) return "attack_city";
            if (Regex.IsMatch(Fold(target ?? ""), @"^(o |os )?(reino|imperio)\b")) return "declare_war";
            int u = BestUnitScore(target, npc), c = BestCityScore(target), k = BestKingdomScore(target);
            if (Regex.IsMatch(f, @"\b(cidade|vila|aldeia|capital)\b")) c += 15;
            if (Regex.IsMatch(f, @"\b(reino|guerra|imperio)\b")) k += 15;
            if (Math.Max(c, k) < 20 || u >= Math.Max(c, k)) return null;
            return k >= c ? "declare_war" : "attack_city";
        }

        /// <summary>v0.7.6: the classifier/shortcut said "attack X" but X is a city or a kingdom: re-route (only if offered).</summary>
        public static WorldfallActions.Request Reroute(WorldfallActions.Request req, string text, Actor npc, List<WorldfallActions.Available> avail)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Target)) return req;
            if (req.Id == "attack_city")
            {
                // "destrua a cidade de Neen" where Neen is the KINGDOM -> declare_war (with its own confirmation)
                try
                {
                    if (FindCity(req.Target, npc) == null && BestKingdomScore(req.Target) >= 45 && avail != null && avail.Any(a => a.Def?.Id == "declare_war"))
                    {
                        Main.LogInfo("[GodBox] ACTION attack_city -> declare_war (\"" + req.Target + "\" is a kingdom)");
                        return new WorldfallActions.Request { Id = "declare_war", Target = req.Target, Message = string.IsNullOrEmpty(req.Message) ? text : req.Message };
                    }
                }
                catch { }
                return req;
            }
            if (req.Id != "attack") return req;
            try
            {
                string kind = PlaceKind(req.Target, text, npc);
                if (kind == null || avail == null || !avail.Any(a => a.Def?.Id == kind)) return req;
                Main.LogInfo("[GodBox] ACTION attack -> " + kind + " (\"" + req.Target + "\" is a " + (kind == "declare_war" ? "kingdom" : "city") + ", not a person)");
                return new WorldfallActions.Request { Id = kind, Target = req.Target, Message = string.IsNullOrEmpty(req.Message) ? text : req.Message };
            }
            catch { return req; }
        }

        public static War WarBetween(Kingdom a, Kingdom b)
        {
            if (a == null || b == null) return null;
            try
            {
                foreach (War w in World.world.wars.getWars(a))
                    try { if (w != null && !w.hasEnded() && w.isInWarWith(a, b)) return w; } catch { }
            }
            catch { }
            return null;
        }

        static string WName(War w) { try { return w?.name; } catch { return null; } }

        // ------------------------------------------------------------------------------------------------
        // Precheck + terms (pending offer)
        // ------------------------------------------------------------------------------------------------
        /// <summary>null = it can really happen (offer terms); otherwise why not (then Execute runs and explains).</summary>
        static string WarPrecheck(WorldfallActions.Request req, Actor npc, Actor you)
        {
            string wb = WarBlock(npc); if (wb != null) return wb;
            if (req.Targets != null && req.Targets.Count > 1)
            {   // v0.7.8: several targets: offer when at least one can really happen
                var why = req.Targets.Select(t => WarPrecheck(Sub(req, t), npc, you)).ToList();
                return why.Any(w => w == null) ? null : string.Join("; ", why.Distinct());
            }
            Kingdom a = npc.kingdom;
            if (req.Id == "attack_city")
            {
                var c = FindCity(req.Target, npc);
                if (c == null) return "cidade não encontrada";
                if (c.kingdom == a) return c == a.capital ? "é a capital" : null;
                return c.kingdom == null ? "cidade sem reino" : null;
            }
            Kingdom b;
            if (req.Id == "make_peace" && !TryToken(req.Target, "#k:", out _))
            {
                string why = PeaceNoTarget(req.Target, npc, out Kingdom only);
                if (why != null) return why;
                if (only != null) req.Target = KToken(only);
            }
            b = FindKingdom(req.Target, npc, out string ask);
            if (b == null) return ask ?? "reino não encontrado";
            if (b == a) return "é o próprio reino";
            var w = WarBetween(a, b);
            if (req.Id == "declare_war" && w != null) return "já em guerra";
            if (req.Id == "make_peace" && w == null) return "não estão em guerra";
            return null;
        }

        public static bool NeedsSecondConfirm(WorldfallActions.Request req, Actor npc)
        {
            if (req == null || req.Id != "attack_city") return false;
            try { var c = FindCity(req.Target, npc); return c != null && c.kingdom == npc.kingdom; } catch { return false; }
        }

        static string KingdomBrief(Kingdom k)
        {
            var parts = new List<string>();
            try { parts.Add(k.getSpecies()); } catch { }
            try { if (k.king != null) parts.Add("rei " + k.king.getName()); } catch { }
            try { parts.Add(k.countCities() + " cidades"); } catch { }
            try { parts.Add(k.countTotalWarriors() + " guerreiros"); } catch { }
            return KName(k) + (parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : "");
        }

        static string WarTerms(WorldfallActions.Request req, Actor npc, Actor you, bool pt)
        {
            Kingdom a = npc.kingdom;
            if (req.Targets != null && req.Targets.Count > 1)
            {   // v0.7.8: one confirmation for every picked kingdom
                var parts = new List<string>();
                foreach (var t in req.Targets)
                {
                    var sub = Sub(req, t);
                    string why = WarPrecheck(sub, npc, you);
                    var k = FindKingdom(t, npc, out _);
                    parts.Add(why == null ? WarTerms(sub, npc, you, pt) : (pt ? KName(k) + ": nada muda (" + why + ")" : KName(k) + ": no change (" + why + ")"));
                }
                return string.Join(pt ? " | E TAMBÉM: " : " | AND: ", parts);
            }
            if (req.Id == "declare_war")
            {
                var b = FindKingdom(req.Target, npc, out _);
                string mine = ""; try { mine = KingdomBrief(a); } catch { }
                return pt ? "o reino " + mine + " DECLARA GUERRA ao reino " + KingdomBrief(b) + "; a guerra começa pelo próprio jogo (alianças entram, exércitos marcham sozinhos)"
                          : "kingdom " + KName(a) + " DECLARES WAR on " + KName(b);
            }
            if (req.Id == "make_peace")
            {
                var b = FindKingdom(req.Target, npc, out _);
                string wn = WName(WarBetween(a, b));
                return pt ? "o reino " + KName(a) + " faz as PAZES com " + KName(b) + (wn != null ? ", encerrando a guerra \"" + wn + "\"" : "")
                          : "kingdom " + KName(a) + " makes PEACE with " + KName(b);
            }
            var c = FindCity(req.Target, npc);
            if (c == null) return "";
            if (c.kingdom == a)
                return pt ? "a cidade " + c.name + " é do NOSSO PRÓPRIO reino: ela vai se REBELAR e virar um reino independente, o reino " + KName(a) + " declara guerra a ela e os guerreiros das nossas cidades marcham para atacá-la. IRREVERSÍVEL — exige DUAS confirmações"
                          : "city " + c.name + " is OUR OWN: it rebels, becomes independent, we declare war on it and march. IRREVERSIBLE — needs TWO confirmations";
            bool atWar = WarBetween(a, c.kingdom) != null;
            return pt ? (atWar ? "" : "DECLARAR GUERRA ao reino " + KingdomBrief(c.kingdom) + " e ") + "mandar os guerreiros das cidades do reino " + KName(a) + " atacarem a cidade " + c.name + " (do reino " + KName(c.kingdom) + ")"
                      : (atWar ? "" : "DECLARE WAR on " + KName(c.kingdom) + " and ") + "send our warriors against " + c.name;
        }

        // ------------------------------------------------------------------------------------------------
        // Execute
        // ------------------------------------------------------------------------------------------------
        static void Announce(Actor npc, Actor you, Kingdom a, string rumor, string toast, string note)
        {
            try { ChatWindow.Toast(toast, 8f); } catch { }
            try
            {
                var cities = new HashSet<City>();
                try { if (npc.city != null) cities.Add(npc.city); } catch { }
                try { if (a?.capital != null) cities.Add(a.capital); } catch { }
                foreach (var c in cities) Rumors.Add(c, rumor);
            }
            catch { }
            try { if (a?.king != null && a.king != npc && a.king != you && a.king.isAlive()) Rumors.Deliver(a.king, "NOTÍCIA: " + note); } catch { }
        }

        /// <summary>v0.7.8: "Ambos": the same war/peace order for each picked kingdom, one result.</summary>
        static void ForEachTarget(Actor npc, Actor you, WorldfallActions.Request req, WorldfallActions.Result res)
        {
            var reasons = new List<string>(); bool any = false;
            Kingdom mine = null; try { mine = npc.kingdom; } catch { }
            var wasAtWar = new HashSet<Kingdom>();
            if (req.Id == "make_peace") foreach (var t in req.Targets) { var k = FindKingdom(t, npc, out _); if (k != null && WarBetween(mine, k) != null) wasAtWar.Add(k); }
            foreach (var t in req.Targets)
            {
                var r = new WorldfallActions.Result { Id = res.Id, Pt = res.Pt, En = res.En };
                if (req.Id == "declare_war") DeclareWar(npc, you, t, r); else MakePeace(npc, you, t, r);
                if (r.Outcome != "feito" && req.Id == "make_peace")
                {   // one endWar can close a war several of the picked kingdoms were in
                    var k = FindKingdom(t, npc, out _);
                    if (k != null && wasAtWar.Contains(k) && WarBetween(mine, k) == null)
                    { any = true; res.Changes.Add("paz também com " + KName(k) + " (a mesma guerra terminou)"); continue; }
                }
                if (r.Outcome == "feito") { any = true; res.Changes.AddRange(r.Changes); }
                else reasons.Add(KName(FindKingdom(t, npc, out _)) + ": " + (r.Reason ?? r.Outcome));
            }
            res.Outcome = any ? "feito" : "sem_efeito";
            if (reasons.Count > 0)
            {
                if (any) res.Changes.Add("não mudou: " + string.Join("; ", reasons));
                else res.Reason = string.Join("; ", reasons);
            }
        }

        static void DeclareWar(Actor npc, Actor you, string target, WorldfallActions.Result res)
        {
            string wb = WarBlock(npc); if (wb != null) { res.Outcome = "sem_efeito"; res.Reason = wb; return; }
            Kingdom a = npc.kingdom;
            var b = FindKingdom(target, npc, out string ask);
            if (b == null) { res.Outcome = "sem_efeito"; res.Reason = ask; return; }
            if (b == a) { res.Outcome = "sem_efeito"; res.Reason = KName(a) + " é o nosso próprio reino"; return; }
            var old = WarBetween(a, b);
            if (old != null) { res.Outcome = "sem_efeito"; res.Reason = "já estamos em guerra com " + KName(b) + (WName(old) != null ? " (guerra \"" + WName(old) + "\")" : ""); return; }
            War w = World.world.diplomacy.startWar(a, b, WarTypeLibrary.normal);
            if (w == null) { res.Reason = "o jogo não abriu a guerra entre " + KName(a) + " e " + KName(b); return; }
            res.Outcome = "feito";
            string wn = WName(w);
            res.Changes.Add("guerra declarada: " + KName(a) + " contra " + KName(b) + (wn != null ? " — \"" + wn + "\"" : ""));
            try { int na = w.getAttackers().Count(), nd = w.getDefenders().Count(); if (na + nd > 2) res.Changes.Add("alianças entraram: " + na + " reino(s) atacando, " + nd + " defendendo"); } catch { }
            string by = you == null ? "por vontade dos deuses" : "por ordem do rei " + Name(you);
            Announce(npc, you, a, KName(a) + " declarou guerra a " + KName(b) + " (" + by + ")",
                "GUERRA! " + KName(a) + " declarou guerra a " + KName(b) + (wn != null ? " — \"" + wn + "\"" : ""),
                KName(a) + " declarou guerra a " + KName(b) + " " + by + (wn != null ? " (\"" + wn + "\")" : ""));
        }

        static void MakePeace(Actor npc, Actor you, string target, WorldfallActions.Result res)
        {
            string wb = WarBlock(npc); if (wb != null) { res.Outcome = "sem_efeito"; res.Reason = wb; return; }
            Kingdom a = npc.kingdom;
            if (!TryToken(target, "#k:", out _))
            {   // v0.7.9: "todos", vague words, a species or our own kingdom never reach the name lookup unresolved
                string why = PeaceNoTarget(target, npc, out Kingdom only);
                if (why != null) { res.Outcome = "sem_efeito"; res.Reason = why; return; }
                if (only != null) target = KToken(only);
            }
            var b = FindKingdom(target, npc, out string ask);
            if (b == null) { res.Outcome = "sem_efeito"; res.Reason = ask; return; }
            if (b == a) { res.Outcome = "sem_efeito"; res.Reason = KName(a) + " é o nosso próprio reino"; return; }
            var w = WarBetween(a, b);
            if (w == null) { res.Outcome = "sem_efeito"; res.Reason = "não estamos em guerra com " + KName(b); return; }
            string wn = WName(w);
            bool aMain = w.isMainAttacker(a) || w.isMainDefender(a), bMain = w.isMainAttacker(b) || w.isMainDefender(b);
            if (aMain && bMain) World.world.wars.endWar(w, WarWinner.Peace);
            else if (!aMain) w.leaveWar(a);
            else w.leaveWar(b);
            if (WarBetween(a, b) != null) { res.Reason = "o jogo não encerrou a guerra"; return; }
            res.Outcome = "feito";
            res.Changes.Add("paz feita entre " + KName(a) + " e " + KName(b) + (wn != null ? " (fim de \"" + wn + "\")" : "") + (aMain && bMain ? "" : "; só o nosso lado saiu da guerra, ela pode continuar entre outros reinos"));
            string by = you == null ? "por vontade dos deuses" : "por ordem do rei " + Name(you);
            Announce(npc, you, a, KName(a) + " fez as pazes com " + KName(b) + " (" + by + ")",
                "PAZ: " + KName(a) + " e " + KName(b) + (wn != null ? " encerraram a guerra \"" + wn + "\"" : " fizeram as pazes"),
                KName(a) + " fez as pazes com " + KName(b) + " " + by);
        }

        static int SendArmies(Kingdom a, City target, List<string> from)
        {
            int n = 0;
            foreach (City mc in a.getCities().ToList())
            {
                try
                {
                    if (mc == null || !mc.isAlive() || !mc.hasAnyWarriors() || !target.reachableFrom(mc)) continue;
                    TileZone z = null;
                    if (target.buildings.Count > 0) z = target.buildings[UnityEngine.Random.Range(0, target.buildings.Count)].current_tile.zone;
                    else if (target.zones.Count > 0) z = target.zones[UnityEngine.Random.Range(0, target.zones.Count)];
                    if (z == null) continue;
                    mc.target_attack_city = target; mc.target_attack_zone = z;   // read by the city AI (CityBehCheckAttackZone)
                    n++; if (from.Count < 5) from.Add(mc.name);
                }
                catch { }
            }
            return n;
        }

        static void AttackCity(Actor npc, Actor you, WorldfallActions.Request req, WorldfallActions.Result res)
        {
            string wb = WarBlock(npc); if (wb != null) { res.Outcome = "sem_efeito"; res.Reason = wb; return; }
            Kingdom a = npc.kingdom;
            var c = FindCity(req.Target, npc);
            if (c == null)
            {
                if (BestKingdomScore(req.Target) >= 45) { res.Outcome = "sem_efeito"; res.Reason = "\"" + req.Target + "\" é um REINO, não uma cidade (para isso é preciso declarar guerra ao reino)"; return; }
                res.Reason = string.IsNullOrWhiteSpace(req.Target) ? "não disse qual cidade" : "não existe cidade chamada \"" + req.Target + "\" neste mundo"; return;
            }
            Kingdom b = c.kingdom;
            if (b == null) { res.Reason = "a cidade " + c.name + " não pertence a nenhum reino"; return; }
            string by = you == null ? "por vontade dos deuses" : "por ordem do rei " + Name(you);
            if (b == a)
            {
                if (you != null) { res.Outcome = "recusado"; res.Reason = "só os deuses podem mandar atacar uma cidade do nosso próprio reino"; return; }
                if (c == a.capital) { res.Outcome = "recusado"; res.Reason = c.name + " é a capital do reino; ela não pode se rebelar contra si mesma"; return; }
                if (req.Confirms < 2) { res.Outcome = "previa"; res.Reason = "precisa da SEGUNDA confirmação"; return; }
                Actor lead = null;
                try { lead = c.leader; } catch { }
                if (lead == null || !lead.isAlive()) try { lead = c.units.FirstOrDefault(u => u != null && u.isAlive() && u.isAdult() && !u.isKing()); } catch { }
                if (lead == null) { res.Reason = "a cidade " + c.name + " não tem ninguém para liderar a revolta"; return; }
                Kingdom nk = c.makeOwnKingdom(lead, true);
                if (nk == null) { res.Reason = "o jogo não separou a cidade"; return; }
                try { lead.joinCity(c); } catch { }
                War rw = World.world.diplomacy.startWar(a, nk, WarTypeLibrary.rebellion);
                res.Changes.Add("a cidade " + c.name + " se rebelou e virou o reino independente " + KName(nk) + " (líder " + Name(lead) + ")");
                if (rw != null) res.Changes.Add("guerra de rebelião: " + KName(a) + " contra " + KName(nk) + (WName(rw) != null ? " — \"" + WName(rw) + "\"" : ""));
                b = nk;
            }
            else if (WarBetween(a, b) == null)
            {
                War w = World.world.diplomacy.startWar(a, b, WarTypeLibrary.normal);
                if (w == null) { res.Reason = "o jogo não abriu a guerra contra " + KName(b); return; }
                res.Changes.Add("guerra declarada: " + KName(a) + " contra " + KName(b) + (WName(w) != null ? " — \"" + WName(w) + "\"" : ""));
            }
            var from = new List<string>();
            int n = SendArmies(a, c, from);
            if (n > 0) res.Changes.Add("guerreiros de " + n + " cidade(s) (" + string.Join(", ", from) + (n > from.Count ? "..." : "") + ") marcham contra " + c.name + " (" + FactsBuilder.Where(npc.current_tile, c.getTile(), true) + ")");
            else res.Changes.Add("nenhuma cidade nossa tem guerreiros que alcancem " + c.name + " agora; a guerra segue pelo próprio jogo");
            res.Outcome = res.Changes.Count > 0 && (n > 0 || res.Changes.Count > 1) ? "feito" : "falhou";
            if (res.Outcome == "falhou") { res.Reason = "nenhum guerreiro do reino alcança " + c.name; return; }
            Announce(npc, you, a, KName(a) + " mandou o exército contra " + c.name + " (" + by + ")",
                "ATAQUE! " + KName(a) + " marcha contra " + c.name + (n > 0 ? " (" + n + " cidade(s) enviam guerreiros)" : ""),
                KName(a) + " mandou o exército contra " + c.name + " " + by + "; " + string.Join("; ", res.Changes));
        }

        // ------------------------------------------------------------------------------------------------
        // Shortcuts (deterministic, before the classifier)
        // ------------------------------------------------------------------------------------------------
        const string Name_ = @"([\p{L}'][\p{L}' -]*?)\s*[.!]*$";
        static readonly Regex PeaceRx = new Regex(
            @"\b(?:faca|facam|faz|fazer|sele|selem|assine|assinem|negocie|negociem|quero|ordeno|declare|declarem|busque|busquem)\b[^.?!]*\b(?:paz|pazes)\b\s*(?:com|a|ao|aos|o|os)?\s*" + Name_ + "|" +
            @"\b(?:acabe|acabem|termine|terminem|encerre|encerrem|pare|parem|ponha fim|ponham fim)\b\s+(?:(?:a |as |com a |com as |com |todas as |com todas as |as )?)guerras?\s*(?:com|contra|a|ao|aos|o|os|de|do|dos)?\s*" + Name_ + "|" +
            @"\b(?:acabe|acabem|termine|terminem|encerre|encerrem)\b\s+(?:com )?(?:a |as |todas as )guerras?\s*[.!]*$", RegexOptions.Compiled);
        static readonly Regex WarRx = new Regex(
            @"\b(?:inicie|iniciem|inicia|comece|comecem|comeca|declare|declarem|declara|faca|facam|faz|entre|entrem|abra|abram|trave|travem|leve|levem|quero|ordeno|mova|movam|travar|declarar|iniciar|comecar)\b[^.?!]*\bguerra\b\s*(?:contra|com|a|ao|aos|as|o|os|de|do|dos|da|das|em)?\s*" + Name_, RegexOptions.Compiled);
        static readonly Regex KingdomAttackRx = new Regex(
            @"\b(?:ataque|ataquem|invada|invadam|conquiste|conquistem|destrua|destruam)\s+(?:o |os )?(?:(?:reino|imperio)\s+(?:de |do |da |dos )?([\p{L}'][\p{L}' -]*?)|(orcs?|elfos?|humanos?|anoes|anao|dwarves|elves|humans))\s*[.!]*$", RegexOptions.Compiled);
        static readonly Regex CityAttackRx = new Regex(
            @"\b(?:destrua|destruam|ataque|ataquem|invada|invadam|conquiste|conquistem|saqueie|saqueiem|arrase|arrasem|tome|tomem|cerque|cerquem)\s+(?:a |o )?(cidade|vila|aldeia|capital)?\s*(?:de |do |da )?" + Name_, RegexOptions.Compiled);

        /// <summary>War / peace / city attack from the raw text, or null.</summary>
        static WorldfallActions.Request WarShortcut(string f, string text, Actor npc, List<WorldfallActions.Available> avail)
        {
            if (f.TrimEnd().EndsWith("?")) return null;
            bool Has(string id) => avail != null && avail.Any(a => a.Def?.Id == id);
            WorldfallActions.Request R(string id, string t) => new WorldfallActions.Request { Id = id, Target = t.Trim(), Message = text };
            Match m;
            if (Has("make_peace") && (m = PeaceRx.Match(f)).Success)
                return R("make_peace", m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value);
            if (Has("declare_war") && (m = WarRx.Match(f)).Success) return R("declare_war", m.Groups[1].Value);
            if (Has("declare_war") && (m = KingdomAttackRx.Match(f)).Success)
                return R("declare_war", m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value);
            if ((m = CityAttackRx.Match(f)).Success)
            {
                string t = m.Groups[2].Value;
                if (m.Groups[1].Success && m.Groups[1].Value.Length > 0) return Has("attack_city") ? R("attack_city", t) : null;
                string kind = PlaceKind(t, f, npc);   // "destrua naola" (city) / "invada neen" (kingdom) / "ataque o Raar" (person: null)
                if (kind != null && Has(kind)) return R(kind, t);
            }
            return null;
        }
    }
}
