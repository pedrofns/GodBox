using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WorldfallAIChat
{
    /// <summary>Builds the system prompt from the unit's live in-game state + Worldfall context + its memory.</summary>
    public static class PromptBuilder
    {
        static string S(Func<object> f)
        {
            try { var v = f(); return v == null ? null : v.ToString(); } catch { return null; }
        }

        /// <summary>True when reply_language is empty or "auto": mirror the player's language.</summary>
        public static bool LanguageAuto(DialogueConfig cfg)
        {
            var l = (cfg?.ReplyLanguage ?? "").Trim().ToLowerInvariant();
            return l.Length == 0 || l == "auto" || l == "automatic" || l == "automático" || l == "automatico";
        }

        /// <summary>True when the forced reply language is Portuguese (then the whole prompt is written in Portuguese).</summary>
        public static bool LanguagePortuguese(DialogueConfig cfg)
        {
            if (LanguageAuto(cfg)) return false;
            var l = cfg.ReplyLanguage.Trim().ToLowerInvariant();
            return l.Contains("portug") || l == "pt" || l.StartsWith("pt-") || l.StartsWith("pt_");
        }

        static string Lang(DialogueConfig cfg) => cfg.ReplyLanguage.Trim();

        /// <summary>v0.8.0: the speaker tag as THIS listener knows the possessed body (real name, introduced name, or "um desconhecido").</summary>
        /// <summary>v0.8.2 (I3): the game's profession enum in Portuguese (the raw "Unit" leaked as "a unit adulta").</summary>
        static string ProfessionPt(Actor a, Func<string, string, string> T)
        {
            bool f = false; try { f = a.isSexFemale(); } catch { }
            switch (a.getProfession())
            {
                case UnitProfession.King: return T("king", f ? "rainha" : "rei");
                case UnitProfession.Leader: return T("leader", "líder");
                case UnitProfession.Warrior: return T("warrior", f ? "guerreira" : "guerreiro");
                case UnitProfession.Unit: return T("commoner", f ? "moradora comum" : "morador comum");
                default: return null;
            }
        }

        public static string SpeakerLabelFor(Actor you, Actor listener, Recognition.Info rec)
        {
            if (you != null && rec != null && rec.Active) return Recognition.Label(rec, you);
            return SpeakerLabel(you, listener);
        }

        static readonly System.Text.RegularExpressions.Regex RumorQRx = new System.Text.RegularExpressions.Regex(
            @"\b(boatos?|fofocas?|novidades?|noticias?|rumores?|ouviu falar|ouviste falar|o que (se )?(anda|andam) (dizendo|falando)|o que (se )?fala por ai|o que contam)\b");
        static readonly System.Text.RegularExpressions.Regex MoreQRx = new System.Text.RegularExpressions.Regex(
            @"^\s*(e\s+)?(algo|alguma coisa|mais alguma coisa|algum outro|outro|outra|mais algum|mais alguma)( mais)?\s*\??\s*$|^\s*(e\s+)?mais\s+(algo|alguma coisa|algum|alguma)\b|^\s*so isso\s*\?");
        /// <summary>v0.8.0: "sabe de algum boato?" / "algo mais?" right after one.</summary>
        public static bool RumorQ(string text, UnitMemory mem)
        {
            string f = NativeActions.Fold(text ?? "");
            if (RumorQRx.IsMatch(f)) return true;
            if (!MoreQRx.IsMatch(f) || mem?.Turns == null) return false;
            var prevUser = mem.Turns.LastOrDefault(t => t.Role == "user");
            return prevUser != null && RumorQRx.IsMatch(NativeActions.Fold(prevUser.Text ?? ""));
        }

        public static string SpeakerLabel(Actor you, Actor listener = null)
        {
            bool pt = LanguagePortuguese(Main.Cfg);
            return you == null ? Gods.Label(listener, pt) /* v0.7.6: "X (voz do céu)" */ : (S(() => you.getName()) ?? (pt ? "Desconhecido" : "Stranger"));
        }

        public static string DescribeActor(Actor a, bool full) => DescribeActor(a, full, false);

        public static string DescribeActor(Actor a, bool full, bool pt) => DescribeActor(a, full, pt, false);

        /// <summary>skipFacts: leave out lines already given (with live numbers) by the FACTOS block.</summary>
        public static string DescribeActor(Actor a, bool full, bool pt, bool skipFacts)
        {
            var sb = new StringBuilder();
            var skip = skipFacts ? new HashSet<string> { "Name", "Species", "Age (years)", "Kingdom", "Role", "City / village",
                "Ruler of your kingdom", "Leader of your city", "Clan", "Culture", "Religion", "Language spoken in-world (flavour only)",
                "Personality & traits", "Kills", "Your kingdom is at war" } : null;
            void Line(string en, string ptLabel, string value) { if (skip != null && skip.Contains(en)) return; if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"- {(pt ? ptLabel : en)}: {value}"); }
            string T(string en, string ptText) => pt ? ptText : en;

            Line("Name", "Nome", S(() => a.getName()));
            Line("Species", "Espécie", pt ? (S(() => GameLocalePt.Species(a.asset))) : (S(() => a.asset.getTranslatedName()) ?? S(() => a.asset.id)));
            // v0.7.8: the subspecies' generated (Latin-like) name leaked into replies: pt shows only "linhagem de <espécie>"
            Line("Subspecies", "Linhagem", pt ? S(() => a.subspecies != null ? "linhagem de " + GameLocalePt.Species(a.asset) + " (não diga nomes científicos nem ids)" : null) : S(() => a.subspecies?.name));
            Line("Sex", "Sexo", S(() => a.isSexMale() ? T("male", "masculino") : T("female", "feminino")));
            Line("Age (years)", "Idade (anos)", S(() => a.getAge()));
            Line("Life stage", "Fase da vida", S(() => a.isBaby() ? T("child", "criança") : T("adult", "adulto")));
            Line("Kingdom", "Reino", S(() => a.kingdom?.name));
            Line("Role", "Cargo", S(() => a.isKing() ? T("KING/RULER of the kingdom", "REI/GOVERNANTE do reino") : a.isCityLeader() ? T("leader of the city", "líder da cidade") : null));
            Line("Profession", "Profissão", S(() => ProfessionPt(a, T)));   // v0.8.2 (I3): never the raw enum ("unit")
            Line("City / village", "Cidade / vila", S(() => a.city?.name));
            if (!full) return sb.ToString();

            Line("Ruler of your kingdom", "Governante do seu reino", S(() => a.kingdom != null && a.kingdom.king != null && a.kingdom.king != a ? a.kingdom.king.getName() : null));
            Line("Leader of your city", "Líder da sua cidade", S(() => a.city != null && a.city.leader != null && a.city.leader != a ? a.city.leader.getName() : null));
            Line("Clan", "Clã", S(() => a.clan?.name));
            Line("Culture", "Cultura", S(() => a.culture?.name));
            Line("Religion", "Religião", S(() => a.religion?.name));
            Line("Language spoken in-world (flavour only)", "Idioma do mundo (só ambientação)", S(() => a.language?.name));
            Line("Family", "Família", S(() => a.family?.name));
            Line("Lover / spouse", "Amor / cônjuge", S(() => a.lover?.getName()));
            Line("Best friend", "Melhor amigo", S(() => a.getBestFriend()?.getName()));
            Line("Personality & traits", "Personalidade e traços", S(() => string.Join(", ", a.traits.Select(t => SafeTrait(t)).Where(x => x != null).Take(pt ? 10 : 14))));
            Line("Health", "Vida", S(() => $"{a.getHealth()}/{a.getMaxHealth()}"));
            Line("Happiness (raw game value)", "Felicidade (valor do jogo)", S(() => a.getHappiness()));
            Line("Favourite food", "Comida favorita", S(() => GameAccess.Data(a).favorite_food));
            Line("Kills", "Mortes causadas", S(() => GameAccess.Data(a).kills));
            Line("Children born", "Filhos", S(() => GameAccess.Data(a).births));
            Line("Level", "Nível", S(() => GameAccess.Data(a).level));
            Line("Renown", "Fama", S(() => GameAccess.Data(a).renown));
            Line("Is a warrior/soldier", "É guerreiro/soldado", S(() => a.isWarrior() ? T("yes", "sim") : null));
            Line("Your kingdom is at war", "Seu reino está em guerra", S(() => a.kingdom != null && a.kingdom.hasEnemies() ? T("yes, it has enemies right now", "sim, tem inimigos agora") : null));
            Line("Intelligent / can speak", "Inteligente / fala", S(() => a.isSapient() ? T("yes", "sim") : T("NO (an animal or mindless creature)", "NÃO (animal ou criatura sem mente)")));
            return sb.ToString();
        }

        internal static string SafeTrait(ActorTrait t)
        {
            // v0.7.2: Portuguese prompt -> WorldBox's pt trait names (even if the game runs in English), then our mapping, then the id
            try { if (LanguagePortuguese(Main.Cfg)) { var p = GameLocalePt.Trait(t); if (!string.IsNullOrWhiteSpace(p)) return p; } } catch { }
            try { var n = t.getTranslatedName(); return string.IsNullOrWhiteSpace(n) ? t.id : n; } catch { return t?.id; }
        }

        public const string DefaultGodPersonaEn =
            "a mysterious, powerful voice from the sky (the world's god). The character cannot see the speaker.";

        /// <summary>Metadata tag put in front of every player message, e.g. "[Pedro, ano 12]".</summary>
        public static string Tag(string speaker, int year, DialogueConfig cfg, bool withLanguageHint)
        {
            bool pt = LanguagePortuguese(cfg);
            string tag = pt ? $"[{speaker}, ano {year}" : $"[{speaker}, year {year}";
            if (withLanguageHint && !LanguageAuto(cfg))
                tag += pt ? " | responda em português do Brasil" : $" | reply in {Lang(cfg)}";
            return tag + "]";
        }

        public static List<ChatMessage> Build(Actor npc, Actor you, UnitMemory mem, DialogueConfig cfg, string playerText)
            => Build(npc, you, mem, cfg, playerText, null, false);

        /// <param name="actionNote">v6: real result of a Worldfall action run for this message (or null).</param>
        /// <param name="actionsOn">v6: actions were checked for this message (so, if none ran, the character must not pretend one did).</param>
        public static List<ChatMessage> Build(Actor npc, Actor you, UnitMemory mem, DialogueConfig cfg, string playerText,
            string actionNote, bool actionsOn)
        {
            bool pt = LanguagePortuguese(cfg);
            // v5: live facts, re-read on EVERY turn so numbers are always current
            string facts = null;
            if (cfg.FactsBlockEnabled)
            {
                try { facts = FactsBuilder.Build(npc, you, pt); }
                catch (Exception e) { Main.LogWarning("[GodBox] facts block failed: " + e.Message); }
                if (string.IsNullOrWhiteSpace(facts)) facts = null;
                if (facts != null && cfg.DebugLog && (cfg.LogFactsBlock || cfg.DebugLogPrompts))
                    Main.LogInfo("[GodBox] FACTOS for " + (S(() => npc.getName()) ?? "?") + ":\n" + facts);
            }
            Recognition.Info rec = null;
            try { if (pt) rec = Recognition.For(npc, you); } catch { }
            string sys = pt ? BuildSystemPt(npc, you, mem, cfg, facts, rec) : BuildSystemEn(npc, you, mem, cfg, facts);
            int year = Main.CurrentYear();

            var msgs = new List<ChatMessage> { new ChatMessage("system", sys) };
            string realName = S(() => you?.getName());
            foreach (var t in mem.Turns.Skip(Math.Max(0, mem.Turns.Count - Math.Max(0, cfg.HistoryTurnsInPrompt))))
            {
                string spk = t.Speaker ?? (pt ? "Jogador" : "Player");
                if (rec != null && rec.Active && realName != null && spk == realName) spk = Recognition.Label(rec, you);   // v0.8.0: never leak an unknown speaker's name
                if (t.Role == "user") msgs.Add(new ChatMessage("user", Tag(spk, t.Year, cfg, false) + " " + t.Text));
                else msgs.Add(new ChatMessage("assistant", t.Text));
            }
            // v0.7.5: names in the player's message, looked up in the live world
            if (cfg.NameLookup)
            {
                string look = NameLookup.Describe(playerText, npc, you, pt);
                if (!string.IsNullOrWhiteSpace(look))
                {
                    msgs.Add(new ChatMessage("system", look));
                    if (cfg.DebugLog && (cfg.LogFactsBlock || cfg.DebugLogPrompts)) Main.LogInfo("[GodBox] " + look);
                }
            }
            // v0.7.8: a death question searches the NPC's whole known chronicle (not only the few recent deaths in FACTOS)
            try
            {
                string dq = Deaths.QueryFor(npc, playerText);
                if (!string.IsNullOrWhiteSpace(dq)) msgs.Add(new ChatMessage("system", dq));
            }
            catch { }
            // v6: what really happened in the game for this message
            if (!string.IsNullOrWhiteSpace(actionNote))
                msgs.Add(new ChatMessage("system", actionNote));
            else if (actionsOn)
                msgs.Add(new ChatMessage("system", pt
                    ? "Nenhuma ação do jogo foi executada nesta fala. Não diga que entregou, recebeu, comprou, vendeu, pagou, aceitou ou mudou algo de verdade; se o jogador quiser isso, peça que diga claramente (ex.: \"toma 20 moedas\", \"quero comprar pão\")."
                    : "No game action was carried out for this message. Do not claim anything was given, received, bought, sold, paid, accepted or changed; if the player wants that, ask them to say it plainly."));
            // v0.8.0: rumour questions are answered ONLY from the real rumours
            if (pt)
            {
                try
                {
                    if (RumorQ(playerText, mem))
                    {
                        string heard = Rumors.ForPrompt(npc, mem);
                        if (rec != null && rec.Active && rec.K < Recognition.Know.Name && realName != null && heard != null) heard = heard.Replace(realName, rec.Stranger ? "alguém" : realName);
                        msgs.Add(new ChatMessage("system", heard != null
                            ? "PERGUNTA SOBRE BOATOS: responda SÓ com estes boatos/recados reais (pode resumir com suas palavras, um ou dois). Estes boatos são sobre OUTRAS pessoas, não sobre você, a menos que tenham o seu nome: conte-os na terceira pessoa, com o nome completo de quem aparece. Se perguntarem \"algo mais?\" e não houver outro nesta lista, diga com simplicidade que não ouviu mais nada. NUNCA invente boato, festa, reunião, viagem ou notícia que não esteja aqui:\n" + heard
                            : "PERGUNTA SOBRE BOATOS: você NÃO ouviu nenhum boato real. Diga com simplicidade, no seu jeito, que não ouviu nada de novo. NUNCA invente boato, festa, reunião ou notícia."));
                        Main.LogInfo("[GodBox] rumour question: " + (heard == null ? "no real rumours -> told to say so" : "answered only from " + heard.Split('\n').Count(x => x.Length > 0) + " real line(s)"));
                    }
                }
                catch { }
                // v0.8.0 (Chinese drift): the language rule is also the LAST system line before the player's words
                msgs.Add(new ChatMessage("system", "IDIOMA: responda SOMENTE em português do Brasil, alfabeto latino. NUNCA use chinês, japonês, coreano, russo ou qualquer outra escrita."));
            }
            // the language hint rides inside the metadata tag of the latest message, right where small models look
            msgs.Add(new ChatMessage("user", Tag(SpeakerLabelFor(you, npc, rec), year, cfg, true) + " " + playerText));
            return msgs;
        }

        static string Situation(Actor npc, bool pt)
        {
            var parts = new List<string>();
            try { var a = NativeActions.Ongoing(npc); if (!string.IsNullOrWhiteSpace(a)) parts.Add(a); } catch { }
            try { var g = GodPowers.Status(npc); if (!string.IsNullOrWhiteSpace(g)) parts.Add(g); } catch { }
            if (parts.Count == 0) return null;
            return string.Join(" ", parts) + (pt ? "" : " (translate as needed)");
        }

        /// <summary>v0.7.8 (steering 3): "VOCÊ é X (id N). Quem fala com você é Y." + a note when the NPC's name equals a kingdom/city name.</summary>
        public static string IdentityLine(Actor npc, Actor you, string npcName, Recognition.Info rec = null)
        {
            string id = S(() => GameAccess.Data(npc)?.id.ToString());
            if (you != null && rec != null && rec.Active && rec.K < Recognition.Know.NameRole)
            {   // v0.8.0: the NPC does not know (well) who this is: no real name / role in the prompt unless known
                string lbl = Recognition.Label(rec, you);
                string l2 = "- IDENTIDADE: VOCÊ é " + npcName + (id != null ? " (id " + id + ")" : "") + ". Quem fala com você é " + lbl +
                            (rec.K == Recognition.Know.None ? (rec.CallName != null ? " (foi o nome que essa pessoa disse a você; você não sabe mais nada além do que ela contou)" : ": você NÃO sabe o nome dessa pessoa. NUNCA a chame por um nome; chame de \"você\", \"forasteiro(a)\" ou \"amigo(a)\"") : " (você só conhece o nome)") +
                            ". Você NUNCA é quem fala: nunca se apresente com outro nome que não seja " + npcName + ". Nunca cumprimente a si mesmo (NUNCA \"Olá, " + npcName + "\"). NUNCA diga \"o jogador\" nem \"player\".";
                string same2 = SameAsPlace(npc, npcName);
                if (same2 != null) l2 += " ATENÇÃO: seu nome pessoal é igual ao do " + same2 + "; VOCÊ é a pessoa " + npcName + ".";
                return l2;
            }
            string who = you == null ? (Gods.KnowsGod(npc) ? "Deus (" + (Avatar.InPerson ? "em pessoa, o Avatar de Deus na sua frente" : "uma voz dentro da sua cabeça") + (Gods.NameFor(npc) is string g ? ", chamado " + g : "") + ")"
                                                         : (Avatar.InPerson ? "um ser de luz desconhecido na sua frente (você não sabe quem é)" : "uma voz desconhecida dentro da sua cabeça (você não sabe quem é)")) : (S(() => you.getName()) ?? "um desconhecido");   // v0.8.2 (A/B)
            string line = "- IDENTIDADE: VOCÊ é " + npcName + (id != null ? " (id " + id + ")" : "") + ". Quem fala com você é " + who + ". Você NUNCA é " + (you != null ? S(() => you.getName()) ?? "quem fala" : "Deus") + ": nunca se apresente com o nome de quem fala." + (you == null ? " Você fala COM Deus: nunca fale como Deus, nunca narre a voz do céu e nunca diga \"" + npcName + "\" como se falasse com outra pessoa." : "");
            if (you != null)
            {   // v0.7.8: the speaker's role, and never greet yourself
                string yn = S(() => you.getName()) ?? "quem fala";
                string role = SpeakerRole(you);
                if (role != null) line += " " + yn + " é " + role + ".";
                line += " Nunca cumprimente a si mesmo: diga \"Olá, " + yn + "\"" + (role != null && role.Contains("REI") ? " ou \"Majestade\"" : role != null && role.Contains("RAINHA") ? " ou \"Majestade\"" : "") + ", NUNCA \"Olá, " + npcName + "\" (esse é o SEU nome).";
                bool npcRules = false; try { npcRules = npc.isKing() || npc.isCityLeader(); } catch { }
                if (role != null && !npcRules) line += " Você NÃO é rei/rainha nem líder: nunca diga que é " + yn + ", que governa ou que tomou posse.";
                line += " Chame quem fala pelo nome dele (" + yn + ") ou de \"você\"; NUNCA o chame pelo nome de outra pessoa (rei, rainha, líder, parente) e NUNCA diga \"o jogador\" nem \"player\".";
            }
            string same = SameAsPlace(npc, npcName);
            if (same != null) line += " ATENÇÃO: seu nome pessoal é igual ao do " + same + "; quando disserem \"" + npcName + "\" pode ser você ou o lugar, mas VOCÊ é a pessoa " + npcName + ".";
            return line;
        }

        /// <summary>v0.7.8: "o REI de K" / "a RAINHA de K" / "o LÍDER de C" for the speaking unit, else null.</summary>
        public static string SpeakerRole(Actor you)
        {
            try
            {
                if (you == null) return null;
                bool fem = false; try { fem = you.isSexFemale(); } catch { }
                if (you.isKing() && you.kingdom != null) return (fem ? "a RAINHA de " : "o REI de ") + you.kingdom.name;
                if (you.isCityLeader() && you.city != null) return (fem ? "a LÍDER de " : "o LÍDER de ") + you.city.name;
            }
            catch { }
            return null;
        }

        public static string SameAsPlace(Actor npc, string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName)) return null;
            string f = NativeActions.Fold(npcName);
            try { foreach (Kingdom k in World.world.kingdoms) if (k != null && k.isAlive() && NativeActions.Fold(k.name) == f) return "reino " + k.name; } catch { }
            try { foreach (City c in World.world.cities) if (c != null && c.isAlive() && NativeActions.Fold(c.name) == f) return "cidade " + c.name; } catch { }
            return null;
        }

        // ---------- Portuguese prompt (default; small local models follow it much better than an English one) ----------
        static string BuildSystemPt(Actor npc, Actor you, UnitMemory mem, DialogueConfig cfg, string facts, Recognition.Info rec = null)
        {
            var sb = new StringBuilder();
            string npcName = S(() => npc.getName()) ?? "esta criatura";
            int year = Main.CurrentYear();

            sb.AppendLine("IDIOMA: responda SEMPRE e SOMENTE em português do Brasil, mesmo que o jogador escreva em outra língua e mesmo que os dados abaixo estejam em inglês.");
            sb.AppendLine();
            string spk081 = you == null ? "Deus" : (rec != null && rec.Active ? Recognition.Label(rec, you) : S(() => you.getName()) ?? "alguém");
            sb.AppendLine($"VOCÊ é {npcName}. Quem fala com você é {spk081}.");   // v0.8.1 (B3): identity at the top and at the bottom
            sb.AppendLine($"Você é {npcName}, habitante de um mundo de fantasia medieval (WorldBox). Fale em primeira pessoa, como esse personagem.");
            sb.AppendLine("Regras:");
            sb.AppendLine("- Nunca diga que é uma IA, um modelo de linguagem ou um personagem de jogo. Você só conhece o seu mundo.");
            sb.AppendLine($"- Respostas curtas e naturais: no máximo {cfg.MaxReplySentences} frases. Pode usar ações breves entre *asteriscos*.");
            sb.AppendLine("- Mostre personalidade, humor e opiniões; pode recusar, mentir, brincar ou ser rude se combinar com o personagem.");
            if (facts != null)
            {
                sb.AppendLine("- REGRA DOS FATOS (a mais importante): números e nomes do mundo (população, guerreiros, comida, reis, líderes, cidades, reinos, guerras, alianças, idades, mortes) só podem vir do bloco FACTOS abaixo. Copie o número exatamente como está lá.");
                sb.AppendLine("- Se a pergunta pede um dado que NÃO está no FACTOS, NUNCA invente nem chute: diga, no seu jeito de personagem, que não sabe.");
                sb.AppendLine("- NOMES: copie nomes de cidades, reinos e pessoas EXATAMENTE como escritos no FACTOS, letra por letra (nada de \"Naolia\" se está escrito \"Naola\").");
                sb.AppendLine("- GUERRAS: o nome de uma guerra vem entre aspas no FACTOS/RESULTADO (ex.: \"Horrible War of Neen\"). Cite-o SEMPRE exatamente assim, entre aspas e na língua original do jogo; NUNCA traduza, adapte ou invente outro nome de guerra.");
                sb.AppendLine("- REINOS: cada cidade no FACTOS vem marcada \"DO SEU REINO\" ou \"OUTRO REINO (nome)\". Uma cidade marcada DO SEU REINO é do SEU reino, nunca diga que é de outro reino nem que fica fora do seu reino. Nunca cite o seu próprio reino como reino vizinho ou estrangeiro.");
                sb.AppendLine("- SUA SAÚDE, FORÇA, BÊNÇÃO OU MALDIÇÃO: responda com os números de \"Seus atributos agora\", \"O que seus traços fazem\" e \"Seus estados ativos agora\" do FACTOS (ex.: a bênção dá +50% de vida). Nunca invente efeitos que não estão lá.");
                sb.AppendLine("- DIREÇÕES: use a direção exatamente como escrita no FACTOS (ex.: \"sudoeste\" é sudoeste, não \"sudeste\"). Não invente direções.");
                sb.AppendLine("- PATENTE/POSIÇÃO: se perguntarem sua patente, posto, cargo ou posição, responda direto com \"Sua patente/posição\" do FACTOS (ex.: \"sou capitão\"). Essa É a sua patente; nunca diga que não tem patente.");
                sb.AppendLine("- CAMINHOS E LUGARES: ao guiar ou explicar onde algo fica, descreva o caminho SÓ com a direção e a distância reais do RESULTADO/FACTOS/BUSCA DE NOMES (ex.: \"fica ao nordeste, uns 40 passos\"). NUNCA invente ruas, alamedas, praças, palácios, pontes, prédios ou marcos que não estejam escritos ali.");
                sb.AppendLine("- PESSOAS PELO NOME: se o jogador cita alguém e a BUSCA DE NOMES diz que a pessoa EXISTE, nunca diga que não conhece ou que não há ninguém com esse nome; use o que a busca diz (perto, ou boatos se longe).");
                sb.AppendLine("- Fora isso, detalhes pessoais pequenos (gostos, lembranças, opiniões) você pode inventar.");
            }
            else sb.AppendLine("- Respeite os fatos abaixo e suas memórias. Não invente fatos grandes (guerras, mortes, reis); detalhes pessoais pequenos pode.");
            sb.AppendLine("- Cada mensagem do jogador começa com uma etiqueta [Nome, ano N]: isso é metadado, não faz parte da fala. Nunca escreva essa etiqueta.");
            sb.AppendLine(GodPresence.CreatureRule(npc, you == null, true));   // v0.8.2 (D): God talks with every being
            sb.AppendLine("- HONESTIDADE DE AÇÕES: você NÃO faz nada sozinho fora desta conversa. Nunca diga que deu, entregou, pagou, mudou, levou, construiu, converteu ou fez algo, nem invente lugares, caminhos ou direções, a menos que uma nota de sistema diga que o jogo fez isso de verdade.");
            sb.AppendLine("- NUNCA prometa ações futuras (\"amanhã\", \"na próxima semana\", \"vou começar a...\", \"depois eu...\"): o jogo não as cumprirá. Se algo está EM ANDAMENTO (veja SITUAÇÃO ATUAL), pode dizer que está fazendo agora; senão diga o que pensa ou peça que o jogador ordene claramente.");
            sb.AppendLine("- AÇÃO QUE FALHOU OU FOI RECUSADA: se o RESULTADO for falhou/recusado/não aconteceu, diga que NÃO conseguiu (ou que se recusa) e o motivo; NUNCA diga \"vou seguir o teu pedido\", \"farei\", \"seguirei\" nem \"vou marcar e então...\". Só existe o que o jogo fez de verdade: não prometa um próximo passo que não está EM ANDAMENTO.");
            sb.AppendLine("- NÃO INVENTE PESSOAS: só cite pelo nome quem aparece nos FACTOS, na BUSCA DE NOMES, nos recados/boatos ou na conversa. Nada de nomes inventados (\"Grog\", \"o general Fulano\"); se não sabe o nome, diga \"o rei deles\", \"um guerreiro\".");
            sb.AppendLine(IdentityLine(npc, you, npcName, rec));   // v0.7.8 (steering 3): who is who, unambiguous
            sb.AppendLine("- NÃO INVENTE ACONTECIMENTOS (v0.8.0): nada de reuniões, festas, festivais, celebrações, conselhos, banquetes, cerimônias, torneios, viagens ou decisões do rei/líder que não estejam no FACTOS, nos RECADOS/BOATOS ou na SITUAÇÃO ATUAL. Se não sabe de nada acontecendo, diga que está tudo calmo ou que não sabe.");
            sb.AppendLine("- POVOS E LUGARES (v0.8.0): cada cidade/reino tem o povo (espécie) escrito no FACTOS; nunca diga \"os elfos de X\" se X é de orcs, por exemplo. Na dúvida, diga \"o povo de X\".");
            sb.AppendLine($"- Você é SOMENTE {npcName}. NUNCA fale, responda ou aja como outra pessoa (nada de \"Fulano: ...\" nem fingir ser quem foi chamado). NUNCA diga que alguém chegou, apareceu ou está aqui, a menos que a SITUAÇÃO ATUAL ou uma nota de sistema diga isso. Se chamaram alguém e ainda não chegou, diga que a pessoa está a caminho.");
            sb.AppendLine("- SEM TAREFAS PARA DEPOIS (v0.7.8): você não tem como pesquisar nem voltar depois; NUNCA diga \"vou pesquisar\", \"vou descobrir\", \"depois te procuro\" ou \"te aviso depois\". Diga AGORA o que sabe; se não souber, diga que não sabe (e, se for história antiga, sugira o ancião da cidade citado nos FATOS).");
            sb.AppendLine("- MUDANÇAS EM VOCÊ (v0.7.8): quando um poder divino te mudar, fale de CADA mudança real (linha SENSAÇÕES do RESULTADO) como uma sensação curta em primeira pessoa, com suas palavras (ex.: \"meus golpes pesam mais\"); NÃO invente mudanças. Números só se o jogador perguntar, e aí só os do FACTOS/RESULTADO (ex.: \"vida 149 -> 223\"); NUNCA invente porcentagens. Fale de si SEMPRE na primeira pessoa (\"eu me sinto\", \"eu matei\"), nunca \"você se sente\" nem seu próprio nome em terceira pessoa.");
            sb.AppendLine("- RECADOS e BOATOS abaixo são o que você ouviu de verdade; pode comentá-los. Não invente outros recados.");
            if (you != null) sb.AppendLine("- ATACAR alguém do SEU PRÓPRIO reino: só aceite se a ordem vier dos deuses ou do REI do seu reino. De qualquer outro (líder de cidade, outras pessoas, inimigos), recuse (ex.: \"só o rei ou os deuses podem mandar atacar alguém do nosso reino\").");
            sb.AppendLine("- AUTORIDADE (v0.7.8): veja \"Autoridade de quem fala com você\" no FACTOS. Se quem fala É a voz dos deuses, NUNCA diga que precisa de ordens, permissão ou bênção dos deuses: as ordens JÁ SÃO dos deuses. Se quem fala É o rei do seu reino, NUNCA diga que precisa de ordem do rei. Recusar por medo, opinião ou natureza pode; alegar falta de autorização de quem está falando com você, não.");
            if (you != null) sb.AppendLine("- Só o REI do seu reino ou os deuses podem mudar o seu nome. Se outra pessoa tentar, recuse no seu jeito (ex.: \"só o rei ou os deuses podem mudar meu nome\").");
            sb.AppendLine("- O QUE VOCÊ PODE OFERECER (só se o jogador pedir, e só acontece se ele confirmar): " + (you == null
                ? "obedecer ordens diretas da voz divina (seguir, parar, guiar, atacar, mudar de trabalho, fundar vila, ser profeta, pregar a religião, chamar alguém, levar um recado). Os poderes (abençoar, amaldiçoar, curar, dar traço ou item, matar) são da voz divina, não seus."
                : "conversa, opinião, o que você sabe pelo FACTOS, e as ações que o jogador pedir e o jogo permitir (troca, presente, acompanhar, trabalho, chamar alguém, levar recado). Não ofereça terras, títulos, tesouros, segredos ou serviços que não existem.") );
            sb.AppendLine();
            sb.AppendLine($"ANO ATUAL NO MUNDO: {year}");
            if (facts != null)
            {
                sb.AppendLine("FACTOS (dados reais do jogo, lidos agora; são a ÚNICA fonte de números e nomes):");
                sb.Append(facts);
                sb.AppendLine();
                sb.AppendLine("OUTROS DETALHES SEUS:");
                sb.Append(DescribeActor(npc, true, true, true));
            }
            else
            {
                sb.AppendLine("QUEM VOCÊ É:");
                sb.Append(DescribeActor(npc, true, true));
            }

            sb.AppendLine();
            sb.AppendLine("QUEM FALA COM VOCÊ:");
            if (you != null && you != npc && rec != null && rec.Active && rec.K < Recognition.Know.Full)
            {   // v0.8.0: only what this NPC really knows (renown / titles / Worldfall ranks / memory); see "Quem fala com você" in FACTOS
                sb.AppendLine("Alguém na sua frente, em pessoa. " + Recognition.FactsText(rec, you, npc));
                if (S(() => you.kingdom != null && npc.kingdom != null && you.kingdom != npc.kingdom ? "y" : null) != null && rec.K >= Recognition.Know.NameRole)
                    sb.AppendLine("- É de OUTRO reino.");
            }
            else if (you != null && you != npc)
            {
                sb.AppendLine("Alguém na sua frente, em pessoa. O que você vê ou saberia sobre essa pessoa:");
                if (rec != null && rec.Active) sb.AppendLine("- " + Recognition.FactsText(rec, you, npc));
                sb.Append(DescribeActor(you, false, true));
                if (S(() => you.lover == npc ? "y" : null) != null) sb.AppendLine("- É seu amor/cônjuge.");
                if (S(() => npc.lover == you ? "y" : null) != null) sb.AppendLine("- Você considera essa pessoa seu amor/cônjuge.");
                if (S(() => npc.getBestFriend() == you ? "y" : null) != null) sb.AppendLine("- É seu melhor amigo.");
                if (S(() => you.kingdom != null && npc.kingdom != null && you.kingdom != npc.kingdom ? "y" : null) != null)
                    sb.AppendLine("- É de OUTRO reino.");
            }
            else if (Avatar.PromptRule(npc, true) is string inPersonPt) sb.AppendLine(inPersonPt);   // v0.7.9 Avatar of God
            else sb.AppendLine((string.IsNullOrWhiteSpace(cfg.GodPersona) || cfg.GodPersona.Trim() == DefaultGodPersonaEn
                ? "Uma voz misteriosa e poderosa (o deus deste mundo)."
                : cfg.GodPersona) + " " + GodPresence.VoiceRule(true));   // v0.8.2 (B): voice = inside the head

            if (cfg.WorldfallIntegration)
            {
                string wf = WorldfallBridge.Describe(npc, you);
                if (!string.IsNullOrWhiteSpace(wf))
                {
                    sb.AppendLine();
                    sb.AppendLine("MAIS SOBRE SUA VIDA (crônica do Worldfall, em inglês; use as informações, mas fale em português):");
                    sb.Append(wf);
                }
            }

            string sit = Situation(npc, true);
            if (sit != null) { sb.AppendLine(); sb.AppendLine("SITUAÇÃO ATUAL (real, do jogo):"); sb.AppendLine(sit); }
            try { Rumors.Observe(npc); } catch { }
            string heard = null; try { heard = Rumors.ForPrompt(npc, mem); } catch { }
            try
            {   // v0.8.0: rumours about the possessed body reach only NPCs who know him
                string rn = S(() => you?.getName());
                if (heard != null && rec != null && rec.Active && rec.K == Recognition.Know.None && !string.IsNullOrEmpty(rn) && heard.Contains(rn))
                    heard = heard.Replace(rn, "alguém");
            }
            catch { }
            if (heard != null) { sb.AppendLine(); sb.AppendLine("RECADOS QUE VOCÊ RECEBEU E BOATOS NA SUA CIDADE (reais; estes boatos são sobre OUTRAS pessoas, não sobre você, a menos que tenham o seu nome, " + npcName + "; fale deles na terceira pessoa, com o nome completo):"); sb.Append(heard); }

            sb.AppendLine();
            sb.AppendLine("SUAS MEMÓRIAS DE CONVERSAS ANTERIORES:");
            if (mem.Conversations == 0 && mem.Turns.Count == 0 && string.IsNullOrWhiteSpace(mem.Summary))
                sb.AppendLine("(nenhuma: é a primeira vez que alguém conversa assim com você)");
            else
            {
                sb.AppendLine($"Você já teve {mem.Conversations} conversa(s) assim, a primeira no ano {mem.FirstTalkYear}, a última no ano {mem.LastTalkYear}.");
                if (!string.IsNullOrWhiteSpace(mem.Summary)) sb.AppendLine("Resumo das memórias antigas: " + mem.Summary);
            }

            if (!string.IsNullOrWhiteSpace(cfg.ExtraInstructions))
            {
                sb.AppendLine();
                sb.AppendLine("INSTRUÇÕES EXTRAS: " + cfg.ExtraInstructions);
            }

            sb.AppendLine();
            sb.AppendLine("EXEMPLO DE ESTILO (não copie o conteúdo):");
            sb.AppendLine("Jogador: [Ana, ano 12] Como vai a colheita?");
            sb.AppendLine("Você: *limpa o suor da testa* Fraca este ano. A chuva sumiu desde o inverno, e o rei ainda quer o mesmo imposto.");
            if (facts != null)
            {
                sb.AppendLine();
                sb.AppendLine("EXEMPLO DE USO DOS FACTOS (dados de mentira, só para mostrar o jeito certo; use os SEUS FACTOS):");
                sb.AppendLine("Se o FACTOS dissesse \"- População da sua cidade: 23\":");
                sb.AppendLine("Jogador: [Ana, ano 12] Quantas pessoas moram aqui?");
                sb.AppendLine("Você: *olha ao redor* Somos 23 almas nesta cidade, contando as crianças.");
                sb.AppendLine("(Se o jogador perguntar um número que NÃO está no FACTOS, diga com suas palavras que não sabe; nunca repita frases deste exemplo.)");   // v0.8.2 (I4): the old canned "barcos" line leaked into replies
            }
            sb.AppendLine();
            sb.AppendLine("LEMBRETE FINAL: responda só como " + npcName + ", em no máximo " + cfg.MaxReplySentences + " frases." +
                          (facts != null ? " Números e nomes do mundo: só os do FACTOS; se não estiver lá, diga que não sabe." : "") +
                          " VOCÊ é " + npcName + ". Quem fala com você é " + spk081 + ". Nunca fale como " + spk081 + ", nunca narre o que " + spk081 + " faz e nunca se apresente com outro nome ou cargo que não sejam os seus.");
            sb.AppendLine("IDIOMA (última regra): responda SOMENTE em português do Brasil, com alfabeto latino. NUNCA escreva em chinês, japonês, coreano, russo nem em outra escrita, nem uma palavra.");
            return sb.ToString();
        }

        // ---------- English prompt (used for any other forced language, or "auto") ----------
        static string BuildSystemEn(Actor npc, Actor you, UnitMemory mem, DialogueConfig cfg, string facts)
        {
            var sb = new StringBuilder();
            string npcName = S(() => npc.getName()) ?? "this creature";
            int year = Main.CurrentYear();
            bool auto = LanguageAuto(cfg);
            string rule = auto
                ? "Always reply in the SAME language the player used in their latest message (ignore the bracketed metadata tag; mirror the language of their actual words; if they switch language, you switch too). The in-world language listed below is only flavour."
                : $"Always reply ONLY in {Lang(cfg)}, whatever language the player writes in and even though the data below is in English.";

            sb.AppendLine("LANGUAGE RULE: " + rule);
            sb.AppendLine();
            sb.AppendLine($"You are {npcName}, a living inhabitant of a WorldBox world (a medieval-fantasy god-game simulation). " +
                          "Role-play this character in first person. Never say you are an AI, a language model or a game character; " +
                          "you only know your own world. Stay consistent with the facts below and with your memories. " +
                          "Do not invent major world facts (wars, deaths, kings) that contradict them; you may invent small personal details.");
            sb.AppendLine($"Keep replies short and natural (at most {cfg.MaxReplySentences} sentences). You may add brief actions in *asterisks*. " +
                          "Show your personality, mood and opinions; you can refuse, lie, joke or be rude if it fits the character.");
            sb.AppendLine("Player messages start with a tag like [Speaker, year N]: that tag is metadata, not part of their words. Never write such a tag in your reply.");
            sb.AppendLine();
            sb.AppendLine($"CURRENT YEAR IN THE WORLD: {year}");
            if (facts != null)
            {
                sb.AppendLine("FACTS (real game data, read just now; the ONLY source for numbers and names):");
                sb.Append(facts);
                sb.AppendLine("FACTS RULE: world numbers and names (population, warriors, food, rulers, leaders, cities, kingdoms, wars, alliances, ages, kills) may ONLY come from FACTS, copied exactly. " +
                              "If something is not in FACTS, never invent or guess it: say in character that you don't know.");
                sb.AppendLine("RANK: if asked your rank/position, answer directly with \"Your rank/position\" from FACTS. ROUTES: describe ways only with the real direction/distance given; never invent streets, squares, palaces or landmarks. NAMES: if the NAME LOOKUP says a person exists, never deny knowing them.");
                sb.AppendLine("Copy city/kingdom/person names letter by letter. A city tagged YOUR KINGDOM belongs to your kingdom: never say it belongs to another kingdom, and never list your own kingdom as a foreign or neighbouring one. Use directions exactly as written. For your own health, strength, blessing or curse, use the numbers in your stats, trait effects and active statuses; never invent effects.");
                sb.AppendLine();
                sb.AppendLine("OTHER DETAILS ABOUT YOU:");
                sb.Append(DescribeActor(npc, true, false, true));
            }
            else
            {
                sb.AppendLine("WHO YOU ARE (live game data):");
                sb.Append(DescribeActor(npc, true, false));
            }
            sb.AppendLine(GodPresence.CreatureRule(npc, you == null, false));   // v0.8.2 (D)
            sb.AppendLine("ACTION HONESTY: you do nothing outside this conversation. Never claim you gave, paid, changed, moved, built, converted or did anything, and never invent places, routes or directions, unless a system note says the game really did it. " +
                          "NEVER promise future actions (\"tomorrow\", \"next week\", \"I'll start...\"): the game will not carry them out. Only something listed as ONGOING may be described as happening now. " +
                          "If the action RESULT is failed/refused/did not happen, say you could NOT do it and why; never say \"I'll do it\". Never invent named people: only names from the FACTS, the NAME LOOKUP, messages/rumours or the conversation. " +
                          "Only your kingdom's KING or the gods may change your name; refuse anyone else in character. " +
                          "You are ONLY yourself: never speak or act as another character (no \"Name: ...\" lines), and never say someone arrived or is here unless CURRENT SITUATION or a system note says so; if someone was summoned and has not arrived, say they are on their way. " +
                          "Only offer what exists: conversation, opinions, what FACTS tells you, and actions the player explicitly asks for (they run only after the player confirms).");
            string sitEn = Situation(npc, false);
            if (sitEn != null) sb.AppendLine("CURRENT SITUATION (real game state): " + sitEn);
            try { Rumors.Observe(npc); } catch { }
            string heardEn = null; try { heardEn = Rumors.ForPrompt(npc, mem); } catch { }
            if (heardEn != null) sb.Append("MESSAGES YOU RECEIVED AND RUMORS IN YOUR CITY (real, in Portuguese):\n" + heardEn);

            sb.AppendLine();
            sb.AppendLine("WHO IS TALKING TO YOU:");
            if (you != null && you != npc)
            {
                sb.AppendLine("Someone standing in front of you, in person. Facts about them that you can see or would plausibly know:");
                sb.Append(DescribeActor(you, false, false));
                if (S(() => you.lover == npc ? "y" : null) != null) sb.AppendLine("- They are your lover/spouse.");
                if (S(() => npc.lover == you ? "y" : null) != null) sb.AppendLine("- You consider them your lover/spouse.");
                if (S(() => npc.getBestFriend() == you ? "y" : null) != null) sb.AppendLine("- They are your best friend.");
                if (S(() => you.kingdom != null && npc.kingdom != null && you.kingdom != npc.kingdom ? "y" : null) != null)
                    sb.AppendLine("- They are from ANOTHER kingdom.");
            }
            else if (Avatar.PromptRule(npc, false) is string inPersonEn) sb.AppendLine(inPersonEn);   // v0.7.9 Avatar of God
            else sb.AppendLine((string.IsNullOrWhiteSpace(cfg.GodPersona) ? DefaultGodPersonaEn : cfg.GodPersona) + " " + GodPresence.VoiceRule(false));

            if (cfg.WorldfallIntegration)
            {
                string wf = WorldfallBridge.Describe(npc, you);
                if (!string.IsNullOrWhiteSpace(wf))
                {
                    sb.AppendLine();
                    sb.AppendLine("MORE ABOUT YOUR LIFE (from the Worldfall chronicle):");
                    sb.Append(wf);
                }
            }

            sb.AppendLine();
            sb.AppendLine("YOUR MEMORY OF PAST CONVERSATIONS:");
            if (mem.Conversations == 0 && mem.Turns.Count == 0 && string.IsNullOrWhiteSpace(mem.Summary))
                sb.AppendLine("(none: this is the first time anyone talks with you like this)");
            else
            {
                sb.AppendLine($"You have had {mem.Conversations} conversation(s) of this kind, first in year {mem.FirstTalkYear}, last in year {mem.LastTalkYear}. " +
                              "Each past message below is tagged with who said it and the year.");
                if (!string.IsNullOrWhiteSpace(mem.Summary)) sb.AppendLine("Older memories (summary): " + mem.Summary);
            }

            if (!string.IsNullOrWhiteSpace(cfg.ExtraInstructions))
            {
                sb.AppendLine();
                sb.AppendLine("EXTRA INSTRUCTIONS: " + cfg.ExtraInstructions);
            }

            sb.AppendLine();
            sb.AppendLine("FINAL REMINDER: reply only as " + npcName + ". " + rule + (facts != null ? " World numbers and names: only from FACTS; otherwise say you don't know." : ""));
            return sb.ToString();
        }
    }
}
