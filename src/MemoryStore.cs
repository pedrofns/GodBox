using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace WorldfallAIChat
{
    public class StoredTurn
    {
        [JsonProperty("role")] public string Role;      // "user" (player) or "assistant" (the unit)
        [JsonProperty("text")] public string Text;
        [JsonProperty("year")] public int Year;          // in-game year
        [JsonProperty("speaker")] public string Speaker;  // who the player was at that moment (possessed unit name or "god")
    }

    /// <summary>v0.8.0: a possessed body this unit talked with, and what it said about itself.</summary>
    public class MetPerson
    {
        [JsonProperty("id")] public long Id;
        [JsonProperty("name")] public string Name;        // the name it introduced itself with (may differ from the real one)
        [JsonProperty("desc")] public string Desc;        // what it said it does / where it is from
        [JsonProperty("first_year")] public int FirstYear;
        [JsonProperty("last_year")] public int LastYear;
        [JsonProperty("talks")] public int Talks;         // player messages to this unit while in that body
        [JsonProperty("last_time")] public float LastTime = -1f;   // unscaled time of the last message (not saved meaningfully across sessions)
        [JsonProperty("sessions")] public int Sessions;    // v0.8.1: distinct conversations (chat-window openings) with this unit
        [JsonProperty("last_session")] public string LastSession;   // v0.8.1: key of the conversation that last counted
    }

    public class MemNote
    {
        [JsonProperty("year")] public int Year;
        [JsonProperty("text")] public string Text;
    }

    public class UnitMemory
    {
        [JsonProperty("actor_id")] public long ActorId;
        [JsonProperty("name")] public string Name;
        [JsonProperty("world")] public string World;
        [JsonProperty("first_talk_year")] public int FirstTalkYear = -1;
        [JsonProperty("last_talk_year")] public int LastTalkYear = -1;
        [JsonProperty("conversations")] public int Conversations;
        [JsonProperty("last_power")] public string LastPower;                 // v0.7.8: before/after numbers of the last god power on this unit
        [JsonProperty("last_power_year")] public int LastPowerYear;
        [JsonProperty("god_name")] public string GodName;                     // v0.7.8: God's name told to a unit with no religion
        [JsonProperty("god_name_old")] public string GodNameOld;
        [JsonProperty("god_name_year")] public int GodNameYear;
        [JsonProperty("offer_asked")] public string OfferAsked;               // v0.8.2 (K): "temple" / "book" the NPC last asked God about
        [JsonProperty("offer_asked_year")] public int OfferAskedYear;
        [JsonProperty("prophet_of")] public string ProphetOf;                 // v7: religion name if the god made this unit a prophet
        [JsonProperty("notes")] public List<MemNote> Notes = new List<MemNote>();   // v7: messages relayed to this unit (capped)
        [JsonProperty("summary")] public string Summary = "";          // rolling summary of older talks (written by the LLM)
        [JsonProperty("turns")] public List<StoredTurn> Turns = new List<StoredTurn>();
        [JsonProperty("met")] public List<MetPerson> Met = new List<MetPerson>();   // v0.8.0: possessed bodies this unit has talked with
        [JsonIgnore] public bool Summarizing;
    }

    /// <summary>
    /// One JSON file per unit: Mods/&lt;mod&gt;/memory/&lt;world key&gt;/&lt;actor id&gt;.json
    /// The world key is a GUID saved inside the WorldBox save itself (map_stats.custom_data),
    /// so the same unit in the same save always finds its memory, and different worlds never mix.
    /// </summary>
    public static class MemoryStore
    {
        const string WorldKeyField = "ai_dialogue_world_id";
        static readonly Dictionary<string, UnitMemory> Cache = new Dictionary<string, UnitMemory>();
        public static string Root;

        public static string CurrentWorldKey()
        {
            var stats = GameAccess.MapStats();
            if (stats == null) return "no_world";
            if (stats.custom_data == null) stats.custom_data = new SaveCustomData();
            stats.custom_data.get(WorldKeyField, out string id, null);
            if (string.IsNullOrEmpty(id))
            {
                id = Sanitize(stats.name) + "_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                stats.custom_data.set(WorldKeyField, id);
            }
            return id;
        }

        static string PathFor(string world, long actorId) => Path.Combine(Root, "memory", world, actorId + ".json");

        public static UnitMemory Get(Actor actor)
        {
            string world = CurrentWorldKey();
            long id = GameAccess.Data(actor)?.id ?? -1L;
            string key = world + "/" + id;
            if (Cache.TryGetValue(key, out var mem)) return mem;
            string path = PathFor(world, id);
            try
            {
                if (File.Exists(path)) mem = JsonConvert.DeserializeObject<UnitMemory>(File.ReadAllText(path));
            }
            catch (Exception e) { Main.LogWarning("[GodBox] memory file unreadable, starting fresh: " + path + " " + e.Message); }
            mem ??= new UnitMemory();
            mem.ActorId = id;
            mem.World = world;
            mem.Name = Safe(() => actor.getName(), mem.Name);
            mem.Turns ??= new List<StoredTurn>();
            mem.Notes ??= new List<MemNote>();
            if (!SummaryOk(mem.Summary))
            {   // v7: older versions could store a raw transcript dump as the "summary"
                string c = CleanSummary(mem.Summary, Math.Max(200, Main.Cfg?.SummaryMaxChars ?? 600));
                mem.Summary = SummaryOk(c) ? c : "";
                Main.LogInfo("[GodBox] memory summary of " + mem.Name + " was malformed; " + (mem.Summary.Length > 0 ? "cleaned." : "dropped."));
            }
            Cache[key] = mem;
            return mem;
        }

        public static void Save(UnitMemory mem)
        {
            try
            {
                string path = PathFor(mem.World, mem.ActorId);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(mem, Formatting.Indented), Encoding.UTF8);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                Deaths.NoteTalked(mem.ActorId);
            }
            catch (Exception e) { Main.LogError("[GodBox] could not save memory: " + e.Message); }
        }

        public static void Forget(UnitMemory mem)
        {
            mem.Summary = "";
            mem.Turns.Clear();
            mem.Conversations = 0;
            mem.FirstTalkYear = mem.LastTalkYear = -1;
            Save(mem);
        }

        /// <summary>Folds the oldest messages into the rolling summary (async LLM call) when the log grows too long.
        /// v7: the summary must be a short third-person prose memory (no transcript lines, no headers, no role labels),
        /// validated and capped at a sentence boundary; one stricter retry, otherwise the old summary is kept.</summary>
        public static void MaybeSummarize(UnitMemory mem, DialogueConfig cfg, string characterName)
        {
            if (mem.Summarizing || mem.Turns.Count <= Math.Max(4, cfg.SummarizeAfterMessages)) return;
            int keep = Math.Max(2, Math.Min(cfg.KeepRecentMessages, mem.Turns.Count - 2));
            var old = mem.Turns.Take(mem.Turns.Count - keep).ToList();
            if (old.Count == 0) return;
            bool pt = PromptBuilder.LanguagePortuguese(cfg);
            bool auto = PromptBuilder.LanguageAuto(cfg);
            int maxChars = Math.Max(200, cfg.SummaryMaxChars);

            // events as plain prose bullets (no "[ano N] Name:" transcript format the model would copy back)
            var sb = new StringBuilder();
            foreach (var t in old)
            {
                string who = t.Role == "user" ? (string.IsNullOrEmpty(t.Speaker) ? (pt ? "Deus (voz do céu)" : "God (voice from the sky)") : t.Speaker) : characterName;
                string said = (t.Text ?? "").Replace("\n", " ");
                if (said.Length > 300) said = said.Substring(0, 300) + "…";
                sb.Append("- ").Append(pt ? "No ano " : "In year ").Append(t.Year).Append(", ").Append(who).Append(pt ? " disse: " : " said: ").Append(said).Append('\n');
            }
            string existing = SummaryOk(mem.Summary) ? mem.Summary : "";

            List<ChatMessage> Prompt(bool strict)
            {
                string sys = pt
                    ? "Escreva SOMENTE em português do Brasil. " +
                      $"Resuma o que {characterName} (personagem de um mundo de fantasia) deve lembrar, em TERCEIRA PESSOA, em prosa corrida, " +
                      $"no máximo {maxChars} caracteres (umas 3 a 5 frases curtas). Junte a memória antiga com os fatos novos. " +
                      "Guarde só o essencial: quem falou com ele, pedidos, promessas, nomes, sentimentos, acontecimentos. " +
                      "PROIBIDO: copiar falas, usar o formato 'Nome: fala', colocar anos entre colchetes, títulos/cabeçalhos (como 'MEMÓRIA'), listas, aspas longas. " +
                      "Quem fala como voz do céu é o deus (rótulo 'X (voz do céu)' = o nome que a religião do personagem dá a ele); não invente outro nome. " +
                      "Responda apenas com o resumo." + (strict ? " ATENÇÃO: a tentativa anterior veio no formato errado. Escreva só 3 frases em prosa, sem nenhuma fala copiada." : "")
                    : $"Summarize what {characterName} (a character in a fantasy world) should remember, in THIRD PERSON, as plain prose, " +
                      $"at most {maxChars} characters (3-5 short sentences). Merge the old memory with the new facts. Keep only what matters: who talked to them, " +
                      "requests, promises, names, feelings, events. FORBIDDEN: copying dialogue, 'Name: line' format, bracketed years, headers (like 'MEMORY'), lists. " +
                      "The voice from the sky is 'God'; do not invent another name unless he gave one. " +
                      (auto ? "Use the language the conversation mostly uses. " : $"Write ONLY in {cfg.ReplyLanguage.Trim()}. ") +
                      "Output only the summary." + (strict ? " WARNING: the previous attempt had the wrong format. Write just 3 prose sentences, no quoted lines." : "");
                string user = (pt ? "Memória antiga: " : "Old memory: ") + (string.IsNullOrWhiteSpace(existing) ? (pt ? "(nenhuma)" : "(none)") : existing) +
                              (pt ? "\n\nFatos novos (não copie, resuma):\n" : "\n\nNew facts (do not copy, summarize):\n") + sb;
                return new List<ChatMessage> { new ChatMessage("system", sys), new ChatMessage("user", user) };
            }

            mem.Summarizing = true;
            int tokens = Math.Max(200, maxChars / 2);
            void Done(string summary)
            {
                mem.Summarizing = false;
                mem.Summary = summary;
                foreach (var t in old) mem.Turns.Remove(t);    // remove exactly the turns that were summarized
                Save(mem);
                Main.LogInfo("[GodBox] memory summary updated (" + summary.Length + " chars).");
            }
            void Fail(string why)
            {
                mem.Summarizing = false;
                Main.LogWarning("[GodBox] memory summary rejected/failed, keeping the old one: " + why);
                if (mem.Turns.Count > cfg.SummarizeAfterMessages * 3) mem.Turns.RemoveRange(0, mem.Turns.Count - cfg.SummarizeAfterMessages * 2);
            }
            LlmClient.Complete(cfg, cfg.GetSummaryModel(), Prompt(false), tokens, first =>
            {
                string c = CleanSummary(first, maxChars);
                if (SummaryOk(c) && c.Length > 0) { Done(c); return; }
                LlmClient.Complete(cfg, cfg.GetSummaryModel(), Prompt(true), tokens, second =>
                {
                    string c2 = CleanSummary(second, maxChars);
                    if (SummaryOk(c2) && c2.Length > 0) Done(c2); else Fail("bad format twice: " + Clip(second, 120));
                }, Fail, false, 0.2f);
            }, Fail, false, float.NaN);
        }

        static readonly Regex BadSummaryRx = new Regex(
            @"\[\s*(ano|year)\b|(?-i:MEM[ÓO]RIA|MEMORY|TRECHOS|EXCERPTS)|\b(disse|said)\s*:|<\||(^|\n)\s*(user|assistant|system|jogador|player)\s*:|(^|\n)\s*-?\s*[A-ZÀ-Ý][\wÀ-ÿ'’ ()]{0,40}:\s",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>True for an empty summary or a clean prose one (no transcript lines / headers / role labels).</summary>
        public static bool SummaryOk(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            return !BadSummaryRx.IsMatch(s);
        }

        /// <summary>Strip headers, join lines, cap at the last sentence end before maxChars.</summary>
        public static string CleanSummary(string s, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = LlmClient.CleanReply(s);
            s = Regex.Replace(s, @"^\s*(\*\*)?\s*(mem[óo]ria( atualizada| de longo prazo)?|resumo|summary|updated memory)\s*(\*\*)?\s*:\s*", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*\n+\s*", " ").Trim();
            if (s.Length > maxChars)
            {
                string cut = s.Substring(0, maxChars);
                int end = Math.Max(cut.LastIndexOf(". ", StringComparison.Ordinal), Math.Max(cut.LastIndexOf("! ", StringComparison.Ordinal), cut.LastIndexOf("? ", StringComparison.Ordinal)));
                if (cut.EndsWith(".") || cut.EndsWith("!") || cut.EndsWith("?")) end = cut.Length - 1;
                s = end > maxChars / 3 ? cut.Substring(0, end + 1) : cut.TrimEnd() + "…";
            }
            return s.Trim();
        }

        static string Clip(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + "…";

        // ------------------------------------------------------------------------------------------------
        // v7: the god's religion and prophets, per world (persistentDataPath/WorldfallAIChat/memory/<world>/_god.json)
        // ------------------------------------------------------------------------------------------------
        public class GodState
        {
            [JsonProperty("religion_id")] public long ReligionId = -1;
            [JsonProperty("religion_name")] public string ReligionName;
            [JsonProperty("prophets")] public List<long> Prophets = new List<long>();
            [JsonProperty("converted_total")] public int ConvertedTotal;
            // v0.8.1 (B13): conversions per prophet (id -> count) and God's deadline threats; (C2) cities with a temple God raised
            [JsonProperty("prophet_conversions")] public Dictionary<string, int> ProphetConversions = new Dictionary<string, int>();
            [JsonProperty("deadline_threats")] public List<DeadlineThreat> Threats = new List<DeadlineThreat>();
            [JsonProperty("god_temple_cities")] public List<long> GodTempleCities = new List<long>();
            // v0.8.2: temple orders with a deadline (F5), prophets God took back (F2), holy books written on God's order (K)
            [JsonProperty("temple_orders")] public List<TempleOrder> TempleOrders = new List<TempleOrder>();
            [JsonProperty("revoked_prophets")] public List<long> RevokedProphets = new List<long>();
            [JsonProperty("holy_books")] public int HolyBooks;
            public int ConversionsOf(long id) => ProphetConversions != null && ProphetConversions.TryGetValue(id.ToString(), out int n) ? n : 0;
            public void AddConversions(long id, int n)
            {
                if (n <= 0) return;
                ProphetConversions ??= new Dictionary<string, int>();
                ProphetConversions[id.ToString()] = ConversionsOf(id) + n;
            }
        }
        public class DeadlineThreat
        {
            [JsonProperty("prophet_id")] public long ProphetId;
            [JsonProperty("prophet_name")] public string ProphetName;
            [JsonProperty("start_conversions")] public int StartConversions;
            [JsonProperty("made_year")] public int MadeYear;
            [JsonProperty("deadline_year")] public int DeadlineYear;
            [JsonProperty("penalty")] public string Penalty = "curse";
            [JsonProperty("text")] public string Text;
        }
        /// <summary>v0.8.2 (F5): "erga um templo, você tem N anos": checked when the deadline expires.</summary>
        public class TempleOrder
        {
            [JsonProperty("npc_id")] public long NpcId;
            [JsonProperty("npc_name")] public string NpcName;
            [JsonProperty("city_id")] public long CityId;
            [JsonProperty("city_name")] public string CityName;
            [JsonProperty("made_year")] public int MadeYear;
            [JsonProperty("deadline_year")] public int DeadlineYear;
            [JsonProperty("penalty")] public string Penalty;   // null = none stated; "curse" = God threatened a curse
        }
        static readonly Dictionary<string, GodState> GodCache = new Dictionary<string, GodState>();

        public static GodState God()
        {
            string world = CurrentWorldKey();
            if (GodCache.TryGetValue(world, out var g)) return g;
            string path = Path.Combine(Root, "memory", world, "_god.json");
            try { if (File.Exists(path)) g = JsonConvert.DeserializeObject<GodState>(File.ReadAllText(path)); } catch { }
            g ??= new GodState();
            g.Prophets ??= new List<long>();
            g.ProphetConversions ??= new Dictionary<string, int>();
            g.Threats ??= new List<DeadlineThreat>();
            g.GodTempleCities ??= new List<long>();
            g.TempleOrders ??= new List<TempleOrder>();
            g.RevokedProphets ??= new List<long>();
            GodCache[world] = g;
            return g;
        }

        public static void SaveGod()
        {
            try
            {
                string world = CurrentWorldKey();
                if (!GodCache.TryGetValue(world, out var g)) return;
                string path = Path.Combine(Root, "memory", world, "_god.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(g, Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception e) { Main.LogWarning("[GodBox] could not save god state: " + e.Message); }
        }

        static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "world";
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            string r = sb.ToString();
            return r.Length > 24 ? r.Substring(0, 24) : r;
        }

        static string Safe(Func<string> f, string fallback) { try { return f() ?? fallback; } catch { return fallback; } }
    }
}
