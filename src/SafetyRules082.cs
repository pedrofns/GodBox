using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WorldfallAIChat
{
    /// <summary>v0.8.2: pure text rules (no game types) so the test project can check them.</summary>
    public static partial class SafetyRules
    {
        static string Clean082(string text) => Regex.Replace(Fold(text ?? "").Trim(), @"\s+", " ");

        // ---------------- F3: acknowledgements never re-run an action ----------------
        static readonly Regex AckRx = new Regex(@"^(?:(?:ah|ahh|hm+|bem|pois bem|entao|muito bem)[, ]+)?(otimo|otima|ok|okay|okey|certo|certinho|tudo bem|ta bem|esta bem|ta certo|esta certo|que assim seja|assim seja|seja feito|muito bem|perfeito|excelente|maravilha|maravilhoso|beleza|entendi|entendo|compreendo|compreendi|show|legal|bom|bom saber|que bom|isso|isso mesmo|exato|exatamente|amem|bem feito|bom trabalho|fico feliz|fico contente|obrigado|obrigada|valeu|sim|claro|hum|hmm|aham|uhum)(?:[, ]+(?:entao|mesmo|meu filho|minha filha|filho|filha|meu profeta|minha profetisa|muito bem|otimo|perfeito|assim seja|que assim seja|amem|bom trabalho|obrigado|obrigada|isso))*[\s.!…]*$", CI);
        /// <summary>"ótimo", "ok", "que assim seja", "tudo bem"…: an acknowledgement, never an order.</summary>
        public static bool IsAcknowledgement(string text)
        {
            string f = Clean082(text);
            if (f.Length == 0 || f.Length > 60) return false;
            return AckRx.IsMatch(f);
        }

        // ---------------- F1: future / intent statements never run actions ----------------
        static readonly Regex FutureTimeRx = new Regex(@"\b((vem|vira|chegara|ha de chegar|ha de vir|esta chegando|chega) (o|um|a|uma) (tempo|dia|momento|hora|era|epoca)|um dia|algum dia|no futuro|futuramente|em breve|mais tarde|mais adiante|quando chegar a hora|quando for a hora|na hora certa|no tempo certo|no momento certo|a seu tempo|ao seu tempo|com o tempo|amanha|depois|daqui a pouco|um belo dia|quando eu quiser|quando eu decidir|ainda vou|ainda irei)\b", CI);
        static readonly Regex SynthFutureRx = new Regex(@"\b(?!(?:serei|sera)\b)[a-z]{2,}(arei|erei|irei|orei|arao|erao|irao|aremos|eremos|iremos)\b", CI);
        static readonly Regex VouInfRx = new Regex(@"\b(vou|irei|iremos|vamos|pretendo|planejo|penso em|hei de)\s+(te |lhe |o |a |vos )?[a-z]+(ar|er|ir|or)\b", CI);
        static readonly Regex MoveLeadRx = new Regex(@"^(va|vai|ande|caminhe|siga|parta|viaje|corra|marche|volte|retorne|se teleporte|teleporte|se mova|se dirija|dirija-se|leve)\b", CI);
        /// <summary>"vem o tempo que escolherei um profeta", "um dia eu te abençoarei": a statement about the future, not an order now.</summary>
        public static bool IsFutureIntent(string text)
        {
            string f = Clean082(text);
            if (f.Length == 0) return false;
            if (Regex.IsMatch(f, @"^(se|caso|quando voce|quando tu|assim que voce|logo que voce)\b", CI)) return false;   // conditional deals (Promises)
            if (MoveLeadRx.IsMatch(f)) return false;                                                                         // "vá até X que eu te abençoarei"
            if (Regex.IsMatch(f, @"\b(agora|imediatamente|neste instante|nesse instante|ja ja)\b", CI)) return false;
            bool time = FutureTimeRx.IsMatch(f);
            bool synth = SynthFutureRx.IsMatch(Regex.Replace(f, @"\b(irei|iremos)\b", ""));   // "irei" alone is "vou"
            bool vou = VouInfRx.IsMatch(f);
            if (time && (synth || vou)) return true;
            if (Regex.IsMatch(f, @"\b(vem|vira|chegara|ha de chegar|ha de vir) (o|um|a|uma) (tempo|dia|momento|hora|era|epoca)\b", CI)) return true;
            // "Eu escolherei um profeta." : a 1st-person synthetic future at the start of the sentence
            if (Regex.IsMatch(f, @"^(e |mas )?(eu )?(te |lhe |vos |o |a )?[a-z]{2,}(arei|erei|irei)\b", CI) && !Regex.IsMatch(f, @"^(e |mas )?(eu )?irei\b", CI)) return true;
            return false;
        }

        // ---------------- F4: conditionals and promises run nothing ----------------
        static readonly Regex DealRx = new Regex(@"(\bpreciso que (voce |vc |tu )?(me )?(prometa|jure|promete|jura)\b|\b(se|caso) (voce |vc |tu )?(me )?(prometer|jurar|prometeres|jurares|der sua palavra|der a sua palavra)\b|\b(desde que|contanto que|com a condicao de|com a condicao que|sob a condicao|em troca|so se|somente se|apenas se)\b|\b(mas|porem|so que|antes)\b[^.!?]*\b(prometa|jure|me prometa|me jure|preciso que)\b|\bprometa(-me)? que\b|\bme prometa\b|\bjure(-me)? que\b)", CI);
        /// <summary>"vou retirar sua maldição, mas preciso que me prometa…": a deal; nothing runs now, the NPC answers with a promise.</summary>
        public static bool IsConditionalDeal(string text)
        {
            string f = Clean082(text);
            if (f.Length == 0) return false;
            if (MoveLeadRx.IsMatch(f) && !Regex.IsMatch(f, @"\b(prometa|jure)\b", CI)) return false;   // "vá até X que eu retiro…" stays with Promises
            return DealRx.IsMatch(f);
        }

        // ---------------- F2: revoke_prophet ----------------
        static readonly Regex RevokeRx = new Regex(@"(\beu nao (te|o|a|lhe) escolhi\b|\bnao (te|o|a) escolhi\b|\bnao (es|e|eh|sera|seras) mais (o |a )?(meu|minha) (profeta|profetisa|escolhid[oa])\b|\b(voce|tu|vc) nao (e|es|eh) (o |a )?(meu|minha) (profeta|profetisa|escolhid[oa])\b|\bretiro (o |a )?(teu|seu|tua|sua) (dom|titulo|posto|bencao|cargo|manto|chamado) de (profeta|profetisa)\b|\bretiro (o |a )?(teu|seu|tua|sua) (dom|titulo|posto|cargo|manto|chamado) profetico\b|\b(deixa|deixe|deixas) de ser (o |a )?(meu|minha) (profeta|profetisa)\b|\b(revogo|retiro|tiro|anulo) (a |o )?(tua |sua |teu |seu )?(condicao|titulo|posto|status) de (profeta|profetisa)\b|\bvoce nao e profeta\b|\btu nao es profeta\b)", CI);
        /// <summary>"eu não te escolhi", "não és mais meu profeta", "retiro teu dom de profeta".</summary>
        public static bool IsRevokeProphet(string text)
        {
            string f = Clean082(text);
            if (f.Length == 0 || f.EndsWith("?")) return false;
            if (IsHypothetical(text)) return false;
            return RevokeRx.IsMatch(f);
        }

        // ---------------- F5: temple deadlines ----------------
        static readonly Regex TempleDeadlineRx = new Regex(@"(?:,\s*|\s+)(?:e\s+)?(?:(?:voce|vc|tu)\s+(?:tem|tens|tera|teras)\s+(?:ate\s+)?|em\s+(?:no maximo\s+|ate\s+)?|dentro de\s+|no prazo de\s+|num prazo de\s+|antes de\s+|ate\s+)(\d{1,3}|um|uma|dois|duas|tres|quatro|cinco|seis|sete|oito|nove|dez|onze|doze|quinze|vinte|trinta|cinquenta|cem)\s+anos?\b(?:\s+(?:para|pra)\s+(?:isso|faze-lo|terminar|construir|erguer)\b)?", CI);
        static readonly Dictionary<string, int> SmallNums082 = new Dictionary<string, int>
        { {"um",1},{"uma",1},{"dois",2},{"duas",2},{"tres",3},{"quatro",4},{"cinco",5},{"seis",6},{"sete",7},{"oito",8},{"nove",9},{"dez",10},{"onze",11},{"doze",12},{"quinze",15},{"vinte",20},{"trinta",30},{"cinquenta",50},{"cem",100} };
        /// <summary>A deadline in a temple order ("você tem 2 anos", "em 3 anos"); years = 0 when none.</summary>
        public static bool ParseTempleDeadline(string text, out int years)
        {
            years = 0;
            var m = TempleDeadlineRx.Match(" " + Clean082(text));
            if (!m.Success) return false;
            string n = m.Groups[1].Value;
            if (!int.TryParse(n, out years) && !SmallNums082.TryGetValue(n, out years)) return false;
            return years > 0 && years <= 200;
        }
        /// <summary>Removes "pra/para mim" and deadlines from a (folded) temple order before the place is read.</summary>
        public static string StripTempleNoise(string folded)
        {
            string s = " " + (folded ?? "");
            s = TempleDeadlineRx.Replace(s, " ");
            s = Regex.Replace(s, @"[, ]+(ou( entao)?|senao|se nao|caso contrario)\s+(sera|serao|vai|vais|voce|vc|tu|te|eu|todos|a cidade|morrera|sofrera)\b.*$", "", CI);   // "... ou será amaldiçoado" (the penalty)
            s = Regex.Replace(s, @"[, ]+(para mim|pra mim|em minha honra|em meu nome|para a minha gloria|para minha gloria)\b", " ", CI);
            s = Regex.Replace(s, @"\s+", " ");
            return s.Trim().TrimEnd(',').Trim();
        }

        // ---------------- F7: species kingdoms and "e pregue lá" ----------------
        static readonly Regex SpeciesKingdomRx = new Regex(@"^(?:o |a |ao |para o |pro |no |na )?(?:(?:reino|imperio|terra|terras|povo|nacao|pais|cidade|cidades|vila|aldeia)\s+)?(?:d[oa]s?\s+)?(elfico|elfica|elficos|elfos|elfo|elfa|elfas|orcs|orc|orquico|orquica|anao|anoes|ana|anas|anaos|humano|humanos|humana|humanas|homens)\b", CI);
        /// <summary>"reino élfico" / "reino dos elfos" / "dos orcs" / "anão" / "humano" -> "elf" / "orc" / "dwarf" / "human" (null when not a species place).</summary>
        public static string SpeciesKingdom(string target)
        {
            string f = Clean082(target).Trim('.', '!', '?', ',', ' ');
            var m = SpeciesKingdomRx.Match(f);
            if (!m.Success) return null;
            string rest = f.Substring(m.Length).Trim();
            if (rest.Length > 0 && !Regex.IsMatch(rest, @"^(mais proximo|mais proxima|mais perto|vizinho|vizinha)$", CI)) return null;
            string w = m.Groups[1].Value;
            if (w.StartsWith("elf")) return "elf";
            if (w.StartsWith("orc") || w.StartsWith("orq")) return "orc";
            if (w.StartsWith("an")) return "dwarf";
            return "human";
        }
        static readonly Regex ChainPreachRx = new Regex(@"[, ]+(?:e|e entao|e depois|depois|para|pra)\s+(?:la\s+|ali\s+)?(?:pregue|pregar|pregues|espalhe|espalhar|ensine|ensinar|anuncie|anunciar|difunda|difundir|converta|converter)\b.*$", CI);
        /// <summary>"o reino élfico e pregue lá a minha fé" -> "o reino élfico" + preach=true.</summary>
        public static string SplitChainedPreach(string target, out bool preach)
        {
            preach = false;
            if (string.IsNullOrEmpty(target)) return target;
            var m = ChainPreachRx.Match(Fold(target));
            if (!m.Success) return target;
            preach = true;
            return target.Substring(0, System.Math.Min(m.Index, target.Length)).Trim();
        }

        // ---------------- H: "onde está o templo?" ----------------
        /// <summary>A question about the temple (where it is, whether there is one): answered from the temple fact, never as a move.</summary>
        public static bool IsTempleQuestion(string text)
        {
            string f = Clean082(text);
            if (!Regex.IsMatch(f, @"\b(templo|templos|santuario|igreja)\b", CI)) return false;
            if (Regex.IsMatch(f, @"^(va|vai|ande|caminhe|siga|leve|me leve|construa|erga|levante)\b", CI)) return false;
            if (Regex.IsMatch(f, @"\b(onde|aonde|cade)\b[^.!]*\b(templo|santuario|igreja)\b", CI)) return true;
            if (f.EndsWith("?") && Regex.IsMatch(f, @"^(e |mas )?(existe|ha|tem|temos|ja tem|ja existe|ja ha|voces tem|vcs tem|tem algum|o templo|ja (construiram|ergueram|terminaram)|terminaram|ficou pronto|esta pronto|como esta o templo|como vai o templo)\b", CI)) return true;
            return false;
        }

        // ---------------- K: write_book ----------------
        static readonly Regex BookRx = new Regex(@"\b(escreva|escrevam|escreve|escrever|escrevas|redija|redigir|componha|compor|registre|registrar)\b(?:\s+[\p{L}-]+){0,4}?\s+(?:um|uma|o|a|meu|minha|nosso|nossa)?\s*(?:novo\s+|grande\s+)?(livro|livros|escritura|escrituras|evangelho|texto sagrado|cronica sagrada|tomo|biblia)\b", CI);
        /// <summary>"escreva um livro sagrado", "escreva um livro sobre mim".</summary>
        public static bool IsWriteBookRequest(string text)
        {
            string f = Clean082(text);
            if (f.Length == 0 || IsHypothetical(text) || IsFutureIntent(text)) return false;
            if (f.EndsWith("?") && !Regex.IsMatch(f, @"^(voce |vc |tu )?(pode|podes|poderia|consegue|quer|aceita)\b", CI)) return false;
            if (Regex.IsMatch(f, @"\b(nao|nunca|jamais)\s+(escreva|escrevam|escreve|escrever|redija)\b", CI)) return false;
            return BookRx.IsMatch(f);
        }

        /// <summary>A plain "yes" (to a question the NPC asked).</summary>
        public static bool IsPlainYes(string text)
        {
            string f = Clean082(text);
            if (f.Length == 0 || f.Length > 70) return false;
            if (Regex.IsMatch(f, @"\b(nao|nunca|jamais|depois|agora nao|ainda nao)\b", CI)) return false;
            return Regex.IsMatch(f, @"^(sim|quero|desejo|faca|facam|pode|claro|com certeza|certamente|isso|exato|por favor|seria bom|seria otimo|eu quero|eu desejo|que assim seja|assim seja|erga|ergam|construa|construam|escreva|escrevam|va em frente|vai em frente|ordeno|aceito|e o que desejo)\b", CI);
        }
        public static bool IsPlainNo(string text)
        {
            string f = Clean082(text);
            return f.Length > 0 && f.Length <= 70 && Regex.IsMatch(f, @"^(nao|agora nao|ainda nao|nunca|jamais|depois|mais tarde|nao quero|nao desejo|nao e preciso|nao precisa)\b", CI);
        }

        /// <summary>v0.8.2 (K): the NPC's reply ASKS God whether he wants a temple or a holy book ("temple"/"book"/null).</summary>
        public static string AskedOffer(string reply)
        {
            if (string.IsNullOrEmpty(reply) || reply.IndexOf('?') < 0) return null;
            foreach (var seg in Regex.Split(reply, @"(?<=[.!?…])\s+"))
            {
                if (!seg.TrimEnd().EndsWith("?")) continue;
                string f = Fold(seg);
                if (!Regex.IsMatch(f, @"\b(deseja|desejas|quer|queres|queira|gostaria|devo|devemos|posso|podemos|ordena|ordenas|permite|permites)\b", CI)) continue;
                if (Regex.IsMatch(f, @"\blivros?\b|\bescritura|\bescrev", CI)) return "book";
                if (Regex.IsMatch(f, @"\btemplos?\b|\bsantuario|\bcasa de oracao|\berg(a|amos|uer|uamos)\b", CI)) return "temple";
            }
            return null;
        }

        // ---------------- I1: the NPC speaks as God ----------------
        static readonly Regex AsGodRx = new Regex(@"(\*\s*(com\s+)?(uma\s+)?voz\s+(poderosa|divina|trovejante|celestial|celeste|de deus|dos ceus|retumbante|onipotente)|\bminha voz (ecoa|ressoa|soa|retumba)\b|\b(estou|estarei) (te|lhe|vos) (guiando|observando|protegendo)\b|\beu (te|lhe|vos) (guio|guiarei|criei|escolhi|abencoo|abencoei|perdoo)\b|\b(sou|eu sou) (o |a )?(teu|seu|vosso|vossa|tua|sua) (deus|criador|criadora|senhor|deusa)\b|\bfalo (do|dos|desde o) ceu\b|\bmeus (filhos|fieis|servos) (mortais|amados)\b)", CI);
        /// <summary>The reply speaks in God's voice ("*voz poderosa* Minha filha... Estou te guiando", "minha voz ecoa…"). Only for NPCs that are not God.</summary>
        public static bool SpeaksAsGod(string reply) => !string.IsNullOrEmpty(reply) && AsGodRx.IsMatch(Fold(reply));

        // ---------------- I2: third-person self-narration and the repeated vocative ----------------
        const string Verbs3 = @"(olha|sorri|acena|se aproxima|suspira|ri|franze|cruza|assente|balanca|inclina|abaixa|levanta|toca|responde|diz|pergunta|fala|pensa|sente|caminha|ajoelha|se ajoelha|observa|estende|ergue|fecha|abre|junta|une|treme|sussurra|murmura|chora|baixa|curva|se curva|sorriu|olhou|disse|respondeu|ajoelhou|esta|parece|fica|permanece|reza|ora|agradece|segura|aperta|coca|hesita|engole|respira|pausa|volta|vira|se vira|encara)";
        /// <summary>"*Sopanka olha para o céu*" / "Sopanka sorri." in Sopanka's own reply.</summary>
        public static bool NarratesSelf(string reply, string ownName)
        {
            if (string.IsNullOrEmpty(reply) || string.IsNullOrWhiteSpace(ownName) || ownName.Length < 2) return false;
            string f = Fold(reply), n = Regex.Escape(Fold(ownName).Split(' ')[0]);
            return Regex.IsMatch(f, @"(^|[.!?]\s*|\*\s*)" + n + @"\s+" + Verbs3 + @"\b", CI);
        }
        /// <summary>Fixes self-narration: "*Sopanka olha*" -> "*olha*"; a spoken sentence "Sopanka sorri." outside *…* is dropped.</summary>
        public static string FixSelfNarration(string reply, string ownName)
        {
            if (!NarratesSelf(reply, ownName)) return reply;
            string first = Regex.Escape(ownName.Split(' ')[0]);
            string r = Regex.Replace(reply, @"\*\s*" + first + @"\s+", "*", RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"(^|(?<=[.!?]\s))" + first + @"\s+[^.!?*]*[.!?]\s*", "", RegexOptions.IgnoreCase);
            return Regex.Replace(r, @"\s{2,}", " ").Trim();
        }
        static readonly Regex VocLeadRx = new Regex(@"^\s*((?:\*[^*]*\*\s*)?)((?:ó|oh|o)\s+)?((?:(?:meu|minha|nosso|nossa)\s+)?(?:\p{Lu}[\p{L}'’-]*|senhor|senhora|deus|pai|mestre|grande|santo|santa|divino|divina)(?:\s+(?:\p{Lu}[\p{L}'’-]*|senhor|senhora|de|do|da|dos|das|todo-poderoso|poderoso|divino|divina|bondoso|celestial)){0,3})\s*[,!]\s*", RegexOptions.CultureInvariant);
        /// <summary>The leading vocative of a reply ("Ó grande Kaiser, …" -> "grande kaiser"), or null.</summary>
        public static string LeadingVocative(string reply)
        {
            var m = VocLeadRx.Match(reply ?? "");
            if (!m.Success) return null;
            string v = Fold(m.Groups[3].Value).Trim();
            if (Regex.IsMatch(v, @"^(eu|sim|nao|bem|olha|veja|ah|oh|ola|claro|certo|obrigad[oa]|que|isso|hum|perdao|desculpe|entendo|perfeito|otimo)$", CI)) return null;
            return v;
        }
        /// <summary>Drops the leading vocative when the previous reply already opened with the same one.</summary>
        public static string StripRepeatedVocative(string reply, string previousReply)
        {
            string v = LeadingVocative(reply), pv = LeadingVocative(previousReply);
            if (v == null || pv == null || v != pv) return reply;
            var m = VocLeadRx.Match(reply);
            string rest = reply.Substring(m.Length).TrimStart();
            if (rest.Length < 3) return reply;
            rest = char.ToUpper(rest[0]) + rest.Substring(1);
            return (m.Groups[1].Value + rest).Trim();
        }

        // ---------------- I3: "unit" and anglicised verbs ----------------
        /// <summary>"a unit adulta" -> "a moradora adulta"; "units" -> "moradores".</summary>
        public static string FixUnitWord(string reply, bool female, out bool changed)
        {
            changed = false;
            if (string.IsNullOrEmpty(reply) || !Regex.IsMatch(reply, @"\bunits?\b", RegexOptions.IgnoreCase)) return reply;
            changed = true;
            string r = Regex.Replace(reply, @"\b(a|uma)\s+unit\b", m => m.Groups[1].Value + " moradora", RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"\b(o|um)\s+unit\b", m => m.Groups[1].Value + " morador", RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"\bunits\b", "moradores", RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"\bunit\b", female ? "moradora" : "morador", RegexOptions.IgnoreCase);
            return r;
        }
        static readonly Dictionary<string, string> AngloStems = new Dictionary<string, string>
        {
            {"spread","espalh"},{"check","verific"},{"shar","compartilh"},{"deliver","entreg"},{"help","ajud"},{"talk","convers"},{"fight","lut"},
            {"attack","atac"},{"pray","or"},{"preach","preg"},{"search","procur"},{"start","inici"},{"follow","acompanh"},{"kill","mat"},
            {"heal","cur"},{"bless","abenço"},{"train","trein"},{"farm","cultiv"},{"upgrad","melhor"},{"build","levant"},
            {"worship","ador"},{"hunt","caç"},{"teach","ensin"},
        };
        static readonly string[] AngloEnds = { "ar", "ando", "ado", "ada", "ados", "adas", "ei", "ou", "amos", "aram", "asse", "aria", "arei", "ara" };
        static readonly Regex AngloRx = new Regex(@"(?<![\p{L}])(" + string.Join("|", AngloStems.Keys.OrderByDescending(k => k.Length)) + @")(" + string.Join("|", AngloEnds.OrderByDescending(e => e.Length)) + @")(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        /// <summary>Anglicised verbs ("spreadar", "checkando") -> Portuguese ("espalhar", "verificando"); returns the words found.</summary>
        public static string FixAnglicisedVerbs(string reply, out List<string> found)
        {
            var hits = new List<string>(); found = hits;
            if (string.IsNullOrEmpty(reply)) return reply;
            return AngloRx.Replace(reply, m =>
            {
                string pt = AngloStems[m.Groups[1].Value.ToLowerInvariant()];
                hits.Add(m.Value);
                string w = pt + m.Groups[2].Value.ToLowerInvariant();
                return char.IsUpper(m.Value[0]) ? char.ToUpper(w[0]) + w.Substring(1) : w;
            });
        }

        // ---------------- I7: invented knowledge and numbers in prophet narration ----------------
        static readonly Regex InventedLoreRx = new Regex(@"\b(conhece|conheco|sabe|sei|guarda|guardo|conta|conto) (muitas|varias|inumeras|tantas) (historias|lendas|cancoes|cantigas|tradicoes)( antigas| do passado| dos antigos| de outrora)?\b", CI);
        public static bool InventsLore(string reply) => !string.IsNullOrEmpty(reply) && InventedLoreRx.IsMatch(Fold(reply));
        public static bool IsInventedLoreSentence(string seg) => InventsLore(seg);
        /// <summary>Numbers in a prophet narration line ("240 → 345 anos", "até o ano 5") -> plain words.</summary>
        public static string StripNarrationNumbers(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            string r = Regex.Replace(line, @"\(?\s*\d+\s*(→|->)\s*\d+\s*(anos?)?\s*\)?", "", RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"\b(até|ate) o ano \d+\b", "por um tempo", RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"\s*\b(no|do) ano \d+\b", "", RegexOptions.IgnoreCase);
            r = Regex.Replace(r, @"\s{2,}", " ").Replace(" .", ".").Replace(" ,", ",");
            return r.Trim();
        }
    }
}
