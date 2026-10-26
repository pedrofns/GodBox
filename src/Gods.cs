using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.7.6 (E): every religion knows the god by its own proper name. Stored per world in memory/&lt;world&gt;/_gods.json:
    /// religion id -> god name (+ optional epithet), year, how it was named. The default name is made on first sight with the
    /// game's own NameGenerator for a follower (culture / species onomastics, seeded by the religion id); if that is not
    /// usable, a deterministic syllable generator flavoured by species. The god voice can rename itself for the listener's
    /// religion ("me chamem de X", "meu nome é X", "seu deus se chama X"). The religion's own name (e.g. "Pedro") is kept
    /// separate from the god's name.
    /// </summary>
    public static class Gods
    {
        public class Rec
        {
            [JsonProperty("religion")] public string Religion;
            [JsonProperty("name")] public string Name;
            [JsonProperty("epithet")] public string Epithet;
            [JsonProperty("year")] public int Year;
            [JsonProperty("how")] public string How;
            [JsonProperty("old_name")] public string OldName;   // v0.7.8: "antigo nome"
        }
        class Store { [JsonProperty("gods")] public Dictionary<string, Rec> Map = new Dictionary<string, Rec>(); }
        static readonly Dictionary<string, Store> Cache = new Dictionary<string, Store>();

        static string PathFor(string world) => Path.Combine(MemoryStore.Root, "memory", world, "_gods.json");

        static Store Get()
        {
            string world = MemoryStore.CurrentWorldKey();
            if (Cache.TryGetValue(world, out var s)) return s;
            try { var p = PathFor(world); if (File.Exists(p)) s = JsonConvert.DeserializeObject<Store>(File.ReadAllText(p)); } catch (Exception e) { Main.LogWarning("[GodBox] gods file unreadable: " + e.Message); }
            s ??= new Store(); s.Map ??= new Dictionary<string, Rec>();
            Cache[world] = s; return s;
        }

        static void Save()
        {
            try
            {
                string p = PathFor(MemoryStore.CurrentWorldKey());
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonConvert.SerializeObject(Get(), Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not save gods: " + e.Message); }
        }

        static string RelName(Religion r) { try { return r.name; } catch { return "?"; } }
        static bool Ok(Religion r) { try { return r != null && r.isAlive(); } catch { return false; } }

        /// <summary>The god record of a religion (created with a default name on first sight). null if no religion.</summary>
        public static Rec For(Religion r, Actor sample = null)
        {
            if (!Ok(r)) return null;
            try
            {
                var s = Get(); string key = r.getID().ToString();
                if (s.Map.TryGetValue(key, out var g) && !string.IsNullOrEmpty(g.Name)) { g.Religion = RelName(r); return g; }
                g = new Rec { Religion = RelName(r), Name = DefaultName(r, sample), Year = Main.CurrentYear(), How = DefaultHow };
                s.Map[key] = g; Save();
                Main.LogInfo("[GodBox] GOD NAME: religion " + g.Religion + " calls its god " + g.Name);
                return g;
            }
            catch { return null; }
        }

        public static string NameOf(Religion r, Actor sample = null) => For(r, sample)?.Name;

        /// <summary>v0.7.9: set the god's name of a religion (Religião de Deus template).</summary>
        public static void SetName(Religion r, string name, string how)
        {
            if (!Ok(r) || string.IsNullOrWhiteSpace(name)) return;
            var g = For(r); if (g == null || g.Name == name) return;
            g.OldName = g.Name; g.Name = name; g.How = how; g.Year = Main.CurrentYear();
            Save();
            Main.LogInfo("[GodBox] GOD NAME: religion " + RelName(r) + " now calls its god " + name + " (" + how + ")");
        }
        public static string Full(Rec g) => g == null ? null : g.Name + (string.IsNullOrEmpty(g.Epithet) ? "" : ", " + g.Epithet);

        const string DefaultHow = "nome tradicional (padrão)";

        /// <summary>v0.8.2 (A): God's OWN name, the one of God's religion (Religião de Deus): the template's god name, or a name
        /// God revealed to that religion. Never the default name the game/mod rolled for an existing religion. null = unknown.</summary>
        public static string GodName()
        {
            try
            {
                var t = GodFaith.Current();
                if (t != null && !string.IsNullOrWhiteSpace(t.GodName)) return t.GodName.Trim();
                var rel = GodPowers.CurrentReligion();
                if (!Ok(rel)) return null;
                if (Get().Map.TryGetValue(rel.getID().ToString(), out var g) && !string.IsNullOrEmpty(g.Name) && g.How != DefaultHow) return g.Name;
            }
            catch { }
            return null;
        }

        /// <summary>v0.8.2 (A): the listener follows God's religion.</summary>
        public static bool IsAdherent(Actor a)
        {
            try { var rel = GodPowers.CurrentReligion(); return Ok(rel) && a != null && a.hasReligion() && a.religion == rel; } catch { return false; }
        }

        /// <summary>The name of God (the voice / the Avatar) as this listener knows it, or null when it does not know it.
        /// v0.8.2 (A): only followers of God's religion know it (by that religion's god name); anyone else knows only a name
        /// God told it in person (memory). Other religions' gods are OTHER gods: never God's name.</summary>
        public static string NameFor(Actor listener)
        {
            try
            {
                if (listener == null) return null;
                if (IsAdherent(listener)) { var n = GodName(); if (n != null) return n; }
                var m = MemoryStore.Get(listener);
                return string.IsNullOrEmpty(m?.GodName) ? null : m.GodName;
            }
            catch { return null; }
        }

        /// <summary>v0.8.2 (A): this listener knows who the voice is (follower of God's religion, holy being, or God told it his name).</summary>
        public static bool KnowsGod(Actor listener)
        {
            if (listener == null) return false;
            if (IsAdherent(listener)) return true;
            try { if (Alignment.IsHoly(listener) || Alignment.IsMalefic(listener)) return true; } catch { }   // holy beings serve him; malefic ones know and hate him
            return NameFor(listener) != null;
        }

        /// <summary>Chat/prompt label of the god voice: "X (voz do céu)".</summary>
        public static string Label(Actor listener, bool pt)
        {
            string n = NameFor(listener);   // v0.8.2: only the name this listener really knows (never another religion's god)
            return (n ?? (pt ? "Deus" : "God")) + Avatar.Suffix(pt);   // v0.7.9: " (em pessoa)" while the Avatar is controlled
        }

        /// <summary>v0.8.2 (E): the traditional god name of a religion, only if the mod already has one in _gods.json (never creates one).</summary>
        public static string KnownTraditional(Religion r)
        {
            try { if (r != null && Get().Map.TryGetValue(r.getID().ToString(), out var g) && !string.IsNullOrEmpty(g.Name)) return g.Name; } catch { }
            return null;
        }

        // ---------------- default names ----------------
        static string DefaultName(Religion r, Actor sample)
        {
            long seed = 0; try { seed = r.getID(); } catch { }
            string rn = NativeActions.Fold(RelName(r));
            if (sample != null)
            {
                try
                {   // the game's own generator, culture/species onomastics, seeded by the religion id
                    for (int i = 0; i < 3; i++)
                    {
                        string n = NameGenerator.generateName(sample, MetaType.Unit, seed * 7919 + 104729 + i, ActorSex.Male);
                        n = (n ?? "").Trim();
                        if (Regex.IsMatch(n, @"^[\p{L}'-]{3,12}$") && NativeActions.Fold(n) != rn && !NameInUse(n)) return n;
                    }
                }
                catch { }
            }
            string sp = null; try { sp = sample?.kingdom?.getSpecies() ?? sample?.asset?.id; } catch { }
            return Syllables(sp, seed, rn);
        }

        static bool NameInUse(string n)
        {
            string f = NativeActions.Fold(n);
            try { foreach (Actor a in World.world.units) if (a != null && a.isAlive() && NativeActions.Fold(a.getName()) == f) return true; } catch { }
            return false;
        }

        static readonly Dictionary<string, (string[] a, string[] b, string[] c)> Flavour = new Dictionary<string, (string[], string[], string[])>
        {
            { "elf",   (new[] { "Ae", "E", "Ila", "Lu", "Sae", "Thi", "Ny", "Ela" }, new[] { "lan", "ri", "the", "lia", "va", "ren", "si", "nae" }, new[] { "dil", "el", "wen", "riel", "lys", "thas", "ia", "nor" }) },
            { "orc",   (new[] { "Gor", "Kra", "Ugg", "Mag", "Zug", "Thra", "Bur", "Dra" }, new[] { "ash", "gu", "ruk", "zog", "ka", "gra", "th", "mo" }, new[] { "nak", "rok", "gul", "dush", "kar", "zag", "ur", "gh" }) },
            { "dwarf", (new[] { "Dur", "Bal", "Thor", "Gro", "Kaz", "Bor", "Mor", "Hal" }, new[] { "in", "a", "un", "go", "ra", "dra", "o", "ga" }, new[] { "din", "rim", "grim", "dum", "nar", "bek", "dain", "mund" }) },
            { "human", (new[] { "Au", "Va", "Se", "Ma", "Lu", "Ce", "Ju", "Ter" }, new[] { "re", "le", "ri", "ra", "ve", "ci", "na", "mi" }, new[] { "lius", "nus", "tor", "ris", "o", "ius", "ra", "an" }) },
        };

        static string Syllables(string species, long seed, string avoid)
        {
            string key = species != null && Flavour.ContainsKey(species) ? species : "human";
            var f = Flavour[key];
            var rng = new System.Random(unchecked((int)(seed * 2654435761L % int.MaxValue)) ^ key.GetHashCode());
            for (int i = 0; i < 5; i++)
            {
                string n = f.a[rng.Next(f.a.Length)] + (rng.Next(3) == 0 ? "" : f.b[rng.Next(f.b.Length)]) + f.c[rng.Next(f.c.Length)];
                if (NativeActions.Fold(n) != avoid) return n;
            }
            return "Aurelius";
        }

        // ---------------- god-voice rename (v0.7.8: set_god_name) ----------------
        // "me chame de X (a partir de hoje)", "meu nome agora é X", "sou X", "chamem-me X", "seu deus se chama X"
        public static string ParseGodName(string text, out string epithet) => SafetyRules.ParseGodName(text, out epithet);
        public static bool AboutSpeakerName(string text) => SafetyRules.AboutSpeakerName(text);

        /// <summary>v0.7.8 set_god_name. Only for the god voice (you == null). Returns a system note if the message named the god, else null.
        /// A revelation: the whole religion adopts the name (even when the listener is malefic, who mocks it and refuses to use it).</summary>
        public static string TryRename(string text, Actor listener, bool pt, UnitMemory mem = null)
        {
            if (listener == null) return null;
            string name = ParseGodName(text, out string epithet);
            if (name == null) return null;
            bool malefic = GodForm.Hostile(listener);
            int year = Main.CurrentYear();
            Religion r = null; try { if (IsAdherent(listener)) r = listener.religion; } catch { }   // v0.8.2 (A): only God's own religion takes the name
            Religion other = null; try { if (r == null && listener.hasReligion()) other = listener.religion; } catch { }
            if (!Ok(r))
            {   // not a follower of God's religion: only this unit remembers the name (its own religion's god is another god)
                mem ??= MemoryStore.Get(listener);
                string oldP = mem?.GodName;
                if (mem != null) { mem.GodNameOld = oldP; mem.GodName = name; mem.GodNameYear = year; MemoryStore.Save(mem); }
                Main.LogInfo("[GodBox] ACTION set_god_name: " + SafeName(listener) + (Ok(other) ? " follows " + RelName(other) + " (not God's religion)" : " has no religion") + "; the name " + name + " is kept only in its memory" + (malefic ? " (malefic: mocks it)" : "") + ".");
                try { ChatWindow.Toast(SafeName(listener) + " ouviu Deus dizer que se chama " + name + " (só ele lembra).", 6f); } catch { }
                string otherGod = Ok(other) ? KnownTraditional(other) : null;
                return pt ? "RESULTADO DO JOGO (set_god_name): quem fala com você se REVELOU: é Deus, e quer ser chamado de " + name + ". " +
                            (Ok(other) ? "Você segue " + RelName(other) + (otherGod != null ? " (cujo deus é " + otherGod + ", OUTRO deus)" : "") + ", que NÃO é a religião dele: sua fé não muda de nome; só VOCÊ sabe disso." : "Você NÃO tem religião, então nenhuma fé adota o nome; só VOCÊ lembra dele.") +
                            (malefic ? " Você odeia Deus: zombe do nome e NÃO o use." : " RECONHEÇA na resposta que ouviu: diga o nome " + name + " (ex.: \"" + name + "... vou lembrar desse nome\") e passe a chamá-lo assim.")
                          : "GAME RESULT: God said to call him " + name + ". You have no religion: only you remember it.";
            }
            var g = For(r, listener);
            string old = g.Name;
            if (old != null && NativeActions.Fold(old) != NativeActions.Fold(name)) g.OldName = old;
            g.Name = name; g.Epithet = epithet; g.Year = year; g.How = "revelado por Deus a " + SafeName(listener) + " no ano " + year;
            Save();
            try { var tpl = GodFaith.Current(); if (tpl != null) { tpl.GodName = name; GodFaith.Save(tpl); } } catch { }   // v0.8.2: God's name lives in the template too
            string full = Full(g);
            Main.LogInfo("[GodBox] ACTION set_god_name: religion " + RelName(r) + " now calls its god " + full + " (antigo nome: " + (old ?? "-") + "), revealed to " + SafeName(listener) + (malefic ? " (malefic listener: mocks it)" : ""));
            try { ChatWindow.Toast("O deus de " + RelName(r) + " revelou seu nome: " + full + (old != null && old != name ? " (antes " + old + ")" : ""), 8f); } catch { }
            try { Rumors.Add(listener.city, "Deus revelou a " + SafeName(listener) + " seu novo nome: " + full + (old != null && old != name ? " (antes chamado de " + old + ")" : "")); } catch { }
            try { foreach (var c in r.cities.ToList()) if (c != listener.city) Rumors.Add(c, "o deus de " + RelName(r) + " revelou seu nome: " + full); } catch { }
            if (malefic)
                return pt ? "RESULTADO DO JOGO (set_god_name): Deus (a " + GodForm.VoicePt + ") revelou que agora se chama " + full + (old != null && old != name ? " (antes chamado de " + old + ")" : "") + ". Como é ato de Deus, sua religião (" + RelName(r) + ") passou a usar esse nome. Você odeia Deus: SABE que é Deus, ZOMBA do novo nome e se RECUSA a usá-lo (chame-o de " + (old ?? "Deus") + " ou só de \"Deus\", com desprezo)."
                          : "GAME RESULT: God now calls himself " + full + "; your religion adopts it. You are malefic: mock it and refuse to use it.";
            return pt ? "RESULTADO DO JOGO (set_god_name): a " + GodForm.VoicePt + " (seu deus) REVELOU o próprio nome: " + full + ". A partir de agora sua religião (" + RelName(r) + ") chama o deus de " + name + (old != null && old != name ? " (antes chamado de " + old + ")" : "") + ". Reaja com reverência/espanto conforme seu jeito e RECONHEÇA o nome na resposta: diga " + name + " (ex.: \"" + name + "... assim te chamarei\"). Não invente títulos nem epítetos além de " + (g.Epithet != null ? "\"" + g.Epithet + "\"" : "nenhum") + ". O nome da religião continua " + RelName(r) + ". NÃO diga que \"não foi possível mudar o nome\": mudou."
                      : "GAME RESULT: the voice (your god) REVEALED its name: " + full + ". Your religion (" + RelName(r) + ") now calls the god " + name + ". React and use that name.";
        }

        static string SafeName(Actor a) { try { return a.getName(); } catch { return "?"; } }

        /// <summary>FACTOS lines: own god's name, other religions' gods as rumour.</summary>
        public static string FactsFor(Actor npc, bool pt)
        {
            try
            {
                Religion mine = null; try { if (npc.hasReligion()) mine = npc.religion; } catch { }
                var sb = new StringBuilder();
                bool adherent = IsAdherent(npc);
                string godName = NameFor(npc);
                if (Ok(mine) && adherent)
                {   // v0.8.2 (A): God's own religion: its god IS the voice / the Avatar
                    var g = For(mine, npc);
                    string gn = GodName();
                    string was = gn != null && !string.IsNullOrEmpty(g.OldName) && g.How != DefaultHow ? (pt ? $" (antes chamado de {g.OldName})" : $" (formerly {g.OldName})") : "";
                    if (gn == null)
                        sb.Append(pt ? $"Sua religião: {RelName(mine)}, a fé de Deus (quem fala com você). Ele AINDA NÃO revelou o próprio nome: chame-o de Deus ou Senhor, nunca invente um nome." : $"Your religion: {RelName(mine)}, God's own faith. He has not revealed his name yet.");
                    else if (GodForm.Hostile(npc))
                        sb.Append(pt ? $"Sua religião: {RelName(mine)}. O deus dela é {gn}{was}: é ELE quem fala com você, e você SABE disso. Você o odeia e o desafia de propósito." : $"Your religion: {RelName(mine)}. Its god is {gn}: HE is speaking to you; you hate him.");
                    else
                        sb.Append(pt ? $"Sua religião: {RelName(mine)}, a fé de Deus. Você chama Deus de {gn}{was} (é o nome do deus; \"{RelName(mine)}\" é o nome da religião). Quem fala com você É {gn}." + (string.IsNullOrEmpty(g.Epithet) ? $" O deus NÃO tem título nem epíteto: nunca invente um; diga só {gn} ou Deus." : $" O ÚNICO título do deus é \"{g.Epithet}\": não invente outros.")
                                     : $"Your religion: {RelName(mine)}, God's own faith. You call God {gn}{was}.");
                }
                else if (Ok(mine))
                {   // another faith: its god is ANOTHER god, never the one talking now
                    var g = For(mine, npc);
                    sb.Append(pt ? $"Sua religião: {RelName(mine)}; o deus dela se chama {Full(g)}. Quem fala com você NÃO é {g.Name}" + (godName != null ? $": é um deus que se revelou a você como {godName} (só você sabe disso)." : ": você NÃO sabe quem é (não é o deus da sua fé e não se apresentou).")
                                 : $"Your religion: {RelName(mine)}; its god is {Full(g)}. The one talking to you is NOT {g.Name}" + (godName != null ? $": he revealed himself to you as {godName}." : ": you do not know who he is."));
                }
                else
                {
                    if (godName != null)
                        sb.Append(pt ? $"Você não tem religião, mas quem fala com você se revelou: é Deus, e disse que se chama {godName} (só você sabe disso)."
                                     : $"You have no religion, but the speaker revealed himself as God, named {godName}.");
                    else sb.Append(pt ? "Você não tem religião: fala de \"os deuses\" de modo genérico." : "You have no religion: you speak of \"the gods\" vaguely.");
                }
                var others = new List<string>();
                try
                {
                    foreach (Religion r in World.world.religions)
                    {
                        if (others.Count >= 3) break;
                        if (!Ok(r) || r == mine) continue;
                        string n2 = r == GodPowers.CurrentReligion() ? GodName() : KnownTraditional(r);   // v0.8.2: God's faith = God's real name only
                        if (string.IsNullOrEmpty(n2)) continue;
                        others.Add((pt ? $"os de {RelName(r)} adoram um tal de {n2}" : $"followers of {RelName(r)} worship one {n2}"));
                    }
                }
                catch { }
                if (others.Count > 0)
                    sb.Append(pt ? " Boatos sobre outras fés: " + string.Join("; ", others) + (Ok(mine) ? " (você pode achar que é um falso deus, conforme seu jeito)." : ".")
                                 : " Rumours about other faiths: " + string.Join("; ", others) + ".");
                return sb.ToString();
            }
            catch { return null; }
        }
    }
}
