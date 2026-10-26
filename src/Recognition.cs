using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WorldfallAIChat
{
    /// <summary>
    /// v0.8.0: who recognises the body the player possesses. What an NPC knows about the speaker comes ONLY from
    /// real game data (renown, titles, Worldfall ranks, family / lover / friend / clan, faith) and from that NPC's
    /// own memory of earlier talks. An NPC that does not know the speaker treats him as a stranger and asks who he is.
    /// Does not apply to God (no body) nor to the Avatar of God.
    /// </summary>
    public static class Recognition
    {
        public enum Know { None = 0, Name = 1, NameRole = 2, Full = 3 }

        public class Info
        {
            public Know K;
            public int Fame;              // 0..4 global fame level of the body
            public string FameWhy = "";   // breakdown for the log
            public string Why = "";       // why this NPC knows him (pt, for the prompt)
            public string RealName;
            public string TitlePt;        // e.g. "o rei Uaau de Neen" / "Uaau, capitão do exército de Neen"
            public string RolePt;         // e.g. "rei de Neen" (no name)
            public MetPerson Met;         // this NPC's memory of the body
            public bool MetBefore;        // talked in an earlier message
            public string Deeds;          // only for Full: real kills / chronicle deaths
            public bool Active;           // false = recognition does not apply (God, Avatar, disabled)
            /// <summary>Name the NPC may use: the real one if known, else the one he introduced himself with, else null.</summary>
            public string CallName => K >= Know.Name ? RealName : (string.IsNullOrEmpty(Met?.Name) ? null : Met.Name);
            public bool Stranger => Active && K == Know.None;
        }

        static readonly string[] NotableTraits = { "chosen_one", "blessed", "kingslayer", "veteran", "immortal" };
        static readonly Dictionary<string, string> TraitPt = new Dictionary<string, string>
        {
            { "chosen_one", "o escolhido" }, { "blessed", "abençoado(a)" }, { "kingslayer", "matador(a) de reis" },
            { "veteran", "veterano(a) de muitas lutas" }, { "immortal", "imortal" },
        };

        static string S(Func<string> f) { try { return f(); } catch { return null; } }
        static bool B(Func<bool> f) { try { return f(); } catch { return false; } }

        public static bool Applies(Actor npc, Actor you)
        {
            if (you == null || npc == null || you == npc) return false;
            if (Main.Cfg != null && !Main.Cfg.RecognitionEnabled) return false;
            try { if (Avatar.IsAvatar(you)) return false; } catch { }
            return true;
        }

        /// <summary>Global fame level 0..4 = max(renown tier, title tier, Worldfall rank tier) (+1 for notable real traits/kills).</summary>
        public static int Fame(Actor you, out string why, out List<WorldfallBridge.WfRank> ranks)
        {
            var cfg = Main.Cfg ?? new DialogueConfig();
            int[] t = (cfg.RenownTiers != null && cfg.RenownTiers.Length >= 4) ? cfg.RenownTiers : new[] { 10, 50, 150, 400 };
            int renown = 0;
            try { renown = you.renown; } catch { }
            int lr = 0;
            for (int i = 0; i < 4; i++) if (renown >= t[i]) lr = i + 1;
            var parts = new List<string> { "renown " + renown + "->" + lr };

            int lt = 0;
            if (B(() => you.isKing()))
            {
                int cities = 0;
                try { cities = you.kingdom.countCities(); } catch { }
                lt = (renown >= t[2] || cities >= cfg.KingWorldFamousCities) ? 4 : 3;
                parts.Add("king(" + cities + " cities)->" + lt);
            }
            else if (B(() => you.isCityLeader())) { lt = renown >= t[1] ? 3 : 2; parts.Add("city leader->" + lt); }
            else if (B(() => you.isArmyGroupLeader() || you.is_army_captain) || B(() => you.clan != null && you.clan.getChief() == you))
            { lt = renown >= t[1] ? 3 : 2; parts.Add("captain/clan chief->" + lt); }

            // Worldfall ranks: a plain soldier (or a guard, which in Worldfall is just a soldier at home) adds nothing;
            // ANY other rank makes him known at least kingdom-wide; the heir of a throne is world-famous.
            int lw = 0;
            ranks = WorldfallBridge.Ranks(you);
            foreach (var r in ranks)
            {
                int v;
                switch (r.Kind)
                {
                    case "soldier": case "guard": v = 0; break;
                    case "spymaster": v = 0; break;                       // secret seat (Worldfall hides it from strangers)
                    case "king": v = Math.Max(3, lt); break;
                    case "heir": v = 4; break;
                    default: v = 3; break;                                // marshal, steward, chancellor, mage, champion, captain, lord, leader
                }
                if (v > lw) lw = v;
            }
            if (WorldfallBridge.ServiceInfo(you, out bool soldier, out bool captain, out float merit, out int wkills))
            {
                if (captain && lw < 3) lw = 3;
                parts.Add("wf service soldier=" + soldier + " captain=" + captain + " merit=" + merit.ToString("0.0") + " kills=" + wkills);
            }
            if (ranks.Count > 0) parts.Add("wf ranks [" + string.Join(", ", ranks.Select(r => r.En)) + "]->" + lw);

            int lvl = Math.Max(lr, Math.Max(lt, lw));
            int kills = 0;
            try { kills = you.data.kills; } catch { }
            var notable = NotableTraits.Where(x => B(() => you.hasTrait(x))).ToList();
            if (notable.Count > 0 || kills >= cfg.NotableKills)
            {
                lvl = Math.Min(4, lvl + 1);
                parts.Add("+1 (" + string.Join(",", notable) + (kills >= cfg.NotableKills ? " kills " + kills : "") + ")");
            }
            why = string.Join("; ", parts) + " => fame " + lvl;
            return lvl;
        }

        /// <summary>Real title in pt-BR, never ids: "o rei Uaau de Neen", "Uaau, capitão do exército de Neen".</summary>
        public static string Title(Actor you, List<WorldfallBridge.WfRank> ranks, out string role)
        {
            role = null;
            string n = S(() => you.getName()) ?? "?";
            bool fem = B(() => you.isSexFemale());
            string kname = S(() => you.kingdom?.name), cname = S(() => you.city?.name);
            if (B(() => you.isKing()) && kname != null)
            {
                role = (fem ? "rainha de " : "rei de ") + kname;
                return (fem ? "a rainha " : "o rei ") + n + " de " + kname;
            }
            var list = new List<string>();
            if (B(() => you.isCityLeader()) && cname != null) list.Add("líder de " + cname);
            foreach (var r in ranks ?? new List<WorldfallBridge.WfRank>())
                if (r.Kind != "soldier" && r.Kind != "guard" && r.Kind != "spymaster" && r.Kind != "king" && r.Kind != "leader" && r.Pt != null) list.Add(r.Pt);
            if (list.Count == 0 && B(() => you.isArmyGroupLeader() || you.is_army_captain))
                list.Add((fem ? "capitã do exército de " : "capitão do exército de ") + (cname ?? kname ?? "seu reino"));
            if (B(() => you.clan != null && you.clan.getChief() == you)) list.Add("chefe do clã " + S(() => you.clan.name));
            try
            {
                if (MemoryStore.God().Prophets.Contains(you.getID()))
                    list.Add((fem ? "profetisa de " : "profeta de ") + (S(() => GodPowers.CurrentReligion()?.name) ?? "Deus"));
            }
            catch { }
            if (list.Count == 0)
            {
                var sol = ranks?.FirstOrDefault(r => r.Kind == "soldier" || r.Kind == "guard");
                if (sol != null) list.Add(sol.Pt);
                else
                {
                    string job = FactsBuilder.Job(you, true);
                    if (!string.IsNullOrEmpty(job)) list.Add(job + (cname != null ? " de " + cname : ""));
                    else if (B(() => !you.isAdult())) list.Add(fem ? "uma menina" + (cname != null ? " de " + cname : "") : "um menino" + (cname != null ? " de " + cname : ""));
                    else if (cname != null) list.Add((fem ? "moradora de " : "morador de ") + cname);
                }
            }
            list = list.Distinct().ToList();
            role = string.Join(", ", list.Take(3));
            return string.IsNullOrEmpty(role) ? n : n + ", " + role;
        }

        /// <summary>What this NPC knows about the body the player is in. Computed every turn.</summary>
        public static Info For(Actor npc, Actor you, bool log = false)
        {
            var info = new Info();
            if (!Applies(npc, you)) return info;
            info.Active = true;
            info.RealName = S(() => you.getName());
            info.Fame = Fame(you, out info.FameWhy, out var ranks);
            info.TitlePt = Title(you, ranks, out info.RolePt);

            bool sameCity = B(() => npc.city != null && npc.city == you.city);
            bool sameKingdom = B(() => npc.kingdom != null && npc.kingdom == you.kingdom);
            Know k = Know.None;
            var why = new List<string>();
            switch (info.Fame)
            {
                case 4: k = Know.Full; why.Add("é famoso(a) no mundo inteiro"); break;
                case 3: k = sameKingdom ? Know.NameRole : Know.Name; why.Add(sameKingdom ? "é conhecido(a) em todo o seu reino" : "a fama dele(a) chegou até aqui"); break;
                case 2: k = sameCity ? Know.NameRole : sameKingdom ? Know.Name : Know.None; if (k > 0) why.Add(sameCity ? "é bem conhecido(a) na sua cidade" : "já ouviu o nome dele(a) no reino"); break;
                case 1: k = sameCity ? Know.Name : Know.None; if (k > 0) why.Add("já ouviu o nome dele(a) na cidade"); break;
            }
            // family, lover, best friend, same clan: always know him well
            string rel = null;
            if (B(() => npc.lover == you || you.lover == npc)) rel = "é o seu amor";
            else if (B(() => npc.isRelatedTo(you) || you.isRelatedTo(npc))) rel = "é da sua família";
            else if (B(() => npc.getBestFriend() == you || you.getBestFriend() == npc)) rel = "é seu melhor amigo/amiga";
            else if (B(() => npc.clan != null && npc.clan == you.clan)) rel = "é do seu clã";
            if (rel != null) { k = Know.Full; why.Add(rel); }
            // prophet of God's religion: known to its believers everywhere
            try
            {
                var relg = GodPowers.CurrentReligion();
                if (relg != null && npc.religion == relg && MemoryStore.God().Prophets.Contains(you.getID()) && k < Know.NameRole)
                { k = Know.NameRole; why.Add("é o(a) profeta da sua fé"); }
            }
            catch { }
            // Worldfall: reputation in this NPC's town; spymaster seat known only to same-realm people who met him
            string stand = WorldfallBridge.TownStanding(you, npc.city);
            if (stand == "Champion" || stand == "Honored" || stand == "Outlaw")
            {
                if (k < Know.NameRole) k = Know.NameRole;
                string cn = S(() => npc.city.name);
                why.Add(stand == "Champion" ? "é o(a) campeão/campeã de " + cn + " (reputação na cidade)" : stand == "Honored" ? "é honrado(a) em " + cn : "é fora da lei em " + cn);
            }
            else if (stand == "Trusted" && k < Know.Name) { k = Know.Name; why.Add("tem a confiança da sua cidade"); }
            bool? wfMet = WorldfallBridge.HasMet(you, npc);
            var spy = ranks.FirstOrDefault(r => r.Kind == "spymaster");
            if (spy != null && sameKingdom && wfMet == true) { info.RolePt = string.IsNullOrEmpty(info.RolePt) ? spy.Pt : info.RolePt + ", " + spy.Pt; if (k < Know.NameRole) k = Know.NameRole; }

            // this NPC's own memory
            try
            {
                var mem = MemoryStore.Get(npc);
                info.Met = mem.Met?.FirstOrDefault(m => m.Id == you.getID());
                // v0.8.1 (B1): only a PREVIOUS conversation counts as "met before" (never the turns of this one,
                // never Worldfall's met flag, which is set as soon as the talk menu opens)
                info.MetBefore = PriorConversations(info.Met) > 0;
            }
            catch { }
            if (info.MetBefore && !string.IsNullOrEmpty(info.Met?.Name) && info.Met.Name == info.RealName && k < Know.Name)
            { k = Know.Name; why.Add("ele(a) mesmo(a) disse o nome a você"); }

            info.K = k;
            info.Why = string.Join("; ", why);
            if (k == Know.Full) info.Deeds = DeedsOf(you);
            if (log)
                Main.LogInfo("[GodBox] recognition: " + S(() => npc.getName()) + " -> " + info.RealName + ": " + k +
                             " (" + info.FameWhy + (info.Why.Length > 0 ? "; " + info.Why : "") + (info.MetBefore ? "; met before" : "") +
                             (info.Met?.Name != null ? "; introduced as " + info.Met.Name : "") + ")");
            return info;
        }

        /// <summary>Real deeds only: kill count and chronicle deaths caused by this body.</summary>
        static string DeedsOf(Actor you)
        {
            var parts = new List<string>();
            int kills = 0;
            try { kills = you.data.kills; } catch { }
            if (kills > 0) parts.Add("já matou " + kills + (kills == 1 ? " inimigo" : " inimigos"));
            try
            {
                long id = you.getID();
                var vict = Deaths.All().Where(r => r.KillerId == id).OrderByDescending(r => r.Year).Take(3)
                    .Select(r => r.Name + (string.IsNullOrEmpty(r.Role) ? "" : " (" + r.Role + ")") + " no ano " + r.Year).ToList();
                if (vict.Count > 0) parts.Add("matou " + string.Join(", ", vict));
            }
            catch { }
            var notable = NotableTraits.Where(x => B(() => you.hasTrait(x))).Select(x => TraitPt[x]).ToList();
            if (notable.Count > 0) parts.Add("é " + string.Join(", ", notable));
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

        // ---------- v0.8.1 (B1): conversation keys ----------
        static readonly string Launch = Guid.NewGuid().ToString("N").Substring(0, 8);
        static int _serial;
        /// <summary>The current conversation (one opening of the chat window in this game launch).</summary>
        public static string SessionKey => Launch + ":" + _serial;
        /// <summary>Called when the chat window opens on a unit: a new conversation starts.</summary>
        public static void NewConversation() => _serial++;

        /// <summary>How many conversations with this person happened BEFORE the current one.</summary>
        public static int PriorConversations(MetPerson m)
        {
            if (m == null) return 0;
            if (m.Sessions == 0) return m.Talks > 0 && m.LastSession == null ? 1 : 0;   // legacy record (0.8.0 and older): from an earlier game session
            return m.LastSession == SessionKey ? m.Sessions - 1 : m.Sessions;
        }

        /// <summary>Called for every player message in a body: counts talks and stores a self-introduction.</summary>
        public static void NoteTurn(Actor npc, Actor you, string text)
        {
            if (!Applies(npc, you)) return;
            try
            {
                var mem = MemoryStore.Get(npc);
                if (mem.Met == null) mem.Met = new List<MetPerson>();
                long id = you.getID();
                var m = mem.Met.FirstOrDefault(x => x.Id == id);
                int year = Main.CurrentYear();
                if (m == null) { m = new MetPerson { Id = id, FirstYear = year }; mem.Met.Add(m); if (mem.Met.Count > 30) mem.Met.RemoveAt(0); }
                m.LastYear = year;
                if (m.LastSession != SessionKey)
                {
                    if (m.Sessions == 0 && m.Talks > 0) m.Sessions = 1;   // legacy migration: the old talks were one earlier conversation
                    m.Sessions++; m.LastSession = SessionKey;
                    Main.LogInfo("[GodBox] recognition: conversation #" + m.Sessions + " between " + S(() => npc.getName()) + " and " + S(() => you.getName()) + ".");
                }
                m.Talks++;
                m.LastTime = UnityEngine.Time.unscaledTime;
                string name = SafetyRules.ParseSelfIntro(text, out string desc);
                if (name != null && name != m.Name) { m.Name = name; Main.LogInfo("[GodBox] recognition: " + S(() => npc.getName()) + " now knows the speaker as \"" + name + "\" (said by the player)"); }
                if (desc != null) { m.Desc = desc; Main.LogInfo("[GodBox] recognition: " + S(() => npc.getName()) + " stored the speaker's self-description: " + desc); }
                MemoryStore.Save(mem);
            }
            catch (Exception e) { Main.LogWarning("[GodBox] recognition NoteTurn failed: " + e.Message); }
        }

        /// <summary>v0.8.1 (B1): the meeting rule for the prompt. Conhecer de fama/título NÃO é ter conversado.</summary>
        static string MeetingRule(Info i)
        {
            if (!i.MetBefore)
                return "Vocês NUNCA conversaram antes desta conversa (as falas de agora são desta mesma conversa). Se perguntarem se já se encontraram, diga que não (ou que só conhece de fama, se for o caso). NUNCA invente encontros passados. ";
            int n = PriorConversations(i.Met);
            return "Vocês já conversaram " + (n == 1 ? "uma vez" : n + " vezes") + " antes desta conversa. O CONTEÚDO desses encontros você só sabe pelas suas lembranças reais (notas/resumo abaixo); se não houver lembrança, diga que se lembra do rosto mas não do que falaram. NUNCA invente o que foi dito. ";
        }

        /// <summary>Label for the speaker in prompts / history: the name the NPC knows, or "um(a) desconhecido(a)".</summary>
        public static string Label(Info i, Actor you)
        {
            if (i == null || !i.Active) return S(() => you?.getName());
            if (i.CallName != null) return i.CallName;
            return B(() => you.isSexFemale()) ? "uma desconhecida" : "um desconhecido";
        }

        /// <summary>The "who is speaking to you" text for the facts block (pt).</summary>
        public static string FactsText(Info i, Actor you, Actor npc)
        {
            bool fem = B(() => you.isSexFemale());
            string sp = FactsBuilder.Species(you) ?? "?";
            bool child = B(() => !you.isAdult());
            string look = (fem ? (child ? "uma menina" : "uma mulher") : (child ? "um menino" : "um homem")) + " (" + sp + ")";
            var sb = new StringBuilder();
            if (i.K == Know.None)
            {
                sb.Append("um(a) DESCONHECIDO(A): " + look + ". Você NÃO conhece esta pessoa: NÃO sabe o nome, a profissão, a família nem de onde é. ");
                if (!string.IsNullOrEmpty(i.Met?.Name))
                    sb.Append("Ela se apresentou a você como \"" + i.Met.Name + "\"" + (string.IsNullOrEmpty(i.Met.Desc) ? "" : " e disse: \"" + i.Met.Desc + "\"") + " (você só sabe o que ela disse). ");
                else if (!string.IsNullOrEmpty(i.Met?.Desc))
                    sb.Append("Ela disse a você: \"" + i.Met.Desc + "\", mas não disse o nome. ");
                if (i.MetBefore && string.IsNullOrEmpty(i.Met?.Name))
                    sb.Append("Vocês já conversaram antes (ano " + i.Met?.FirstYear + "), mas você não sabe o nome dela. ");
                if (!i.MetBefore && string.IsNullOrEmpty(i.Met?.Name))
                    sb.Append("É a primeira vez que vocês se falam: trate-a como estranha e pergunte com naturalidade quem ela é e o que faz. ");
                sb.Append(MeetingRule(i));
                sb.Append("NUNCA diga um nome para ela que ela não disse, nem invente quem ela é.");
                return sb.ToString();
            }
            sb.Append(i.CallName ?? i.RealName);
            if (i.K >= Know.NameRole && !string.IsNullOrEmpty(i.RolePt)) sb.Append(" — " + i.RolePt);
            sb.Append(" (" + look + "). ");
            if (!string.IsNullOrEmpty(i.Why)) sb.Append("Você conhece esta pessoa porque " + i.Why + ". ");
            if (i.K == Know.Name) sb.Append("Você só conhece o NOME dela (não sabe a profissão, a família nem o que fez). ");
            if (i.K == Know.NameRole) sb.Append("Você sabe o nome e o título/posição dela, mas não os detalhes da vida dela. ");
            if (i.K == Know.Full && !string.IsNullOrEmpty(i.Deeds)) sb.Append("Feitos REAIS dela (só estes): " + i.Deeds + ". ");
            if (!string.IsNullOrEmpty(i.Met?.Desc)) sb.Append("Ela contou a você: \"" + i.Met.Desc + "\". ");
            sb.Append(MeetingRule(i));
            if (i.K >= Know.NameRole) sb.Append("Ao se dirigir a ela pelo título, use: " + i.TitlePt + ".");
            return sb.ToString().Trim();
        }
    }
}
