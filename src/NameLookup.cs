using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.5: whenever the player's message names a person, city or kingdom ("conhece o Pedro?", "onde está X?",
    /// "me leve até X", or just a name in the sentence), look it up in the live world (accent-insensitive, fuzzy)
    /// and hand the NPC the real answer, so it never denies someone who exists.
    /// </summary>
    public static class NameLookup
    {
        static readonly HashSet<string> Stop = new HashSet<string>(@"
a o e é as os um uma uns umas de da do das dos em no na nos nas ao aos à às para pra pro por pelo pela com sem
que quem qual quais quando onde como porque porquê se sim não nao nem mas ou já ja muito muita pouco mais menos
eu tu ele ela nós nos vós eles elas você voce vocês voces me te se lhe meu minha meus minhas teu tua seu sua
seus suas nosso nossa dele dela isso isto aquilo esse essa este esta aquele aquela aqui ali lá la cá ca hoje agora
ser estar ter ir vir fazer dizer saber ver dar ficar poder querer está esta estão estao foi era sou somos são sao
tem tenho temos vai vou vamos pode posso quero sabe sei conhece conheço conheco onde leve leva levar guie guia
mostre mostra chame chama traga traz ataque ataca fale falar diga ache encontre procure procura cadê cade até ate
deus deuses rei rainha lider líder cidade vila reino reinos aldeia capital povo gente pessoa pessoas alguém alguem
ninguém ninguem todo todos toda todas tudo nada coisa coisas bom boa mal bem obrigado oi olá ola tchau favor
sobre depois antes ainda também tambem então entao assim aqui vida morte guerra paz casa terra mundo nome
filho filha pai mãe mae irmão irmao irmã irma amigo amiga inimigo homem mulher criança crianca
the and you who where is are king queen city village kingdom take me to know".Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NativeActions.Fold));

        // explicit asks; group 1 = the name (one or two words)
        static readonly Regex AskRx = new Regex(
            @"\b(?:conhece[s]?|conheceu|conhecia|sabe quem (?:e|foi)|quem (?:e|foi)|onde (?:esta|estao|fica|mora|anda|vive|foi parar)|cade|" +
            @"(?:me )?(?:leve|leva|levar|levem|guie|guia|mostre|mostra|mostrar|leve-me|leva-me)(?: me)? (?:ate|para|pra|pro|ao|a)|" +
            @"noticias? d[eoa]s?|ouviu falar (?:d[eoa]s?|em)|viu (?:o|a)|chame|chama|traga|traz|ataque|ataca|fale com|falar com|procure|procura|ache|encontre|" +
            @"where is|who is|do you know|take me to)\s+(?:a cidade de |a vila de |o reino de |cidade de |vila de |reino de |senhor |senhora |o |a |os |as |ao )?" +
            @"([\p{L}'-]{2,}(?:\s+(?:d[aeo]s?\s+)?[\p{L}'-]{2,})?)", RegexOptions.CultureInvariant);

        class Hit { public string Query; public List<Actor> Units = new List<Actor>(); public City City; public Kingdom Kingdom; public bool Explicit; public List<Deaths.Rec> Dead = new List<Deaths.Rec>(); }

        public static string Describe(string text, Actor npc, Actor you, bool pt)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                string f = NativeActions.Fold(text);
                // ---- index of the live world ----
                var byFull = new Dictionary<string, List<Actor>>();
                var byFirst = new Dictionary<string, List<Actor>>();
                foreach (Actor a in World.world.units)
                {
                    try
                    {
                        if (a == null || !a.isAlive()) continue;
                        string n = NativeActions.Fold(a.getName());
                        if (n.Length < 2) continue;
                        Add(byFull, n, a);
                        string first = n.Split(' ')[0];
                        if (first != n) Add(byFirst, first, a);
                    }
                    catch { }
                }
                var cities = new Dictionary<string, City>();
                foreach (City c in World.world.cities) try { if (c != null && c.isAlive()) cities[NativeActions.Fold(c.name)] = c; } catch { }
                var kingdoms = new Dictionary<string, Kingdom>();
                foreach (Kingdom k in World.world.kingdoms) try { if (k != null && k.isAlive() && k.isCiv()) kingdoms[NativeActions.Fold(k.name)] = k; } catch { }

                var hits = new List<Hit>();
                var seen = new HashSet<string>();
                Hit Find(string q, bool fuzzy)
                {
                    var h = new Hit { Query = q };
                    if (byFull.TryGetValue(q, out var l)) h.Units.AddRange(l);
                    if (byFirst.TryGetValue(q, out l)) h.Units.AddRange(l);
                    cities.TryGetValue(q, out h.City);
                    kingdoms.TryGetValue(q, out h.Kingdom);
                    // v0.7.6: a single word of a longer place name ("Naola" -> "Naola Raadi")
                    if (h.City == null && q.Length >= 3) h.City = cities.Where(kv => Score(q, kv.Key) >= 70).Select(kv => kv.Value).FirstOrDefault();
                    if (h.Kingdom == null && q.Length >= 3) h.Kingdom = kingdoms.Where(kv => Score(q, kv.Key) >= 70).Select(kv => kv.Value).FirstOrDefault();
                    if (fuzzy && h.Units.Count == 0 && h.City == null && h.Kingdom == null && q.Length >= 4)
                    {
                        int tol = q.Length >= 8 ? 2 : 1;
                        foreach (var kv in byFull) if (Lev(kv.Key, q) <= tol) h.Units.AddRange(kv.Value);
                        if (h.Units.Count == 0) foreach (var kv in byFirst) if (Lev(kv.Key, q) <= tol) h.Units.AddRange(kv.Value);
                        if (h.Units.Count == 0)
                        {
                            h.City = cities.Where(kv => Lev(kv.Key, q) <= tol).Select(kv => kv.Value).FirstOrDefault();
                            h.Kingdom = kingdoms.Where(kv => Lev(kv.Key, q) <= tol).Select(kv => kv.Value).FirstOrDefault();
                        }
                    }
                    h.Units = h.Units.Distinct().Where(u => u != npc).ToList();
                    // v0.7.6: the death chronicle (people who died); exact name or first name, fuzzy if nothing alive matched
                    try
                    {
                        var alive = new HashSet<long>(h.Units.Select(u => { try { return u.getID(); } catch { return -1L; } }));
                        h.Dead = Deaths.Find(q, h.Units.Count == 0 && fuzzy ? 60 : 70).Where(r => !alive.Contains(r.Id)).Take(3).ToList();
                    }
                    catch { }
                    return h;
                }
                bool Any(Hit h) => h.Units.Count > 0 || h.City != null || h.Kingdom != null || h.Dead.Count > 0;

                // 1) explicit asks: always answered (found or "does not exist")
                foreach (Match m in AskRx.Matches(f))
                {
                    string q2 = m.Groups[1].Value.Trim();
                    string q1 = q2.Split(' ')[0];
                    if ((Stop.Contains(q1) || SafetyRules.IsCommonWord(q1)) && !q2.Contains(" ")) continue;
                    Hit h = null;
                    if (q2 != q1) { h = Find(q2, false); if (!Any(h)) h = null; }
                    if (h == null)
                    {
                        if ((Stop.Contains(q1) || SafetyRules.IsCommonWord(q1))) continue;
                        h = Find(q1, true);
                    }
                    if (seen.Contains(h.Query)) continue;
                    seen.Add(h.Query); h.Explicit = true; hits.Add(h);
                }
                // 2) any other word (or pair of words) that is exactly a name in the world
                var words = Regex.Matches(f, @"[\p{L}'-]+").Cast<Match>().Select(x => x.Value).ToList();
                for (int i = 0; i < words.Count && hits.Count < 4; i++)
                {
                    string w = words[i];
                    if (i + 1 < words.Count)
                    {
                        string bi = w + " " + words[i + 1];
                        if (!seen.Contains(bi) && (byFull.ContainsKey(bi) || cities.ContainsKey(bi) || kingdoms.ContainsKey(bi)))
                        {
                            var hb = Find(bi, false); if (Any(hb)) { seen.Add(bi); seen.Add(w); hits.Add(hb); i++; continue; }
                        }
                    }
                    if (w.Length < 3 || (Stop.Contains(w) || SafetyRules.IsCommonWord(w)) || seen.Contains(w)) continue;
                    var h = Find(w, false);
                    h.Units.RemoveAll(u => u == you);
                    if (!Any(h)) continue;
                    seen.Add(w); hits.Add(h);
                }
                if (hits.Count == 0) return null;

                var sb = new StringBuilder();
                sb.AppendLine(pt ? "BUSCA DE NOMES (feita agora no jogo; é a verdade sobre quem/o que existe):"
                                 : "NAME LOOKUP (done now in the game; this is the truth about who/what exists):");
                foreach (var h in hits.Take(4)) sb.AppendLine("- " + Line(h, npc, you, pt));
                sb.Append(pt
                    ? "Use isso: se a pessoa/lugar EXISTE, NÃO diga que não conhece ou que não existe; diga o que sabe (de perto, ou \"ouvi dizer\" se for só boato). Só diga que nunca ouviu falar se a busca disser que ninguém com esse nome existe. Não invente detalhes além disso."
                    : "Use it: if the person/place EXISTS do NOT deny knowing it; say what you know (or 'I heard' for rumours). Only say you never heard of it if the lookup says nobody with that name exists. Invent nothing beyond this.");
                return sb.ToString();
            }
            catch (Exception e) { Main.LogWarning("[GodBox] name lookup failed: " + e.Message); return null; }
        }

        static void Add(Dictionary<string, List<Actor>> d, string k, Actor a)
        {
            if (!d.TryGetValue(k, out var l)) d[k] = l = new List<Actor>();
            l.Add(a);
        }

        static string Line(Hit h, Actor npc, Actor you, bool pt)
        {
            WorldTile from = null; try { from = npc.current_tile; } catch { }
            Kingdom mine = null; try { mine = npc.kingdom; } catch { }
            var parts = new List<string>();
            if (h.Units.Count > 0)
            {
                // v0.7.6: several units can share a name (5 "Onno"): prefer the most relevant, mention the others
                City myCity = null; try { myCity = npc.city; } catch { }
                int Rel(Actor x)
                {
                    int s = 0;
                    try { if (x == npc.lover) s += 50; } catch { }
                    try { if (x.city != null && x.city == myCity) s += 30; } catch { }
                    try { if (x.kingdom != null && x.kingdom == mine) s += 10; } catch { }
                    try { if (x.isKing() || x.isCityLeader()) s += 15; } catch { }
                    return s;
                }
                var ordered = h.Units.OrderByDescending(Rel).ThenBy(x => D(from, Tile(x))).ToList();
                var u = ordered[0];
                string others = "";
                if (ordered.Count > 1)
                {
                    var o = ordered.Skip(1).Take(3).Select(x => { string c = null; try { c = x.city?.name ?? x.kingdom?.name; } catch { } return c != null ? (pt ? "um(a) de " : "one from ") + c : (pt ? "um(a) sem cidade" : "one with no city"); });
                    others = pt ? $" (há {ordered.Count} pessoas com esse nome; esta é a mais provável para você; outras: {string.Join(", ", o)}{(ordered.Count > 4 ? "…" : "")}. Se não estiver claro qual, pergunte ao jogador)"
                                : $" ({ordered.Count} people share this name; this is the most likely one; others: {string.Join(", ", o)}. Ask which one if unclear)";
                }
                parts.Add(Unit(u, from, mine, pt) + others);
            }
            foreach (var r in h.Dead)
                parts.Add((h.Units.Count > 0 ? (pt ? "outra pessoa com esse nome já morreu: " : "another person by that name died: ") : "") + Deaths.Describe(r, npc, pt));
            if (h.City != null)
            {
                var c = h.City; string k = null, rel = null; WorldTile t = null;
                try { k = c.kingdom?.name; rel = FactsBuilder.Relation(mine, c.kingdom, pt); t = c.getTile(); } catch { }
                string loc = Loc(from, t, D(from, t) <= 150, pt);
                parts.Add((pt ? $"{c.name} é uma CIDADE que existe" : $"{c.name} is an existing CITY") + (k != null ? (pt ? $" do reino {k}" : $" of {k}") : "") + (rel != null ? $" ({rel})" : "") + (loc != null ? "; " + loc : ""));
            }
            if (h.Kingdom != null)
            {
                var k = h.Kingdom; string cap = null, rel = null, king = null;
                try { cap = k.capital?.name; rel = FactsBuilder.Relation(mine, k, pt); king = k.king?.getName(); } catch { }
                parts.Add((pt ? $"{k.name} é um REINO que existe" : $"{k.name} is an existing KINGDOM") + (rel != null ? $" ({rel})" : "") + (cap != null ? (pt ? $", capital {cap}" : $", capital {cap}") : "") + (king != null ? (pt ? $", rei {king}" : $", king {king}") : ""));
            }
            if (parts.Count == 0)
                return pt ? $"\"{h.Query}\": ninguém com esse nome existe (nenhuma pessoa viva, cidade ou reino com esse nome neste mundo, e nenhuma morte conhecida)"
                          : $"\"{h.Query}\": nobody with that name exists (no living person, city or kingdom by that name)";
            return string.Join(pt ? "; também: " : "; also: ", parts);
        }

        static string Unit(Actor u, WorldTile from, Kingdom mine, bool pt)
        {
            string name = "?", sp = null, role = null, city = null, kname = null, rel = null;
            bool king = false;
            try { name = u.getName(); } catch { }
            try { sp = FactsBuilder.Species(u); } catch { }
            try { role = FactsBuilder.Role(u, pt); } catch { }
            try { city = u.city?.name; } catch { }
            try { kname = u.kingdom?.name; rel = FactsBuilder.Relation(mine, u.kingdom, pt); } catch { }
            try { king = u.isKing(); } catch { }
            WorldTile t = Tile(u);
            float d = D(from, t);
            bool exact = king || d <= 60f;
            var sb = new StringBuilder();
            sb.Append(pt ? $"{name} EXISTE e está vivo(a)" : $"{name} EXISTS and is alive");
            var tags = new List<string>(); if (sp != null) tags.Add(sp); if (role != null) tags.Add(role);
            try { var nt = Alignment.Tag(u, pt); if (nt != null) tags.Add(nt); } catch { }   // v0.7.7
            if (tags.Count > 0) sb.Append(" (").Append(string.Join(", ", tags)).Append(")");
            sb.Append(city != null ? (pt ? $"; mora em {city}" : $"; lives in {city}") : (pt ? "; não mora em cidade" : "; no city"));
            if (kname != null) sb.Append(pt ? $", reino {kname}" : $", kingdom {kname}").Append(rel != null ? $" ({rel})" : "");
            string loc = Loc(from, t, exact, pt);
            if (loc != null) sb.Append("; ").Append(loc);
            return sb.ToString();
        }

        static string Loc(WorldTile from, WorldTile t, bool exact, bool pt)
        {
            if (from == null || t == null) return null;
            if (exact) return (pt ? "fica a: " : "is: ") + FactsBuilder.Where(from, t, pt) + (pt ? " de você" : " from you");
            float dx = t.x - from.x, dy = t.y - from.y;
            int dist = (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
            return (pt ? "longe; você só sabe por boatos: " : "far away; you only know by rumour: ") + FactsBuilder.FuzzyDir(dx, dy, pt) + ", " + FactsBuilder.FuzzyDist(dist, pt);
        }

        static WorldTile Tile(Actor a) { try { return a.current_tile; } catch { return null; } }
        static float D(WorldTile a, WorldTile b)
        {
            if (a == null || b == null) return 99999f;
            float dx = a.x - b.x, dy = a.y - b.y; return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>v0.7.6: one fuzzy matcher for every action target (units, cities, kingdoms). Both strings are
        /// accent-folded. 100 exact, 85 the name starts with the whole query as words ("naola" -> "Naola Raadi"),
        /// 70 a word of the name equals the query, 60 plain prefix, 45 a word starts with it, 30 contains,
        /// 20 one/two typos on the whole name or on a word. 0 = no match.</summary>
        public static int Score(string q, string name)
        {
            q = NativeActions.Fold(q); name = NativeActions.Fold(name);
            if (q.Length == 0 || name.Length == 0) return 0;
            if (name == q) return 100;
            if (name.StartsWith(q + " ")) return 85;
            var words = name.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Contains(q)) return 70;
            if (q.Length >= 3 && name.StartsWith(q)) return 60;
            if (q.Length >= 3 && words.Any(w => w.StartsWith(q))) return 45;
            if (q.Length >= 4 && name.Contains(q)) return 30;
            if (q.Length >= 4)
            {
                int tol = q.Length >= 8 ? 2 : 1;
                if (Lev(name, q) <= tol || words.Any(w => w.Length >= 4 && Lev(w, q) <= tol)) return 20;
            }
            return 0;
        }

        static int Lev(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 2) return 99;
            var p = new int[b.Length + 1]; var c = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) p[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                c[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    c[j] = Math.Min(Math.Min(c[j - 1] + 1, p[j] + 1), p[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                var tmp = p; p = c; c = tmp;
            }
            return p[b.Length];
        }
    }
}
